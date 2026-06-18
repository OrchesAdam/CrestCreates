using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityContextAwareHandlerInvokerTests
{
    [Fact]
    public async Task Pipeline_Should_Invoke_ContextAware_Handler_With_ExecutionContext()
    {
        var registry = new CapabilityRegistry(
            new RegistryValidationEngine<CapabilityDescriptor>(
                Array.Empty<IRegistryValidator<CapabilityDescriptor>>()));
        registry.Build([new TestProvider([new CapabilityDescriptor
        {
            Id = "cert.approve",
            Name = "Approve",
            Version = 1,
            CapabilityKind = CapabilityKind.Command
        }])]);

        var invoker = new CapturingContextAwareInvoker();
        var resolver = new CapabilityHandlerResolver();
        resolver.Register("cert.approve", invoker);

        var services = new ServiceCollection();
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton<ICapabilityHandlerResolver>(resolver);
        services.AddSingleton(new CapabilityPipelineBuilder());
        var provider = services.BuildServiceProvider();

        var pipeline = new CapabilityPipeline(
            provider,
            registry,
            resolver,
            provider.GetRequiredService<CapabilityPipelineBuilder>());

        var result = await pipeline.ExecuteAsync("cert.approve", new Dictionary<string, object?>
        {
            ["requestId"] = "req-001"
        });

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Be("approved");
        invoker.SeenContext.Should().NotBeNull();
        invoker.SeenContext!.CapabilityId.Should().Be("cert.approve");
        invoker.SeenContext.Items.Should().ContainKey("__domainEvents");
    }

    private sealed class CapturingContextAwareInvoker : ICapabilityContextAwareHandlerInvoker
    {
        public CapabilityExecutionContext? SeenContext { get; private set; }

        public Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            SeenContext = context;
            context.Items["__domainEvents"] = new object[] { new ApprovedEvent("req-001") };
            return Task.FromResult<object?>("approved");
        }

        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>(null);
    }

    private sealed record ApprovedEvent(string RequestId);

    private sealed class TestProvider : IDescriptorProvider<CapabilityDescriptor>
    {
        private readonly IReadOnlyList<CapabilityDescriptor> _descriptors;

        public TestProvider(IReadOnlyList<CapabilityDescriptor> descriptors)
        {
            _descriptors = descriptors;
        }

        public IReadOnlyList<CapabilityDescriptor> GetDescriptors()
        {
            return _descriptors;
        }
    }
}
