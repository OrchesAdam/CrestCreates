using Confluent.Kafka;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Exceptions;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests;

public class PublisherTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellation_PropagatesAndReturnsBorrowedProducer(bool dlq)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new KafkaOptions { ProducerPoolSize = 1 });
        var producer = new Mock<IProducer<string, byte[]>>();
        using var canceled = new CancellationTokenSource();
        producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Message<string, byte[]> _, CancellationToken token) =>
            {
                canceled.Cancel();
                return Task.FromCanceled<DeliveryResult<string, byte[]>>(token);
            });
        using var pool = new KafkaProducerPool(options, NullLogger<KafkaProducerPool>.Instance, () => producer.Object);
        var publisher = new KafkaPublisher(pool, ConsumerJsonContext.Default, options, NullLogger<KafkaPublisher>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dlq
            ? publisher.PublishToDeadLetterTopicAsync("events", new byte[] { 1 }, "key", 0, canceled.Token)
            : publisher.PublishAsync("events", new ConsumerEvent("value"), cancellationToken: canceled.Token));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Same(producer.Object, await pool.GetProducerAsync(deadline.Token));
        pool.ReturnProducer(producer.Object);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedDelivery_IsNotReportedAsSuccess(bool dlq)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new KafkaOptions());
        var producer = new Mock<IProducer<string, byte[]>>();
        producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult<string, byte[]> { Status = PersistenceStatus.PossiblyPersisted });
        using var pool = new KafkaProducerPool(options, NullLogger<KafkaProducerPool>.Instance, () => producer.Object);
        var publisher = new KafkaPublisher(pool, ConsumerJsonContext.Default, options, NullLogger<KafkaPublisher>.Instance);
        await Assert.ThrowsAsync<KafkaPublishException>(() => dlq
            ? publisher.PublishToDeadLetterTopicAsync("events", new byte[] { 1 }, "key", 0)
            : publisher.PublishAsync("events", new ConsumerEvent("value")));
    }
}
