using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Concurrent volatile budget ledger intended only for development and tests.
/// Reservations and capacity are lost on restart and are not coordinated across
/// nodes; production Hosts must supply a durable budget gate.
/// </summary>
public sealed class DevelopmentInMemoryAgentToolBudgetGate : IAgentToolBudgetGate
{
    private readonly object _sync = new();
    private readonly Dictionary<AttemptKey, BudgetEntry> _byAttempt = [];
    private readonly Dictionary<string, BudgetEntry> _byReservationId
        = new(StringComparer.Ordinal);

    public ValueTask<AgentToolBudgetReserveResult> ReserveAsync(
        AgentToolBudgetReserveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var context = request.Context;
        if (!AgentToolGovernanceGuard.IsValid(context))
        {
            return ValueTask.FromResult(Denied("budget_context_invalid"));
        }

        var requirement = context.Governance.Budget;
        if (requirement is null
            || string.IsNullOrWhiteSpace(requirement.Category)
            || requirement.CostUnits <= 0
            || requirement.MaxCallsPerExecution is <= 0)
        {
            return ValueTask.FromResult(Denied("budget_requirement_invalid"));
        }

        lock (_sync)
        {
            var attemptKey = new AttemptKey(
                context.LogicalInvocationKey,
                context.AttemptId);
            if (_byAttempt.TryGetValue(attemptKey, out var existingAttempt))
            {
                if (!Matches(existingAttempt, context, requirement))
                {
                    return ValueTask.FromResult(Denied("budget_attempt_conflict"));
                }

                return ValueTask.FromResult(
                    existingAttempt.Reservation.State == AgentToolBudgetReservationState.Reserved
                    ? Reserved(existingAttempt.Reservation)
                    : Denied("budget_attempt_already_finalized"));
            }

            var logicalReservations = _byAttempt
                .Where(pair => pair.Key.LogicalInvocationKey == context.LogicalInvocationKey)
                .Select(pair => pair.Value)
                .ToArray();

            if (logicalReservations.Any(entry =>
                    entry.ToolContract != context.ToolContract
                    ||
                    !string.Equals(
                        entry.Reservation.InvocationFingerprint,
                        context.InvocationFingerprint,
                        StringComparison.Ordinal)))
            {
                return ValueTask.FromResult(Denied("budget_logical_invocation_conflict"));
            }

            if (logicalReservations.Any(entry =>
                    entry.Reservation.State == AgentToolBudgetReservationState.Committed))
            {
                return ValueTask.FromResult(Denied("budget_logical_invocation_committed"));
            }

            if (logicalReservations.Any(entry =>
                    entry.Reservation.State == AgentToolBudgetReservationState.Indeterminate))
            {
                return ValueTask.FromResult(Denied("budget_logical_invocation_indeterminate"));
            }

            if (requirement.MaxCallsPerExecution is { } maxCalls)
            {
                var capacityKey = CapacityKey.Create(context);
                var occupied = _byReservationId.Values.Count(entry =>
                    entry.CapacityKey == capacityKey
                    && entry.Reservation.State is AgentToolBudgetReservationState.Reserved
                        or AgentToolBudgetReservationState.Committed
                        or AgentToolBudgetReservationState.Indeterminate);
                if (occupied >= maxCalls)
                {
                    return ValueTask.FromResult(Denied("budget_capacity_exceeded"));
                }
            }

            var reservation = new AgentToolBudgetReservation
            {
                ReservationId = $"budget-{Guid.NewGuid():N}",
                AttemptId = context.AttemptId,
                InvocationFingerprint = context.InvocationFingerprint,
                Category = requirement.Category,
                CostUnits = requirement.CostUnits,
                MaxCallsPerExecution = requirement.MaxCallsPerExecution,
                State = AgentToolBudgetReservationState.Reserved
            };
            var entry = new BudgetEntry(
                attemptKey,
                CapacityKey.Create(context),
                context.ToolContract,
                reservation);
            _byAttempt.Add(attemptKey, entry);
            _byReservationId.Add(reservation.ReservationId, entry);
            return ValueTask.FromResult(Reserved(reservation));
        }
    }

