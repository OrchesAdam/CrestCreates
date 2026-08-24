using Microsoft.Extensions.Logging;

namespace CrestCreates.HumanTask;

/// <summary>Retains timed-out optional executions until they finish.</summary>
internal sealed class OptionalCompatibilityExecutionTracker
{
    private readonly object _gate = new();
    private readonly int _maximum;
    private readonly HashSet<Task> _running = [];
    private readonly ILogger<OptionalCompatibilityExecutionTracker>? _logger;

    public OptionalCompatibilityExecutionTracker(HumanTaskDeliveryOptions options, ILogger<OptionalCompatibilityExecutionTracker>? logger = null)
    {
        options.Validate();
        _maximum = options.MaximumDetachedOptionalExecutions;
        _logger = logger;
    }

    public bool TryTrack(Task execution, IDisposable scope, IDisposable cancellation)
    {
        lock (_gate)
        {
            if (_running.Count >= _maximum)
                return false;
            _running.Add(execution);
        }
        _ = ObserveAsync(execution, scope, cancellation);
        return true;
    }

    private async Task ObserveAsync(Task execution, IDisposable scope, IDisposable cancellation)
    {
        try { await execution.ConfigureAwait(false); }
        catch (Exception exception) { _logger?.LogWarning(exception, "Detached optional HumanTask compatibility execution failed after delivery acknowledgement."); }
        finally
        {
            cancellation.Dispose();
            scope.Dispose();
            lock (_gate) _running.Remove(execution);
        }
    }
}
