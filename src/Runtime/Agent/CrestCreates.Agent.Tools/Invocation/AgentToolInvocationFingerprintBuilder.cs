using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

public sealed record AgentToolInvocationFingerprint(
    string ArgumentsHash,
    string Value);

public sealed class AgentToolInvocationFingerprintBuilder
{
    public string BuildRawArgumentsHash(JsonElement? arguments)
        => Hash(writer => WriteRawValue(writer, arguments ?? default));

    public AgentToolInvocationFingerprint Build(
        AgentToolRuntimeEntry entry,
        AgentExecutionContext execution,
        AgentToolLogicalInvocationKey key,
        JsonElement arguments)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(execution);
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Agent Tool arguments must be an object.", nameof(arguments));

        var argumentsHash = Hash(writer => WriteArguments(writer, arguments, entry.InputSchema));
        var fingerprint = Hash(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("shapeVersion", "agent-tool-invocation-v1");
            WriteIdentity(writer, "tool", entry.DiscoveryContract.ToolContract);
            WriteIdentity(writer, "capability", entry.DiscoveryContract.CapabilityContract);
            WriteSchemaIdentity(writer, "inputSchema", entry.DiscoveryContract.InputSchemaContract);
            WriteSchemaIdentity(writer, "outputSchema", entry.DiscoveryContract.OutputSchemaContract);
            writer.WriteString("argumentsHash", argumentsHash);
            WriteNullable(writer, "tenantId", key.TenantId);
            writer.WriteString("userId", key.UserId);
            writer.WriteString("agentId", key.AgentId);
            writer.WritePropertyName("agentRoles");
            writer.WriteStartArray();
            foreach (var role in execution.AgentRoles.OrderBy(role => role, StringComparer.Ordinal))
                writer.WriteStringValue(role);
            writer.WriteEndArray();
            writer.WriteString("executionId", key.ExecutionId);
            writer.WriteString("invocationId", key.InvocationId);
            writer.WriteNumber("callOrigin", (int)execution.CallOrigin);
            writer.WriteEndObject();
        });
        return new AgentToolInvocationFingerprint(argumentsHash, fingerprint);
    }

    private static void WriteArguments(
        Utf8JsonWriter writer,
        JsonElement arguments,
        SchemaDescriptor? schema)
    {
        var fields = schema?.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal)
            ?? new Dictionary<string, SchemaFieldDescriptor>(StringComparer.Ordinal);
        writer.WriteStartObject();
        foreach (var property in arguments.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            writer.WritePropertyName(property.Name);
            fields.TryGetValue(property.Name, out var field);
            WriteValue(writer, property.Value, field);
        }
        writer.WriteEndObject();
    }

    private static void WriteValue(
        Utf8JsonWriter writer,
        JsonElement value,
        SchemaFieldDescriptor? field)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            writer.WriteNullValue();
            return;
        }
        if (field?.IsCollection == true)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
                WriteScalar(writer, item, field.CollectionElementType);
            writer.WriteEndArray();
            return;
        }
        WriteScalar(writer, value, field?.FieldType);
    }

    private static void WriteRawValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteRawValue(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteRawValue(writer, item);
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

    private static void WriteScalar(Utf8JsonWriter writer, JsonElement value, string? token)
    {
        switch (token)
        {
            case "int": writer.WriteNumberValue(value.GetInt32()); break;
            case "long": writer.WriteNumberValue(value.GetInt64()); break;
            case "decimal": writer.WriteNumberValue(value.GetDecimal()); break;
            case "double": writer.WriteNumberValue(value.GetDouble()); break;
            case "bool": writer.WriteBooleanValue(value.GetBoolean()); break;
            default: writer.WriteStringValue(value.GetString()); break;
        }
    }

    private static void WriteIdentity(
        Utf8JsonWriter writer,
        string name,
        AgentToolContractIdentity identity)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("id", identity.Id);
        writer.WriteNumber("version", identity.Version);
        writer.WriteString("contractHash", identity.ContractHash);
        writer.WriteEndObject();
    }

    private static void WriteSchemaIdentity(
        Utf8JsonWriter writer,
        string name,
        AgentToolSchemaContractIdentity? identity)
    {
        if (identity is null)
        {
            writer.WriteNull(name);
            return;
        }
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("id", identity.Id);
        writer.WriteNumber("version", identity.Version);
        writer.WriteString("contractHash", identity.ContractHash);
        writer.WriteEndObject();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name); else writer.WriteString(name, value);
    }

    private static string Hash(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
            writer.Flush();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }
}
