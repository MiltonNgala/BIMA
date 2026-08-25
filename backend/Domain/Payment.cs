namespace Bima.Api.Domain;

public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public required string TenantId { get; set; }
    public decimal Amount { get; set; }
    public required string Reference { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public required string ReceivedBy { get; set; }
}
