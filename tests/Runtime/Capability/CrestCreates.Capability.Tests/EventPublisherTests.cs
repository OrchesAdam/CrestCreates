using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class EventPublisherTests
{
    [Fact]
    public async Task PublishAsync_WithNullEventBus_DoesNotThrow()
    {
        var publisher = new EventPublisher(null);
        await publisher.Invoking(p => p.PublishAsync("test.event", new { }))
            .Should().NotThrowAsync();
    }

    [Fact]
    public void EventPublisher_Implements_IEventPublisher()
    {
        var publisher = new EventPublisher(null);
        publisher.Should().BeAssignableTo<IEventPublisher>();
    }

    [Fact]
    public async Task EventPublishingMiddleware_Passthrough_WhenNullPublisher()
    {
        var middleware = new Middleware.EventPublishingMiddleware(null);
        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityName = "test", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EventPublishingMiddleware_PreservesFailureResult()
    {
        var middleware = new Middleware.EventPublishingMiddleware(null);
        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityName = "test", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Failure("ERR", "bad", TimeSpan.Zero)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ERR");
    }
}
