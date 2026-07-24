using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Abstractions.Security;

/// <summary>
/// Computes the canonical identity of a projection access scope.
/// </summary>
public static class AgentMemoryScopeFingerprint
{
    private const string EncodingVersion = "projection-scope-v2";

    public static string Compute(AgentMemoryAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return Compute(scope.TenantId, scope.AllowUnscopedMemory, scope.VisibleDescriptorRefs);
    }

    /// <summary>
    /// Computes the fingerprint from projection-neutral scope components.
    /// Strings are encoded as length-prefixed UTF-8 bytes so field boundaries
    /// cannot be changed by delimiter characters in descriptor identities.
    /// </summary>
    public static string Compute(
        string tenantId,
        bool allowUnscopedMemory,
        IReadOnlyList<DescriptorRef> visibleDescriptorRefs)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(visibleDescriptorRefs);

        var orderedDescriptorRefs = visibleDescriptorRefs
            .OrderBy(reference => reference.Namespace, StringComparer.Ordinal)
            .ThenBy(reference => reference.Id, StringComparer.Ordinal)
            .ThenBy(reference => reference.Version)
            .ToArray();

        var writer = new ArrayBufferWriter<byte>();
        WriteString(writer, EncodingVersion);
        WriteString(writer, tenantId);
        WriteByte(writer, allowUnscopedMemory ? (byte)1 : (byte)0);
        WriteInt32(writer, orderedDescriptorRefs.Length);

        foreach (var descriptorRef in orderedDescriptorRefs)
        {
            WriteString(writer, descriptorRef.Namespace);
            WriteString(writer, descriptorRef.Id);
            WriteByte(writer, descriptorRef.Version.HasValue ? (byte)1 : (byte)0);
            WriteInt32(writer, descriptorRef.Version.GetValueOrDefault());
        }

        return Convert.ToHexString(
                SHA256.HashData(writer.WrittenSpan))
            .ToLowerInvariant();
    }

    private static void WriteString(ArrayBufferWriter<byte> writer, string? value)
    {
        if (value is null)
        {
            WriteInt32(writer, -1);
            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt32(writer, byteCount);
        if (byteCount == 0)
            return;

        var destination = writer.GetSpan(byteCount);
        var written = Encoding.UTF8.GetBytes(value, destination);
        writer.Advance(written);
    }

    private static void WriteByte(ArrayBufferWriter<byte> writer, byte value)
    {
        var destination = writer.GetSpan(1);
        destination[0] = value;
        writer.Advance(1);
    }

    private static void WriteInt32(ArrayBufferWriter<byte> writer, int value)
    {
        var destination = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        writer.Advance(sizeof(int));
    }
}
