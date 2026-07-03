using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Llm.Validation;

public static class AgentMemoryLlmOutputValidators
{
    public static AgentMemoryConfidence CapConfidence(
        AgentMemoryConfidence confidence,
        AgentMemoryConfidence maxConfidence,
        List<AgentMemoryDiagnostic> diagnostics)
    {
        if ((int)confidence > (int)maxConfidence)
        {
            diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                AgentMemoryLlmDiagnosticCodes.CandidateConfidenceCapped,
                $"Confidence {confidence} capped to {maxConfidence}.",
                SeverityLevel.Warning));
            return maxConfidence;
        }

        return confidence;
    }

    public static bool EnforceNonAuthoritativeOutput(
        string? status,
        bool? isAuthoritative,
        List<AgentMemoryDiagnostic> diagnostics)
    {
        var violated = false;

        if (!string.IsNullOrEmpty(status) && !string.Equals(status, "Candidate", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                AgentMemoryLlmDiagnosticCodes.NonAuthoritativeOutputEnforced,
                $"Provider output status '{status}' rejected; enforced to Candidate.",
                SeverityLevel.Warning));
            violated = true;
        }

        if (isAuthoritative == true)
        {
            diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                AgentMemoryLlmDiagnosticCodes.NonAuthoritativeOutputEnforced,
                "Provider output authoritative flag rejected; enforced to non-authoritative.",
                SeverityLevel.Warning));
            violated = true;
        }

        return violated;
    }

    public static void ValidateSourceRefs(
        IReadOnlyList<string> sourceRefIds,
        IReadOnlySet<string> allowedSourceRefIds,
        List<AgentMemoryDiagnostic> diagnostics)
    {
        foreach (var refId in sourceRefIds)
        {
            if (!allowedSourceRefIds.Contains(refId))
            {
                diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                    AgentMemoryLlmDiagnosticCodes.InvalidSourceRef,
                    $"Source ref '{refId}' not found in allowed source refs.",
                    SeverityLevel.Warning));
            }
        }
    }

    public static void AddProviderFailureDiagnostics(
        AgentMemoryLlmModelResponse response,
        List<AgentMemoryDiagnostic> diagnostics)
    {
        if (response.FailureKind is null)
            return;

        var code = response.FailureKind.Value switch
        {
            AgentMemoryLlmProviderFailureKind.CredentialUnavailable => AgentMemoryLlmDiagnosticCodes.CredentialUnavailable,
            AgentMemoryLlmProviderFailureKind.Unauthorized => AgentMemoryLlmDiagnosticCodes.Unauthorized,
            AgentMemoryLlmProviderFailureKind.RateLimited => AgentMemoryLlmDiagnosticCodes.RateLimited,
            AgentMemoryLlmProviderFailureKind.Timeout => AgentMemoryLlmDiagnosticCodes.Timeout,
            AgentMemoryLlmProviderFailureKind.NetworkError => AgentMemoryLlmDiagnosticCodes.NetworkError,
            AgentMemoryLlmProviderFailureKind.ProviderUnavailable => AgentMemoryLlmDiagnosticCodes.ProviderUnavailable,
            AgentMemoryLlmProviderFailureKind.ParseFailed => AgentMemoryLlmDiagnosticCodes.ParseFailed,
            AgentMemoryLlmProviderFailureKind.ValidationFailed => AgentMemoryLlmDiagnosticCodes.ParseFailed,
            _ => AgentMemoryLlmDiagnosticCodes.ProviderUnavailable
        };

        diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
            code,
            $"Provider failure: {response.FailureKind.Value}. {response.FailureDetail}",
            SeverityLevel.Error));
    }
}
