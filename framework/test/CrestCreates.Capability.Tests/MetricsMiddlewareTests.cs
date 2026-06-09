using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class MetricsMiddlewareTests
{
    [Fact]
    public async Task Passthrough_WhenNoMetrics()
    {
        var middleware = new MetricsMiddleware(null);
        var context = new CapabilityExecutionContext
        {
            CapabilityName = "test", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RecordsSuccess()
    {
        var metrics = new InMemoryPipelineMetrics();
        var middleware = new MetricsMiddleware(metrics);
        var context = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.FromMilliseconds(10))));

        var snapshot = metrics.GetSnapshot();
        snapshot.TotalExecutions.Should().Be(1);
        snapshot.SuccessfulExecutions.Should().Be(1);
    }

    [Fact]
    public async Task RecordsFailure()
    {
        var metrics = new InMemoryPipelineMetrics();
        var middleware = new MetricsMiddleware(metrics);
        var context = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Failure("ERR", "bad", TimeSpan.Zero)));

        var snapshot = metrics.GetSnapshot();
        snapshot.FailedExecutions.Should().Be(1);
    }
}