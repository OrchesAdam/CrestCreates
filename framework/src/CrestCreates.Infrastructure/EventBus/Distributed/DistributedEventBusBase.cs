using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Infrastructure.EventBus;

namespace CrestCreates.Infrastructure.EventBus.Distributed
{
    public abstract class DistributedEventBusBase : IEventBus
    {
        protected readonly JsonSerializerOptions JsonSerializerOptions;

        protected DistributedEventBusBase()
        {
            JsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                IncludeFields = true
            };
        }

        public abstract Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default);
        
        public abstract Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IDomainEvent;
        
        public abstract void Subscribe<TEvent, THandler>() where TEvent : IDomainEvent where THandler : IEventHandler<TEvent>;
        
        public abstract void Unsubscribe<TEvent, THandler>() where TEvent : IDomainEvent where THandler : IEventHandler<TEvent>;

        protected string SerializeEvent(IDomainEvent @event)
        {
            return JsonSerializer.Serialize(@event, @event.GetType(), JsonSerializerOptions);
        }

        protected TEvent DeserializeEvent<TEvent>(string json) where TEvent : IDomainEvent
        {
            return JsonSerializer.Deserialize<TEvent>(json, JsonSerializerOptions);
        }

        protected string GetEventName(Type eventType)
        {
            return eventType.FullName;
        }
    }
}