using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class RabbitMqSubscribeAttribute : Attribute
    {
        public string EventType { get; }
        public string? Exchange { get; set; }
        public string? Queue { get; set; }
        public int PrefetchCount { get; set; } = 10;

        public RabbitMqSubscribeAttribute(string eventType)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
            EventType = eventType;
        }
    }
}
