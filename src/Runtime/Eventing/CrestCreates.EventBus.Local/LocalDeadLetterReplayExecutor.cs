using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Shared replay orchestration: preflight (state/identity/budget/decode) → execution phase (tenant
/// activation, admission, preparation, MarkRetrying, handler execution, release) → independent
/// settlement. The phases are internal collaborators; this class only sequences them, so the
/// ambient context activation and restore stay inside the execution phase's async frame.
/// </summary>
internal sealed class LocalDeadLetterReplayExecutor : ILocalDeadLetterReplayExecutor
{
    private readonly LocalDeadLetterReplayPreflight _preflight;
    private readonly LocalDeadLetterReplayExecution _execution;
    private readonly LocalDeadLetterReplaySettlement _settlement;

    public LocalDeadLetterReplayExecutor(
        ILocalEventDispatchRegistry registry,
        IServiceScopeFactory scopeFactory,
        ILogger<LocalDeadLetterReplayExecutor>? logger = null)
    {
        var stateWriter = new LocalDeadLetterStateWriter(scopeFactory, logger);
        _preflight = new LocalDeadLetterReplayPreflight(registry, scopeFactory, stateWriter, logger);
        _execution = new LocalDeadLetterReplayExecution(scopeFactory, logger);
        _settlement = new LocalDeadLetterReplaySettlement(stateWriter, logger);
    }

    public async Task<DeadLetterRetryResult> ExecuteAsync(
        DeadLetterMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Pre-execution cancellation leaves the message state untouched.
        cancellationToken.ThrowIfCancellationRequested();

        var preflight = await _preflight.RunAsync(message, cancellationToken).ConfigureAwait(false);
        if (preflight.Refusal is not null)
        {
            return preflight.Refusal;
        }

        return await _execution
            .RunAttemptAsync(
                preflight.Message, preflight.Binding!, preflight.Payload!, _settlement, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Deterministic payload decode failure (corrupt JSON, null or mismatched type).</summary>
internal sealed class LocalEventPayloadDecodeException : InvalidOperationException
{
    public LocalEventPayloadDecodeException(string message)
        : base(message)
    {
    }
}
