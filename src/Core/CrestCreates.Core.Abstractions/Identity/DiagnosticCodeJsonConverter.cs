namespace CrestCreates.Core.Abstractions.Identity;

public sealed class DiagnosticCodeJsonConverter : SemanticStringJsonConverter<DiagnosticCode>
{
    protected override DiagnosticCode Create(string value) => new(value);

    protected override string GetRequiredValue(DiagnosticCode value) => value.RequireValue();
}
