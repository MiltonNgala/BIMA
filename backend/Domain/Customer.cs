namespace Bima.Api.Domain;

public sealed class Customer
{
    public Guid Id { get; set; }
    public required string TenantId { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string Phone { get; set; }
    public required string CustomerType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public required string UpdatedBy { get; set; }
}
