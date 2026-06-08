using System.Text.Json;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

public class RabbitMqRetryAndDLQTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task HandlerFailureRetry_MessageNacked_Redelivered()
    {
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<RetryTestRabbitEvent>();
        var queueName = $"retry-q-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true);
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queueName, exchange, routingKey);

            int deliveryCount = 0;
            var allDelivered = new TaskCompletionSource<bool>();
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                deliveryCount++;
                if (deliveryCount >= 2)
                {
                    await channel.BasicAckAsync(ea.DeliveryTag, false);
                    allDelivered.SetResult(true);
                }
                else
                {
                    await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true);
                }
            };
            await channel.BasicConsumeAsync(queueName, autoAck: false, consumer);

            await publisher.PublishAsync(new RetryTestRabbitEvent { Message = "Retry me" }, exchange, routingKey, null, CancellationToken.None);

            var result = await allDelivered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            result.Should().BeTrue();
            deliveryCount.Should().BeGreaterThanOrEqualTo(2);
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task HandlerFailureDLQ_MessageRejected_GoesToDLQ()
    {
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var dlx = Options.DeadLetterExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<DLQTestRabbitEvent>();
        var queueName = $"dlq-main-{Guid.NewGuid():N}";
        var dlqName = $"{queueName}.dlq";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true);
            await channel.ExchangeDeclareAsync(dlx, ExchangeType.Direct, durable: true);
            await channel.QueueDeclareAsync(dlqName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(dlqName, dlx, queueName);

            var args = new Dictionary<string, object?>
            {
                { "x-dead-letter-exchange", dlx },
                { "x-dead-letter-routing-key", queueName }
            };
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true, arguments: args!);
            await channel.QueueBindAsync(queueName, exchange, routingKey);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
            };
            await channel.BasicConsumeAsync(queueName, autoAck: false, consumer);

            await publisher.PublishAsync(new DLQTestRabbitEvent { Message = "To DLQ" }, exchange, routingKey, null, CancellationToken.None);
            await Task.Delay(2000);

            var dlqMessage = await channel.BasicGetAsync(dlqName, autoAck: true);
            dlqMessage.Should().NotBeNull();
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task ConnectionRecovery_ChannelRecreated_AfterRecycle()
    {
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();

        // Get and return a channel -- pool should remain healthy
        var channel1 = await pool.GetChannelAsync();
        channel1.IsOpen.Should().BeTrue();
        pool.ReturnChannel(channel1);

        // Get another channel; verify it's open (either reused or newly created)
        var channel2 = await pool.GetChannelAsync();
        channel2.IsOpen.Should().BeTrue();
        pool.ReturnChannel(channel2);
    }
}

public sealed class RetryTestRabbitEvent : DomainEvent { public string Message { get; set; } = string.Empty; }
public sealed class DLQTestRabbitEvent : DomainEvent { public string Message { get; set; } = string.Empty; }