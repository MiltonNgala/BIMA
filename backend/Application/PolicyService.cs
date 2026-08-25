using Bima.Api.Data;
using Bima.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bima.Api.Application;

public sealed record PolicySummary(string Number, string Customer, string Product, string Status, decimal Premium, string RenewalDate);

public interface IPolicyService
{
    Task<IReadOnlyList<PolicySummary>> GetPoliciesAsync(CancellationToken cancellationToken = default);
    Task<PolicySummary> GetPolicyAsync(string number, CancellationToken cancellationToken = default);
    Task<PolicySummary> CreatePolicyAsync(CreatePolicyRequest request, string userId, CancellationToken cancellationToken = default);
    Task<PolicySummary> UpdatePolicyAsync(string number, UpdatePolicyRequest request, string userId, CancellationToken cancellationToken = default);
    Task DeletePolicyAsync(string number, string userId, CancellationToken cancellationToken = default);
}

public sealed record CreatePolicyRequest(string Number, string Customer, string Product, decimal Premium, DateOnly RenewalDate, Guid? CustomerId = null);
public sealed record UpdatePolicyRequest(string Status, decimal? Premium, DateOnly? RenewalDate);

public sealed class DatabasePolicyService(BimaDbContext db, TenantContext tenantContext, AuditService auditService) : IPolicyService
{
    public async Task<IReadOnlyList<PolicySummary>> GetPoliciesAsync(CancellationToken cancellationToken = default)
    {
        return await db.Policies
            .AsNoTracking()
            .Where(policy => policy.TenantId == tenantContext.TenantId)
            .OrderBy(policy => policy.RenewalDate)
            .Select(policy => new PolicySummary(
                policy.Number,
                policy.Customer,
                policy.Product,
                policy.Status,
                policy.Premium,
                policy.RenewalDate.ToString("yyyy-MM-dd")))
            .ToListAsync(cancellationToken);
    }

    public async Task<PolicySummary> GetPolicyAsync(string number, CancellationToken cancellationToken = default) =>
        await db.Policies.AsNoTracking().Where(policy => policy.TenantId == tenantContext.TenantId && policy.Number == number)
            .Select(policy => new PolicySummary(policy.Number, policy.Customer, policy.Product, policy.Status, policy.Premium, policy.RenewalDate.ToString("yyyy-MM-dd"))).SingleOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException("Policy was not found.");

    public async Task<PolicySummary> CreatePolicyAsync(CreatePolicyRequest request, string userId, CancellationToken cancellationToken = default)
    {
        PolicyValidation.Validate(request);
        if (request.CustomerId is not null && !await db.Customers.AnyAsync(customer => customer.Id == request.CustomerId && customer.TenantId == tenantContext.TenantId, cancellationToken))
            throw new KeyNotFoundException("Customer was not found in this tenant.");
        if (await db.Policies.AnyAsync(policy => policy.TenantId == tenantContext.TenantId && policy.Number == request.Number, cancellationToken))
        {
            throw new InvalidOperationException($"Policy number {request.Number} already exists for this tenant.");
        }

        var now = DateTimeOffset.UtcNow;
        var policy = new Policy
        {
            Id = Guid.NewGuid(), TenantId = tenantContext.TenantId, Number = request.Number,
            CustomerId = request.CustomerId,
            Customer = request.Customer, Product = request.Product, Status = "Active",
            Premium = request.Premium, RenewalDate = request.RenewalDate,
            CreatedAt = now, CreatedBy = userId, UpdatedAt = now, UpdatedBy = userId
        };
        db.Policies.Add(policy);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(userId, "policy.created", "Policy", policy.Id.ToString(), new { policy.Number }, cancellationToken);
        return new PolicySummary(policy.Number, policy.Customer, policy.Product, policy.Status, policy.Premium, policy.RenewalDate.ToString("yyyy-MM-dd"));
    }

