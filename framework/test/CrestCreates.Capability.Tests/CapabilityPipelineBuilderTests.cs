using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityPipelineBuilderTests
{
    private sealed class TestMiddlewareA : ICapabilityPipelineMiddleware
    {
        public Task<CapabilityExecutionResult> InvokeAsync(
            CapabilityExecutionContext context,
            CapabilityPipelineDelegate next)
            => next(context);
    }

    private sealed class TestMiddlewareB : ICapabilityPipelineMiddleware
    {
        public Task<CapabilityExecutionResult> InvokeAsync(
            CapabilityExecutionContext context,
            CapabilityPipelineDelegate next)
            => next(context);
    }

    [Fact]
    public void Use_Adds_Middleware_In_Order()
    {
        var builder = new CapabilityPipelineBuilder();
        builder.Use<TestMiddlewareA>();
        builder.Use<TestMiddlewareB>();

        builder.MiddlewareTypes.Should().HaveCount(2);
        builder.MiddlewareTypes[0].Should().Be(typeof(TestMiddlewareA));
        builder.MiddlewareTypes[1].Should().Be(typeof(TestMiddlewareB));
    }

    [Fact]
    public void Clear_Removes_All_Middleware()
    {
        var builder = new CapabilityPipelineBuilder();
        builder.Use<TestMiddlewareA>();
        builder.Clear();

        builder.MiddlewareTypes.Should().BeEmpty();
    }

    [Fact]
    public void Builder_Starts_Empty()
    {
        var builder = new CapabilityPipelineBuilder();
        builder.MiddlewareTypes.Should().BeEmpty();
    }
}
