using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.UnitOfWork;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Application.Services;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// Test-side observation facility for issue #129. It records whether a Ticket write reached the
/// business service, whether a unit of work was executed, and lets the tests compare persisted
/// state. Nothing here is exposed from production code; the decorators are installed only in the
/// measurement host built by <see cref="TicketProjectionGapBaselineTests"/>.
/// </summary>
internal sealed class TicketWriteSideEffectProbe
{
    public List<string> ServiceCalls { get; } = new();

    /// <summary>Logical kernel entries (Execute / ExecuteAsync). Says nothing about transactions.</summary>
    public int LogicalExecutions { get; set; }

    /// <summary>
    /// Real unit-of-work starts observed where the kernel makes them observable: inside the Execute
    /// delegate frame, or after IUnitOfWorkScope.StartAsync. Counts only frames where the ambient unit
    /// of work was actually resolvable, so a no-op wrapper cannot inflate it.
    /// </summary>
    public int TransactionsStarted { get; set; }

    /// <summary>Frames that claimed to execute but had no ambient unit of work — must stay 0.</summary>
    public int UnobservedExecutions { get; set; }

    /// <summary>Token the business service actually received from the generated invoker.</summary>
    public CancellationToken LastServiceToken { get; set; }

    /// <summary>Injects a failure at the business-service boundary (failure-bridge probes).</summary>
    public Exception? ServiceFailure { get; set; }

    public bool ReviewThrowLiveTokenOce { get; set; }

    public void Reset()
    {
        ServiceCalls.Clear();
        LogicalExecutions = 0;
        TransactionsStarted = 0;
        UnobservedExecutions = 0;
        LastServiceToken = default;
        ServiceFailure = null;
        ReviewThrowLiveTokenOce = false;
    }
}

internal sealed class CountingTicketAppService : ITicketAppService
{
    private readonly ITicketAppService _inner;
    private readonly TicketWriteSideEffectProbe _probe;

    internal CountingTicketAppService(ITicketAppService inner, TicketWriteSideEffectProbe probe)
    {
        _inner = inner;
        _probe = probe;
    }

    private Task<T> Tracked<T>(string name, CancellationToken cancellationToken, Func<Task<T>> invoke)
    {
        _probe.ServiceCalls.Add(name);
        _probe.LastServiceToken = cancellationToken;
        if (_probe.ReviewThrowLiveTokenOce) throw new OperationCanceledException(cancellationToken);
        if (_probe.ServiceFailure is { } failure)
        {
            throw failure;
        }

        return invoke();
    }

    public Task<TicketDto> CreateAsync(CreateTicketDto input, CancellationToken cancellationToken = default)
        => Tracked(nameof(CreateAsync), cancellationToken, () => _inner.CreateAsync(input, cancellationToken));

    public Task<TicketDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Tracked(nameof(GetByIdAsync), cancellationToken, () => _inner.GetByIdAsync(id, cancellationToken));

    public Task<IReadOnlyList<TicketDto>> GetAllAsync(CancellationToken cancellationToken = default)
        => Tracked(nameof(GetAllAsync), cancellationToken, () => _inner.GetAllAsync(cancellationToken));

    public Task<TicketDto> UpdateAsync(Guid id, UpdateTicketDto input, CancellationToken cancellationToken = default)
        => Tracked(nameof(UpdateAsync), cancellationToken, () => _inner.UpdateAsync(id, input, cancellationToken));

    public async Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
    {
        _probe.ServiceCalls.Add(nameof(DeleteAsync));
        _probe.LastServiceToken = cancellationToken;
        if (_probe.ReviewThrowLiveTokenOce) throw new OperationCanceledException(cancellationToken);
        if (_probe.ServiceFailure is { } failure)
        {
            throw failure;
        }

        await _inner.DeleteAsync(id, expectedStamp, cancellationToken);
    }

    public Task<TicketDto> AssignAsync(Guid id, Guid agentId, CancellationToken cancellationToken = default)
        => Tracked(nameof(AssignAsync), cancellationToken, () => _inner.AssignAsync(id, agentId, cancellationToken));

    public Task<TicketDto> ResolveAsync(Guid id, CancellationToken cancellationToken = default)
        => Tracked(nameof(ResolveAsync), cancellationToken, () => _inner.ResolveAsync(id, cancellationToken));

    public Task<TicketDto> CloseAsync(Guid id, CancellationToken cancellationToken = default)
        => Tracked(nameof(CloseAsync), cancellationToken, () => _inner.CloseAsync(id, cancellationToken));

    public Task<List<TicketDto>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
        => Tracked(nameof(GetByCustomerAsync), cancellationToken, () => _inner.GetByCustomerAsync(customerId, cancellationToken));

