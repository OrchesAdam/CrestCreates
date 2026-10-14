using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Minimal local context bridge over the existing tenant (ICurrentTenant/ITenantProvider) and
/// Accountability primitives. Capture validates consistency at the publish/enqueue boundary;
/// restore separates the async tenant resolution from the synchronous ambient activation performed
/// in the real execution frame (never inside an async helper), and every dispose step is
/// independently protected.
/// </summary>
internal static class LocalEventContextBridge
{
    private static readonly AuditActor BackgroundActor = new()
    {
        Kind = "system",
        Id = "local-event-execution",
    };

    public static LocalEventExecutionContextSnapshot Capture(IServiceProvider provider)
    {
        var currentTenant = provider.GetService<ICurrentTenant>();
        var tenantProvider = provider.GetService<ITenantProvider>();
        var auditContext = provider.GetService<IAuditOperationContextAccessor>()?.Current;
        var currentTenantId = currentTenant?.Id;

        if (!string.IsNullOrEmpty(auditContext?.TenantId))
        {
            // Audit and current-tenant are two views of one fact: a contradiction must fail the
            // capture instead of being merged into a fabricated snapshot.
            if (!string.Equals(auditContext!.TenantId, currentTenantId, StringComparison.Ordinal))
            {
                throw new LocalEventContextCaptureException(
                    "AuditTenantMismatch",
                    $"the ambient audit context tenant '{auditContext.TenantId}' differs from the current tenant " +
                    $"'{currentTenantId}'; the snapshot must not combine two conflicting sources.");
            }

            if (currentTenant is null || tenantProvider is null)
            {
                throw new LocalEventContextCaptureException(
                    "MissingTenantFacility",
                    $"the ambient audit context carries tenant '{auditContext.TenantId}' but this host has no tenant " +
                    "restore facility; a captured tenant that cannot be restored must not be persisted.");
            }
        }

        return LocalEventExecutionContextSnapshot.Capture(currentTenantId, auditContext?.CorrelationId);
    }

    /// <summary>
    /// Async phase: validates the persisted snapshot against the binding policy and resolves the
    /// tenant through the authoritative provider. Never mutates ambient state.
    /// </summary>
    public static async Task<LocalEventContextResolution> ResolveAsync(
        IServiceProvider provider,
        LocalEventBinding binding,
        LocalEventExecutionContextSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.ContextVersion != LocalEventExecutionContextSnapshot.CurrentContextVersion)
        {
            if (snapshot.ContextVersion is not null)
            {
                throw new LocalEventContextRestoreException(
                    "UnknownContextVersion",
                    $"event '{binding.LocalName}' carries unsupported context version v{snapshot.ContextVersion}; " +
                    "the record is preserved and refused.");
            }

            if (string.IsNullOrEmpty(snapshot.TenantId) && !binding.AllowHistoricalHostContext)
            {
                throw new LocalEventContextRestoreException(
                    "HistoricalContextNotRecognized",
                    $"event '{binding.LocalName}' carries a legacy row without a context snapshot; executing it as " +
                    "host context requires the explicit per-event historical-host migration policy.");
            }
        }

        // Missing and invalid tenant ids are handled uniformly: an empty id is never a valid tenant
        // and is never converted into an executable host.
        switch (binding.TenantMode)
        {
            case LocalEventTenantMode.HostOnly when !string.IsNullOrEmpty(snapshot.TenantId):
                throw new LocalEventContextRestoreException(
                    "TenantModeHostOnly",
                    $"event '{binding.LocalName}' is host-only but the record carries tenant '{snapshot.TenantId}'.");
            case LocalEventTenantMode.Required when string.IsNullOrEmpty(snapshot.TenantId):
                throw new LocalEventContextRestoreException(
                    "TenantModeRequired",
                    $"event '{binding.LocalName}' requires a tenant but the record carries no valid tenant id.");
        }

        ITenantInfo? tenant = null;
        if (!string.IsNullOrEmpty(snapshot.TenantId))
        {
            var tenantProvider = provider.GetService<ITenantProvider>()
                ?? throw new LocalEventContextRestoreException(
                    "MissingTenantProvider",
                    $"the record of event '{binding.LocalName}' carries tenant '{snapshot.TenantId}' but this host has " +
                    "no ITenantProvider; the tenant cannot be validated and the attempt is refused.");

            if (provider.GetService<ICurrentTenant>() is null)
            {
                throw new LocalEventContextRestoreException(
                    "MissingCurrentTenant",
                    $"the record of event '{binding.LocalName}' carries tenant '{snapshot.TenantId}' but this host has " +
                    "no ICurrentTenant; the tenant cannot be activated and the attempt is refused.");
            }

            tenant = await tenantProvider.GetTenantAsync(snapshot.TenantId, cancellationToken).ConfigureAwait(false);
            if (tenant is null)
            {
                throw new LocalEventContextRestoreException(
                    "UnknownTenant",
                    $"tenant '{snapshot.TenantId}' of event '{binding.LocalName}' cannot be resolved; unknown or " +
                    "inactive tenants are refused and never fall back to host execution.");
            }

            if (!string.Equals(tenant.Id, snapshot.TenantId, StringComparison.Ordinal))
            {
                throw new LocalEventContextRestoreException(
                    "TenantIdMismatch",
                    $"tenant provider resolved id '{tenant.Id}' for requested '{snapshot.TenantId}' of event " +
                    $"'{binding.LocalName}'; name-based fallback to a different id is refused.");
            }
        }

