using System.Text.Json;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

public class RabbitMqMultiHandlerTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task MultipleQueues_SameExchange_AllReceiveCopy()
    {
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<MultiHandlerRabbitEvent>();
        var queueA = $"multi-a-{Guid.NewGuid():N}";
        var queueB = $"multi-b-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true);
            await channel.QueueDeclareAsync(queueA, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueDeclareAsync(queueB, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queueA, exchange, routingKey);
            await channel.QueueBindAsync(queueB, exchange, routingKey);

            var tcsA = new TaskCompletionSource<string?>();
            var tcsB = new TaskCompletionSource<string?>();
            var consumerA = new AsyncEventingBasicConsumer(channel);
            consumerA.ReceivedAsync += async (_, ea) =>
            {
                var envelope = JsonSerializer.Deserialize(ea.Body.Span, RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
                var evt = JsonSerializer.Deserialize<MultiHandlerRabbitEvent>(envelope!.Payload);
                tcsA.SetResult(evt!.Message);
                await Task.CompletedTask;
            };
            await channel.BasicConsumeAsync(queueA, autoAck: true, consumerA);
            var consumerB = new AsyncEventingBasicConsumer(channel);
            consumerB.ReceivedAsync += async (_, ea) =>
            {
                var envelope = JsonSerializer.Deserialize(ea.Body.Span, RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
                var evt = JsonSerializer.Deserialize<MultiHandlerRabbitEvent>(envelope!.Payload);
                tcsB.SetResult(evt!.Message);
                await Task.CompletedTask;
            };
            await channel.BasicConsumeAsync(queueB, autoAck: true, consumerB);

            await publisher.PublishAsync(new MultiHandlerRabbitEvent { Message = "Fanout" }, exchange, routingKey, null, CancellationToken.None);

            (await tcsA.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("Fanout");
            (await tcsB.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("Fanout");
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task IdempotentConsumption_SameMessagePublishedTwice_DeliveredTwice()
    {
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<MultiHandlerRabbitEvent>();
        var queueName = $"idem-q-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true);
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queueName, exchange, routingKey);

            int receiveCount = 0;
            var done = new TaskCompletionSource<bool>();
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, _) =>
            {
                receiveCount++;
                if (receiveCount >= 2) done.SetResult(true);
                await Task.CompletedTask;
            };
            await channel.BasicConsumeAsync(queueName, autoAck: true, consumer);

            await publisher.PublishAsync(new MultiHandlerRabbitEvent { Message = "Dup" }, exchange, routingKey, null, CancellationToken.None);
            await publisher.PublishAsync(new MultiHandlerRabbitEvent { Message = "Dup" }, exchange, routingKey, null, CancellationToken.None);

            (await done.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
            receiveCount.Should().Be(2);
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }
}

public sealed class MultiHandlerRabbitEvent : DomainEvent { public string Message { get; set; } = string.Empty; }