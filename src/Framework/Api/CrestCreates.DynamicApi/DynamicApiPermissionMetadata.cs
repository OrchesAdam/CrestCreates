namespace CrestCreates.DynamicApi;

public sealed class DynamicApiPermissionMetadata
{
    public string[] Permissions { get; init; } = Array.Empty<string>();

    public bool RequireAll { get; init; }
}
