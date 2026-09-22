using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MultiTenantSaaS.Infrastructure;
using MultiTenantSaaS.Application;

namespace MultiTenantSaaS.Api;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenant-settings", async (
            ITenantContext tenantContext,
            CatalogDbContext catalog,
            CancellationToken cancellationToken) =>
        {
            if (tenantContext.TenantId is null)
            {
                return Results.Forbid();
            }

            var tenant = await catalog.Tenants
                .SingleOrDefaultAsync(item => item.Id == tenantContext.TenantId, cancellationToken);
            return tenant is null
                ? Results.NotFound()
                : Results.Ok(new TenantSettingsResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.Domain, tenant.BrandingColor));
        })
        .RequireAuthorization()
        .WithName("GetTenantSettings");

        endpoints.MapPut("/api/tenant-settings", async (
            UpdateTenantSettingsRequest request,
            ITenantContext tenantContext,
            CatalogDbContext catalog,
            CancellationToken cancellationToken) =>
        {
            if (tenantContext.TenantId is null)
            {
                return Results.BadRequest(new ApiError("Invalid tenant settings", "A tenant context is required."));
            }

            if (!IsHexColor(request.BrandingColor))
            {
                return Results.BadRequest(new ApiError("Invalid tenant settings", "Branding color must use a valid hex format."));
            }

            var tenant = await catalog.Tenants
                .SingleOrDefaultAsync(item => item.Id == tenantContext.TenantId, cancellationToken);
            if (tenant is null)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 160)
            {
                return Results.BadRequest(new ApiError("Invalid tenant settings", "Tenant name is required and must be at most 160 characters."));
            }

            tenant.Name = request.Name.Trim();
            tenant.Domain = string.IsNullOrWhiteSpace(request.Domain) ? null : request.Domain.Trim();
            tenant.BrandingColor = request.BrandingColor.ToUpperInvariant();
            await catalog.SaveChangesAsync(cancellationToken);
            return Results.Ok(new TenantSettingsResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.Domain, tenant.BrandingColor));
        })
        .RequireAuthorization(policy => policy.RequireRole(TenantRoles.SuperAdmin, TenantRoles.TenantAdmin))
        .WithName("UpdateTenantSettings");

        endpoints.MapGet("/api/tenants", async (
            CatalogDbContext catalog,
            CancellationToken cancellationToken) =>
        {
            var tenants = await catalog.Tenants
                .OrderBy(tenant => tenant.Name)
                .Select(tenant => new TenantSummaryResponse(
                    tenant.Id,
                    tenant.Slug,
                    tenant.Name,
                    tenant.Domain ?? string.Empty,
                    tenant.BrandingColor,
                    tenant.ProvisioningStatus.ToString()))
                .ToListAsync(cancellationToken);

            return Results.Ok(tenants);
        })
        .RequireAuthorization(policy => policy.RequireRole(TenantRoles.SuperAdmin, TenantRoles.TenantAdmin))
        .WithName("ListTenants");

        endpoints.MapPost("/api/tenants", async (
            CreateTenantRequest request,
            ITenantProvisioningService provisioningService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var tenant = await provisioningService.ProvisionAsync(
                    request.Slug,
                    request.Name,
                    request.Domain,
                    cancellationToken);

                return Results.Created($"/api/tenants/{tenant.Id}", new TenantResponse(
                    tenant.Id,
                    tenant.Slug,
                    tenant.Name,
                    tenant.ProvisioningStatus.ToString()));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new ApiError("Tenant provisioning failed", exception.Message));
            }
        })
        .RequireAuthorization(policy => policy.RequireRole(TenantRoles.SuperAdmin))
        .WithName("ProvisionTenant");

        return endpoints;
    }

    private static bool IsHexColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}

public sealed record CreateTenantRequest(string Slug, string Name, string? Domain);
public sealed record TenantResponse(Guid Id, string Slug, string Name, string ProvisioningStatus);
public sealed record TenantSummaryResponse(Guid Id, string Slug, string Name, string Domain, string BrandingColor, string ProvisioningStatus);
public sealed record UpdateTenantSettingsRequest(string Name, string? Domain, string BrandingColor);
public sealed record TenantSettingsResponse(Guid Id, string Name, string Slug, string? Domain, string BrandingColor);