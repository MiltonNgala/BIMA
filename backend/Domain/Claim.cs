namespace Bima.Api.Domain;

public sealed class Claim
{
    public Guid Id { get; set; }
    public required string TenantId { get; set; }
    public required string ClaimNumber { get; set; }
    public required string PolicyNumber { get; set; }
    public required string Customer { get; set; }
    public required string Description { get; set; }
    public required string Status { get; set; }
    public decimal ReserveAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTimeOffset LossDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public required string UpdatedBy { get; set; }
}
