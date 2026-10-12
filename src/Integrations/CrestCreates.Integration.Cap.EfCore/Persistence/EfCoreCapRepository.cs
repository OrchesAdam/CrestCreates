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
/// 2) **统一最终视图**：所有读取（Find/First/GetAll/GetByIds/Any/Count/LongCount）共用同一合并规则——
///    本地跟踪视图（Added/Modified/Unchanged，**Deleted 显式排除**）优先，数据库候选按跟踪实例的
///    当前状态做谓词复核与删除覆盖，主键去重；「数据库旧值」绝不把本地已删除/已不匹配的实体带回；
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

    // ── 统一最终视图（所有读取共用；Deleted 为显式排除覆盖） ──

    private async Task<List<TEntity>> QueryFinalViewAsync(
        Expression<Func<TEntity, bool>>? predicate,
        CancellationToken cancellationToken)
    {
        var compiled = predicate?.Compile();
        var result = new List<TEntity>();
        var seenIds = new HashSet<TKey>();

        // 本地视图优先（含未落库的 Added 与已修改的 Modified/Unchanged；Deleted 明确排除）。
        foreach (var entry in Db.ChangeTracker.Entries<TEntity>())
        {
            if (entry.State == EntityState.Deleted)
            {
                continue;
            }

            var entity = entry.Entity;
            if (compiled is not null && !compiled(entity))
            {
                continue;
            }

            if (seenIds.Add(entity.Id))
            {
                result.Add(entity);
            }
        }

        // 数据库候选：跟踪查询（同键经 identity resolution 返回本地实例）；
        // 对最终身份实例复核谓词、并覆盖待删除状态——旧快照不得复活本地已删除/已不匹配的实体。
        var fromDatabase = predicate is null
            ? await Set.ToListAsync(cancellationToken).ConfigureAwait(false)
            : await Set.Where(predicate).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var materialized in fromDatabase)
        {
            var entry = Db.Entry(materialized);
            if (entry.State == EntityState.Deleted)
            {
                continue;
            }

            var chosen = entry.Entity;
            if (compiled is not null && !compiled(chosen))
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

    // ── 查询（统一视图；计数与列表身份/状态一致） ──

    public async Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        => await QueryFinalViewAsync(predicate: null, cancellationToken).ConfigureAwait(false);

    public async Task<List<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await QueryFinalViewAsync(predicate, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var view = await QueryFinalViewAsync(predicate, cancellationToken).ConfigureAwait(false);
        return view.FirstOrDefault();
    }

    public async Task<bool> AnyAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var view = await QueryFinalViewAsync(predicate, cancellationToken).ConfigureAwait(false);
        return view.Count > 0;
    }

    public async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var view = await QueryFinalViewAsync(predicate, cancellationToken).ConfigureAwait(false);
        return view.Count;
    }

    public async Task<long> LongCountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var view = await QueryFinalViewAsync(predicate, cancellationToken).ConfigureAwait(false);
        return view.LongCount();
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

        // 与读取共用同一最终视图：同 UoW 新增的行可被删除；本地已不匹配/待删除的行不得误删。
        // （Added 实体经 RemoveRange 转为 Detached，不会产生数据库删除语句。）
        var matches = await QueryFinalViewAsync(predicate, cancellationToken).ConfigureAwait(false);
        Set.RemoveRange(matches);
        return matches.Count;
    }

    public IQueryableBuilder<TEntity> AsQueryable() => new EfCoreQueryableBuilder<TEntity>(Set, _chainNode);

    // ── 主键操作（统一视图语义） ──

    public Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entry = Db.ChangeTracker.Entries<TEntity>().FirstOrDefault(candidate => candidate.Entity.Id.Equals(id));
        if (entry is not null)
        {
            return Task.FromResult(entry.State == EntityState.Deleted ? null : entry.Entity);
        }

        return FirstOrDefaultAsync(entity => entity.Id.Equals(id), cancellationToken);
    }

    public async Task<List<TEntity>> GetByIdsAsync(
        IEnumerable<TKey> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var idList = ids.ToList();
        return await QueryFinalViewAsync(entity => idList.Contains(entity.Id), cancellationToken).ConfigureAwait(false);
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
