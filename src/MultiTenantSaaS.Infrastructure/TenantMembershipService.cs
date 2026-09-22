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
}