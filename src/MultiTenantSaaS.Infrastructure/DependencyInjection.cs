using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MultiTenantSaaS.Application;
using Serilog;

namespace MultiTenantSaaS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Catalog")
            ?? "Host=localhost;Port=5432;Database=saas_catalog;Username=saas;Password=saas";

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantResolver, TenantResolver>();
        services.AddDbContext<CatalogDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContext<TenantDbContext>(options => options
            .UseNpgsql(connectionString)
            .ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>());
        services.AddSerilog((_, logger) => logger.Enrich.FromLogContext().WriteTo.Console());
        return services;
    }
}