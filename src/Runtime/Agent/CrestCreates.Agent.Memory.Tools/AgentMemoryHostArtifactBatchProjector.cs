using System.Security.Cryptography;
using System.Text;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Tools;

internal static class AgentMemoryHostArtifactBatchProjector
{
    public static AgentMemorySecurityArtifactBatchKey Create(
        AgentMemoryHostArtifactBatchKey hostKey,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        AgentMemoryHistorySourceKind sourceKind,
        string sourceId,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        IReadOnlyList<AgentMemorySourceGrant> grants)
    {
        var binding = Canonical(
            "memory-host-origin-v2", hostKey.HostOperationId, hostKey.OperationFingerprint,
            hostKey.ArtifactPurpose, principal.TenantId, principal.UserId, principal.AgentId,
            principal.ExecutionId, sourceKind.ToString(), sourceId,
            AgentMemoryScopeFingerprint.Compute(scope, principal));
        var bindingHash = Hash(binding, "agent-memory-host-origin-binding", "SourceBinding", "memory-host-origin-v2");
        var plan = AgentMemoryArtifactPlanProjector.Compute(principal, scope, hostKey.ArtifactPurpose, handles, grants);
        return new AgentMemorySecurityArtifactBatchKey
        {
            OriginKind = AgentMemorySecurityArtifactBatchOriginKind.TrustedHostOperation,
            OriginBindingHash = bindingHash,
            ArtifactPurpose = hostKey.ArtifactPurpose,
            PreparationOrdinal = 0,
            ArtifactPlanHash = plan
        };
    }

    private static string Canonical(params object[] values)
        => string.Join('|', values.Select(value =>
        {
            var text = value switch
            {
                CanonicalHash hash => string.Join(':', hash.Value, hash.AlgorithmVersion, hash.ArtifactKind,
                    hash.Scope, hash.Purpose, hash.ContractVersion, hash.CanonicalShapeVersion),
                _ => value?.ToString() ?? string.Empty
            };
            return $"{text.Length}:{text}";
        }));

    private static CanonicalHash Hash(string value, string artifactKind, string purpose, string shape)
        => new()
        {
            Value = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant(),
            Algorithm = "SHA-256", AlgorithmVersion = "sha256-length-prefixed-v1",
            ArtifactKind = artifactKind, Scope = "TenantVisible", Purpose = purpose,
            ContractVersion = "memory-security-artifact-v2", CanonicalShapeVersion = shape
        };
}
