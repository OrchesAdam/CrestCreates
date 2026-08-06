using System.Text.Json.Serialization;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WorkflowInstance))]
[JsonSerializable(typeof(HumanTaskInstance))]
[JsonSerializable(typeof(WorkflowSuspensionReceipt))]
[JsonSerializable(typeof(RuntimeDescriptorPin))]
[JsonSerializable(typeof(DescriptorSnapshot))]
[JsonSerializable(typeof(AuditEnvelope))]
[JsonSerializable(typeof(AgentToolLogicalInvocationKey))]
[JsonSerializable(typeof(AgentToolContractIdentity))]
[JsonSerializable(typeof(AgentToolSchemaContractIdentity))]
[JsonSerializable(typeof(AgentToolEffectiveGovernance))]
[JsonSerializable(typeof(AgentToolInvocationLease))]
[JsonSerializable(typeof(AgentToolApprovalResult))]
[JsonSerializable(typeof(AgentToolBudgetReservation))]
[JsonSerializable(typeof(AgentToolInvocationPreDispatchIntentSnapshot))]
[JsonSerializable(typeof(AgentToolGovernancePreDispatchReceipt))]
[JsonSerializable(typeof(AgentToolPreDispatchReconciliationReceipt))]
[JsonSerializable(typeof(AgentToolPreDispatchReconciliationObservation))]
[JsonSerializable(typeof(AgentToolGovernanceAuditContext))]
[JsonSerializable(typeof(AgentToolGovernancePreDispatchRecord))]
[JsonSerializable(typeof(AgentToolBudgetRequirement))]
[JsonSerializable(typeof(AgentExecutionContext))]
[JsonSerializable(typeof(AgentToolInvocationAbandonedReceipt))]
[JsonSerializable(typeof(AgentToolInvocationPrepareCompletionRequest))]
[JsonSerializable(typeof(AgentToolInvocationPrepareReleaseRequest))]
[JsonSerializable(typeof(AgentToolInvocationOutcome))]
[JsonSerializable(typeof(AgentToolGovernanceFinalizationRecord))]
[JsonSerializable(typeof(AgentToolGovernanceDecisionRecord))]
[JsonSerializable(typeof(AgentToolBudgetReserveRequest))]
[JsonSerializable(typeof(AgentToolBudgetFinalizeRequest))]
[JsonSerializable(typeof(AgentToolInvocationAcquireRequest))]
internal sealed partial class PostgreSqlRuntimeJsonSerializerContext : JsonSerializerContext
{
}
