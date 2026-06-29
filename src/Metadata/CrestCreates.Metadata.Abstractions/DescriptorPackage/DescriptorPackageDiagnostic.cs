using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public sealed record DescriptorPackageDiagnostic
{
    public required DiagnosticCode Code { get; init; }
    public required SeverityLevel Severity { get; init; }
    public required string Message { get; init; }
    public DescriptorRef? Subject { get; init; }
}
