using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Consuming;
using CrestCreates.EventBus.Kafka.Extensions;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests;

public class ConsumerTests
{
    private static ConsumeResult<string, byte[]> Record(string body) => new()
    {
        Topic = "events", Partition = 0, Offset = 7,
        Message = new() { Key = "original-key", Value = Encoding.UTF8.GetBytes(body),
            Headers = new Headers { { "correlation", new byte[] { 1, 2 } }, { "correlation", new byte[] { 3 } } } }
    };

    private static string Envelope(string payload = "{}", string? type = null, int retry = 0) => JsonSerializer.Serialize(
        new KafkaMessageEnvelope(type ?? typeof(ConsumerEvent).FullName!, payload) { RetryCount = retry, CorrelationId = "stable-correlation" },
        KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);

    private sealed class Harness : IDisposable
    {
        public readonly Mock<IProducer<string, byte[]>> Producer = new();
        public readonly Mock<IConsumer<string, byte[]>> Native = new();
        public readonly Mock<IHostApplicationLifetime> Lifetime = new();
        public readonly KafkaOptions Options = new() { RetryDelaySeconds = 0, RetryCount = 1 };
        public readonly ServiceProvider Services;
        public readonly KafkaProducerPool Pool;
        public readonly KafkaConsumer Consumer;
        public int Handlers;
        public string? ForwardTopic;
        public Message<string, byte[]>? Forwarded;
        public TaskCompletionSource<DeliveryResult<string, byte[]>>? PendingDelivery;
        public Exception? HandlerFailure;
        public CancellationTokenSource? CancelInHandler;
        public Func<CancellationToken, Task>? Handler;

        public Harness(bool missingType = false, bool duplicate = false, bool autoCommit = false)
        {
            Options.EnableAutoCommit = autoCommit;
            Producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
                .Returns((string topic, Message<string, byte[]> message, CancellationToken token) =>
                {
                    ForwardTopic = topic; Forwarded = message;
                    return PendingDelivery?.Task ?? Task.FromResult(new DeliveryResult<string, byte[]> { Status = PersistenceStatus.Persisted });
                });
            var services = new ServiceCollection();
            var subscription = new KafkaSubscriptionInfo("events", missingType ? typeof(Uri) : typeof(ConsumerEvent), typeof(ConsumerTests), "Handle",
                (_, _, token) => { Handlers++; CancelInHandler?.Cancel(); return Handler?.Invoke(token) ?? (HandlerFailure == null ? Task.CompletedTask : Task.FromException(HandlerFailure)); });
            services.AddKafkaSubscription(subscription);
            if (duplicate) services.AddKafkaSubscription(subscription);
            services.AddSingleton(Lifetime.Object);
            Services = services.BuildServiceProvider();
            Pool = new KafkaProducerPool(Microsoft.Extensions.Options.Options.Create(Options), NullLogger<KafkaProducerPool>.Instance, () => Producer.Object);
            Consumer = new KafkaConsumer(Pool, ConsumerJsonContext.Default, Microsoft.Extensions.Options.Options.Create(Options),
                Services, NullLogger<KafkaConsumer>.Instance, config =>
                {
                    Assert.False(config.EnableAutoCommit);
                    Assert.False(config.EnableAutoOffsetStore);
                    return Native.Object;
                });
        }
        public void Dispose() { Consumer.Dispose(); Pool.Dispose(); Services.Dispose(); }
    }

