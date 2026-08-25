using Bima.Api.Data;
using Bima.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bima.Api.Application;

public sealed record ClaimSummary(string ClaimNumber, string PolicyNumber, string Customer, string Description, string Status, decimal ReserveAmount, decimal PaidAmount, string LossDate);
public sealed record CreateClaimRequest(string ClaimNumber, string PolicyNumber, string Customer, string Description, decimal ReserveAmount, DateTimeOffset LossDate);
public sealed record UpdateClaimRequest(string Status, decimal? ReserveAmount, decimal? PaidAmount);

public interface IClaimService
{
    Task<IReadOnlyList<ClaimSummary>> GetClaimsAsync(CancellationToken cancellationToken = default);
    Task<ClaimSummary> GetClaimAsync(string claimNumber, CancellationToken cancellationToken = default);
    Task<ClaimSummary> CreateClaimAsync(CreateClaimRequest request, string userId, CancellationToken cancellationToken = default);
    Task<ClaimSummary> UpdateClaimAsync(string claimNumber, UpdateClaimRequest request, string userId, CancellationToken cancellationToken = default);
    Task DeleteClaimAsync(string claimNumber, string userId, CancellationToken cancellationToken = default);
}

public sealed class DatabaseClaimService(BimaDbContext db, TenantContext tenantContext, AuditService auditService) : IClaimService
{
    public async Task<IReadOnlyList<ClaimSummary>> GetClaimsAsync(CancellationToken cancellationToken = default) =>
        await db.Claims.AsNoTracking()
            .Where(claim => claim.TenantId == tenantContext.TenantId)
            .OrderByDescending(claim => claim.CreatedAt)
            .Select(claim => new ClaimSummary(claim.ClaimNumber, claim.PolicyNumber, claim.Customer, claim.Description, claim.Status, claim.ReserveAmount, claim.PaidAmount, claim.LossDate.ToString("yyyy-MM-dd")))
            .ToListAsync(cancellationToken);

    public async Task<ClaimSummary> GetClaimAsync(string claimNumber, CancellationToken cancellationToken = default) =>
        await db.Claims.AsNoTracking()
            .Where(claim => claim.TenantId == tenantContext.TenantId && claim.ClaimNumber == claimNumber)
            .Select(claim => new ClaimSummary(claim.ClaimNumber, claim.PolicyNumber, claim.Customer, claim.Description, claim.Status, claim.ReserveAmount, claim.PaidAmount, claim.LossDate.ToString("yyyy-MM-dd")))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException("Claim was not found.");

    public async Task<ClaimSummary> CreateClaimAsync(CreateClaimRequest request, string userId, CancellationToken cancellationToken = default)
    {
        ClaimValidation.Validate(request);
        if (await db.Claims.AnyAsync(claim => claim.TenantId == tenantContext.TenantId && claim.ClaimNumber == request.ClaimNumber, cancellationToken))
            throw new InvalidOperationException($"Claim number {request.ClaimNumber} already exists for this tenant.");

        var now = DateTimeOffset.UtcNow;
        var claim = new Claim
        {
            Id = Guid.NewGuid(), TenantId = tenantContext.TenantId, ClaimNumber = request.ClaimNumber.Trim(),
            PolicyNumber = request.PolicyNumber.Trim(), Customer = request.Customer.Trim(), Description = request.Description.Trim(),
            Status = "Open", ReserveAmount = request.ReserveAmount, PaidAmount = 0, LossDate = request.LossDate,
            CreatedAt = now, CreatedBy = userId, UpdatedAt = now, UpdatedBy = userId
        };
        db.Claims.Add(claim);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(userId, "claim.created", "Claim", claim.Id.ToString(), new { claim.ClaimNumber }, cancellationToken);
        return ToSummary(claim);
    }

    private static ClaimSummary ToSummary(Claim claim) => new(claim.ClaimNumber, claim.PolicyNumber, claim.Customer, claim.Description, claim.Status, claim.ReserveAmount, claim.PaidAmount, claim.LossDate.ToString("yyyy-MM-dd"));

