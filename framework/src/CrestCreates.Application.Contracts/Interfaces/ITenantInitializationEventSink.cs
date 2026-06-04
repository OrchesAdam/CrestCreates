using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;

namespace CrestCreates.Application.Contracts.Interfaces;

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