        var accessor = provider.GetService<IAuditOperationContextAccessor>();
        var generator = provider.GetService<IAuditIdentityGenerator>();
        if (snapshot.CorrelationId is not null && (accessor is null || generator is null))
        {
            throw new LocalEventContextRestoreException(
                "MissingAuditFacility",
                $"the record of event '{binding.LocalName}' carries correlation '{snapshot.CorrelationId}' but this " +
                "host has no audit accessor/identity facility; the association cannot be restored and the attempt is " +
                "refused instead of silently degrading to an unassociated execution.");
        }

        return new LocalEventContextResolution(tenant, snapshot, accessor, generator);
    }

    /// <summary>
    /// Sync phase, executed in the real execution frame: activates tenant and association frames
    /// and returns the LIFO frame that restores the caller state. A failure in a later step is the
    /// responsibility of this method: frames already entered are unwound LIFO before the original
    /// error is rethrown, with cleanup failures kept as independent facts.
    /// </summary>
    public static LocalEventContextFrame Activate(
        IServiceProvider provider,
        LocalEventContextResolution resolution)
    {
        var disposers = new List<IDisposable>(2);

        try
        {
            var currentTenant = provider.GetService<ICurrentTenant>();
            if (currentTenant is not null)
            {
                // resolution.Tenant == null means explicit host: the caller's tenant is cleared for
                // the duration of the execution and restored afterwards (recoverable Change frame).
                disposers.Add(currentTenant.Change(resolution.Tenant));
            }

            if (resolution.AuditAccessor is not null && resolution.IdentityGenerator is not null)
            {
                var snapshot = resolution.Snapshot;
                var correlation = snapshot.CorrelationId ?? resolution.IdentityGenerator.CreateOperationId();
                var actor = resolution.AuditAccessor.Current?.Actor ?? BackgroundActor;
                disposers.Add(resolution.AuditAccessor.Push(new AuditOperationContext
                {
                    CorrelationId = correlation,
                    OperationId = resolution.IdentityGenerator.CreateOperationId(),
                    Actor = actor,
                    InvocationSource = "local-event-execution",
                    TenantId = snapshot.TenantId,
                }));
            }
        }
        catch (Exception activationFailure)
        {
            var cleanupFailure = UnwindDisposers(disposers);
            if (cleanupFailure is not null)
            {
                throw new AggregateException(
                    $"context activation failed ({activationFailure.Message}) and restoring the already-entered " +
                    "frames also failed; both facts are reported.",
                    activationFailure, cleanupFailure);
            }

            throw;
        }

        return new LocalEventContextFrame(disposers);
    }

    private static Exception? UnwindDisposers(List<IDisposable> disposers)
    {
        Exception? first = null;
        for (var index = disposers.Count - 1; index >= 0; index--)
        {
            try
            {
                disposers[index].Dispose();
            }
            catch (Exception failure)
            {
                first ??= failure;
            }
        }

        disposers.Clear();
        return first;
    }
}

internal sealed record LocalEventContextResolution(
    ITenantInfo? Tenant,
    LocalEventExecutionContextSnapshot Snapshot,
    IAuditOperationContextAccessor? AuditAccessor,
    IAuditIdentityGenerator? IdentityGenerator)
{
    public static LocalEventContextResolution None { get; } = new(
        null,
        LocalEventExecutionContextSnapshot.Host,
        null,
        null);
}

/// <summary>
/// LIFO frame of ambient activations. Every dispose step is independently protected so one cleanup
/// failure never skips the remaining restores; secondary failures are aggregated and reported
/// instead of being hidden as success.
/// </summary>
internal sealed class LocalEventContextFrame : IDisposable
{
    private List<IDisposable>? _disposers;

    public LocalEventContextFrame(List<IDisposable> disposers) => _disposers = disposers;

    public static LocalEventContextFrame Empty { get; } = new(new List<IDisposable>(0));

    public Exception? DisposeAndCapture()
    {
        var disposers = _disposers;
        if (disposers is null)
        {
            return null;
        }

        _disposers = null;
        Exception? first = null;
        List<string>? problems = null;
        for (var index = disposers.Count - 1; index >= 0; index--)
        {
            try
            {
                disposers[index].Dispose();
            }
            catch (Exception failure)
            {
                first ??= failure;
                (problems ??= new List<string>()).Add(failure.Message);
            }
        }

        if (first is null)
        {
            return null;
        }

        return problems is { Count: > 1 }
            ? new AggregateException(
                $"multiple context restore steps failed: {string.Join(" | ", problems)}",
                disposers.Count == 0 ? Array.Empty<Exception>() : new[] { first })
            : first;
    }

    public void Dispose()
    {
        var failure = DisposeAndCapture();
        if (failure is not null)
        {
            throw failure;
        }
    }
}
