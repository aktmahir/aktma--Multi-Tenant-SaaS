using Microsoft.AspNetCore.DataProtection;

namespace MultiTenantSaaS.Api;

public sealed class ConsentStateProtector
{
    private readonly ITimeLimitedDataProtector protector;

    public ConsentStateProtector(IDataProtectionProvider provider)
    {
        protector = provider
            .CreateProtector("Northstar.Identity.Consent.v1")
            .ToTimeLimitedDataProtector();
    }

    public string Protect(string returnUrl) => protector.Protect(returnUrl, TimeSpan.FromMinutes(5));

    public string Unprotect(string state) => protector.Unprotect(state);
}
