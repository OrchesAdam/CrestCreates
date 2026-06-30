using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Metadata.Abstractions.Evidence;

public sealed record EvidenceFinding : ISnapshotable<EvidenceFinding>
{
    public required string Source { get; init; }
    public required DiagnosticCode Code { get; init; }
    public required SeverityLevel Severity { get; init; }
    public DescriptorRef? Subject { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<DescriptorRef> RelatedRefs { get; init; } = Array.Empty<DescriptorRef>();

    public EvidenceFinding Snapshot() => this with
    {
        RelatedRefs = RelatedRefs.ToArray()
    };
}
