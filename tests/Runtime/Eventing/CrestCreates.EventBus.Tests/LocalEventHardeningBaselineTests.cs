using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Tests;

// Top-level types: STJ source generation requires all containing types to be partial, and the
// LocalEvent generator must see accessible handler/event shapes.
internal sealed class BorrowHostEvent : DomainEvent
{
}

internal sealed class PollutedHostEvent : DomainEvent
{
}

internal sealed class GlobalConflictProbeEvent : DomainEvent
{
}

internal sealed class FirstWinsProbeEvent : DomainEvent
{
}

internal sealed class TransientProbeEvent : DomainEvent
{
}

[JsonSerializable(typeof(PollutedHostEvent))]
[JsonSerializable(typeof(GlobalConflictProbeEvent))]
[JsonSerializable(typeof(FirstWinsProbeEvent))]
[JsonSerializable(typeof(TransientProbeEvent))]
internal partial class HardeningBaselineJsonContext : JsonSerializerContext;

internal sealed class CountingTransientHandler : ILocalEventHandler<TransientProbeEvent>
{
    public static int Constructions;
    public static object? ExecutedInstance;

    public CountingTransientHandler() => Interlocked.Increment(ref Constructions);

    public Task HandleAsync(TransientProbeEvent @event, CancellationToken cancellationToken = default)
    {
        ExecutedInstance = this;
        return Task.CompletedTask;
    }
}

/// <summary>
/// #146 S2 regression suite for host-isolated codec contributions, per-item conflict validation and
/// single-resolution handler execution. These began as S1 failing baselines (H01/H02/P01); the S2
/// lifecycle rework turned them green and they remain as the standing regression for the per-host
/// model specified in docs/superpowers/specs/2026-10-14-issue-146-local-event-framework-hardening-design.md.
/// </summary>
public class LocalEventHardeningBaselineTests
{
    // ── Contributors ──

    private sealed class ManualContributor(string contributorId, Action<LocalEventDispatchRegistrationBuilder> register)
        : ILocalEventRegistrationContributor
    {
        public string ContributorId => contributorId;

        public void Register(LocalEventDispatchRegistrationBuilder builder) => register(builder);
    }

    private static LocalEventRegistration ReplayableRegistration<TEvent>(
        Func<JsonTypeInfo?>? codecFactory,
        IReadOnlyList<Type>? expectedHandlers = null,
        string sourceAssembly = "Baseline.Handlers")
        where TEvent : ILocalEvent
        => LocalEventRegistration.Create<TEvent>(
            new LocalEventInvoker<TEvent>(),
            replayVersion: 1,
            replayable: true,
            codecFactory,
            expectedHandlers,
            legacyIdentities: null,
            sourceAssembly);

