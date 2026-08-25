namespace Bima.Api.Domain;

public sealed class Policy
{
    public Guid Id { get; set; }
    public required string TenantId { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? CustomerEntity { get; set; }
    public required string Number { get; set; }
    public required string Customer { get; set; }
    public required string Product { get; set; }
    public required string Status { get; set; }
    public decimal Premium { get; set; }
    public DateOnly RenewalDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public required string UpdatedBy { get; set; }
}
