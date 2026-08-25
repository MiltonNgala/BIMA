using Bima.Api.Data;
using Bima.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bima.Api.Application;

public sealed record InvoiceSummary(string InvoiceNumber, string PolicyNumber, decimal Amount, decimal PaidAmount, string DueDate, string Status);
public sealed record CreateInvoiceRequest(string InvoiceNumber, string PolicyNumber, decimal Amount, DateOnly DueDate);

public sealed class BillingService(BimaDbContext db, TenantContext tenantContext, AuditService auditService)
{
    public async Task<IReadOnlyList<InvoiceSummary>> GetInvoicesAsync(CancellationToken cancellationToken = default) =>
        await db.Invoices.AsNoTracking().Where(invoice => invoice.TenantId == tenantContext.TenantId).OrderBy(invoice => invoice.DueDate)
            .Select(invoice => new InvoiceSummary(invoice.InvoiceNumber, invoice.PolicyNumber, invoice.Amount, invoice.PaidAmount, invoice.DueDate.ToString("yyyy-MM-dd"), invoice.Status)).ToListAsync(cancellationToken);

    public async Task<InvoiceSummary> CreateInvoiceAsync(CreateInvoiceRequest request, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.InvoiceNumber) || request.InvoiceNumber.Length > 32) throw new ArgumentException("Invoice number is required and must be 32 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.PolicyNumber) || request.PolicyNumber.Length > 32) throw new ArgumentException("Policy number is required and must be 32 characters or fewer.");
        if (request.Amount <= 0) throw new ArgumentException("Invoice amount must be greater than zero.");
        if (await db.Invoices.AnyAsync(invoice => invoice.TenantId == tenantContext.TenantId && invoice.InvoiceNumber == request.InvoiceNumber, cancellationToken)) throw new InvalidOperationException($"Invoice number {request.InvoiceNumber} already exists for this tenant.");
        var now = DateTimeOffset.UtcNow;
        var invoice = new Invoice { Id = Guid.NewGuid(), TenantId = tenantContext.TenantId, InvoiceNumber = request.InvoiceNumber.Trim(), PolicyNumber = request.PolicyNumber.Trim(), Amount = request.Amount, PaidAmount = 0, DueDate = request.DueDate, Status = "Open", CreatedAt = now, CreatedBy = userId, UpdatedAt = now, UpdatedBy = userId };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(userId, "invoice.created", "Invoice", invoice.Id.ToString(), new { invoice.InvoiceNumber }, cancellationToken);
        return ToSummary(invoice);
    }

    public async Task DeleteInvoiceAsync(string invoiceNumber, string userId, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(candidate => candidate.TenantId == tenantContext.TenantId && candidate.InvoiceNumber == invoiceNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Invoice was not found.");
        if (invoice.PaidAmount != 0 || await db.Payments.AnyAsync(payment => payment.InvoiceId == invoice.Id && payment.TenantId == tenantContext.TenantId, cancellationToken))
            throw new InvalidOperationException("An invoice with payments cannot be deleted.");
        db.Invoices.Remove(invoice);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(userId, "invoice.deleted", "Invoice", invoice.Id.ToString(), new { invoice.InvoiceNumber }, cancellationToken);
    }

    private static InvoiceSummary ToSummary(Invoice invoice) => new(invoice.InvoiceNumber, invoice.PolicyNumber, invoice.Amount, invoice.PaidAmount, invoice.DueDate.ToString("yyyy-MM-dd"), invoice.Status);
}
