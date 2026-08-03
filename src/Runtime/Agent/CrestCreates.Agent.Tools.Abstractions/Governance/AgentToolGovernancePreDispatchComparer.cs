using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// The sole semantic authority for complete checkpoint equality and identity
/// validation. InMemory, PostgreSQL, Invoker recovery, reconciler, and tests
/// reuse this comparator. Provider SQL does not invent a narrower equality
/// definition.
///
/// Comparison includes every dispatch-authorizing fact (INV-04):
/// - full logical invocation identity and AttemptId;
/// - invocation fingerprint, arguments-evaluated flag/hash, call origin, roles hash;
/// - Tool, Capability, InputSchema, OutputSchema contract identities;
/// - complete effective governance;
/// - LeaseId, AttemptId, FencingToken, AcquiredAt, ExpiresAt;
/// - approval decision, claim state, EvidenceId, approver-safe reference, stable reason;
/// - ReservationId, AttemptId, fingerprint, category, units, limit, state.
/// </summary>
public static class AgentToolGovernancePreDispatchComparer
{
    /// <summary>
    /// Compares two complete pre-dispatch checkpoints for semantic equality.
    /// Returns true only when every dispatch-authorizing fact matches.
    /// </summary>
    public static bool Equivalent(
        AgentToolGovernancePreDispatchRecord left,
        AgentToolGovernancePreDispatchRecord right)
    {
        return ContextsEqual(left.Context, right.Context)
            && LeasesEqual(left.Lease, right.Lease)
            && ApprovalsEqual(left.Approval, right.Approval)
            && BudgetReservationsEqual(left.BudgetReservation, right.BudgetReservation);
    }

    /// <summary>
    /// Validates that the record's identity fields match the expected
    /// <see cref="AgentToolPreDispatchIdentity"/>. String comparison is ordinal.
    /// </summary>
    public static bool ValidateIdentity(
        AgentToolGovernancePreDispatchRecord record,
        AgentToolPreDispatchIdentity identity)
    {
        if (string.IsNullOrEmpty(identity.AttemptId))
            return false;

        if (!string.Equals(
                identity.AttemptId,
                record.Context.AttemptId,
                StringComparison.Ordinal))
            return false;

        if (!LogicalKeysEqual(
                identity.LogicalInvocationKey,
                record.Context.LogicalInvocationKey))
            return false;

        if (!string.Equals(
                identity.AttemptId,
                record.Lease.AttemptId,
                StringComparison.Ordinal))
            return false;

        if (!string.Equals(
                identity.AttemptId,
                record.BudgetReservation.AttemptId,
                StringComparison.Ordinal))
            return false;

        return true;
    }

    private static bool ContextsEqual(
        AgentToolGovernanceAuditContext left,
        AgentToolGovernanceAuditContext right)
    {
        if (!LogicalKeysEqual(left.LogicalInvocationKey, right.LogicalInvocationKey))
            return false;

        if (!string.Equals(left.AttemptId, right.AttemptId, StringComparison.Ordinal))
            return false;

        if (!string.Equals(left.InvocationFingerprint, right.InvocationFingerprint, StringComparison.Ordinal))
            return false;

        if (!string.Equals(left.ArgumentsHash, right.ArgumentsHash, StringComparison.Ordinal))
            return false;

        if (left.ArgumentsEvaluated != right.ArgumentsEvaluated)
            return false;

        if (left.CallOrigin != right.CallOrigin)
            return false;

        if (!string.Equals(left.AgentRolesHash, right.AgentRolesHash, StringComparison.Ordinal))
            return false;

        if (!ContractIdentitiesEqual(left.ToolContract, right.ToolContract))
            return false;

        if (!ContractIdentitiesEqual(left.CapabilityContract, right.CapabilityContract))
            return false;

        if (!SchemaContractsEqual(left.InputSchemaContract, right.InputSchemaContract))
            return false;

        if (!SchemaContractsEqual(left.OutputSchemaContract, right.OutputSchemaContract))
            return false;

        if (!EffectiveGovernanceEqual(left.Governance, right.Governance))
            return false;

        return true;
    }

