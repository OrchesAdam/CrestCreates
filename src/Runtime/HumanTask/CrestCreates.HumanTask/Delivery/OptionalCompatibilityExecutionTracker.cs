using Microsoft.Extensions.Logging;

namespace CrestCreates.HumanTask;

/// <summary>Retains timed-out optional executions until they finish.</summary>
internal sealed class OptionalCompatibilityExecutionTracker
{
    private readonly object _gate = new();
    private readonly int _maximum;
    private int _reserved;
    private readonly ILogger<OptionalCompatibilityExecutionTracker>? _logger;

    public OptionalCompatibilityExecutionTracker(HumanTaskDeliveryOptions options, ILogger<OptionalCompatibilityExecutionTracker>? logger = null)
    {
        options.Validate();
        _maximum = options.MaximumDetachedOptionalExecutions;
        _logger = logger;
    }

    public bool TryReserve()
    {
        lock (_gate)
        {
            if (_reserved >= _maximum)
                return false;
            _reserved++;
        }
        return true;
    }

    public void TrackReserved(Task execution, IDisposable scope, IDisposable cancellation)
        => _ = ObserveAsync(execution, scope, cancellation);

    public void ReleaseReservation()
    {
        lock (_gate)
        {
            if (_reserved > 0)
                _reserved--;
        }
    }

    private async Task ObserveAsync(Task execution, IDisposable scope, IDisposable cancellation)
    {
        try { await execution.ConfigureAwait(false); }
        catch (Exception exception) { _logger?.LogWarning(exception, "Detached optional HumanTask compatibility execution failed after delivery acknowledgement."); }
        finally
        {
            cancellation.Dispose();
            scope.Dispose();
            ReleaseReservation();
        }
    }
}
