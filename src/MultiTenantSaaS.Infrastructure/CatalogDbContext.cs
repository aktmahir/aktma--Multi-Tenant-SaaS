using Microsoft.EntityFrameworkCore;
using MultiTenantSaaS.Domain;

namespace MultiTenantSaaS.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>().HasIndex(tenant => tenant.Slug).IsUnique();
        modelBuilder.Entity<Tenant>().Property(tenant => tenant.Slug).HasMaxLength(64);
        modelBuilder.Entity<Tenant>().Property(tenant => tenant.Name).HasMaxLength(160);
    }
}