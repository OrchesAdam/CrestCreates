using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Application.Tenants;

public class TenantCacheInvalidator
{
    private readonly ICrestCacheService _cacheService;
    private readonly TenantCacheKeyContributor _cacheKeyContributor;
    private readonly ILogger<TenantCacheInvalidator> _logger;

    public TenantCacheInvalidator(
        ICrestCacheService cacheService,
        TenantCacheKeyContributor cacheKeyContributor,
        ILogger<TenantCacheInvalidator> logger)
    {
        _cacheService = cacheService;
        _cacheKeyContributor = cacheKeyContributor;
        _logger = logger;
    }

    public async Task InvalidateAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("正在失效租户 {TenantId} 的所有缓存", tenantId);

        var pattern = _cacheKeyContributor.GetTenantCacheKey(tenantId, "*");
        await _cacheService.RemoveByPatternAsync(pattern);

        var permissionPattern = _cacheKeyContributor.GetPermissionCacheKey(tenantId, "*", "*");
        await _cacheService.RemoveByPatternAsync(permissionPattern);

        var authPattern = _cacheKeyContributor.GetAuthorizationCacheKey(tenantId, "*", "*");
        await _cacheService.RemoveByPatternAsync(authPattern);

        _logger.LogInformation("租户 {TenantId} 的缓存已失效", tenantId);
    }

    public async Task InvalidateConnectionStringAsync(string tenantId, string connectionStringName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("正在失效租户 {TenantId} 的连接串 {ConnectionStringName} 缓存", tenantId, connectionStringName);

        var cacheKey = _cacheKeyContributor.GetTenantCacheKey(tenantId, "ConnectionString", connectionStringName);
        await _cacheService.RemoveAsync("Tenant", cacheKey);
        await InvalidateAsync(tenantId, cancellationToken);
    }
}
