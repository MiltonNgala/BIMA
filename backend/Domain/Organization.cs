namespace Bima.Api.Domain;

public sealed class Organization
{
    public required string TenantId { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
