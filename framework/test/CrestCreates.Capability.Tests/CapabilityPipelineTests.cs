using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_CapabilityNotFound_ReturnsFailure()
    {
        var services = new ServiceCollection();
        var registry = new CapabilityRegistry();
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton(new CapabilityPipelineBuilder());
        services.AddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        var sp = services.BuildServiceProvider();

        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();
        var result = await pipeline.ExecuteAsync("nonexistent.cap");

        result.Status.Should().Be(CapabilityExecutionStatus.Failed);
        result.ErrorCode.Should().Be("CAPABILITY_NOT_FOUND");
    }

    [Fact]
    public async Task ExecuteAsync_CapabilityFound_ButHandlerNotFound_ReturnsFailure()
    {
        var services = new ServiceCollection();
        var registry = new CapabilityRegistry();
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "test.echo",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active
        });
        services.AddSingleton<ICapabilityRegistry>(registry);

        var builder = new CapabilityPipelineBuilder();
        services.AddSingleton(builder);
        services.AddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        var sp = services.BuildServiceProvider();

        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();
        var result = await pipeline.ExecuteAsync("test.echo");

        result.Status.Should().Be(CapabilityExecutionStatus.Failed);
        result.ErrorCode.Should().Be("HANDLER_NOT_FOUND");
    }

    [Fact]
    public async Task ExecuteAsync_ConfigureContext_Overrides_Context_Values()
    {
        var services = new ServiceCollection();
        var registry = new CapabilityRegistry();
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "test.echo",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active
        });
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton(new CapabilityPipelineBuilder());
        services.AddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        var sp = services.BuildServiceProvider();

        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();
        var result = await pipeline.ExecuteAsync("test.echo", configureContext: ctx =>
        {
            ctx.TenantId = "tenant_01";
            ctx.UserId = "user_01";
        });

        result.ErrorCode.Should().Be("HANDLER_NOT_FOUND");
    }
}
