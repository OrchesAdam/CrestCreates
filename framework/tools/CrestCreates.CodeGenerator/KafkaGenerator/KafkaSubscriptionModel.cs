// framework/tools/CrestCreates.CodeGenerator/KafkaGenerator/KafkaSubscriptionModel.cs
using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.KafkaGenerator
{
    internal sealed class KafkaSubscriptionInfo
    {
        public string Topic { get; set; } = string.Empty;
        public string EventTypeFullName { get; set; } = string.Empty;
        public string HandlerTypeFullName { get; set; } = string.Empty;
        public string HandlerMethodName { get; set; } = string.Empty;
        public string? ConsumerGroup { get; set; }
    }

    internal sealed class KafkaSubscriptionModel
    {
        public string Namespace { get; set; } = "CrestCreates.EventBus.Kafka.Generated";
        public List<KafkaSubscriptionInfo> Subscriptions { get; set; } = new();
    }
}
