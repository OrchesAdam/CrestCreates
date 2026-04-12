using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Features;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Application.Features;

public class FeatureChecker : IFeatureChecker
{
    private readonly IFeatureDefinitionManager _featureDefinitionManager;
    private readonly IFeatureValueResolver _featureValueResolver;
    private readonly ICurrentTenant _currentTenant;

    public FeatureChecker(
        IFeatureDefinitionManager featureDefinitionManager,
        IFeatureValueResolver featureValueResolver,
        ICurrentTenant currentTenant)
    {
        _featureDefinitionManager = featureDefinitionManager;
        _featureValueResolver = featureValueResolver;
        _currentTenant = currentTenant;
    }

    public async Task<bool> IsEnabledAsync(string featureName, CancellationToken cancellationToken = default)
    {
        var tenantId = GetCurrentTenantIdOrNull();
        return await IsEnabledInternalAsync(tenantId, featureName, cancellationToken);
    }

    public async Task<bool> IsEnabledAsync(string tenantId, string featureName, CancellationToken cancellationToken = default)
    {
        return await IsEnabledInternalAsync(tenantId, featureName, cancellationToken);
    }

    private async Task<bool> IsEnabledInternalAsync(string? tenantId, string featureName, CancellationToken cancellationToken)
    {
        var definition = _featureDefinitionManager.GetOrNull(featureName)
                         ?? throw new InvalidOperationException($"未定义的功能特性: {featureName}");

        if (!string.IsNullOrWhiteSpace(tenantId) && !definition.SupportsScope(FeatureScope.Tenant))
        {
            throw new InvalidOperationException($"功能特性 '{featureName}' 不支持租户作用域");
        }

        var resolved = await _featureValueResolver.ResolveAsync(featureName, tenantId, cancellationToken);
        return ParseBoolValue(resolved.Value, definition);
    }

    private string? GetCurrentTenantIdOrNull()
    {
        return string.IsNullOrWhiteSpace(_currentTenant.Id) ? null : _currentTenant.Id;
    }

    private static bool ParseBoolValue(string? value, FeatureDefinition definition)
    {
        if (value == null)
        {
            if (definition.ValueType != FeatureValueType.Bool)
            {
                return false;
            }

            var defaultValue = definition.GetNormalizedDefaultValue();
            return defaultValue != null && bool.Parse(defaultValue);
        }

        return bool.TryParse(value, out var result) && result;
    }
}
