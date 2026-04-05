namespace CrestCreates.Caching;

public interface ICrestCacheService
{
    Task<T?> GetAsync<T>(string prefix, params object[] parts);
    Task<T?> GetAsync<T>(string prefix, string? tenantId, params object[] parts);
    Task SetAsync<T>(string prefix, T value, params object[] parts);
    Task SetAsync<T>(string prefix, T value, TimeSpan expiration, params object[] parts);
    Task SetAsync<T>(string prefix, string? tenantId, T value, params object[] parts);
    Task RemoveAsync(string prefix, params object[] parts);
    Task RemoveByPatternAsync(string pattern);
    Task ClearAsync();
    Task<bool> ExistsAsync(string prefix, params object[] parts);
}