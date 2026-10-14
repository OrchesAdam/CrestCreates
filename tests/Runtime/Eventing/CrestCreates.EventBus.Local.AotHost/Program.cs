using System.Text.Json;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using CrestCreates.Generated;
using CrestCreates.Generated.CrestCreates_EventBus_Local_AotHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.Local.AotHost;

internal static class Program
{
    private static int Main()
    {
        var failures = new List<string>();

        Run("sync-typed-dispatch", SyncTypedDispatchAsync, failures);
        Run("failure-recording-and-manual-replay", FailureRecordingAndManualReplayAsync, failures);
        Run("background-retry-service", BackgroundRetryServiceAsync, failures);
        Run("refusal-contracts", RefusalContractsAsync, failures);
        Run("channel-full-chain", ChannelFullChainAsync, failures);

        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                Console.Error.WriteLine($"FAIL: {failure}");
            }

            return 1;
        }

        Console.WriteLine("CRESTCREATES_LOCALEVENT_TYPED_REPLAY_NATIVE_OK");
        return 0;
    }

    private static void Run(string name, Func<Task> scenario, List<string> failures)
    {
        try
        {
            scenario().GetAwaiter().GetResult();
            Console.WriteLine($"{name}:PASS");
        }
        catch (Exception exception)
        {
            failures.Add($"{name}: {exception}");
        }
    }

    private static ServiceCollection CreateSyncServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AotControl>();
        services.AddScoped<AotScopedProbe>();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddCrestLocalEventDispatch();
        services.AddLocalEvents();
        LocalEventJsonCodecContributions.AddCodecs(services);
        return services;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Fixture assertion failed: " + message);
        }
    }

    private static async Task SyncTypedDispatchAsync()
    {
        await using var provider = CreateSyncServices().BuildServiceProvider();
        var control = provider.GetRequiredService<AotControl>();

        using var scope = provider.CreateScope();
        var eventBus = scope.ServiceProvider.GetRequiredService<ILocalEventBus>();
        var scopedProbe = scope.ServiceProvider.GetRequiredService<AotScopedProbe>();

        var probe = new AotProbeEvent { Message = "generic" };
        await eventBus.PublishAsync(probe);
        await eventBus.PublishAsync<AotProbeEvent>(new AotProbeEvent { Message = "non-generic" });

        var handler = scope.ServiceProvider.GetServices<ILocalEventHandler<AotProbeEvent>>()
            .OfType<AotProbeHandler>()
            .Single();

        Check(control.ProbeCalls == 2, $"expected two typed dispatches, observed {control.ProbeCalls}");
        Check(handler.LastMessage == "non-generic", "the scoped handler instance must observe the non-generic dispatch");
        Check(handler.ObservedProbeId == scopedProbe.Id, "handler must resolve scoped dependencies from the caller scope");
    }

    private static async Task FailureRecordingAndManualReplayAsync()
    {
        await using var provider = CreateSyncServices().BuildServiceProvider();
        var control = provider.GetRequiredService<AotControl>();
        Check(control.FailFailEvent, "fail switch must start enabled");

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var failure = await CaptureExceptionAsync(() => eventBus.PublishAsync(new AotFailEvent { Message = "primary" }));
        Check(failure is InvalidOperationException, "handler failure must propagate to the caller");

        var messages = await store.GetPendingAsync(0, 10, CancellationToken.None);
        Check(messages.Count == 1, $"expected one dead letter record, observed {messages.Count}");
        var message = messages[0];
        Check(message.LocalReplayKey == LocalEventIdentity.BuildLocalReplayKey(typeof(AotFailEvent)),
            "recorded message must carry the deterministic local replay key");
        Check(message.EventVersion == 1, "recorded message must carry the declared replay version");
        Check(message.PayloadTypeFullName.Contains(nameof(AotFailEvent), StringComparison.Ordinal),
            "payload type name must be preserved for diagnostics");

        var decoded = JsonSerializer.Deserialize(
            message.Payload,
            (System.Text.Json.Serialization.Metadata.JsonTypeInfo<AotFailEvent>)
                LocalEventJsonSerializerContext.Default.GetTypeInfo(typeof(AotFailEvent))!);
        Check(decoded?.Message == "primary", "recorded payload must round-trip through the generated codec");

        control.FailFailEvent = false;
        var result = await manager.RetryAsync(message.MessageId);
        Check(result.Success, $"manual replay must succeed after the switch flip: {result.ErrorMessage}");
        Check(control.FailEventCalls == 1 && control.LastFailEventMessage == "primary",
            "manual replay must execute the handler with the decoded payload");

        var updated = await store.GetByIdAsync(message.MessageId, CancellationToken.None);
        Check(updated!.Status == DeadLetterStatus.Retried, "successful replay must mark the message Retried");
    }

    private static async Task BackgroundRetryServiceAsync()
    {
        await using var provider = CreateSyncServices().BuildServiceProvider();
        var control = provider.GetRequiredService<AotControl>();
        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<IDeadLetterStore>();

        await CaptureExceptionAsync(() => eventBus.PublishAsync(new AotFailEvent { Message = "background" }));

        control.FailFailEvent = false;

        var options = Options.Create(new LocalDeadLetterOptions { RetryIntervalSeconds = 1 });
        var service = new LocalDeadLetterBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILocalDeadLetterReplayExecutor>(),
            options,
            provider.GetService<ILogger<LocalDeadLetterBackgroundService>>());

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            DeadLetterMessage? current = null;
            while (DateTime.UtcNow < deadline)
            {
                current = (await store.GetPendingAsync(0, 10, CancellationToken.None)).FirstOrDefault()
                    ?? (await store.GetByEventNameAsync(typeof(AotFailEvent).FullName!, 0, 10, CancellationToken.None)).FirstOrDefault();
                if (current is { Status: DeadLetterStatus.Retried })
                {
                    break;
                }

                await Task.Delay(100);
            }

            Check(current is { Status: DeadLetterStatus.Retried },
                "the actual background retry service must replay the pending message");
            Check(control.FailEventCalls == 1 && control.LastFailEventMessage == "background",
                "background replay must execute the handler with the decoded payload");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static async Task RefusalContractsAsync()
    {
        await using var provider = CreateSyncServices().BuildServiceProvider();
        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();
        var control = provider.GetRequiredService<AotControl>();

        var undeclared = await CaptureExceptionAsync(() => eventBus.PublishAsync(new AotUndeclaredEvent()));
        Check(undeclared is UnknownLocalEventException,
            $"undeclared events must fail deterministically, observed {undeclared?.GetType().Name}");

        var codec = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<AotProbeEvent>)
            LocalEventJsonSerializerContext.Default.GetTypeInfo(typeof(AotProbeEvent))!;
        var probePayload = JsonSerializer.SerializeToUtf8Bytes(new AotProbeEvent { Message = "seeded" }, codec);

        var unknownKey = SeedMessage(store, "unknown-key", "MissingAssembly:Missing.Event", probePayload);
        var unknownResult = await manager.RetryAsync(unknownKey.MessageId);
        Check(!unknownResult.Success && unknownResult.ErrorMessage!.Contains("no registered local event", StringComparison.Ordinal),
            "unknown replay identity must be refused and preserved");
        Check((await store.GetByIdAsync(unknownKey.MessageId, CancellationToken.None))!.Status == DeadLetterStatus.Pending,
            "precheck refusals must not move the message out of Pending");

        var unknownVersion = SeedMessage(
            store, "unknown-version", LocalEventIdentity.BuildLocalReplayKey(typeof(AotProbeEvent)), probePayload, version: 9);
        var versionResult = await manager.RetryAsync(unknownVersion.MessageId);
        Check(!versionResult.Success, "unknown replay version must be refused");

        var corruption = SeedMessage(
            store, "corrupt", LocalEventIdentity.BuildLocalReplayKey(typeof(AotProbeEvent)), "not-json"u8.ToArray());
        var corruptResult = await manager.RetryAsync(corruption.MessageId);
        var corrupted = await store.GetByIdAsync(corruption.MessageId, CancellationToken.None);
        // #146 R01: decode happens before MarkRetrying — a corrupt payload is refused without
        // consuming the retry budget and never archives.
        Check(!corruptResult.Success && corrupted!.RetryCount == 0 && corrupted.Status == DeadLetterStatus.Pending,
            "corrupt payloads must be refused before any state transition and must not consume the retry budget");

        var orphanPayload = JsonSerializer.SerializeToUtf8Bytes(
            new AotOrphanEvent(),
            (System.Text.Json.Serialization.Metadata.JsonTypeInfo<AotOrphanEvent>)
                LocalEventJsonSerializerContext.Default.GetTypeInfo(typeof(AotOrphanEvent))!);
        var orphan = SeedMessage(store, "orphan", LocalEventIdentity.BuildLocalReplayKey(typeof(AotOrphanEvent)), orphanPayload);
        var orphanResult = await manager.RetryAsync(orphan.MessageId);
        Check(!orphanResult.Success && orphanResult.ErrorMessage!.Contains("no handlers", StringComparison.Ordinal),
            "replay without any handler must be refused before any state transition");
        Check((await store.GetByIdAsync(orphan.MessageId, CancellationToken.None))!.Status == DeadLetterStatus.Pending,
            "refused orphan replay must not be marked successful or left Retrying");

        Check(control.ProbeCalls == 0, "refused replays must not execute handlers");

        var codecMissingServices = new ServiceCollection();
        codecMissingServices.AddLogging();
        codecMissingServices.AddCrestLocalEventDispatch(builder => builder.Declare<AotNoCodecEvent>(replayable: true));
        await using (var codecMissingProvider = codecMissingServices.BuildServiceProvider())
        {
            var freezeFailure = CaptureException(
                () => codecMissingProvider.GetRequiredService<ILocalEventDispatchRegistry>());
            Check(freezeFailure is LocalEventDispatchConfigurationException configuration &&
                  configuration.Message.Contains("JSON", StringComparison.Ordinal),
                "a replayable declaration without a registered codec must be rejected at freeze");
        }
    }

    private static async Task ChannelFullChainAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<AotControl>();
        builder.Services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        builder.Services.Configure<LocalDeadLetterOptions>(options => options.RetryIntervalSeconds = 30);
        builder.Services.AddCrestLocalChannelEventDispatch();
        builder.Services.AddLocalEvents();
        LocalEventJsonCodecContributions.AddCodecs(builder.Services);

        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            var control = host.Services.GetRequiredService<AotControl>();
            var eventBus = host.Services.GetRequiredService<ILocalEventBus>();
            var store = host.Services.GetRequiredService<IDeadLetterStore>();
            var manager = host.Services.GetRequiredService<ILocalDeadLetterManager>();

            await eventBus.PublishAsync(new AotChannelEvent { Message = "channel" });

            var deadline = DateTime.UtcNow.AddSeconds(10);
            DeadLetterMessage? pending = null;
            while (DateTime.UtcNow < deadline)
            {
                pending = (await store.GetPendingAsync(0, 10, CancellationToken.None)).FirstOrDefault();
                if (pending is not null)
                {
                    break;
                }

                await Task.Delay(50);
            }

            Check(pending is not null, "channel handler failure must produce a real dead letter record");
            Check(pending!.LocalReplayKey == LocalEventIdentity.BuildLocalReplayKey(typeof(AotChannelEvent)),
                "channel records must carry the typed replay identity");

            control.FailChannelEvent = false;
            var result = await manager.RetryAsync(pending.MessageId);
            Check(result.Success, $"channel dead letter manual replay must succeed: {result.ErrorMessage}");
            Check(control.ChannelCalls == 1 && control.LastChannelMessage == "channel",
                "channel replay must execute the handler with the decoded payload");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static DeadLetterMessage SeedMessage(
        IDeadLetterStore store,
        string messageId,
        string replayKey,
        byte[] payload,
        int version = 1)
    {
        var message = new DeadLetterMessage(
            messageId,
            typeof(AotProbeEvent).FullName!,
            version,
            null,
            null,
            null,
            EventScope.Local,
            typeof(AotProbeEvent).AssemblyQualifiedName!,
            payload,
            "seeded failure",
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            0,
            3,
            DeadLetterStatus.Pending,
            LocalReplayKey: replayKey,
            LocalContextVersion: LocalEventExecutionContextSnapshot.CurrentContextVersion);
        store.EnqueueAsync(message, CancellationToken.None).GetAwaiter().GetResult();
        return message;
    }

    private static async Task<Exception?> CaptureExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
