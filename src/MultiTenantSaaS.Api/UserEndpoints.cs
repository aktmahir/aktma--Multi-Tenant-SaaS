using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MultiTenantSaaS.Application;
using MultiTenantSaaS.Domain;
using MultiTenantSaaS.Infrastructure;

namespace MultiTenantSaaS.Api;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users", async (
            TenantDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var users = await dbContext.Users
                .Join(
                    dbContext.Memberships,
                    user => user.Id,
                    membership => membership.UserId,
                    (user, membership) => new UserResponse(
                        user.Id,
                        user.Email,
                        user.DisplayName,
                        user.IsActive,
                        membership.Role))
                .OrderBy(user => user.DisplayName)
                .ToListAsync(cancellationToken);

            return Results.Ok(users);
        })
        .RequireAuthorization()
        .WithName("ListTenantUsers");

        endpoints.MapPost("/api/users", async (
            CreateUserRequest request,
            HttpContext httpContext,
            ClaimsPrincipal principal,
            ITenantContext tenantContext,
            TenantDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (tenantContext.TenantId is null || principal.GetUserId() is null)
            {
                return Results.Forbid();
            }

            if (!IsValidRequest(request))
            {
                return Results.BadRequest(new { error = "A valid email, display name, and supported role are required." });
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var existingUser = await dbContext.Users
                .SingleOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
            if (existingUser is not null)
            {
                return Results.Conflict(new { error = "A user with this email already exists in the tenant." });
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var user = new AppUser
            {
                Email = normalizedEmail,
                DisplayName = request.DisplayName.Trim()
            };
            dbContext.Users.Add(user);
            dbContext.Memberships.Add(new TenantMembership
            {
                UserId = user.Id,
                TenantId = tenantContext.TenantId.Value,
                Role = request.Role
            });
            dbContext.AuditEntries.Add(new AuditEntry
            {
                ActorId = principal.GetUserId(),
                TenantId = tenantContext.TenantId.Value,
                Action = "user.created",
                Resource = user.Id.ToString(),
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString()
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Results.Created($"/api/users/{user.Id}", new UserResponse(
                user.Id,
                user.Email,
                user.DisplayName,
                user.IsActive,
                request.Role));
        })
        .RequireAuthorization(Policies.CanManageUsers)
        .WithName("CreateTenantUser");

        endpoints.MapPost("/api/invitations", async (
            CreateInvitationRequest request,
            HttpContext httpContext,
            ClaimsPrincipal principal,
            ITenantContext tenantContext,
            TenantDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (tenantContext.TenantId is null || principal.GetUserId() is null)
            {
                return Results.Forbid();
            }

            if (!IsValidInvitationRequest(request))
            {
                return Results.BadRequest(new { error = "A valid email and supported role are required." });
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var existingMembership = await dbContext.Memberships
                .AnyAsync(membership =>
                    membership.TenantId == tenantContext.TenantId.Value &&
                    dbContext.Users.Any(user => user.Id == membership.UserId && user.Email == normalizedEmail),
                    cancellationToken);
            if (existingMembership)
            {
                return Results.Conflict(new { error = "This email already has a tenant membership." });
            }

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var invitation = new TenantInvitation
            {
                TenantId = tenantContext.TenantId.Value,
                Email = normalizedEmail,
                Role = request.Role,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                TokenHash = InvitationTokenHasher.Hash(token)
            };

            dbContext.Invitations.Add(invitation);
            dbContext.AuditEntries.Add(new AuditEntry
            {
                ActorId = principal.GetUserId(),
                TenantId = tenantContext.TenantId.Value,
                Action = "invitation.created",
                Resource = invitation.Id.ToString(),
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString()
            });
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(new InvitationResponse(invitation.Id, invitation.Email, invitation.Role, invitation.ExpiresAt, token));
        })
        .RequireAuthorization(Policies.CanManageUsers)
        .WithName("CreateTenantInvitation");

        endpoints.MapPost("/api/invitations/{invitationId:guid}/revoke", async (
            Guid invitationId,
            HttpContext httpContext,
            ClaimsPrincipal principal,
            ITenantContext tenantContext,
            TenantDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (tenantContext.TenantId is null || principal.GetUserId() is null)
            {
                return Results.Forbid();
            }

            var invitation = await dbContext.Invitations
                .SingleOrDefaultAsync(item => item.Id == invitationId && item.TenantId == tenantContext.TenantId.Value, cancellationToken);

            if (invitation is null)
            {
                return Results.NotFound(new { error = "Invitation was not found for this tenant." });
            }

            if (!invitation.Revoke())
            {
                return Results.BadRequest(new { error = "The invitation is already expired, accepted, or revoked." });
            }

            dbContext.AuditEntries.Add(new AuditEntry
            {
                ActorId = principal.GetUserId(),
                TenantId = tenantContext.TenantId.Value,
                Action = "invitation.revoked",
                Resource = invitation.Id.ToString(),
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString()
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { revoked = true, id = invitation.Id, email = invitation.Email });
        })
        .RequireAuthorization(Policies.CanManageUsers)
        .WithName("RevokeTenantInvitation");

        endpoints.MapGet("/api/invitations", async (
            ITenantContext tenantContext,
            TenantDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (tenantContext.TenantId is null)
            {
                return Results.Forbid();
            }

            var invitations = await dbContext.Invitations
                .Where(item => item.TenantId == tenantContext.TenantId.Value)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new InvitationSummary(
                    item.Id,
                    item.Email,
                    item.Role,
                    item.ExpiresAt,
                    item.IsActive))
                .ToListAsync(cancellationToken);

            return Results.Ok(invitations);
        })
        .RequireAuthorization(Policies.CanManageUsers)
        .WithName("ListTenantInvitations");

        endpoints.MapPost("/api/invitations/accept", async (
            AcceptInvitationRequest request,
            UserManager<CatalogUser> userManager,
            ITenantContext tenantContext,
            TenantDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (tenantContext.TenantId is null || string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "A valid tenant, invitation token, and password are required." });
            }

            var tokenHash = InvitationTokenHasher.Hash(request.Token.Trim());
            var invitation = await dbContext.Invitations
                .SingleOrDefaultAsync(item =>
                    item.TenantId == tenantContext.TenantId.Value
                    && item.TokenHash == tokenHash
                    && !item.IsRevoked
                    && item.AcceptedAt == null
                    && item.ExpiresAt > DateTimeOffset.UtcNow,
                    cancellationToken);
            if (invitation is null)
            {
                return Results.BadRequest(new { error = "The invitation is invalid, expired, or already used." });
            }

            var normalizedEmail = invitation.Email.Trim().ToLowerInvariant();
            var tenantUser = await dbContext.Users
                .SingleOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
            if (tenantUser is null)
            {
                tenantUser = new AppUser
                {
                    Email = normalizedEmail,
                    DisplayName = normalizedEmail.Split('@')[0].Trim()
                };
                dbContext.Users.Add(tenantUser);
            }

            var existingMembership = await dbContext.Memberships
                .AnyAsync(membership => membership.TenantId == tenantContext.TenantId.Value && membership.UserId == tenantUser.Id, cancellationToken);
            if (!existingMembership)
            {
                dbContext.Memberships.Add(new TenantMembership
                {
                    UserId = tenantUser.Id,
                    TenantId = tenantContext.TenantId.Value,
                    Role = invitation.Role
                });
            }

            var catalogUser = await userManager.FindByEmailAsync(normalizedEmail);
            if (catalogUser is null)
            {
                catalogUser = new CatalogUser
                {
                    UserName = normalizedEmail,
                    Email = normalizedEmail,
                    DisplayName = tenantUser.DisplayName,
                    EmailConfirmed = true
                };
                var createResult = await userManager.CreateAsync(catalogUser, request.Password);
                if (!createResult.Succeeded)
                {
                    return Results.BadRequest(new { error = string.Join(", ", createResult.Errors.Select(error => error.Description)) });
                }
            }

            if (!await userManager.IsInRoleAsync(catalogUser, invitation.Role))
            {
                var roleResult = await userManager.AddToRoleAsync(catalogUser, invitation.Role);
                if (!roleResult.Succeeded)
                {
                    return Results.BadRequest(new { error = string.Join(", ", roleResult.Errors.Select(error => error.Description)) });
                }
            }

            invitation.AcceptedAt = DateTimeOffset.UtcNow;
            dbContext.AuditEntries.Add(new AuditEntry
            {
                TenantId = tenantContext.TenantId.Value,
                Action = "invitation.accepted",
                Resource = invitation.Id.ToString(),
                IpAddress = null
            });
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { accepted = true, email = normalizedEmail, role = invitation.Role });
        })
        .AllowAnonymous()
        .WithName("AcceptTenantInvitation");

        return endpoints;
    }

    private static bool IsValidRequest(CreateUserRequest request)
    {
        try
        {
            _ = new MailAddress(request.Email);
        }
        catch (FormatException)
        {
            return false;
        }

        return request.Email.Length <= 320
            && !string.IsNullOrWhiteSpace(request.DisplayName)
            && request.DisplayName.Length <= 160
            && TenantRoles.IsSupported(request.Role);
    }

    private static bool IsValidInvitationRequest(CreateInvitationRequest request)
    {
        try
        {
            _ = new MailAddress(request.Email);
        }
        catch (FormatException)
        {
            return false;
        }

        return request.Email.Length <= 320
            && TenantRoles.IsSupported(request.Role);
    }
}

public sealed record CreateUserRequest(string Email, string DisplayName, string Role);
public sealed record CreateInvitationRequest(string Email, string Role);
public sealed record AcceptInvitationRequest(string Token, string Password);
public sealed record InvitationResponse(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt, string Token);
public sealed record InvitationSummary(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt, bool IsActive);
public sealed record UserResponse(Guid Id, string Email, string DisplayName, bool IsActive, string Role);
