namespace CrestCreates.Form.Abstractions;

public sealed class FormFieldDescriptor
{
    // Existing (unchanged)
    public string SchemaFieldName { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? Placeholder { get; init; }
    public string? HelpText { get; init; }
    public string? FormatHint { get; init; }
    public int Order { get; init; }
    public string? Group { get; init; }
    public bool IsReadOnly { get; init; }
    public string? VisibilityCondition { get; init; }

    // New — Phase 5g interaction metadata
    public string? ControlType { get; init; }
    public bool? IsRequiredOverride { get; init; }
    public string? ValidationMessage { get; init; }
    public string? DefaultValueExpression { get; init; }
    public string? OptionsSource { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}
