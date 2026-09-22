using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MultiTenantSaaS.Infrastructure;

public sealed class CatalogMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<CatalogMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Database:ApplyMigrations"))
        {
            return;
        }

        if (!environment.IsDevelopment() && !configuration.GetValue<bool>("Database:AllowProductionMigrations"))
        {
            throw new InvalidOperationException("Production migrations require an explicit Database:AllowProductionMigrations opt-in.");
        }

        using var scope = scopeFactory.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await catalog.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Catalog database migrations applied");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
