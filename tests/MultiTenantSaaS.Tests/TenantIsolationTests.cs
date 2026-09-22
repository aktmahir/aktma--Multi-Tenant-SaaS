using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using MultiTenantSaaS.Application;
using MultiTenantSaaS.Infrastructure;
using Testcontainers.PostgreSql;

namespace MultiTenantSaaS.Tests;

public sealed class TenantIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE SCHEMA tenant_a;
            CREATE SCHEMA tenant_b;
            CREATE TABLE tenant_a."Memberships" ("Id" uuid PRIMARY KEY, "UserId" uuid NOT NULL, "Role" text NOT NULL, "TenantId" uuid NOT NULL);
            CREATE TABLE tenant_b."Memberships" ("Id" uuid PRIMARY KEY, "UserId" uuid NOT NULL, "Role" text NOT NULL, "TenantId" uuid NOT NULL);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task TenantA_CannotReadTenantBMemberships()
    {
        var tenantAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var userAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
        var userBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
        await InsertMembershipAsync("tenant_a", userAId, tenantAId, "TenantAdmin");
        await InsertMembershipAsync("tenant_b", userBId, tenantBId, "ReadOnly");

        var tenantAContext = CreateTenantContext(tenantAId, "tenant_a");
        await using (var tenantADb = CreateTenantDbContext(tenantAContext))
        {
            var memberships = await tenantADb.Memberships.ToListAsync();

            // This is the critical isolation assertion: Tenant A's EF context is mapped
            // to tenant_a and must never return the row stored in tenant_b.
            var membership = Assert.Single(memberships);
            Assert.Equal(userAId, membership.UserId);
            Assert.Equal("TenantAdmin", membership.Role);
            Assert.DoesNotContain(memberships, item => item.UserId == userBId);
        }

        var tenantBContext = CreateTenantContext(tenantBId, "tenant_b");
        await using var tenantBDb = CreateTenantDbContext(tenantBContext);
        var tenantBMemberships = await tenantBDb.Memberships.ToListAsync();
        var tenantBMembership = Assert.Single(tenantBMemberships);
        Assert.Equal(userBId, tenantBMembership.UserId);
        Assert.Equal("ReadOnly", tenantBMembership.Role);
    }

    private async Task InsertMembershipAsync(string schema, Guid userId, Guid tenantId, string role)
    {
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO \"{schema}\".\"Memberships\" (\"Id\", \"UserId\", \"Role\", \"TenantId\") VALUES (@id, @userId, @role, @tenantId)";
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("tenantId", tenantId);
        await command.ExecuteNonQueryAsync();
    }

    private static TenantContext CreateTenantContext(Guid tenantId, string schema)
    {
        var context = new TenantContext();
        context.Set(new TenantResolution(tenantId, schema, schema));
        return context;
    }

    private TenantDbContext CreateTenantDbContext(TenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>()
            .Options;
        return new TenantDbContext(options, tenantContext);
    }
}
