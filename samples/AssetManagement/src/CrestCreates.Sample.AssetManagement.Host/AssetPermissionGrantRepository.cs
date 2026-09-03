using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.Domain.Shared.Permissions;

namespace CrestCreates.Sample.AssetManagement.Host;

// The sample owns only the storage adapter. Permission evaluation remains the
// framework's PermissionChecker -> PermissionGrantManager -> PermissionGrantStore
// chain, which makes the business wiring visible in the golden application.
public sealed class AssetPermissionGrantRepository : CrestRepositoryBase<PermissionGrant, Guid>, IPermissionGrantRepository
{
    private readonly ConcurrentDictionary<Guid, PermissionGrant> _grants = new();

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The generic compatibility query surface is not used by the AOT authorization mainline; specialized grant methods below are the only runtime authority calls.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The generic compatibility query surface is not used by the AOT authorization mainline; specialized grant methods below are the only runtime authority calls.")]
    public override IQueryable<PermissionGrant> GetQueryableUnfiltered()
        => new EnumerableQuery<PermissionGrant>(_grants.Values.ToArray());

    public override Task<List<PermissionGrant>> GetListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Snapshot(cancellationToken));

    public override Task<List<PermissionGrant>> GetListAsync(Expression<Func<PermissionGrant, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(Snapshot(cancellationToken).Where(predicate.Compile()).ToList());

    public override Task<List<PermissionGrant>> GetListAsync(Expression<Func<PermissionGrant, bool>> predicate, Expression<Func<PermissionGrant, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        => Task.FromResult((ascending ? Snapshot(cancellationToken).Where(predicate.Compile()).OrderBy(orderBy.Compile()) : Snapshot(cancellationToken).Where(predicate.Compile()).OrderByDescending(orderBy.Compile())).ToList());

    public override Task<PermissionGrant?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_grants.GetValueOrDefault(id));

    public override Task<PermissionGrant?> GetAsync(Expression<Func<PermissionGrant, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(Snapshot(cancellationToken).FirstOrDefault(predicate.Compile()));

    public override Task<PermissionGrant> InsertAsync(PermissionGrant entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_grants.TryAdd(entity.Id, entity))
            throw new InvalidOperationException($"Permission grant '{entity.Id}' already exists.");
        return Task.FromResult(entity);
    }

    public override Task<IEnumerable<PermissionGrant>> InsertRangeAsync(IEnumerable<PermissionGrant> entities, CancellationToken cancellationToken = default)
    {
        var values = entities.ToArray();
        foreach (var entity in values)
            InsertAsync(entity, cancellationToken).GetAwaiter().GetResult();
        return Task.FromResult<IEnumerable<PermissionGrant>>(values);
    }

    public override Task<PermissionGrant> UpdateAsync(PermissionGrant entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _grants[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    public override Task<IEnumerable<PermissionGrant>> UpdateRangeAsync(IEnumerable<PermissionGrant> entities, CancellationToken cancellationToken = default)
    {
        var values = entities.ToArray();
        foreach (var entity in values)
            _grants[entity.Id] = entity;
        return Task.FromResult<IEnumerable<PermissionGrant>>(values);
    }

    public override Task DeleteAsync(PermissionGrant entity, CancellationToken cancellationToken = default)
        => DeleteAsync(entity.Id, cancellationToken);

    public override Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _grants.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public override Task DeleteAsync(Guid id, string expectedStamp, CancellationToken cancellationToken = default)
        => DeleteAsync(id, cancellationToken);

    public override Task DeleteRangeAsync(IEnumerable<PermissionGrant> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
            _grants.TryRemove(entity.Id, out _);
        return Task.CompletedTask;
    }

    public override Task DeleteRangeAsync(Expression<Func<PermissionGrant, bool>> predicate, CancellationToken cancellationToken = default)
    {
        foreach (var entity in Snapshot(cancellationToken).Where(predicate.Compile()))
            _grants.TryRemove(entity.Id, out _);
        return Task.CompletedTask;
    }

    public override Task<PagedResult<PermissionGrant>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        => Page(Snapshot(cancellationToken), pageIndex, pageSize);

    public override Task<PagedResult<PermissionGrant>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<PermissionGrant, bool>> predicate, CancellationToken cancellationToken = default)
        => Page(Snapshot(cancellationToken).Where(predicate.Compile()), pageIndex, pageSize);

    public override Task<PagedResult<PermissionGrant>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<PermissionGrant, bool>> predicate, Expression<Func<PermissionGrant, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        => Page((ascending ? Snapshot(cancellationToken).Where(predicate.Compile()).OrderBy(orderBy.Compile()) : Snapshot(cancellationToken).Where(predicate.Compile()).OrderByDescending(orderBy.Compile())), pageIndex, pageSize);

    public override Task<PagedResult<PermissionGrant>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<PermissionGrant, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        => Page(ascending ? Snapshot(cancellationToken).OrderBy(orderBy.Compile()) : Snapshot(cancellationToken).OrderByDescending(orderBy.Compile()), pageIndex, pageSize);

    public override Task<long> GetCountAsync(CancellationToken cancellationToken = default) => Task.FromResult((long)Snapshot(cancellationToken).Count);
    public override Task<long> GetCountAsync(Expression<Func<PermissionGrant, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult((long)Snapshot(cancellationToken).Count(predicate.Compile()));
    public override Task<bool> AnyAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot(cancellationToken).Count != 0);
    public override Task<bool> AnyAsync(Expression<Func<PermissionGrant, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot(cancellationToken).Any(predicate.Compile()));
    public override Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_grants.ContainsKey(id));

    public Task<List<PermissionGrant>> GetListByProviderAsync(PermissionGrantProviderType providerType, string providerKey, CancellationToken cancellationToken = default)
        => Task.FromResult(Snapshot(cancellationToken).Where(grant => grant.ProviderType == providerType && string.Equals(grant.ProviderKey, providerKey, StringComparison.Ordinal)).OrderBy(grant => grant.PermissionName, StringComparer.OrdinalIgnoreCase).ToList());

    public Task<PermissionGrant?> FindAsync(string permissionName, PermissionGrantProviderType providerType, string providerKey, PermissionGrantScope scope, string? tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult(Snapshot(cancellationToken).FirstOrDefault(grant => string.Equals(grant.PermissionName, permissionName, StringComparison.OrdinalIgnoreCase)
            && grant.ProviderType == providerType && string.Equals(grant.ProviderKey, providerKey, StringComparison.Ordinal)
            && grant.Scope == scope && string.Equals(grant.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)));

    public Task<List<PermissionGrant>> GetListByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult(Snapshot(cancellationToken).Where(grant => string.Equals(grant.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)).OrderBy(grant => grant.PermissionName, StringComparer.OrdinalIgnoreCase).ToList());

    private List<PermissionGrant> Snapshot(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _grants.Values.ToList();
    }

    private static Task<PagedResult<PermissionGrant>> Page(IEnumerable<PermissionGrant> values, int pageIndex, int pageSize)
    {
        var list = values.ToList();
        return Task.FromResult(new PagedResult<PermissionGrant>(list.Skip(pageIndex * pageSize).Take(pageSize).ToList(), list.Count, pageIndex, pageSize));
    }
}
