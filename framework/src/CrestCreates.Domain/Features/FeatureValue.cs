using System;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Domain.Features;

public class FeatureValue : AuditedAggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;

    public string? Value { get; private set; }

    public FeatureScope Scope { get; private set; }

    public string ProviderKey { get; private set; } = string.Empty;

    public string? TenantId { get; private set; }

    public FeatureValue()
    {
    }

    public FeatureValue(
        Guid id,
        string name,
        FeatureScope scope,
        string providerKey,
        string? value,
        string? tenantId = null)
    {
        Id = id;
        SetIdentity(name, scope, providerKey, tenantId);
        SetValue(value);
        CreationTime = DateTime.UtcNow;
    }

    public void SetIdentity(string name, FeatureScope scope, string providerKey, string? tenantId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("功能特性名称不能为空", nameof(name));
        }

        Name = name.Trim();
        Scope = scope;
        ProviderKey = NormalizeProviderKey(scope, providerKey, tenantId);
        TenantId = NormalizeTenantId(scope, tenantId);
    }

    public void SetValue(string? value)
    {
        Value = value;
        LastModificationTime = DateTime.UtcNow;
    }

    private static string NormalizeProviderKey(FeatureScope scope, string providerKey, string? tenantId)
    {
        return scope switch
        {
            FeatureScope.Global => string.Empty,
            FeatureScope.Tenant => NormalizeRequired(tenantId ?? providerKey, nameof(providerKey)),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不支持的功能特性作用域")
        };
    }

    private static string? NormalizeTenantId(FeatureScope scope, string? tenantId)
    {
        return scope switch
        {
            FeatureScope.Global => null,
            FeatureScope.Tenant => NormalizeRequired(tenantId, nameof(tenantId)),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不支持的功能特性作用域")
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
