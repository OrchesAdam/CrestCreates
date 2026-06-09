using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class InMemoryIdempotenceStore : IIdempotenceStore
{
    private readonly ConcurrentDictionary<string, CapabilityExecutionResult> _results = new();

    public Task<CapabilityExecutionResult?> GetResultAsync(string idempotencyKey, CancellationToken ct = default)
    {
        _results.TryGetValue(idempotencyKey, out var result);
        return Task.FromResult(result);
    }

    public Task StoreResultAsync(string idempotencyKey, CapabilityExecutionResult result, CancellationToken ct = default)
    {
        _results[idempotencyKey] = result;
        return Task.CompletedTask;
    }
}