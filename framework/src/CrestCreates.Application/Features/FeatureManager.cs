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
    private readonly IFeatureAuditRecorder _featureAuditRecorder;

    public FeatureManager(
        IFeatureDefinitionManager featureDefinitionManager,
        IFeatureRepository featureRepository,
        IFeatureStore featureStore,
        FeatureValueTypeConverter featureValueTypeConverter,
        FeatureCacheInvalidator featureCacheInvalidator,
        IFeatureAuditRecorder featureAuditRecorder)
    {
        _featureDefinitionManager = featureDefinitionManager;
        _featureRepository = featureRepository;
        _featureStore = featureStore;
        _featureValueTypeConverter = featureValueTypeConverter;
        _featureCacheInvalidator = featureCacheInvalidator;
        _featureAuditRecorder = featureAuditRecorder;
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
            throw FeatureManagementExceptionFactory.InvalidValue(name, FeatureValueType.String, value);
        }

        var definition = EnsureDefinitionExists(name);
        EnsureScopeAllowed(definition, scope);
        var normalizedProviderKey = NormalizeProviderKey(scope, providerKey, tenantId);
        var normalizedTenantId = NormalizeTenantId(scope, tenantId);

        try
        {
            _featureValueTypeConverter.Validate(value, definition.ValueType, definition.Name);
        }
        catch (ArgumentException)
        {
            throw FeatureManagementExceptionFactory.InvalidValue(definition.Name, definition.ValueType, value);
        }

        var existing = await _featureRepository.FindAsync(
            definition.Name,
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);

        var oldValue = existing?.Value;
        var normalizedValue = NormalizeValue(value, definition.ValueType);

        if (existing == null)
        {
            await _featureRepository.InsertAsync(
                new FeatureValue(
                    Guid.NewGuid(),
                    definition.Name,
                    scope,
                    normalizedProviderKey,
                    normalizedValue,
                    normalizedTenantId),
                cancellationToken);
        }
        else
        {
            existing.SetValue(normalizedValue);
            await _featureRepository.UpdateAsync(existing, cancellationToken);
        }

        await _featureCacheInvalidator.InvalidateAsync(
            definition.Name,
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);

        await _featureAuditRecorder.RecordAsync(
            new FeatureAuditEntry
            {
                FeatureName = definition.Name,
                Scope = scope,
                TenantId = normalizedTenantId,
                OldValue = oldValue,
                NewValue = normalizedValue,
                Operation = "Set"
            },
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

        var oldValue = existing.Value;

        await _featureRepository.DeleteAsync(existing, cancellationToken);
        await _featureCacheInvalidator.InvalidateAsync(
            definition.Name,
            scope,
            normalizedProviderKey,
            normalizedTenantId,
            cancellationToken);

        await _featureAuditRecorder.RecordAsync(
            new FeatureAuditEntry
            {
                FeatureName = definition.Name,
                Scope = scope,
                TenantId = normalizedTenantId,
                OldValue = oldValue,
                NewValue = null,
                Operation = "Remove"
            },
            cancellationToken);
    }

    private FeatureDefinition EnsureDefinitionExists(string name)
    {
        return _featureDefinitionManager.GetOrNull(name)
               ?? throw FeatureManagementExceptionFactory.UndefinedFeature(name);
    }

    private static void EnsureScopeAllowed(FeatureDefinition definition, FeatureScope scope)
    {
        if (!definition.SupportsScope(scope))
        {
            throw FeatureManagementExceptionFactory.UnsupportedScope(definition.Name, scope);
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
