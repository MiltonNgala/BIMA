namespace Bima.Api.Application;

public enum Permission
{
    ReadRecords,
    WriteRecords,
    ManageClaims,
    ManageUsers,
    ReadAudit,
    ManageAttachments
}

public sealed class AccessContext
{
    public string UserId { get; set; } = "local-user";
    public string Role { get; set; } = "viewer";
    public HashSet<Permission> GrantedPermissions { get; } = [];

    public bool CanWrite => Role is "admin" or "underwriter" or "agent";

    public bool IsAdministrator => Role == "admin";

    public bool CanManageClaims => Role is "admin" or "underwriter";

    public bool HasPermission(Permission permission)
    {
        if (GrantedPermissions.Contains(permission)) return true;
        return permission switch
        {
            Permission.ReadRecords => Role is "admin" or "underwriter" or "agent" or "viewer",
            Permission.WriteRecords => CanWrite,
            Permission.ManageClaims => CanManageClaims,
            Permission.ManageUsers => IsAdministrator,
            Permission.ReadAudit => IsAdministrator,
            Permission.ManageAttachments => CanWrite,
            _ => false
        };
    }
}

public static class AccessControl
{
    public static void Require(AccessContext accessContext, Permission permission)
    {
        if (!accessContext.HasPermission(permission))
            throw new UnauthorizedAccessException($"The current role does not have the {permission} permission.");
    }

    public static void RequireWrite(AccessContext accessContext)
    {
        Require(accessContext, Permission.WriteRecords);
    }

    public static void RequireAdministrator(AccessContext accessContext)
    {
        Require(accessContext, Permission.ManageUsers);
    }

    public static void RequireClaimsManager(AccessContext accessContext)
    {
        Require(accessContext, Permission.ManageClaims);
    }
}
