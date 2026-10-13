using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Shared replay execution core: load → precheck (status/budget/identity/codec/handlers) →
/// MarkRetrying → fresh-scope decode and dispatch → success/failure state update. Both the
/// manual manager and the background retry service delegate here; replay re-enters the unified
/// dispatcher directly and never re-publishes (which could produce a second dead letter).
/// </summary>
internal sealed class LocalDeadLetterReplayExecutor : ILocalDeadLetterReplayExecutor
{
    private static readonly TimeSpan StateWriteTimeout = TimeSpan.FromSeconds(5);

    private readonly ILocalEventDispatchRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocalDeadLetterReplayExecutor>? _logger;

    public LocalDeadLetterReplayExecutor(
        ILocalEventDispatchRegistry registry,
        IServiceScopeFactory scopeFactory,
        ILogger<LocalDeadLetterReplayExecutor>? logger = null)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<DeadLetterRetryResult> ExecuteAsync(
        DeadLetterMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Pre-execution cancellation leaves the message state untouched.
        cancellationToken.ThrowIfCancellationRequested();

        if (message.MaxRetries < 1)
        {
            return Reject(message,
                $"invalid persisted MaxRetries ({message.MaxRetries}); ordinary retry refused without state change.");
        }

        if (message.RetryCount < 0 || message.RetryCount > message.MaxRetries)
        {
            return Reject(message,
                $"invalid persisted RetryCount ({message.RetryCount}/{message.MaxRetries}); " +
                "ordinary retry refused without state change.");
        }

        if (message.Status != DeadLetterStatus.Pending)
        {
            return Reject(message, $"status {message.Status} does not accept ordinary retry.");
        }

        if (message.RetryCount >= message.MaxRetries)
        {
            var write = await TryWriteStateAsync(
                message with { Status = DeadLetterStatus.Archived },
                "archive budget-exhausted pending message");
            _logger?.LogWarning(
                "Dead letter message {MessageId} reached its persisted retry budget ({RetryCount}/{MaxRetries}); archived without executing handlers. State write succeeded: {Written}.",
                message.MessageId, message.RetryCount, message.MaxRetries, write.Ok);
            return new DeadLetterRetryResult(
                message.MessageId,
                false,
                write.Ok
                    ? "retry budget exhausted; message archived without executing handlers."
                    : $"retry budget exhausted; archiving failed: {write.Problem}");
        }

        if (!TryResolveBinding(message, out var binding, out var resolveProblem))
        {
            return Reject(message, resolveProblem!);
        }

        if (!binding!.Replayable)
        {
            return Reject(message, $"event '{binding.LocalName}' is dispatch-only and cannot be replayed.");
        }

        var typeInfo = binding.CodecFactory?.Invoke();
        if (typeInfo is null)
        {
            return Reject(message, $"event '{binding.LocalName}' resolved no JSON codec at replay time.");
        }

        if (typeInfo.Type != binding.EventType)
        {
            return Reject(message,
                $"codec for event '{binding.LocalName}' describes '{typeInfo.Type.FullName}'; identity mismatch.");
        }

        var handlerProblem = await ProbeHandlersAsync(binding, cancellationToken).ConfigureAwait(false);
        if (handlerProblem is not null)
        {
            return Reject(message, handlerProblem);
        }

        await using (var stateScope = _scopeFactory.CreateAsyncScope())
        {
            var store = stateScope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
            await store.MarkRetryingAsync(message.MessageId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var decoded = JsonSerializer.Deserialize(message.Payload, typeInfo)
                    ?? throw new LocalEventPayloadDecodeException(
                        $"payload of message {message.MessageId} deserialized to null");

                if (!binding.EventType.IsInstanceOfType(decoded))
                {
                    throw new LocalEventPayloadDecodeException(
                        $"payload of message {message.MessageId} deserialized to '{decoded.GetType().FullName}' " +
                        $"instead of '{binding.EventType.FullName}'");
                }

                var dispatcher = scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>();
                await dispatcher.DispatchAsync((ILocalEvent)decoded, cancellationToken).ConfigureAwait(false);
            }

            var successWrite = await TryWriteStateAsync(
                message with { Status = DeadLetterStatus.Retried },
                "record successful replay");

            if (!successWrite.Ok)
            {
                _logger?.LogError(
                    "Replay of dead letter message {MessageId} dispatched successfully but the Retried state write failed: {Problem}. The message remains Retrying; success must not be claimed.",
                    message.MessageId, successWrite.Problem);
                return new DeadLetterRetryResult(
                    message.MessageId,
                    false,
                    $"handlers executed but the Retried state write failed: {successWrite.Problem}");
            }

            _logger?.LogInformation("Replayed dead letter message {MessageId} successfully.", message.MessageId);
            return new DeadLetterRetryResult(message.MessageId, true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var restored = await TryWriteStateAsync(
                message with { Status = DeadLetterStatus.Pending },
                "restore Pending after cooperative cancellation");
            if (!restored.Ok)
            {
                _logger?.LogError(
                    "Replay of dead letter message {MessageId} was cancelled and restoring Pending failed: {Problem}. The message state write failure is reported and not claimed as recovered.",
                    message.MessageId, restored.Problem);
            }

            throw;
        }
        catch (Exception failure)
        {
            var newRetryCount = message.RetryCount + 1;
            var updated = message with
            {
                RetryCount = newRetryCount,
                Status = newRetryCount >= message.MaxRetries ? DeadLetterStatus.Archived : DeadLetterStatus.Pending,
                ErrorMessage = failure.Message,
                ExceptionType = failure.GetType().FullName,
            };

            var write = await TryWriteStateAsync(updated, "record failed replay attempt");
            _logger?.LogError(
                failure,
                "Replay attempt {RetryCount}/{MaxRetries} failed for dead letter message {MessageId}; state write succeeded: {Written}.",
                newRetryCount, message.MaxRetries, message.MessageId, write.Ok);

            return new DeadLetterRetryResult(
                message.MessageId,
                false,
                write.Ok ? failure.Message : $"{failure.Message} (state write failed: {write.Problem})");
        }
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
                $"no registered local event matches replay key '{message.LocalReplayKey}:v{message.EventVersion}'; " +
                "the message is preserved and not treated as failed.";
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

    private async Task<string?> ProbeHandlersAsync(LocalEventBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var scope = _scopeFactory.CreateAsyncScope();
        IReadOnlyList<object> instances = binding.Invoker.ProbeHandlerInstances(scope.ServiceProvider);

        if (instances.Count == 0)
        {
            return $"event '{binding.LocalName}' resolved no handlers in a fresh scope; " +
                   "a dead-letter replay requires at least one matching handler and must not be marked successful.";
        }

        foreach (var expectedHandlerType in binding.ExpectedHandlerTypes)
        {
            var satisfied = false;
            foreach (var instance in instances)
            {
                if (expectedHandlerType.IsInstanceOfType(instance))
                {
                    satisfied = true;
                    break;
                }
            }

            if (!satisfied)
            {
                return $"event '{binding.LocalName}' declares required handler '{expectedHandlerType.FullName}' " +
                       "but no instance assignable to it is registered; replay refused before any state transition.";
            }
        }

        return null;
    }

    private async Task<(bool Ok, string? Problem)> TryWriteStateAsync(DeadLetterMessage updated, string description)
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

    private static DeadLetterRetryResult Reject(DeadLetterMessage message, string problem)
        => new(message.MessageId, false, problem);
}

/// <summary>Deterministic payload decode failure (corrupt JSON, null or mismatched type).</summary>
internal sealed class LocalEventPayloadDecodeException : InvalidOperationException
{
    public LocalEventPayloadDecodeException(string message)
        : base(message)
    {
    }
}
