using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Metadata.Abstractions.Evidence;

public sealed record EvidenceFindingCount : ISnapshotable<EvidenceFindingCount>
{
    public required SeverityLevel Severity { get; init; }
    public required DiagnosticCode Code { get; init; }
    public int Count { get; init; }

    public EvidenceFindingCount Snapshot() => this with { };
}
