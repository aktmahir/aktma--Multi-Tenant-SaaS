using System.Security.Claims;
using MultiTenantSaaS.Api;
using MultiTenantSaaS.Application;
using MultiTenantSaaS.Domain;

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
    public void TenantAuthorization_AllowsCookieUserWhenMembershipMatchesSelectedTenant()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], "Cookies"));

        Assert.True(TenantClaimValidator.IsAuthorizedForTenant(principal, tenantId, "TenantUser"));
    }

    [Fact]
    public void TenantAuthorization_RejectsCookieUserWithoutMatchingMembership()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], "Cookies"));

        Assert.False(TenantClaimValidator.IsAuthorizedForTenant(principal, tenantId, null));
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
}
