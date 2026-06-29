using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Metadata.Abstractions.Evidence;

public sealed record EvidenceFindingCount
{
    public required SeverityLevel Severity { get; init; }
    public required DiagnosticCode Code { get; init; }
    public int Count { get; init; }
}
