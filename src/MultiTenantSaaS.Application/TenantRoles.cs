namespace MultiTenantSaaS.Application;

public static class TenantRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string TenantUser = "TenantUser";
    public const string ReadOnly = "ReadOnly";

    public static bool IsSupported(string role) => role is SuperAdmin or TenantAdmin or TenantUser or ReadOnly;
}