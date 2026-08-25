namespace Bima.Api.Domain;

public sealed class User
{
    public Guid Id { get; set; }
    public required string TenantId { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public required string Role { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
