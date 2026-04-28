using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    /// <summary>
    /// Marks a method as a Kafka message handler. The source generator will
    /// discover methods marked with this attribute and generate handler invokers.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class KafkaSubscribeAttribute : Attribute
    {
        /// <summary>
        /// Gets the topic to subscribe to.
        /// </summary>
        public string Topic { get; }

        /// <summary>
        /// Gets or sets an optional consumer group override for this subscription.
        /// If not specified, the default consumer group from KafkaOptions is used.
        /// </summary>
        public string? ConsumerGroup { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="KafkaSubscribeAttribute"/> class.
        /// </summary>
        /// <param name="topic">The topic to subscribe to.</param>
        /// <exception cref="ArgumentException">Thrown when topic is null or whitespace.</exception>
        public KafkaSubscribeAttribute(string topic)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(topic);
            Topic = topic;
        }
    }
}
