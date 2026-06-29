using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public sealed record DescriptorCompatibilityDiagnostic(
    SeverityLevel Severity,
    DiagnosticCode Code,
    string Message,
    DescriptorRef? Subject,
    IReadOnlyList<DescriptorRef>? RelatedRefs);
