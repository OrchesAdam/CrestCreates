using System.Collections.Generic;
using CrestCreates.EventBus.Kafka.Options;

namespace CrestCreates.EventBus.Kafka.Generated;

/// <summary>
/// Partial class that provides subscription information.
/// The source generator will provide the implementation.
/// </summary>
public static partial class KafkaSubscriptionRegistry
{
    /// <summary>
    /// Gets the list of Kafka subscriptions discovered at compile time.
    /// Returns an empty list if no subscriptions are found.
    /// </summary>
    public static IReadOnlyList<KafkaSubscriptionInfo> GetSubscriptions() => new List<KafkaSubscriptionInfo>();
}