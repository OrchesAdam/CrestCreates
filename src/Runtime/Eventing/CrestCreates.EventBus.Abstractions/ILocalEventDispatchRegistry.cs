using System;
using System.Collections.Generic;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Read-only execution index for local event dispatch. Frozen after activation; consumed by the
/// dispatcher, the channel consumer and the dead-letter replay core. It does not replace
/// <c>IEventRegistry</c> and does not redefine event names, versions, permissions, audit or
/// reliability semantics.
/// </summary>
public interface ILocalEventDispatchRegistry
{
    IReadOnlyCollection<LocalEventBinding> Bindings { get; }

    bool TryGet(Type eventType, out LocalEventBinding binding);

    bool TryGetByReplayKey(string replayKey, int version, out LocalEventBinding binding);

    /// <summary>
    /// Resolves the payload identity of a legacy record (no <c>LocalReplayKey</c>): normalized
    /// metadata type name + version first, then the statically declared legacy alias table.
    /// </summary>
    bool TryResolvePayloadIdentity(string payloadTypeFullName, int eventVersion, out LocalEventBinding binding);
}

/// <summary>
/// Contributes dispatch registrations for one assembly. Generated per-assembly contributors are
/// activated only when the host explicitly calls that assembly's generated registration extension.
/// </summary>
public interface ILocalEventRegistrationContributor
{
    string ContributorId { get; }

    void Register(LocalEventDispatchRegistrationBuilder builder);
}
