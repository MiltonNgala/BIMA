using Bima.Api.Data;
using Bima.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bima.Api.Application;

public sealed record CustomerSummary(Guid Id, string Name, string Email, string Phone, string CustomerType);
public sealed record CreateCustomerRequest(string Name, string Email, string Phone, string CustomerType);

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerSummary>> GetCustomersAsync(CancellationToken cancellationToken = default);
    Task<CustomerSummary> CreateCustomerAsync(CreateCustomerRequest request, string userId, CancellationToken cancellationToken = default);
    Task DeleteCustomerAsync(Guid customerId, string userId, CancellationToken cancellationToken = default);
}

public sealed class DatabaseCustomerService(BimaDbContext db, TenantContext tenantContext, AuditService auditService) : ICustomerService
{
    public async Task<IReadOnlyList<CustomerSummary>> GetCustomersAsync(CancellationToken cancellationToken = default) =>
        await db.Customers.AsNoTracking()
            .Where(customer => customer.TenantId == tenantContext.TenantId)
            .OrderBy(customer => customer.Name)
            .Select(customer => new CustomerSummary(customer.Id, customer.Name, customer.Email, customer.Phone, customer.CustomerType))
            .ToListAsync(cancellationToken);

    public async Task<CustomerSummary> CreateCustomerAsync(CreateCustomerRequest request, string userId, CancellationToken cancellationToken = default)
    {
        CustomerValidation.Validate(request);
        if (await db.Customers.AnyAsync(customer => customer.TenantId == tenantContext.TenantId && customer.Email == request.Email, cancellationToken))
            throw new InvalidOperationException($"A customer with email {request.Email} already exists for this tenant.");

        var now = DateTimeOffset.UtcNow;
        var customer = new Customer
        {
            Id = Guid.NewGuid(), TenantId = tenantContext.TenantId, Name = request.Name,
            Email = request.Email, Phone = request.Phone, CustomerType = request.CustomerType,
            CreatedAt = now, CreatedBy = userId, UpdatedAt = now, UpdatedBy = userId
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(userId, "customer.created", "Customer", customer.Id.ToString(), new { customer.Email }, cancellationToken);
        return ToSummary(customer);
    }

    private static CustomerSummary ToSummary(Customer customer) => new(customer.Id, customer.Name, customer.Email, customer.Phone, customer.CustomerType);

    public async Task DeleteCustomerAsync(Guid customerId, string userId, CancellationToken cancellationToken = default)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(candidate => candidate.Id == customerId && candidate.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Customer was not found.");
        if (await db.Policies.AnyAsync(policy => policy.CustomerId == customerId && policy.TenantId == tenantContext.TenantId, cancellationToken))
            throw new InvalidOperationException("A customer with policies cannot be deleted.");
        db.Customers.Remove(customer);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(userId, "customer.deleted", "Customer", customer.Id.ToString(), new { customer.Email }, cancellationToken);
    }
}

public sealed class SampleCustomerService : ICustomerService
{
    private readonly List<CustomerSummary> customers = [];

    public Task<IReadOnlyList<CustomerSummary>> GetCustomersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CustomerSummary>>(customers);

    public Task<CustomerSummary> CreateCustomerAsync(CreateCustomerRequest request, string userId, CancellationToken cancellationToken = default)
    {
        CustomerValidation.Validate(request);
        var customer = new CustomerSummary(Guid.NewGuid(), request.Name, request.Email, request.Phone, request.CustomerType);
        customers.Add(customer);
        return Task.FromResult(customer);
    }

    public Task DeleteCustomerAsync(Guid customerId, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException("Customer deletion requires a configured database.");
}

internal static class CustomerValidation
{
    public static void Validate(CreateCustomerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200) throw new ArgumentException("Customer name is required and must be 200 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@') || request.Email.Length > 200) throw new ArgumentException("A valid customer email is required.");
        if (string.IsNullOrWhiteSpace(request.Phone) || request.Phone.Length > 40) throw new ArgumentException("Customer phone is required and must be 40 characters or fewer.");
        if (request.CustomerType is not ("Individual" or "Business")) throw new ArgumentException("Customer type must be Individual or Business.");
    }
}
