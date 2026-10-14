using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.Local.Tests.Integration;

internal static class LocalEventTestComposition
{
    public static ServiceProvider Build(
        Action<LocalEventDispatchRegistrationBuilder> declare,
        Action<IServiceCollection>? configure = null,
        Action<LocalDeadLetterOptions>? configureDeadLetter = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        if (configureDeadLetter is not null)
        {
            services.Configure(configureDeadLetter);
        }

        services.AddCrestLocalEventDispatch(declare);
        configure?.Invoke(services);

        // Mirrors the owning assembly's generated AddLocalEvents() activating its per-host codec
        // contribution (the generated AddCodecs() registers service contributions into this host).
        LocalEventJsonCodecContributions.AddCodecs(services);
        return services.BuildServiceProvider();
    }

    public static JsonTypeInfo<TEvent> CodecFor<TEvent>()
        => (JsonTypeInfo<TEvent>)LocalEventJsonSerializerContext.Default.GetTypeInfo(typeof(TEvent))!;

    public static byte[] SerializeWithCodec<TEvent>(TEvent @event)
        => JsonSerializer.SerializeToUtf8Bytes(@event, CodecFor<TEvent>());

    public static DeadLetterMessage CreateLegacyMessage<TEvent>(
        byte[] payload,
        int eventVersion = 1,
        int retryCount = 0,
        int maxRetries = 3,
        string? payloadTypeFullName = null,
        string? messageId = null)
        => new(
            messageId ?? Guid.NewGuid().ToString("N"),
            typeof(TEvent).FullName!,
            eventVersion,
            null,
            null,
            null,
            EventScope.Local,
            payloadTypeFullName ?? typeof(TEvent).AssemblyQualifiedName!,
            payload,
            "seeded failure",
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            retryCount,
            maxRetries,
            DeadLetterStatus.Pending);

    public static DeadLetterMessage CreateModernMessage<TEvent>(
        byte[] payload,
        string replayKey,
        int eventVersion = 1,
        int retryCount = 0,
        int maxRetries = 3,
        string? messageId = null,
        string? tenantId = null,
        string? correlationId = null)
        => new(
            messageId ?? Guid.NewGuid().ToString("N"),
            typeof(TEvent).FullName!,
            eventVersion,
            null,
            correlationId,
            tenantId,
            EventScope.Local,
            typeof(TEvent).AssemblyQualifiedName!,
            payload,
            "seeded failure",
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            retryCount,
            maxRetries,
            DeadLetterStatus.Pending,
            LocalReplayKey: replayKey,
            LocalContextVersion: LocalEventExecutionContextSnapshot.CurrentContextVersion);
}

/// <summary>Shared observable state for per-event controllable handlers.</summary>
internal sealed class HandlerControl
{
    public int Calls { get; private set; }

    public bool ShouldFail { get; set; }

    public Func<int, Task>? OnHandleAsync { get; set; }

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int RegisterCall() => ++Calls;
}

internal sealed class ReplayableFailingHandler : ILocalEventHandler<ReplayableFailingEvent>
{
    private readonly HandlerControl _control;

    public ReplayableFailingHandler(HandlerControl control) => _control = control;

    public Task HandleAsync(ReplayableFailingEvent @event, System.Threading.CancellationToken cancellationToken = default)
    {
        var call = _control.RegisterCall();
        if (_control.OnHandleAsync is not null)
        {
            return _control.OnHandleAsync(call);
        }

        return _control.ShouldFail
            ? Task.FromException(new InvalidOperationException($"ReplayableFailingEvent handler failure #{call}"))
            : Task.CompletedTask;
    }
}

internal sealed class CancellableReplayHandler : ILocalEventHandler<CancellableReplayEvent>
{
    private readonly HandlerControl _control;

    public CancellableReplayHandler(HandlerControl control) => _control = control;

    public async Task HandleAsync(CancellableReplayEvent @event, System.Threading.CancellationToken cancellationToken = default)
    {
        _control.RegisterCall();
        _control.Started.TrySetResult();
        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

internal sealed class ReplayableDispatchHandler : ILocalEventHandler<ReplayableDispatchEvent>
{
    public int InvocationCount { get; private set; }

    public string? LastMessage { get; private set; }

    public Task HandleAsync(ReplayableDispatchEvent @event, System.Threading.CancellationToken cancellationToken = default)
    {
        InvocationCount++;
        LastMessage = @event.Message;
        return Task.CompletedTask;
    }
}
