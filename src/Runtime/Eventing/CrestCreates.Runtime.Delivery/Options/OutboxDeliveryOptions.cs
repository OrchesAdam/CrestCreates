using CrestCreates.Runtime.Delivery.Abstractions.Contracts;

namespace CrestCreates.Runtime.Delivery.Options;

public sealed class OutboxDeliveryOptions
{
    public int BatchSize { get; set; } = 32;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public int MaximumHandlerAttempts { get; set; } = 8;
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
    public void Validate()
    {
        if (BatchSize is < OutboxContractLimits.MinBatchSize or > OutboxContractLimits.MaxBatchSize) throw new ArgumentOutOfRangeException(nameof(BatchSize));
        if (PollingInterval < TimeSpan.FromMilliseconds(10) || PollingInterval > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(PollingInterval));
        if (LeaseDuration < TimeSpan.FromSeconds(OutboxContractLimits.MinLeaseSeconds) || LeaseDuration > TimeSpan.FromMinutes(OutboxContractLimits.MaxLeaseMinutes)) throw new ArgumentOutOfRangeException(nameof(LeaseDuration));
        if (HandlerTimeout < TimeSpan.FromMilliseconds(OutboxContractLimits.MinHandlerTimeoutMilliseconds) || HandlerTimeout >= LeaseDuration) throw new ArgumentOutOfRangeException(nameof(HandlerTimeout));
        if (MaximumHandlerAttempts is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(MaximumHandlerAttempts));
        if (BaseRetryDelay < TimeSpan.FromMilliseconds(OutboxContractLimits.MinRetryDelayMilliseconds) || BaseRetryDelay > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(BaseRetryDelay));
        if (MaximumRetryDelay < BaseRetryDelay || MaximumRetryDelay > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(MaximumRetryDelay));
    }
    public TimeSpan GetRetryDelay(int attempt)
    {
        Validate();
        var multiplier = Math.Pow(2, Math.Max(0, attempt - 1));
        var milliseconds = Math.Min(MaximumRetryDelay.TotalMilliseconds, BaseRetryDelay.TotalMilliseconds * multiplier);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
