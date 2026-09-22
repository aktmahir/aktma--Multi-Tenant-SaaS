using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using MultiTenantSaaS.Domain;

namespace MultiTenantSaaS.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : IdentityDbContext<CatalogUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseOpenIddict();
        modelBuilder.Entity<Tenant>().HasIndex(tenant => tenant.Slug).IsUnique();
        modelBuilder.Entity<Tenant>().Property(tenant => tenant.Slug).HasMaxLength(64);
        modelBuilder.Entity<Tenant>().Property(tenant => tenant.Name).HasMaxLength(160);
        modelBuilder.Entity<Tenant>().Property(tenant => tenant.BrandingColor).HasMaxLength(7);
    }
}