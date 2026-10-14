using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Explicit per-event execution check. Checks are registered scoped services and activated per
/// event; a declared check that has no DI registration fails at freeze. Checks observe the frozen
/// binding, the payload, the execution source and the context snapshot, and may only reject — they
/// must never execute business handlers or mutate registries.
/// </summary>
public interface ILocalEventExecutionCheck
{
    Task<LocalEventExecutionCheckResult> CheckAsync(
        LocalEventExecutionCheckContext context,
        CancellationToken cancellationToken = default);
}

public sealed record LocalEventExecutionCheckContext(
    LocalEventBinding Binding,
    ILocalEvent Payload,
    LocalEventExecutionSource Source,
    LocalEventExecutionContextSnapshot ContextSnapshot,
    string? EventDescriptorId);

/// <summary>Structured check outcome: pass, or a deterministic rejection with a stable code.</summary>
public sealed record LocalEventExecutionCheckResult(bool Allowed, string? ProblemCode, string? Problem)
{
    public static LocalEventExecutionCheckResult Pass { get; } = new(true, null, null);

    public static LocalEventExecutionCheckResult Reject(string problemCode, string problem)
        => new(false, problemCode, problem);
}
