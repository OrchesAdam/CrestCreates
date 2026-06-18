namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorSearchResult
{
    public required IReadOnlyList<DescriptorInfo> Descriptors { get; init; }
    public required int TotalCount { get; init; }
    public required bool WasTruncated { get; init; }
}
