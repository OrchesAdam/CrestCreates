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

[CapabilityName(AgentMemoryToolCapabilityIds.BuildPack)]
internal sealed class BuildAgentMemoryPackHandler : AgentMemoryToolHandlerBase, ICapabilityHandler<BuildAgentMemoryPackInput, BuildAgentMemoryPackResult>
{
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemoryReadCore _readCore;
    private readonly IAgentMemoryAccessArtifactCoordinator _coordinator;
    private readonly IAgentMemoryOperationIdentityFactory _identities;

    public BuildAgentMemoryPackHandler(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution,
        IAuditOperationContextAccessor auditContexts,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemoryReadCore readCore,
        IAgentMemoryAccessArtifactCoordinator coordinator,
        IAgentMemoryOperationIdentityFactory identities)
        : base(capabilityContext, agentExecution, auditContexts)
    {
        _scopeProvider = scopeProvider;
        _readCore = readCore;
        _coordinator = coordinator;
        _identities = identities;
    }

    public async Task<BuildAgentMemoryPackResult> ExecuteAsync(BuildAgentMemoryPackInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope))
            return PrepareOutcome(scope, "build-memory-pack", "unavailable", Unavailable("scope-invalid"));

        var newPrincipal = ToAccessPrincipal(principal);
        var newScope = ToAccessScope(scope, principal.TenantId);
        var origin = ToAgentToolOrigin(principal);
        var identity = _identities.Create();
        var request = new AgentMemoryRecallOperationRequest
        {
            Principal = newPrincipal,
            Origin = origin,
            Identity = identity,
            InvocationContext = AgentToolInvocationContext(principal, newPrincipal.TenantId),
            Scope = newScope,
            Input = input
        };

        AgentMemoryReadCoreOutcome<BuildAgentMemoryPackResult>? outcome = null;
        try
        {
            outcome = await _readCore.RecallAsync(request, ct).ConfigureAwait(false);
        }
        catch (AgentMemoryReadCoreException ex)
        {
            return PrepareOutcome(scope, "build-memory-pack", "unavailable", Unavailable(ex.Code));
        }

        try
        {
            AddBranchInvariantFacts(scope, "build-memory-pack");
            PublishAllowedOutcomes(("completed", PrepareOutput(outcome.Result)));
            return outcome.Result;
        }
        catch
        {
            if (outcome?.CompensationToken is not null)
            {
                await _coordinator.RevokeCreatedAsync(outcome.CompensationToken, ct).ConfigureAwait(false);
            }
            throw;
        }
    }

    private BuildAgentMemoryPackResult Unavailable(string code) => new()
    {
        OperationStatus = AgentMemoryToolOperationStatus.Unavailable,
        Items = Array.Empty<AgentMemoryToolItemDto>(),
        ReturnedCount = 0,
        WasTruncated = false,
        IsAuthoritative = false,
        Diagnostics = [Diagnostic(code)]
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
