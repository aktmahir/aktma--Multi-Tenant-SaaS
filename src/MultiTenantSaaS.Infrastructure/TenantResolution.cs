using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using MultiTenantSaaS.Application;

namespace MultiTenantSaaS.Infrastructure;

public sealed class TenantResolver(CatalogDbContext catalog, ITenantContext tenantContext) : ITenantResolver
{
    public async Task<TenantResolution?> ResolveAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var requestedTenant = httpContext.Request.Headers["X-Tenant"].FirstOrDefault()
            ?? httpContext.Request.Query["tenant"].FirstOrDefault()
            ?? httpContext.User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrWhiteSpace(requestedTenant))
        {
            return null;
        }

        var tenant = Guid.TryParse(requestedTenant, out var tenantId)
            ? await catalog.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken)
            : await catalog.Tenants.SingleOrDefaultAsync(item => item.Slug == requestedTenant, cancellationToken);

        if (tenant is null || tenant.ProvisioningStatus != MultiTenantSaaS.Domain.TenantProvisioningStatus.Provisioned)
        {
            return null;
        }

        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var claimTenantId = httpContext.User.GetTenantId();
            if (claimTenantId is Guid tokenTenantId && tokenTenantId != tenant.Id)
            {
                return null;
            }
        }

        var resolution = new TenantResolution(tenant.Id, tenant.Slug, tenant.SchemaName);
        ((TenantContext)tenantContext).Set(resolution);
        return resolution;
    }
}