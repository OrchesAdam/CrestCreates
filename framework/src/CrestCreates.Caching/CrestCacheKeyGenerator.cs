using System.Collections.Generic;
using System.Linq;
using CrestCreates.Caching.Abstractions;

namespace CrestCreates.Caching;

public class CrestCacheKeyGenerator : ICrestCacheKeyGenerator
{
    public string GenerateKey(string prefix, params object[] parts)
    {
        var keyParts = new[] { prefix };
        if (parts != null && parts.Length > 0)
        {
            keyParts = keyParts.Concat(parts.Select(p => p?.ToString() ?? "null")).ToArray();
        }
        return string.Join(":", keyParts);
    }

    public string GenerateTenantKey(string prefix, string? tenantId, params object[] parts)
    {
        var keyParts = new List<object> { prefix };
        if (!string.IsNullOrEmpty(tenantId))
        {
            keyParts.Add(tenantId);
        }
        if (parts != null)
        {
            keyParts.AddRange(parts);
        }
        return string.Join(":", keyParts.Select(p => p?.ToString() ?? "null"));
    }

    public string GenerateUserKey(string prefix, string? userId, params object[] parts)
    {
        var keyParts = new List<object> { prefix };
        if (!string.IsNullOrEmpty(userId))
        {
            keyParts.Add(userId);
        }
        if (parts != null)
        {
            keyParts.AddRange(parts);
        }
        return string.Join(":", keyParts.Select(p => p?.ToString() ?? "null"));
    }

    public string GenerateFullKey(string prefix, string? tenantId, string? userId, params object[] parts)
    {
        var keyParts = new List<object> { prefix };
        if (!string.IsNullOrEmpty(tenantId))
        {
            keyParts.Add(tenantId);
        }
        if (!string.IsNullOrEmpty(userId))
        {
            keyParts.Add(userId);
        }
        if (parts != null)
        {
            keyParts.AddRange(parts);
        }
        return string.Join(":", keyParts.Select(p => p?.ToString() ?? "null"));
    }
}