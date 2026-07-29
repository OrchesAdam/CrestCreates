using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityServiceCollectionExtensionsTests
{
    [Fact]
    public async Task CapabilityHostWithoutAccountabilityFailsDuringStartup()
    {
        var services = new ServiceCollection();
        services.AddCapabilityRuntime();
        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "CapabilityAccountabilityCompositionValidator");

        var action = () => validator.StartAsync(CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CAPABILITY_ACCOUNTABILITY_FOUNDATION_MISSING*");
    }

    [Fact]
    public async Task CapabilityHostWithAccountabilityPassesStartupValidation()
    {
        var services = new ServiceCollection();
        services.AddCapabilityRuntime();
        services.AddAccountability();
        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "CapabilityAccountabilityCompositionValidator");

        var action = () => validator.StartAsync(CancellationToken.None);

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public void AddCapabilityPipeline_BothResolvers_AreSameInstance()
    {
        var services = new ServiceCollection();
        services.AddCapabilityPipeline();
        var sp = services.BuildServiceProvider();

        var concrete = sp.GetRequiredService<CapabilityHandlerResolver>();
        var iface = sp.GetRequiredService<ICapabilityHandlerResolver>();

        concrete.Should().BeSameAs(iface);
    }

    [Fact]
    public void AddCapabilityPipeline_ComposesFromModules()
    {
        var services = new ServiceCollection();
        services.AddCapabilityPipeline();

        // Add a test module after AddCapabilityPipeline to ensure composition
        services.AddSingleton<ICapabilityHandlerModule>(
            new TestModule("test-module", r => r.Register("test-cap", new TestInvoker())));

        var sp = services.BuildServiceProvider();

        var resolver = sp.GetRequiredService<CapabilityHandlerResolver>();
        var result = resolver.Resolve("test-cap");
        result.Should().NotBeNull();
    }

    [Fact]
    public void AddCapabilityPipeline_RegistersLegacyModule()
    {
        var services = new ServiceCollection();
        services.AddCapabilityPipeline();
        var sp = services.BuildServiceProvider();

        var modules = sp.GetServices<ICapabilityHandlerModule>().ToList();
        modules.Should().ContainSingle(m => m.Id == "legacy-capability-pipeline");
    }

    private sealed class TestModule : ICapabilityHandlerModule
    {
        private readonly Action<CapabilityHandlerResolver> _apply;

        public TestModule(string id, Action<CapabilityHandlerResolver> apply)
        {
            Id = id;
            _apply = apply;
        }

        public string Id { get; }
        public void Apply(CapabilityHandlerResolver resolver) => _apply(resolver);
    }

    private sealed class TestInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>(null);
    }
}
