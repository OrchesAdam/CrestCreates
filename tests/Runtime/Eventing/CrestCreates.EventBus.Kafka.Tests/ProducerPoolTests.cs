using Confluent.Kafka;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests;

public class ProducerPoolTests
{
    private static KafkaProducerPool Create() => new(
        Microsoft.Extensions.Options.Options.Create(new KafkaOptions { ProducerPoolSize = 2 }), NullLogger<KafkaProducerPool>.Instance);

    [Fact]
    public async Task BorrowedProducer_ReturnAfterDisposal_DisposesWithoutReleasingDisposedSemaphore()
    {
        var pool = Create();
        var producer = await pool.GetProducerAsync();
        pool.Dispose();
        Assert.Null(Record.Exception(() => pool.ReturnProducer(producer)));
    }

    [Fact]
    public async Task DuplicateReturn_IsRejected()
    {
        using var pool = Create();
        var producer = await pool.GetProducerAsync();
        pool.ReturnProducer(producer);
        Assert.Throws<InvalidOperationException>(() => pool.ReturnProducer(producer));
    }

    [Fact]
    public async Task CreationFailure_RestoresFullCapacity()
    {
        var attempts = 0;
        var producers = new[] { new Moq.Mock<IProducer<string, byte[]>>(), new Moq.Mock<IProducer<string, byte[]>>() };
        using var pool = new KafkaProducerPool(Microsoft.Extensions.Options.Options.Create(new KafkaOptions { ProducerPoolSize = 2 }),
            NullLogger<KafkaProducerPool>.Instance, () =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt == 1) throw new InvalidOperationException("factory");
                return producers[attempt - 2].Object;
            });
        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.GetProducerAsync());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = await pool.GetProducerAsync(deadline.Token);
        var second = await pool.GetProducerAsync(deadline.Token);
        var waiter = pool.GetProducerAsync(deadline.Token);
        Assert.False(waiter.IsCompleted);
        pool.ReturnProducer(first);
        Assert.Same(first, await waiter);
        pool.ReturnProducer(first);
        pool.ReturnProducer(second);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task DisposeDuringCreation_RejectsWaiterAndDisposesCreatedProducerExactlyOnce()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var producer = new Moq.Mock<IProducer<string, byte[]>>();
        var pool = new KafkaProducerPool(Microsoft.Extensions.Options.Options.Create(new KafkaOptions { ProducerPoolSize = 1 }),
            NullLogger<KafkaProducerPool>.Instance, () =>
            {
                entered.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                return producer.Object;
            });
        // A dedicated test thread blocks the synchronous factory at the ownership boundary.
        var borrower = Task.Factory.StartNew(() => pool.GetProducerAsync(), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var waiter = pool.GetProducerAsync();
        Assert.False(waiter.IsCompleted);
        try
        {
            pool.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiter.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { release.Set(); }
        await Assert.ThrowsAsync<ObjectDisposedException>(() => borrower.WaitAsync(TimeSpan.FromSeconds(5)));
        await pool.DisposeAsync();
        producer.Verify(p => p.Dispose(), Moq.Times.Once);
    }

    [Fact]
    public async Task DisposeWithBorrowAndWaiter_ReturnOwnsFinalDisposal()
    {
        var producer = new Moq.Mock<IProducer<string, byte[]>>();
        var pool = new KafkaProducerPool(Microsoft.Extensions.Options.Options.Create(new KafkaOptions { ProducerPoolSize = 1 }),
            NullLogger<KafkaProducerPool>.Instance, () => producer.Object);
        var borrowed = await pool.GetProducerAsync();
        var waiter = pool.GetProducerAsync();
        Assert.False(waiter.IsCompleted);
        pool.Dispose();
        producer.Verify(p => p.Dispose(), Moq.Times.Never);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiter.WaitAsync(TimeSpan.FromSeconds(5)));
        pool.ReturnProducer(borrowed);
        Assert.Throws<InvalidOperationException>(() => pool.ReturnProducer(borrowed));
        pool.Dispose();
        producer.Verify(p => p.Dispose(), Moq.Times.Once);
    }

    [Fact]
    public async Task CanceledWaiter_DoesNotConsumeCapacity()
    {
        var producer = new Moq.Mock<IProducer<string, byte[]>>();
        using var pool = new KafkaProducerPool(Microsoft.Extensions.Options.Options.Create(new KafkaOptions { ProducerPoolSize = 1 }),
            NullLogger<KafkaProducerPool>.Instance, () => producer.Object);
        var borrowed = await pool.GetProducerAsync();
        using var canceled = new CancellationTokenSource();
        var waiter = pool.GetProducerAsync(canceled.Token);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        pool.ReturnProducer(borrowed);
        Assert.Same(borrowed, await pool.GetProducerAsync());
        pool.ReturnProducer(borrowed);
    }

    [Fact]
    public async Task ForeignReturn_IsRejected()
    {
        using var pool = Create();
        using var other = Create();
        var producer = await other.GetProducerAsync();
        try { Assert.Throws<InvalidOperationException>(() => pool.ReturnProducer(producer)); }
        finally { other.ReturnProducer(producer); }
    }
}
