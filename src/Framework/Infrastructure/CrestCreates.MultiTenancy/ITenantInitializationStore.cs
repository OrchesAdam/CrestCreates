using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;

namespace CrestCreates.MultiTenancy;

public interface ITenantInitializationStore
{
    Task<TenantInitializationRecord?> TryBeginInitializationAsync(
        Guid tenantId, string correlationId, CancellationToken cancellationToken = default);

    Task<TenantInitializationRecord?> ForceBeginInitializationAsync(
        Guid tenantId, string correlationId, string reason, CancellationToken cancellationToken = default);

    Task<TenantInitializationRecord?> GetLatestAsync(
        Guid tenantId, CancellationToken cancellationToken = default);

    Task UpdateAsync(
        TenantInitializationRecord record, CancellationToken cancellationToken = default);

    Task ForceFailAsync(
        Guid tenantId, string correlationId, CancellationToken cancellationToken = default);

    Task CompleteInitializationAsync(
        Guid tenantId, TenantInitializationRecord record, CancellationToken cancellationToken = default);

    Task FailInitializationAsync(
        Guid tenantId, TenantInitializationRecord record, string error, CancellationToken cancellationToken = default);
}
