using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using MultiTenantSaaS.Application;

namespace MultiTenantSaaS.Infrastructure;

public sealed class TenantResolver(CatalogDbContext catalog, ITenantContext tenantContext) : ITenantResolver
{
    public async Task<TenantResolution?> ResolveAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var slug = httpContext.Request.Headers["X-Tenant"].FirstOrDefault()
            ?? httpContext.User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var tenant = Guid.TryParse(slug, out var tenantId)
            ? await catalog.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken)
            : await catalog.Tenants.SingleOrDefaultAsync(item => item.Slug == slug, cancellationToken);

        if (tenant is null || tenant.ProvisioningStatus != MultiTenantSaaS.Domain.TenantProvisioningStatus.Provisioned)
        {
            return null;
        }

        var resolution = new TenantResolution(tenant.Id, tenant.Slug, tenant.SchemaName);
        ((TenantContext)tenantContext).Set(resolution);
        return resolution;
    }
}