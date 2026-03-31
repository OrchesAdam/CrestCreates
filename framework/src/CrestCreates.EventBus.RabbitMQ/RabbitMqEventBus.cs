using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;

namespace CrestCreates.EventBus.RabbitMQ
{
    public class RabbitMqEventBus : DistributedEventBusBase
    {
        private readonly string _connectionString;

        public RabbitMqEventBus(string connectionString)
        {
            _connectionString = connectionString;
        }

        public override async Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
        {
            // 实现RabbitMQ事件发布逻辑
            // 这里是示例实现，实际项目中需要使用RabbitMQ客户端库
            await Task.CompletedTask;
        }

        public override async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        {
            // 实现RabbitMQ事件发布逻辑
            await Task.CompletedTask;
        }

        public override void Subscribe<TEvent, THandler>()
        {
            // 实现RabbitMQ事件订阅逻辑
        }

        public override void Unsubscribe<TEvent, THandler>()
        {
            // 实现RabbitMQ事件取消订阅逻辑
        }
    }
}