    private static bool LeasesEqual(
        AgentToolInvocationLease left,
        AgentToolInvocationLease right)
    {
        if (!string.Equals(left.AttemptId, right.AttemptId, StringComparison.Ordinal))
            return false;

        if (!string.Equals(left.LeaseId, right.LeaseId, StringComparison.Ordinal))
            return false;

        if (left.FencingToken != right.FencingToken)
            return false;

        if (left.AcquiredAt != right.AcquiredAt)
            return false;

        if (left.ExpiresAt != right.ExpiresAt)
            return false;

        return true;
    }

    private static bool ApprovalsEqual(
        AgentToolApprovalResult left,
        AgentToolApprovalResult right)
    {
        if (left.Decision != right.Decision)
            return false;

        if (left.ClaimState != right.ClaimState)
            return false;

        if (!string.Equals(left.EvidenceId, right.EvidenceId, StringComparison.Ordinal))
            return false;

        if (!string.Equals(left.ApproverReference, right.ApproverReference, StringComparison.Ordinal))
            return false;

        if (!string.Equals(left.ReasonCode, right.ReasonCode, StringComparison.Ordinal))
            return false;

        return true;
    }

    private static bool BudgetReservationsEqual(
        AgentToolBudgetReservation left,
        AgentToolBudgetReservation right)
    {
        if (!string.Equals(left.ReservationId, right.ReservationId, StringComparison.Ordinal))
            return false;

        if (!string.Equals(left.AttemptId, right.AttemptId, StringComparison.Ordinal))
            return false;

        if (!string.Equals(left.InvocationFingerprint, right.InvocationFingerprint, StringComparison.Ordinal))
            return false;

        if (!string.Equals(left.Category, right.Category, StringComparison.Ordinal))
            return false;

        if (left.CostUnits != right.CostUnits)
            return false;

        if (left.MaxCallsPerExecution != right.MaxCallsPerExecution)
            return false;

        if (left.State != right.State)
            return false;

        return true;
    }

    private static bool LogicalKeysEqual(
        AgentToolLogicalInvocationKey left,
        AgentToolLogicalInvocationKey right)
    {
        if (!string.Equals(left.TenantId, right.TenantId, StringComparison.Ordinal))
            return false;

        if (!string.Equals(left.UserId, right.UserId, StringComparison.Ordinal))
            return false;

        if (!string.Equals(left.AgentId, right.AgentId, StringComparison.Ordinal))
            return false;

        if (!string.Equals(left.ExecutionId, right.ExecutionId, StringComparison.Ordinal))
            return false;

        if (!string.Equals(left.InvocationId, right.InvocationId, StringComparison.Ordinal))
            return false;

        return true;
    }

    private static bool ContractIdentitiesEqual(
        AgentToolContractIdentity left,
        AgentToolContractIdentity right)
    {
        if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal))
            return false;

        if (left.Version != right.Version)
            return false;

        if (!string.Equals(left.ContractHash, right.ContractHash, StringComparison.Ordinal))
            return false;

        return true;
    }

    private static bool SchemaContractsEqual(
        AgentToolSchemaContractIdentity? left,
        AgentToolSchemaContractIdentity? right)
    {
        if (left is null && right is null)
            return true;

        if (left is null || right is null)
            return false;

        if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal))
            return false;

        if (left.Version != right.Version)
            return false;

        if (!string.Equals(left.ContractHash, right.ContractHash, StringComparison.Ordinal))
            return false;

        return true;
    }

    private static bool EffectiveGovernanceEqual(
        AgentToolEffectiveGovernance left,
        AgentToolEffectiveGovernance right)
    {
        if (left.SelectionPolicy != right.SelectionPolicy)
            return false;

        if (left.SideEffectKind != right.SideEffectKind)
            return false;

        if (left.EffectiveRisk != right.EffectiveRisk)
            return false;

        if (left.EffectiveApprovalMode != right.EffectiveApprovalMode)
            return false;

        if (!BudgetRequirementsEqual(left.Budget, right.Budget))
            return false;

        if (left.EffectiveAuditMode != right.EffectiveAuditMode)
            return false;

        return true;
    }

    private static bool BudgetRequirementsEqual(
        AgentToolBudgetRequirement left,
        AgentToolBudgetRequirement right)
    {
        if (!string.Equals(left.Category, right.Category, StringComparison.Ordinal))
            return false;

        if (left.CostUnits != right.CostUnits)
            return false;

        if (left.MaxCallsPerExecution != right.MaxCallsPerExecution)
            return false;

        return true;
    }
}
