using CrestCreates.Agent.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools.Tests.Governance;

internal static class GovernanceTestData
{
    public static AgentToolGovernanceContext Context(
        string attemptId = "attempt-1",
        string fingerprint = "fingerprint-1",
        string invocationId = "invocation-1",
        AgentToolApprovalMode approvalMode = AgentToolApprovalMode.Required,
        CapabilityRiskLevel risk = CapabilityRiskLevel.Low,
        AgentToolSideEffectKind sideEffect = AgentToolSideEffectKind.ReadOnly,
        AgentToolAuditMode auditMode = AgentToolAuditMode.Required,
        int? maxCalls = 1,
        string category = "agent-read")
    {
        var key = new AgentToolLogicalInvocationKey(
            "tenant-1",
            "user-1",
            "agent-1",
            "execution-1",
            invocationId);

        return new AgentToolGovernanceContext
        {
            LogicalInvocationKey = key,
            AttemptId = attemptId,
            InvocationFingerprint = fingerprint,
            ArgumentsHash = "arguments-hash-1",
            ExecutionContext = new AgentExecutionContext
            {
                ExecutionId = key.ExecutionId,
                InvocationId = key.InvocationId,
                AgentId = key.AgentId,
                AgentRoles = new HashSet<string>(["sales-agent"], StringComparer.Ordinal),
                CallOrigin = AgentToolCallOrigin.ExplicitRequest
            },
            ToolContract = new AgentToolContractIdentity("tool-1", 1, "tool-contract-hash"),
            CapabilityContract = new AgentToolContractIdentity(
                "capability-1",
                1,
                "capability-contract-hash"),
            Governance = new AgentToolEffectiveGovernance(
                AgentToolSelectionPolicy.ExplicitOnly,
                sideEffect,
                risk,
                approvalMode,
                new AgentToolBudgetRequirement
                {
                    Category = category,
                    CostUnits = 2,
                    MaxCallsPerExecution = maxCalls
                },
                auditMode)
        };
    }

    public static AgentToolGovernanceAuditContext AuditContext(
        AgentToolGovernanceContext context)
        => new()
        {
            LogicalInvocationKey = context.LogicalInvocationKey,
            AttemptId = context.AttemptId,
            InvocationFingerprint = context.InvocationFingerprint,
            ArgumentsHash = context.ArgumentsHash,
            CallOrigin = context.ExecutionContext.CallOrigin,
            AgentRolesHash = "roles-hash-1",
            ToolContract = context.ToolContract,
            CapabilityContract = context.CapabilityContract,
            InputSchemaContract = context.InputSchemaContract,
            OutputSchemaContract = context.OutputSchemaContract,
            Governance = context.Governance
        };

    public static AgentToolInvocationLease Lease(string attemptId = "attempt-1")
        => new()
        {
            AttemptId = attemptId,
            LeaseId = $"lease-{attemptId}",
            FencingToken = 1,
            AcquiredAt = new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 7, 16, 0, 5, 0, TimeSpan.Zero)
        };

    public static AgentToolBudgetReservation Reservation(
        AgentToolGovernanceContext context,
        AgentToolBudgetReservationState state = AgentToolBudgetReservationState.Reserved)
        => new()
        {
            ReservationId = $"reservation-{context.AttemptId}",
            AttemptId = context.AttemptId,
            InvocationFingerprint = context.InvocationFingerprint,
            Category = context.Governance.Budget.Category,
            CostUnits = context.Governance.Budget.CostUnits,
            MaxCallsPerExecution = context.Governance.Budget.MaxCallsPerExecution,
            State = state
        };

    public static AgentToolGovernancePreDispatchRecord PreDispatch(
        AgentToolGovernanceContext context)
        => new()
        {
            Context = AuditContext(context),
            Lease = Lease(context.AttemptId),
            Approval = new AgentToolApprovalResult
            {
                Decision = AgentToolApprovalDecision.Approved,
                ClaimState = AgentToolApprovalEvidenceClaimState.Claimed,
                EvidenceId = "evidence-1"
            },
            BudgetReservation = Reservation(context)
        };

    internal sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
            => _utcNow;

        public void Advance(TimeSpan duration)
            => _utcNow = _utcNow.Add(duration);
    }
}
