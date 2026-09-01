using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

internal sealed class CachedOrganizationHierarchyService : IOrganizationHierarchyService, IDisposable, IAsyncDisposable
{
    private readonly IOrganizationStore _store;
    private readonly IOrganizationHierarchyCacheOwner _owner;
    private readonly bool _ownerOwnedByService;

    public CachedOrganizationHierarchyService(IOrganizationStore store, IOrganizationHierarchyCacheOwner owner)
    {
        _store = store;
        _owner = owner;
        _ownerOwnedByService = false;
    }

    public CachedOrganizationHierarchyService(IOrganizationStore store)
    {
        _store = store;
        _owner = new OrganizationHierarchyCacheOwner();
        _ownerOwnedByService = true;
    }

    public async Task<IReadOnlyList<OrganizationUnit>> GetAncestorsAsync(
        string organizationUnitId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var allUnits = await GetHierarchyUnitsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var result = new List<OrganizationUnit>();
        var visited = new HashSet<string> { organizationUnitId };
        var currentId = organizationUnitId;

        while (true)
        {
            var lookupKey = OrganizationScopedKey.FromTenantId(tenantId, currentId);
            if (!allUnits.UnitMap.TryGetValue(lookupKey, out var current))
                break;

            var parentId = current.ParentId;
            if (parentId is null)
                break;

            if (!visited.Add(parentId))
                throw new OrganizationHierarchyException(
                    $"Circular hierarchy detected: organization unit '{parentId}' is already in the ancestor chain starting from '{organizationUnitId}'.");

            var parentKey = OrganizationScopedKey.FromTenantId(tenantId, parentId);
            if (!allUnits.UnitMap.TryGetValue(parentKey, out var parent))
                break;

            result.Add(parent.Snapshot());
            currentId = parentId;
        }

        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<OrganizationUnit>> GetDescendantsAsync(
        string organizationUnitId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var allUnits = await GetHierarchyUnitsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var result = new List<OrganizationUnit>();
        var visited = new HashSet<OrganizationScopedKey> { OrganizationScopedKey.FromTenantId(tenantId, organizationUnitId) };
        var queue = new Queue<OrganizationScopedKey>();

        var startKey = OrganizationScopedKey.FromTenantId(tenantId, organizationUnitId);
        if (allUnits.ChildrenMap.TryGetValue(startKey, out var directChildren))
        {
            foreach (var childKey in directChildren)
                queue.Enqueue(childKey);
        }

        while (queue.Count > 0)
        {
            var currentKey = queue.Dequeue();

            if (!visited.Add(currentKey))
                throw new OrganizationHierarchyException(
                    $"Circular hierarchy detected: organization unit '{currentKey}' appears multiple times in the descendant tree of '{organizationUnitId}'.");

            if (!allUnits.UnitMap.TryGetValue(currentKey, out var current))
                continue;

            result.Add(current.Snapshot());

            if (allUnits.ChildrenMap.TryGetValue(currentKey, out var children))
            {
                foreach (var childKey in children)
                    queue.Enqueue(childKey);
            }
        }

        return result.AsReadOnly();
    }

    public async Task<bool> IsDescendantOfAsync(
        string organizationUnitId,
        string ancestorOrganizationUnitId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (organizationUnitId == ancestorOrganizationUnitId)
            return false;

        var ancestors = await GetAncestorsAsync(organizationUnitId, tenantId, cancellationToken).ConfigureAwait(false);
        return ancestors.Any(a => a.Id == ancestorOrganizationUnitId);
    }

    public async Task<bool> IsUserInOrganizationAsync(
        string userId,
        string organizationUnitId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken).ConfigureAwait(false);
        return memberships.Any(m => m.IsActive && m.OrganizationUnitId == organizationUnitId);
    }

    public async Task<bool> IsUserInDescendantOrganizationAsync(
        string userId,
        string ancestorOrganizationUnitId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken).ConfigureAwait(false);
        var activeMembershipOrgIds = memberships.Where(m => m.IsActive).Select(m => m.OrganizationUnitId).ToHashSet();

        if (activeMembershipOrgIds.Count == 0)
            return false;

        if (activeMembershipOrgIds.Contains(ancestorOrganizationUnitId))
            return true;

