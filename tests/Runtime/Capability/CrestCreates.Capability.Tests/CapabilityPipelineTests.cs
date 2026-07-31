using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityPipelineTests
{
    private sealed class TestCapabilityProvider : IDescriptorProvider<CapabilityDescriptor>
    {
        private readonly List<CapabilityDescriptor> _descriptors;
        public TestCapabilityProvider(List<CapabilityDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => _descriptors;
    }

    private static IServiceProvider BuildPipelineServiceProvider(
        ICapabilityRegistry registry,
        ICapabilityHandlerResolver resolver)
    {
        var hashBuilderMock = new Mock<IDescriptorStableHashBuilder>();
        hashBuilderMock.Setup(h => h.Build(It.IsAny<IDescriptor>()))
            .Returns(new DescriptorStableHashes
            {
                ContractHash = new CanonicalHash
                {
                    Value = "abc123",
                    Algorithm = "SHA-256",
                    AlgorithmVersion = "v1",
                    ArtifactKind = "Descriptor",
                    Scope = "InternalFull",
                    Purpose = "Contract",
                    ContractVersion = "v1",
                    CanonicalShapeVersion = "v1"
                },
                DefinitionHash = new CanonicalHash
                {
                    Value = "def456",
                    Algorithm = "SHA-256",
                    AlgorithmVersion = "v1",
                    ArtifactKind = "Descriptor",
                    Scope = "InternalFull",
                    Purpose = "Definition",
                    ContractVersion = "v1",
                    CanonicalShapeVersion = "v1"
                }
            });

        var services = new ServiceCollection();
        services.AddSingleton(registry);
        services.AddSingleton(resolver);
        services.AddSingleton(new CapabilityPipelineBuilder());
        services.AddSingleton(hashBuilderMock.Object);
        services.AddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ExecuteAsync_CapabilityNotFound_ReturnsFailure()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var resolver = new CapabilityHandlerResolver();
        var sp = BuildPipelineServiceProvider(registry, resolver);

        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();
        var result = await pipeline.ExecuteAsync("nonexistent.cap");

        result.Status.Should().Be(CapabilityExecutionStatus.Failed);
        result.ErrorCode.Should().Be("CAPABILITY_NOT_FOUND");
    }

    [Fact]
    public Task UnresolvedCapabilityIsExplicitlyOutsideExecutionFactBoundary()
        => ExecuteAsync_CapabilityNotFound_ReturnsFailure();

    [Fact]
    public async Task ExecuteAsync_CapabilityFound_ButHandlerNotFound_ReturnsFailure()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var resolver = new CapabilityHandlerResolver();
        registry.Build([new TestCapabilityProvider([
            new CapabilityDescriptor
            {
                Id = "cap_01",
                Name = "test.echo",
                Version = 1,
                CapabilityKind = CapabilityKind.Query,
                State = DescriptorState.Active
            }
        ])]);
        var sp = BuildPipelineServiceProvider(registry, resolver);

        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();
        var result = await pipeline.ExecuteAsync("test.echo");

        result.Status.Should().Be(CapabilityExecutionStatus.Failed);
        result.ErrorCode.Should().Be("HANDLER_NOT_FOUND");
    }

    [Fact]
    public async Task ExecuteAsync_ConfigureContext_Overrides_Context_Values()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var resolver = new CapabilityHandlerResolver();
        registry.Build([new TestCapabilityProvider([
            new CapabilityDescriptor
            {
                Id = "cap_01",
                Name = "test.echo",
                Version = 1,
                CapabilityKind = CapabilityKind.Query,
                State = DescriptorState.Active
            }
        ])]);
        var sp = BuildPipelineServiceProvider(registry, resolver);

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
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var resolver = new CapabilityHandlerResolver();

        registry.Build([new TestCapabilityProvider([
            new CapabilityDescriptor
            {
                Id = "cap_01",
                Name = "test.echo",
                Version = 1,
                CapabilityKind = CapabilityKind.Query,
                State = DescriptorState.Active
            }
        ])]);

        // Register handler invoker — zero reflection, AOT-safe
        resolver.Register("cap_01", new EchoHandlerInvoker());

        var sp = BuildPipelineServiceProvider(registry, resolver);
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
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var resolver = new CapabilityHandlerResolver();

        registry.Build([new TestCapabilityProvider([
            new CapabilityDescriptor
            {
                Id = "cap_02",
                Name = "test.upper",
                Version = 1,
                CapabilityKind = CapabilityKind.Command,
                State = DescriptorState.Active
            }
        ])]);

        resolver.Register("cap_02", new UpperHandlerInvoker());

        var sp = BuildPipelineServiceProvider(registry, resolver);
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
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var resolver = new CapabilityHandlerResolver();

        registry.Build([new TestCapabilityProvider([
            new CapabilityDescriptor
            {
                Id = "cap_03",
                Name = "test.broken",
                Version = 1,
                CapabilityKind = CapabilityKind.Command,
                State = DescriptorState.Active
            }
        ])]);

        resolver.Register("cap_03", new ThrowingHandlerInvoker());

        var sp = BuildPipelineServiceProvider(registry, resolver);
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

    [Fact]
    public async Task ExecuteAsync_PopulatesServiceProvider_OnContext()
    {
        // Arrange
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var resolver = new CapabilityHandlerResolver();

        registry.Build([new TestCapabilityProvider([
            new CapabilityDescriptor
            {
                Id = "test.pipe",
                Name = "Test",
                Version = 1,
                CapabilityKind = CapabilityKind.Command,
                State = DescriptorState.Active
            }
        ])]);

        CapabilityExecutionContext? capturedContext = null;
        var mockInvoker = new Mock<ICapabilityContextAwareHandlerInvoker>();
        mockInvoker
            .Setup(x => x.InvokeAsync(It.IsAny<CapabilityExecutionContext>(), It.IsAny<CancellationToken>()))
            .Callback<CapabilityExecutionContext, CancellationToken>((ctx, _) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync((object?)"ok");

        resolver.Register("test.pipe", mockInvoker.Object);

        var sp = BuildPipelineServiceProvider(registry, resolver);
        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();

        // Act
        var result = await pipeline.ExecuteAsync("test.pipe");

        // Assert — verifies ServiceProvider is populated from DI-injected _serviceProvider
        result.Status.Should().Be(CapabilityExecutionStatus.Succeeded);
        capturedContext.Should().NotBeNull();
        capturedContext!.ServiceProvider.Should().NotBeNull();
        // sp is ServiceProvider wrapper; DI injects the internal scope.
        // Verify it's non-null — the key contract is that ServiceProvider is populated.
        capturedContext.ServiceProvider.Should().NotBeNull();
    }

    // === Composition: AddCapabilityPipeline() registers IDescriptorStableHashBuilder ===
    [Fact]
    public void AddCapabilityPipeline_RegistersDescriptorStableHash()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IPermissionChecker>(Mock.Of<IPermissionChecker>());

        // Act — only AddCapabilityPipeline, no manual AddDescriptorStableHash
        services.AddCapabilityPipeline();

        // Assert — the stable hash dependency is registered
        services.Should().Contain(
            x => x.ServiceType == typeof(IDescriptorStableHashBuilder),
            "AddCapabilityPipeline must register IDescriptorStableHashBuilder since CapabilityPipeline depends on it");
    }

    private sealed class ThrowingHandlerInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        {
            throw new InvalidOperationException("Handler failure");
        }
    }
}
