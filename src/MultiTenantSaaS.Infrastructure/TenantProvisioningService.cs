using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MultiTenantSaaS.Application;
using MultiTenantSaaS.Domain;
using Npgsql;

namespace MultiTenantSaaS.Infrastructure;

public sealed class TenantProvisioningService(CatalogDbContext catalog, IConfiguration configuration) : ITenantProvisioningService
{
    private static readonly Regex SlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    public async Task<Tenant> ProvisionAsync(string slug, string name, string? domain, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug) || !SlugPattern.IsMatch(slug) || slug.Length > 64)
        {
            throw new ArgumentException("Tenant slug must use lowercase letters, numbers, and hyphens.", nameof(slug));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
        {
            throw new ArgumentException("Tenant name is required and must be at most 160 characters.", nameof(name));
        }

        var existing = await catalog.Tenants.SingleOrDefaultAsync(tenant => tenant.Slug == slug, cancellationToken);
        if (existing?.ProvisioningStatus == TenantProvisioningStatus.Provisioned)
        {
            return existing;
        }

        var tenant = existing ?? new Tenant { Slug = slug, Name = name, Domain = domain };
        tenant.Name = name;
        tenant.Domain = domain;
        tenant.ProvisioningStatus = TenantProvisioningStatus.Pending;
        if (existing is null)
        {
            catalog.Tenants.Add(tenant);
        }

        await catalog.SaveChangesAsync(cancellationToken);

        try
        {
            await CreateTenantSchemaAsync(tenant.SchemaName, cancellationToken);
            tenant.ProvisioningStatus = TenantProvisioningStatus.Provisioned;
            await catalog.SaveChangesAsync(cancellationToken);
            return tenant;
        }
        catch
        {
            tenant.ProvisioningStatus = TenantProvisioningStatus.Failed;
            await catalog.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task CreateTenantSchemaAsync(string schemaName, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Catalog")
            ?? throw new InvalidOperationException("ConnectionStrings:Catalog is required for tenant provisioning.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var identifier = new NpgsqlCommandBuilder().QuoteIdentifier(schemaName);
        command.CommandText = $"""
            CREATE SCHEMA IF NOT EXISTS {identifier};
            CREATE TABLE IF NOT EXISTS {identifier}."AppUsers" (
                "Id" uuid PRIMARY KEY,
                "Email" varchar(320) NOT NULL,
                "DisplayName" varchar(160) NOT NULL,
                "IsActive" boolean NOT NULL DEFAULT true
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_{schemaName}_AppUsers_Email" ON {identifier}."AppUsers" ("Email");
            CREATE TABLE IF NOT EXISTS {identifier}."Memberships" (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL,
                "Role" varchar(64) NOT NULL,
                "TenantId" uuid NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_{schemaName}_Memberships_UserId" ON {identifier}."Memberships" ("UserId");
            CREATE TABLE IF NOT EXISTS {identifier}."AuditEntries" (
                "Id" uuid PRIMARY KEY,
                "OccurredAt" timestamptz NOT NULL,
                "ActorId" uuid,
                "Action" varchar(120) NOT NULL,
                "Resource" varchar(160) NOT NULL,
                "IpAddress" varchar(64),
                "TenantId" uuid NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_{schemaName}_AuditEntries_TenantId" ON {identifier}."AuditEntries" ("TenantId");
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}