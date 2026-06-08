namespace CrestCreates.Schema.Abstractions;

public sealed class SchemaValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<SchemaValidationError> Errors { get; init; } = Array.Empty<SchemaValidationError>();

    public static SchemaValidationResult Success()
        => new() { IsValid = true };

    public static SchemaValidationResult Failure(IReadOnlyList<SchemaValidationError> errors)
        => new() { IsValid = false, Errors = errors };
}
