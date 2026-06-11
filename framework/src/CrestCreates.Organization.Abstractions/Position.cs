namespace CrestCreates.Organization.Abstractions;

public sealed class Position
{
    public string Id { get; init; } = default!;
    public string? TenantId { get; init; }
    public string Name { get; init; } = default!;
    public string? Code { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public Position Clone() => new()
    {
        Id = Id,
        TenantId = TenantId,
        Name = Name,
        Code = Code,
        IsActive = IsActive,
        CreatedAt = CreatedAt
    };
}
