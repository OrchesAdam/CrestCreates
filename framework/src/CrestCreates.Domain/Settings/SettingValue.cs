using System;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Domain.Settings;

public class SettingValue : AuditedAggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;

    public string? Value { get; private set; }

    public string ProviderType { get; private set; } = string.Empty;

    public SettingScope Scope { get; private set; }

    public string ProviderKey { get; private set; } = string.Empty;

    public string? TenantId { get; private set; }

    public bool IsEncrypted { get; private set; }

    public SettingValue()
    {
    }

    public SettingValue(
        Guid id,
        string name,
        SettingScope scope,
        string providerKey,
        string? value,
        bool isEncrypted,
        string? tenantId = null)
    {
        Id = id;
        SetIdentity(name, scope, providerKey, tenantId);
        SetValue(value, isEncrypted);
        CreationTime = DateTime.UtcNow;
    }

    public void SetIdentity(string name, SettingScope scope, string providerKey, string? tenantId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("设置名称不能为空", nameof(name));
        }

        Name = name.Trim();
        Scope = scope;
        ProviderType = scope.ToString();
        ProviderKey = NormalizeProviderKey(scope, providerKey, tenantId);
        TenantId = NormalizeTenantId(scope, tenantId);
    }

    public void SetValue(string? value, bool isEncrypted)
    {
        Value = value;
        IsEncrypted = isEncrypted;
        LastModificationTime = DateTime.UtcNow;
    }

    private static string NormalizeProviderKey(SettingScope scope, string providerKey, string? tenantId)
    {
        return scope switch
        {
            SettingScope.Global => string.Empty,
            SettingScope.Tenant => NormalizeRequired(tenantId ?? providerKey, nameof(providerKey)),
            SettingScope.User => NormalizeRequired(providerKey, nameof(providerKey)),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不支持的设置作用域")
        };
    }

    private static string? NormalizeTenantId(SettingScope scope, string? tenantId)
    {
        return scope switch
        {
            SettingScope.Global => null,
            SettingScope.Tenant => NormalizeRequired(tenantId, nameof(tenantId)),
            SettingScope.User => string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不支持的设置作用域")
        };
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("参数不能为空", parameterName);
        }

        return value.Trim();
    }
}
