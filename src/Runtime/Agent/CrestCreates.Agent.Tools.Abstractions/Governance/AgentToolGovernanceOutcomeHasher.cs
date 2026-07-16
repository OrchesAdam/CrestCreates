using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Produces a data-minimizing integrity digest used to confirm a governance
/// finalization. It lets durable auditors omit the full structured output while
/// still proving that a queried terminal record belongs to the same outcome;
/// ordinary SHA-256 does not provide confidentiality against offline guessing.
/// </summary>
public static class AgentToolGovernanceOutcomeHasher
{
    public static string Compute(AgentToolInvocationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("shapeVersion", "agent-tool-outcome-v1");
            writer.WriteNumber("kind", (int)outcome.Kind);
            writer.WriteString("code", outcome.Code);
            writer.WriteString("message", outcome.Message);
            writer.WritePropertyName("structuredOutput");
            WriteCanonicalValue(writer, outcome.StructuredOutput);
            writer.WritePropertyName("issues");
            writer.WriteStartArray();
            foreach (var issue in outcome.Issues)
            {
                writer.WriteStartObject();
                writer.WriteString("code", issue.Code);
                if (issue.FieldPath is null)
                    writer.WriteNull("fieldPath");
                else
                    writer.WriteString("fieldPath", issue.FieldPath);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void WriteCanonicalValue(Utf8JsonWriter writer, JsonElement? value)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        WriteCanonicalValue(writer, value.Value);
    }

    private static void WriteCanonicalValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalValue(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonicalValue(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
