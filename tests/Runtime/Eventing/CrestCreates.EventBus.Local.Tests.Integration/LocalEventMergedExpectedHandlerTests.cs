using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

/// <summary>
/// R1-LE-04：普通分发必须用 binding 合并后的完整 expected 清单做预校验，而不是被保留的那一个
/// contributor 的局部闭包。否则同事件被多个 contributor 贡献必需 handler 时，缺任一 handler 时
/// 普通分发会静默成功（与重放路径语义不一致）。
/// </summary>
public class LocalEventMergedExpectedHandlerTests
{
    internal sealed class MergedHandlerEvent : ILocalEvent
    {
    }

    internal sealed class FirstMergedHandler : ILocalEventHandler<MergedHandlerEvent>
    {
        public int Calls { get; private set; }

        public Task HandleAsync(MergedHandlerEvent @event, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    internal sealed class SecondMergedHandler : ILocalEventHandler<MergedHandlerEvent>
    {
        public int Calls { get; private set; }

        public Task HandleAsync(MergedHandlerEvent @event, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Mimics a generated contributor: typed binding + its own expected-handler subset.</summary>
    private sealed class ExpectedHandlerContributor : ILocalEventRegistrationContributor
    {
        private readonly Type[] _expected;

        public ExpectedHandlerContributor(string contributorId, params Type[] expected)
        {
            ContributorId = contributorId;
            _expected = expected;
        }

        public string ContributorId { get; }

        public void Register(LocalEventDispatchRegistrationBuilder builder)
            => builder.Add(LocalEventRegistration.Create<MergedHandlerEvent>(
                new LocalEventInvoker<MergedHandlerEvent>(),
                replayVersion: 1,
                replayable: false,
                codecFactory: null,
                expectedHandlerTypes: _expected,
                legacyIdentities: null,
                sourceAssembly: ContributorId));
    }

    private static ServiceProvider Build(
        ILocalEventRegistrationContributor[] contributors,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        foreach (var contributor in contributors)
        {
            services.AddSingleton<ILocalEventRegistrationContributor>(contributor);
        }

        services.AddCrestLocalEventDispatch();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static Task DispatchAsync(ServiceProvider provider)
    {
        var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>();
        return dispatcher.DispatchAsync(new MergedHandlerEvent());
    }

    [Fact]
    public async Task MissingMergedHandler_Throws_BeforeAnyHandlerRuns()
    {
        var first = new FirstMergedHandler();
        using var provider = Build(
            new ILocalEventRegistrationContributor[]
            {
                new ExpectedHandlerContributor("ContributorA", typeof(FirstMergedHandler)),
                new ExpectedHandlerContributor("ContributorB", typeof(SecondMergedHandler)),
            },
            configure: services => services.AddSingleton<ILocalEventHandler<MergedHandlerEvent>>(first));

        var failure = await Assert.ThrowsAsync<LocalEventRequiredHandlerMissingException>(
            () => DispatchAsync(provider));

        failure.HandlerType.Should().Be(typeof(SecondMergedHandler));
        first.Calls.Should().Be(0);
    }

    [Fact]
    public async Task MissingMergedHandler_Throws_WhenContributorOrderSwapped()
    {
        var second = new SecondMergedHandler();
        using var provider = Build(
            new ILocalEventRegistrationContributor[]
            {
                new ExpectedHandlerContributor("ContributorB", typeof(SecondMergedHandler)),
                new ExpectedHandlerContributor("ContributorA", typeof(FirstMergedHandler)),
            },
            configure: services => services.AddSingleton<ILocalEventHandler<MergedHandlerEvent>>(second));

        var failure = await Assert.ThrowsAsync<LocalEventRequiredHandlerMissingException>(
            () => DispatchAsync(provider));

        failure.HandlerType.Should().Be(typeof(FirstMergedHandler));
        second.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AllMergedHandlers_RunExactlyOnce_WhenAllPresent()
    {
        var first = new FirstMergedHandler();
        var second = new SecondMergedHandler();
        using var provider = Build(
            new ILocalEventRegistrationContributor[]
            {
                new ExpectedHandlerContributor("ContributorA", typeof(FirstMergedHandler)),
                new ExpectedHandlerContributor("ContributorB", typeof(SecondMergedHandler)),
            },
            configure: services =>
            {
                services.AddSingleton<ILocalEventHandler<MergedHandlerEvent>>(first);
                services.AddSingleton<ILocalEventHandler<MergedHandlerEvent>>(second);
            });

        await DispatchAsync(provider);

        first.Calls.Should().Be(1);
        second.Calls.Should().Be(1);
    }

    [Fact]
    public void MergedBinding_ExposesUnion_OfExpectedHandlers()
    {
        using var provider = Build(
            new ILocalEventRegistrationContributor[]
            {
                new ExpectedHandlerContributor("ContributorA", typeof(FirstMergedHandler)),
                new ExpectedHandlerContributor("ContributorB", typeof(SecondMergedHandler)),
            });

        var registry = provider.GetRequiredService<ILocalEventDispatchRegistry>();
        registry.TryGet(typeof(MergedHandlerEvent), out var binding).Should().BeTrue();
        binding.ExpectedHandlerTypes.Should().BeEquivalentTo(
            new[] { typeof(FirstMergedHandler), typeof(SecondMergedHandler) });
    }
}
