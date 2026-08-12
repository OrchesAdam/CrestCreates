using System.Security.Cryptography;
using System.Text;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.ReadCore;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Tools;

[CapabilityName(AgentMemoryToolCapabilityIds.ExpandSource)]
internal sealed class ExpandAgentMemorySourceHandler : AgentMemoryToolHandlerBase, ICapabilityHandler<ExpandAgentMemorySourceInput, ExpandAgentMemorySourceResult>
{
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemorySourceExpandCore _expandCore;
    private readonly IAgentMemoryOperationIdentityFactory _identities;

    public ExpandAgentMemorySourceHandler(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution,
        IAuditOperationContextAccessor auditContexts,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemorySourceExpandCore expandCore,
        IAgentMemoryOperationIdentityFactory identities)
        : base(capabilityContext, agentExecution, auditContexts)
    {
        _scopeProvider = scopeProvider;
        _expandCore = expandCore;
        _identities = identities;
    }

    public async Task<ExpandAgentMemorySourceResult> ExecuteAsync(ExpandAgentMemorySourceInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope))
            return PrepareOutcome(scope, "expand-memory-source", "unavailable", Unavailable("scope-invalid"));

        var newPrincipal = ToAccessPrincipal(principal);
        var newScope = ToAccessScope(scope, principal.TenantId);
        var origin = ToAgentToolOrigin(principal);
        var identity = _identities.Create();
        var request = new AgentMemorySourceExpansionOperationRequest
        {
            Principal = newPrincipal,
            Origin = origin,
            Identity = identity,
            InvocationContext = AgentToolInvocationContext(principal, newPrincipal.TenantId),
            Scope = newScope,
            Input = input
        };

        AgentMemoryReadCoreOutcome<ExpandAgentMemorySourceResult>? outcome = null;
        try
        {
            outcome = await _expandCore.ExpandAsync(request, ct).ConfigureAwait(false);
        }
        catch (AgentMemoryReadCoreException ex)
        {
            return PrepareOutcome(scope, "expand-memory-source", "unavailable", Unavailable(ex.Code));
        }

        var result = outcome.Result;
        return PrepareOutcome(scope, "expand-memory-source", WireStatus(result.OperationStatus), result);
    }

    private static ExpandAgentMemorySourceResult Unavailable(string code) => new()
    {
        OperationStatus = AgentMemoryToolOperationStatus.Unavailable,
        SanitizedContent = null,
        CanonicalContentHash = null,
        WasTruncated = false,
        Diagnostics = [Diagnostic(code)]
    };

    private static string WireStatus(AgentMemoryToolOperationStatus status) => status switch
    {
        AgentMemoryToolOperationStatus.Completed => "completed",
        AgentMemoryToolOperationStatus.Unavailable => "unavailable",
        AgentMemoryToolOperationStatus.Conflict => "conflict",
        AgentMemoryToolOperationStatus.Redacted => "redacted",
        AgentMemoryToolOperationStatus.NotExpandable => "not-expandable",
        _ => throw new InvalidOperationException("Unknown Memory Tool operation status.")
    };

    private static AgentMemoryAccessPrincipal ToAccessPrincipal(AgentMemoryToolPrincipal p)
        => new()
        {
            TenantId = p.TenantId,
            UserId = p.UserId,
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = p.AgentId,
            SecurityContextId = p.ExecutionId
        };

    private static AgentMemoryAccessScope ToAccessScope(AgentMemoryToolAccessScope s, string tenantId)
        => new()
        {
            TenantId = tenantId,
            VisibleDescriptorRefs = s.VisibleDescriptorRefs,
            AllowUnscopedMemory = s.AllowUnscopedMemory,
            MaxVisibleDescriptorRefs = s.MaxVisibleDescriptorRefs,
            MaxRecallCount = s.MaxRecallCount,
            MaxRecallCharacters = s.MaxRecallCharacters,
            MaxExpansionCharacters = s.MaxExpansionCharacters,
            MaxContextRecallCharacters = s.MaxRecallCharacters,
            MaxCompressedBlockCount = s.MaxCompressedBlockCount,
            MaxCompressedBlockCharacters = s.MaxCompressedBlockCharacters,
            MaxCandidateCount = s.MaxCandidateCount,
            MaxCandidateCharacters = s.MaxCandidateCharacters,
            MaxSourceRefsPerArtifact = s.MaxSourceRefsPerArtifact,
            MaxGrantsPerResource = s.MaxGrantsPerResource,
            MaxGrantsPerOperation = s.MaxGrantsPerInvocation,
            MaxResourceHandlesPerOperation = s.MaxResourceHandlesPerInvocation,
            MaxActiveResourceHandlesPerResource = s.MaxActiveResourceHandlesPerResource,
            MaxAuditFacts = s.MaxAuditFacts,
            MaxTagsPerResource = s.MaxTagsPerResource,
            ExpansionGrantLifetime = s.ExpansionGrantLifetime,
            ResourceHandleLifetime = s.ResourceHandleLifetime
        };

    private AgentMemoryArtifactOrigin ToAgentToolOrigin(AgentMemoryToolPrincipal p)
    {
        var binding = InvocationBinding;
        return new AgentMemoryArtifactOrigin
        {
            Kind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OperationId = binding.LogicalKey.InvocationId,
            BindingHash = ComputeOriginBindingHash(binding)
        };
    }

    private static CanonicalHash ComputeOriginBindingHash(AgentToolInvocationBindingSnapshot binding)
    {
        var raw = Encoding.UTF8.GetBytes($"origin-{binding.LogicalKey.InvocationId}-{binding.InvocationFingerprint}");
        return new CanonicalHash
        {
            Value = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(),
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "agent-memory-security-artifact-origin-binding",
            Scope = "TenantVisible",
            Purpose = "SourceBinding",
            ContractVersion = "memory-security-artifact-v2",
            CanonicalShapeVersion = "agent-tool-origin-binding-v3"
        };
    }
}
