using CrestCreates.DescriptorDraft.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Immutable snapshot of a resolved draft resource.
/// </summary>
internal sealed record DraftResourceSnapshot(Draft Draft);
