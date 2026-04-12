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
