namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPack
{
    public required MetadataContextPackRequest Request { get; init; }
    public required IReadOnlyList<MetadataContextPackDescriptorEntry> Descriptors { get; init; }
    public required IReadOnlyList<MetadataContextPackRelationshipEntry> Relationships { get; init; }
    public required MetadataContextPackSummary Summary { get; init; }
    public required IReadOnlyList<MetadataContextPackDiagnostic> Diagnostics { get; init; }

    /// <summary>
    /// Deep copy for boundary snapshot isolation. Not ISnapshotable because
    /// MetadataContextPack lives in ContextPack.Abstractions which does not
    /// reference Snapshot.Abstractions.
    /// </summary>
    public MetadataContextPack Copy() => this with
    {
        Request = Request.Copy(),
        Descriptors = Descriptors.ToArray(),
        Relationships = Relationships.ToArray(),
        Summary = Summary.Copy(),
        Diagnostics = Diagnostics.ToArray()
    };
}
