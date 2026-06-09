namespace CrestCreates.Capability.Abstractions;

public interface IIdempotenceStore
{
    Task<CapabilityExecutionResult?> GetResultAsync(string idempotencyKey, CancellationToken ct = default);
    Task StoreResultAsync(string idempotencyKey, CapabilityExecutionResult result, CancellationToken ct = default);
}