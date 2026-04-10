using CrestCreates.Authorization.Abstractions;
using CrestCreates.Caching;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Authorization;

public class PermissionGrantManager : IPermissionGrantManager
{
    private readonly IPermissionGrantRepository _permissionGrantRepository;
    private readonly IPermissionGrantStore _permissionGrantStore;
    private readonly PermissionGrantCacheService _permissionGrantCacheService;
    private readonly TenantPermissionScopeValidator _tenantPermissionScopeValidator;
    private readonly TenantCacheKeyContributor _cacheKeyContributor;
    private readonly ICurrentTenant _currentTenant;

    public PermissionGrantManager(
        IPermissionGrantRepository permissionGrantRepository,
        IPermissionGrantStore permissionGrantStore,
        PermissionGrantCacheService permissionGrantCacheService,
        TenantPermissionScopeValidator tenantPermissionScopeValidator,
        TenantCacheKeyContributor cacheKeyContributor,
        ICurrentTenant currentTenant)
    {
        _permissionGrantRepository = permissionGrantRepository;
        _permissionGrantStore = permissionGrantStore;
        _permissionGrantCacheService = permissionGrantCacheService;
        _tenantPermissionScopeValidator = tenantPermissionScopeValidator;
        _cacheKeyContributor = cacheKeyContributor;
        _currentTenant = currentTenant;
    }

    public async Task GrantAsync(PermissionGrantInfo grant, CancellationToken cancellationToken = default)
    {
        var normalizedGrant = NormalizeGrant(grant);

        var validationResult = await _tenantPermissionScopeValidator.ValidateAsync(normalizedGrant, cancellationToken);
        if (!validationResult.IsAllowed)
        {
            throw new InvalidOperationException($"权限授予被拒绝: {validationResult.FailureReason}");
        }

        var existingGrant = await _permissionGrantRepository.FindAsync(
            normalizedGrant.PermissionName,
            normalizedGrant.ProviderType,
            normalizedGrant.ProviderKey,
            normalizedGrant.Scope,
            normalizedGrant.TenantId,
            cancellationToken);

        if (existingGrant != null)
        {
            return;
        }

        await _permissionGrantRepository.InsertAsync(
            new PermissionGrant(
                Guid.NewGuid(),
                normalizedGrant.PermissionName,
                normalizedGrant.ProviderType,
                normalizedGrant.ProviderKey,
                normalizedGrant.Scope,
                normalizedGrant.TenantId),
            cancellationToken);

        var cacheKey = _cacheKeyContributor.GetPermissionCacheKey(
            normalizedGrant.TenantId,
            normalizedGrant.ProviderType.ToString(),
            normalizedGrant.ProviderKey);
        await _permissionGrantCacheService.RemoveAsync(normalizedGrant.ProviderType, normalizedGrant.ProviderKey);
    }

    public async Task RevokeAsync(PermissionGrantInfo grant, CancellationToken cancellationToken = default)
    {
        var normalizedGrant = NormalizeGrant(grant);

        var existingGrant = await _permissionGrantRepository.FindAsync(
            normalizedGrant.PermissionName,
            normalizedGrant.ProviderType,
            normalizedGrant.ProviderKey,
            normalizedGrant.Scope,
            normalizedGrant.TenantId,
            cancellationToken);

        if (existingGrant == null)
        {
            return;
        }

        await _permissionGrantRepository.DeleteAsync(existingGrant, cancellationToken);

        var cacheKey = _cacheKeyContributor.GetPermissionCacheKey(
            normalizedGrant.TenantId,
            normalizedGrant.ProviderType.ToString(),
            normalizedGrant.ProviderKey);
        await _permissionGrantCacheService.RemoveAsync(normalizedGrant.ProviderType, normalizedGrant.ProviderKey);
    }

    public Task<IReadOnlyList<PermissionGrantInfo>> GetGrantsAsync(
        PermissionGrantProviderType providerType,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            return Task.FromResult<IReadOnlyList<PermissionGrantInfo>>(Array.Empty<PermissionGrantInfo>());
        }

        return _permissionGrantStore.GetGrantsAsync(providerType, providerKey.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(
        string userId,
        IEnumerable<string> roleNames,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<string>();
        }

        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var permissionName in await _permissionGrantStore.GetGrantedPermissionsAsync(
                     PermissionGrantProviderType.User,
                     userId.Trim(),
                     tenantId,
                     cancellationToken))
        {
            permissions.Add(permissionName);
        }

        foreach (var roleName in (roleNames ?? Array.Empty<string>())
                     .Where(roleName => !string.IsNullOrWhiteSpace(roleName))
                     .Select(roleName => roleName.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var permissionName in await _permissionGrantStore.GetGrantedPermissionsAsync(
                         PermissionGrantProviderType.Role,
                         roleName,
                         tenantId,
                         cancellationToken))
            {
                permissions.Add(permissionName);
            }
        }

        return permissions
            .OrderBy(permissionName => permissionName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PermissionGrantInfo NormalizeGrant(PermissionGrantInfo grant)
    {
        if (string.IsNullOrWhiteSpace(grant.PermissionName))
        {
            throw new ArgumentException("PermissionName is required.", nameof(grant));
        }

        if (string.IsNullOrWhiteSpace(grant.ProviderKey))
        {
            throw new ArgumentException("ProviderKey is required.", nameof(grant));
        }

        if (grant.Scope == PermissionGrantScope.Tenant && string.IsNullOrWhiteSpace(grant.TenantId))
        {
            throw new ArgumentException("TenantId is required when Scope is Tenant.", nameof(grant));
        }

        return new PermissionGrantInfo
        {
            PermissionName = grant.PermissionName.Trim(),
            ProviderType = grant.ProviderType,
            ProviderKey = grant.ProviderKey.Trim(),
            Scope = grant.Scope,
            TenantId = grant.Scope == PermissionGrantScope.Global ? null : grant.TenantId?.Trim()
        };
    }
}
