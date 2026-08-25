using Bima.Api.Data;
using Bima.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bima.Api.Application;

public sealed record AttachmentSummary(Guid Id, string FileName, string ContentType, long SizeBytes, string UploadedAt);
public sealed record AttachmentFile(string FileName, string ContentType, Stream Content);

public sealed class ClaimAttachmentService(BimaDbContext db, TenantContext tenantContext, AuditService auditService, IWebHostEnvironment environment)
{
    private const long MaxFileSize = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedContentTypes = ["application/pdf", "image/jpeg", "image/png"];

    public async Task<IReadOnlyList<AttachmentSummary>> GetAsync(string claimNumber, CancellationToken cancellationToken = default) =>
        await db.ClaimAttachments.AsNoTracking()
            .Where(attachment => attachment.TenantId == tenantContext.TenantId && db.Claims.Any(claim => claim.Id == attachment.ClaimId && claim.ClaimNumber == claimNumber && claim.TenantId == tenantContext.TenantId))
            .OrderByDescending(attachment => attachment.UploadedAt)
            .Select(attachment => new AttachmentSummary(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.UploadedAt.ToString("O")))
            .ToListAsync(cancellationToken);

    public async Task<AttachmentSummary> AddAsync(string claimNumber, IFormFile file, string userId, CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0 || file.Length > MaxFileSize) throw new ArgumentException("Attachment must be between 1 byte and 10 MB.");
        if (!AllowedContentTypes.Contains(file.ContentType)) throw new ArgumentException("Only PDF, JPEG, and PNG attachments are supported.");
        var claim = await db.Claims.SingleOrDefaultAsync(candidate => candidate.TenantId == tenantContext.TenantId && candidate.ClaimNumber == claimNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Claim was not found.");
        var attachmentId = Guid.NewGuid();
        var safeName = Path.GetFileName(file.FileName);
        var relativePath = Path.Combine("App_Data", "attachments", tenantContext.TenantId, claim.Id.ToString(), $"{attachmentId:N}-{safeName}");
        var absolutePath = Path.Combine(environment.ContentRootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await using (var stream = File.Create(absolutePath)) await file.CopyToAsync(stream, cancellationToken);
        var attachment = new ClaimAttachment { Id = attachmentId, ClaimId = claim.Id, TenantId = tenantContext.TenantId, FileName = safeName, ContentType = file.ContentType, SizeBytes = file.Length, StoragePath = relativePath, UploadedAt = DateTimeOffset.UtcNow, UploadedBy = userId };
        db.ClaimAttachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(userId, "claim.attachment_added", "Claim", claim.Id.ToString(), new { attachment.FileName }, cancellationToken);
        return new AttachmentSummary(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.UploadedAt.ToString("O"));
    }

    public async Task<AttachmentFile> OpenAsync(string claimNumber, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await FindAsync(claimNumber, attachmentId, cancellationToken);
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "attachments"));
        var path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, attachment.StoragePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Attachment path is invalid.");
        if (!File.Exists(path)) throw new FileNotFoundException("Attachment content was not found.");
        return new AttachmentFile(attachment.FileName, attachment.ContentType, File.OpenRead(path));
    }

    public async Task DeleteAsync(string claimNumber, Guid attachmentId, string userId, CancellationToken cancellationToken = default)
    {
        var attachment = await FindAsync(claimNumber, attachmentId, cancellationToken);
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "attachments"));
        var path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, attachment.StoragePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Attachment path is invalid.");
        db.ClaimAttachments.Remove(attachment);
        await db.SaveChangesAsync(cancellationToken);
        if (File.Exists(path)) File.Delete(path);
        await auditService.RecordAsync(userId, "claim.attachment_deleted", "Claim", attachment.ClaimId.ToString(), new { attachment.FileName }, cancellationToken);
    }

    private async Task<ClaimAttachment> FindAsync(string claimNumber, Guid attachmentId, CancellationToken cancellationToken) =>
        await db.ClaimAttachments.SingleOrDefaultAsync(attachment => attachment.Id == attachmentId && attachment.TenantId == tenantContext.TenantId && db.Claims.Any(claim => claim.Id == attachment.ClaimId && claim.ClaimNumber == claimNumber && claim.TenantId == tenantContext.TenantId), cancellationToken)
        ?? throw new KeyNotFoundException("Attachment was not found.");
}