    private static ServiceProvider BuildProvider(LocalEventRegistration registration, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddSingleton<ILocalEventRegistrationContributor>(
            new ManualContributor("baseline", builder => builder.Add(registration)));
        services.AddCrestLocalEventDispatch();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static LocalEventJsonCodecRegistration Codec<TEvent>(string origin)
        => new()
        {
            PayloadType = typeof(TEvent),
            CodecFactory = () => HardeningBaselineJsonContext.Default.GetTypeInfo(typeof(TEvent)),
            Origin = origin,
        };

    // ── H01: per-host codec isolation ──

    [Fact]
    public void Host_B_borrowing_a_process_global_codec_must_fail_freeze()
    {
        // Host A activates its owning assembly's per-host codec contribution and freezes.
        using (var hostA = BuildProvider(
            ReplayableRegistration<PollutedHostEvent>(codecFactory: null),
            services => services.AddSingleton(Codec<PollutedHostEvent>("HostA.Owner"))))
        {
            Assert.True(hostA.GetRequiredService<ILocalEventDispatchRegistry>().TryGet(typeof(PollutedHostEvent), out var frozen));
            Assert.NotNull(frozen!.Codec);
        }

        // Host B never activates that contribution: the generator-shaped registration (no carried
        // factory) must fail freeze instead of borrowing Host A's codec.
        using var hostB = BuildProvider(ReplayableRegistration<PollutedHostEvent>(codecFactory: null));
        Assert.Throws<LocalEventDispatchConfigurationException>(
            () => hostB.GetRequiredService<ILocalEventDispatchRegistry>());
    }

    // ── H02: per-item codec conflict validation, order-independent ──

    [Fact]
    public void Conflicting_codec_contributions_for_one_type_must_not_be_first_wins()
    {
        static LocalEventDispatchConfigurationException Capture(bool goodFirst)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IEventValidator, PassThroughEventValidator>();
            var good = new LocalEventJsonCodecRegistration
            {
                PayloadType = typeof(FirstWinsProbeEvent),
                CodecFactory = static () => HardeningBaselineJsonContext.Default.FirstWinsProbeEvent,
                Origin = "Conflict.OwnerA",
            };
            var conflicting = new LocalEventJsonCodecRegistration
            {
                PayloadType = typeof(FirstWinsProbeEvent),
                CodecFactory = static () => HardeningBaselineJsonContext.Default.GlobalConflictProbeEvent,
                Origin = "Conflict.OwnerB",
            };
            services.AddSingleton(goodFirst ? good : conflicting);
            services.AddSingleton(goodFirst ? conflicting : good);
            services.AddCrestLocalEventDispatch(builder => builder.Declare<FirstWinsProbeEvent>(
                replayable: true,
                codecFactory: static () => HardeningBaselineJsonContext.Default.FirstWinsProbeEvent));
            using var provider = services.BuildServiceProvider();

            return Assert.Throws<LocalEventDispatchConfigurationException>(
                () => provider.GetRequiredService<ILocalEventDispatchRegistry>());
        }

        Assert.Contains(Capture(goodFirst: true).Violations, v => v.Contains("multiple origins"));
        Assert.Contains(Capture(goodFirst: false).Violations, v => v.Contains("multiple origins"));
    }

    [Fact]
    public void Same_origin_codec_conflicts_are_order_independent()
    {
        static LocalEventDispatchConfigurationException Capture(bool goodFirst)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IEventValidator, PassThroughEventValidator>();
            var good = new LocalEventJsonCodecRegistration
            {
                PayloadType = typeof(GlobalConflictProbeEvent),
                CodecFactory = static () => HardeningBaselineJsonContext.Default.GlobalConflictProbeEvent,
                Origin = "Same.Owner",
            };
            var wrong = new LocalEventJsonCodecRegistration
            {
                PayloadType = typeof(GlobalConflictProbeEvent),
                CodecFactory = static () => HardeningBaselineJsonContext.Default.FirstWinsProbeEvent,
                Origin = "Same.Owner",
            };
            services.AddSingleton(goodFirst ? good : wrong);
            services.AddSingleton(goodFirst ? wrong : good);
            services.AddCrestLocalEventDispatch(builder => builder.Declare<GlobalConflictProbeEvent>(replayable: true));
            using var provider = services.BuildServiceProvider();

            return Assert.Throws<LocalEventDispatchConfigurationException>(
                () => provider.GetRequiredService<ILocalEventDispatchRegistry>());
        }

        Assert.Contains(Capture(goodFirst: true).Violations, v => v.Contains("is invalid"));
        Assert.Contains(Capture(goodFirst: false).Violations, v => v.Contains("is invalid"));
    }

    // ── P01: single resolution, same instances ──

    [Fact]
    public async Task Transient_handler_is_constructed_once_and_executed_as_the_same_instance()
    {
        Interlocked.Exchange(ref CountingTransientHandler.Constructions, 0);
        CountingTransientHandler.ExecutedInstance = null;

        var registration = LocalEventRegistration.Create<TransientProbeEvent>(
            new LocalEventInvoker<TransientProbeEvent>(),
            replayVersion: 1,
            replayable: false,
            codecFactory: null,
            expectedHandlerTypes: [typeof(CountingTransientHandler)],
            legacyIdentities: null,
            sourceAssembly: "Baseline.Handlers");
        using var provider = BuildProvider(registration, services =>
        {
            services.AddTransient<ILocalEventHandler<TransientProbeEvent>, CountingTransientHandler>();
        });

        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>();
        await dispatcher.DispatchAsync(new TransientProbeEvent());

        // Designed behavior: one resolution prepares the whole actual handler list and the same
        // instances execute — a transient handler is constructed exactly once per execution.
        Assert.Equal(1, CountingTransientHandler.Constructions);
        Assert.NotNull(CountingTransientHandler.ExecutedInstance);
    }
}
