using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Domain.Settings;

public class CoreSettingDefinitionProvider : ISettingDefinitionProvider
{
    public void Define(SettingDefinitionContext context)
    {
        var appGroup = context.GetOrAddGroup("App", "应用设置", "应用级运行时设置");
        appGroup.AddDefinition(
            "App.DisplayName",
            "应用显示名称",
            "系统运行时显示名称",
            "CrestCreates",
            SettingValueType.String,
            false,
            SettingScope.Global | SettingScope.Tenant);
        appGroup.AddDefinition(
            "App.IsRegistrationEnabled",
            "是否允许注册",
            "控制是否允许新用户注册",
            bool.TrueString.ToLowerInvariant(),
            SettingValueType.Bool,
            false,
            SettingScope.Global | SettingScope.Tenant);

        var securityGroup = context.GetOrAddGroup("Security", "安全设置", "安全相关运行时设置");
        securityGroup.AddDefinition(
            "Security.Password.MinLength",
            "密码最小长度",
            "密码策略最小长度",
            "8",
            SettingValueType.Int,
            false,
            SettingScope.Global);

        var storageGroup = context.GetOrAddGroup("Storage", "存储设置", "存储相关运行时设置");
        storageGroup.AddDefinition(
            "Storage.DefaultProvider",
            "默认存储提供程序",
            "默认文件存储提供程序",
            "Local",
            SettingValueType.String,
            false,
            SettingScope.Global | SettingScope.Tenant);
        storageGroup.AddDefinition(
            "Storage.SecretKey",
            "存储密钥",
            "存储提供程序密钥",
            string.Empty,
            SettingValueType.String,
            true,
            SettingScope.Global | SettingScope.Tenant);
    }
}
