using CrestCreates.Agent.Tools;
using CrestCreates.Agent.Tools.Persistence.Testing.Cases;
using CrestCreates.Accountability.Abstractions;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Recording;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Invocation;

/// <summary>
/// InMemory activation of the shared ownership-fence contract cases
/// (review P0): Indeterminate fencing and reconciliation-claim ordering.
/// Exercises the real DevelopmentInMemory participants through DI so the
/// cases also cover the registered service graph.
/// </summary>
public sealed class AgentToolPreDispatchOwnershipFenceTests : IAsyncLifetime
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.Parse("2026-07-16T00:00:00Z");

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private readonly ManualTimeProvider _time = new();
    private ServiceProvider _provider = null!;
    private AgentToolPreDispatchOwnershipFenceContext _ctx = null!;

    public Task InitializeAsync()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate(
            _time, TimeSpan.FromSeconds(10));

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(gate);
        services.AddSingleton<IAuditRecorder>(new NoopAuditRecorder());
        services.AddCrestAgentTools();
        _provider = services.BuildServiceProvider();

        _ctx = new AgentToolPreDispatchOwnershipFenceContext
        {
            Gate = _provider.GetRequiredService<IAgentToolInvocationGate>(),
            BudgetGate = _provider.GetRequiredService<IAgentToolBudgetGate>(),
            Auditor = _provider.GetRequiredService<IAgentToolGovernanceAuditor>(),
            Reconciler = _provider.GetRequiredService<IAgentToolPreDispatchReconciler>(),
            ReconciliationStore = _provider.GetRequiredService<IAgentToolPreDispatchReconciliationStore>(),
            ExpireLeaseAsync = (_, _) =>
            {
                _time.Advance(TimeSpan.FromSeconds(11));
                return ValueTask.CompletedTask;
            }
        };
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _provider.DisposeAsync().AsTask();

    private sealed class NoopAuditRecorder : IAuditRecorder
    {
        public ValueTask<AuditRecordResult> RecordAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditRecordResult
            {
                AuditId = envelope.AuditId,
                Status = AuditRecordStatus.Recorded,
                ProcessedAt = DateTimeOffset.UtcNow
            });
    }

    [Fact]
    public Task IndeterminatePending_Should_Reject_ReservationBind()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .IndeterminatePending_Should_Reject_ReservationBind(_ctx, CancellationToken.None);

    [Fact]
    public Task IndeterminateReady_Should_Reject_AcceptedBind()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .IndeterminateReady_Should_Reject_AcceptedBind(_ctx, CancellationToken.None);

    [Fact]
    public Task IndeterminateAccepted_Should_Reject_DispatchStarted()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .IndeterminateAccepted_Should_Reject_DispatchStarted(_ctx, CancellationToken.None);

    [Fact]
    public Task LivePending_Reconcile_Should_Not_AbandonActiveWorker()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .LivePending_Reconcile_Should_Not_AbandonActiveWorker(_ctx, CancellationToken.None);

    [Fact]
    public Task LiveReady_Reconcile_Should_Not_ReleaseActiveWorkerBudget()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .LiveReady_Reconcile_Should_Not_ReleaseActiveWorkerBudget(_ctx, CancellationToken.None);

    [Fact]
    public Task ExpiredLease_Reconcile_Should_ClaimAndConverge()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .ExpiredLease_Reconcile_Should_ClaimAndConverge(_ctx, CancellationToken.None);

    [Fact]
    public Task MarkedIndeterminate_Reconcile_Should_ClaimAndConverge()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .MarkedIndeterminate_Reconcile_Should_ClaimAndConverge(_ctx, CancellationToken.None);

    [Fact]
    public Task ReconciliationClaimWins_Should_BlockDispatch()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .ReconciliationClaimWins_Should_BlockDispatch(_ctx, CancellationToken.None);

    [Fact]
    public Task ReconciliationClaimWins_Should_ReleaseBudgetExactlyOnce()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .ReconciliationClaimWins_Should_ReleaseBudgetExactlyOnce(_ctx, CancellationToken.None);

    [Fact]
    public Task ReconciliationClaimWins_Should_FinalizeGovernanceExactlyOnce()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .ReconciliationClaimWins_Should_FinalizeGovernanceExactlyOnce(_ctx, CancellationToken.None);

    [Fact]
    public Task DispatchWins_Should_Not_ReleaseBudget()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .DispatchWins_Should_Not_ReleaseBudget(_ctx, CancellationToken.None);

    [Fact]
    public Task DispatchWins_Should_Not_FinalizeGovernanceAsReleased()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .DispatchWins_Should_Not_FinalizeGovernanceAsReleased(_ctx, CancellationToken.None);

    [Fact]
    public Task LiveAccepted_DispatchAndReconcileRace_Should_HaveSingleWinner()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .LiveAccepted_DispatchAndReconcileRace_Should_HaveSingleWinner(_ctx, CancellationToken.None);

    [Fact]
    public Task PublishedCompletion_Should_Never_Accept_LateIndeterminateMutation()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .PublishedCompletion_Should_Never_Accept_LateIndeterminateMutation(_ctx, CancellationToken.None);

    [Fact]
    public Task PublishedRelease_Should_Never_Accept_LateIndeterminateMutation()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .PublishedRelease_Should_Never_Accept_LateIndeterminateMutation(_ctx, CancellationToken.None);

    [Fact]
    public Task CompletionPending_WithIndeterminateMarker_Should_ReadAsIndeterminate()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .CompletionPending_WithIndeterminateMarker_Should_ReadAsIndeterminate(_ctx, CancellationToken.None);

    [Fact]
    public Task ReleasePending_WithIndeterminateMarker_Should_ReadAsIndeterminate()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .ReleasePending_WithIndeterminateMarker_Should_ReadAsIndeterminate(_ctx, CancellationToken.None);

    [Fact]
    public Task MarkIndeterminate_Is_Idempotent_On_ExistingMarker()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .MarkIndeterminate_Is_Idempotent_On_ExistingMarker(_ctx, CancellationToken.None);

    [Fact]
    public Task MarkIndeterminate_ZeroAffectedRows_Should_Not_ReportSuccess()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .MarkIndeterminate_ZeroAffectedRows_Should_Not_ReportSuccess(_ctx, CancellationToken.None);

    [Fact]
    public Task MarkIndeterminate_vs_DispatchStarted_Should_HaveLinearizableOrder()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .MarkIndeterminate_vs_DispatchStarted_Should_HaveLinearizableOrder(_ctx, CancellationToken.None);

    [Fact]
    public Task MarkIndeterminate_vs_PublishCompletion_Should_HaveSingleWinner()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .MarkIndeterminate_vs_PublishCompletion_Should_HaveSingleWinner(_ctx, CancellationToken.None);

    [Fact]
    public Task MarkIndeterminate_vs_PublishRelease_Should_HaveSingleWinner()
        => AgentToolPreDispatchOwnershipFenceContractCases
            .MarkIndeterminate_vs_PublishRelease_Should_HaveSingleWinner(_ctx, CancellationToken.None);
}
