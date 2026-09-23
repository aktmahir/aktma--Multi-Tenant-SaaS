namespace MultiTenantSaaS.Domain;

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public string? Domain { get; set; }
    public string BrandingColor { get; set; } = "#E45D42";
    public string SchemaName => $"tenant_{Id:N}";
    public TenantProvisioningStatus ProvisioningStatus { get; set; } = TenantProvisioningStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum TenantProvisioningStatus
{
    Pending,
    Provisioned,
    Failed
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class TenantMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string Role { get; set; }
    public Guid TenantId { get; set; }
}

public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ActorId { get; set; }
    public required string Action { get; set; }
    public required string Resource { get; set; }
    public string? IpAddress { get; set; }
    public Guid TenantId { get; set; }
}

public sealed class TenantInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public bool IsRevoked { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }

    public bool IsActive => !IsRevoked && AcceptedAt is null && ExpiresAt > DateTimeOffset.UtcNow;

    public bool Revoke()
    {
        if (!IsActive)
        {
            return false;
        }

        IsRevoked = true;
        return true;
    }
}

public static class InvitationTokenHasher
{
    public static string Hash(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}