using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Extensions;
using CrestCreates.EventBus.RabbitMQ.Options;
using FluentAssertions;
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
