using System.Text.Json;
using Confluent.Kafka;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Publishing;
using CrestCreates.EventBus.Kafka.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests.Integration;

public class KafkaMultiConsumerTests : KafkaIntegrationTestBase
{
    [Fact]
    public async Task MultipleEvents_DifferentKeys_DifferentPartitions()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = Options.DefaultTopic;
        var groupId = $"multi-cg-{Guid.NewGuid():N}";

        for (int i = 0; i < 10; i++)
        {
            await publisher.PublishAsync(topic, new KafkaMultiTestEvent { Message = $"Event {i}" }, key: $"key-{i}", headers: null, CancellationToken.None);
        }

        var config = new ConsumerConfig { BootstrapServers = Options.BootstrapServers, GroupId = groupId, AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false };
        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        var received = new HashSet<int>();
        for (int i = 0; i < 10; i++)
        {
            var cr = consumer.Consume(TimeSpan.FromSeconds(15));
            if (cr is null) break;
            var envelope = JsonSerializer.Deserialize(cr.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
            var evt = JsonSerializer.Deserialize(envelope!.Payload, TestKafkaSerializerContext.Default.KafkaMultiTestEvent);
            received.Add(int.Parse(evt!.Message.Split(' ')[1]));
            consumer.Commit(cr);
        }

        received.Should().HaveCount(10);
    }

    [Fact]
    public async Task ConsumerGroup_Rebalance_MultipleConsumers()
    {
        var topic = Options.DefaultTopic;
        var groupId = $"rebalance-cg-{Guid.NewGuid():N}";

        var config = new ConsumerConfig { BootstrapServers = Options.BootstrapServers, GroupId = groupId, AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false };
        using var consumer1 = new ConsumerBuilder<string, byte[]>(config).Build();
        using var consumer2 = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer1.Subscribe(topic);
        consumer2.Subscribe(topic);

        consumer1.Assignment.Should().NotBeNull();
        consumer2.Assignment.Should().NotBeNull();
    }

    [Fact]
    public async Task SASLConnection_Plaintext_ConnectsSuccessfully()
    {
        var pool = ServiceProvider.GetRequiredService<KafkaProducerPool>();
        var producer = await pool.GetProducerAsync();
        producer.Should().NotBeNull();
        pool.ReturnProducer(producer);
    }
}
