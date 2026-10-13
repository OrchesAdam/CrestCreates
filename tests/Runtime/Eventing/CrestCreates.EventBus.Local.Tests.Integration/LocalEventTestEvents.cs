using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local.Tests.Integration;

[LocalEvent(Replayable = true)]
internal sealed class ReplayableDispatchEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

[LocalEvent(Replayable = true)]
internal sealed class ReplayableFailingEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

/// <summary>Declared replayable with no handler anywhere: dispatch is a declared no-op and replay must be refused.</summary>
[LocalEvent(Replayable = true)]
internal sealed class ReplayableOrphanEvent : DomainEvent
{
}

/// <summary>Explicit dispatch-only capability: failures are never recorded as replayable dead letters.</summary>
[LocalEvent]
internal sealed class DispatchOnlyEvent : DomainEvent
{
}

[LocalEvent(Replayable = true, Version = 2)]
internal sealed class VersionedReplayEvent : DomainEvent
{
}

[LocalEvent(Replayable = true)]
[LocalEventLegacyIdentity("CrestCreates.Legacy.GoneEvent", 1)]
internal sealed class LegacyAliasedReplayEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

[LocalEvent(Replayable = true)]
internal sealed class CancellableReplayEvent : DomainEvent
{
}
