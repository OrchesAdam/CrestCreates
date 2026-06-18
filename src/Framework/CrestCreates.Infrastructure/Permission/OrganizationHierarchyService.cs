using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Infrastructure.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace CrestCreates.Infrastructure.Permission;

public class OrganizationHierarchyService : IOrganizationHierarchyService
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IMemoryCache _cache;
    private const string CacheKeyPrefix = "OrgHierarchy_";

    public OrganizationHierarchyService(
        IOrganizationRepository organizationRepository,
        IMemoryCache cache)
    {
        _organizationRepository = organizationRepository;
        _cache = cache;
    }

    public async Task<List<Guid>> GetOrganizationAndSubIdsAsync(Guid organizationId)
    {
        var cacheKey = $"{CacheKeyPrefix}{organizationId}";
        
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            
            var result = new List<Guid> { organizationId };
            await CollectDescendantIdsAsync(organizationId, result);
            return result;
        });
    }

    private async Task CollectDescendantIdsAsync(Guid parentId, List<Guid> result)
    {
        var children = await _organizationRepository.GetChildrenAsync(parentId);
        foreach (var child in children)
        {
            result.Add(child.Id);
            await CollectDescendantIdsAsync(child.Id, result);
        }
    }
}