    [Theory]
    [InlineData("null", "null-envelope")]
    [InlineData("{", "invalid-envelope")]
    [InlineData("payload", "invalid-payload")]
    [InlineData("null-payload", "null-payload")]
    [InlineData("mismatch", "event-type-mismatch")]
    [InlineData("missing", "missing-type-info")]
    [InlineData("unknown", "unknown-subscription")]
    [InlineData("negative-retry", "invalid-retry-count")]
    public async Task Poison_IsRawQuarantinedBeforeCommit(string kind, string reason)
    {
        using var h = new Harness(missingType: kind == "missing");
        var record = Record(kind switch
        {
            "payload" => Envelope("{"), "null-payload" => Envelope("null"),
            "mismatch" => Envelope(type: "foreign"), "missing" => Envelope(type: typeof(Uri).FullName),
            "unknown" => Envelope(), "negative-retry" => Envelope(retry: -1), _ => kind
        });
        if (kind == "unknown") record.Topic = "unknown";
        h.PendingDelivery = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var processing = h.Consumer.ProcessMessageAsync(h.Native.Object, record, default);
        Assert.False(processing.IsCompleted);
        h.Native.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, byte[]>>()), Times.Never);
        Assert.Equal(record.Topic + ".dlq", h.ForwardTopic);
        Assert.Equal(record.Message.Value, h.Forwarded!.Value);
        Assert.Equal(record.Message.Key, h.Forwarded.Key);
        Assert.Equal(2, record.Message.Headers.Count);
        Assert.Equal(2, h.Forwarded.Headers.Count(x => x.Key == "correlation"));
        Assert.Equal(reason, Encoding.UTF8.GetString(h.Forwarded.Headers.GetLastBytes("x-settlement-reason")));
        h.PendingDelivery.SetResult(new DeliveryResult<string, byte[]> { Status = PersistenceStatus.Persisted });
        await processing;
        h.Native.Verify(c => c.Commit(record), Times.Once);
        Assert.Equal(0, h.Handlers);
    }

    [Fact]
    public async Task HealthyRecord_CommitsWithoutForwarding()
    {
        using var h = new Harness();
        var record = Record(Envelope());
        await h.Consumer.ProcessMessageAsync(h.Native.Object, record, default);
        Assert.Equal(1, h.Handlers);
        Assert.Null(h.Forwarded);
        h.Native.Verify(c => c.Commit(record), Times.Once);
    }

    [Theory]
    [InlineData(0, "events", 1)]
    [InlineData(1, "events.dlq", 1)]
    public async Task HandlerFailure_UsesBudgetAndPreservesWireIdentity(int retry, string topic, int expectedRetry)
    {
        using var h = new Harness { HandlerFailure = new InvalidOperationException("handler") };
        var record = Record(Envelope(retry: retry));
        await h.Consumer.ProcessMessageAsync(h.Native.Object, record, default);
        Assert.Equal(topic, h.ForwardTopic);
        var envelope = JsonSerializer.Deserialize(h.Forwarded!.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope)!;
        Assert.Equal(expectedRetry, envelope.RetryCount);
        Assert.Equal(typeof(ConsumerEvent).FullName, envelope.EventType);
        Assert.Equal("stable-correlation", envelope.CorrelationId);
        Assert.Equal(record.Message.Key, h.Forwarded.Key);
        if (retry == 1) Assert.Equal(record.Message.Value, h.Forwarded.Value);
        h.Native.Verify(c => c.Commit(record), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrUnconfirmedForward_DoesNotCommit(bool unconfirmed)
    {
        using var h = new Harness();
        if (unconfirmed)
            h.Producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeliveryResult<string, byte[]> { Status = PersistenceStatus.PossiblyPersisted });
        else
            h.Producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("delivery"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Consumer.ProcessMessageAsync(h.Native.Object, Record("null"), default));
        h.Native.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, byte[]>>()), Times.Never);
    }

    [Fact]
    public async Task CommitFailure_DoesNotRepublishOrConsumeNextRecord_StopsHostAndClosesOnce()
    {
        using var h = new Harness();
        h.Native.Setup(c => c.Consume(It.IsAny<CancellationToken>())).Returns(Record(Envelope()));
        h.Native.Setup(c => c.Commit(It.IsAny<ConsumeResult<string, byte[]>>())).Throws(new InvalidOperationException("commit"));
        await h.Consumer.StartAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)));
        await h.Consumer.StopAsync(default);
        h.Native.Verify(c => c.Consume(It.IsAny<CancellationToken>()), Times.Once);
        h.Native.Verify(c => c.Close(), Times.Once);
        h.Native.Verify(c => c.Dispose(), Times.Once);
        h.Lifetime.Verify(l => l.StopApplication(), Times.Once);
        Assert.Null(h.Forwarded);
    }

    [Fact]
    public async Task CallerCancellation_InHandler_DoesNotRepublishOrCommit()
    {
        using var canceled = new CancellationTokenSource();
        using var h = new Harness { CancelInHandler = canceled, HandlerFailure = new OperationCanceledException(canceled.Token) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Consumer.ProcessMessageAsync(h.Native.Object, Record(Envelope()), canceled.Token));
        Assert.Null(h.Forwarded);
        h.Native.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, byte[]>>()), Times.Never);
    }

    [Fact]
    public async Task StopWhileHandlerBlocked_CancelsButDoesNotCloseUntilOwnerFinishes()
    {
        using var h = new Harness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Handler = async token =>
        {
            using var registration = token.Register(() => canceled.TrySetResult());
            entered.TrySetResult();
            await release.Task;
            token.ThrowIfCancellationRequested();
        };
        h.Native.Setup(c => c.Consume(It.IsAny<CancellationToken>())).Returns(Record(Envelope()));
        await h.Consumer.StartAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping = h.Consumer.StopAsync(default);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            h.Native.Verify(c => c.Close(), Times.Never);
            h.Native.Verify(c => c.Dispose(), Times.Never);
            Assert.False(stopping.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        h.Native.Verify(c => c.Close(), Times.Once);
        h.Native.Verify(c => c.Dispose(), Times.Once);
        h.Native.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, byte[]>>()), Times.Never);
        Assert.Null(h.Forwarded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForwardFailure_InOwnerLoop_NeverConsumesLaterRecord(bool handlerRetry)
    {
        using var h = new Harness();
        if (handlerRetry) h.HandlerFailure = new InvalidOperationException("handler");
        h.Native.Setup(c => c.Consume(It.IsAny<CancellationToken>())).Returns(Record(handlerRetry ? Envelope() : "null"));
        h.Producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("forward"));
        await h.Consumer.StartAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)));
        h.Native.Verify(c => c.Consume(It.IsAny<CancellationToken>()), Times.Once);
        h.Native.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, byte[]>>()), Times.Never);
        h.Lifetime.Verify(l => l.StopApplication(), Times.Once);
    }

    [Fact]
    public void AmbiguousSubscriptions_AndAutomaticCommit_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new Harness(duplicate: true));
        Assert.Throws<ArgumentException>(() => new Harness(autoCommit: true));
    }
}

public sealed record ConsumerEvent(string? Value);
[JsonSerializable(typeof(ConsumerEvent))]
public partial class ConsumerJsonContext : JsonSerializerContext { }