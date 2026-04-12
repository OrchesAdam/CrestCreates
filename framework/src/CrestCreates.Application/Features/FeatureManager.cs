using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Application.Features;

public class FeatureManager : IFeatureManager
{
    private readonly IFeatureDefinitionManager _featureDefinitionManager;
    private readonly IFeatureRepository _featureRepository;
    private readonly IFeatureStore _featureStore;
    private readonly FeatureValueTypeConverter _featureValueTypeConverter;
    private readonly FeatureCacheInvalidator _featureCacheInvalidator;

    public FeatureManager(
        IFeatureDefinitionManager featureDefinitionManager,
        IFeatureRepository featureRepository,
        IFeatureStore featureStore,
        FeatureValueTypeConverter featureValueTypeConverter,
        FeatureCacheInvalidator featureCacheInvalidator)
    {
        _featureDefinitionManager = featureDefinitionManager;
        _featureRepository = featureRepository;
        _featureStore = featureStore;
        _featureValueTypeConverter = featureValueTypeConverter;
        _featureCacheInvalidator = featureCacheInvalidator;
    }

    public Task SetGlobalAsync(string name, string? value, CancellationToken cancellationToken = default)
    {
        return SetAsync(name, FeatureScope.Global, string.Empty, value, null, cancellationToken);
    }

    public Task SetTenantAsync(string name, string tenantId, string? value, CancellationToken cancellationToken = default)
    {
        return SetAsync(name, FeatureScope.Tenant, tenantId, value, tenantId, cancellationToken);
    }

    public Task RemoveGlobalAsync(string name, CancellationToken cancellationToken = default)
    {
        return RemoveAsync(name, FeatureScope.Global, string.Empty, null, cancellationToken);
    }

    public Task RemoveTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default)
    {
        return RemoveAsync(name, FeatureScope.Tenant, tenantId, tenantId, cancellationToken);
    }

    public Task<FeatureValueEntry?> GetScopedValueOrNullAsync(
        string name,
        FeatureScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureDefinitionExists(name);
        return _featureStore.GetOrNullAsync(name.Trim(), scope, providerKey, tenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<FeatureValueEntry>> GetScopedValuesAsync(
        FeatureScope scope,
        string providerKey,
        string? groupName = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var definitions = _featureDefinitionManager.GetAll()
            .Where(definition => definition.SupportsScope(scope))
            .Where(definition => string.IsNullOrWhiteSpace(groupName) ||
                                 string.Equals(definition.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        var values = await _featureStore.GetListAsync(scope, providerKey, tenantId, cancellationToken);
        return values
            .Where(value => definitions.ContainsKey(value.Name))
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task SetAsync(
        string name,
        FeatureScope scope,
        string providerKey,
        string? value,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        if (value == null)
        {
            throw new ArgumentException("功能特性值不能为空，请使用删除接口移除覆盖值", nameof(value));
        }

        var definition = EnsureDefinitionExists(name);
        EnsureScopeAllowed(definition, scope);
        var normalizedProviderKey = NormalizeProviderKey(scope, providerKey, tenantId);
        var normalizedTenantId = NormalizeTenantId(scope, tenantId);

        _featureValueTypeConverter.Validate(value, definition.ValueType, definition.Name);

        var existing = await _featureRepository.FindAsync(
            definition.Name,
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);

        if (existing == null)
        {
            await _featureRepository.InsertAsync(
                new FeatureValue(
                    Guid.NewGuid(),
                    definition.Name,
                    scope,
                    normalizedProviderKey,
                    NormalizeValue(value, definition.ValueType),
                    normalizedTenantId),
                cancellationToken);
        }
        else
        {
            existing.SetValue(NormalizeValue(value, definition.ValueType));
            await _featureRepository.UpdateAsync(existing, cancellationToken);
        }

        await _featureCacheInvalidator.InvalidateAsync(
            definition.Name,
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);
    }

    private async Task RemoveAsync(
        string name,
        FeatureScope scope,
        string providerKey,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var definition = EnsureDefinitionExists(name);
        EnsureScopeAllowed(definition, scope);
        var normalizedProviderKey = NormalizeProviderKey(scope, providerKey, tenantId);
        var normalizedTenantId = NormalizeTenantId(scope, tenantId);

        var existing = await _featureRepository.FindAsync(
            definition.Name,
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);

        if (existing == null)
        {
            return;
        }

        await _featureRepository.DeleteAsync(existing, cancellationToken);
        await _featureCacheInvalidator.InvalidateAsync(
            definition.Name,
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);
    }

    private FeatureDefinition EnsureDefinitionExists(string name)
    {
        return _featureDefinitionManager.GetOrNull(name)
               ?? throw new InvalidOperationException($"未定义的功能特性: {name}");
    }

    private static void EnsureScopeAllowed(FeatureDefinition definition, FeatureScope scope)
    {
        if (!definition.SupportsScope(scope))
        {
            throw new InvalidOperationException($"功能特性 '{definition.Name}' 不支持作用域 {scope}");
        }
    }

    private static string NormalizeProviderKey(FeatureScope scope, string providerKey, string? tenantId)
    {
        return scope switch
        {
            FeatureScope.Global => string.Empty,
            FeatureScope.Tenant => Require(tenantId ?? providerKey, nameof(providerKey)),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不支持的功能特性作用域")
        };
    }

    private static string? NormalizeTenantId(FeatureScope scope, string? tenantId)
    {
        return scope switch
        {
            FeatureScope.Global => null,
            FeatureScope.Tenant => Require(tenantId, nameof(tenantId)),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不支持的功能特性作用域")
        };
    }

    private static string Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("参数不能为空", parameterName);
        }

        return value.Trim();
    }

    private static string NormalizeValue(string value, FeatureValueType valueType)
    {
        return valueType switch
        {
            FeatureValueType.Bool when bool.TryParse(value, out var boolValue) => boolValue.ToString().ToLowerInvariant(),
            FeatureValueType.Int when int.TryParse(value, out _) => value.Trim(),
            FeatureValueType.String => value.Trim(),
            _ => value.Trim()
        };
    }
}
