namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPack
{
    public required MetadataContextPackRequest Request { get; init; }
    public required IReadOnlyList<MetadataContextPackDescriptorEntry> Descriptors { get; init; }
    public required IReadOnlyList<MetadataContextPackRelationshipEntry> Relationships { get; init; }
    public required MetadataContextPackSummary Summary { get; init; }
    public required IReadOnlyList<MetadataContextPackDiagnostic> Diagnostics { get; init; }
}
