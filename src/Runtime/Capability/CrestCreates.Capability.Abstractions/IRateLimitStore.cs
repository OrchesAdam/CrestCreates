namespace CrestCreates.Capability.Abstractions;

public interface IRateLimitStore
{
    Task<bool> CheckRateLimitAsync(string key, int maxRequests, TimeSpan window, CancellationToken ct = default);
}