    public async Task<ClaimSummary> UpdateClaimAsync(string claimNumber, UpdateClaimRequest request, string userId, CancellationToken cancellationToken = default)
    {
        var claim = await db.Claims.SingleOrDefaultAsync(candidate => candidate.TenantId == tenantContext.TenantId && candidate.ClaimNumber == claimNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Claim was not found.");
        if (request.Status is not ("Open" or "Under Review" or "Approved" or "Rejected" or "Settled" or "Closed"))
            throw new ArgumentException("Status is not supported.");
        if (!IsAllowedTransition(claim.Status, request.Status))
            throw new InvalidOperationException($"A claim cannot move from {claim.Status} to {request.Status}.");
        if (request.ReserveAmount is <= 0 || request.PaidAmount is < 0)
            throw new ArgumentException("Reserve must be greater than zero and paid amount cannot be negative.");

        var reserve = request.ReserveAmount ?? claim.ReserveAmount;
        var paid = request.PaidAmount ?? claim.PaidAmount;
        if (paid > reserve)
            throw new ArgumentException("Paid amount cannot exceed the reserve amount.");
        claim.Status = request.Status;
        claim.ReserveAmount = reserve;
        claim.PaidAmount = paid;
        claim.UpdatedAt = DateTimeOffset.UtcNow;
        claim.UpdatedBy = userId;
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(userId, "claim.updated", "Claim", claim.Id.ToString(), new { claim.Status, claim.ReserveAmount, claim.PaidAmount }, cancellationToken);
        return ToSummary(claim);
    }

    public async Task DeleteClaimAsync(string claimNumber, string userId, CancellationToken cancellationToken = default)
    {
        var claim = await db.Claims.SingleOrDefaultAsync(candidate => candidate.TenantId == tenantContext.TenantId && candidate.ClaimNumber == claimNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Claim was not found.");
        if (claim.Status is not ("Open" or "Closed"))
            throw new InvalidOperationException("Only open or closed claims can be deleted.");
        db.Claims.Remove(claim);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(userId, "claim.deleted", "Claim", claim.Id.ToString(), new { claim.ClaimNumber }, cancellationToken);
    }

    private static bool IsAllowedTransition(string current, string next) => current switch
    {
        "Open" => next == "Under Review",
        "Under Review" => next is "Approved" or "Rejected",
        "Approved" => next is "Settled" or "Closed",
        "Rejected" => next == "Closed",
        "Settled" => next == "Closed",
        _ => false
    };
}

public sealed class SampleClaimService : IClaimService
{
    public Task<IReadOnlyList<ClaimSummary>> GetClaimsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ClaimSummary>>([]);
    public Task<ClaimSummary> GetClaimAsync(string claimNumber, CancellationToken cancellationToken = default) => throw new KeyNotFoundException("Claim was not found.");

    public Task<ClaimSummary> CreateClaimAsync(CreateClaimRequest request, string userId, CancellationToken cancellationToken = default)
    {
        ClaimValidation.Validate(request);
        return Task.FromResult(new ClaimSummary(request.ClaimNumber, request.PolicyNumber, request.Customer, request.Description, "Open", request.ReserveAmount, 0, request.LossDate.ToString("yyyy-MM-dd")));
    }

    public Task<ClaimSummary> UpdateClaimAsync(string claimNumber, UpdateClaimRequest request, string userId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Claim updates require a configured database.");
    public Task DeleteClaimAsync(string claimNumber, string userId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Claim deletion requires a configured database.");
}

internal static class ClaimValidation
{
    public static void Validate(CreateClaimRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClaimNumber) || request.ClaimNumber.Length > 32) throw new ArgumentException("Claim number is required and must be 32 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.PolicyNumber) || request.PolicyNumber.Length > 32) throw new ArgumentException("Policy number is required and must be 32 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.Customer) || request.Customer.Length > 200) throw new ArgumentException("Customer is required and must be 200 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 2000) throw new ArgumentException("Claim description is required and must be 2000 characters or fewer.");
        if (request.ReserveAmount <= 0) throw new ArgumentException("Reserve amount must be greater than zero.");
        if (request.LossDate > DateTimeOffset.UtcNow) throw new ArgumentException("Loss date cannot be in the future.");
    }
}
