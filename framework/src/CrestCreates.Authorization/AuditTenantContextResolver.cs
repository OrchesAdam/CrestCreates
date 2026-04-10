using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Caching;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Authorization;

public class AuditTenantContextResolver
{
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly TenantCacheKeyContributor _cacheKeyContributor;
    private readonly ILogger<AuditTenantContextResolver> _logger;

    public AuditTenantContextResolver(
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        TenantCacheKeyContributor cacheKeyContributor,
        ILogger<AuditTenantContextResolver> logger)
    {
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _cacheKeyContributor = cacheKeyContributor;
        _logger = logger;
    }

    public string ResolveTenantId()
    {
        return _currentTenant.Id ?? _currentUser.TenantId ?? string.Empty;
    }

    public string? ResolveTenantIdOrNull()
    {
        return _currentTenant.Id ?? _currentUser.TenantId;
    }

    public string ResolveAuditCacheKey()
    {
        return _cacheKeyContributor.GetTenantCacheKey(ResolveTenantIdOrNull(), "Audit");
    }

    public AuditTenantInfo GetAuditTenantInfo()
    {
        return new AuditTenantInfo
        {
            TenantId = ResolveTenantIdOrNull(),
            TenantName = _currentTenant.Tenant?.Name,
            IsSuperAdmin = _currentUser.IsSuperAdmin,
            UserId = _currentUser.Id
        };
    }
}

public class AuditTenantInfo
{
    public string? TenantId { get; set; }
    public string? TenantName { get; set; }
    public bool IsSuperAdmin { get; set; }
    public string? UserId { get; set; }
}
