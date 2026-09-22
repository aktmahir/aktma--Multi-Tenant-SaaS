using Microsoft.AspNetCore.Identity;

namespace MultiTenantSaaS.Infrastructure;

public sealed class CatalogUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
}