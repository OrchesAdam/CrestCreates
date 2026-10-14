using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.Local;

internal readonly record struct LocalEventAdmissionResult(
    bool Allowed,
    LocalEventAcceptedIdentity? Identity,
    string? ProblemCode,
    string? Problem)
{
    public static LocalEventAdmissionResult Allow(LocalEventAcceptedIdentity identity)
        => new(true, identity, null, null);

    public static LocalEventAdmissionResult Reject(string problemCode, string problem)
        => new(false, null, problemCode, problem);
}

/// <summary>
/// Shared admission core: membership identity is already resolved by the caller; this core enforces
/// the governance descriptor association, the explicit execution checks and the activated
/// validator on every entry (publish, channel enqueue/consume, direct dispatch, replay) with the
/// same semantics. Checks read the authoritative built registry on each execution; nothing is
/// cached from a previous admission.
/// </summary>
internal static class LocalEventAdmission
{
    public static async Task<LocalEventAdmissionResult> AdmitAsync(
        IServiceProvider provider,
        LocalEventBinding binding,
        ILocalEvent payload,
        LocalEventExecutionSource source,
        LocalEventExecutionContextSnapshot snapshot,
        string? persistedDescriptorId,
        CancellationToken cancellationToken)
    {
        GeneratedEventDescriptor? descriptor = null;

        if (binding.Governance == LocalEventGovernance.Required)
        {
            var registry = provider.GetService<IEventRegistry>();
            if (registry is null)
            {
                return RegistryNotReady(binding);
            }

            if (registry.State != RegistryState.Built)
            {
                // Hosted composition: the coordinator gate covers the startup window (signalled only
                // after the registry task ran). Non-hosted: there is no gate and the actionable
                // rejection below applies — never cached, so a later bootstrap retry passes.
                var readiness = provider.GetService<LocalEventGovernanceReadiness>();
                if (readiness is not null)
                {
                    await readiness.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                if (registry.State != RegistryState.Built)
                {
                    return RegistryNotReady(binding);
                }
            }

            var metadata = provider.GetService<IEventMetadataProvider>();
            var matches = metadata?.GetAll()
                .Where(candidate => candidate.PayloadType == binding.EventType
                                    && candidate.Version == binding.ReplayVersion)
                .ToArray() ?? Array.Empty<GeneratedEventDescriptor>();

            if (matches.Length == 0)
            {
                return LocalEventAdmissionResult.Reject(
                    "GovernedDescriptorMissing",
                    $"event '{binding.LocalName}' requires governance but no descriptor with payload " +
                    $"'{binding.EventType.FullName}' at version v{binding.ReplayVersion} exists in the built registry.");
            }

            if (matches.Length > 1)
            {
                return LocalEventAdmissionResult.Reject(
                    "GovernedDescriptorAmbiguous",
                    $"event '{binding.LocalName}' requires governance but {matches.Length} descriptors with payload " +
                    $"'{binding.EventType.FullName}' at version v{binding.ReplayVersion} exist; the association must be unique.");
            }

            descriptor = matches[0];

            if (descriptor.State != DescriptorState.Active)
            {
                return LocalEventAdmissionResult.Reject(
                    "GovernedDescriptorInactive",
                    $"descriptor '{descriptor.Name}:v{descriptor.Version}' of event '{binding.LocalName}' is " +
                    $"{descriptor.State}; an inactive/deprecated exact version must not execute even when another " +
                    "version is active.");
            }

            if (persistedDescriptorId is not null
                && !string.Equals(persistedDescriptorId, descriptor.Id, StringComparison.Ordinal))
            {
                return LocalEventAdmissionResult.Reject(
                    "GovernedDescriptorMismatch",
                    $"persisted descriptor id '{persistedDescriptorId}' of event '{binding.LocalName}' does not match " +
                    $"the associated descriptor '{descriptor.Id}' (v{descriptor.Version}).");
            }

            if (source == LocalEventExecutionSource.Replay && persistedDescriptorId is null && !binding.AdoptGovernedHistory)
            {
                return LocalEventAdmissionResult.Reject(
                    "GovernedHistoryAdoptionMissing",
                    $"event '{binding.LocalName}' requires governance and the persisted record carries no descriptor " +
                    "id; adopting an ungoverned historical row requires the explicit per-event adoption mapping.");
            }
        }
        else if (persistedDescriptorId is not null)
        {
            return LocalEventAdmissionResult.Reject(
                "GovernanceDowngrade",
                $"event '{binding.LocalName}' is not governed but the record carries descriptor id " +
                $"'{persistedDescriptorId}'; a non-governed declaration must not silently downgrade a governed record.");
        }

        var identity = binding.CreateAcceptedIdentity(descriptor?.Id, descriptor?.Name);

        foreach (var checkType in binding.ExecutionCheckTypes)
        {
            if (provider.GetService(checkType) is not ILocalEventExecutionCheck check)
            {
                return LocalEventAdmissionResult.Reject(
                    "ExecutionCheckMissing",
                    $"execution check '{checkType.FullName}' of event '{binding.LocalName}' has no DI registration.");
            }

            LocalEventExecutionCheckResult outcome;
            try
            {
                outcome = await check.CheckAsync(
                    new LocalEventExecutionCheckContext(binding, payload, source, snapshot, descriptor?.Id),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception checkFailure)
            {
                return LocalEventAdmissionResult.Reject(
                    "ExecutionCheckFailed",
                    $"execution check '{checkType.FullName}' of event '{binding.LocalName}' failed: {checkFailure.Message}");
            }

            if (!outcome.Allowed)
            {
                return LocalEventAdmissionResult.Reject(
                    outcome.ProblemCode ?? "ExecutionCheckRejected",
                    outcome.Problem ?? $"execution check '{checkType.FullName}' rejected the event.");
            }
        }

        if (binding.UseEventValidator)
        {
            var validator = provider.GetService<CrestCreates.Event.Abstractions.IEventValidator>();
            if (validator is null)
            {
                return LocalEventAdmissionResult.Reject(
                    "EventValidatorMissing",
                    $"event '{binding.LocalName}' activates validator checks but no IEventValidator is registered.");
            }

            try
            {
                validator.ValidateOrThrow(descriptor?.Name ?? binding.LocalName, payload);
            }
            catch (Exception validationFailure)
            {
                return LocalEventAdmissionResult.Reject("EventValidatorRejected", validationFailure.Message);
            }
        }

        return LocalEventAdmissionResult.Allow(identity);
    }

    private static LocalEventAdmissionResult RegistryNotReady(LocalEventBinding binding)
        => LocalEventAdmissionResult.Reject(
            "GovernanceRegistryNotReady",
            $"event '{binding.LocalName}' requires governance but the event registry is not built in this " +
            "host; run the existing event bootstrap (BootstrapCoordinator / event-registry task) before " +
            "governed execution. The not-ready state is not cached: retry after bootstrap.");
}
