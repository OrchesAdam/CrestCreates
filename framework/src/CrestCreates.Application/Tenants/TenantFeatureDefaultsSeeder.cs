using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Application.Tenants;

public class TenantFeatureDefaultsSeeder : ITenantFeatureDefaultsSeeder
{
    private readonly IFeatureDefinitionManager _featureDefinitionManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantFeatureDefaultsSeeder> _logger;

    public TenantFeatureDefaultsSeeder(
        IFeatureDefinitionManager featureDefinitionManager,
        IServiceProvider serviceProvider,
        ILogger<TenantFeatureDefaultsSeeder> logger)
    {
        _featureDefinitionManager = featureDefinitionManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<TenantFeatureDefaultsResult> SeedAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var featureManager = _serviceProvider.GetRequiredService<IFeatureManager>();
            var tenantId = context.TenantId.ToString();
            var definitions = _featureDefinitionManager.GetAll()
                .Where(d => d.SupportsScope(FeatureScope.Tenant) && d.DefaultValue != null)
                .ToList();

            foreach (var definition in definitions)
            {
                var existing = await featureManager.GetScopedValueOrNullAsync(
                    definition.Name,
                    FeatureScope.Tenant,
                    tenantId,
                    tenantId,
                    cancellationToken);

                if (existing == null)
                {
                    await featureManager.SetTenantAsync(
                        definition.Name,
                        tenantId,
                        definition.GetNormalizedDefaultValue(),
                        cancellationToken);
                }
            }

            _logger.LogInformation(
                "Seeded {Count} tenant feature defaults for tenant {TenantId}",
                definitions.Count, context.TenantId);

            return TenantFeatureDefaultsResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to seed feature defaults for tenant {TenantId}: {Message}",
                context.TenantId, ex.Message);

            return TenantFeatureDefaultsResult.Failed(ex.Message);
        }
    }
}
