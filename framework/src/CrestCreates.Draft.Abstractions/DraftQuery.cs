namespace CrestCreates.Draft.Abstractions;

public sealed class DraftQuery
{
    public string? TenantId { get; init; }
    public string? OwnerId { get; init; }
    public string? DraftType { get; init; }
    public DraftStatus? Status { get; init; }
    public int? MaxResults { get; init; }
}
