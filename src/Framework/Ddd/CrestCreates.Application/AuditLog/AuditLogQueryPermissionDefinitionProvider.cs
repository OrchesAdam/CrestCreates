using CrestCreates.Authorization.Abstractions;

namespace CrestCreates.Application.AuditLog;

public sealed class AuditLogQueryPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetGroupOrNull("AuditLogging")
                    ?? context.AddGroup("AuditLogging", "Audit logging");
        group.AddPermission(AuditLoggingPermissions.Read, "Query audit logs");
    }
}