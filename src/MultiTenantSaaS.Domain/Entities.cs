namespace MultiTenantSaaS.Domain;

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public string? Domain { get; set; }
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