    public ValueTask<AgentToolBudgetReservation> FinalizeAsync(
        AgentToolBudgetFinalizeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.ReservationId)
            || string.IsNullOrWhiteSpace(request.AttemptId)
            || string.IsNullOrWhiteSpace(request.InvocationFingerprint)
            || string.IsNullOrWhiteSpace(request.ReasonCode)
            || request.RequestedState is not (
                AgentToolBudgetReservationState.Released
                or AgentToolBudgetReservationState.Committed
                or AgentToolBudgetReservationState.Indeterminate))
        {
            throw new ArgumentException("Budget finalization has an invalid contract.", nameof(request));
        }

        lock (_sync)
        {
            if (!_byReservationId.TryGetValue(request.ReservationId, out var entry))
            {
                throw new InvalidOperationException("The budget reservation does not exist.");
            }

            var reservation = entry.Reservation;
            if (!string.Equals(reservation.AttemptId, request.AttemptId, StringComparison.Ordinal)
                || !string.Equals(
                    reservation.InvocationFingerprint,
                    request.InvocationFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The budget reservation identity does not match.");
            }

            if (reservation.State != AgentToolBudgetReservationState.Reserved)
            {
                if (reservation.State == request.RequestedState)
                {
                    return ValueTask.FromResult(reservation);
                }

                throw new InvalidOperationException(
                    "A terminal budget reservation cannot change state.");
            }

            var finalized = reservation with { State = request.RequestedState };
            var finalizedEntry = entry with { Reservation = finalized };
            _byReservationId[reservation.ReservationId] = finalizedEntry;
            _byAttempt[entry.AttemptKey] = finalizedEntry;
            return ValueTask.FromResult(finalized);
        }
    }

    public ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var attemptKey = new AttemptKey(
                identity.LogicalInvocationKey,
                identity.AttemptId);
            if (!_byAttempt.TryGetValue(attemptKey, out var entry))
            {
                return ValueTask.FromResult(new AgentToolBudgetReservationReadResult
                {
                    Status = AgentToolBudgetReadStatus.Missing
                });
            }

            var status = entry.Reservation.State switch
            {
                AgentToolBudgetReservationState.Reserved => AgentToolBudgetReadStatus.Reserved,
                AgentToolBudgetReservationState.Released => AgentToolBudgetReadStatus.Released,
                AgentToolBudgetReservationState.Committed => AgentToolBudgetReadStatus.Committed,
                AgentToolBudgetReservationState.Indeterminate => AgentToolBudgetReadStatus.Indeterminate,
                _ => AgentToolBudgetReadStatus.Unknown
            };
            return ValueTask.FromResult(new AgentToolBudgetReservationReadResult
            {
                Status = status,
                Reservation = entry.Reservation
            });
        }
    }

    private static bool Matches(
        BudgetEntry entry,
        AgentToolGovernanceContext context,
        AgentToolBudgetRequirement requirement)
        => entry.ToolContract == context.ToolContract
            && entry.CapacityKey == CapacityKey.Create(context)
            && string.Equals(
                entry.Reservation.AttemptId,
                context.AttemptId,
                StringComparison.Ordinal)
            && string.Equals(
                entry.Reservation.InvocationFingerprint,
                context.InvocationFingerprint,
                StringComparison.Ordinal)
            && string.Equals(
                entry.Reservation.Category,
                requirement.Category,
                StringComparison.Ordinal)
            && entry.Reservation.CostUnits == requirement.CostUnits
            && entry.Reservation.MaxCallsPerExecution == requirement.MaxCallsPerExecution;

    private static AgentToolBudgetReserveResult Reserved(
        AgentToolBudgetReservation reservation)
        => new()
        {
            Status = AgentToolBudgetReserveStatus.Reserved,
            Reservation = reservation
        };

    private static AgentToolBudgetReserveResult Denied(string reasonCode)
        => new()
        {
            Status = AgentToolBudgetReserveStatus.Denied,
            ReasonCode = reasonCode
        };

    private readonly record struct AttemptKey(
        AgentToolLogicalInvocationKey LogicalInvocationKey,
        string AttemptId);

    private readonly record struct CapacityKey(
        string? TenantId,
        string UserId,
        string AgentId,
        string ExecutionId,
        AgentToolContractIdentity ToolContract,
        string Category)
    {
        public static CapacityKey Create(AgentToolGovernanceContext context)
            => new(
                context.LogicalInvocationKey.TenantId,
                context.LogicalInvocationKey.UserId,
                context.LogicalInvocationKey.AgentId,
                context.LogicalInvocationKey.ExecutionId,
                context.ToolContract,
                context.Governance.Budget.Category);
    }

    private sealed record BudgetEntry(
        AttemptKey AttemptKey,
        CapacityKey CapacityKey,
        AgentToolContractIdentity ToolContract,
        AgentToolBudgetReservation Reservation);
}
