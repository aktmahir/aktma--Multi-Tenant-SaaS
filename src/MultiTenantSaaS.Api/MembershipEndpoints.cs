using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MultiTenantSaaS.Application;
using MultiTenantSaaS.Domain;
using MultiTenantSaaS.Infrastructure;

namespace MultiTenantSaaS.Api;

public static class MembershipEndpoints
{
    public static IEndpointRouteBuilder MapMembershipEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/memberships/me", async (
            ClaimsPrincipal principal,
            ITenantContext tenantContext,
            ITenantMembershipService membershipService,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.GetUserId();
            if (userId is null || tenantContext.TenantId is null)
            {
                return Results.Forbid();
            }

            var role = await membershipService.GetRoleAsync(userId.Value, tenantContext.TenantId.Value, cancellationToken);
            return role is null
                ? Results.NotFound(new { error = "Membership was not found for this tenant." })
                : Results.Ok(new MembershipResponse(userId.Value, role));
        })
        .RequireAuthorization()
        .WithName("GetCurrentMembership");

        endpoints.MapPost("/api/memberships", async (
            CreateMembershipRequest request,
            ClaimsPrincipal principal,
            ITenantContext tenantContext,
            TenantDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (tenantContext.TenantId is null || principal.GetUserId() is null)
            {
                return Results.Forbid();
            }

            if (request.UserId == Guid.Empty || !TenantRoles.IsSupported(request.Role))
            {
                return Results.BadRequest(new { error = "UserId and a supported role are required." });
            }

            var exists = await dbContext.Memberships.AnyAsync(
                membership => membership.UserId == request.UserId,
                cancellationToken);
            if (exists)
            {
                return Results.Conflict(new { error = "The user already has a membership in this tenant." });
            }

            var membership = new MultiTenantSaaS.Domain.TenantMembership
            {
                UserId = request.UserId,
                TenantId = tenantContext.TenantId.Value,
                Role = request.Role
            };
            dbContext.Memberships.Add(membership);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/memberships/{membership.Id}", new MembershipResponse(membership.UserId, membership.Role));
        })
        .RequireAuthorization(Policies.CanManageUsers)
        .WithName("CreateMembership");

        endpoints.MapPut("/api/memberships/{userId:guid}/role", async (
            Guid userId,
            UpdateMembershipRoleRequest request,
            ClaimsPrincipal principal,
            ITenantContext tenantContext,
            TenantDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (tenantContext.TenantId is null || principal.GetUserId() is null)
            {
                return Results.Forbid();
            }

            if (!TenantRoles.IsSupported(request.Role))
            {
                return Results.BadRequest(new { error = "A supported role is required." });
            }

            var membership = await dbContext.Memberships
                .SingleOrDefaultAsync(item => item.UserId == userId && item.TenantId == tenantContext.TenantId.Value, cancellationToken);

            if (membership is null)
            {
                return Results.NotFound(new { error = "Membership was not found for this tenant." });
            }

            var previousRole = membership.Role;
            membership.Role = request.Role;
            dbContext.AuditEntries.Add(new AuditEntry
            {
                ActorId = principal.GetUserId(),
                TenantId = tenantContext.TenantId.Value,
                Action = "membership.updated",
                Resource = membership.Id.ToString(),
                IpAddress = null
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new MembershipResponse(membership.UserId, membership.Role) { });
        })
        .RequireAuthorization(Policies.CanManageUsers)
        .WithName("UpdateMembershipRole");

        endpoints.MapDelete("/api/memberships/{userId:guid}", async (
            Guid userId,
            ClaimsPrincipal principal,
            ITenantContext tenantContext,
            TenantDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (tenantContext.TenantId is null || principal.GetUserId() is null)
            {
                return Results.Forbid();
            }

            var currentUserId = principal.GetUserId();
            if (currentUserId == userId)
            {
                return Results.BadRequest(new { error = "You cannot remove your own membership from the tenant." });
            }

            var membership = await dbContext.Memberships
                .SingleOrDefaultAsync(item => item.UserId == userId && item.TenantId == tenantContext.TenantId.Value, cancellationToken);

            if (membership is null)
            {
                return Results.NotFound(new { error = "Membership was not found for this tenant." });
            }

            dbContext.Memberships.Remove(membership);
            dbContext.AuditEntries.Add(new AuditEntry
            {
                ActorId = principal.GetUserId(),
                TenantId = tenantContext.TenantId.Value,
                Action = "membership.removed",
                Resource = membership.Id.ToString(),
                IpAddress = null
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanManageUsers)
        .WithName("RemoveMembership");

        return endpoints;
    }
}

public sealed record CreateMembershipRequest(Guid UserId, string Role);
public sealed record UpdateMembershipRoleRequest(string Role);
public sealed record MembershipResponse(Guid UserId, string Role);
