namespace CrestCreates.FileManagement.Models;

public record FileMetadata
{
    public Dictionary<string, string> Tags { get; init; } = new();
    public string? Description { get; init; }
    public string? Category { get; init; }
    public Dictionary<string, object> CustomProperties { get; init; } = new();
}