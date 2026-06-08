namespace CrestCreates.Form.Abstractions;

public sealed class FormFieldDescriptor
{
    public string SchemaFieldName { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? Placeholder { get; init; }
    public string? HelpText { get; init; }
    public string? FormatHint { get; init; }
    public int Order { get; init; }
    public string? Group { get; init; }
    public bool IsReadOnly { get; init; }
    public string? VisibilityCondition { get; init; }
}
