namespace CrestCreates.Schema.Abstractions;

public sealed class SchemaValidationError
{
    public string FieldName { get; init; } = string.Empty;
    public string ErrorCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
