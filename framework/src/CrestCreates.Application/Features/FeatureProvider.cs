using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Features;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Application.Features;

public class FeatureProvider : IFeatureProvider
{
    private readonly IFeatureDefinitionManager _featureDefinitionManager;
    private readonly IFeatureValueResolver _featureValueResolver;
    private readonly FeatureValueTypeConverter _featureValueTypeConverter;
    private readonly ICurrentTenant _currentTenant;

    public FeatureProvider(
        IFeatureDefinitionManager featureDefinitionManager,
        IFeatureValueResolver featureValueResolver,
        FeatureValueTypeConverter featureValueTypeConverter,
        ICurrentTenant currentTenant)
    {
        _featureDefinitionManager = featureDefinitionManager;
        _featureValueResolver = featureValueResolver;
        _featureValueTypeConverter = featureValueTypeConverter;
        _currentTenant = currentTenant;
    }

    public async Task<string?> GetOrNullAsync(string name, CancellationToken cancellationToken = default)
    {
        var result = await _featureValueResolver.ResolveAsync(
            name,
            GetCurrentTenantIdOrNull(),
            cancellationToken);

        return result.Value;
    }

    public async Task<T?> GetAsync<T>(string name, CancellationToken cancellationToken = default)
    {
        var definition = _featureDefinitionManager.GetOrNull(name)
                         ?? throw new InvalidOperationException($"未定义的功能特性: {name}");

        var value = await GetOrNullAsync(name, cancellationToken);
        return _featureValueTypeConverter.ConvertTo<T>(value, definition.ValueType);
    }

    public Task<IReadOnlyList<ResolvedFeatureValue>> GetAllAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default)
    {
        return _featureValueResolver.ResolveAllAsync(
            groupName,
            GetCurrentTenantIdOrNull(),
            cancellationToken);
    }

    private string? GetCurrentTenantIdOrNull()
    {
        return string.IsNullOrWhiteSpace(_currentTenant.Id) ? null : _currentTenant.Id;
    }
}
