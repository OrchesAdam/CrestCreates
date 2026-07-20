using System.Security.Cryptography;

namespace CrestCreates.Agent.Memory.Tools;

/// <summary>
/// Development security-artifact id allocator. The 24 random bytes provide
/// 192 bits of entropy; production implementations must provide at least
/// 128 bits of cryptographically secure entropy and must not embed tenant,
/// resource, invocation, or content data.
/// </summary>
internal static class AgentMemorySecurityArtifactIdGenerator
{
    public static string Create(string prefix)
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return $"{prefix}_{Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')}";
    }
}
