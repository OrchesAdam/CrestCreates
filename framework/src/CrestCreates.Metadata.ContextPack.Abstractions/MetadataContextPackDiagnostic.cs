using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackDiagnostic
{
    public required MetadataContextPackDiagnosticSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public DescriptorRef? Subject { get; init; }
    public string? Path { get; init; }
}
