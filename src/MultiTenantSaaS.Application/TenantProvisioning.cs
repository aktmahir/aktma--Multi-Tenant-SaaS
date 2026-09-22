using MultiTenantSaaS.Domain;

namespace MultiTenantSaaS.Application;

public interface ITenantProvisioningService
{
    Task<Tenant> ProvisionAsync(string slug, string name, string? domain, CancellationToken cancellationToken);
}