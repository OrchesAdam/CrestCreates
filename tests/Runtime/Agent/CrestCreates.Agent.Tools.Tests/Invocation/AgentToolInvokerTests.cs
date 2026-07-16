using System.Text.Json;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Invocation;

public sealed class AgentToolInvokerTests
{
    [Fact]
    public async Task Invoke_SucceedsAndCompletedReplayDoesNotRedispatchOrReserveAgain()
    {
        var harness = CreateHarness();

        var first = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));
        var replay = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        replay.Should().Be(first);
        harness.Dispatcher.CallCount.Should().Be(1);
        harness.Budget.ReserveCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_ChangedCallOriginForSameLogicalInvocationConflictsBeforeDispatch()
    {
        var harness = CreateHarness(automaticAllowed: true);
        (await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName)))
            .Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);
        harness.Execution.CurrentValue = harness.Execution.CurrentValue! with
        {
            CallOrigin = AgentToolCallOrigin.AutomaticSelection
        };

        var conflict = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        conflict.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationConflict);
        harness.Dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_RoleDeniedToolBehavesAsUnknownAndRecordsGovernanceDecision()
    {
        var harness = CreateHarness();
        harness.Execution.CurrentValue = harness.Execution.CurrentValue! with
        {
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "viewer" }
        };

        var outcome = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.UnknownTool);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Budget.ReserveCount.Should().Be(0);
        harness.Auditor!.Decisions.Should().ContainSingle(decision =>
            decision.ReasonCode == "role_denied");
    }

    [Fact]
    public async Task Invoke_TimedOutCapabilityConsumesIndeterminateBudgetAndBlocksReplay()
    {
        var harness = CreateHarness(CapabilityExecutionResult.Timeout(TimeSpan.Zero));

        var first = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));
        var retry = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        first.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Dispatcher.CallCount.Should().Be(1);
        harness.Budget.LastFinalState.Should().Be(AgentToolBudgetReservationState.Indeterminate);
    }

    [Fact]
    public async Task Invoke_RequiredPreDispatchAuditFailureReleasesBudgetAndNeverDispatches()
    {
        var harness = CreateHarness(requiredAudit: true, audit: new ThrowingAuditor());

        var outcome = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.GovernanceDenied);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Budget.LastFinalState.Should().Be(AgentToolBudgetReservationState.Released);
    }

    [Fact]
    public async Task Invoke_NonObjectArgumentsFailBeforeLogicalInvocationAcquisition()
    {
        var harness = CreateHarness();
        using var document = JsonDocument.Parse("[]");

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName, document.RootElement.Clone()));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvalidRequest);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Budget.ReserveCount.Should().Be(0);
    }

    [Fact]
    public async Task Invoke_UncertainDispatchFenceReleasesBudgetButMarksInvocationIndeterminate()
    {
        var gate = new ThrowingDispatchFenceGate();
        var harness = CreateHarness(invocationGate: gate);

        var outcome = await harness.Invoker.InvokeAsync(new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Budget.LastFinalState.Should().Be(AgentToolBudgetReservationState.Released);
        gate.MarkIndeterminateCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoke_BudgetReservationExceptionMarksLogicalInvocationIndeterminate()
    {
        var harness = CreateHarness(throwOnBudgetReserve: true);

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));
        var retry = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        retry.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Dispatcher.CallCount.Should().Be(0);
        harness.Auditor!.Decisions.Should().ContainSingle(decision =>
            decision.ReasonCode == "budget_reservation_uncertain");
    }

    [Fact]
    public async Task Invoke_BudgetSettlementExceptionFinalizesAuditAsUnknownAndIndeterminate()
    {
        var harness = CreateHarness(throwOnBudgetFinalize: true);

        var outcome = await harness.Invoker.InvokeAsync(
            new AgentToolInvocationRequest(harness.ToolName));

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        harness.Auditor!.Finalizations.Should().ContainSingle().Which.Should().Match<AgentToolGovernanceFinalizationRecord>(
            finalization => finalization.BudgetReservation.State == AgentToolBudgetReservationState.Unknown
                && finalization.InvocationState == AgentToolInvocationTerminalState.Indeterminate);
    }

    private static Harness CreateHarness(
        CapabilityExecutionResult? dispatcherResult = null,
        bool automaticAllowed = false,
        bool requiredAudit = false,
        IAgentToolGovernanceAuditor? audit = null,
        IAgentToolInvocationGate? invocationGate = null,
        bool throwOnBudgetReserve = false,
        bool throwOnBudgetFinalize = false)
    {
        var capability = AgentToolRuntimeTestFixture.Capability("invoker-capability");
        var source = AgentToolRuntimeTestFixture.Tool(
            $"invoker-tool-{Guid.NewGuid():N}",
            capability.Id,
            $"invoker.tool.{Guid.NewGuid():N}",
            audit: requiredAudit ? AgentToolAuditMode.Required : AgentToolAuditMode.BestEffort);
        var tool = automaticAllowed ? CopyWithAutomaticSelection(source) : source;
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);
        var snapshot = AgentToolRuntimeTestFixture.SnapshotBuilder(
                AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
                AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability),
                AgentToolRuntimeTestFixture.BuildSchemaRegistry())
            .Build();
        var snapshots = new AgentToolRuntimeSnapshotProvider();
        snapshots.Publish(snapshot);

        var execution = new MutableExecutionContextAccessor
        {
            CurrentValue = new AgentExecutionContext
            {
                ExecutionId = "execution-1",
                InvocationId = "invocation-1",
                AgentId = "agent-1",
                AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
                CallOrigin = AgentToolCallOrigin.ExplicitRequest
            }
        };
        var dispatcher = new RecordingDispatcher(
            dispatcherResult ?? CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        var budget = new RecordingBudgetGate(throwOnBudgetReserve, throwOnBudgetFinalize);
        var inMemoryAudit = audit is null
            ? new DevelopmentInMemoryAgentToolGovernanceAuditor()
            : null;
        var invoker = new AgentToolInvoker(
            snapshots,
            execution,
            new TestCurrentUser(),
            new TestTenantContext(),
            invocationGate ?? new DevelopmentInMemoryAgentToolInvocationGate(),
            new FailClosedAgentToolApprovalGate(),
            budget,
            audit ?? inMemoryAudit!,
            dispatcher,
            new SchemaValidator(),
            new AgentToolInvocationFingerprintBuilder(),
            new AgentCapabilityIdempotencyKeyBuilder(),
            new AgentToolResultMapper());
        return new Harness(tool.ToolName, invoker, execution, dispatcher, budget, inMemoryAudit);
    }

    private static AgentCapabilityToolDescriptor CopyWithAutomaticSelection(
        AgentCapabilityToolDescriptor source)
        => new()
        {
            Id = source.Id,
            Name = source.Name,
            Version = source.Version,
            State = source.State,
            Capability = source.Capability,
            ToolName = source.ToolName,
            Title = source.Title,
            Description = source.Description,
            SelectionPolicy = AgentToolSelectionPolicy.AutomaticAllowed,
            SideEffectKind = source.SideEffectKind,
            RiskFloor = source.RiskFloor,
            ApprovalMode = source.ApprovalMode,
            Budget = source.Budget,
            AuditMode = source.AuditMode,
            AllowedAgentRoles = source.AllowedAgentRoles
        };

    private sealed record Harness(
        string ToolName,
        AgentToolInvoker Invoker,
        MutableExecutionContextAccessor Execution,
        RecordingDispatcher Dispatcher,
        RecordingBudgetGate Budget,
        DevelopmentInMemoryAgentToolGovernanceAuditor? Auditor);

    private sealed class MutableExecutionContextAccessor : IAgentExecutionContextAccessor
    {
        public AgentExecutionContext? CurrentValue { get; set; }
        public AgentExecutionContext? Current => CurrentValue;
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public string? CurrentTenantId => "tenant-1";
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public string Id => "user-1";
        public string UserName => "user";
        public bool IsAuthenticated => true;
        public string TenantId => "tenant-1";
        public string[] Roles => [];
        public Guid? OrganizationId => null;
        public IReadOnlyList<Guid> OrganizationIds => Array.Empty<Guid>();
        public int DataScopeValue => 0;
        public bool IsSuperAdmin => false;
        public string FindClaimValue(string claimType) => string.Empty;
        public string[] FindClaimValues(string claimType) => [];
        public bool IsInRole(string roleName) => false;
        public bool IsInOrganization(Guid orgId) => false;
    }

    private sealed class RecordingDispatcher(CapabilityExecutionResult result) : ICapabilityDispatcher
    {
        public int CallCount { get; private set; }

        public Task<CapabilityExecutionResult> DispatchAsync(
            CapabilityDescriptor descriptor,
            InvocationSource source,
            object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
        {
            CallCount++;
            configureContext?.Invoke(new CapabilityExecutionContext { ServiceProvider = EmptyServiceProvider.Instance });
            return Task.FromResult(result);
        }

        public Task<CapabilityExecutionResult> DispatchAsync(
            string capabilityId,
            InvocationSource source,
            object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingBudgetGate(bool throwOnReserve, bool throwOnFinalize) : IAgentToolBudgetGate
    {
        private AgentToolBudgetReservation? _reservation;

        public int ReserveCount { get; private set; }
        public AgentToolBudgetReservationState? LastFinalState { get; private set; }

        public ValueTask<AgentToolBudgetReserveResult> ReserveAsync(
            AgentToolBudgetReserveRequest request,
            CancellationToken cancellationToken = default)
        {
            if (throwOnReserve)
                throw new InvalidOperationException("budget response unavailable");
            ReserveCount++;
            _reservation = new AgentToolBudgetReservation
            {
                ReservationId = $"reservation-{ReserveCount}",
                AttemptId = request.Context.AttemptId,
                InvocationFingerprint = request.Context.InvocationFingerprint,
                Category = request.Context.Governance.Budget.Category,
                CostUnits = request.Context.Governance.Budget.CostUnits,
                MaxCallsPerExecution = request.Context.Governance.Budget.MaxCallsPerExecution,
                State = AgentToolBudgetReservationState.Reserved
            };
            return ValueTask.FromResult(new AgentToolBudgetReserveResult
            {
                Status = AgentToolBudgetReserveStatus.Reserved,
                Reservation = _reservation
            });
        }

        public ValueTask<AgentToolBudgetReservation> FinalizeAsync(
            AgentToolBudgetFinalizeRequest request,
            CancellationToken cancellationToken = default)
        {
            if (throwOnFinalize)
                throw new InvalidOperationException("budget settlement unavailable");
            LastFinalState = request.RequestedState;
            _reservation = _reservation! with { State = request.RequestedState };
            return ValueTask.FromResult(_reservation);
        }
    }

    private sealed class ThrowingAuditor : IAgentToolGovernanceAuditor
    {
        public ValueTask RecordDecisionAsync(
            AgentToolGovernanceDecisionRecord record,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<AgentToolGovernanceAuditHandle> RecordPreDispatchAsync(
            AgentToolGovernancePreDispatchRecord record,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<AgentToolGovernanceAuditHandle>(new InvalidOperationException("audit unavailable"));

        public ValueTask FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class ThrowingDispatchFenceGate : IAgentToolInvocationGate
    {
        private readonly DevelopmentInMemoryAgentToolInvocationGate _inner = new();

        public int MarkIndeterminateCount { get; private set; }

        public ValueTask<AgentToolInvocationAcquireResult> AcquireAsync(
            AgentToolInvocationAcquireRequest request,
            CancellationToken cancellationToken = default)
            => _inner.AcquireAsync(request, cancellationToken);

        public ValueTask<AgentToolInvocationLease> RenewAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.RenewAsync(lease, cancellationToken);

        public ValueTask<bool> TryMarkDispatchStartedAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<bool>(new InvalidOperationException("fence result unavailable"));

        public ValueTask CompleteAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationOutcome outcome,
            CancellationToken cancellationToken = default)
            => _inner.CompleteAsync(lease, outcome, cancellationToken);

        public async ValueTask MarkIndeterminateAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
        {
            MarkIndeterminateCount++;
            await _inner.MarkIndeterminateAsync(lease, reasonCode, cancellationToken);
        }

        public ValueTask ReleaseLeaseAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => _inner.ReleaseLeaseAsync(lease, cancellationToken);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();
        public object? GetService(Type serviceType) => null;
    }
}
