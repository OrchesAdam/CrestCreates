using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Application.Tenants;

public class TenantSettingDefaultsSeeder : ITenantSettingDefaultsSeeder
{
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantSettingDefaultsSeeder> _logger;

    public TenantSettingDefaultsSeeder(
        ISettingDefinitionManager settingDefinitionManager,
        IServiceProvider serviceProvider,
        ILogger<TenantSettingDefaultsSeeder> logger)
    {
        _settingDefinitionManager = settingDefinitionManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<TenantSettingDefaultsResult> SeedAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Create a new scope so ISettingManager resolves its DbContext within
            // the ICurrentTenant context set by the orchestrator.
            using var scope = _serviceProvider.CreateScope();

            var settingManager = scope.ServiceProvider.GetRequiredService<ISettingManager>();
            var tenantId = context.TenantId.ToString();
            var definitions = _settingDefinitionManager.GetAll()
                .Where(d => d.SupportsScope(SettingScope.Tenant) && d.DefaultValue != null)
                .ToList();

            foreach (var definition in definitions)
            {
                var existing = await settingManager.GetScopedValueOrNullAsync(
                    definition.Name,
                    SettingScope.Tenant,
                    tenantId,
                    tenantId,
                    cancellationToken);

                if (existing == null)
                {
                    await settingManager.SetTenantAsync(
                        definition.Name,
                        tenantId,
                        definition.DefaultValue,
                        cancellationToken);
                }
            }

            _logger.LogInformation(
                "Seeded {Count} tenant setting defaults for tenant {TenantId}",
                definitions.Count, context.TenantId);

            return TenantSettingDefaultsResult.Succeeded();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to seed setting defaults for tenant {TenantId}: {Message}",
                context.TenantId, ex.Message);

            return TenantSettingDefaultsResult.Failed(ex.Message);
        }
    }
}
