using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Domain.Settings;

/// <summary>
/// 审计日志设置定义提供者
/// </summary>
public class AuditLoggingSettingDefinitionProvider : ISettingDefinitionProvider
{
    public void Define(SettingDefinitionContext context)
    {
        var auditGroup = context.GetOrAddGroup("AuditLogging", "审计日志设置", "审计日志相关运行时设置");

        auditGroup.AddDefinition(
            "AuditLogging.Enabled",
            "是否启用审计日志",
            "控制是否记录审计日志",
            bool.TrueString.ToLowerInvariant(),
            SettingValueType.Bool,
            false,
            SettingScope.Global | SettingScope.Tenant);

        auditGroup.AddDefinition(
            "AuditLogging.RetentionDays",
            "审计日志保留天数",
            "审计日志保留天数，超出此天数的日志将被清理",
            "90",
            SettingValueType.Int,
            false,
            SettingScope.Global);

        auditGroup.AddDefinition(
            AuditRetentionSettingNames.Enabled,
            "启用审计保留清理",
            "控制是否允许自动或手工执行正式审计保留清理；默认关闭",
            bool.FalseString.ToLowerInvariant(),
            SettingValueType.Bool,
            false,
            SettingScope.Global);

        auditGroup.AddDefinition(
            AuditRetentionSettingNames.BatchSize,
            "审计保留单批上限",
            "单次清理批次最多处理的正文条数，允许 1-1000",
            "500",
            SettingValueType.Int,
            false,
            SettingScope.Global);

        auditGroup.AddDefinition(
            AuditRetentionSettingNames.MaxBatchesPerRun,
            "审计保留单次批次数上限",
            "单次清理运行最多提交的批次数，允许 1-100",
            "20",
            SettingValueType.Int,
            false,
            SettingScope.Global);

        auditGroup.AddDefinition(
            AuditRetentionSettingNames.MaxRunSeconds,
            "审计保留单次运行秒数上限",
            "单次清理运行的时间预算，允许 1-300 秒",
            "60",
            SettingValueType.Int,
            false,
            SettingScope.Global);

        auditGroup.AddDefinition(
            "AuditLogging.RecordReturnValue",
            "是否记录返回值",
            "控制是否记录方法返回值",
            bool.FalseString.ToLowerInvariant(),
            SettingValueType.Bool,
            false,
            SettingScope.Global | SettingScope.Tenant);

        auditGroup.AddDefinition(
            "AuditLogging.RecordExceptionStackTrace",
            "是否记录异常堆栈",
            "控制是否记录异常堆栈信息",
            bool.TrueString.ToLowerInvariant(),
            SettingValueType.Bool,
            false,
            SettingScope.Global | SettingScope.Tenant);
    }
}
