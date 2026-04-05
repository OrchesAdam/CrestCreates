namespace CrestCreates.Aop.Abstractions.Options;

public class CacheOptions
{
    public bool EnableCache { get; set; } = true;
    public int DefaultExpirationMinutes { get; set; } = 10;
    public bool EnablePerTenant { get; set; } = true;
    public Dictionary<string, CacheProfile> Profiles { get; set; } = new();
}

public class CacheProfile
{
    public int ExpirationMinutes { get; set; } = 10;
    public bool PerTenant { get; set; } = true;
    public string? KeyPrefix { get; set; }
}
