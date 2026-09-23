using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MultiTenantSaaS.Api;
using MultiTenantSaaS.Application;
using MultiTenantSaaS.Domain;
using MultiTenantSaaS.Infrastructure;

namespace MultiTenantSaaS.Tests;

public sealed class TenantAuthorizationTests
{
    [Fact]
    public void MissingMembership_DoesNotEmitTenantIdClaim()
    {
        var tenantId = Guid.NewGuid();
        var canIssue = TenantTokenClaims.TryCreate(tenantId, null, out var emittedTenantId, out var role);

        Assert.False(canIssue);
        Assert.Equal(string.Empty, emittedTenantId);
        Assert.Equal(string.Empty, role);
    }

    [Fact]
    public void ValidMembership_EmitsTenantIdAndRole()
    {
        var tenantId = Guid.NewGuid();
        var canIssue = TenantTokenClaims.TryCreate(tenantId, "TenantAdmin", out var emittedTenantId, out var role);

        Assert.True(canIssue);
        Assert.Equal(tenantId.ToString(), emittedTenantId);
        Assert.Equal("TenantAdmin", role);
    }

    [Fact]
    public void TenantAuthorization_AllowsCookieUserWhenTenantClaimMatchesSelectedTenant()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim(ClaimTypes.Role, TenantRoles.TenantUser)
        ], "Cookies"));

        Assert.True(TenantClaimValidator.IsAuthorizedForTenant(principal, tenantId, TenantRoles.TenantUser));
    }

    [Fact]
    public void TenantAuthorization_RejectsMismatchedTenantClaimEvenWithMembership()
    {
        var userId = Guid.NewGuid();
        var selectedTenantId = Guid.NewGuid();
        var tokenTenantId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("tenant_id", tokenTenantId.ToString()),
            new Claim(ClaimTypes.Role, TenantRoles.TenantUser)
        ], "Cookies"));

        Assert.False(TenantClaimValidator.IsAuthorizedForTenant(principal, selectedTenantId, TenantRoles.TenantUser));
    }

    [Fact]
    public void TenantAuthorization_RejectsCookieUserWithoutTenantClaim()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], "Cookies"));

        Assert.False(TenantClaimValidator.IsAuthorizedForTenant(principal, tenantId, TenantRoles.TenantUser));
    }

    [Fact]
    public void InvitationTokenHash_IsStableAndNotReadable()
    {
        const string token = "invite-token-123";
        var first = InvitationTokenHasher.Hash(token);
        var second = InvitationTokenHasher.Hash(token);

        Assert.Equal(first, second);
        Assert.NotEqual(token, first);
        Assert.True(first.Length > 16);
    }

    [Fact]
    public void Invitation_IsActiveOnlyWhenUnredeemedAndNotExpired()
    {
        var invitation = new TenantInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Email = "new-member@northstar.local",
            Role = TenantRoles.TenantUser,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            IsRevoked = false,
            AcceptedAt = null
        };

        Assert.True(invitation.IsActive);

        invitation.AcceptedAt = DateTimeOffset.UtcNow;
        Assert.False(invitation.IsActive);
    }

    [Fact]
    public void Invitation_RevokeMarksInvitationInactive()
    {
        var invitation = new TenantInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Email = "new-member@northstar.local",
            Role = TenantRoles.TenantUser,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            IsRevoked = false,
            AcceptedAt = null
        };

        Assert.True(invitation.Revoke());
        Assert.True(invitation.IsRevoked);
        Assert.False(invitation.IsActive);
    }

    [Fact]
    public async Task TenantMembershipService_UpdatesTenantRole_WhenMembershipExists()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new FakeTenantContext(tenantId);

        await using (var dbContext = new TenantDbContext(options, tenantContext))
        {
            dbContext.Memberships.Add(new TenantMembership
            {
                UserId = userId,
                TenantId = tenantId,
                Role = TenantRoles.TenantUser
            });
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new TenantDbContext(options, tenantContext))
        {
            var service = new TenantMembershipService(dbContext, tenantContext);

            var updatedRole = await service.UpdateRoleAsync(userId, tenantId, TenantRoles.TenantAdmin, CancellationToken.None);

            Assert.Equal(TenantRoles.TenantAdmin, updatedRole);
            var membership = await dbContext.Memberships.SingleAsync(m => m.UserId == userId && m.TenantId == tenantId);
            Assert.Equal(TenantRoles.TenantAdmin, membership.Role);
        }
    }

    [Fact]
    public async Task TenantMembershipService_RemovesMembership_WhenMembershipExists()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new FakeTenantContext(tenantId);

        await using (var dbContext = new TenantDbContext(options, tenantContext))
        {
            dbContext.Memberships.Add(new TenantMembership
            {
                UserId = userId,
                TenantId = tenantId,
                Role = TenantRoles.TenantUser
            });
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new TenantDbContext(options, tenantContext))
        {
            var service = new TenantMembershipService(dbContext, tenantContext);

            var removed = await service.RemoveAsync(userId, tenantId, CancellationToken.None);

            Assert.True(removed);
            var membership = await dbContext.Memberships.SingleOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId);
            Assert.Null(membership);
        }
    }

    private sealed class FakeTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public string? Slug => "acme-corp";
        public string? SchemaName => "public";
        public bool IsResolved => true;
    }
}