    public async Task<PolicySummary> UpdatePolicyAsync(string number, UpdatePolicyRequest request, string userId, CancellationToken cancellationToken = default)
    {
        var policy = await db.Policies.SingleOrDefaultAsync(candidate => candidate.TenantId == tenantContext.TenantId && candidate.Number == number, cancellationToken) ?? throw new KeyNotFoundException("Policy was not found.");
        if (request.Status is not ("Active" or "Renewal Due" or "Cancelled" or "Expired")) throw new ArgumentException("Status must be Active, Renewal Due, Cancelled, or Expired.");
        if (policy.Status == "Cancelled" || policy.Status == "Expired") throw new InvalidOperationException("A cancelled or expired policy cannot be updated.");
        if (request.Premium is <= 0) throw new ArgumentException("Premium must be greater than zero.");
        policy.Status = request.Status; policy.Premium = request.Premium ?? policy.Premium; policy.RenewalDate = request.RenewalDate ?? policy.RenewalDate; policy.UpdatedAt = DateTimeOffset.UtcNow; policy.UpdatedBy = userId;
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(userId, "policy.updated", "Policy", policy.Id.ToString(), new { policy.Status, policy.Premium, policy.RenewalDate }, cancellationToken);
        return new PolicySummary(policy.Number, policy.Customer, policy.Product, policy.Status, policy.Premium, policy.RenewalDate.ToString("yyyy-MM-dd"));
    }

    public async Task DeletePolicyAsync(string number, string userId, CancellationToken cancellationToken = default)
    {
        var policy = await db.Policies.SingleOrDefaultAsync(candidate => candidate.TenantId == tenantContext.TenantId && candidate.Number == number, cancellationToken)
            ?? throw new KeyNotFoundException("Policy was not found.");
        db.Policies.Remove(policy);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(userId, "policy.deleted", "Policy", policy.Id.ToString(), new { policy.Number }, cancellationToken);
    }
}

public sealed class SamplePolicyService : IPolicyService
{
    private static readonly PolicySummary[] Policies =
    [
        new("POL-10482", "Northwind Logistics", "Commercial Auto", "Active", 128400m, "2026-09-14"),
        new("POL-10476", "Aster & Co.", "General Liability", "Renewal due", 86400m, "2026-08-30"),
        new("POL-10463", "Redwood Hospitality", "Property", "Active", 214750m, "2026-11-02"),
        new("POL-10451", "Cedar Health Partners", "Workers Compensation", "Review", 97500m, "2026-09-04")
    ];

    public Task<IReadOnlyList<PolicySummary>> GetPoliciesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PolicySummary>>(Policies);
    public Task<PolicySummary> GetPolicyAsync(string number, CancellationToken cancellationToken = default) => Task.FromResult(Policies.SingleOrDefault(policy => policy.Number == number) ?? throw new KeyNotFoundException("Policy was not found."));

    public Task<PolicySummary> CreatePolicyAsync(CreatePolicyRequest request, string userId, CancellationToken cancellationToken = default)
    {
        PolicyValidation.Validate(request);
        var policy = new PolicySummary(request.Number, request.Customer, request.Product, "Active", request.Premium, request.RenewalDate.ToString("yyyy-MM-dd"));
        return Task.FromResult(policy);
    }
    public Task<PolicySummary> UpdatePolicyAsync(string number, UpdatePolicyRequest request, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException("Policy updates require a configured database.");
    public Task DeletePolicyAsync(string number, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException("Policy deletion requires a configured database.");
}

internal static class PolicyValidation
{
    public static void Validate(CreatePolicyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Number) || request.Number.Length > 32) throw new ArgumentException("Policy number is required and must be 32 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.Customer) || request.Customer.Length > 200) throw new ArgumentException("Customer is required and must be 200 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.Product) || request.Product.Length > 120) throw new ArgumentException("Product is required and must be 120 characters or fewer.");
        if (request.Premium <= 0) throw new ArgumentException("Premium must be greater than zero.");
    }
}
