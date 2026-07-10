using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Capability.Tests;

/// <summary>
/// Integration tests for AppService-to-Capability compatibility projection.
/// Verifies: scoped DI resolution, context-aware invoker dispatch, native+compat coexistence.
/// </summary>
public class AppServiceCompatibilityProjectionTests
{
    [Fact]
    public async Task CompatibilityInvoker_ResolvesAppService_FromScopedProvider()
    {
        // Arrange — simulate what the generated invoker does:
        // resolve the AppService from the scoped IServiceProvider
        var services = new ServiceCollection();
        services.AddScoped<BookAppService>();
        services.AddScoped<ITenantContext, FakeTenantContext>();
        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var scopedProvider = scope.ServiceProvider;
        var appService = scopedProvider.GetRequiredService<BookAppService>();

        // Act — the AppService should share the same scoped tenant context
        var tenantContext = scopedProvider.GetRequiredService<ITenantContext>();

        // Assert
        appService.TenantContext.Should().BeSameAs(tenantContext);
        tenantContext.TenantId.Should().Be("tenant-001");
    }

    [Fact]
    public async Task CompatibilityInvoker_InvokesThroughPipeline_WithExecutionContext()
    {
        // Arrange — set up capability pipeline with a compatibility invoker
        var registry = new CapabilityRegistry(
            new RegistryValidationEngine<CapabilityDescriptor>(
                Array.Empty<IRegistryValidator<CapabilityDescriptor>>()));
        registry.Build([new TestProvider([new CapabilityDescriptor
        {
            Id = "compat.appservice.book.create",
            Name = "CreateBook",
            Version = 1,
            CapabilityKind = CapabilityKind.Command,
            ProjectionKind = CapabilityProjectionKind.AppServiceCompatibility
        }])]);

        var invoker = new FakeCompatibilityInvoker();
        var resolver = new CapabilityHandlerResolver();
        resolver.Register("compat.appservice.book.create", invoker);

        var services = new ServiceCollection();
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton<ICapabilityHandlerResolver>(resolver);
        services.AddSingleton(new CapabilityPipelineBuilder());
        var provider = services.BuildServiceProvider();

        var pipeline = new CapabilityPipeline(
            provider,
            registry,
            resolver,
            provider.GetRequiredService<CapabilityPipelineBuilder>(),
            new DescriptorStableHashBuilder(new DefaultCanonicalHashComputer()));

        // Act
        var result = await pipeline.ExecuteAsync("compat.appservice.book.create", new Dictionary<string, object?>
        {
            ["title"] = "Test Book"
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        invoker.ReceivedContext.Should().NotBeNull();
        invoker.ReceivedContext!.ServiceProvider.Should().NotBeNull();
        invoker.ReceivedContext.CapabilityId.Should().Be("compat.appservice.book.create");
    }

    [Fact]
    public async Task NativeAndCompatibilityHandlers_Coexist_InSameResolver()
    {
        // Arrange — register both a native handler and a compatibility handler
        var nativeInvoker = new MockNativeInvoker("native-result");
        var compatInvoker = new MockNativeInvoker("compat-result");

        CapabilityHandlerResolverProvider.Register("cert.approve", nativeInvoker);
        CapabilityHandlerResolverProvider.Register("compat.appservice.book.create", compatInvoker);

        var resolver = CapabilityHandlerResolverProvider.GetResolver();

        // Act & Assert — both should be resolvable, neither overwrites the other
        resolver.Resolve("cert.approve").Should().BeSameAs(nativeInvoker);
        resolver.Resolve("compat.appservice.book.create").Should().BeSameAs(compatInvoker);
    }

    // --- Test infrastructure ---

    private interface ITenantContext
    {
        string TenantId { get; }
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public string TenantId => "tenant-001";
    }

    private sealed class BookAppService
    {
        public ITenantContext TenantContext { get; }

        public BookAppService(ITenantContext tenantContext)
        {
            TenantContext = tenantContext;
        }

        public Task<string> CreateAsync(string title, CancellationToken ct)
            => Task.FromResult($"Created: {title} in tenant {TenantContext.TenantId}");
    }

    private sealed class FakeCompatibilityInvoker : ICapabilityContextAwareHandlerInvoker
    {
        public CapabilityExecutionContext? ReceivedContext { get; private set; }

        public Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            ReceivedContext = context;
            return Task.FromResult<object?>("created");
        }

        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => throw new InvalidOperationException("Context-aware overload should be used.");
    }

    private sealed class MockNativeInvoker : ICapabilityHandlerInvoker
    {
        private readonly string _result;

        public MockNativeInvoker(string result) => _result = result;

        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>(_result);
    }

    private sealed class TestProvider : IDescriptorProvider<CapabilityDescriptor>
    {
        private readonly IReadOnlyList<CapabilityDescriptor> _descriptors;

        public TestProvider(IReadOnlyList<CapabilityDescriptor> descriptors)
            => _descriptors = descriptors;

        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => _descriptors;
    }
}
