using System;

namespace CrestCreates.FileManagement.Models;

public class LocalStorageMetadata : IStorageMetadata
{
    public long Size { get; init; }
    public DateTimeOffset LastModified { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
}