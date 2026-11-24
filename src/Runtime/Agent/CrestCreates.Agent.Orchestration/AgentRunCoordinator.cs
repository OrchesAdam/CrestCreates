using System.Collections.Immutable;

namespace CrestCreates.Agent.Orchestration;

public sealed partial class AgentRunCoordinator(IAgentExecutionStore store, TimeProvider clock)
{
    public async ValueTask<AgentRunOwner> StartAsync(
        AgentExecutionKey key, string actorId, DateTimeOffset deadline, AgentRunAssignment assignment,
        CancellationToken cancellationToken = default)
    {
        ValidateAssignment(assignment);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (assignment.RunId != key.RootRunId || deadline <= clock.GetUtcNow())
        {
            throw new ArgumentException("A root assignment requires its execution ID and a future deadline.");
        }

        var root = CreateRun(assignment, null);
        var execution = new AgentExecutionSnapshot(key, 0, actorId, deadline,
            ImmutableDictionary<Guid, AgentRunSnapshot>.Empty.Add(root.Owner.RunId, root));
        if (!await store.TryCreateAsync(execution, cancellationToken))
        {
            throw new InvalidOperationException("The agent execution already exists.");
        }

        return root.Owner;
    }

    public async ValueTask<AgentRunContext> ReadContextAsync(
        AgentExecutionKey key, AgentRunOwner owner, CancellationToken cancellationToken = default)
    {
        var execution = await FindAsync(key, cancellationToken);
        return CreateContext(execution, RequireRunnable(execution, owner));
    }

    private static AgentRunContext CreateContext(AgentExecutionSnapshot execution, AgentRunSnapshot run)
    {
        var latest = run.Resumptions.IsEmpty ? null : run.Resumptions[^1];
        return new(execution.Key, run.Owner, execution.ActorId, run.Task, execution.Deadline, run.RemainingBudget,
            run.Tools, run.Inputs, latest?.OwnerEpoch == run.Owner.Epoch ? latest.Delivery : null);
    }

    public ValueTask<ImmutableArray<AgentRunOwner>> StartChildrenAsync(
        AgentExecutionKey key, AgentRunOwner parent, ImmutableArray<AgentRunAssignment> assignments,
        CancellationToken cancellationToken = default) =>
        StartChildrenAsync(key, parent, assignments, null, cancellationToken);

    internal ValueTask<ImmutableArray<AgentRunOwner>> StartChildrenAsync(
        AgentExecutionKey key, AgentRunOwner parent, ImmutableArray<AgentRunAssignment> assignments,
        long? expectedPlanVersion, CancellationToken cancellationToken) =>
        ChangeAsync(key, execution =>
        {
            var caller = RequireRunnable(execution, parent);
            if (expectedPlanVersion is not null &&
                (CurrentPlan(caller) is not { } plan || plan.Version != expectedPlanVersion))
            {
                throw new InvalidOperationException("The active plan has changed.");
            }
            if (assignments.IsDefaultOrEmpty)
            {
                throw new ArgumentException("At least one child assignment is required.", nameof(assignments));
            }

            if (assignments.Select(item => item.RunId).Distinct().Count() != assignments.Length)
            {
                throw new ArgumentException("A child batch requires distinct identities.", nameof(assignments));
            }

            var owners = ImmutableArray.CreateBuilder<AgentRunOwner>(assignments.Length);
            if (assignments.Any(item => execution.Runs.ContainsKey(item.RunId)))
            {
                foreach (var assignment in assignments)
                {
                    ValidateAssignment(assignment);
                    var existing = RequireRun(execution, assignment.RunId);
                    if (existing.ChildCall != new AgentChildCall(parent.RunId, parent.Epoch, assignment.RunId) ||
                        existing.OriginalAssignment is not { } original || !SameAssignment(original, assignment))
                    {
                        throw new InvalidOperationException("The child run ID is already bound to a different assignment or batch.");
                    }

                    owners.Add(InitialOwner(original));
                }

                return (execution.Runs, owners.MoveToImmutable());
            }

            var runs = execution.Runs.ToBuilder();
            foreach (var assignment in assignments)
            {
                ValidateAssignment(assignment);
                RequireNarrowerScope(caller, assignment.Tools, assignment.Inputs);
                caller = caller with { RemainingBudget = caller.RemainingBudget.Reserve(assignment.Budget) };
                var child = CreateRun(assignment, new(parent.RunId, parent.Epoch, assignment.RunId));
                runs.Add(child.Owner.RunId, child);
                owners.Add(child.Owner);
            }

            runs[parent.RunId] = caller;
            return (runs.ToImmutable(), owners.MoveToImmutable());
        }, cancellationToken);

