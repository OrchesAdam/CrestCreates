namespace CrestCreates.Agent.Tools;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

public sealed record AgentToolOutputPreflightReceipt
{
    public required string ToolDescriptorId { get; init; }
    public required int ToolDescriptorVersion { get; init; }
    public required string OutputContractFingerprint { get; init; }
    public required string StructuredOutputHash { get; init; }
}

public sealed record AgentToolPreparedOutcomeReceipt
{
    public required string OutcomeCode { get; init; }
    public required AgentToolOutputPreflightReceipt Receipt { get; init; }
    public IReadOnlyList<AgentToolAuditFact> InternalFacts { get; init; } = Array.Empty<AgentToolAuditFact>();
}

public sealed record AgentToolPreparedOutput<TOutput>
{
    public required TOutput Output { get; init; }
    public required JsonElement StructuredOutput { get; init; }
    public IReadOnlyList<AgentToolAuditFact> ProjectedOutputFacts { get; init; } = Array.Empty<AgentToolAuditFact>();
    public required AgentToolOutputPreflightReceipt Receipt { get; init; }
}

public interface IAgentToolOutputPreflight<TOutput>
{
    AgentToolPreparedOutput<TOutput> Prepare(TOutput output);
}

/// <summary>
/// Invoker-owned exact preflight bridge. Handlers receive only this operation
/// through the Capability context; schema and binding configuration remain
/// outside the Memory Tool module.
/// </summary>
public interface IAgentToolOutputPreflightRuntime
{
    AgentToolPreparedOutput<TOutput> Prepare<TOutput>(TOutput output, JsonTypeInfo<TOutput> typeInfo);
}

public interface IAgentToolOutputPreflightReceiptSink
{
    bool HasPublishedOutcomes { get; }
    void PublishAllowedOutcomes(IReadOnlyList<AgentToolPreparedOutcomeReceipt> outcomes);
}
