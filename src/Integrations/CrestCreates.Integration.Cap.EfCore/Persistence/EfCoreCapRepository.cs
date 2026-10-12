using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Integration.Cap.EfCore.Persistence;

/// <summary>
/// Data.Abstractions <see cref="IRepository{TEntity, TKey}"/> 的 EF Core 实现（CAP 持久化专用）：
/// 1) 读写经 scoped <see cref="IDataBaseContext"/> 解析实际上下文（跟随受管链路由）；
/// 2) 不自行 SaveChanges——写入由 UoW 内核的 flush（SaveChangesAsync）统一落库；
/// 3) 仅覆盖 Data.Abstractions 仓储契约（Domain 仓储体系不变），不新增扫描/反射。
/// </summary>
public sealed class EfCoreCapRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly IDataBaseContext _dataBaseContext;
    private readonly UnitOfWorkChainNode? _chainNode;

    public EfCoreCapRepository(IDataBaseContext dataBaseContext, UnitOfWorkChainNode? chainNode = null)
    {
        _dataBaseContext = dataBaseContext ?? throw new ArgumentNullException(nameof(dataBaseContext));
        _chainNode = chainNode;
    }

    private DbSet<TEntity> Set => ((DbContext)_dataBaseContext.GetNativeContext()).Set<TEntity>();

    public async Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        => await Set.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<List<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await Set.AsNoTracking().Where(predicate).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await Set.AsNoTracking().FirstOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);

    public async Task<bool> AnyAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await Set.AsNoTracking().AnyAsync(predicate, cancellationToken).ConfigureAwait(false);

    public async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
        => predicate is null
            ? await Set.AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false)
            : await Set.AsNoTracking().CountAsync(predicate, cancellationToken).ConfigureAwait(false);

    public async Task<long> LongCountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
        => predicate is null
            ? await Set.AsNoTracking().LongCountAsync(cancellationToken).ConfigureAwait(false)
            : await Set.AsNoTracking().LongCountAsync(predicate, cancellationToken).ConfigureAwait(false);

    public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // 仅登记到 ChangeTracker；落库由 UoW 内核 flush 统一完成。
        await Set.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        await Set.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
    }

    public Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Set.Update(entity);
        return Task.FromResult(entity);
    }

    public Task<int> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var list = entities as ICollection<TEntity> ?? entities.ToList();
        Set.UpdateRange(list);
        return Task.FromResult(list.Count);
    }

    public Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Set.Remove(entity);
        return Task.CompletedTask;
    }

    public Task<int> DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var list = entities as ICollection<TEntity> ?? entities.ToList();
        Set.RemoveRange(list);
        return Task.FromResult(list.Count);
    }

    public async Task<int> DeleteAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var matches = await Set.Where(predicate).ToListAsync(cancellationToken).ConfigureAwait(false);
        Set.RemoveRange(matches);
        return matches.Count;
    }

    public IQueryableBuilder<TEntity> AsQueryable() => new EfCoreQueryableBuilder<TEntity>(Set, _chainNode);

    public async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
        => await Set.FindAsync(new object?[] { id }, cancellationToken).ConfigureAwait(false);

    public async Task<List<TEntity>> GetByIdsAsync(
        IEnumerable<TKey> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var idList = ids as ICollection<TKey> ?? ids.ToList();
        return await Set.AsNoTracking()
            .Where(entity => idList.Contains(entity.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await Set.FindAsync(new object?[] { id }, cancellationToken).ConfigureAwait(false);
        if (entity is not null)
        {
            Set.Remove(entity);
        }
    }

    public async Task<int> DeleteByIdsAsync(
        IEnumerable<TKey> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var idList = ids as ICollection<TKey> ?? ids.ToList();
        var matches = await Set.Where(entity => idList.Contains(entity.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        Set.RemoveRange(matches);
        return matches.Count;
    }
}
