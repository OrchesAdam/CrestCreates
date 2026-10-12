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
/// 2) **跟踪契约**：读取本地优先（同键已跟踪实例复用，含未落库的 Added 行），数据库查询保持跟踪，
///    连续读写同一主键复用同一实例——首次写入保持 Added、不会被 Update 误改为 Modified；
/// 3) **不自行 SaveChanges**：落库由唯一 UoW 内核的 flush 统一完成（业务路径由调用方 UoW 承载，
///    后台补偿由 <c>CompensationRetryBackgroundService</c> 经内核建立 UoW 边界）。
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

    private DbContext Db => (DbContext)_dataBaseContext.GetNativeContext();

    private DbSet<TEntity> Set => Db.Set<TEntity>();

    private IEnumerable<TEntity> LocallyTracked()
        => Db.ChangeTracker.Entries<TEntity>()
            .Where(entry => entry.State != EntityState.Deleted)
            .Select(entry => entry.Entity);

    private TEntity? FindTrackedLocal(TKey id)
        => LocallyTracked().FirstOrDefault(entity => entity.Id.Equals(id));

    // ── 查询（本地优先 + 跟踪查询；同键复用同一实例） ──

    public async Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var fromDatabase = await Set.ToListAsync(cancellationToken).ConfigureAwait(false);
        return MergeLocalFirst(fromDatabase);
    }

    public async Task<List<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        var compiled = predicate.Compile();
        var fromDatabase = await Set.Where(predicate).ToListAsync(cancellationToken).ConfigureAwait(false);
        return MergeLocalFirst(fromDatabase, compiled);
    }

    public async Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        var compiled = predicate.Compile();

        // 本地优先：同一 UoW 内先 Add 后查询必须看到未落库的 Added 行（且复用同一实例）。
        var localMatch = LocallyTracked().AsQueryable().FirstOrDefault(predicate);
        if (localMatch is not null)
        {
            return localMatch;
        }

        // SQL 按数据库旧值筛选，却可能返回 identity map 中已修改的同键实例：对最终实例复核谓词。
        var candidate = await Set.FirstOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);
        return candidate is not null && compiled(candidate) ? candidate : null;
    }

    public async Task<bool> AnyAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        var compiled = predicate.Compile();
        if (LocallyTracked().Any(compiled))
        {
            return true;
        }

        // 数据库命中的 id：无本地跟踪实例时按数据库事实计入；有跟踪实例时以实例当前状态复核。
        var candidateIds = await Set.Where(predicate).Select(entity => entity.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return candidateIds.Any(id =>
        {
            var local = FindTrackedLocal(id);
            return local is null || compiled(local);
        });
    }

    public async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        if (predicate is null)
        {
            return await Set.CountAsync(cancellationToken).ConfigureAwait(false) + PendingLocalAdds(null);
        }

        return await CountTrackedAwareAsync(predicate, cancellationToken).ConfigureAwait(false) + PendingLocalAdds(predicate);
    }

    public async Task<long> LongCountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        if (predicate is null)
        {
            return await Set.LongCountAsync(cancellationToken).ConfigureAwait(false) + PendingLocalAdds(null);
        }

        return await CountTrackedAwareAsync(predicate, cancellationToken).ConfigureAwait(false) + PendingLocalAdds(predicate);
    }

    private async Task<int> CountTrackedAwareAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var compiled = predicate.Compile();
        var candidateIds = await Set.Where(predicate).Select(entity => entity.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return candidateIds.Count(id =>
        {
            var local = FindTrackedLocal(id);
            return local is null || compiled(local);
        });
    }

    private int PendingLocalAdds(Expression<Func<TEntity, bool>>? predicate)
    {
        var compiled = predicate?.Compile();
        return Db.ChangeTracker.Entries<TEntity>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .Count(entity => compiled is null || compiled(entity));
    }

    private List<TEntity> MergeLocalFirst(List<TEntity> fromDatabase, Func<TEntity, bool>? predicate = null)
    {
        var result = new List<TEntity>();
        var seenIds = new HashSet<TKey>();

        foreach (var entity in LocallyTracked())
        {
            if (predicate is not null && !predicate(entity))
            {
                continue;
            }

            if (seenIds.Add(entity.Id))
            {
                result.Add(entity);
            }
        }

        foreach (var tracked in fromDatabase)
        {
            var local = FindTrackedLocal(tracked.Id);
            var chosen = local ?? tracked;

            // SQL 旧快照命中的行，其本地同键实例可能已不满足谓词（如已变为 Completed）：
            // 对最终选中的身份实例复核谓词，避免把已不匹配的本地实体带回结果。
            if (predicate is not null && !predicate(chosen))
            {
                continue;
            }

            if (seenIds.Add(chosen.Id))
            {
                result.Add(chosen);
            }
        }

        return result;
    }

    // ── 写入（不 SaveChanges；状态由 UoW flush 统一落库） ──

    public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // 同键已跟踪（本 UoW 内先前 Add 或查询）→ 复用已跟踪实例，避免重复跟踪冲突。
        var tracked = FindTrackedLocal(entity.Id);
        if (tracked is not null)
        {
            return tracked;
        }

        await Set.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        foreach (var entity in entities)
        {
            await AddAsync(entity, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var entry = Db.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            // 已跟踪（含 Added 未落库）：保持原状态；属性就地修改由快照自动标记，绝不把 Added 误改为 Modified。
            return Task.FromResult(entity);
        }

        var tracked = FindTrackedLocal(entity.Id);
        if (tracked is not null)
        {
            // 同键已有另一跟踪实例：合并值到该实例（避免同键重复跟踪）。
            Db.Entry(tracked).CurrentValues.SetValues(entity);
            return Task.FromResult(tracked);
        }

        // 外部实例且键已存在：附加为 Modified（UPDATE 已存在的行）。
        Db.Attach(entity);
        entry.State = EntityState.Modified;
        return Task.FromResult(entity);
    }

    public Task<int> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var list = entities.ToList();
        foreach (var entity in list)
        {
            UpdateAsync(entity, cancellationToken);
        }

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
        var list = entities.ToList();
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

    // ── 主键操作 ──

    public async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var local = FindTrackedLocal(id);
        if (local is not null)
        {
            return local;
        }

        return await Set.FindAsync(new object?[] { id }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<TEntity>> GetByIdsAsync(
        IEnumerable<TKey> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var idList = ids.ToList();
        var fromDatabase = await Set.Where(entity => idList.Contains(entity.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return MergeLocalFirst(fromDatabase, entity => idList.Contains(entity.Id));
    }

    public async Task DeleteByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
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
        var idList = ids.ToList();
        var matches = await GetByIdsAsync(idList, cancellationToken).ConfigureAwait(false);
        Set.RemoveRange(matches);
        return matches.Count;
    }
}
