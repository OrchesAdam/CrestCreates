using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;

namespace CrestCreates.Application.Tenants;

/// <summary>
/// Internal persistence abstraction for the orchestrator.
/// Works with Domain entities; lives in Application, not Contracts.
/// </summary>
public interface ITenantInitializationStore
{
    /// <summary>
    /// Atomically transitions Pending/Failed → Initializing,
    /// computes AttemptNo, inserts a new TenantInitializationRecord.
    /// Returns null if the transition fails.
    /// </summary>
    Task<TenantInitializationRecord?> TryBeginInitializationAsync(
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically transitions Pending/Failed/Initializing → Initializing,
    /// computes AttemptNo, inserts a new recovery/retry record.
    /// Returns null if tenant is Initialized or transition conflicts.
    /// </summary>
    Task<TenantInitializationRecord?> ForceBeginInitializationAsync(
        Guid tenantId,
        string correlationId,
        string reason,
        CancellationToken cancellationToken);

    Task<TenantInitializationRecord?> GetLatestAsync(
        Guid tenantId,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        TenantInitializationRecord record,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically transitions Initializing → Failed on both the Tenant
    /// and the latest Initializing record. If no active record exists,
    /// creates a recovery record. Does NOT expose intermediate Initializing state.
    /// </summary>
    Task ForceFailAsync(
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically marks both Tenant and Record as Initialized in a single host-DB transaction.
    /// </summary>
    Task CompleteInitializationAsync(
        Guid tenantId,
        TenantInitializationRecord record,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically marks both Tenant and Record as Failed in a single host-DB transaction.
    /// </summary>
    Task FailInitializationAsync(
        Guid tenantId,
        TenantInitializationRecord record,
        string sanitizedError,
        CancellationToken cancellationToken);
}
