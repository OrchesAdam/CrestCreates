using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Tests.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Tests;

public class LocalEventDispatchRegistryTests
{
    [Fact]
    public async Task Replayable_Declaration_Without_Codec_Fails_At_Freeze()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddCrestLocalEventDispatch(builder => builder.Declare<MissingCodecProbeEvent>(replayable: true));

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<LocalEventDispatchConfigurationException>(
            () => provider.GetRequiredService<ILocalEventDispatchRegistry>());

        Assert.Contains(exception.Violations, violation => violation.Contains("resolved no JSON type info"));
    }

    [Fact]
    public async Task Replayable_Declaration_With_Handwritten_Contract_Passes_Freeze()
    {
        LocalEventJsonCodecRegistry.Register(
            typeof(CodecContractProbeEvent),
            static () => LocalEventTestContracts.Default.GetTypeInfo(typeof(CodecContractProbeEvent)));

        var services = new ServiceCollection();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddCrestLocalEventDispatch(builder => builder.Declare<CodecContractProbeEvent>(replayable: true, version: 2));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ILocalEventDispatchRegistry>();

        Assert.True(registry.TryGet(typeof(CodecContractProbeEvent), out var binding));
        Assert.True(binding.Replayable);
        Assert.Equal(2, binding.ReplayVersion);
        Assert.NotNull(binding.CodecFactory!());
    }

    [Fact]
    public void Conflicting_Replayable_Flags_Across_Contributors_Fail_At_Freeze()
    {
        var replayable = LocalEventBinding.Create<TestLocalEvent>(
            new LocalEventInvoker<TestLocalEvent>(static (provider, @event, cancellationToken) =>
                LocalEventDispatch.InvokeHandlersAsync(provider, @event, cancellationToken)),
            1,
            replayable: true,
            codecFactory: null,
            expectedHandlerTypes: null,
            legacyIdentities: null,
            sourceAssembly: "AssemblyA");

        var dispatchOnly = LocalEventBinding.Create<TestLocalEvent>(
            new LocalEventInvoker<TestLocalEvent>(static (provider, @event, cancellationToken) =>
                LocalEventDispatch.InvokeHandlersAsync(provider, @event, cancellationToken)),
            1,
            replayable: false,
            codecFactory: null,
            expectedHandlerTypes: null,
            legacyIdentities: null,
            sourceAssembly: "AssemblyB");

        var services = new ServiceCollection();
        services.AddCrestLocalEventDispatch(builder =>
        {
            builder.Add(replayable);
            builder.Add(dispatchOnly);
        });

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<LocalEventDispatchConfigurationException>(
            () => provider.GetRequiredService<ILocalEventDispatchRegistry>());

        Assert.Contains(exception.Violations, violation =>
            violation.Contains("declared both as replayable") && violation.Contains("AssemblyB"));
    }

    [Fact]
    public async Task FanOut_Across_Contributors_Merges_Expected_Handlers()
    {
        var contributorA = new StaticBindingContributor("AssemblyA", EventDispatchBinding.Create(
            expectedHandlerTypes: new[] { typeof(LocalEventBusTests.FirstRecordingHandler) }));
        var contributorB = new StaticBindingContributor("AssemblyB", EventDispatchBinding.Create(
            expectedHandlerTypes: new[] { typeof(LocalEventBusTests.SecondRecordingHandler) }));

        var first = new LocalEventBusTests.FirstRecordingHandler(new List<string>());
        var second = new LocalEventBusTests.SecondRecordingHandler(new List<string>());

        var services = new ServiceCollection();
        services.AddCrestLocalEventDispatch();
        services.AddSingleton<ILocalEventRegistrationContributor>(contributorA);
        services.AddSingleton<ILocalEventRegistrationContributor>(contributorB);
        services.AddSingleton(first);
        services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<LocalEventBusTests.FirstRecordingHandler>());
        services.AddSingleton(second);
        services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<LocalEventBusTests.SecondRecordingHandler>());

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ILocalEventDispatchRegistry>();

        Assert.True(registry.TryGet(typeof(TestLocalEvent), out var binding));
        Assert.Equal(
            new[] { typeof(LocalEventBusTests.FirstRecordingHandler), typeof(LocalEventBusTests.SecondRecordingHandler) },
            binding.ExpectedHandlerTypes.OrderBy(type => type.Name).ToArray());

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        await eventBus.PublishAsync(new TestLocalEvent(Guid.NewGuid()));
    }

    [Fact]
    public async Task Missing_Expected_Handler_Module_Fails_Before_Any_Handler_Runs()
    {
        var contributor = new StaticBindingContributor("AssemblyExpected", LocalEventBinding.Create<TestLocalEvent>(
            new LocalEventInvoker<TestLocalEvent>(static (provider, @event, cancellationToken) =>
                LocalEventDispatch.InvokeHandlersAsync(
                    provider, @event, cancellationToken, new[] { typeof(LocalEventDispatchRegistryTests.NeverRegisteredHandler) })),
            1,
            replayable: false,
            codecFactory: null,
            expectedHandlerTypes: new[] { typeof(LocalEventDispatchRegistryTests.NeverRegisteredHandler) },
            legacyIdentities: null,
            sourceAssembly: "AssemblyExpected"));

        var registeredHandler = new LocalEventBusTests.RecordingLocalEventHandler();

        var services = new ServiceCollection();
        services.AddCrestLocalEventDispatch();
        services.AddSingleton<ILocalEventRegistrationContributor>(contributor);
        services.AddSingleton(registeredHandler);
        services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<LocalEventBusTests.RecordingLocalEventHandler>());

        using var provider = services.BuildServiceProvider();
        var eventBus = provider.GetRequiredService<ILocalEventBus>();

        var exception = await Assert.ThrowsAsync<LocalEventRequiredHandlerMissingException>(
            () => eventBus.PublishAsync(new TestLocalEvent(Guid.NewGuid())));

        Assert.Equal(typeof(LocalEventDispatchRegistryTests.NeverRegisteredHandler), exception.HandlerType);
        Assert.False(registeredHandler.Handled);
    }

    [Fact]
    public void Legacy_Alias_Colliding_With_Current_Identity_Fails_At_Freeze()
    {
        var current = LocalEventBinding.Create<TestLocalEvent>(
            new LocalEventInvoker<TestLocalEvent>(static (provider, @event, cancellationToken) =>
                LocalEventDispatch.InvokeHandlersAsync(provider, @event, cancellationToken)),
            1,
            replayable: false,
            codecFactory: null,
            expectedHandlerTypes: null,
            legacyIdentities: null,
            sourceAssembly: "AssemblyCurrent");

        var aliasing = LocalEventBinding.Create<CollidingAliasEvent>(
            new LocalEventInvoker<CollidingAliasEvent>(static (provider, @event, cancellationToken) =>
                LocalEventDispatch.InvokeHandlersAsync(provider, @event, cancellationToken)),
            1,
            replayable: false,
            codecFactory: null,
            expectedHandlerTypes: null,
            legacyIdentities: new[] { new LocalEventLegacyIdentity(typeof(TestLocalEvent).FullName!, 1) },
            sourceAssembly: "AssemblyAliasing");

        var services = new ServiceCollection();
        services.AddCrestLocalEventDispatch(builder =>
        {
            builder.Add(current);
            builder.Add(aliasing);
        });

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<LocalEventDispatchConfigurationException>(
            () => provider.GetRequiredService<ILocalEventDispatchRegistry>());

        Assert.Contains(exception.Violations, violation => violation.Contains("collides with the current identity"));
    }

    [Fact]
    public void Duplicate_Contributor_Activation_Is_Idempotent()
    {
        var contributor = new StaticBindingContributor("AssemblyDup", EventDispatchBinding.Create());

        var services = new ServiceCollection();
        services.AddCrestLocalEventDispatch();
        services.AddSingleton<ILocalEventRegistrationContributor>(contributor);
        services.AddSingleton<ILocalEventRegistrationContributor>(contributor);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ILocalEventDispatchRegistry>();

        Assert.Single(registry.Bindings);
    }

    [Fact]
    public void Unregistered_Event_Is_Not_Resolvable()
    {
        var services = new ServiceCollection();
        services.AddCrestLocalEventDispatch();
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ILocalEventDispatchRegistry>();

        Assert.False(registry.TryGet(typeof(UnregisteredProbeEvent), out _));
    }

    internal sealed class NeverRegisteredHandler : ILocalEventHandler<TestLocalEvent>
    {
        public Task HandleAsync(TestLocalEvent @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StaticBindingContributor : ILocalEventRegistrationContributor
    {
        private readonly LocalEventBinding _binding;

        public StaticBindingContributor(string contributorId, LocalEventBinding binding)
        {
            ContributorId = contributorId;
            _binding = binding;
        }

        public string ContributorId { get; }

        public void Register(LocalEventDispatchRegistrationBuilder builder) => builder.Add(_binding);
    }
}

internal static class EventDispatchBinding
{
    public static LocalEventBinding Create(IReadOnlyList<Type>? expectedHandlerTypes = null)
        => LocalEventBinding.Create<TestLocalEvent>(
            new LocalEventInvoker<TestLocalEvent>((provider, @event, cancellationToken) =>
                LocalEventDispatch.InvokeHandlersAsync(provider, @event, cancellationToken, expectedHandlerTypes)),
            1,
            replayable: false,
            codecFactory: null,
            expectedHandlerTypes: expectedHandlerTypes,
            legacyIdentities: null,
            sourceAssembly: "AssemblyTest");
}

internal sealed class UnregisteredProbeEvent : DomainEvent
{
}

internal sealed class MissingCodecProbeEvent : DomainEvent
{
}

internal sealed class CodecContractProbeEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

internal sealed class CollidingAliasEvent : DomainEvent
{
}

// Handwritten closed JSON contract used to exercise the replayable-with-codec freeze path
// without the build-time toolchain (which the source-generated path covers separately).
[JsonSerializable(typeof(CodecContractProbeEvent))]
internal sealed partial class LocalEventTestContracts : JsonSerializerContext
{
}
