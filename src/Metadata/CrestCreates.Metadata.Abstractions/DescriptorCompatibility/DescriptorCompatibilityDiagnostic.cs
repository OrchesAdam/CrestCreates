using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public sealed record DescriptorCompatibilityDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    DescriptorRef? Subject,
    IReadOnlyList<DescriptorRef>? RelatedRefs);
