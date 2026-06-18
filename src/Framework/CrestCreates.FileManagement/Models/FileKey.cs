using System.Text.RegularExpressions;

namespace CrestCreates.FileManagement.Models;

public record FileKey
{
    public required Guid TenantId { get; init; }
    public required int Year { get; init; }
    public required Guid FileGuid { get; init; }
    public required string Extension { get; init; }

    public string ToStorageKey() => $"{TenantId}/{Year}/{FileGuid}{Extension}";

    public static FileKey Create(Guid tenantId, string extension) => new()
    {
        TenantId = tenantId,
        Year = DateTimeOffset.UtcNow.Year,
        FileGuid = Guid.NewGuid(),
        Extension = extension.StartsWith('.') ? extension : $".{extension}"
    };

    public static FileKey? Parse(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey))
            return null;

        var pattern = @"^([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/(\d{4})/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(\.[^/]+)?$";
        var match = Regex.Match(storageKey, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        return new FileKey
        {
            TenantId = Guid.Parse(match.Groups[1].Value),
            Year = int.Parse(match.Groups[2].Value),
            FileGuid = Guid.Parse(match.Groups[3].Value),
            Extension = match.Groups[4].Value
        };
    }
}