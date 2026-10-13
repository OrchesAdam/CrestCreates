using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.Generated.CrestCreates_EventBus_Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Tests;

/// <summary>
/// Exercises the generated per-assembly contributor path end to end: the host explicitly calls
/// the generated AddLocalEvents() extension and dispatch flows through the generated typed
/// invoker and handler DI bindings.
/// </summary>
public class GeneratedLocalEventRegistrationTests
{
    [Fact]
    public async Task Generated_Contributor_Dispatches_Through_Typed_Invoker()
    {
        GeneratedProbeHandler.Reset();

        var services = new ServiceCollection();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddCrestLocalEventDispatch();
        services.AddLocalEvents();

        using var provider = services.BuildServiceProvider();
        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var probe = new GeneratedProbeEvent { Message = "generated" };

        await eventBus.PublishAsync(probe);
        await eventBus.PublishAsync<GeneratedProbeEvent>(probe);

        Assert.Equal(2, GeneratedProbeHandler.InvocationCount);
        Assert.Equal(new[] { "generated", "generated" }, GeneratedProbeHandler.ObservedMessages);
    }

    [Fact]
    public async Task Generated_Activation_Is_Idempotent()
    {
        GeneratedProbeHandler.Reset();

        var services = new ServiceCollection();
        services.AddCrestLocalEventDispatch();
        services.AddLocalEvents();
        services.AddLocalEvents();

        using var provider = services.BuildServiceProvider();
        var eventBus = provider.GetRequiredService<ILocalEventBus>();

        await eventBus.PublishAsync(new GeneratedProbeEvent { Message = "once" });

        Assert.Equal(1, GeneratedProbeHandler.InvocationCount);
    }

    [Fact]
    public void Generated_Binding_Carries_Deterministic_Identity_And_Expected_Handlers()
    {
        var services = new ServiceCollection();
        services.AddCrestLocalEventDispatch();
        services.AddLocalEvents();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ILocalEventDispatchRegistry>();

        Assert.True(registry.TryGet(typeof(GeneratedProbeEvent), out var binding));
        Assert.Equal(LocalEventIdentity.BuildLocalReplayKey(typeof(GeneratedProbeEvent)), binding.LocalReplayKey);
        Assert.Equal(typeof(GeneratedProbeEvent).FullName, binding.LocalName);
        Assert.Equal(1, binding.ReplayVersion);
        Assert.False(binding.Replayable);
        Assert.Equal(new[] { typeof(GeneratedProbeHandler) }, binding.ExpectedHandlerTypes.ToArray());
        Assert.Equal("CrestCreates.EventBus.Tests", binding.SourceAssembly);
    }

    [Fact]
    public async Task Generated_Declaration_Without_Handlers_Is_A_Declared_NoOp()
    {
        var services = new ServiceCollection();
        services.AddCrestLocalEventDispatch();
        services.AddLocalEvents();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ILocalEventDispatchRegistry>();

        Assert.True(registry.TryGet(typeof(GeneratedDeclarationOnlyEvent), out var binding));
        Assert.Empty(binding.ExpectedHandlerTypes);

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var exception = await Record.ExceptionAsync(
            () => eventBus.PublishAsync(new GeneratedDeclarationOnlyEvent()));

        Assert.Null(exception);
    }

    internal sealed class GeneratedProbeHandler : ILocalEventHandler<GeneratedProbeEvent>
    {
        private static readonly object Gate = new();
        private static readonly System.Collections.Generic.List<string> Messages = [];

        public static int InvocationCount { get; private set; }

        public static System.Collections.Generic.IReadOnlyList<string> ObservedMessages
        {
            get
            {
                lock (Gate)
                {
                    return Messages.ToArray();
                }
            }
        }

        public static void Reset()
        {
            lock (Gate)
            {
                InvocationCount = 0;
                Messages.Clear();
            }
        }

        public Task HandleAsync(GeneratedProbeEvent @event, CancellationToken cancellationToken = default)
        {
            lock (Gate)
            {
                InvocationCount++;
                Messages.Add(@event.Message);
            }

            return Task.CompletedTask;
        }
    }
}

[LocalEvent]
internal sealed class GeneratedProbeEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

[LocalEvent]
internal sealed class GeneratedDeclarationOnlyEvent : DomainEvent
{
}
