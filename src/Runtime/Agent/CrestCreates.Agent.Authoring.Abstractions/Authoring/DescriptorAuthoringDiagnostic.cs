using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public sealed record DescriptorAuthoringDiagnostic : ISnapshotable<DescriptorAuthoringDiagnostic>
{
    public required DiagnosticCode Code { get; init; }
    public required string Message { get; init; }
    public SeverityLevel Severity { get; init; } = SeverityLevel.Info;
    public string? Path { get; init; }

    public DescriptorAuthoringDiagnostic Snapshot() => this;
}
