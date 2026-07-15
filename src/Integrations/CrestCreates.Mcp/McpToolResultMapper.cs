using CrestCreates.Capability.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Mcp;

public sealed class McpToolResultMapper
{
    public McpToolInvocationOutcome MapFailure(CapabilityExecutionResult result)
    {
        var messages = string.Equals(
                result.ErrorCode,
                CapabilityExecutionErrorCodes.ValidationFailed,
                StringComparison.Ordinal)
            ? result.Issues
                .Where(issue => IsSafeValidationCode(issue.Code))
                .Select(FormatValidationIssue)
                .ToArray()
            : [];
        var message = messages.Length == 0
            ? "The operation could not be completed."
            : string.Join(" ", messages);
        return new(
            true,
            [new McpToolTextContent(message)],
            StructuredContent: null,
            ErrorCode: result.ErrorCode ?? StatusCode(result.Status));
    }

    public McpToolInvocationOutcome MapInputError(string code, string safeMessage)
        => new(true, [new McpToolTextContent(safeMessage)], null, code);

    public McpToolInvocationOutcome MapInputValidationError(IReadOnlyList<SchemaValidationError> errors)
    {
        var issues = errors
            .Select(error => new CapabilityExecutionIssue(
                error.ErrorCode.ToString(),
                error.Message,
                error.FieldName))
            .ToArray();
        var messages = issues
            .Where(issue => IsSafeValidationCode(issue.Code))
            .Select(FormatValidationIssue)
            .ToArray();
        var text = messages.Length == 0
            ? "Tool arguments are invalid."
            : string.Join(" ", messages);
        return new(true, [new McpToolTextContent(text)], null, "INVALID_ARGUMENTS");
    }

    public McpToolInvocationOutcome MapVoidSuccess()
        => new(false, [new McpToolTextContent("Operation completed successfully.")]);

    public McpToolInvocationOutcome MapStructuredSuccess(System.Text.Json.JsonElement output)
        => new(
            false,
            [new McpToolTextContent(output.GetRawText())],
            output);

    private static string StatusCode(CapabilityExecutionStatus status) => status switch
    {
        CapabilityExecutionStatus.TimedOut => "TIMEOUT",
        _ => "CAPABILITY_FAILED"
    };

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

    private static string FormatValidationIssue(CapabilityExecutionIssue issue)
    {
        var field = string.IsNullOrWhiteSpace(issue.FieldName) ? "input" : issue.FieldName;
        var detail = issue.Code == SchemaValidationErrorCodes.FieldRequired.ToString() ? "required"
            : issue.Code == SchemaValidationErrorCodes.NullNotAllowed.ToString() ? "must not be null"
            : issue.Code == SchemaValidationErrorCodes.TypeMismatch.ToString() ? "has an invalid type"
            : issue.Code == SchemaValidationErrorCodes.MaxLengthExceeded.ToString() ? "is too long"
            : issue.Code == SchemaValidationErrorCodes.MinLengthNotMet.ToString() ? "is too short"
            : issue.Code == SchemaValidationErrorCodes.PatternMismatch.ToString() ? "has an invalid format"
            : issue.Code == SchemaValidationErrorCodes.MaxValueExceeded.ToString() ? "is above the allowed maximum"
            : issue.Code == SchemaValidationErrorCodes.MinValueNotMet.ToString() ? "is below the allowed minimum"
            : issue.Code == SchemaValidationErrorCodes.UnknownProperty.ToString() ? "is not recognized"
            : issue.Code == SchemaValidationErrorCodes.DuplicateProperty.ToString() ? "is duplicated"
            : "is invalid";
        return $"Field '{field}': {detail}.";
    }
}
