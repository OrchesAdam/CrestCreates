using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Logging;

namespace CrestCreates.MultiTenancy;

public class TenantLifecycleService
{
    private readonly TenantManager _tenantManager;
    private readonly ITenantInitializationOrchestrator _orchestrator;
    private readonly ILogger<TenantLifecycleService> _logger;

    public TenantLifecycleService(
        TenantManager tenantManager,
        ITenantInitializationOrchestrator orchestrator,
        ILogger<TenantLifecycleService> logger)
    {
        _tenantManager = tenantManager;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task<TenantInitializationResult> CreateAndInitializeAsync(
        string tenantName,
        string? connectionString,
        Guid? requestedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantManager.CreateAsync(tenantName, tenantName, connectionString);

        var context = new TenantInitializationContext
        {
            TenantId = tenant.Id,
            TenantName = tenant.Name,
            ConnectionString = connectionString,
            CorrelationId = Guid.NewGuid().ToString("N"),
            RequestedByUserId = requestedByUserId
        };

        _logger.LogInformation("Starting tenant initialization for {TenantName} ({TenantId})", tenantName, tenant.Id);
        return await _orchestrator.InitializeAsync(context, cancellationToken);
    }
}
