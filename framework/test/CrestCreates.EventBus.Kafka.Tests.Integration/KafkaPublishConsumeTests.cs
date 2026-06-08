using System.Text.Json;
using Confluent.Kafka;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Kafka.Publishing;
using CrestCreates.EventBus.Kafka.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests.Integration;

public class KafkaPublishConsumeTests : KafkaIntegrationTestBase
{
    [Fact]
    public async Task PublishAndConsume_EventPublished_ConsumerReceives()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = Options.DefaultTopic;
        var key = EventNamingConvention.GetRoutingKey<TestKafkaEvent>();
        var groupId = $"test-cg-{Guid.NewGuid():N}";

        await publisher.PublishAsync(topic, new TestKafkaEvent { Message = "Hello Kafka" }, key: key, headers: null, CancellationToken.None);

        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        var cr = consumer.Consume(TimeSpan.FromSeconds(15));
        cr.Should().NotBeNull();
        var envelope = JsonSerializer.Deserialize(cr.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
        envelope.Should().NotBeNull();
        var eventData = JsonSerializer.Deserialize(envelope!.Payload, TestKafkaSerializerContext.Default.TestKafkaEvent);
        eventData!.Message.Should().Be("Hello Kafka");
        consumer.Commit(cr);
    }

    [Fact]
    public async Task PartitionOrdering_SameKey_SamePartition()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = Options.DefaultTopic;
        var key = EventNamingConvention.GetRoutingKey<TestKafkaEvent>();
        var groupId = $"order-cg-{Guid.NewGuid():N}";

        for (int i = 0; i < 5; i++)
        {
            await publisher.PublishAsync(topic, new TestKafkaEvent { Message = $"Ordered {i}" }, key: key, headers: null, CancellationToken.None);
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        var messages = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var cr = consumer.Consume(TimeSpan.FromSeconds(10));
            if (cr is null) break;
            var envelope = JsonSerializer.Deserialize(cr.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
            var evt = JsonSerializer.Deserialize(envelope!.Payload, TestKafkaSerializerContext.Default.TestKafkaEvent);
            messages.Add(evt!.Message);
            consumer.Commit(cr);
        }

        messages.Should().HaveCount(5);
        messages.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task LargePayload_TransmittedCorrectly()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = Options.DefaultTopic;
        var key = EventNamingConvention.GetRoutingKey<TestKafkaEvent>();
        var groupId = $"large-cg-{Guid.NewGuid():N}";

        var largeMessage = new string('y', 100_000);
        await publisher.PublishAsync(topic, new TestKafkaEvent { Message = largeMessage }, key: key, headers: null, CancellationToken.None);

        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            MaxPartitionFetchBytes = 5 * 1024 * 1024
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        var cr = consumer.Consume(TimeSpan.FromSeconds(20));
        cr.Should().NotBeNull();
        var envelope = JsonSerializer.Deserialize(cr.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
        var evt = JsonSerializer.Deserialize(envelope!.Payload, TestKafkaSerializerContext.Default.TestKafkaEvent);
        evt!.Message.Should().Be(largeMessage);
        consumer.Commit(cr);
    }

    [Fact]
    public async Task ConcurrentPublishers_AllSucceed()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = Options.DefaultTopic;
        var key = EventNamingConvention.GetRoutingKey<TestKafkaEvent>();

        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            var idx = i;
            tasks[i] = publisher.PublishAsync(topic, new TestKafkaEvent { Message = $"Concurrent {idx}" }, key: key, headers: null, CancellationToken.None);
        }

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(tasks));
        exception.Should().BeNull();
    }

    [Fact]
    public async Task ManualOffsetCommit_CommitDoesNotThrow()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = Options.DefaultTopic;
        var key = EventNamingConvention.GetRoutingKey<TestKafkaEvent>();
        var groupId = $"commit-cg-{Guid.NewGuid():N}";

        await publisher.PublishAsync(topic, new TestKafkaEvent { Message = "Commit test" }, key: key, headers: null, CancellationToken.None);

        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        var cr = consumer.Consume(TimeSpan.FromSeconds(10));
        cr.Should().NotBeNull();
        consumer.Commit(cr);
    }
}
