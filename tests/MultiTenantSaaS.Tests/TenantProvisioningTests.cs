using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MultiTenantSaaS.Domain;
using MultiTenantSaaS.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MultiTenantSaaS.Tests;

public sealed class TenantProvisioningTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();
    private CatalogDbContext catalog = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        catalog = new CatalogDbContext(options);
        await catalog.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await catalog.DisposeAsync();
        await postgres.DisposeAsync();
    }

    [Fact]
    public async Task Provisioning_CreatesTenantSchemaAndIsIdempotent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Catalog"] = postgres.GetConnectionString()
            })
            .Build();
        var service = new TenantProvisioningService(catalog, configuration);

        var first = await service.ProvisionAsync("acme-test", "Acme Test", null, CancellationToken.None);
        var second = await service.ProvisionAsync("acme-test", "Acme Test", null, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(TenantProvisioningStatus.Provisioned, second.ProvisioningStatus);

        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @schema AND table_name IN ('AppUsers', 'Memberships', 'AuditEntries')";
        command.Parameters.AddWithValue("schema", first.SchemaName);
        var tableCount = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.Equal(3, tableCount);
    }

    [Theory]
    [InlineData("Acme")]
    [InlineData("acme_test")]
    [InlineData("acme test")]
    public async Task Provisioning_RejectsUnsafeSlugs(string slug)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Catalog"] = postgres.GetConnectionString()
            })
            .Build();
        var service = new TenantProvisioningService(catalog, configuration);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ProvisionAsync(slug, "Invalid", null, CancellationToken.None));
    }
}
