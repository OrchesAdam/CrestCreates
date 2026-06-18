using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantInitializationEventSink
{
    Task PhaseStartedAsync(
        TenantInitializationContext context,
        string phaseName,
        CancellationToken cancellationToken = default);

    Task PhaseSucceededAsync(
        TenantInitializationContext context,
        string phaseName,
        CancellationToken cancellationToken = default);

    Task PhaseFailedAsync(
        TenantInitializationContext context,
        string phaseName,
        string error,
        CancellationToken cancellationToken = default);

    Task InfrastructureFailureAsync(
        TenantInitializationContext context,
        Exception exception,
        CancellationToken cancellationToken = default);
}
