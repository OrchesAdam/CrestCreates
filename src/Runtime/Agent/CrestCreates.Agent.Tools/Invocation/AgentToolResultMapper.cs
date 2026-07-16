using CrestCreates.Capability.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolResultMapper
{
    public AgentToolInvocationOutcome CapabilityFailure(CapabilityExecutionResult result)
    {
        var issues = string.Equals(
                result.ErrorCode,
                CapabilityExecutionErrorCodes.ValidationFailed,
                StringComparison.Ordinal)
            ? result.Issues
                .Where(issue => IsSafeValidationCode(issue.Code))
                .Select(issue => new AgentToolInvocationIssue(issue.Code, issue.FieldName))
                .ToArray()
            : [];

        return new()
        {
            Kind = AgentToolInvocationOutcomeKind.CapabilityFailure,
            Code = result.ErrorCode ?? "AGENT_TOOL_CAPABILITY_FAILURE",
            Message = "The requested operation could not be completed.",
            Issues = issues
        };
    }

    private static bool IsSafeValidationCode(string code)
        => code == SchemaValidationErrorCodes.FieldRequired.ToString()
            || code == SchemaValidationErrorCodes.NullNotAllowed.ToString()
            || code == SchemaValidationErrorCodes.TypeMismatch.ToString()
            || code == SchemaValidationErrorCodes.MaxLengthExceeded.ToString()
            || code == SchemaValidationErrorCodes.MinLengthNotMet.ToString()
            || code == SchemaValidationErrorCodes.PatternMismatch.ToString()
            || code == SchemaValidationErrorCodes.MaxValueExceeded.ToString()
            || code == SchemaValidationErrorCodes.MinValueNotMet.ToString()
            || code == SchemaValidationErrorCodes.UnknownProperty.ToString()
            || code == SchemaValidationErrorCodes.DuplicateProperty.ToString();

    public static AgentToolInvocationOutcome Outcome(
        AgentToolInvocationOutcomeKind kind,
        string code,
        string message)
        => new() { Kind = kind, Code = code, Message = message };
}
