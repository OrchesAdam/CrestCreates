using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Shared execution core for every entry point (sync publish, direct dispatcher, channel consumer
/// and replay) AFTER admission passed: prepare resolves the whole handler batch exactly once inside
/// the caller's execution scope and the same prepared instances execute. Zero handlers complete as
/// a no-op; missing required handlers fail during prepare before any handler runs.
/// </summary>
internal static class LocalEventExecution
{
    public static ValueTask ExecuteAsync(
        IServiceProvider provider,
        LocalEventBinding binding,
        ILocalEvent payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(payload);

        var prepared = binding.Invoker.Prepare(provider, payload, binding.ExpectedHandlerTypes);
        return prepared.ExecuteAsync(cancellationToken);
    }
}
