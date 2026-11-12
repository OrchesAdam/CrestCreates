using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using CrestCreates.EventBus.Kafka.Consuming;
using CrestCreates.EventBus.Kafka.Extensions;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests.Integration;

public sealed class KafkaSettlementTests : KafkaIntegrationTestBase
{
    private async Task<string> CreateTopicAsync(bool logAppendTime = false)
    {
        var topic = "settlement-" + Guid.NewGuid().ToString("N");
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = Options.BootstrapServers }).Build();
        await admin.CreateTopicsAsync(new[] { new TopicSpecification
        {
            Name = topic, NumPartitions = 1, ReplicationFactor = 1,
            Configs = new Dictionary<string, string>
            {
                ["message.timestamp.type"] = logAppendTime ? "LogAppendTime" : "CreateTime"
            }
        } });
        return topic;
    }

    private IHost CreateHost(string topic, string group, Func<object, CancellationToken, Task> handler,
        string suffix = ".dlq", bool missingType = false, int retryCount = 0)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddKafkaEventBus(TestKafkaSerializerContext.Default, o =>
        {
            o.BootstrapServers = Options.BootstrapServers; o.ConsumerGroupId = group;
            o.RetryCount = retryCount; o.RetryDelaySeconds = 0; o.DeadLetterTopicSuffix = suffix;
        });
        builder.Services.AddKafkaSubscription(new KafkaSubscriptionInfo(topic,
            missingType ? typeof(Uri) : typeof(TestKafkaEvent), typeof(KafkaSettlementTests), "Handle",
            (_, value, token) => handler(value, token)));
        return builder.Build();
    }

    private static byte[] Envelope(string payload = "{\"message\":\"healthy\"}", string? type = null, int retry = 0) => JsonSerializer.SerializeToUtf8Bytes(
        new KafkaMessageEnvelope(type ?? typeof(TestKafkaEvent).FullName!, payload)
        { CorrelationId = "stable-correlation", RetryCount = retry }, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);

    private async Task SendAsync(string topic, params byte[][] values)
    {
        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        { BootstrapServers = Options.BootstrapServers, Acks = Acks.All }).Build();
        foreach (var value in values)
        {
            var result = await producer.ProduceAsync(new TopicPartition(topic, 0), new Message<string, byte[]>
            {
                Key = "same-partition-key", Value = value,
                Headers = new Headers { { "correlation", new byte[] { 1, 2 } }, { "correlation", new byte[] { 3 } } }
            });
            Assert.Equal(PersistenceStatus.Persisted, result.Status);
        }
    }

    private IConsumer<string, byte[]> Observer(string group) => new ConsumerBuilder<string, byte[]>(new ConsumerConfig
    {
        BootstrapServers = Options.BootstrapServers, GroupId = group,
        EnableAutoCommit = false, EnableAutoOffsetStore = false, AutoOffsetReset = AutoOffsetReset.Earliest
    }).Build();

    private async Task WaitCommittedAsync(string topic, string group, long expected)
    {
        using var observer = Observer(group);
        var partition = new TopicPartition(topic, 0);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            try
            {
                var actual = observer.Committed(new[] { partition }, TimeSpan.FromSeconds(3)).Single().Offset.Value;
                if (actual == expected) return;
                Assert.True(actual < expected, $"Unexpected offset {actual}, expected {expected}");
            }
            catch (KafkaException ex) when (ex.Error.Code is ErrorCode.NotCoordinatorForGroup or ErrorCode.GroupLoadInProgress or ErrorCode.GroupCoordinatorNotAvailable)
            {
                // The broker can still be electing the group coordinator on a fresh cluster.
            }
            await Task.Delay(25, deadline.Token);
        }
    }

    [Theory]
    [InlineData("invalid-envelope", "invalid-envelope", false)]
    [InlineData("null-envelope", "null-envelope", false)]
    [InlineData("mismatch", "event-type-mismatch", false)]
    [InlineData("bad-payload", "invalid-payload", false)]
    [InlineData("null-payload", "null-payload", false)]
    [InlineData("missing-type", "missing-type-info", false)]
    [InlineData("invalid-envelope", "invalid-envelope", true)]
    [InlineData("null-envelope", "null-envelope", true)]
    [InlineData("mismatch", "event-type-mismatch", true)]
    [InlineData("bad-payload", "invalid-payload", true)]
    [InlineData("null-payload", "null-payload", true)]
    [InlineData("missing-type", "missing-type-info", true)]
    public async Task PoisonThenFollowingRecord_SamePartition_SettlesWithoutLoss(string kind, string reason, bool logAppendTime)
    {
        var topic = await CreateTopicAsync(logAppendTime);
        var group = "group-" + Guid.NewGuid().ToString("N");
        var poison = kind switch
        {
            "invalid-envelope" => Encoding.UTF8.GetBytes("{"), "null-envelope" => Encoding.UTF8.GetBytes("null"),
            "mismatch" => Envelope(type: "foreign"), "bad-payload" => Envelope("{"),
            "null-payload" => Envelope("null"), "missing-type" => Envelope(type: typeof(Uri).FullName),
            _ => throw new ArgumentException(kind)
        };
        // With no codec, both records are quarantined; the other cases exercise a healthy second record.
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var host = CreateHost(topic, group, (value, _) =>
        {
            Assert.Equal("healthy", Assert.IsType<TestKafkaEvent>(value).Message);
            Interlocked.Increment(ref calls); handled.TrySetResult(); return Task.CompletedTask;
        }, missingType: kind == "missing-type");
        await SendAsync(topic, poison, Envelope());
        using var sourceObserver = Observer("timestamp-" + Guid.NewGuid().ToString("N"));
        sourceObserver.Assign(new TopicPartitionOffset(topic, 0, Offset.Beginning));
        var source = sourceObserver.Consume(TimeSpan.FromSeconds(15));
        Assert.NotNull(source);
        Assert.Equal(logAppendTime ? TimestampType.LogAppendTime : TimestampType.CreateTime, source.Message.Timestamp.Type);
        await host.StartAsync();
        if (kind != "missing-type") await handled.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await WaitCommittedAsync(topic, group, 2);
        using var observer = Observer("dlq-" + Guid.NewGuid().ToString("N"));
        observer.Subscribe(topic + ".dlq");
        var dlq = observer.Consume(TimeSpan.FromSeconds(15));
        Assert.NotNull(dlq);
        Assert.Equal(poison, dlq.Message.Value);
        Assert.Equal(TimestampType.CreateTime, dlq.Message.Timestamp.Type);
        Assert.Equal(source.Message.Timestamp.UnixTimestampMs, dlq.Message.Timestamp.UnixTimestampMs);
        Assert.Equal("same-partition-key", dlq.Message.Key);
        Assert.Equal(new byte[] { 3 }, dlq.Message.Headers.GetLastBytes("correlation"));
        Assert.Equal(2, dlq.Message.Headers.Count(h => h.Key == "correlation"));
        Assert.Equal(reason, Encoding.UTF8.GetString(dlq.Message.Headers.GetLastBytes("x-settlement-reason")));
        Assert.Equal("0", Encoding.UTF8.GetString(dlq.Message.Headers.GetLastBytes("x-original-offset")));
        Assert.Equal(kind == "missing-type" ? 0 : 1, calls);
        await host.StopAsync();
    }

    [Fact]
    public async Task FailedDlqDelivery_StopsHostWithoutAdvancing_AndRestartRecovers()
    {
        var topic = await CreateTopicAsync();
        var group = "recovery-" + Guid.NewGuid().ToString("N");
        var poison = Encoding.UTF8.GetBytes("null" + new string(' ', 8192));
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = Options.BootstrapServers }).Build())
            await admin.CreateTopicsAsync(new[] { new TopicSpecification
            {
                Name = topic + ".restricted", NumPartitions = 1, ReplicationFactor = 1,
                Configs = new Dictionary<string, string> { ["max.message.bytes"] = "128" }
            } });
        await SendAsync(topic, poison, Envelope());
        var calls = 0;
        using (var failed = CreateHost(topic, group, (_, _) => { calls++; return Task.CompletedTask; }, suffix: ".restricted"))
        {
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = failed.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopped.TrySetResult());
            await failed.StartAsync();
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await failed.StopAsync();
            using var observer = Observer(group);
            var committed = observer.Committed(new[] { new TopicPartition(topic, 0) }, TimeSpan.FromSeconds(5)).Single().Offset;
            Assert.Equal(Offset.Unset, committed);
            Assert.Equal(0, calls);
        }
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var restarted = CreateHost(topic, group, (_, _) => { calls++; handled.TrySetResult(); return Task.CompletedTask; });
        await restarted.StartAsync();
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await WaitCommittedAsync(topic, group, 2);
        using var dlqConsumer = Observer("recovered-dlq-" + Guid.NewGuid().ToString("N"));
        dlqConsumer.Subscribe(topic + ".dlq");
        var dlq = dlqConsumer.Consume(TimeSpan.FromSeconds(15));
        Assert.NotNull(dlq);
        Assert.Equal(poison, dlq.Message.Value);
        Assert.Equal(1, calls);
        await restarted.StopAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerRetry_ThenDlq_ConfirmsBothBeforeCommitting(bool logAppendTime)
    {
        var topic = await CreateTopicAsync(logAppendTime);
        var group = "retry-" + Guid.NewGuid().ToString("N");
        var original = Envelope();
        await SendAsync(topic, original);
        var calls = 0;
        using var host = CreateHost(topic, group, (_, _) =>
        { Interlocked.Increment(ref calls); return Task.FromException(new InvalidOperationException("handler")); }, retryCount: 1);
        await host.StartAsync();
        await WaitCommittedAsync(topic, group, 2);
        using var observer = Observer("retry-dlq-" + Guid.NewGuid().ToString("N"));
        observer.Subscribe(topic + ".dlq");
        var dlq = observer.Consume(TimeSpan.FromSeconds(15));
        Assert.NotNull(dlq);
        var envelope = JsonSerializer.Deserialize(dlq.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope)!;
        Assert.Equal("stable-correlation", envelope.CorrelationId);
        Assert.Equal(typeof(TestKafkaEvent).FullName, envelope.EventType);
        Assert.Equal(1, envelope.RetryCount);
        Assert.Equal("same-partition-key", dlq.Message.Key);
        Assert.Equal(2, calls);
        using var sourceObserver = Observer("retry-timestamp-" + Guid.NewGuid().ToString("N"));
        sourceObserver.Assign(new TopicPartitionOffset(topic, 0, new Offset(1)));
        var retry = sourceObserver.Consume(TimeSpan.FromSeconds(15));
        Assert.NotNull(retry);
        Assert.Equal(logAppendTime ? TimestampType.LogAppendTime : TimestampType.CreateTime, retry.Message.Timestamp.Type);
        Assert.Equal(TimestampType.CreateTime, dlq.Message.Timestamp.Type);
        Assert.Equal(retry.Message.Timestamp.UnixTimestampMs, dlq.Message.Timestamp.UnixTimestampMs);
        await host.StopAsync();
    }
}
