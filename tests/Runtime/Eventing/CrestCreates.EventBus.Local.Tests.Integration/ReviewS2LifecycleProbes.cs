using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

internal sealed class ReviewDisposeState
{
    public int Calls;
    public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ReviewThrowingRelease(ReviewDisposeState state) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref state.Calls);
        state.Disposed.TrySetResult();
        throw new InvalidOperationException("review-dispose-failure");
    }
}

internal sealed class ReviewCancelHandler(ReviewThrowingRelease release, CancellationTokenSource source)
    : ILocalEventHandler<ReplayableDispatchEvent>
{
    public Task HandleAsync(ReplayableDispatchEvent payload, CancellationToken ct = default)
    {
        _ = release;
        source.Cancel();
        throw new OperationCanceledException(ct);
    }
}

internal sealed class ReviewSuccessfulChannelHandler(ReviewThrowingRelease release)
    : ILocalEventHandler<ReplayableDispatchEvent>
{
    public static int Calls;
    public Task HandleAsync(ReplayableDispatchEvent payload, CancellationToken ct = default)
    {
        _ = release;
        Interlocked.Increment(ref Calls);
        return Task.CompletedTask;
    }
}

internal sealed class ReviewLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Sink(this);
    public void Dispose() { }
    private sealed class Sink(ReviewLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
            => owner.Entries.Enqueue(format(state, error) + " " + error);
    }
}

public sealed class ReviewS2LifecycleProbes
{
    [Fact]
    public void Readonly_options_must_not_leave_frozen_codec_metadata_mutable()
    {
        var options = LocalEventTestComposition.CodecFor<ReplayableDispatchEvent>().Options;
        Assert.True(options.IsReadOnly);
        var metadata = JsonTypeInfo.CreateJsonTypeInfo<ReplayableDispatchEvent>(options);
        metadata.CreateObject = static () => new ReplayableDispatchEvent();
        var property = metadata.CreateJsonPropertyInfo(typeof(string), "Message");
        property.Get = value => ((ReplayableDispatchEvent)value).Message;
        property.Set = (value, message) => ((ReplayableDispatchEvent)value).Message = (string)message!;
        metadata.Properties.Add(property);
        Assert.False(metadata.IsReadOnly);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrestLocalEventDispatch(b => b.Declare<ReplayableDispatchEvent>(replayable: true, codecFactory: () => metadata));
        using var provider = services.BuildServiceProvider();
        ILocalEventDispatchRegistry registry;
        try { registry = provider.GetRequiredService<ILocalEventDispatchRegistry>(); }
        catch (LocalEventDispatchConfigurationException) { return; } // Rejecting unsafe metadata is valid.
        Assert.True(registry.TryGet(typeof(ReplayableDispatchEvent), out var binding));
        try { property.Name = "ChangedAfterFreeze"; } catch (InvalidOperationException) { }
        var json = JsonSerializer.Serialize(new ReplayableDispatchEvent { Message = "original" }, binding.Codec!);
        Assert.Contains("\"Message\"", json);
    }

    [Fact]
    public async Task Cooperative_cancellation_must_not_silently_drop_release_failure()
    {
        using var cancel = new CancellationTokenSource();
        var logs = new ReviewLogs();
        var state = new ReviewDisposeState();
        await using var provider = LocalEventTestComposition.Build(
            b => b.Declare<ReplayableDispatchEvent>(replayable: true),
            services =>
            {
                services.AddLogging(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
                services.AddSingleton(cancel);
                services.AddSingleton(state);
                services.AddScoped<ReviewThrowingRelease>();
                services.AddScoped<ILocalEventHandler<ReplayableDispatchEvent>, ReviewCancelHandler>();
            });
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<ReplayableDispatchEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableDispatchEvent)));
        await store.EnqueueAsync(record, CancellationToken.None);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<ILocalDeadLetterReplayExecutor>().ExecuteAsync(record, cancel.Token));
        Assert.Equal(1, state.Calls);
        Assert.Equal(DeadLetterStatus.Pending, (await store.GetByIdAsync(record.MessageId, CancellationToken.None))!.Status);
        var observable = logs.Entries.Any(e => e.Contains("review-dispose-failure", StringComparison.Ordinal))
            || error.Data.Values.Cast<object>().Any(v => v.ToString()!.Contains("review-dispose-failure", StringComparison.Ordinal))
            || error.InnerException?.ToString().Contains("review-dispose-failure", StringComparison.Ordinal) == true;
        Assert.True(observable, "release failure must remain observable alongside caller cancellation");
    }

    [Fact]
    public async Task Channel_success_must_not_become_replayable_failure_because_scope_release_throws()
    {
        var state = new ReviewDisposeState();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddScoped<ReviewThrowingRelease>();
        services.AddScoped<ILocalEventHandler<ReplayableDispatchEvent>, ReviewSuccessfulChannelHandler>();
        services.AddCrestLocalChannelEventDispatch(b => b.Declare<ReplayableDispatchEvent>(
            replayable: true, codecFactory: () => LocalEventTestComposition.CodecFor<ReplayableDispatchEvent>()));
        await using var provider = services.BuildServiceProvider();
        var consumer = provider.GetServices<IHostedService>().OfType<BackgroundChannelLocalEventBusConsumer>().Single();
        Interlocked.Exchange(ref ReviewSuccessfulChannelHandler.Calls, 0);
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new ReplayableDispatchEvent());
            await state.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var store = provider.GetRequiredService<IDeadLetterStore>();
            var until = DateTime.UtcNow.AddSeconds(2);
            while (await store.CountAsync(null, CancellationToken.None) == 0 && DateTime.UtcNow < until)
                await Task.Delay(25);
            Assert.Equal(1, ReviewSuccessfulChannelHandler.Calls);
            Assert.Equal(0, await store.CountAsync(null, CancellationToken.None));
        }
        finally { await consumer.StopAsync(CancellationToken.None); }
    }
}
