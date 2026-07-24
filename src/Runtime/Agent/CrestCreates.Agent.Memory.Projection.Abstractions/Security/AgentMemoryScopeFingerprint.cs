using System.Security.Cryptography;
using System.Text;

namespace CrestCreates.Agent.Memory.Projection.Abstractions.Security;

/// <summary>
/// Computes the canonical identity of a projection access scope.
/// </summary>
public static class AgentMemoryScopeFingerprint
{
    public static string Compute(AgentMemoryAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var builder = new StringBuilder();
        builder.Append($"projection-scope-v1|{scope.TenantId}|{scope.AllowUnscopedMemory}|");
        var orderedDescriptorRefs = scope.VisibleDescriptorRefs
            .OrderBy(reference => reference.Namespace, StringComparer.Ordinal)
            .ThenBy(reference => reference.Id, StringComparer.Ordinal)
            .ThenBy(reference => reference.Version);
        builder.Append(string.Join(
            '|',
            orderedDescriptorRefs.Select(reference =>
                $"{reference.Namespace}:{reference.Id}:{reference.Version}")));

        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }
}
