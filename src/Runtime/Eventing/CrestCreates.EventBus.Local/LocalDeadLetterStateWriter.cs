using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Settlement store writes for replay attempts (archive, restore-Pending, record failure/retry).
/// Each write uses its own scope and a bounded budget so a state write can never hang the attempt.
/// </summary>
internal sealed class LocalDeadLetterStateWriter
{
    private static readonly TimeSpan StateWriteTimeout = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocalDeadLetterReplayExecutor>? _logger;

    public LocalDeadLetterStateWriter(
        IServiceScopeFactory scopeFactory,
        ILogger<LocalDeadLetterReplayExecutor>? logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<(bool Ok, string? Problem)> TryWriteAsync(
        DeadLetterMessage updated,
        string description)
    {
        try
        {
            using var cts = new CancellationTokenSource(StateWriteTimeout);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
            await store.EnqueueAsync(updated, cts.Token).ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception writeFailure)
        {
            _logger?.LogError(
                writeFailure,
                "Failed to {Description} for dead letter message {MessageId}.",
                description, updated.MessageId);
            return (false, writeFailure.Message);
        }
    }
}
