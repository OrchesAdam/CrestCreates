using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Models;

namespace CrestCreates.FileManagement.Repositories;

public class InMemoryFileRepository : IFileRepository
{
    private readonly ConcurrentDictionary<string, FileEntity> _entities = new(StringComparer.OrdinalIgnoreCase);

    public Task<FileEntity?> GetByKeyAsync(FileKey key, CancellationToken ct = default)
    {
        _entities.TryGetValue(key.ToStorageKey(), out var entity);
        return Task.FromResult(entity);
    }

    public Task<IEnumerable<FileEntity>> ListAsync(Guid tenantId, int? year = null, CancellationToken ct = default)
    {
        var query = _entities.Values.Where(e => e.TenantId == tenantId);

        if (year.HasValue)
            query = query.Where(e => e.Key.Year == year.Value);

        return Task.FromResult(query.ToList() as IEnumerable<FileEntity>);
    }

    public Task CreateAsync(FileEntity entity, CancellationToken ct = default)
    {
        _entities.TryAdd(entity.Key.ToStorageKey(), entity);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(FileEntity entity, CancellationToken ct = default)
    {
        _entities.AddOrUpdate(entity.Key.ToStorageKey(), entity, (_, _) => entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(FileKey key, CancellationToken ct = default)
    {
        _entities.TryRemove(key.ToStorageKey(), out _);
        return Task.CompletedTask;
    }
}
