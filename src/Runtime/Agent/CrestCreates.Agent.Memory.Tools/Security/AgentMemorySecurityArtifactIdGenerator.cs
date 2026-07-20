using System.Security.Cryptography;

namespace CrestCreates.Agent.Memory.Tools;

internal static class AgentMemorySecurityArtifactIdGenerator
{
    public static string Create(string prefix)
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return $"{prefix}_{Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')}";
    }
}
