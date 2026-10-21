using System.Linq.Expressions;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.DTOs;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Repositories;
using SaaSHelpdesk.Domain.Shared.Enums;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// The Ticket persistence leaf. It stores detached row images, issues/rotates optimistic stamps like
/// the real providers, and executes only the query shapes the AppService actually uses. No EF,
/// EnumerableQuery, Expression.Compile or dynamic LINQ is involved.
/// </summary>
public sealed class MemoryTicketRepository(TicketMemoryStore store) : ITicketRepository
{
    private TicketMemoryStore Store { get; } = store;

    public IQueryable<Ticket> GetQueryable()
        => new TicketQueryable(() => Store.TicketRows);

    public Task<List<Ticket>> GetListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Store.TicketRows.Select(TicketRowsMapper.ToTicket).ToList());

    public Task<List<Ticket>> GetListAsync(Expression<Func<Ticket, bool>> predicate, CancellationToken cancellationToken = default)
        => throw Unsupported(predicate);

    public Task<List<Ticket>> GetListAsync(
        Expression<Func<Ticket, bool>> predicate,
        Expression<Func<Ticket, object>> orderBy,
        bool ascending = true,
        CancellationToken cancellationToken = default)
        => throw Unsupported(predicate);

    public Task<Ticket?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = Store.FindTicket(id);
        return Task.FromResult(row is null ? null : TicketRowsMapper.ToTicket(row));
    }

    public Task<Ticket?> GetAsync(Expression<Func<Ticket, bool>> predicate, CancellationToken cancellationToken = default)
        => throw Unsupported(predicate);

    public Task<Ticket> InsertAsync(Ticket entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        lock (Store.Sync)
        {
            if (string.IsNullOrWhiteSpace(entity.ConcurrencyStamp))
            {
                entity.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            }

            if (entity.CreationTime == default)
            {
                entity.CreationTime = DateTime.UtcNow;
            }

            Store.InsertTicket(TicketRowsMapper.Capture(entity));
        }

        return Task.FromResult(entity);
    }

    public async Task<IEnumerable<Ticket>> InsertRangeAsync(IEnumerable<Ticket> entities, CancellationToken cancellationToken = default)
    {
        var inserted = new List<Ticket>();
        foreach (var entity in entities)
        {
            inserted.Add(await InsertAsync(entity, cancellationToken));
        }

        return inserted;
    }

    public Task<Ticket> UpdateAsync(Ticket entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        lock (Store.Sync)
        {
            var current = Store.FindTicket(entity.Id)
                ?? throw new CrestConcurrencyException(nameof(Ticket), entity.Id);
            if (!string.Equals(current.ConcurrencyStamp, entity.ConcurrencyStamp, StringComparison.Ordinal))
            {
                throw new CrestConcurrencyException(nameof(Ticket), entity.Id);
            }

            entity.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            Store.ReplaceTicket(TicketRowsMapper.Capture(entity));
        }

        return Task.FromResult(entity);
    }

    public async Task<IEnumerable<Ticket>> UpdateRangeAsync(IEnumerable<Ticket> entities, CancellationToken cancellationToken = default)
    {
        var updated = new List<Ticket>();
        foreach (var entity in entities)
        {
            updated.Add(await UpdateAsync(entity, cancellationToken));
        }

        return updated;
    }

    public Task DeleteAsync(Ticket entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Store.RemoveTicket(entity.Id, expectedStamp: null, requireStamp: false);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Store.RemoveTicket(id, expectedStamp: null, requireStamp: false);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, string expectedStamp, CancellationToken cancellationToken = default)
    {
        Store.RemoveTicket(id, expectedStamp, requireStamp: true);
        return Task.CompletedTask;
    }

    public async Task DeleteRangeAsync(IEnumerable<Ticket> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            await DeleteAsync(entity, cancellationToken);
        }
    }

    public Task DeleteRangeAsync(Expression<Func<Ticket, bool>> predicate, CancellationToken cancellationToken = default)
        => throw Unsupported(predicate);

    public Task<PagedResult<Ticket>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        => Task.FromResult(Page(Store.TicketRows, pageIndex, pageSize));

    public Task<PagedResult<Ticket>> GetPagedAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<Ticket, bool>> predicate,
        CancellationToken cancellationToken = default)
        => throw Unsupported(predicate);

    public Task<PagedResult<Ticket>> GetPagedAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<Ticket, bool>> predicate,
        Expression<Func<Ticket, object>> orderBy,
        bool ascending = true,
        CancellationToken cancellationToken = default)
        => throw Unsupported(predicate);

    public Task<PagedResult<Ticket>> GetPagedAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<Ticket, object>> orderBy,
        bool ascending = true,
        CancellationToken cancellationToken = default)
        => throw Unsupported(orderBy);

    public Task<long> GetCountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult((long)Store.TicketCount);

    public Task<long> GetCountAsync(Expression<Func<Ticket, bool>> predicate, CancellationToken cancellationToken = default)
        => throw Unsupported(predicate);

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Store.TicketCount > 0);

    public Task<bool> AnyAsync(Expression<Func<Ticket, bool>> predicate, CancellationToken cancellationToken = default)
        => throw Unsupported(predicate);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Store.FindTicket(id) is not null);

    public Task<List<Ticket>> GetOverdueTicketsAsync(DateTime referenceDate, CancellationToken ct = default)
    {
        var rows = Store.FindTickets(row =>
            row.Status != TicketStatus.Closed
            && row.DueDate.HasValue
            && row.DueDate.Value < referenceDate);
        return Task.FromResult(rows.Select(TicketRowsMapper.ToTicket).ToList());
    }

    public Task<List<Ticket>> GetByCustomerAsync(Guid customerId, CancellationToken ct = default)
    {
        var rows = Store.FindTickets(row => row.CustomerId == customerId);
        return Task.FromResult(rows.Select(TicketRowsMapper.ToTicket).ToList());
    }

    public Task<List<Ticket>> GetByAssigneeAsync(Guid assigneeId, CancellationToken ct = default)
    {
        var rows = Store.FindTickets(row => row.AssigneeId == assigneeId);
        return Task.FromResult(rows.Select(TicketRowsMapper.ToTicket).ToList());
    }

    public Task<List<Ticket>> GetByStatusAsync(TicketStatus status, CancellationToken ct = default)
    {
        var rows = Store.FindTickets(row => row.Status == status);
        return Task.FromResult(rows.Select(TicketRowsMapper.ToTicket).ToList());
    }

    public Task<List<Ticket>> GetByDateRangeAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        var rows = Store.FindTickets(row => row.CreationTime >= start && row.CreationTime <= end);
        return Task.FromResult(rows.Select(TicketRowsMapper.ToTicket).ToList());
    }

    public Task<List<Ticket>> GetSLABreachedTicketsAsync(DateTime referenceDate, CancellationToken ct = default)
    {
        var rows = Store.FindTickets(row =>
            row.Status is TicketStatus.Open or TicketStatus.InProgress
            && row.DueDate.HasValue
            && row.DueDate.Value < referenceDate);
        return Task.FromResult(rows.Select(TicketRowsMapper.ToTicket).ToList());
    }

    private static PagedResult<Ticket> Page(IReadOnlyList<TicketRow> rows, int pageIndex, int pageSize)
        => new(
            rows.Skip(pageIndex * pageSize).Take(pageSize).Select(TicketRowsMapper.ToTicket).ToList(),
            rows.Count,
            pageIndex,
            pageSize);

    private static NotSupportedException Unsupported(Expression expression)
        => new($"The native Ticket fixture does not execute expression '{expression}'.");
}

