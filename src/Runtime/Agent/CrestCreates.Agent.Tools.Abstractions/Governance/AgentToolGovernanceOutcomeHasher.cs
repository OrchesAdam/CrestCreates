using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Produces the governance OutcomeHash v2 digest. Only safe outcome shape and
/// issue facts participate; user-facing messages and structured payload bytes
/// are intentionally excluded from the audit digest.
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
            writer.WriteString("shapeVersion", "agent-tool-governance-outcome-v2");
            writer.WriteNumber("kind", (int)outcome.Kind);
            writer.WriteString("code", outcome.Code);
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
}
