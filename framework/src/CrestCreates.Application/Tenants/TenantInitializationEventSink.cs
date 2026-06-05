using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Application.Tenants;

public class TenantInitializationEventSink : ITenantInitializationEventSink
{
    private readonly ILogger<TenantInitializationEventSink> _logger;

    public TenantInitializationEventSink(ILogger<TenantInitializationEventSink> logger)
    {
        _logger = logger;
    }

    public Task PhaseStartedAsync(
        TenantInitializationContext context,
        string phaseName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Tenant initialization phase {PhaseName} started for tenant {TenantId}. CorrelationId: {CorrelationId}",
            phaseName, context.TenantId, context.CorrelationId);
        return Task.CompletedTask;
    }

    public Task PhaseSucceededAsync(
        TenantInitializationContext context,
        string phaseName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Tenant initialization phase {PhaseName} succeeded for tenant {TenantId}. CorrelationId: {CorrelationId}",
            phaseName, context.TenantId, context.CorrelationId);
        return Task.CompletedTask;
    }

    public Task PhaseFailedAsync(
        TenantInitializationContext context,
        string phaseName,
        string error,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Tenant initialization phase {PhaseName} failed for tenant {TenantId}. CorrelationId: {CorrelationId}. Error: {Error}",
            phaseName, context.TenantId, context.CorrelationId, error);
        return Task.CompletedTask;
    }

    public Task InfrastructureFailureAsync(
        TenantInitializationContext context,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        _logger.LogError(
            exception,
            "Infrastructure failure during tenant {TenantId} initialization. CorrelationId: {CorrelationId}",
            context.TenantId,
            context.CorrelationId);
        return Task.CompletedTask;
    }
}
