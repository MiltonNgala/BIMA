using System.Text.Json;
using Bima.Api.Data;
using Bima.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bima.Api.Application;

public sealed record AuditSummary(Guid Id, string UserId, string Action, string EntityType, string? EntityId, DateTimeOffset CreatedAt, string? Metadata);

public sealed class AuditService(BimaDbContext db, TenantContext tenantContext)
{
    public async Task RecordAsync(string userId, string action, string entityType, string? entityId = null, object? metadata = null, CancellationToken cancellationToken = default)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(), TenantId = tenantContext.TenantId, UserId = userId,
            Action = action, EntityType = entityType, EntityId = entityId,
            CreatedAt = DateTimeOffset.UtcNow,
            Metadata = metadata is null ? null : JsonSerializer.Serialize(metadata)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditSummary>> GetAsync(CancellationToken cancellationToken = default) =>
        await db.AuditEvents.AsNoTracking()
            .Where(audit => audit.TenantId == tenantContext.TenantId)
            .OrderByDescending(audit => audit.CreatedAt)
            .Take(200)
            .Select(audit => new AuditSummary(audit.Id, audit.UserId, audit.Action, audit.EntityType, audit.EntityId, audit.CreatedAt, audit.Metadata))
            .ToListAsync(cancellationToken);
}
