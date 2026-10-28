using CrestCreates.Application.Contracts.AuditRetention;
using CrestCreates.Authorization.Abstractions;

namespace CrestCreates.Application.AuditLog;

public sealed class AuditRetentionPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetGroupOrNull("AuditLogging") ?? context.AddGroup("AuditLogging", "Audit logging");
        group.AddPermission(AuditRetentionPermissions.Preview, "Preview audit retention candidates");
        group.AddPermission(AuditRetentionPermissions.Execute, "Execute audit retention runs");
        group.AddPermission(AuditRetentionPermissions.ReadRuns, "Read audit retention run facts");
        group.AddPermission(AuditRetentionPermissions.Export, "Re-export committed audit retention receipts");
        group.AddPermission(AuditRetentionPermissions.ManageHolds, "Place and release audit retention holds");
    }
}
