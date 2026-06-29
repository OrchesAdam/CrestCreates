using System.Security.Cryptography;
using System.Text;

namespace CrestCreates.Agent.Memory.Internal;

internal static class AgentMemoryHash
{
    public static string ComputeCanonicalHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
