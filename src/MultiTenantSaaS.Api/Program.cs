using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using MultiTenantSaaS.Api;
using MultiTenantSaaS.Application;
using MultiTenantSaaS.Infrastructure;
using Serilog;
using System.Threading.RateLimiting;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);
var authority = builder.Configuration["Authentication:Authority"]
    ?? (builder.Environment.IsDevelopment() ? "http://localhost:8080" : throw new InvalidOperationException("Authentication:Authority is required outside development."));
var audience = builder.Configuration["Authentication:Audience"] ?? "saas-api";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000"];

builder.Host.UseSerilog((_, logger) => logger.Enrich.FromLogContext().WriteTo.Console());
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<ConsentStateProtector>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddIdentityCore<CatalogUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>()
    .AddEntityFrameworkStores<CatalogDbContext>()
    .AddSignInManager();
builder.Services.AddOpenIddict()
    .AddCore(options => options
        .UseEntityFrameworkCore()
        .UseDbContext<CatalogDbContext>())
    .AddServer(options =>
    {
        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            "saas-api");

        options.SetAuthorizationEndpointUris("/connect/authorize")
            .SetTokenEndpointUris("/connect/token");
        options.AllowAuthorizationCodeFlow()
            .RequireProofKeyForCodeExchange()
            .AllowRefreshTokenFlow();
        options.DisableAccessTokenEncryption();

        if (builder.Environment.IsDevelopment())
        {
            options.AddEphemeralSigningKey()
                .AddEphemeralEncryptionKey();
        }
        else
        {
            var signingCertificatePath = builder.Configuration["Authentication:SigningCertificatePath"]
                ?? throw new InvalidOperationException("Authentication:SigningCertificatePath is required outside development.");
            var signingCertificatePassword = builder.Configuration["Authentication:SigningCertificatePassword"];
            var signingCertificate = X509CertificateLoader.LoadPkcs12FromFile(signingCertificatePath, signingCertificatePassword);
            options.AddSigningCertificate(signingCertificate)
                .AddEncryptionCertificate(signingCertificate);
        }

        var aspNetCoreBuilder = options.UseAspNetCore();
        if (builder.Environment.IsDevelopment())
        {
            aspNetCoreBuilder.DisableTransportSecurityRequirement();
        }

        aspNetCoreBuilder
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough();
    });
builder.Services.AddCors(options => options.AddPolicy("spa", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));
builder.Services.AddRateLimiter(options => options.AddPolicy("auth", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        })));
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.CanManageUsers, policy => policy.RequireRole("SuperAdmin", "TenantAdmin"))
    .AddPolicy(Policies.CanManageBilling, policy => policy.RequireRole("SuperAdmin", "TenantAdmin"));
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(authority),
            ValidIssuer = authority,
            ValidateAudience = !string.IsNullOrWhiteSpace(audience),
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Northstar Multi-Tenant SaaS API");
        options.RoutePrefix = "swagger";
    });
}

app.UseExceptionHandler();
app.UseCors("spa");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "api" }));
app.MapHealthChecks("/api/health/ready");
app.MapGet("/api/session", (HttpContext context) =>
{
    var principal = context.User;
    return principal.Identity?.IsAuthenticated == true
        ? Results.Ok(new { authenticated = true, email = principal.Identity.Name })
        : Results.Unauthorized();
})
    .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = IdentityConstants.ApplicationScheme });
app.MapGet("/api/tenant-context", (ITenantContext tenant) => Results.Ok(new { tenant.TenantId, tenant.Slug }))
    .RequireAuthorization();
app.MapIdentityEndpoints();
app.MapMembershipEndpoints();
app.MapTenantEndpoints();
app.MapUserEndpoints();
app.MapAuditEndpoints();

app.Run();

public partial class Program;
