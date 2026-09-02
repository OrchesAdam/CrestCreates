namespace CrestCreates.Authorization;

public sealed class PermissionGrantCacheOptions
{
    public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(5);
}
