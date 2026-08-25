namespace Bima.Api.Domain;

public sealed class UserPermission
{
    public required string TenantId { get; set; }
    public Guid UserId { get; set; }
    public required string Permission { get; set; }
}
