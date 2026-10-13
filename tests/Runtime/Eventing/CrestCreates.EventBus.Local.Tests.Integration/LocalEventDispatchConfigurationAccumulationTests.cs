using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

/// <summary>
/// R1-LE-05：手工声明（configure 回调）必须独立累积、冻结时统一应用。默认无回调入口先装配后，
/// Host/模块再追加的声明不得被静默丢弃。覆盖默认入口→Host 声明、多次声明、以及同步/Channel 组合。
/// </summary>
public class LocalEventDispatchConfigurationAccumulationTests
{
    private sealed class AccumulatedEventA : ILocalEvent { }

    private sealed class AccumulatedEventB : ILocalEvent { }

    private sealed class AccumulatedEventC : ILocalEvent { }

    private sealed class AccumulatedEventD : ILocalEvent { }

    private static ServiceProvider Build(params System.Action<IServiceCollection>[] steps)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        foreach (var step in steps)
        {
            step(services);
        }

        return services.BuildServiceProvider();
    }

    private static void AssertDeclared(System.IServiceProvider provider, System.Type[] eventTypes)
    {
        var registry = provider.GetRequiredService<ILocalEventDispatchRegistry>();
        foreach (var eventType in eventTypes)
        {
            registry.TryGet(eventType, out _).Should().BeTrue(
                $"manual declaration of '{eventType.Name}' must survive composition order");
        }
    }

    [Fact]
    public void DefaultEntryThenHostDeclaration_IsApplied()
    {
        using var provider = Build(
            services => services.AddCrestLocalEventDispatch(),
            services => services.AddCrestLocalEventDispatch(builder => builder.Declare<AccumulatedEventA>()));

        AssertDeclared(provider, [typeof(AccumulatedEventA)]);
    }

    [Fact]
    public void MultipleDeclarations_AcrossEntries_AreAllApplied()
    {
        using var provider = Build(
            services => services.AddCrestLocalEventDispatch(),
            services => services.AddCrestLocalEventDispatch(builder => builder.Declare<AccumulatedEventA>()),
            services => services.AddCrestLocalEventDispatch(builder => builder.Declare<AccumulatedEventB>()));

        AssertDeclared(provider, [typeof(AccumulatedEventA), typeof(AccumulatedEventB)]);
    }

    [Fact]
    public void SyncEntryThenChannelDeclaration_IsApplied()
    {
        using var provider = Build(
            services => services.AddCrestLocalEventDispatch(),
            services => services.AddCrestLocalChannelEventDispatch(builder => builder.Declare<AccumulatedEventC>()));

        AssertDeclared(provider, [typeof(AccumulatedEventC)]);
    }

    [Fact]
    public void ChannelEntryThenSyncDeclaration_IsApplied()
    {
        using var provider = Build(
            services => services.AddCrestLocalChannelEventDispatch(),
            services => services.AddCrestLocalEventDispatch(builder => builder.Declare<AccumulatedEventD>()));

        AssertDeclared(provider, [typeof(AccumulatedEventD)]);
    }
}
