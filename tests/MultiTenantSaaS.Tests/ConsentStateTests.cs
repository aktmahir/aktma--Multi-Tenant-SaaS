using Microsoft.AspNetCore.DataProtection;
using MultiTenantSaaS.Api;

namespace MultiTenantSaaS.Tests;

public sealed class ConsentStateTests
{
    [Fact]
    public void ConsentState_RoundTripsReturnUrl()
    {
        var provider = DataProtectionProvider.Create("northstar-tests");
        var protector = new ConsentStateProtector(provider);
        const string returnUrl = "/connect/authorize?client_id=northstar-spa&response_type=code";

        var state = protector.Protect(returnUrl);

        Assert.Equal(returnUrl, protector.Unprotect(state));
    }

    [Fact]
    public void ConsentState_RejectsTampering()
    {
        var provider = DataProtectionProvider.Create("northstar-tests");
        var protector = new ConsentStateProtector(provider);
        var state = protector.Protect("/connect/authorize");
        var tamperedState = state + "tampered";

        Assert.ThrowsAny<Exception>(() => protector.Unprotect(tamperedState));
    }
}
