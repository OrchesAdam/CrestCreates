using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Agent.Abstractions;

namespace CrestCreates.Agent.Tools;

public sealed class AgentCapabilityIdempotencyKeyBuilder
{
    public string Build(AgentToolRuntimeEntry entry, AgentExecutionContext execution)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(execution);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("shapeVersion", "agent-capability-idempotency-v1");
            writer.WriteString("toolContractHash", entry.ToolContractHash);
            writer.WriteString("capabilityContractHash", entry.CapabilityContractHash);
            WriteNullable(writer, "inputSchemaContractHash", entry.InputSchemaContractHash);
            WriteNullable(writer, "outputSchemaContractHash", entry.OutputSchemaContractHash);
            writer.WriteString("executionId", execution.ExecutionId);
            writer.WriteString("invocationId", execution.InvocationId);
            writer.WriteEndObject();
            writer.Flush();
        }

        return "agent:v1:" + Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }
}
