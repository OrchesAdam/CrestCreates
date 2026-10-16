using System.Linq.Expressions;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.GeneratedCrud.AotFixture.Domain;

namespace CrestCreates.GeneratedCrud.AotFixture;

/// <summary>
/// #128 S4 Step A memory repository: the persistence seam the generated WidgetAppService injects.
/// It models the real provider contract in the two places the generated CRUD surface depends on:
/// identity/stamp issuance on insert (an entity always leaves the persistence layer with a non-empty
/// Id and ConcurrencyStamp) and the optimistic-concurrency rotation on update/delete
/// (<see cref="CrestConcurrencyException"/> on a stamp mismatch, new stamp after a successful update
/// — the same semantics as the EF/FreeSql/SqlSugar repositories). Reads return detached copies, so a
/// caller's in-memory mutations never leak into the store before UpdateAsync is called.
/// No UnitOfWork participates here: Step A deliberately runs without weaving, which is exactly why
/// the generated [UnitOfWorkMo] attributes are inert in this fixture (Step B introduces the UoW).
/// </summary>
public sealed class MemoryWidgetRepository : ICrestRepositoryBase<Widget, Guid>
{
    private readonly object _sync = new();
    private readonly List<Widget> _store = new();

    public IQueryable<Widget> GetQueryable()
    {
        lock (_sync)
        {
            return _store.Select(Clone).ToList().AsQueryable();
        }
    }

    public Task<List<Widget>> GetListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetQueryable().ToList());

    public Task<List<Widget>> GetListAsync(Expression<Func<Widget, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(GetQueryable().Where(predicate).ToList());

    public Task<List<Widget>> GetListAsync(
        Expression<Func<Widget, bool>> predicate,
        Expression<Func<Widget, object>> orderBy,
        bool ascending = true,
        CancellationToken cancellationToken = default)
    {
        var query = GetQueryable().Where(predicate);
        return Task.FromResult((ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy)).ToList());
    }

    public Task<Widget?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(GetQueryable().FirstOrDefault(x => x.Id == id));

    public Task<Widget?> GetAsync(Expression<Func<Widget, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(GetQueryable().FirstOrDefault(predicate));

    public Task<Widget> InsertAsync(Widget entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        lock (_sync)
        {
            if (entity.Id == Guid.Empty)
            {
                entity.Id = Guid.NewGuid();
            }

            if (string.IsNullOrWhiteSpace(entity.ConcurrencyStamp))
            {
                entity.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            }

            if (entity.CreationTime == default)
            {
                entity.CreationTime = DateTime.UtcNow;
            }

            _store.Add(Clone(entity));
        }

        return Task.FromResult(entity);
    }

    public async Task<IEnumerable<Widget>> InsertRangeAsync(IEnumerable<Widget> entities, CancellationToken cancellationToken = default)
    {
        var inserted = new List<Widget>();
        foreach (var entity in entities)
        {
            inserted.Add(await InsertAsync(entity, cancellationToken));
        }

        return inserted;
    }

    public Task<Widget> UpdateAsync(Widget entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        lock (_sync)
        {
            var index = _store.FindIndex(x => x.Id == entity.Id);
            if (index < 0)
            {
                throw new CrestConcurrencyException(nameof(Widget), entity.Id);
            }

            // Optimistic concurrency: the presented stamp must match the persisted one, and a
            // successful update rotates it — mirroring the EF provider's concurrency-token update.
            if (!string.Equals(_store[index].ConcurrencyStamp, entity.ConcurrencyStamp, StringComparison.Ordinal))
            {
                throw new CrestConcurrencyException(nameof(Widget), entity.Id);
            }

            entity.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            _store[index] = Clone(entity);
        }

        return Task.FromResult(entity);
    }

    public async Task<IEnumerable<Widget>> UpdateRangeAsync(IEnumerable<Widget> entities, CancellationToken cancellationToken = default)
    {
        var updated = new List<Widget>();
        foreach (var entity in entities)
        {
            updated.Add(await UpdateAsync(entity, cancellationToken));
        }

        return updated;
    }

    public Task DeleteAsync(Widget entity, CancellationToken cancellationToken = default)
        => DeleteEntityAsync(entity);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _store.RemoveAll(x => x.Id == id);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, string expectedStamp, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var index = _store.FindIndex(x => x.Id == id);
            if (index < 0)
            {
                return Task.CompletedTask;
            }

            if (!string.Equals(_store[index].ConcurrencyStamp, expectedStamp, StringComparison.Ordinal))
            {
                throw new CrestConcurrencyException(nameof(Widget), id);
            }

            _store.RemoveAt(index);
        }

        return Task.CompletedTask;
    }

    public async Task DeleteRangeAsync(IEnumerable<Widget> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            await DeleteEntityAsync(entity);
        }
    }

    public Task DeleteRangeAsync(Expression<Func<Widget, bool>> predicate, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _store.RemoveAll(new Predicate<Widget>(predicate.Compile()));
        }

        return Task.CompletedTask;
    }

    public Task<PagedResult<Widget>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        => Task.FromResult(Page(GetQueryable(), pageIndex, pageSize));

    public Task<PagedResult<Widget>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<Widget, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(Page(GetQueryable().Where(predicate), pageIndex, pageSize));

    public Task<PagedResult<Widget>> GetPagedAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<Widget, bool>> predicate,
        Expression<Func<Widget, object>> orderBy,
        bool ascending = true,
        CancellationToken cancellationToken = default)
    {
        var query = GetQueryable().Where(predicate);
        query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
        return Task.FromResult(Page(query, pageIndex, pageSize));
    }

    public Task<PagedResult<Widget>> GetPagedAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<Widget, object>> orderBy,
        bool ascending = true,
        CancellationToken cancellationToken = default)
    {
        var query = GetQueryable();
        query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
        return Task.FromResult(Page(query, pageIndex, pageSize));
    }

    public Task<long> GetCountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult((long)GetQueryable().Count());

    public Task<long> GetCountAsync(Expression<Func<Widget, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult((long)GetQueryable().Count(predicate));

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetQueryable().Any());

    public Task<bool> AnyAsync(Expression<Func<Widget, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(GetQueryable().Any(predicate));

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(GetQueryable().Any(x => x.Id == id));

    /// <summary>Reads one row as a detached copy, so callers observe exactly what is persisted.</summary>
    public Widget? Find(Guid id) => GetQueryable().FirstOrDefault(x => x.Id == id);

    private Task DeleteEntityAsync(Widget entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return DeleteAsync(entity.Id);
    }

    private static PagedResult<Widget> Page(IQueryable<Widget> query, int pageIndex, int pageSize)
    {
        var totalCount = query.Count();
        var items = query.Skip(pageIndex * pageSize).Take(pageSize).ToList();
        return new PagedResult<Widget>(items, totalCount, pageIndex, pageSize);
    }

    private static Widget Clone(Widget source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Price = source.Price,
        Description = source.Description,
        CreationTime = source.CreationTime,
        ConcurrencyStamp = source.ConcurrencyStamp,
    };
}
