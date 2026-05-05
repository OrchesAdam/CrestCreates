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
            // Create a new scope so IFeatureManager resolves its DbContext within
            // the ICurrentTenant context set by the orchestrator.
            using var scope = _serviceProvider.CreateScope();

            var featureManager = scope.ServiceProvider.GetRequiredService<IFeatureManager>();
            var tenantId = context.TenantId.ToString();
            var definitions = _featureDefinitionManager.GetAll()
                .Where(definition => definition.SupportsScope(FeatureScope.Tenant))
                .ToArray();

            foreach (var definition in definitions)
            {
                var existing = await featureManager.GetScopedValueOrNullAsync(
                    definition.Name,
                    FeatureScope.Tenant,
                    tenantId,
                    tenantId,
                    cancellationToken);

                if (existing is not null)
                {
                    continue;
                }

                // Default behavior is lazy fallback. Current definitions do not declare explicit tenant
                // defaults, so no write is needed.
            }

            _logger.LogInformation(
                "Checked {Count} tenant feature definitions for tenant {TenantId}",
                definitions.Length, context.TenantId);

            return TenantFeatureDefaultsResult.Succeeded();
        }
        catch (OperationCanceledException)
        {
            throw;
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
