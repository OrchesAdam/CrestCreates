using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Accountability;

/// <summary>
/// Bounded, safe diagnostic codes emitted by the Agent Memory Accountability
/// write bridge. These are the only diagnostics a producer may log; they never
/// carry payload JSON, memory/source/candidate content, exception messages,
/// reasons, explanations, trace attributes, or diagnostic messages.
/// </summary>
public static class AgentMemoryAccountabilityDiagnosticCodes
{
    private const string CompositionInvalidValue = "AGENT_MEMORY_ACCOUNTABILITY_COMPOSITION_INVALID";
    public static DiagnosticCode CompositionInvalid { get; } = new(CompositionInvalidValue);

    private const string ProducerContractInvalidValue = "AGENT_MEMORY_ACCOUNTABILITY_PRODUCER_CONTRACT_INVALID";
    public static DiagnosticCode ProducerContractInvalid { get; } = new(ProducerContractInvalidValue);

    private const string RecordedValue = "AGENT_MEMORY_ACCOUNTABILITY_RECORDED";
    public static DiagnosticCode Recorded { get; } = new(RecordedValue);

    private const string DuplicateValue = "AGENT_MEMORY_ACCOUNTABILITY_DUPLICATE";
    public static DiagnosticCode Duplicate { get; } = new(DuplicateValue);

    private const string ConflictValue = "AGENT_MEMORY_ACCOUNTABILITY_CONFLICT";
    public static DiagnosticCode Conflict { get; } = new(ConflictValue);

    private const string RecorderRejectedValue = "AGENT_MEMORY_ACCOUNTABILITY_RECORDER_REJECTED";
    public static DiagnosticCode RecorderRejected { get; } = new(RecorderRejectedValue);

    private const string NoSinkValue = "AGENT_MEMORY_ACCOUNTABILITY_NO_SINK";
    public static DiagnosticCode NoSink { get; } = new(NoSinkValue);

    private const string SinkFailedValue = "AGENT_MEMORY_ACCOUNTABILITY_SINK_FAILED";
    public static DiagnosticCode SinkFailed { get; } = new(SinkFailedValue);

    private const string TimeoutValue = "AGENT_MEMORY_ACCOUNTABILITY_TIMEOUT";
    public static DiagnosticCode Timeout { get; } = new(TimeoutValue);

    private const string RecorderFailedValue = "AGENT_MEMORY_ACCOUNTABILITY_RECORDER_FAILED";
    public static DiagnosticCode RecorderFailed { get; } = new(RecorderFailedValue);
}