    public Task<List<TicketDto>> GetByAssigneeAsync(Guid assigneeId, CancellationToken cancellationToken = default)
        => Tracked(nameof(GetByAssigneeAsync), cancellationToken, () => _inner.GetByAssigneeAsync(assigneeId, cancellationToken));

    public Task<List<TicketDto>> GetOverdueAsync(CancellationToken cancellationToken = default)
        => Tracked(nameof(GetOverdueAsync), cancellationToken, () => _inner.GetOverdueAsync(cancellationToken));
}

internal sealed class CountingUnitOfWorkManager : IUnitOfWorkManager
{
    private readonly IUnitOfWorkManager _inner;
    private readonly TicketWriteSideEffectProbe _probe;

    internal CountingUnitOfWorkManager(IUnitOfWorkManager inner, TicketWriteSideEffectProbe probe)
    {
        _inner = inner;
        _probe = probe;
    }

    public IUnitOfWork? CurrentOrNull => _inner.CurrentOrNull;
    public IUnitOfWork Current => _inner.Current;

    public IUnitOfWorkScope BeginScope(UnitOfWorkOptions? options = null)
        => new CountingUnitOfWorkScope(_inner.BeginScope(options), this);

    internal void ObserveScopeStarted()
    {
        if (_inner.CurrentOrNull is null)
        {
            _probe.UnobservedExecutions++;
        }
        else
        {
            _probe.TransactionsStarted++;
        }
    }

    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        UnitOfWorkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _probe.LogicalExecutions++;
        return _inner.ExecuteAsync(
            async token =>
            {
                ObserveAmbient();
                return await action(token);
            },
            options,
            cancellationToken);
    }

    public TResult Execute<TResult>(
        Func<TResult> action,
        UnitOfWorkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _probe.LogicalExecutions++;
        return _inner.Execute(
            () =>
            {
                ObserveAmbient();
                return action();
            },
            options,
            cancellationToken);
    }

    private void ObserveAmbient()
    {
        if (_inner.CurrentOrNull is null)
        {
            _probe.UnobservedExecutions++;
        }
        else
        {
            _probe.TransactionsStarted++;
        }
    }
}

internal sealed class CountingUnitOfWorkScope : IUnitOfWorkScope, IUnitOfWorkCancellationSource
{
    private readonly IUnitOfWorkScope _inner;
    private readonly CountingUnitOfWorkManager _manager;

    internal CountingUnitOfWorkScope(IUnitOfWorkScope inner, CountingUnitOfWorkManager manager)
    {
        _inner = inner;
        _manager = manager;
    }

    public IUnitOfWork UnitOfWork => _inner.UnitOfWork;
    public bool IsOwner => _inner.IsOwner;
    public bool IsTransactional => _inner.IsTransactional;
    public UnitOfWorkState State => _inner.State;
    public UnitOfWorkTransactionOutcome TransactionOutcome => _inner.TransactionOutcome;
    public UnitOfWorkNotificationOutcome NotificationOutcome => _inner.NotificationOutcome;
    public bool IsReleased => _inner.IsReleased;
    public Exception? RollbackTerminationFailure => _inner.RollbackTerminationFailure;
    public CancellationToken ExecutionToken => _inner.ExecutionToken;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _inner.StartAsync(cancellationToken);
        _manager.ObserveScopeStarted();
    }

    public CrestCreates.Core.Abstractions.Threading.UnitOfWorkCancellationSnapshot? CaptureCancellationSnapshot()
        => (_inner as IUnitOfWorkCancellationSource)?.CaptureCancellationSnapshot();

    public Task CompleteAsync(CancellationToken cancellationToken = default) => _inner.CompleteAsync(cancellationToken);
    public Task RollbackAsync(CancellationToken cancellationToken = default) => _inner.RollbackAsync(cancellationToken);
    public void Dispose() => _inner.Dispose();
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

internal static class TicketProbeHostBuilderExtensions
{
    internal static IServiceCollection AddTicketSideEffectProbe(
        this IServiceCollection services,
        TicketWriteSideEffectProbe probe)
    {
        var manager = services.Last(d => d.ServiceType == typeof(IUnitOfWorkManager));
        services.Remove(manager);
        services.Add(new ServiceDescriptor(
            typeof(IUnitOfWorkManager),
            sp => new CountingUnitOfWorkManager((IUnitOfWorkManager)manager.ImplementationFactory!(sp), probe),
            manager.Lifetime));

        var service = services.Last(d => d.ServiceType == typeof(ITicketAppService));
        services.Remove(service);
        services.Add(new ServiceDescriptor(
            typeof(ITicketAppService),
            sp => new CountingTicketAppService(
                (ITicketAppService)ActivatorUtilities.CreateInstance(sp, service.ImplementationType!), probe),
            service.Lifetime));

        return services;
    }
}
