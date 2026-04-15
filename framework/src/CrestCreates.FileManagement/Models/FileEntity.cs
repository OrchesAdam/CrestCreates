namespace CrestCreates.FileManagement.Models;

public record FileEntity
{
    public required FileKey Key { get; init; }
    public required Guid TenantId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long Size { get; init; }
    public required string Extension { get; init; }
    public required long UploadedAt { get; init; }
    public long? LastAccessedAt { get; init; }
    public string? UploadedBy { get; init; }
    public FileAccessMode AccessMode { get; init; }
}