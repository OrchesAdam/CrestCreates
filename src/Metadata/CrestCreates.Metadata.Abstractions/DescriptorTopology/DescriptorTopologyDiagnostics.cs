using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public sealed record DescriptorTopologyDiagnostics
{
    public required IReadOnlyList<DescriptorTopologyDiagnostic> All { get; init; }

    public IReadOnlyList<DescriptorTopologyDiagnostic> Errors =>
        All.Where(d => d.Severity == SeverityLevel.Error).ToList().AsReadOnly();

    public IReadOnlyList<DescriptorTopologyDiagnostic> Warnings =>
        All.Where(d => d.Severity == SeverityLevel.Warning).ToList().AsReadOnly();

    public bool HasErrors => Errors.Count > 0;
    public bool IsHealthy => !HasErrors;
}
