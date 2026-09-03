using System.Collections.Concurrent;
using CrestCreates.Sample.AssetManagement.Application;
using CrestCreates.Sample.AssetManagement.Domain.Entities;

namespace CrestCreates.Sample.AssetManagement.Persistence;

public sealed class InMemoryAssetStore : IAssetStore
{
    private readonly ConcurrentDictionary<(string Tenant, Guid Id), Asset> _assets = new();

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AddAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        if (!_assets.TryAdd((asset.TenantId, asset.Id), asset))
            throw new InvalidOperationException($"Asset '{asset.Id}' already exists.");
        return Task.CompletedTask;
    }

    public Task<Asset?> GetAsync(string tenantId, Guid assetId, CancellationToken cancellationToken = default)
        => Task.FromResult(_assets.GetValueOrDefault((tenantId, assetId)));

    public Task<IReadOnlyList<Asset>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Asset>>(_assets.Where(pair => pair.Key.Tenant == tenantId).Select(pair => pair.Value).ToArray());

    public Task UpdateAsync(Asset asset, string expectedConcurrencyStamp, CancellationToken cancellationToken = default)
    {
        if (!_assets.TryGetValue((asset.TenantId, asset.Id), out var current)
            || current.ConcurrencyStamp != expectedConcurrencyStamp)
            throw new InvalidOperationException($"Asset '{asset.Id}' was changed.");
        _assets[(asset.TenantId, asset.Id)] = asset;
        return Task.CompletedTask;
    }

    public Task SaveAssignmentAsync(Asset asset, string expectedConcurrencyStamp, AssetAssignment assignment, CancellationToken cancellationToken = default)
        => UpdateAsync(asset, expectedConcurrencyStamp, cancellationToken);

    public Task SaveReturnAsync(Asset asset, string expectedConcurrencyStamp, Guid assignmentId, CancellationToken cancellationToken = default)
        => UpdateAsync(asset, expectedConcurrencyStamp, cancellationToken);

    public Task SaveMaintenanceDecisionAsync(Asset asset, string expectedConcurrencyStamp, MaintenanceRecord record, CancellationToken cancellationToken = default)
        => UpdateAsync(asset, expectedConcurrencyStamp, cancellationToken);
}
