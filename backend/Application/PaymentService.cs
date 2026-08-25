using Bima.Api.Data;
using Bima.Api.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Bima.Api.Application;

public sealed record PaymentSummary(Guid Id, string Reference, decimal Amount, string ReceivedAt);
public sealed record RecordPaymentRequest(decimal Amount, string Reference);

public sealed class PaymentService(BimaDbContext db, TenantContext tenantContext, AuditService auditService)
{
    public async Task<IReadOnlyList<PaymentSummary>> GetPaymentsAsync(string invoiceNumber, CancellationToken cancellationToken = default) =>
        await db.Payments.AsNoTracking()
            .Where(payment => payment.TenantId == tenantContext.TenantId && db.Invoices.Any(invoice => invoice.Id == payment.InvoiceId && invoice.InvoiceNumber == invoiceNumber && invoice.TenantId == tenantContext.TenantId))
            .OrderByDescending(payment => payment.ReceivedAt)
            .Select(payment => new PaymentSummary(payment.Id, payment.Reference, payment.Amount, payment.ReceivedAt.ToString("O")))
            .ToListAsync(cancellationToken);

    public async Task<PaymentSummary> RecordPaymentAsync(string invoiceNumber, RecordPaymentRequest request, string userId, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0) throw new ArgumentException("Payment amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Length > 100) throw new ArgumentException("Payment reference is required and must be 100 characters or fewer.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var invoice = await db.Invoices.SingleOrDefaultAsync(candidate => candidate.TenantId == tenantContext.TenantId && candidate.InvoiceNumber == invoiceNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Invoice was not found.");
        if (invoice.PaidAmount + request.Amount > invoice.Amount) throw new InvalidOperationException("Payment cannot exceed the invoice balance.");

        var payment = new Payment { Id = Guid.NewGuid(), InvoiceId = invoice.Id, TenantId = tenantContext.TenantId, Amount = request.Amount, Reference = request.Reference.Trim(), ReceivedAt = DateTimeOffset.UtcNow, ReceivedBy = userId };
        invoice.PaidAmount += request.Amount;
        invoice.Status = invoice.PaidAmount == invoice.Amount ? "Paid" : "Partially Paid";
        invoice.UpdatedAt = DateTimeOffset.UtcNow;
        invoice.UpdatedBy = userId;
        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await auditService.RecordAsync(userId, "invoice.payment_recorded", "Invoice", invoice.Id.ToString(), new { payment.Reference, payment.Amount }, cancellationToken);
        return new PaymentSummary(payment.Id, payment.Reference, payment.Amount, payment.ReceivedAt.ToString("O"));
    }
}