    public async ValueTask<ImmutableArray<AgentRunResult?>> ReadChildResultsAsync(
        AgentExecutionKey key, AgentRunOwner parent, ImmutableArray<Guid> childRunIds,
        CancellationToken cancellationToken = default)
    {
        var execution = await FindAsync(key, cancellationToken);
        RequireOwned(execution, parent);
        var results = ImmutableArray.CreateBuilder<AgentRunResult?>(childRunIds.Length);
        foreach (var childRunId in childRunIds)
        {
            results.Add(RequireChild(execution, parent, childRunId).Result);
        }

        return results.MoveToImmutable();
    }

    public ValueTask<bool> ReserveAsync(
        AgentExecutionKey key, AgentRunOwner owner, Guid operationId, AgentBudget amount,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(key, execution =>
        {
            var run = RequireRunnable(execution, owner);
            RequireId(operationId);
            if (run.Reservations.TryGetValue(operationId, out var existing))
            {
                if (!IsOwnedReservation(existing, owner) || existing.Reserved != amount)
                {
                    throw new InvalidOperationException("The operation ID is unavailable for this reservation.");
                }

                return (execution.Runs, false);
            }

            return (execution.Runs.SetItem(owner.RunId, run with
            {
                RemainingBudget = run.RemainingBudget.Reserve(amount),
                Reservations = run.Reservations.Add(operationId, new(amount) { OwnerEpoch = owner.Epoch })
            }), true);
        }, cancellationToken);

    public ValueTask<bool> SettleAsync(
        AgentExecutionKey key, AgentRunOwner owner, Guid operationId, AgentBudget used,
        CancellationToken cancellationToken = default) =>
        SettleAsync(key, owner, operationId, used, null, null, cancellationToken);

    internal ValueTask<bool> RecordModelDecisionAsync(
        AgentExecutionKey key, AgentRunOwner owner, Guid operationId, long used, AgentModelDecision decision,
        CancellationToken cancellationToken) =>
        SettleAsync(key, owner, operationId, new(used, 0), decision, null, cancellationToken);

    internal ValueTask<bool> RecordToolObservationAsync(
        AgentExecutionKey key, AgentRunOwner owner, Guid operationId, AgentToolObservation observation,
        CancellationToken cancellationToken) =>
        SettleAsync(key, owner, operationId, new(0, 1), null, observation, cancellationToken);

    public async ValueTask<AgentRunSnapshot> ReadRunAsync(
        AgentExecutionKey key, AgentRunOwner owner, CancellationToken cancellationToken = default)
    {
        // Persistence retains cross-epoch receipts for execution recovery and event deduplication.
        var run = await ReadRunStateAsync(key, owner, cancellationToken);
        return run with
        {
            ChildCall = owner.Epoch == 1 ? run.ChildCall : null,
            OriginalAssignment = owner.Epoch == 1 ? run.OriginalAssignment : null,
            Handoffs = [.. run.Handoffs.Where(handoff => handoff.PreviousOwner == owner)],
            Resumptions = [.. run.Resumptions.Where(resumption => resumption.OwnerEpoch == owner.Epoch)],
            Reservations = run.Reservations.RemoveRange(run.Reservations
                .Where(entry => !IsOwnedReservation(entry.Value, owner)).Select(entry => entry.Key)),
            PlanHistory = [.. run.PlanHistory.Where(plan => plan.OwnerEpoch == owner.Epoch)],
            Discussion = run.Discussion?.OwnerEpoch == owner.Epoch ? run.Discussion : null,
            TeamDispatchClaimed = false
        };
    }

    internal async ValueTask<AgentRunSnapshot> ReadRunStateAsync(
        AgentExecutionKey key, AgentRunOwner owner, CancellationToken cancellationToken) =>
        RequireOwned(await FindAsync(key, cancellationToken), owner);

    private static bool IsOwnedReservation(AgentBudgetReservation reservation, AgentRunOwner owner) =>
        reservation.OwnerEpoch == owner.Epoch ||
        // An unsettled legacy reservation cannot have crossed a handoff.
        (reservation.OwnerEpoch == 0 && (owner.Epoch == 1 || reservation.Used is null));

    private ValueTask<bool> SettleAsync(
        AgentExecutionKey key, AgentRunOwner owner, Guid operationId, AgentBudget used,
        AgentModelDecision? modelDecision, AgentToolObservation? observation, CancellationToken cancellationToken) =>
        ChangeAsync(key, execution =>
        {
            var run = RequireOwned(execution, owner);
            if (!run.Reservations.TryGetValue(operationId, out var reservation) || !IsOwnedReservation(reservation, owner))
            {
                throw new InvalidOperationException("No budget was reserved for this operation in the current epoch.");
            }

            if (reservation.Used is { } prior)
            {
                if (prior != used || modelDecision is not null || observation is not null)
                {
                    throw new InvalidOperationException("The operation already has a usage receipt.");
                }

                return (execution.Runs, false);
            }

            if (run.Result is not null)
            {
                throw new InvalidOperationException("The operation could not be reconciled before the run ended.");
            }

            return (execution.Runs.SetItem(owner.RunId, run with
            {
                RemainingBudget = run.RemainingBudget.Refund(reservation.Reserved.Reserve(used)),
                Reservations = run.Reservations.SetItem(operationId, reservation with
                {
                    OwnerEpoch = owner.Epoch, Used = used, ModelDecision = modelDecision, ToolObservation = observation
                })
            }), true);
        }, cancellationToken);

    public ValueTask<AgentHandoffRecord> OfferHandoffAsync(
        AgentExecutionKey key, AgentRunOwner owner, AgentHandoffOffer offer,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(key, execution =>
        {
            var run = RequireRunnable(execution, owner);
            RequireUndispatched(run);
            RequireSettledOperations(run);
            RequireId(offer.HandoffId);
            ArgumentException.ThrowIfNullOrWhiteSpace(offer.RecipientAgentId);
            RequireNarrowerScope(run, offer.Tools, offer.Inputs);
            if (offer.RecipientAgentId == owner.AgentId || run.Handoffs.Any(entry => entry.Offer.HandoffId == offer.HandoffId))
            {
                throw new InvalidOperationException("The handoff requires a different agent and a new handoff ID.");
            }

            if (Children(execution.Runs, owner.RunId).Any(child => child.Result is null))
            {
                throw new InvalidOperationException("Active child runs must end before handoff.");
            }

            var handoff = new AgentHandoffRecord(offer, owner, null);
            return (execution.Runs.SetItem(owner.RunId, run with { Handoffs = run.Handoffs.Add(handoff) }), handoff);
        }, cancellationToken);

    public ValueTask<AgentRunOwner> AcceptHandoffAsync(
        AgentExecutionKey key, Guid runId, Guid handoffId, string recipientAgentId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(key, execution =>
        {
            var run = RequireRun(execution, runId);
            if (run.PendingHandoff is not { } pending || pending.Offer.HandoffId != handoffId ||
                pending.Offer.RecipientAgentId != recipientAgentId)
            {
                throw new InvalidOperationException("The recipient does not hold this handoff offer.");
            }

            RequireOpen(execution, run);
            var owner = new AgentRunOwner(runId, recipientAgentId, checked(run.Owner.Epoch + 1));
            return (execution.Runs.SetItem(runId, run with
            {
                Owner = owner,
                Tools = pending.Offer.Tools,
                Inputs = pending.Offer.Inputs,
                Handoffs = run.Handoffs.SetItem(run.Handoffs.Length - 1, pending with { AcceptedOwner = owner })
            }), owner);
        }, cancellationToken);

    public ValueTask<AgentEventWait> WaitForEventAsync(
        AgentExecutionKey key, AgentRunOwner owner, AgentEventWait wait,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(key, execution =>
        {
            var run = RequireRunnable(execution, owner);
            RequireUndispatched(run);
            RequireSettledOperations(run);
            RequireId(wait.WaitId);
            ValidateSelector(wait.Selector);
            if (run.Resumptions.Any(entry => entry.Wait.WaitId == wait.WaitId))
            {
                throw new InvalidOperationException("The wait ID has already been consumed.");
            }

            return (execution.Runs.SetItem(owner.RunId, run with { Wait = wait }), wait);
        }, cancellationToken);

    public ValueTask<bool> ResumeAsync(
        AgentExecutionKey key, Guid runId, Guid waitId, AgentEventDelivery delivery,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(key, execution =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(delivery.EventId);
            ValidateSelector(delivery.Selector);
            ValidateArtifact(delivery.Payload);
            var run = RequireRun(execution, runId);
            if (delivery.TenantId != key.TenantId)
            {
                throw new InvalidOperationException("The event belongs to a different execution scope.");
            }

            foreach (var prior in run.Resumptions)
            {
                if (prior.Wait.WaitId == waitId ||
                    (prior.Delivery.Selector.Source == delivery.Selector.Source && prior.Delivery.EventId == delivery.EventId))
                {
                    if (prior.Wait.WaitId == waitId && prior.Delivery == delivery)
                    {
                        return (execution.Runs, false);
                    }

                    throw new InvalidOperationException("The wait or event identity is already bound to another delivery.");
                }
            }

            RequireOpen(execution, run);
            if (run.Wait is not { } wait || wait.WaitId != waitId || wait.Selector != delivery.Selector)
            {
                throw new InvalidOperationException("The event does not match the registered wait.");
            }

            return (execution.Runs.SetItem(runId, run with
            {
                Wait = null,
                Resumptions = run.Resumptions.Add(new(wait, delivery, run.Owner.Epoch))
            }), true);
        }, cancellationToken);

    public ValueTask<AgentRunResult> FinishAsync(
        AgentExecutionKey key, AgentRunOwner owner, AgentRunResult result,
        CancellationToken cancellationToken = default) =>
        FinishAsync(key, owner, result, fromTeamWorker: false, cancellationToken);

    internal ValueTask<AgentRunResult> FinishTeamChildAsync(
        AgentExecutionKey key, AgentRunOwner owner, AgentRunResult result, CancellationToken cancellationToken) =>
        FinishAsync(key, owner, result, fromTeamWorker: true, cancellationToken);

    private ValueTask<AgentRunResult> FinishAsync(
        AgentExecutionKey key, AgentRunOwner owner, AgentRunResult result, bool fromTeamWorker,
        CancellationToken cancellationToken) =>
        ChangeAsync(key, execution =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(result.Code);
            if (result.Output is { } output)
            {
                ValidateArtifact(output);
            }

            var run = RequireOwned(execution, owner);
            if (run.Result == result)
            {
                return (execution.Runs, result);
            }

            var runs = execution.Runs.ToBuilder();
            switch (result.Kind)
            {
                case AgentResultKind.Returned:
                case AgentResultKind.Failed:
                    RequireRunnable(execution, owner);
                    RequireSettledOperations(run);
                    if (Children(execution.Runs, owner.RunId).Any(child => child.Result is null))
                    {
                        throw new InvalidOperationException("Child runs must end before their parent can finish.");
                    }

                    SetResult(runs, run, result);
                    break;
                case AgentResultKind.Cancelled when run.Result is null && result.Output is null:
                    if (fromTeamWorker)
                    {
                        RequireOpen(execution, run);
                    }

                    CancelTree(runs, run, result);
                    break;
                default:
                    throw new InvalidOperationException("This agent result cannot be applied.");
            }

            return (runs.ToImmutable(), result);
        }, cancellationToken);

    private async ValueTask<AgentExecutionSnapshot> FindAsync(AgentExecutionKey key, CancellationToken cancellationToken) =>
        await store.FindAsync(key, cancellationToken) ?? throw new InvalidOperationException("The agent execution does not exist.");

    private ValueTask<T> ChangeAsync<T>(AgentExecutionKey key,
        Func<AgentExecutionSnapshot, (ImmutableDictionary<Guid, AgentRunSnapshot> Runs, T Result)> change,
        CancellationToken cancellationToken) =>
        ChangeExecutionAsync(key, execution =>
        {
            var (runs, result) = change(execution);
            return (ReferenceEquals(runs, execution.Runs) ? execution : execution with { Runs = runs }, result);
        }, cancellationToken);

    private async ValueTask<T> ChangeExecutionAsync<T>(AgentExecutionKey key,
        Func<AgentExecutionSnapshot, (AgentExecutionSnapshot Snapshot, T Result)> change,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var execution = await FindAsync(key, cancellationToken);
            var (updated, result) = change(execution);
            if (ReferenceEquals(updated, execution) || await store.TryReplaceAsync(
                updated with { Revision = checked(execution.Revision + 1) }, execution.Revision, cancellationToken))
            {
                return result;
            }
        }
    }

    private AgentRunSnapshot RequireRunnable(AgentExecutionSnapshot execution, AgentRunOwner owner)
    {
        var run = RequireOwned(execution, owner);
        RequireOpen(execution, run);
        if (run.Wait is not null || run.PendingHandoff is not null)
        {
            throw new InvalidOperationException("The agent run is waiting for an event or handoff.");
        }

        return run;
    }

    private void RequireOpen(AgentExecutionSnapshot execution, AgentRunSnapshot run)
    {
        if (run.Result is not null || execution.Deadline <= clock.GetUtcNow())
        {
            throw new InvalidOperationException("The agent run has ended or exceeded its deadline.");
        }
    }

    private static AgentRunSnapshot RequireOwned(AgentExecutionSnapshot execution, AgentRunOwner owner)
    {
        var run = RequireRun(execution, owner.RunId);
        if (run.Owner != owner)
        {
            throw new InvalidOperationException("The caller does not hold the current run ownership.");
        }

        return run;
    }

    private static AgentRunSnapshot RequireRun(AgentExecutionSnapshot execution, Guid runId) =>
        execution.Runs.TryGetValue(runId, out var run) ? run : throw new InvalidOperationException("The agent run does not exist.");

    private static AgentRunSnapshot RequireChild(AgentExecutionSnapshot execution, AgentRunOwner parent, Guid childRunId)
    {
        if (!execution.Runs.TryGetValue(childRunId, out var child) ||
            child.ChildCall != new AgentChildCall(parent.RunId, parent.Epoch, childRunId))
        {
            throw new InvalidOperationException("The run is outside the caller's child scope.");
        }

        return child;
    }

    private static void RequireUndispatched(AgentRunSnapshot run)
    {
        if (run.TeamDispatchClaimed)
        {
            throw new InvalidOperationException("The team worker dispatch has no confirmed result and requires reconciliation.");
        }
    }

    private static void RequireSettledOperations(AgentRunSnapshot run)
    {
        if (run.Reservations.Values.Any(reservation => reservation.Used is null))
        {
            throw new InvalidOperationException("Reserved operations must be reconciled before the run can transition.");
        }
    }

    private static IEnumerable<AgentRunSnapshot> Children(ImmutableDictionary<Guid, AgentRunSnapshot> runs, Guid parentId) =>
        runs.Values.Where(item => item.ChildCall?.ParentRunId == parentId);

    private static void CancelTree(ImmutableDictionary<Guid, AgentRunSnapshot>.Builder runs, AgentRunSnapshot root, AgentRunResult result)
    {
        var subtree = new List<Guid> { root.Owner.RunId };
        var children = runs.Values.Where(item => item.Result is null && item.ChildCall is not null)
            .ToLookup(item => item.ChildCall!.ParentRunId, item => item.Owner.RunId);
        for (var index = 0; index < subtree.Count; index++)
        {
            subtree.AddRange(children[subtree[index]]);
        }

        for (var index = subtree.Count - 1; index >= 0; index--)
        {
            SetResult(runs, runs[subtree[index]], result);
        }
    }

    private static void SetResult(ImmutableDictionary<Guid, AgentRunSnapshot>.Builder runs, AgentRunSnapshot run, AgentRunResult result)
    {
        runs[run.Owner.RunId] = run with { Result = result };
        if (run.ChildCall is { } call)
        {
            var parent = runs[call.ParentRunId];
            runs[call.ParentRunId] = parent with { RemainingBudget = parent.RemainingBudget.Refund(run.RemainingBudget) };
        }
    }

    private static AgentRunOwner InitialOwner(AgentRunAssignment assignment) => new(assignment.RunId, assignment.AgentId, 1);

    private static AgentRunSnapshot CreateRun(AgentRunAssignment assignment, AgentChildCall? call) =>
        new(InitialOwner(assignment), assignment.Task, assignment.Budget, assignment.Tools,
            assignment.Inputs, call, [], null, [], ImmutableDictionary<Guid, AgentBudgetReservation>.Empty, null)
        {
            OriginalAssignment = assignment
        };

    private static bool SameAssignment(AgentRunAssignment left, AgentRunAssignment right) =>
        left.RunId == right.RunId && left.AgentId == right.AgentId && left.Task == right.Task &&
        left.Budget == right.Budget && left.Tools.SequenceEqual(right.Tools, StringComparer.Ordinal) &&
        left.Inputs.SequenceEqual(right.Inputs);

    private static void RequireNarrowerScope(AgentRunSnapshot source, ImmutableArray<string> tools, ImmutableArray<AgentArtifactReference> inputs)
    {
        ValidateScope(tools, inputs);
        if (tools.Any(tool => !source.Tools.Contains(tool, StringComparer.Ordinal)) ||
            inputs.Any(input => !source.Inputs.Contains(input)))
        {
            throw new InvalidOperationException("The requested tool or artifact is outside the caller's scope.");
        }
    }

    private static void ValidateAssignment(AgentRunAssignment assignment)
    {
        RequireId(assignment.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assignment.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assignment.Task);
        if (!assignment.Budget.IsNonNegative)
        {
            throw new ArgumentException("Agent budgets must be nonnegative.", nameof(assignment));
        }

        ValidateScope(assignment.Tools, assignment.Inputs);
    }

    private static void ValidateScope(ImmutableArray<string> tools, ImmutableArray<AgentArtifactReference> inputs)
    {
        if (tools.IsDefault || inputs.IsDefault || tools.Any(string.IsNullOrWhiteSpace) ||
            tools.Distinct(StringComparer.Ordinal).Count() != tools.Length || inputs.Distinct().Count() != inputs.Length)
        {
            throw new ArgumentException("An agent scope requires explicit and distinct tools and artifacts.");
        }

        foreach (var input in inputs)
        {
            ValidateArtifact(input);
        }
    }

    private static void ValidateArtifact(AgentArtifactReference artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifact.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifact.ContentHash);
    }

    private static void ValidateSelector(AgentEventSelector selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector.SubjectId);
    }

    private static void RequireId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An agent execution identity must be nonempty.", nameof(id));
        }
    }
}
