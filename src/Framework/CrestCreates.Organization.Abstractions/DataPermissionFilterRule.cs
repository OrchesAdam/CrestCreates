namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionFilterRule
{
    public required string FieldName { get; init; }
    public DataPermissionFilterOperator Operator { get; init; }
    public string? Value { get; init; }
    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();
}