/// <summary>The SLA policy leaf used by AssignAsync's active-policy lookup.</summary>
public sealed class MemorySlaPolicyRepository(TicketMemoryStore store) : ICrestRepositoryBase<SLAPolicy, Guid>
{
    private TicketMemoryStore Store { get; } = store;

    public IQueryable<SLAPolicy> GetQueryable()
        => new SlaQueryable(() => Store.SlaRow is null ? Array.Empty<SlaRow>() : new[] { Store.SlaRow });

    public Task<List<SLAPolicy>> GetListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Store.SlaRow is null
            ? new List<SLAPolicy>()
            : new List<SLAPolicy> { TicketRowsMapper.ToSlaPolicy(Store.SlaRow) });

    public Task<List<SLAPolicy>> GetListAsync(Expression<Func<SLAPolicy, bool>> predicate, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture does not execute predicate queries.");

    public Task<List<SLAPolicy>> GetListAsync(
        Expression<Func<SLAPolicy, bool>> predicate,
        Expression<Func<SLAPolicy, object>> orderBy,
        bool ascending = true,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture does not execute ordered queries.");

    public Task<SLAPolicy?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Store.SlaRow is { } row && row.Id == id ? TicketRowsMapper.ToSlaPolicy(row) : null);

    public Task<SLAPolicy?> GetAsync(Expression<Func<SLAPolicy, bool>> predicate, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture does not execute predicate queries.");

    public Task<SLAPolicy> InsertAsync(SLAPolicy entity, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture is read-only.");

    public Task<IEnumerable<SLAPolicy>> InsertRangeAsync(IEnumerable<SLAPolicy> entities, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture is read-only.");

    public Task<SLAPolicy> UpdateAsync(SLAPolicy entity, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture is read-only.");

    public Task<IEnumerable<SLAPolicy>> UpdateRangeAsync(IEnumerable<SLAPolicy> entities, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture is read-only.");

    public Task DeleteAsync(SLAPolicy entity, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture is read-only.");

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture is read-only.");

    public Task DeleteAsync(Guid id, string expectedStamp, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture is read-only.");

    public Task DeleteRangeAsync(IEnumerable<SLAPolicy> entities, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture is read-only.");

    public Task DeleteRangeAsync(Expression<Func<SLAPolicy, bool>> predicate, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture is read-only.");

    public Task<PagedResult<SLAPolicy>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture does not execute paged queries.");

    public Task<PagedResult<SLAPolicy>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<SLAPolicy, bool>> predicate, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture does not execute paged queries.");

    public Task<PagedResult<SLAPolicy>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<SLAPolicy, bool>> predicate, Expression<Func<SLAPolicy, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture does not execute paged queries.");

    public Task<PagedResult<SLAPolicy>> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<SLAPolicy, object>> orderBy, bool ascending = true, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture does not execute paged queries.");

    public Task<long> GetCountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Store.SlaRow is null ? 0L : 1L);

    public Task<long> GetCountAsync(Expression<Func<SLAPolicy, bool>> predicate, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture does not execute predicate queries.");

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Store.SlaRow is not null);

    public Task<bool> AnyAsync(Expression<Func<SLAPolicy, bool>> predicate, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The native SLA fixture does not execute predicate queries.");

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Store.SlaRow is { } row && row.Id == id);
}
