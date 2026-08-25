namespace Bima.Api.Domain;

public sealed class Invoice
{
    public Guid Id { get; set; }
    public required string TenantId { get; set; }
    public required string InvoiceNumber { get; set; }
    public required string PolicyNumber { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public DateOnly DueDate { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public required string UpdatedBy { get; set; }
}
