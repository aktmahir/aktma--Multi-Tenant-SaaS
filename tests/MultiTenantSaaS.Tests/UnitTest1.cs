using System.Security.Claims;
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
}
