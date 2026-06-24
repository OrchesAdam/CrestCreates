namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public sealed record DescriptorPackageDiagnostic
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public DescriptorRef? Subject { get; init; }
}
