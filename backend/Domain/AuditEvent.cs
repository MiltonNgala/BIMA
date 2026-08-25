namespace Bima.Api.Domain;

public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public required string TenantId { get; set; }
    public required string UserId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Metadata { get; set; }
}
