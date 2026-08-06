using CrestCreates.Agent.Tools;
using CrestCreates.Agent.Tools.Persistence.Testing.Assertions;
using CrestCreates.Agent.Tools.Persistence.Testing.Drivers;

namespace CrestCreates.Agent.Tools.Persistence.Testing.Cases;

/// <summary>
/// Shared semantic contract cases for Agent Tool pre-dispatch governance
/// (Auditor, Budget, Comparator). Activated by concrete runners in Slice 2+.
/// </summary>
public static class AgentToolPreDispatchContractCases
{
    public static async Task H01_IdenticalPreDispatchRetryShouldReturnSameAuditId(
        IAgentToolPreDispatchContractDriver driver,
        AgentToolGovernancePreDispatchRecord record,
        CancellationToken cancellationToken)
    {
        var first = await driver.Auditor.RecordPreDispatchAsync(record, cancellationToken);
        var second = await driver.Auditor.RecordPreDispatchAsync(record, cancellationToken);

        AgentToolPreDispatchContractAssertions.True(
            first.Status == AgentToolGovernancePreDispatchWriteStatus.Accepted,
            $"First write should be Accepted, got {first.Status}.");

        AgentToolPreDispatchContractAssertions.True(
            second.Status == AgentToolGovernancePreDispatchWriteStatus.Duplicate,
            $"Second write should be Duplicate, got {second.Status}.");

        AgentToolPreDispatchContractAssertions.Equal(
            first.Receipt!.AuditId,
            second.Receipt!.AuditId,
            "Duplicate retry should return the same AuditId.");
    }

    public static async Task B02_DifferentAttemptShouldNotBeTreatedAsDuplicate(
        IAgentToolPreDispatchContractDriver driver,
        AgentToolGovernancePreDispatchRecord firstRecord,
        AgentToolGovernancePreDispatchRecord secondRecord,
        CancellationToken cancellationToken)
    {
        var first = await driver.Auditor.RecordPreDispatchAsync(firstRecord, cancellationToken);
        var second = await driver.Auditor.RecordPreDispatchAsync(secondRecord, cancellationToken);

        AgentToolPreDispatchContractAssertions.True(
            first.Status == AgentToolGovernancePreDispatchWriteStatus.Accepted,
            $"First write should be Accepted, got {first.Status}.");

        AgentToolPreDispatchContractAssertions.True(
            second.Status == AgentToolGovernancePreDispatchWriteStatus.Accepted,
            $"Second write should be Accepted, got {second.Status}.");

        AgentToolPreDispatchContractAssertions.True(
            first.Receipt!.AuditId != second.Receipt!.AuditId,
            "Different Attempt should produce different AuditId.");
    }
}
