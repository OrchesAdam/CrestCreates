using System.Text.Json;

namespace CrestCreates.Core.Abstractions.Identity;

public sealed class SeverityLevelJsonConverter : SemanticStringJsonConverter<SeverityLevel>
{
    protected override SeverityLevel Create(string value)
        => value switch
        {
            "Error" => SeverityLevel.Error,
            "Warning" => SeverityLevel.Warning,
            "Info" => SeverityLevel.Info,
            "Blocker" => SeverityLevel.Blocker,
            "Review" => SeverityLevel.Review,
            _ => throw new JsonException($"Unsupported severity level '{value}'.")
        };

    protected override string GetRequiredValue(SeverityLevel value) => value.RequireValue();
}
