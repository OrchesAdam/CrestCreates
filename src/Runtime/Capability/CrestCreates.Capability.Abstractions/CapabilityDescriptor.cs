using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata;

public sealed class CapabilityDescriptor : IDescriptor, IVersionedDescriptor
{
    // === IDescriptor ===
    public string Namespace { get; init; } = "capability";
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorKind Kind => DescriptorKind.Capability;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }

    // === IVersionedDescriptor ===
    public int Version { get; init; }

    // === Catalog Properties ===
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<EventRef> Produces { get; init; } = Array.Empty<EventRef>();
    public IReadOnlyList<EventRef> Consumes { get; init; } = Array.Empty<EventRef>();
    public IReadOnlyList<string> SemanticTags { get; init; } = Array.Empty<string>();

    // === Runtime Properties (merged from Capability.Abstractions) ===
    public CapabilityKind CapabilityKind { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor>? InputSchema { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor>? OutputSchema { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public CapabilityRiskLevel RiskLevel { get; init; } = CapabilityRiskLevel.Medium;

    /// <summary>
    /// Marks the origin of this capability.
    /// Compatibility projections are migration artifacts with an exit path to native capabilities.
    /// </summary>
    public CapabilityProjectionKind ProjectionKind { get; init; } = CapabilityProjectionKind.Native;
}

/// <summary>
/// Strong-typed event reference for Capability descriptors.
/// </summary>
public readonly record struct EventRef(string Namespace, string Id, int? Version = null) : IDescriptorRef;
