using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CrestCreates.Mcp;

public interface IMcpIdempotencyKeyBuilder
{
    string Build(McpToolRuntimeEntry entry, McpToolCallContext context);
}

public sealed class DefaultMcpIdempotencyKeyBuilder : IMcpIdempotencyKeyBuilder
{
    public string Build(McpToolRuntimeEntry entry, McpToolCallContext context)
    {
        using var stream = new MemoryStream();
        Write(stream, "mcp-idempotency-v1");
        Write(stream, context.Host.HostId);
        Write(stream, entry.ToolContractHash);
        Write(stream, entry.CapabilityContractHash);
        WriteOptional(stream, entry.InputSchemaContractHash);
        WriteOptional(stream, entry.OutputSchemaContractHash);
        Write(stream, context.InvocationId);
        var digest = SHA256.HashData(stream.ToArray());
        return "mcp:v1:" + Convert.ToBase64String(digest)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void Write(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    private static void WriteOptional(Stream stream, string? value)
    {
        stream.WriteByte(value is null ? (byte)0 : (byte)1);
        if (value is not null)
            Write(stream, value);
    }
}
