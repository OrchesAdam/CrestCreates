// framework/tools/CrestCreates.CodeGenerator/RabbitMqGenerator/RabbitMqSubscriptionModel.cs
using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.RabbitMqGenerator
{
    internal sealed class RabbitMqSubscriptionModel
    {
        public string Namespace { get; set; } = string.Empty;
        public List<SubscriptionInfo> Subscriptions { get; set; } = new();
    }

    internal sealed class SubscriptionInfo
    {
        public string EventType { get; set; } = string.Empty;
        public string HandlerType { get; set; } = string.Empty;
        public string HandlerMethod { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string Queue { get; set; } = string.Empty;
        public int PrefetchCount { get; set; } = 10;
    }
}
