using System.Security.Cryptography;
using System.Text;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Event.Abstractions;

public sealed record GeneratedEventDescriptor : IEventDescriptor, IVersionedDescriptor
{
    // ── 1. Identity ──
    public string Id { get; init; } = string.Empty;    // Explicit stable identity from [CrestEvent(Id=...)]. Name changes do not break audit/DLQ.
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public DescriptorState State { get; init; }
    public string? Description { get; init; }

    // ── 2. Payload ──
    public Type PayloadType { get; init; } = null!;
    public VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor> PayloadSchemaRef { get; init; }

    // ── 3. Scope ──
    public EventScope Scope { get; init; }

    // ── 4. Reliability ──
    public EventReliability Reliability { get; init; }
    public bool RequiresIdempotency { get; init; }

    // ── 5. Ownership ──
    public string? CapabilityId { get; init; }          // Phase 3: typed VersionedDescriptorRef<CapabilityDescriptor>
    public string? CreatedBy { get; init; }

    // ── Classification ──
    public EventImportance Importance { get; init; }

    // ── Operational flags ──
    public bool IsAuditable { get; init; }
    public bool IsReplayable { get; init; }
    public bool IsPublic { get; init; }

    // ── Compatibility ──
    public SchemaChangeKind ChangeKind { get; init; }

    // ── Topology (reserved) ──
    public IReadOnlyList<string> Producers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Consumers { get; init; } = Array.Empty<string>();

    // ── IVersionedDescriptor ──
    public string Namespace { get; init; } = "event";
    DescriptorKind IDescriptor.Kind => DescriptorKind.Event;
    string? IDescriptor.SupersededById => null;

    public static string GenerateId(string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return "evt_" + Convert.ToHexString(hash)[..12];
    }
}
