using CrestCreates.Authorization.Abstractions;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Application.Services;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// Fixture-owned observability for the zero-business claim. Counters are independent of HTTP status
/// and of the memory row count: a validation failure that reaches the real service, starts its unit
/// of work, or attempts a repository write must make the gate fail even if the final response is 400.
/// Normal writes provide the positive control so these counters can never stay zero.
/// </summary>
public sealed class TicketBusinessObserver
{
    private int _businessEntries;
    private int _unitOfWorkBegins;
    private int _repositoryWrites;

    public int BusinessEntries => Volatile.Read(ref _businessEntries);
    public int UnitOfWorkBegins => Volatile.Read(ref _unitOfWorkBegins);
    public int RepositoryWrites => Volatile.Read(ref _repositoryWrites);

    public void RecordBusinessEntry() => Interlocked.Increment(ref _businessEntries);
    public void RecordUnitOfWorkBegin() => Interlocked.Increment(ref _unitOfWorkBegins);
    public void RecordRepositoryWrite() => Interlocked.Increment(ref _repositoryWrites);

    public Snapshot Capture() => new(BusinessEntries, UnitOfWorkBegins, RepositoryWrites);

    public readonly record struct Snapshot(int BusinessEntries, int UnitOfWorkBegins, int RepositoryWrites);
}

/// <summary>
/// A counting façade over the real TicketAppService. Every call increments the business-entry
/// observation and then delegates; the real service keeps its weaving, permissions, validation and
/// persistence. This is fixture instrumentation, not a replacement business service.
/// </summary>
public sealed class CountingTicketAppService(TicketAppService inner, TicketBusinessObserver observer) : ITicketAppService
{
    public Task<TicketDto> CreateAsync(CreateTicketDto input, CancellationToken cancellationToken = default)
    {
        observer.RecordBusinessEntry();
        return inner.CreateAsync(input, cancellationToken);
    }

    public Task<TicketDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        observer.RecordBusinessEntry();
        return inner.GetByIdAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<TicketDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        observer.RecordBusinessEntry();
        return inner.GetAllAsync(cancellationToken);
    }

    public Task<TicketDto> UpdateAsync(Guid id, UpdateTicketDto input, CancellationToken cancellationToken = default)
    {
        observer.RecordBusinessEntry();
        return inner.UpdateAsync(id, input, cancellationToken);
    }

    public Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
    {
        observer.RecordBusinessEntry();
        return inner.DeleteAsync(id, expectedStamp, cancellationToken);
    }

    public Task<TicketDto> AssignAsync(Guid id, Guid agentId, CancellationToken cancellationToken = default)
    {
        observer.RecordBusinessEntry();
        return inner.AssignAsync(id, agentId, cancellationToken);
    }

    public Task<TicketDto> ResolveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        observer.RecordBusinessEntry();
        return inner.ResolveAsync(id, cancellationToken);
    }

    public Task<TicketDto> CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        observer.RecordBusinessEntry();
        return inner.CloseAsync(id, cancellationToken);
    }

    public Task<List<TicketDto>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        observer.RecordBusinessEntry();
        return inner.GetByCustomerAsync(customerId, cancellationToken);
    }

    public Task<List<TicketDto>> GetByAssigneeAsync(Guid assigneeId, CancellationToken cancellationToken = default)
    {
        observer.RecordBusinessEntry();
        return inner.GetByAssigneeAsync(assigneeId, cancellationToken);
    }

    public Task<List<TicketDto>> GetOverdueAsync(CancellationToken cancellationToken = default)
    {
        observer.RecordBusinessEntry();
        return inner.GetOverdueAsync(cancellationToken);
    }
}
