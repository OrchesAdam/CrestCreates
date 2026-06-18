using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public sealed record DescriptorTopologyDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    DescriptorRef? Subject,
    IReadOnlyList<DescriptorRef>? RelatedRefs);
