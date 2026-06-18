using System.Text.Json;
using Confluent.Kafka;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Publishing;
using CrestCreates.EventBus.Kafka.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests.Integration;

public class KafkaRetryAndDLQTests : KafkaIntegrationTestBase
{
    [Fact]
    public async Task HandlerFailureRetry_ConsumerSeeksBack_Redelivers()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = Options.DefaultTopic;
        var key = EventNamingConvention.GetRoutingKey<KafkaRetryTestEvent>();
        var groupId = $"retry-cg-{Guid.NewGuid():N}";

        await publisher.PublishAsync(topic, new KafkaRetryTestEvent { Message = "Retry" }, key: key, headers: null, CancellationToken.None);

        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        var cr1 = consumer.Consume(TimeSpan.FromSeconds(10));
        cr1.Should().NotBeNull();
        consumer.Seek(cr1.TopicPartitionOffset);
        var cr2 = consumer.Consume(TimeSpan.FromSeconds(10));
        cr2.Should().NotBeNull();
        cr2!.Offset.Should().Be(cr1!.Offset);
        consumer.Commit(cr2);
    }

    [Fact]
    public async Task HandlerFailureDLQ_RetriesExhausted_PublishedToDLQTopic()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var mainTopic = Options.DefaultTopic;

        var dlqEvent = new KafkaDLQTestEvent { Message = "DLQ bound" };
        var payload = JsonSerializer.Serialize(dlqEvent, TestKafkaSerializerContext.Default.KafkaDLQTestEvent);
        var dlqEnvelope = new KafkaMessageEnvelope(typeof(KafkaDLQTestEvent).FullName!, payload, null) { RetryCount = 3 };
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(dlqEnvelope, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);

        await publisher.PublishToDeadLetterTopicAsync(mainTopic, envelopeBytes, key: EventNamingConvention.GetRoutingKey<KafkaDLQTestEvent>(), retryCount: 3, CancellationToken.None);

        var dlqTopic = $"{mainTopic}{Options.DeadLetterTopicSuffix}";
        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = $"dlq-cg-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(dlqTopic);

        var cr = consumer.Consume(TimeSpan.FromSeconds(15));
        cr.Should().NotBeNull();
        var envelope = JsonSerializer.Deserialize(cr.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
        envelope.Should().NotBeNull();
        envelope!.RetryCount.Should().Be(3);
        consumer.Commit(cr);
    }

    [Fact]
    public async Task IdempotentConsumption_ProducerPool_ProducesIdempotentProducer()
    {
        var pool = ServiceProvider.GetRequiredService<KafkaProducerPool>();
        var producer = await pool.GetProducerAsync();
        producer.Should().NotBeNull();
        pool.ReturnProducer(producer);
    }

    [Fact]
    public async Task MultiConsumerGroup_DifferentGroups_EachReceivesFullStream()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = Options.DefaultTopic;
        var key = EventNamingConvention.GetRoutingKey<KafkaRetryTestEvent>();

        await publisher.PublishAsync(topic, new KafkaRetryTestEvent { Message = "Multi-group" }, key: key, headers: null, CancellationToken.None);

        var config1 = new ConsumerConfig { BootstrapServers = Options.BootstrapServers, GroupId = $"multi-cg1-{Guid.NewGuid():N}", AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false };
        var config2 = new ConsumerConfig { BootstrapServers = Options.BootstrapServers, GroupId = $"multi-cg2-{Guid.NewGuid():N}", AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false };

        using var consumer1 = new ConsumerBuilder<string, byte[]>(config1).Build();
        using var consumer2 = new ConsumerBuilder<string, byte[]>(config2).Build();
        consumer1.Subscribe(topic);
        consumer2.Subscribe(topic);

        var cr1 = consumer1.Consume(TimeSpan.FromSeconds(10));
        var cr2 = consumer2.Consume(TimeSpan.FromSeconds(10));
        cr1.Should().NotBeNull();
        cr2.Should().NotBeNull();

        var env1 = JsonSerializer.Deserialize(cr1.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
        var evt1 = JsonSerializer.Deserialize(env1!.Payload, TestKafkaSerializerContext.Default.KafkaRetryTestEvent);
        evt1!.Message.Should().Be("Multi-group");

        var env2 = JsonSerializer.Deserialize(cr2.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
        var evt2 = JsonSerializer.Deserialize(env2!.Payload, TestKafkaSerializerContext.Default.KafkaRetryTestEvent);
        evt2!.Message.Should().Be("Multi-group");

        consumer1.Commit(cr1);
        consumer2.Commit(cr2);
    }
}