        var descendants = await GetDescendantsAsync(ancestorOrganizationUnitId, tenantId, cancellationToken).ConfigureAwait(false);
        var descendantIds = descendants.Select(d => d.Id).ToHashSet();
        return activeMembershipOrgIds.Overlaps(descendantIds);
    }

    private async ValueTask<OrganizationHierarchySnapshot> GetHierarchyUnitsAsync(
        string? tenantId,
        CancellationToken cancellationToken)
    {
        // Null tenant bypasses cache
        if (tenantId is null)
        {
            var units = await _store.GetOrganizationUnitsAsync(null, cancellationToken: cancellationToken).ConfigureAwait(false);
            return OrganizationHierarchySnapshotBuilder.Build(0, units);
        }

        var scope = OrganizationScopeIdentity.Tenant(tenantId);
        var generationRead = await _store.ReadScopeGenerationAsync(scope, cancellationToken).ConfigureAwait(false);
        var admission = await _owner.AdmitScopeAsync(tenantId, generationRead, cancellationToken).ConfigureAwait(false);

        // Unavailable fallback (non-quarantined)
        if (admission.Generation is null)
        {
            var fallbackResult = await LoadAuthorityDirectAsync(tenantId, generation: 0, cancellationToken).ConfigureAwait(false);
            if (_owner.TryCompleteUnavailableFallback(admission, fallbackResult))
                return fallbackResult;
            throw new OrganizationHierarchyFreshnessException(
                OrganizationHierarchyFreshnessFailureKind.InvalidGenerationOutcome,
                message: "unavailable generation fallback rejected by safety state.");
        }

        var generation = admission.Generation.Value;
        var cacheKey = new OrganizationHierarchyCacheKey(tenantId, generation);

        try
        {
            if (_owner.TryReadSnapshot(cacheKey, out var cached))
            {
                if (_owner.TryCompleteGenerationResult(admission, cacheKey, cached, publish: false, out var accepted))
                    return accepted;
                throw CompletionRejected(admission, generation);
            }
        }
        catch (OrganizationHierarchySnapshotCacheException)
        {
            // OHC09 lookup failure: no candidate exists. Load exactly once and
            // re-enter the same final caller-completion safety gate.
            var directCandidate = await LoadAuthorityDirectAsync(tenantId, generation, cancellationToken).ConfigureAwait(false);
            if (_owner.TryCompleteGenerationResult(admission, cacheKey, directCandidate, publish: false, out var accepted))
                return accepted;
            throw CompletionRejected(admission, generation);
        }

        // Cache miss — single-flight load
        var candidate = await _owner.JoinOrCreateFlightAsync(cacheKey, async ct =>
        {
            var loadedUnits = await _store.GetOrganizationUnitsAsync(tenantId, cancellationToken: ct).ConfigureAwait(false);
            return OrganizationHierarchySnapshotBuilder.Build(generation, loadedUnits);
        }, cancellationToken).ConfigureAwait(false);

        if (_owner.TryCompleteGenerationResult(admission, cacheKey, candidate, publish: true, out var completed))
            return completed;
        throw CompletionRejected(admission, generation);
    }

    private async Task<OrganizationHierarchySnapshot> LoadAuthorityDirectAsync(
        string tenantId,
        long generation,
        CancellationToken cancellationToken)
    {
        var units = await _store.GetOrganizationUnitsAsync(tenantId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return OrganizationHierarchySnapshotBuilder.Build(generation, units);
    }

    private static OrganizationHierarchyFreshnessException CompletionRejected(
        OrganizationHierarchyAdmissionToken admission,
        long generation)
        => new(
            OrganizationHierarchyFreshnessFailureKind.GenerationRegression,
            observedGeneration: generation,
            observedHighWaterGeneration: admission.ObservedHighWater,
            quarantineFloorGeneration: admission.QuarantineFloor,
            message: "Hierarchy result lost the final caller-completion safety-state race.");

    public void Dispose()
    {
        if (_ownerOwnedByService)
            (_owner as IDisposable)?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        if (_ownerOwnedByService && _owner is IAsyncDisposable asyncDisposable)
            return asyncDisposable.DisposeAsync();
        Dispose();
        return ValueTask.CompletedTask;
    }
}
