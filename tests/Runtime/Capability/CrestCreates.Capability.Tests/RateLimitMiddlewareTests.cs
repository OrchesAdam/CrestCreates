using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class RateLimitMiddlewareTests
{
    private static CapabilityExecutionContext CreateContext(string name = "test.cap")
    {
        return new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityName = name, CapabilityVersion = 1, CapabilityContractHash = "abc"
        };
    }

    [Fact]
    public async Task Passthrough_WhenNoStore()
    {
        var middleware = new RateLimitMiddleware(null);
        var result = await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AllowsRequests_WithinLimit()
    {
        var store = new InMemoryRateLimitStore();
        var middleware = new RateLimitMiddleware(store, defaultMaxRequests: 10);

        for (int i = 0; i < 10; i++)
        {
            var result = await middleware.InvokeAsync(CreateContext(), _ =>
                Task.FromResult(CapabilityExecutionResult.Success(i, TimeSpan.Zero)));
            result.IsSuccess.Should().BeTrue();
        }
    }

    [Fact]
    public async Task RejectsWhenOverLimit()
    {
        var store = new InMemoryRateLimitStore();
        var middleware = new RateLimitMiddleware(store, defaultMaxRequests: 3);

        for (int i = 0; i < 3; i++)
            await middleware.InvokeAsync(CreateContext(), _ =>
                Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        var result = await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("blocked", TimeSpan.Zero)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task DifferentCapabilities_HaveSeparateLimits()
    {
        var store = new InMemoryRateLimitStore();
        var middleware = new RateLimitMiddleware(store, defaultMaxRequests: 1);

        await middleware.InvokeAsync(CreateContext("cap.a"), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("a1", TimeSpan.Zero)));

        var r2 = await middleware.InvokeAsync(CreateContext("cap.b"), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("b1", TimeSpan.Zero)));

        r2.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SlidingWindow_ExpiresOldEntries()
    {
        var store = new InMemoryRateLimitStore();
        var middleware = new RateLimitMiddleware(store, defaultMaxRequests: 2,
            defaultWindow: TimeSpan.FromMilliseconds(50));

        await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("1", TimeSpan.Zero)));
        await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("2", TimeSpan.Zero)));

        var blocked = await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("3", TimeSpan.Zero)));
        blocked.ErrorCode.Should().Be("RATE_LIMIT_EXCEEDED");

        await Task.Delay(100);

        var allowed = await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("4", TimeSpan.Zero)));
        allowed.IsSuccess.Should().BeTrue();
    }
}