namespace CrestCreates.JsonContracts.BuildTasks.Diagnostics;

using Microsoft.CodeAnalysis;

public enum JsonContractDiagnosticSeverity
{
    Warning,
    Error
}

public sealed class JsonContractDiagnostic
{
    public string Id { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public JsonContractDiagnosticSeverity Severity { get; init; } = JsonContractDiagnosticSeverity.Error;
    public string FilePath { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
    public string ContextMetadataName { get; init; } = string.Empty;
    public string SurfaceMetadataName { get; init; } = string.Empty;
    public string MethodSignature { get; init; } = string.Empty;
    public string ParameterName { get; init; } = string.Empty;
    public string OffendingType { get; init; } = string.Empty;

    public JsonContractDiagnostic WithLocation(Location? location)
    {
        if (location is null)
            return this;

        var lineSpan = location.GetLineSpan();
        return new JsonContractDiagnostic
        {
            Id = Id,
            Message = Message,
            Severity = Severity,
            FilePath = lineSpan.Path ?? string.Empty,
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1,
            ContextMetadataName = ContextMetadataName,
            SurfaceMetadataName = SurfaceMetadataName,
            MethodSignature = MethodSignature,
            ParameterName = ParameterName,
            OffendingType = OffendingType,
        };
    }
}
