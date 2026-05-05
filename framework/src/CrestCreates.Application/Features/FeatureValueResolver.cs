using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Application.Features;

public class FeatureValueResolver : IFeatureValueResolver
{
    private readonly IFeatureDefinitionManager _featureDefinitionManager;
    private readonly IFeatureStore _featureStore;

    public FeatureValueResolver(
        IFeatureDefinitionManager featureDefinitionManager,
        IFeatureStore featureStore)
    {
        _featureDefinitionManager = featureDefinitionManager;
        _featureStore = featureStore;
    }

    public async Task<ResolvedFeatureValue> ResolveAsync(
        string name,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var definition = _featureDefinitionManager.GetOrNull(name)
                         ?? throw FeatureManagementExceptionFactory.UndefinedFeature(name);

        var values = await ResolveValuesAsync([definition], tenantId, cancellationToken);
        return values[0];
    }

    public async Task<IReadOnlyList<ResolvedFeatureValue>> ResolveAllAsync(
        string? groupName = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var definitions = _featureDefinitionManager.GetAll()
            .Where(definition => string.IsNullOrWhiteSpace(groupName) ||
                                 string.Equals(definition.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return await ResolveValuesAsync(definitions, tenantId, cancellationToken);
    }

    private async Task<IReadOnlyList<ResolvedFeatureValue>> ResolveValuesAsync(
        IReadOnlyList<FeatureDefinition> definitions,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var globalValues = await _featureStore.GetListAsync(FeatureScope.Global, string.Empty, cancellationToken: cancellationToken);
        var tenantValues = string.IsNullOrWhiteSpace(tenantId)
            ? Array.Empty<FeatureValueEntry>()
            : (await _featureStore.GetListAsync(FeatureScope.Tenant, tenantId.Trim(), tenantId.Trim(), cancellationToken)).ToArray();

        var globalLookup = globalValues.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var tenantLookup = tenantValues.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

        return definitions
            .Select(definition => ResolveValue(definition, tenantLookup, globalLookup))
            .ToArray();
    }

    private static ResolvedFeatureValue ResolveValue(
        FeatureDefinition definition,
        IReadOnlyDictionary<string, FeatureValueEntry> tenantLookup,
        IReadOnlyDictionary<string, FeatureValueEntry> globalLookup)
    {
        if (tenantLookup.TryGetValue(definition.Name, out var tenantValue))
        {
            return new ResolvedFeatureValue
            {
                Name = definition.Name,
                Value = tenantValue.Value,
                Scope = FeatureScope.Tenant,
                ProviderKey = tenantValue.ProviderKey,
                TenantId = tenantValue.TenantId
            };
        }

        if (globalLookup.TryGetValue(definition.Name, out var globalValue))
        {
            return new ResolvedFeatureValue
            {
                Name = definition.Name,
                Value = globalValue.Value,
                Scope = FeatureScope.Global,
                ProviderKey = globalValue.ProviderKey,
                TenantId = null
            };
        }

        return new ResolvedFeatureValue
        {
            Name = definition.Name,
            Value = definition.GetNormalizedDefaultValue(),
            Scope = null,
            ProviderKey = null,
            TenantId = null
        };
    }
}
