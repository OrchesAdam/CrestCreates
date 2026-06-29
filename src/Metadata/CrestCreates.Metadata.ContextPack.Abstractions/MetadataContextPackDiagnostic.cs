using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackDiagnostic
{
    public required SeverityLevel Severity { get; init; }
    public required DiagnosticCode Code { get; init; }
    public required string Message { get; init; }
    public DescriptorRef? Subject { get; init; }
    public string? Path { get; init; }
}
