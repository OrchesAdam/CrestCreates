using System;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Declares a local event type for typed dispatch membership. Handler-derived events do not
/// need this attribute; declare-only events (third-party types via host-side typeof remain a
/// separate path) use it. Replay is an explicit capability: <see cref="Replayable"/> defaults
/// to <c>false</c> (dispatch-only) and must never be inferred at failure time.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class LocalEventAttribute : Attribute
{
    public int Version { get; set; } = 1;

    public bool Replayable { get; set; }
}
