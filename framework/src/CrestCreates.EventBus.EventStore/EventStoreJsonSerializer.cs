using System;
using System.Text.Json;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;

namespace CrestCreates.EventBus.EventStore
{
    public class EventStoreJsonSerializer : IEventStoreSerializer
    {
        private readonly JsonSerializerOptions _options;

        public EventStoreJsonSerializer()
        {
            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
        }

        public string Serialize(IDomainEvent @event)
        {
            return JsonSerializer.Serialize(@event, _options);
        }

        public IDomainEvent Deserialize(string json, Type eventType)
        {
            return (IDomainEvent)JsonSerializer.Deserialize(json, eventType, _options);
        }
    }
}