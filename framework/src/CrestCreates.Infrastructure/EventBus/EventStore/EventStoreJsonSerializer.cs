using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.Infrastructure.EventBus.EventStore
{
    public class EventStoreJsonSerializer : IEventStoreSerializer
    {
        private readonly JsonSerializerOptions _jsonSerializerOptions;

        public EventStoreJsonSerializer()
        {
            _jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                IncludeFields = true,
                TypeInfoResolver = new EventTypeResolver()
            };
        }

        public string Serialize(IDomainEvent @event)
        {
            return JsonSerializer.Serialize(@event, @event.GetType(), _jsonSerializerOptions);
        }

        public IDomainEvent Deserialize(string data, Type eventType)
        {
            return (IDomainEvent)JsonSerializer.Deserialize(data, eventType, _jsonSerializerOptions);
        }

        private class EventTypeResolver : DefaultJsonTypeInfoResolver
        {
            public EventTypeResolver()
            {
                // 可以在这里添加自定义类型解析逻辑
            }
        }
    }
}