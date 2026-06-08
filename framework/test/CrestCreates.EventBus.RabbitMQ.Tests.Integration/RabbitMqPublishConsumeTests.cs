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

public class RabbitMqPublishConsumeTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task PublishAndConsume_EventPublished_ConsumerReceives()
    {
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<TestRabbitEvent>();
        var queueName = $"test-queue-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            // Publisher declares exchange as Direct; we must use Direct here too
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true);
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queueName, exchange, routingKey);

            var testEvent = new TestRabbitEvent { Message = "Hello RabbitMQ" };
            var tcs = new TaskCompletionSource<TestRabbitEvent?>();

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                var envelope = JsonSerializer.Deserialize(ea.Body.Span, RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
                var eventData = JsonSerializer.Deserialize<TestRabbitEvent>(envelope!.Payload);
                tcs.SetResult(eventData);
                await Task.CompletedTask;
            };
            await channel.BasicConsumeAsync(queueName, autoAck: true, consumer);

            await publisher.PublishAsync(testEvent, exchange, routingKey, null, CancellationToken.None);

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            received.Should().NotBeNull();
            received!.Message.Should().Be("Hello RabbitMQ");
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task MultiHandlerDispatch_MultipleQueues_AllReceive()
    {
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<TestRabbitEvent>();
        var queue1 = $"multi-q1-{Guid.NewGuid():N}";
        var queue2 = $"multi-q2-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true);
            await channel.QueueDeclareAsync(queue1, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueDeclareAsync(queue2, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queue1, exchange, routingKey);
            await channel.QueueBindAsync(queue2, exchange, routingKey);

            var tcs1 = new TaskCompletionSource<bool>();
            var tcs2 = new TaskCompletionSource<bool>();
            var consumer1 = new AsyncEventingBasicConsumer(channel);
            consumer1.ReceivedAsync += async (_, _) => { tcs1.SetResult(true); await Task.CompletedTask; };
            await channel.BasicConsumeAsync(queue1, autoAck: true, consumer1);
            var consumer2 = new AsyncEventingBasicConsumer(channel);
            consumer2.ReceivedAsync += async (_, _) => { tcs2.SetResult(true); await Task.CompletedTask; };
            await channel.BasicConsumeAsync(queue2, autoAck: true, consumer2);

            await publisher.PublishAsync(new TestRabbitEvent { Message = "Broadcast" }, exchange, routingKey, null, CancellationToken.None);

            (await tcs1.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
            (await tcs2.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task LargePayload_TransmittedCorrectly()
    {
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<TestRabbitEvent>();
        var queueName = $"large-q-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true);
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queueName, exchange, routingKey);

            var largeMessage = new string('x', 100_000);
            var tcs = new TaskCompletionSource<string?>();
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                var envelope = JsonSerializer.Deserialize(ea.Body.Span, RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
                var evt = JsonSerializer.Deserialize<TestRabbitEvent>(envelope!.Payload);
                tcs.SetResult(evt!.Message);
                await Task.CompletedTask;
            };
            await channel.BasicConsumeAsync(queueName, autoAck: true, consumer);

            await publisher.PublishAsync(new TestRabbitEvent { Message = largeMessage }, exchange, routingKey, null, CancellationToken.None);

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
            received.Should().Be(largeMessage);
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task PublisherConfirmation_PublishDoesNotThrow()
    {
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<TestRabbitEvent>();
        var queueName = $"confirm-q-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            // Must declare exchange and bind a queue so mandatory publish succeeds
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true);
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queueName, exchange, routingKey);

            var exception = await Record.ExceptionAsync(() =>
                publisher.PublishAsync(new TestRabbitEvent { Message = "Confirm" }, exchange, routingKey, null, CancellationToken.None));

            exception.Should().BeNull();
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task ConcurrentPublishers_AllSucceed()
    {
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<TestRabbitEvent>();
        var queueName = $"concurrent-q-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true);
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queueName, exchange, routingKey);

            var tasks = new Task[10];
            for (int i = 0; i < 10; i++)
            {
                var idx = i;
                tasks[i] = publisher.PublishAsync(
                    new TestRabbitEvent { Message = $"Concurrent {idx}" }, exchange, routingKey, null, CancellationToken.None);
            }

            var exception = await Record.ExceptionAsync(() => Task.WhenAll(tasks));
            exception.Should().BeNull();
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }
}

public sealed class TestRabbitEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}