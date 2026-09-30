using CrestCreates.Metadata.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;

/// <summary>Finite draft authorization and binding facts captured with a package artifact.</summary>
public sealed record AgentPackageArtifactOwner
{
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required string DescriptorId { get; init; }
    public required DescriptorKind DescriptorKind { get; init; }
    public required DescriptorDraftOperation Operation { get; init; }
    public required DescriptorDraftAuthorKind AuthorKind { get; init; }
    public required string AuthorId { get; init; }
    public required DescriptorDraftStatus Status { get; init; }
    public string? BaseVersion { get; init; }
    public string? ProposedVersion { get; init; }
}
