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
    /// Full recoverability validation shared by every accept/execute boundary: context format,
    /// damaged ids, tenant mode, authoritative tenant resolution with exact-id equality, and audit
    /// association capability. A borrowed caller scope does not skip it, and a later execution
    /// re-validates instead of trusting the earlier pass.
    /// </summary>
    public static async Task<ITenantInfo?> ValidateRecoverabilityAsync(
        IServiceProvider provider,
        LocalEventBinding binding,
        LocalEventExecutionContextSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        // A damaged (empty/whitespace) persisted tenant id is refused before anything else: it is
        // not an explicit host fact and must not be converted into one.
        if (snapshot.TenantId is not null && string.IsNullOrWhiteSpace(snapshot.TenantId))
        {
            throw new LocalEventContextRestoreException(
                "InvalidTenantId",
                $"event '{binding.LocalName}' carries a damaged (empty) tenant id; a damaged value is never " +
                "converted into an executable host.");
        }

        var hasTenant = !string.IsNullOrEmpty(snapshot.TenantId);

        if (snapshot.ContextVersion != LocalEventExecutionContextSnapshot.CurrentContextVersion)
        {
            if (snapshot.ContextVersion is not null)
            {
                throw new LocalEventContextRestoreException(
                    "UnknownContextVersion",
                    $"event '{binding.LocalName}' carries unsupported context version v{snapshot.ContextVersion}; " +
                    "the record is preserved and refused.");
            }

            if (!hasTenant && !binding.AllowHistoricalHostContext)
            {
                throw new LocalEventContextRestoreException(
                    "HistoricalContextNotRecognized",
                    $"event '{binding.LocalName}' carries a legacy row without a context snapshot; executing it as " +
                    "host context requires the explicit per-event historical-host migration policy.");
            }
        }

        switch (binding.TenantMode)
        {
            case LocalEventTenantMode.HostOnly when hasTenant:
                throw new LocalEventContextRestoreException(
                    "TenantModeHostOnly",
                    $"event '{binding.LocalName}' is host-only but the request carries tenant '{snapshot.TenantId}'.");
            case LocalEventTenantMode.Required when !hasTenant:
                throw new LocalEventContextRestoreException(
                    "TenantModeRequired",
                    $"event '{binding.LocalName}' requires a tenant but the request carries no valid tenant id.");
        }

        ITenantInfo? resolvedTenant = null;
        if (hasTenant)
        {
            var tenantProvider = provider.GetService<ITenantProvider>()
                ?? throw new LocalEventContextRestoreException(
                    "MissingTenantProvider",
                    $"event '{binding.LocalName}' carries tenant '{snapshot.TenantId}' but this host has no " +
                    "ITenantProvider; the tenant cannot be validated and the request is refused.");

            resolvedTenant = await tenantProvider.GetTenantAsync(snapshot.TenantId!, cancellationToken).ConfigureAwait(false);
            if (resolvedTenant is null)
            {
                throw new LocalEventContextRestoreException(
                    "UnknownTenant",
                    $"tenant '{snapshot.TenantId}' of event '{binding.LocalName}' cannot be resolved; unknown or " +
                    "inactive tenants are refused and never fall back to host execution.");
            }

            if (!string.Equals(resolvedTenant.Id, snapshot.TenantId, StringComparison.Ordinal))
            {
                throw new LocalEventContextRestoreException(
                    "TenantIdMismatch",
                    $"tenant provider resolved id '{resolvedTenant.Id}' for requested '{snapshot.TenantId}' of event " +
                    $"'{binding.LocalName}'; name-based fallback to a different id is refused.");
            }
        }

        if (snapshot.CorrelationId is not null
            && (provider.GetService<IAuditOperationContextAccessor>() is null
                || provider.GetService<IAuditIdentityGenerator>() is null))
        {
            throw new LocalEventContextRestoreException(
                "MissingAuditFacility",
                $"event '{binding.LocalName}' carries correlation '{snapshot.CorrelationId}' but this host has no " +
                "audit accessor/identity facility; the association could not be restored by a later execution and " +
                "the request is refused instead of degrading to an unassociated one.");
        }

        return resolvedTenant;
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
        var tenant = await ValidateRecoverabilityAsync(provider, binding, snapshot, cancellationToken).ConfigureAwait(false);

        if (tenant is not null && provider.GetService<ICurrentTenant>() is null)
        {
            throw new LocalEventContextRestoreException(
                "MissingCurrentTenant",
                $"the record of event '{binding.LocalName}' carries tenant '{snapshot.TenantId}' but this host has " +
                "no ICurrentTenant; the tenant cannot be activated and the attempt is refused.");
        }

        return new LocalEventContextResolution(
            tenant,
            snapshot,
            provider.GetService<IAuditOperationContextAccessor>(),
            provider.GetService<IAuditIdentityGenerator>());
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
            if (cleanupFailure is null)
            {
                throw;
            }

            // The original failure keeps its identity: a cancellation (caller-driven or not) stays
            // an OCE with the cleanup fact attached, and a non-OCE failure is surfaced together with
            // the cleanup failure so callers report Release.Failed instead of claiming a clean
            // release.
            if (activationFailure is OperationCanceledException)
            {
                activationFailure.Data["LocalEventContextCleanupFailure"] = cleanupFailure;
                throw;
            }

            throw new LocalEventContextActivationException(activationFailure, cleanupFailure);
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
        List<Exception>? failures = null;
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
                (failures ??= new List<Exception>()).Add(failure);
            }
        }

        if (first is null)
        {
            return null;
        }

        return problems is { Count: > 1 } && failures is not null
            ? new AggregateException(
                $"multiple context restore steps failed: {string.Join(" | ", problems)}",
                failures)
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
