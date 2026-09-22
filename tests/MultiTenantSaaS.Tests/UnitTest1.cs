using System.Security.Claims;
using MultiTenantSaaS.Api;
using MultiTenantSaaS.Application;

namespace MultiTenantSaaS.Tests;

public class UnitTest1
{
    [Fact]
    public void TenantContext_StoresResolvedSchemaAndTenant()
    {
        var context = new TenantContext();
        var tenantId = Guid.NewGuid();

        context.Set(new TenantResolution(tenantId, "acme-corp", "tenant_acme"));

        Assert.True(context.IsResolved);
        Assert.Equal(tenantId, context.TenantId);
        Assert.Equal("tenant_acme", context.SchemaName);
    }

    [Fact]
    public void TokenTenantClaim_IsAvailableForReplayValidation()
    {
        var expectedTenant = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", expectedTenant.ToString())], "Bearer"));

        Assert.Equal(expectedTenant, principal.GetTenantId());
    }

    [Fact]
    public void TokenTenantClaim_DoesNotMatchAnotherTenant()
    {
        var tokenTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", tokenTenant.ToString())], "Bearer"));

        Assert.False(TenantClaimValidator.Matches(principal, otherTenant));
    }

    [Fact]
    public void TokenWithoutTenantClaim_IsRejected()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer"));

        Assert.False(TenantClaimValidator.Matches(principal, Guid.NewGuid()));
    }

    [Fact]
    public void ApiError_UsesStableFrontendKey()
    {
        var error = new ApiError("Invalid tenant settings", "Branding color must be a hex color.");

        Assert.Equal("Invalid tenant settings", error.Error);
        Assert.Equal("Branding color must be a hex color.", error.Detail);
    }

    [Fact]
    public void TenantSummary_ExposesSlugAndStatus()
    {
        var tenant = new TenantSummaryResponse(Guid.NewGuid(), "orbit-labs", "Orbit Labs", "orbit.example.com", "#123456", "Provisioned");

        Assert.Equal("orbit-labs", tenant.Slug);
        Assert.Equal("Provisioned", tenant.ProvisioningStatus);
    }

    [Theory]
    [InlineData(TenantRoles.SuperAdmin)]
    [InlineData(TenantRoles.TenantAdmin)]
    [InlineData(TenantRoles.TenantUser)]
    [InlineData(TenantRoles.ReadOnly)]
    public void SupportedTenantRoles_AreAccepted(string role)
    {
        Assert.True(TenantRoles.IsSupported(role));
    }

    [Fact]
    public void UnknownTenantRole_IsRejected()
    {
        Assert.False(TenantRoles.IsSupported("BillingOwner"));
    }
}
