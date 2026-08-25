using Bima.Api.Data;
using Bima.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bima.Api.Application;

public sealed record OrganizationSummary(string TenantId, string Name, bool IsActive);
public sealed record UpdateOrganizationRequest(string Name);
public sealed record UserPermissionSummary(Guid UserId, string Permission);

public sealed class OrganizationService(BimaDbContext db, TenantContext tenantContext, AuditService auditService)
{
    public async Task<OrganizationSummary> GetAsync(CancellationToken cancellationToken = default)
    {
        var organization = await EnsureOrganizationAsync(cancellationToken);
        return new OrganizationSummary(organization.TenantId, organization.Name, organization.IsActive);
    }

    public async Task<OrganizationSummary> UpdateAsync(UpdateOrganizationRequest request, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
            throw new ArgumentException("Organization name is required and must be 200 characters or fewer.");
        var organization = await EnsureOrganizationAsync(cancellationToken);
        organization.Name = request.Name.Trim();
        organization.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(userId, "organization.updated", "Organization", organization.TenantId, new { organization.Name }, cancellationToken);
        return new OrganizationSummary(organization.TenantId, organization.Name, organization.IsActive);
    }

    public async Task<IReadOnlyList<UserPermissionSummary>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.UserPermissions.AsNoTracking()
            .Where(permission => permission.TenantId == tenantContext.TenantId && permission.UserId == userId)
            .OrderBy(permission => permission.Permission)
            .Select(permission => new UserPermissionSummary(permission.UserId, permission.Permission))
            .ToListAsync(cancellationToken);

    public async Task GrantPermissionAsync(Guid userId, string permission, string actorId, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePermission(permission);
        if (!await db.Users.AnyAsync(user => user.Id == userId && user.TenantId == tenantContext.TenantId, cancellationToken))
            throw new KeyNotFoundException("User was not found in this organization.");
        if (!await db.UserPermissions.AnyAsync(item => item.TenantId == tenantContext.TenantId && item.UserId == userId && item.Permission == normalized, cancellationToken))
        {
            db.UserPermissions.Add(new UserPermission { TenantId = tenantContext.TenantId, UserId = userId, Permission = normalized });
            await db.SaveChangesAsync(cancellationToken);
            await auditService.RecordAsync(actorId, "user.permission_granted", "User", userId.ToString(), new { permission = normalized }, cancellationToken);
        }
    }

    public async Task RevokePermissionAsync(Guid userId, string permission, string actorId, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePermission(permission);
        var item = await db.UserPermissions.SingleOrDefaultAsync(permission => permission.TenantId == tenantContext.TenantId && permission.UserId == userId && permission.Permission == normalized, cancellationToken);
        if (item is null) return;
        db.UserPermissions.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(actorId, "user.permission_revoked", "User", userId.ToString(), new { permission = normalized }, cancellationToken);
    }

    private async Task<Organization> EnsureOrganizationAsync(CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.SingleOrDefaultAsync(item => item.TenantId == tenantContext.TenantId, cancellationToken);
        if (organization is not null) return organization;
        var now = DateTimeOffset.UtcNow;
        organization = new Organization { TenantId = tenantContext.TenantId, Name = tenantContext.TenantId, CreatedAt = now, UpdatedAt = now };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(cancellationToken);
        return organization;
    }

    private static string NormalizePermission(string permission)
    {
        var normalized = permission.Trim();
        if (!Enum.TryParse<Permission>(normalized, true, out var parsed))
            throw new ArgumentException("Permission is not supported.");
        return parsed.ToString();
    }
}
