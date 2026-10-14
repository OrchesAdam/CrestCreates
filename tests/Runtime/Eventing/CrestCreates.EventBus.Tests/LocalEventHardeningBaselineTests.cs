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
/// #146 S1 failing-baseline tests. Each test asserts the designed behavior from
/// docs/superpowers/specs/2026-10-14-issue-146-local-event-framework-hardening-design.md and is
/// expected to FAIL against the #126 implementation, proving the recorded gap with a real behavioral
/// error (not merely an absent new API). The S2/S3 implementations must make these pass; the codec
/// tests will be adapted to the per-host contribution API when it lands, exactly like the #127
/// review probes were.
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

    private static LocalEventBinding TypedBinding<TEvent>(
        Func<JsonTypeInfo?> codecFactory,
        IReadOnlyList<Type>? expectedHandlers = null,
        string sourceAssembly = "Baseline.Handlers")
        where TEvent : ILocalEvent
        => LocalEventBinding.Create<TEvent>(
            new LocalEventInvoker<TEvent>((provider, @event, cancellationToken) =>
                LocalEventDispatch.InvokeHandlersAsync(provider, @event, cancellationToken)),
            replayVersion: 1,
            replayable: true,
            codecFactory,
            expectedHandlers,
            legacyIdentities: null,
            sourceAssembly);

    private static ServiceProvider BuildProvider(LocalEventBinding binding, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddSingleton<ILocalEventRegistrationContributor>(
            new ManualContributor("baseline", builder => builder.Add(binding)));
        services.AddCrestLocalEventDispatch();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // ── H01 baseline ──

    [Fact]
    public void Host_B_borrowing_a_process_global_codec_must_fail_freeze()
    {
        // Host A activates its owning assembly's codec contribution (process-global today).
        LocalEventJsonCodecRegistry.Register(
            typeof(PollutedHostEvent),
            static () => HardeningBaselineJsonContext.Default.PollutedHostEvent);

        // Host B never activates that contribution; its binding only carries the generator-shaped
        // factory that resolves through the process-global table.
        var binding = TypedBinding<PollutedHostEvent>(
            static () => LocalEventJsonCodecRegistry.Resolve(typeof(PollutedHostEvent)));
        using var provider = BuildProvider(binding);

        // Designed behavior: Host B must fail — a codec contributed by another host must not satisfy
        // B's required replay contract.
        Assert.Throws<LocalEventDispatchConfigurationException>(
            () => provider.GetRequiredService<ILocalEventDispatchRegistry>());
    }

    // ── H02 baseline ──

    [Fact]
    public void Conflicting_process_global_codecs_for_one_type_must_not_be_first_wins()
    {
        // Two different contributions claim the same payload type: the first is correct, the second
        // describes a different contract. First-wins silently ignores the second.
        LocalEventJsonCodecRegistry.Register(
            typeof(FirstWinsProbeEvent),
            static () => HardeningBaselineJsonContext.Default.FirstWinsProbeEvent);
        LocalEventJsonCodecRegistry.Register(
            typeof(FirstWinsProbeEvent),
            static () => HardeningBaselineJsonContext.Default.GlobalConflictProbeEvent);

        var binding = TypedBinding<FirstWinsProbeEvent>(
            static () => LocalEventJsonCodecRegistry.Resolve(typeof(FirstWinsProbeEvent)));
        using var provider = BuildProvider(binding);

        // Designed behavior: conflicting contributions for one payload type are a deterministic
        // freeze violation regardless of registration order.
        Assert.Throws<LocalEventDispatchConfigurationException>(
            () => provider.GetRequiredService<ILocalEventDispatchRegistry>());
    }

    // ── P01 baseline ──

    [Fact]
    public async Task Transient_handler_is_constructed_once_and_executed_as_the_same_instance()
    {
        Interlocked.Exchange(ref CountingTransientHandler.Constructions, 0);
        CountingTransientHandler.ExecutedInstance = null;

        var binding = TypedBinding<TransientProbeEvent>(
            static () => HardeningBaselineJsonContext.Default.TransientProbeEvent,
            expectedHandlers: [typeof(CountingTransientHandler)]);
        using var provider = BuildProvider(binding, services =>
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
