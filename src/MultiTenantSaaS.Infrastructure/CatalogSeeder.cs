using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiTenantSaaS.Application;
using Npgsql;
using OpenIddict.Abstractions;

namespace MultiTenantSaaS.Infrastructure;

public sealed class CatalogSeedHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<CatalogSeedHostedService> logger) : BackgroundService
{
    private static readonly string[] DefaultRoles = ["SuperAdmin", "TenantAdmin", "TenantUser", "ReadOnly"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Seed:Enabled"))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<CatalogUser>>();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        var tenantProvisioning = scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>();

        await SeedRolesAsync(roleManager);
        await SeedScopeAsync(scopeManager);
        await SeedApplicationAsync(applicationManager);
        var demoUser = await SeedDemoUserAsync(userManager, configuration, stoppingToken);
        await SeedDemoTenantsAsync(tenantProvisioning, demoUser.Id, configuration, stoppingToken);

        logger.LogInformation("Catalog identity seed completed");
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole<Guid>> roleManager)
    {
        foreach (var roleName in DefaultRoles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                ThrowIfFailed(result, $"creating role '{roleName}'");
            }
        }
    }

    private static async Task SeedScopeAsync(IOpenIddictScopeManager scopeManager)
    {
        var scopeNames = new[]
        {
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            "saas-api"
        };

        foreach (var scopeName in scopeNames)
        {
            if (await scopeManager.FindByNameAsync(scopeName) is not null)
            {
                continue;
            }

            var descriptor = new OpenIddictScopeDescriptor
            {
                Name = scopeName,
                DisplayName = scopeName == "saas-api" ? "Northstar API" : scopeName
            };

            if (scopeName == "saas-api")
            {
                descriptor.Resources.Add("saas-api");
            }

            await scopeManager.CreateAsync(descriptor);
        }
    }

    private static async Task SeedApplicationAsync(IOpenIddictApplicationManager applicationManager)
    {
        if (await applicationManager.FindByClientIdAsync("northstar-spa") is not null)
        {
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = "northstar-spa",
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Explicit,
            DisplayName = "Northstar React SPA"
        };
        descriptor.RedirectUris.Add(new Uri("http://localhost:3000/auth/callback"));
        descriptor.PostLogoutRedirectUris.Add(new Uri("http://localhost:3000/"));
        descriptor.Permissions.UnionWith([
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
            OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
            OpenIddictConstants.Permissions.ResponseTypes.Code,
            OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Permissions.Prefixes.Scope + "saas-api"
        ]);
        await applicationManager.CreateAsync(descriptor);
    }

    private static async Task<CatalogUser> SeedDemoUserAsync(
        UserManager<CatalogUser> userManager,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var password = configuration["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Seed:DemoPassword is required when Seed:Enabled is true.");
        }

        const string email = "demo@northstar.local";
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new CatalogUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Demo Administrator"
            };
            var result = await userManager.CreateAsync(user, password);
            ThrowIfFailed(result, "creating the demo user");
        }

        if (!await userManager.IsInRoleAsync(user, "SuperAdmin"))
        {
            var result = await userManager.AddToRoleAsync(user, "SuperAdmin");
            ThrowIfFailed(result, "assigning the demo role");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return user;
    }

    private static async Task SeedDemoTenantsAsync(
        ITenantProvisioningService tenantProvisioning,
        Guid userId,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Catalog")
            ?? throw new InvalidOperationException("ConnectionStrings:Catalog is required for demo tenant seeding.");

        await SeedTenantAsync(tenantProvisioning, userId, connectionString, "acme-corp", "Acme Corporation", "acme.localhost", "TenantAdmin", cancellationToken);
        await SeedTenantAsync(tenantProvisioning, userId, connectionString, "orbit-labs", "Orbit Labs", "orbit.localhost", "TenantAdmin", cancellationToken);
    }

    private static async Task SeedTenantAsync(
        ITenantProvisioningService tenantProvisioning,
        Guid userId,
        string connectionString,
        string slug,
        string name,
        string domain,
        string role,
        CancellationToken cancellationToken)
    {
        var tenant = await tenantProvisioning.ProvisionAsync(slug, name, domain, cancellationToken);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var schema = new NpgsqlCommandBuilder().QuoteIdentifier(tenant.SchemaName);
        command.CommandText = $"""
            INSERT INTO {schema}."AppUsers" ("Id", "Email", "DisplayName")
            SELECT @userId, 'demo@northstar.local', 'Demo Administrator'
            WHERE NOT EXISTS (SELECT 1 FROM {schema}."AppUsers" WHERE "Id" = @userId);
            INSERT INTO {schema}."Memberships" ("Id", "UserId", "Role", "TenantId")
            SELECT gen_random_uuid(), @userId, @role, @tenantId
            WHERE NOT EXISTS (SELECT 1 FROM {schema}."Memberships" WHERE "UserId" = @userId AND "TenantId" = @tenantId);
            """;
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("tenantId", tenant.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ThrowIfFailed(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(", ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Failed while {operation}: {errors}");
    }
}
