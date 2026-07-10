using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Registry;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityPipelineDescriptorOverloadTests
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
    public async Task ExecuteAsync_DescriptorOverload_DoesNotCallRegistryGetById()
    {
        // Arrange: set up a registry with a descriptor, but verify it isn't used for resolution
        var descriptor = new CapabilityDescriptor
        {
            Id = "cap_direct",
            Name = "test.direct",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active
        };

        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestCapabilityProvider([descriptor])]);
        var resolver = new CapabilityHandlerResolver();
        resolver.Register("cap_direct", new EchoHandlerInvoker());

        var sp = BuildPipelineServiceProvider(registry, resolver);
        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();

        // Act: call descriptor overload directly — this should NOT hit registry resolution path
        var result = await pipeline.ExecuteAsync(descriptor, input: "hello");

        // Assert: handler was invoked via descriptor overload (not via registry lookup)
        result.Status.Should().Be(CapabilityExecutionStatus.Succeeded);
        result.Output.Should().Be("ECHO: hello");

        // Verify registry was not used for resolution by checking that removing the descriptor
        // from registry does not affect the descriptor overload — the overload is self-contained.
        registry.Build([]);
        var result2 = await pipeline.ExecuteAsync(descriptor, input: "world");
        result2.Status.Should().Be(CapabilityExecutionStatus.Succeeded);
        result2.Output.Should().Be("ECHO: world");
    }

    [Fact]
    public async Task ExecuteAsync_DescriptorOverload_PreservesVersionFromDescriptor()
    {
        // Arrange
        var descriptor = new CapabilityDescriptor
        {
            Id = "cap_v3",
            Name = "test.versioned",
            Version = 3, // explicit version
            CapabilityKind = CapabilityKind.Command,
            State = DescriptorState.Active
        };

        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestCapabilityProvider([descriptor])]);
        var resolver = new CapabilityHandlerResolver();
        resolver.Register("cap_v3", new ContextCapturingHandlerInvoker());

        var sp = BuildPipelineServiceProvider(registry, resolver);
        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();

        // Act
        var result = await pipeline.ExecuteAsync(descriptor, input: "test");

        // Assert: version 3 is propagated from descriptor into context and result
        result.Status.Should().Be(CapabilityExecutionStatus.Succeeded);
        result.Output.Should().Be("VERSION:3");
    }

    [Fact]
    public async Task ExecuteAsync_StringOverload_FallsBackToDescriptorOverload()
    {
        // Arrange: verify that string overload resolves from registry then delegates to descriptor overload.
        var descriptor = new CapabilityDescriptor
        {
            Id = "cap_fallback",
            Name = "test.fallback",
            Version = 2,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active
        };

        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestCapabilityProvider([descriptor])]);
        var resolver = new CapabilityHandlerResolver();
        resolver.Register("cap_fallback", new ContextCapturingHandlerInvoker());

        var sp = BuildPipelineServiceProvider(registry, resolver);
        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();

        // Act: call the string overload — it should resolve from registry then delegate to
        // the descriptor overload which preserves Version=2 from the descriptor.
        var result = await pipeline.ExecuteAsync("test.fallback", input: "test");

        // Assert
        result.Status.Should().Be(CapabilityExecutionStatus.Succeeded);
        result.Output.Should().Be("VERSION:2");
    }

    // ================================================================
    // ValidationMiddleware uses GetByVersion not GetByName
    // ================================================================

    [Fact]
    public async Task ValidationMiddleware_UsesGetByVersion_NotGetByName()
    {
        // Arrange
        var validator = new Mock<ISchemaValidator>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capabilityRegistry = new Mock<ICapabilityRegistry>();

        var inputSchemaRef = new VersionedDescriptorRef<SchemaDescriptor>("schema_test", 1);
        var v1Descriptor = new CapabilityDescriptor
        {
            Id = "cap_test",
            Name = "test",
            Version = 1,
            InputSchema = inputSchemaRef,
            State = DescriptorState.Active
        };
        var v2Descriptor = new CapabilityDescriptor
        {
            Id = "cap_test",
            Name = "test",
            Version = 2,
            State = DescriptorState.Active
        };

        var schemaDescriptor = new SchemaDescriptor
        {
            Id = "schema_test",
            Name = "TestSchema",
            Version = 1
        };

        // Set up GetByVersion for version 1 to return v1Descriptor
        capabilityRegistry.Setup(r => r.GetByVersion("cap_test", 1)).Returns(v1Descriptor);
        // Set up GetByName to return v2Descriptor (which should NOT be used)
        capabilityRegistry.Setup(r => r.GetByName("cap_test")).Returns(v2Descriptor);

        schemaRegistry.Setup(s => s.GetById("schema_test")).Returns(schemaDescriptor);
        validator.Setup(v => v.Validate(schemaDescriptor, "payload"))
            .Returns(SchemaValidationResult.Success());

        var middleware = new ValidationMiddleware(
            validator.Object,
            capabilityRegistry.Object,
            schemaRegistry.Object);

        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "cap_test",
            CapabilityName = "test",
            CapabilityVersion = 1,
            Input = "payload"
        };

        // Act
        CapabilityPipelineDelegate next = ctx =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero));
        var result = await middleware.InvokeAsync(context, next);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify that GetByVersion was called with version 1
        capabilityRegistry.Verify(r => r.GetByVersion("cap_test", 1), Times.Once);

        // Verify that GetByName was NOT called
        capabilityRegistry.Verify(r => r.GetByName(It.IsAny<string>()), Times.Never);
    }

    // --- test handler invokers ---

    private sealed class EchoHandlerInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        {
            var result = $"ECHO: {input}";
            return Task.FromResult<object?>(result);
        }
    }

    private sealed class ContextCapturingHandlerInvoker : ICapabilityContextAwareHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        {
            // This path is not exercised in the descriptor overload tests;
            // the pipeline routes to InvokeAsync(CapabilityExecutionContext, ...) for context-aware invokers.
            throw new NotSupportedException();
        }

        public Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            var result = $"VERSION:{context.CapabilityVersion}";
            return Task.FromResult<object?>(result);
        }
    }
}
