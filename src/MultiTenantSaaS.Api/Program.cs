using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using MultiTenantSaaS.Api;
using MultiTenantSaaS.Application;
using MultiTenantSaaS.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((_, logger) => logger.Enrich.FromLogContext().WriteTo.Console());
builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.CanManageUsers, policy => policy.RequireRole("SuperAdmin", "TenantAdmin"))
    .AddPolicy(Policies.CanManageBilling, policy => policy.RequireRole("SuperAdmin", "TenantAdmin"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.Authority = builder.Configuration["Authentication:Authority"]);
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "api" }));
app.MapGet("/api/tenant-context", (ITenantContext tenant) => Results.Ok(new { tenant.TenantId, tenant.Slug }))
    .RequireAuthorization();

app.Run();

public partial class Program;
