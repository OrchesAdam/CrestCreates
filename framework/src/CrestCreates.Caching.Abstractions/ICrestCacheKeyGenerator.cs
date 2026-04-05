namespace CrestCreates.Caching.Abstractions;

public interface ICrestCacheKeyGenerator
{
    string GenerateKey(string prefix, params object[] parts);
    string GenerateTenantKey(string prefix, string? tenantId, params object[] parts);
    string GenerateUserKey(string prefix, string? userId, params object[] parts);
    string GenerateFullKey(string prefix, string? tenantId, string? userId, params object[] parts);
}