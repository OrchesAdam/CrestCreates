using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Event.Abstractions;

/// <summary>
/// Per-host contribution of one Integration event's source-generated JSON codec, registered through
/// the owning assembly's generated AddEventDescriptors() activation. Contributions are host-scoped
/// service registrations — never a process-global registry — so an assembly's codec is only available
/// to hosts that explicitly activated that assembly. Freeze treats repeated activation of the same
/// origin as idempotent and reports conflicting origins as a deterministic violation.
/// </summary>
public sealed record EventJsonCodecRegistration
{
    public required Type PayloadType { get; init; }

    public required Func<JsonTypeInfo?> CodecFactory { get; init; }

    /// <summary>Owning assembly (origin) used for idempotency and conflict diagnostics.</summary>
    public required string Origin { get; init; }
}
