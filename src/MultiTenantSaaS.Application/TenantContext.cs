using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace MultiTenantSaaS.Application;

public interface ITenantContext
{
    Guid? TenantId { get; }
    string? Slug { get; }
    string? SchemaName { get; }
    bool IsResolved { get; }
}

public interface ITenantResolver
{
    Task<TenantResolution?> ResolveAsync(HttpContext httpContext, CancellationToken cancellationToken);
}

public sealed record TenantResolution(Guid TenantId, string Slug, string SchemaName);

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public string? Slug { get; private set; }
    public string? SchemaName { get; private set; }
    public bool IsResolved => TenantId.HasValue;

    public void Set(TenantResolution resolution)
    {
        TenantId = resolution.TenantId;
        Slug = resolution.Slug;
        SchemaName = resolution.SchemaName;
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetTenantId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("tenant_id");
        return Guid.TryParse(value, out var tenantId) ? tenantId : null;
    }
}