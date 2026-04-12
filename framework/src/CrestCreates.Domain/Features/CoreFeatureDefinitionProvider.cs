using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Domain.Features;

public class CoreFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    public void Define(FeatureDefinitionContext context)
    {
        var identityGroup = context.GetOrAddGroup("Identity", "身份认证");
        identityGroup.AddDefinition(
            "Identity.UserCreationEnabled",
            "启用用户创建",
            "控制是否允许创建用户账户",
            "true",
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        var fileManagementGroup = context.GetOrAddGroup("FileManagement", "文件管理");
        fileManagementGroup.AddDefinition(
            "FileManagement.Enabled",
            "启用文件管理",
            "控制是否启用文件管理功能",
            "true",
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        var auditLoggingGroup = context.GetOrAddGroup("AuditLogging", "审计日志");
        auditLoggingGroup.AddDefinition(
            "AuditLogging.Enabled",
            "启用审计日志",
            "控制是否启用审计日志功能",
            "true",
            FeatureValueType.Bool,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        var storageGroup = context.GetOrAddGroup("Storage", "存储");
        storageGroup.AddDefinition(
            "Storage.MaxFileCount",
            "最大文件数量",
            "允许存储的最大文件数量",
            "100",
            FeatureValueType.Int,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);

        var uiGroup = context.GetOrAddGroup("UI", "用户界面");
        uiGroup.AddDefinition(
            "Ui.Theme",
            "界面主题",
            "控制界面使用的主题",
            "Default",
            FeatureValueType.String,
            true,
            true,
            FeatureScope.Global | FeatureScope.Tenant);
    }
}
