using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Allowlisted code-table policy for diagnostic explanations.
/// Never echoes caller-supplied diagnostic codes, messages, or paths.
/// Unknown codes return a fixed UNKNOWN_DIAGNOSTIC entry.
/// </summary>
internal sealed record DiagnosticExplanationTemplate(
    string Explanation,
    string Remediation,
    SeverityLevel Severity,
    IReadOnlyList<string> SuggestedFixToolNames);

internal sealed class AgentDiagnosticExplanationPolicy
{
    private static readonly IReadOnlyDictionary<string, DiagnosticExplanationTemplate> Templates =
        new Dictionary<string, DiagnosticExplanationTemplate>(StringComparer.Ordinal)
        {
            [DescriptorDraftDiagnosticCodes.KindPayloadMismatch] = new(
                "The declared descriptor kind and payload kind differ.",
                "Submit a payload whose typed kind matches the declared kind.",
                SeverityLevel.Error,
                [AgentToolName.SuggestDescriptorDraftFixes]),
            [DescriptorDraftDiagnosticCodes.DraftIdEmpty] = new(
                "The draft identifier must not be empty.",
                "Provide a non-empty DraftId or use auto-generation.",
                SeverityLevel.Error,
                [AgentToolName.SuggestDescriptorDraftFixes]),
            [DescriptorDraftDiagnosticCodes.DescriptorIdEmpty] = new(
                "The descriptor identifier must not be empty.",
                "Provide the descriptor identifier this draft targets.",
                SeverityLevel.Error,
                [AgentToolName.SuggestDescriptorDraftFixes]),
            [DescriptorDraftDiagnosticCodes.AuthorIdEmpty] = new(
                "The author identifier must not be empty.",
                "Provide the author identifier.",
                SeverityLevel.Error,
                [AgentToolName.SuggestDescriptorDraftFixes]),
            [DescriptorDraftDiagnosticCodes.RationaleEmpty] = new(
                "The rationale must not be empty.",
                "Provide a rationale for the draft.",
                SeverityLevel.Warning,
                []),
            [DescriptorDraftDiagnosticCodes.IntentEmpty] = new(
                "The intent must not be empty.",
                "Provide an intent for the draft.",
                SeverityLevel.Warning,
                []),
            [DescriptorDraftDiagnosticCodes.ProposedVersionMissing] = new(
                "ProposedVersion is required for Create and Update operations.",
                "Set ProposedVersion on the draft.",
                SeverityLevel.Error,
                []),
            [DescriptorDraftDiagnosticCodes.ProposedVersionNotInteger] = new(
                "ProposedVersion must be a valid integer.",
                "Set ProposedVersion to an integer string.",
                SeverityLevel.Error,
                []),
            [DescriptorDraftDiagnosticCodes.ProposedVersionMismatch] = new(
                "ProposedVersion does not match the payload descriptor version.",
                "Ensure ProposedVersion matches the payload descriptor version.",
                SeverityLevel.Error,
                []),
            [DescriptorDraftDiagnosticCodes.CreateBaseVersionMustBeEmpty] = new(
                "Create operation must not specify BaseVersion.",
                "Remove BaseVersion for Create operations.",
                SeverityLevel.Error,
                []),
            [DescriptorDraftDiagnosticCodes.UpdateBaseVersionRequired] = new(
                "Update operation requires BaseVersion.",
                "Set BaseVersion for Update operations.",
                SeverityLevel.Error,
                []),
            [DescriptorDraftDiagnosticCodes.PayloadIdMismatch] = new(
                "The Payload descriptor Id does not match the draft DescriptorId.",
                "Ensure the Payload descriptor Id matches the draft DescriptorId.",
                SeverityLevel.Error,
                [])
        };

    /// <summary>
    /// Returns an explanation entry for the given diagnostic code.
    /// Unknown codes return a fixed UNKNOWN_DIAGNOSTIC entry
    /// without echoing the caller-supplied code.
    /// </summary>
    public DiagnosticExplanationEntry Explain(AgentToolDiagnostic diagnostic)
    {
        if (Templates.TryGetValue(diagnostic.Code, out var template))
        {
            return new DiagnosticExplanationEntry
            {
                Code = diagnostic.Code,
                Explanation = template.Explanation,
                Remediation = template.Remediation,
                Severity = template.Severity,
                SuggestedFixToolNames = template.SuggestedFixToolNames
            };
        }

        return new DiagnosticExplanationEntry
        {
            Code = AgentToolDiagnosticCodes.UnknownDiagnostic,
            Explanation = "No explanation is available for this diagnostic code.",
            Remediation = "Verify the diagnostic code against the allowlisted code table.",
            Severity = SeverityLevel.Warning,
            SuggestedFixToolNames = []
        };
    }
}
