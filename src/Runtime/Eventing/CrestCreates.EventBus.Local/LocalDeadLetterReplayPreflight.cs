using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Replay preflight: re-read the persisted message in its own scope, validate status/budget/identity,
/// resolve the frozen binding and decode the payload. Every refusal leaves the persisted state and
/// budget untouched; only a budget-exhausted Pending message is archived here.
/// </summary>
internal sealed class LocalDeadLetterReplayPreflight
{
    private readonly ILocalEventDispatchRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LocalDeadLetterStateWriter _stateWriter;
    private readonly ILogger<LocalDeadLetterReplayExecutor>? _logger;

    public LocalDeadLetterReplayPreflight(
        ILocalEventDispatchRegistry registry,
        IServiceScopeFactory scopeFactory,
        LocalDeadLetterStateWriter stateWriter,
        ILogger<LocalDeadLetterReplayExecutor>? logger)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _stateWriter = stateWriter;
        _logger = logger;
    }

    public async Task<LocalDeadLetterReplayPreflightResult> RunAsync(
        DeadLetterMessage message,
        CancellationToken cancellationToken)
    {
        // Re-read the current persisted state to avoid stale EF tracking cache from the caller's scope.
        await using (var readScope = _scopeFactory.CreateAsyncScope())
        {
            var freshStore = readScope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
            var freshMessage = await freshStore.GetByIdAsync(message.MessageId, cancellationToken);
            if (freshMessage is null)
            {
                return LocalDeadLetterReplayPreflightResult.Refused(Refuse(
                    message, $"message {message.MessageId} no longer exists in the store.", "MessageMissing"));
            }

            message = freshMessage;
        }

        if (message.MaxRetries < 1)
        {
            return LocalDeadLetterReplayPreflightResult.Refused(Refuse(message,
                $"invalid persisted MaxRetries ({message.MaxRetries}); ordinary retry refused without state change.",
                "InvalidMaxRetries"));
        }

        if (message.RetryCount < 0 || message.RetryCount > message.MaxRetries)
        {
            return LocalDeadLetterReplayPreflightResult.Refused(Refuse(message,
                $"invalid persisted RetryCount ({message.RetryCount}/{message.MaxRetries}); " +
                "ordinary retry refused without state change.",
                "InvalidRetryCount"));
        }

        if (message.Status != DeadLetterStatus.Pending)
        {
            return LocalDeadLetterReplayPreflightResult.Refused(Refuse(
                message, $"status {message.Status} does not accept ordinary retry.", "NotPending"));
        }

        if (message.RetryCount >= message.MaxRetries)
        {
            var write = await _stateWriter.TryWriteAsync(
                message with { Status = DeadLetterStatus.Archived },
                "archive budget-exhausted pending message");
            _logger?.LogWarning(
                "Dead letter message {MessageId} reached its persisted retry budget ({RetryCount}/{MaxRetries}); archived without executing handlers. State write succeeded: {Written}.",
                message.MessageId, message.RetryCount, message.MaxRetries, write.Ok);
            return LocalDeadLetterReplayPreflightResult.Refused(new DeadLetterRetryResult(
                message.MessageId,
                false,
                write.Ok
                    ? "retry budget exhausted; message archived without executing handlers."
                    : $"retry budget exhausted; archiving failed: {write.Problem}")
            {
                Execution = LocalEventExecutionOutcome.NotStarted,
                StateWrite = write.Ok ? LocalEventStateWriteOutcome.Confirmed : LocalEventStateWriteOutcome.Unknown,
                ProblemCode = "BudgetExhausted",
            });
        }

        if (!TryResolveBinding(message, out var binding, out var resolveProblem))
        {
            return LocalDeadLetterReplayPreflightResult.Refused(
                Refuse(message, resolveProblem!, "UnknownIdentity"));
        }

        if (!binding!.Replayable)
        {
            return LocalDeadLetterReplayPreflightResult.Refused(Refuse(
                message, $"event '{binding.LocalName}' is dispatch-only and cannot be replayed.", "DispatchOnly"));
        }

        var typeInfo = binding.Codec;
        if (typeInfo is null)
        {
            return LocalDeadLetterReplayPreflightResult.Refused(Refuse(message,
                $"event '{binding.LocalName}' carries no frozen JSON codec; the replay cannot decode.", "MissingCodec"));
        }

        // Decode with the frozen codec BEFORE any state transition: a corrupt payload must leave the
        // original state and budget untouched (MarkRetrying is never called for it).
        object decoded;
        try
        {
            decoded = JsonSerializer.Deserialize(message.Payload, typeInfo)
                ?? throw new LocalEventPayloadDecodeException(
                    $"payload of message {message.MessageId} deserialized to null");
            if (!binding.EventType.IsInstanceOfType(decoded))
            {
                throw new LocalEventPayloadDecodeException(
                    $"payload of message {message.MessageId} deserialized to '{decoded.GetType().FullName}' " +
                    $"instead of '{binding.EventType.FullName}'");
            }
        }
        catch (Exception decodeFailure) when (decodeFailure is JsonException or LocalEventPayloadDecodeException)
        {
            _logger?.LogWarning(
                "Replay of dead letter message {MessageId} refused before any state transition: payload decode failed with {Failure}: {Message}.",
                message.MessageId, decodeFailure.GetType().Name, decodeFailure.Message);
            return LocalDeadLetterReplayPreflightResult.Refused(Refuse(message,
                $"payload decode failed before any state transition: {decodeFailure.Message}", "PayloadDecode"));
        }

        return LocalDeadLetterReplayPreflightResult.Accepted(message, binding, decoded);
    }

    private bool TryResolveBinding(
        DeadLetterMessage message,
        out LocalEventBinding? binding,
        out string? problem)
    {
        if (!string.IsNullOrEmpty(message.LocalReplayKey))
        {
            if (_registry.TryGetByReplayKey(message.LocalReplayKey, message.EventVersion, out var byKey))
            {
                binding = byKey;
                problem = null;
                return true;
            }

            binding = null;
            problem =
                $"no registered local event matches replay key '{message.LocalReplayKey}:v{message.EventVersion}' " +
                "(current key or explicit alias); the message is preserved and not treated as failed.";
            return false;
        }

        if (_registry.TryResolvePayloadIdentity(message.PayloadTypeFullName, message.EventVersion, out var byIdentity))
        {
            binding = byIdentity;
            problem = null;
            return true;
        }

        binding = null;
        problem =
            $"no registered local event or static legacy alias matches payload identity " +
            $"'{LocalEventIdentity.NormalizePayloadTypeName(message.PayloadTypeFullName)}:v{message.EventVersion}'; " +
            "the message is preserved and not treated as failed.";
        return false;
    }

    private static DeadLetterRetryResult Refuse(
        DeadLetterMessage message,
        string problem,
        string problemCode)
        => new(message.MessageId, false, problem)
        {
            Execution = LocalEventExecutionOutcome.NotStarted,
            StateWrite = LocalEventStateWriteOutcome.NotAttempted,
            Release = LocalEventReleaseOutcome.NotOwned,
            ProblemCode = problemCode,
        };
}

/// <summary>
/// Preflight outcome: either a terminal refusal (the message state is untouched, except the
/// budget-exhausted archive) or the fresh message plus its frozen binding and decoded payload.
/// </summary>
internal sealed record LocalDeadLetterReplayPreflightResult(
    DeadLetterRetryResult? Refusal,
    DeadLetterMessage Message,
    LocalEventBinding? Binding,
    object? Payload)
{
    public static LocalDeadLetterReplayPreflightResult Accepted(
        DeadLetterMessage message,
        LocalEventBinding binding,
        object payload)
        => new(null, message, binding, payload);

    public static LocalDeadLetterReplayPreflightResult Refused(DeadLetterRetryResult refusal)
        => new(refusal, null!, null, null);
}
