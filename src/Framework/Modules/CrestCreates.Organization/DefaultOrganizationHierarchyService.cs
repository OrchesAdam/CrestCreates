using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

/// <summary>
/// Public compatibility facade over the single generation-validated hierarchy
/// implementation. Direct construction owns a private cache owner; production
/// DI supplies the process owner and retains its lifetime.
/// </summary>
public sealed class DefaultOrganizationHierarchyService : IOrganizationHierarchyService, IDisposable, IAsyncDisposable
{
    private readonly CachedOrganizationHierarchyService _inner;

    public DefaultOrganizationHierarchyService(IOrganizationStore store)
    {
        _inner = new CachedOrganizationHierarchyService(store);
    }

    internal DefaultOrganizationHierarchyService(
        IOrganizationStore store,
        IOrganizationHierarchyCacheOwner owner)
    {
        _inner = new CachedOrganizationHierarchyService(store, owner);
    }

    public Task<IReadOnlyList<OrganizationUnit>> GetAncestorsAsync(
        string organizationUnitId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
        => _inner.GetAncestorsAsync(organizationUnitId, tenantId, cancellationToken);

    public Task<IReadOnlyList<OrganizationUnit>> GetDescendantsAsync(
        string organizationUnitId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
        => _inner.GetDescendantsAsync(organizationUnitId, tenantId, cancellationToken);

    public Task<bool> IsDescendantOfAsync(
        string organizationUnitId,
        string ancestorOrganizationUnitId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
        => _inner.IsDescendantOfAsync(organizationUnitId, ancestorOrganizationUnitId, tenantId, cancellationToken);

    public Task<bool> IsUserInOrganizationAsync(
        string userId,
        string organizationUnitId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
        => _inner.IsUserInOrganizationAsync(userId, organizationUnitId, tenantId, cancellationToken);

    public Task<bool> IsUserInDescendantOrganizationAsync(
        string userId,
        string ancestorOrganizationUnitId,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
        => _inner.IsUserInDescendantOrganizationAsync(userId, ancestorOrganizationUnitId, tenantId, cancellationToken);

    public void Dispose() => _inner.Dispose();

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
