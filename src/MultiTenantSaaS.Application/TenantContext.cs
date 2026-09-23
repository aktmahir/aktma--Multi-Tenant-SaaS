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

public interface ITenantMembershipService
{
    Task<string?> GetRoleAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
    Task<string?> UpdateRoleAsync(Guid userId, Guid tenantId, string role, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
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
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    public static Guid? GetTenantId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("tenant_id");
        return Guid.TryParse(value, out var tenantId) ? tenantId : null;
    }
}

public static class TenantClaimValidator
{
    public static bool Matches(ClaimsPrincipal principal, Guid tenantId) =>
        principal.GetTenantId() is { } claimTenantId && claimTenantId == tenantId;

    public static bool IsAuthorizedForTenant(ClaimsPrincipal principal, Guid tenantId, string? membershipRole = null)
    {
        if (!Matches(principal, tenantId))
        {
            return false;
        }

        if (principal.IsInRole(TenantRoles.SuperAdmin))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(membershipRole))
        {
            return false;
        }

        return principal.IsInRole(membershipRole);
    }
}

public static class TenantTokenClaims
{
    public static bool TryCreate(Guid tenantId, string? role, out string tenantIdValue, out string roleValue)
    {
        tenantIdValue = string.Empty;
        roleValue = string.Empty;

        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        tenantIdValue = tenantId.ToString();
        roleValue = role;
        return true;
    }
}

public sealed record ApiError(string Error, string Detail);