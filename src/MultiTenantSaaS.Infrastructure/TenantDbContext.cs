using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MultiTenantSaaS.Application;
using MultiTenantSaaS.Domain;

namespace MultiTenantSaaS.Infrastructure;

public sealed class TenantDbContext(
    DbContextOptions<TenantDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(tenantContext.SchemaName ?? "public");
        modelBuilder.Entity<AppUser>().HasQueryFilter(user => tenantContext.TenantId.HasValue);
        modelBuilder.Entity<TenantMembership>().HasQueryFilter(membership => membership.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<AuditEntry>().HasQueryFilter(entry => entry.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<AppUser>().HasIndex(user => user.Email).IsUnique();
    }
}

public sealed class TenantModelCacheKeyFactory(ITenantContext resolvedContext) : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is TenantDbContext tenantDbContext
            ? (context.GetType(), tenantDbContext.Database.GetDbConnection().Database, resolvedContext.SchemaName, designTime)
            : (context.GetType(), designTime);
}