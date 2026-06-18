namespace CrestCreates.FileManagement.Models;

public interface IStorageMetadata
{
    long Size { get; }
    DateTimeOffset LastModified { get; }
    string ContentType { get; }
}