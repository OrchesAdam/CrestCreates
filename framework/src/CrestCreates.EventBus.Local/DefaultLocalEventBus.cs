using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.Local;

public class DefaultLocalEventBus : ILocalEventBus, IEventBus
{
    private readonly ILocalEventDispatcher _dispatcher;
    private readonly IDeadLetterStore? _deadLetterStore;
    private readonly IEventValidator _validator;
    private readonly LocalDeadLetterOptions _deadLetterOptions;

    public DefaultLocalEventBus(
        ILocalEventDispatcher dispatcher,
        IEventValidator validator,
        IDeadLetterStore? deadLetterStore = null,
        IOptions<LocalDeadLetterOptions>? deadLetterOptions = null)
    {
        _dispatcher = dispatcher;
        _validator = validator;
        _deadLetterStore = deadLetterStore;
        _deadLetterOptions = deadLetterOptions?.Value ?? new LocalDeadLetterOptions();
    }

    public async Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
    {
        _validator.ValidateOrThrow(@event.GetType().Name, @event);

        try
        {
            await _dispatcher.DispatchAsync(@event, cancellationToken);
        }
        catch (Exception ex) when (_deadLetterStore is not null)
        {
            await EnqueueToDeadLetterAsync(@event, ex, cancellationToken);
            throw;
        }
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent
    {
        _validator.ValidateOrThrow(@event.GetType().Name, @event);

        try
        {
            await _dispatcher.DispatchAsync(@event, cancellationToken);
        }
        catch (Exception ex) when (_deadLetterStore is not null)
        {
            await EnqueueToDeadLetterAsync(@event, ex, cancellationToken);
            throw;
        }
    }

    Task IEventBus.PublishAsync(IDomainEvent @event, CancellationToken cancellationToken)
    {
        return PublishAsync(@event, cancellationToken);
    }

    Task IEventBus.PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
    {
        return PublishAsync(@event, cancellationToken);
    }

    void IEventBus.Subscribe<TEvent, THandler>()
    {
    }

    void IEventBus.Unsubscribe<TEvent, THandler>()
    {
    }

    private async Task EnqueueToDeadLetterAsync(ILocalEvent @event, Exception ex, CancellationToken cancellationToken)
    {
        if (_deadLetterStore is null) return;

        var eventType = @event.GetType();
        var payload = JsonSerializer.SerializeToUtf8Bytes(@event, eventType);

        var message = new DeadLetterMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            EventName: eventType.Name,
            EventVersion: 1,
            EventDescriptorId: null,
            CorrelationId: null,
            TenantId: null,
            Scope: CrestCreates.Event.Abstractions.EventScope.Local,
            PayloadTypeFullName: eventType.AssemblyQualifiedName!,
            Payload: payload,
            ErrorMessage: ex.Message,
            ExceptionType: ex.GetType().FullName,
            OccurredAt: DateTime.UtcNow,
            FailedAt: DateTime.UtcNow,
            RetryCount: 0,
            MaxRetries: _deadLetterOptions.MaxRetries,
            Status: DeadLetterStatus.Pending);

        await _deadLetterStore.EnqueueAsync(message, cancellationToken);
    }
}
