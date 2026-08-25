using Bima.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bima.Api.Application;

public sealed record UserSummary(Guid Id, string Email, string DisplayName, string Role, bool IsActive);
public sealed record ChangeRoleRequest(string Role);

public sealed class UserService(BimaDbContext db, TenantContext tenantContext, AccessContext accessContext, AuditService auditService)
{
    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking()
            .Where(user => user.TenantId == tenantContext.TenantId)
            .OrderBy(user => user.Email)
            .Select(user => new UserSummary(user.Id, user.Email, user.DisplayName, user.Role, user.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<UserSummary> ChangeRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var normalizedRole = role.Trim().ToLowerInvariant();
        if (normalizedRole is not ("admin" or "underwriter" or "agent" or "viewer"))
            throw new ArgumentException("Role must be admin, underwriter, agent, or viewer.");

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId && candidate.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("User was not found in this tenant.");
        user.Role = normalizedRole;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(accessContext.UserId, "user.role_changed", "User", user.Id.ToString(), new { user.Role }, cancellationToken);
        return new UserSummary(user.Id, user.Email, user.DisplayName, user.Role, user.IsActive);
    }
}
