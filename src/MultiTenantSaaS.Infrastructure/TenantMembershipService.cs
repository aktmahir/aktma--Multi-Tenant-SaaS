using Microsoft.EntityFrameworkCore;
using MultiTenantSaaS.Application;

namespace MultiTenantSaaS.Infrastructure;

public sealed class TenantMembershipService(TenantDbContext dbContext, ITenantContext tenantContext) : ITenantMembershipService
{
    public Task<string?> GetRoleAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
        tenantContext.TenantId is not null && tenantContext.TenantId.Value == tenantId
            ? dbContext.Memberships
                .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
                .Select(membership => membership.Role)
                .SingleOrDefaultAsync(cancellationToken)
            : Task.FromResult<string?>(null);

    public async Task<string?> UpdateRoleAsync(Guid userId, Guid tenantId, string role, CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is null || tenantContext.TenantId.Value != tenantId || !TenantRoles.IsSupported(role))
        {
            return null;
        }

        var membership = await dbContext.Memberships
            .SingleOrDefaultAsync(item => item.UserId == userId && item.TenantId == tenantId, cancellationToken);

        if (membership is null)
        {
            return null;
        }

        membership.Role = role;
        await dbContext.SaveChangesAsync(cancellationToken);
        return membership.Role;
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is null || tenantContext.TenantId.Value != tenantId)
        {
            return false;
        }

        var membership = await dbContext.Memberships
            .SingleOrDefaultAsync(item => item.UserId == userId && item.TenantId == tenantId, cancellationToken);

        if (membership is null)
        {
            return false;
        }

        dbContext.Memberships.Remove(membership);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}