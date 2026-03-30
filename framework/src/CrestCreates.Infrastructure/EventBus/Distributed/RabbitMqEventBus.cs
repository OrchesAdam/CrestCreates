using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Infrastructure.EventBus;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CrestCreates.Infrastructure.EventBus.Distributed
{
    public class RabbitMqEventBus : DistributedEventBusBase
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly string _exchangeName = "domain-events";
        private readonly ConcurrentDictionary<string, Type> _eventTypes = new ConcurrentDictionary<string, Type>();
        private readonly ConcurrentDictionary<Type, List<Type>> _handlers = new ConcurrentDictionary<Type, List<Type>>();

        public RabbitMqEventBus(string connectionString)
        {
            var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _channel.ExchangeDeclare(
                exchange: _exchangeName,
                type: ExchangeType.Fanout,
                durable: true,
                autoDelete: false
            );

            StartConsuming();
        }

        public override async Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
        {
            var eventName = GetEventName(@event.GetType());
            var message = SerializeEvent(@event);

            var body = System.Text.Encoding.UTF8.GetBytes(message);

            _channel.BasicPublish(
                exchange: _exchangeName,
                routingKey: string.Empty,
                basicProperties: null,
                body: body
            );

            await Task.CompletedTask;
        }

        public override async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        {
            await PublishAsync((IDomainEvent)@event, cancellationToken);
        }

        public override void Subscribe<TEvent, THandler>()
        {
            var eventType = typeof(TEvent);
            var handlerType = typeof(THandler);

            if (!_handlers.TryGetValue(eventType, out var handlerList))
            {
                handlerList = new List<Type>();
                _handlers[eventType] = handlerList;
            }

            if (!handlerList.Contains(handlerType))
            {
                handlerList.Add(handlerType);
            }

            var eventName = GetEventName(eventType);
            _eventTypes[eventName] = eventType;
        }

        public override void Unsubscribe<TEvent, THandler>()
        {
            var eventType = typeof(TEvent);
            var handlerType = typeof(THandler);

            if (_handlers.TryGetValue(eventType, out var handlerList))
            {
                handlerList.Remove(handlerType);
                if (handlerList.Count == 0)
                {
                    _handlers.TryRemove(eventType, out _);
                }
            }
        }

        private void StartConsuming()
        {
            var queueName = _channel.QueueDeclare().QueueName;
            _channel.QueueBind(
                queue: queueName,
                exchange: _exchangeName,
                routingKey: string.Empty
            );

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = System.Text.Encoding.UTF8.GetString(body);
                var eventName = ea.RoutingKey;

                if (_eventTypes.TryGetValue(eventName, out var eventType))
                {
                    var @event = System.Text.Json.JsonSerializer.Deserialize(message, eventType);
                    if (@event is IDomainEvent domainEvent)
                    {
                        await HandleEventAsync(domainEvent, eventType);
                    }
                }
            };

            _channel.BasicConsume(
                queue: queueName,
                autoAck: true,
                consumer: consumer
            );
        }

        private async Task HandleEventAsync(IDomainEvent @event, Type eventType)
        {
            if (_handlers.TryGetValue(eventType, out var handlerTypes))
            {
                foreach (var handlerType in handlerTypes)
                {
                    var handler = Activator.CreateInstance(handlerType);
                    if (handler is IEventHandler<IDomainEvent> eventHandler)
                    {
                        await eventHandler.HandleAsync(@event);
                    }
                }
            }
        }

        public void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
        }
    }
}