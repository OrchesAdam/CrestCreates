using System;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Per-host contribution of one local event's source-generated JSON codec, registered through the
/// owning assembly's generated <c>LocalEventJsonCodecContributions.AddCodecs(IServiceCollection)</c>
/// activation. Contributions are host-scoped service registrations — never a process-global registry —
/// so an assembly's local codec is only available to hosts that explicitly activated that assembly.
/// This namespace is intentionally separate from the transport wire codec registrations (an event type
/// may have different local and wire JSON contracts).
/// </summary>
public sealed record LocalEventJsonCodecRegistration
{
    public required Type PayloadType { get; init; }

    public required Func<JsonTypeInfo?> CodecFactory { get; init; }

    /// <summary>Owning assembly (origin) used for idempotency and conflict diagnostics.</summary>
    public required string Origin { get; init; }
}
