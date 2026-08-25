namespace Bima.Api.Domain;

public sealed class ClaimAttachment
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public required string TenantId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string StoragePath { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public required string UploadedBy { get; set; }
}
