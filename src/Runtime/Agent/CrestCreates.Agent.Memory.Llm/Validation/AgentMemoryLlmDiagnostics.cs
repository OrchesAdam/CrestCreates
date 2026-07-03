using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Llm.Validation;

public static class AgentMemoryLlmDiagnostics
{
    public static AgentMemoryDiagnostic Create(
        DiagnosticCode code,
        string message,
        SeverityLevel severity,
        IReadOnlyList<AgentContextSourceRef>? sourceRefs = null)
    {
        return new AgentMemoryDiagnostic
        {
            Code = code,
            Message = message,
            Severity = severity,
            SourceRefs = sourceRefs ?? Array.Empty<AgentContextSourceRef>()
        };
    }

    public static AgentMemoryDiagnostic Create(
        DiagnosticCode code,
        string message,
        IReadOnlyList<AgentContextSourceRef>? sourceRefs = null)
    {
        return Create(code, message, SeverityLevel.Info, sourceRefs);
    }
}
