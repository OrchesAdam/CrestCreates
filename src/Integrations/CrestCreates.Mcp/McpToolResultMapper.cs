using CrestCreates.Capability.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Mcp;

public sealed class McpToolResultMapper
{
    public McpToolInvocationOutcome MapFailure(CapabilityExecutionResult result)
    {
        var messages = result.Issues
            .Where(issue => IsSafeValidationCode(issue.Code))
            .Select(FormatValidationIssue)
            .ToArray();
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
        => code is "FIELD_REQUIRED"
            or "NULL_NOT_ALLOWED"
            or "TYPE_MISMATCH"
            or "MAX_LENGTH_EXCEEDED"
            or "MIN_LENGTH_NOT_MET"
            or "PATTERN_MISMATCH"
            or "MAX_VALUE_EXCEEDED"
            or "MIN_VALUE_NOT_MET"
            or "UNKNOWN_PROPERTY"
            or "DUPLICATE_PROPERTY";

    private static string FormatValidationIssue(CapabilityExecutionIssue issue)
    {
        var field = string.IsNullOrWhiteSpace(issue.FieldName) ? "input" : issue.FieldName;
        var detail = issue.Code switch
        {
            "FIELD_REQUIRED" => "required",
            "NULL_NOT_ALLOWED" => "must not be null",
            "TYPE_MISMATCH" => "has an invalid type",
            "MAX_LENGTH_EXCEEDED" => "is too long",
            "MIN_LENGTH_NOT_MET" => "is too short",
            "PATTERN_MISMATCH" => "has an invalid format",
            "MAX_VALUE_EXCEEDED" => "is above the allowed maximum",
            "MIN_VALUE_NOT_MET" => "is below the allowed minimum",
            "UNKNOWN_PROPERTY" => "is not recognized",
            "DUPLICATE_PROPERTY" => "is duplicated",
            _ => "is invalid"
        };
        return $"Field '{field}': {detail}.";
    }
}
