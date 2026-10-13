using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Extensions;
using CrestCreates.EventBus.RabbitMQ.Options;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests;

public class RabbitMqOptionsTests
{
    [Fact]
    public void Defaults_are_stable()
    {
        var options = new RabbitMqOptions();

        options.HostName.Should().Be("localhost");
        options.Port.Should().Be(5672);
        options.VirtualHost.Should().Be("/");
        options.MaxChannels.Should().Be(10);
        options.RetryCount.Should().Be(3);
        options.RetryDelaySeconds.Should().Be(5);
        options.DeadLetterExchange.Should().Be("crestcreates.dlx");
        options.DefaultExchange.Should().Be("crestcreates.events");
        options.PublisherConfirmTimeoutSeconds.Should().Be(30);
        options.DefaultPrefetchCount.Should().Be(10);
        options.ConsumerDispatchConcurrency.Should().Be(1);
        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_rejects_non_positive_max_channels(int maxChannels)
    {
        var options = new RabbitMqOptions { MaxChannels = maxChannels };
        options.Invoking(o => o.Validate()).Should().Throw<ArgumentException>().WithMessage("*MaxChannels*");
    }

    [Fact]
    public void Validate_rejects_negative_retry_count_and_delay()
    {
        new RabbitMqOptions { RetryCount = -1 }
            .Invoking(o => o.Validate()).Should().Throw<ArgumentException>().WithMessage("*RetryCount*");
        new RabbitMqOptions { RetryDelaySeconds = -1 }
            .Invoking(o => o.Validate()).Should().Throw<ArgumentException>().WithMessage("*RetryDelaySeconds*");
    }

    [Fact]
    public void Validate_rejects_non_positive_confirm_timeout()
    {
        new RabbitMqOptions { PublisherConfirmTimeoutSeconds = 0 }
            .Invoking(o => o.Validate()).Should().Throw<ArgumentException>().WithMessage("*PublisherConfirmTimeoutSeconds*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Validate_rejects_prefetch_out_of_ushort_range_without_truncation(int prefetch)
    {
        new RabbitMqOptions { DefaultPrefetchCount = prefetch }
            .Invoking(o => o.Validate()).Should().Throw<ArgumentException>().WithMessage("*DefaultPrefetchCount*");
    }

    [Fact]
    public void Validate_rejects_invalid_endpoint_values()
    {
        new RabbitMqOptions { HostName = " " }
            .Invoking(o => o.Validate()).Should().Throw<ArgumentException>().WithMessage("*HostName*");
        new RabbitMqOptions { Port = 0 }
            .Invoking(o => o.Validate()).Should().Throw<ArgumentException>().WithMessage("*Port*");
        new RabbitMqOptions { VirtualHost = "" }
            .Invoking(o => o.Validate()).Should().Throw<ArgumentException>().WithMessage("*VirtualHost*");
        new RabbitMqOptions { DeadLetterExchange = "" }
            .Invoking(o => o.Validate()).Should().Throw<ArgumentException>().WithMessage("*DeadLetterExchange*");
        new RabbitMqOptions { DefaultExchange = " " }
            .Invoking(o => o.Validate()).Should().Throw<ArgumentException>().WithMessage("*DefaultExchange*");
    }

    [Fact]
    public void AddRabbitMqEventBus_validates_the_configured_snapshot_immediately()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        FluentActions.Invoking(() => services.AddRabbitMqEventBus(options => options.RetryCount = -1))
            .Should().Throw<ArgumentException>().WithMessage("*RetryCount*");
    }

    [Fact]
    public void ConnectionPool_constructor_revalidates_options()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions { MaxChannels = 0 });

        FluentActions.Invoking(() => new RabbitMqConnectionPool(options, NullLogger<RabbitMqConnectionPool>.Instance))
            .Should().Throw<ArgumentException>().WithMessage("*MaxChannels*");
    }

    [Fact]
    public async Task ConnectionPool_cancellation_during_recovery_wait_preserves_cancellation_semantics_and_lease()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions { MaxChannels = 1 });
        using var pool = new RabbitMqConnectionPool(options, NullLogger<RabbitMqConnectionPool>.Instance);

        var connectionOpen = false;
        var connection = new Moq.Mock<global::RabbitMQ.Client.IConnection>();
        connection.SetupGet(c => c.IsOpen).Returns(() => Volatile.Read(ref connectionOpen));
        var channel = new Moq.Mock<global::RabbitMQ.Client.IChannel>();
        channel.SetupGet(c => c.IsOpen).Returns(true);
        connection.Setup(c => c.CreateChannelAsync(
                Moq.It.IsAny<global::RabbitMQ.Client.CreateChannelOptions>(),
                Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel.Object);
        typeof(RabbitMqConnectionPool)
            .GetField("_connection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(pool, connection.Object);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await FluentActions.Awaiting(() => pool.GetChannelAsync(cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>(
                "caller cancellation during the recovery wait must stay a cancellation, not a connection failure");

        // The semaphore permit must have been released exactly once: with MaxChannels=1 a leaked
        // permit would block this acquisition forever.
        Volatile.Write(ref connectionOpen, true);
        var acquired = await pool.GetChannelAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        acquired.Should().BeSameAs(channel.Object);
        pool.ReturnChannel(acquired);
    }

    [Fact]
    public async Task ConnectionPool_get_channel_honors_cancellation_without_connecting()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions { HostName = "localhost", MaxChannels = 1 });
        using var pool = new RabbitMqConnectionPool(options, NullLogger<RabbitMqConnectionPool>.Instance);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await FluentActions.Invoking(() => pool.GetChannelAsync(cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
