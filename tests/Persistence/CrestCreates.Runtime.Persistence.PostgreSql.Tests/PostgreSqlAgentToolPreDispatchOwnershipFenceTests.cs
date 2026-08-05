using CrestCreates.Agent.Tools;
using CrestCreates.Agent.Tools.Persistence.Testing.Cases;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

/// <summary>
/// PostgreSQL activation of the shared ownership-fence contract cases
/// (review P0): Indeterminate fencing and reconciliation-claim ordering against
/// the durable backend.
/// </summary>
[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentToolPreDispatchOwnershipFenceTests : IAsyncLifetime
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;
    private PostgreSqlRuntimeSchemaLease _lease = null!;
    private ServiceProvider _provider = null!;
    private AgentToolPreDispatchOwnershipFenceContext _ctx = null!;

    public PostgreSqlAgentToolPreDispatchOwnershipFenceTests(PostgreSqlRuntimeCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _lease = await _fixture.CreateSchemaLeaseAsync();
        _provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(_lease.Options)
            .BuildServiceProvider();

        var gate = _provider.GetRequiredService<IAgentToolInvocationGate>();
        var budgetGate = _provider.GetRequiredService<IAgentToolBudgetGate>();
        var auditor = _provider.GetRequiredService<IAgentToolGovernanceAuditor>();
        var store = _provider.GetRequiredService<IAgentToolPreDispatchReconciliationStore>();

        _ctx = new AgentToolPreDispatchOwnershipFenceContext
        {
            Gate = gate,
            BudgetGate = budgetGate,
            Auditor = auditor,
            Reconciler = new DefaultAgentToolPreDispatchReconciler(gate, budgetGate, auditor, store),
            ReconciliationStore = store,
            ExpireLeaseAsync = (identity, lease) => ExpireLeaseAsyncCore(identity, lease)
        };
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _lease.DisposeAsync();
    }

    private async ValueTask ExpireLeaseAsyncCore(
        AgentToolPreDispatchIdentity identity,
        AgentToolInvocationLease lease)
    {
        await using var connection = new NpgsqlConnection(_lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            update {_lease.Options.Schema}.agent_tool_invocation_pre_dispatch
            set expires_at = clock_timestamp() - interval '1 second'
            where tenant_id = @tenantId and lease_id = @leaseId
            """;
        command.Parameters.AddWithValue("tenantId", identity.LogicalInvocationKey.TenantId!);
        command.Parameters.AddWithValue("leaseId", lease.LeaseId);
        var affected = await command.ExecuteNonQueryAsync();
        Assert.Equal(1, affected);
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
}
