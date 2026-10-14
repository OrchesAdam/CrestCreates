using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Capability.Tests;

/// <summary>
/// R1-LE-07: the first-party <see cref="EventPublisher"/> always publishes the internal
/// dispatch-only <c>CapabilityEventEnvelope</c>. When a host wires it onto the official
/// <see cref="ILocalEventBus"/>, that envelope must be part of the typed membership — declared by
/// the owning Capability package and activated by <c>AddCapabilityRuntime</c> — otherwise every
/// lifecycle notification throws <see cref="UnknownLocalEventException"/> after the handler has
/// already completed. The envelope stays a zero-handler notification: it is never replayable and
/// carries no JSON codec.
/// </summary>
public class EventPublisherLocalEventBusTests
{
    private const string EnvelopeTypeName = "CrestCreates.Capability.CapabilityEventEnvelope";

    [Fact]
    public async Task FirstPartyPublisher_OntoOfficialBus_DoesNotThrowUnknownLocalEvent()
    {
        using var provider = Compose();
        using var scope = provider.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        await publisher
            .Invoking(p => p.PublishAsync("capability.succeeded", new { ok = true }))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task LifecycleMiddleware_OntoOfficialBus_PublishesSuccessAndFailureWithoutThrowing()
    {
        using var provider = Compose();
        using var scope = provider.CreateScope();
        var middleware = scope.ServiceProvider.GetRequiredService<EventPublishingMiddleware>();

        var context = new CapabilityExecutionContext
        {
            ServiceProvider = scope.ServiceProvider,
            CapabilityName = "sample.capability",
            CapabilityVersion = 1,
            CapabilityContractHash = "hash",
        };

        var success = await middleware.InvokeAsync(
            context,
            _ => Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));
        success.IsSuccess.Should().BeTrue();

        var failure = await middleware.InvokeAsync(
            context,
            _ => Task.FromResult(CapabilityExecutionResult.Failure("ERR", "bad", TimeSpan.Zero)));
        failure.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void CapabilityEnvelope_IsDeclaredAsZeroHandlerNotification_NotReplayable()
    {
        using var provider = Compose();

        var registry = provider.GetRequiredService<ILocalEventDispatchRegistry>();
        var binding = registry.Bindings
            .Single(candidate => candidate.EventType.FullName == EnvelopeTypeName);

        binding.SourceAssembly.Should().Be("CrestCreates.Capability");
        binding.ExpectedHandlerTypes.Should().BeEmpty();
        binding.Replayable.Should().BeFalse();
        binding.Codec.Should().BeNull();
    }

    private static ServiceProvider Compose()
    {
        var services = new ServiceCollection();
        services.AddCrestLocalEventDispatch();
        services.AddCapabilityRuntime();
        services.AddScoped<IEventPublisher>(sp => new EventPublisher(sp.GetRequiredService<ILocalEventBus>()));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
