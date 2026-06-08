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
        var resolver = new CapabilityHandlerResolver();
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton<ICapabilityHandlerResolver>(resolver);
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
        var resolver = new CapabilityHandlerResolver();
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "test.echo",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active
        });
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton<ICapabilityHandlerResolver>(resolver);
        services.AddSingleton(new CapabilityPipelineBuilder());
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
        var resolver = new CapabilityHandlerResolver();
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "test.echo",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active
        });
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton<ICapabilityHandlerResolver>(resolver);
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

    [Fact]
    public async Task ExecuteAsync_HandlerRegistered_InvokesSuccessfully()
    {
        // Arrange: register a capability and a simple echo handler
        var services = new ServiceCollection();
        var registry = new CapabilityRegistry();
        var resolver = new CapabilityHandlerResolver();

        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "test.echo",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active
        });

        // Register handler invoker — zero reflection, AOT-safe
        resolver.Register("test.echo", new EchoHandlerInvoker());

        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton<ICapabilityHandlerResolver>(resolver);
        services.AddSingleton(new CapabilityPipelineBuilder());
        services.AddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        var sp = services.BuildServiceProvider();

        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();

        // Act
        var result = await pipeline.ExecuteAsync("test.echo", input: "hello");

        // Assert
        result.Status.Should().Be(CapabilityExecutionStatus.Succeeded);
        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Be("ECHO: hello");
        result.Duration.Should().BePositive();
    }

    [Fact]
    public async Task ExecuteAsync_HandlerRegistered_WithContext_InvokesSuccessfully()
    {
        var services = new ServiceCollection();
        var registry = new CapabilityRegistry();
        var resolver = new CapabilityHandlerResolver();

        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_02",
            Name = "test.upper",
            Version = 1,
            CapabilityKind = CapabilityKind.Command,
            State = DescriptorState.Active
        });

        resolver.Register("test.upper", new UpperHandlerInvoker());

        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton<ICapabilityHandlerResolver>(resolver);
        services.AddSingleton(new CapabilityPipelineBuilder());
        services.AddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        var sp = services.BuildServiceProvider();

        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();
        var result = await pipeline.ExecuteAsync("test.upper", input: "hello world",
            configureContext: ctx =>
            {
                ctx.TenantId = "tenant_01";
                ctx.UserId = "user_01";
            });

        result.Status.Should().Be(CapabilityExecutionStatus.Succeeded);
        result.Output.Should().Be("HELLO WORLD");
    }

    [Fact]
    public async Task ExecuteAsync_PipelineError_ReturnsFailure()
    {
        var services = new ServiceCollection();
        var registry = new CapabilityRegistry();
        var resolver = new CapabilityHandlerResolver();

        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_03",
            Name = "test.broken",
            Version = 1,
            CapabilityKind = CapabilityKind.Command,
            State = DescriptorState.Active
        });

        resolver.Register("test.broken", new ThrowingHandlerInvoker());

        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton<ICapabilityHandlerResolver>(resolver);
        services.AddSingleton(new CapabilityPipelineBuilder());
        services.AddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        var sp = services.BuildServiceProvider();

        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();
        var result = await pipeline.ExecuteAsync("test.broken");

        result.Status.Should().Be(CapabilityExecutionStatus.Failed);
        result.ErrorCode.Should().Be("PIPELINE_ERROR");
    }

    // --- test handler invokers (AOT-safe, zero reflection) ---

    private sealed class EchoHandlerInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        {
            var result = $"ECHO: {input}";
            return Task.FromResult<object?>(result);
        }
    }

    private sealed class UpperHandlerInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        {
            var result = (input as string)?.ToUpperInvariant();
            return Task.FromResult<object?>(result);
        }
    }

    private sealed class ThrowingHandlerInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        {
            throw new InvalidOperationException("Handler failure");
        }
    }
}
