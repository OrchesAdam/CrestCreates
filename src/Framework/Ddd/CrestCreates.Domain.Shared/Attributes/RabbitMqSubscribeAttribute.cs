using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    /// <summary>
    /// Compile-time subscription to a canonical Integration event on the RabbitMQ transport. The event
    /// name argument references the canonical [CrestEvent] Name; the payload type is taken from the
    /// handler method parameter, never from a string.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class RabbitMqSubscribeAttribute : Attribute
    {
        public string EventType { get; }
        public string? Exchange { get; set; }
        public string? Queue { get; set; }

        /// <summary>Optional explicit routing key; defaults to the canonical event name.</summary>
        public string? RoutingKey { get; set; }

        /// <summary>Optional explicit canonical version; 0 (default) resolves the unique Active version at freeze.</summary>
        public int Version { get; set; }

        public int PrefetchCount { get; set; } = 10;

        public RabbitMqSubscribeAttribute(string eventType)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
            EventType = eventType;
        }
    }
}
