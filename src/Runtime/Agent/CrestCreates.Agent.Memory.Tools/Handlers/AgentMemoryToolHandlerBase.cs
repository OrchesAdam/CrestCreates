using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

internal abstract class AgentMemoryToolHandlerBase
{
    private readonly ICapabilityExecutionContextAccessor _capabilityContext;
    private readonly IAgentExecutionContextAccessor _agentExecution;

    protected AgentMemoryToolHandlerBase(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution)
    {
        _capabilityContext = capabilityContext;
        _agentExecution = agentExecution;
    }

    protected CapabilityExecutionContext Context
        => _capabilityContext.Current ?? throw new InvalidOperationException("Capability context is unavailable.");

    protected AgentMemoryToolPrincipal Principal
    {
        get
        {
            var execution = _agentExecution.Current
                ?? throw new InvalidOperationException("Agent execution context is unavailable.");
            if (string.IsNullOrWhiteSpace(Context.TenantId)
                || string.IsNullOrWhiteSpace(execution.AgentId)
                || string.IsNullOrWhiteSpace(execution.ExecutionId))
                throw new InvalidOperationException("Trusted tenant and execution identity are required.");
            return new AgentMemoryToolPrincipal
            {
                TenantId = Context.TenantId!,
                UserId = Context.UserId ?? string.Empty,
                AgentId = execution.AgentId,
                ExecutionId = execution.ExecutionId
            };
        }
    }

    protected AgentExecutionContext Execution
        => _agentExecution.Current ?? throw new InvalidOperationException("Agent execution context is unavailable.");

    protected static AgentMemorySecurityArtifactBatchKey AgentToolBatchKey(
        CapabilityExecutionContext context,
        string purpose,
        string artifactPlanHash,
        int ordinal = 0)
    {
        if (!context.Items.TryGetValue(AgentCapabilityContextItemNames.InvocationBindingSnapshot, out var value)
            || value is not AgentToolInvocationBindingSnapshot binding)
            throw new InvalidOperationException("Exact invocation binding is unavailable.");
        return new AgentMemorySecurityArtifactBatchKey
        {
            OriginKind = AgentMemorySecurityArtifactBatchOriginKind.AgentToolInvocation,
            LogicalInvocationKeyHash = ComputeLogicalKeyHash(binding.LogicalKey),
            InvocationFingerprint = binding.InvocationFingerprint,
            ArtifactPurpose = purpose,
            PreparationOrdinal = ordinal,
            ArtifactPlanHash = artifactPlanHash
        };
    }

    protected static AgentMemoryToolDiagnosticDto Diagnostic(string code, AgentMemoryToolDiagnosticSeverity severity = AgentMemoryToolDiagnosticSeverity.Warning)
        => new() { Code = code, Severity = severity };

    protected static bool IsValidScope(AgentMemoryToolAccessScope scope)
        => scope.VisibleDescriptorRefs.Count <= scope.MaxVisibleDescriptorRefs
            && scope.VisibleDescriptorRefs.All(item => item.Version is > 0)
            && scope.MaxRecallCount > 0
            && scope.MaxRecallCharacters > 0
            && scope.MaxExpansionCharacters > 0
            && scope.MaxCompressedBlockCount > 0
            && scope.MaxCompressedBlockCharacters > 0
            && scope.MaxCandidateCount > 0
            && scope.MaxCandidateCharacters > 0
            && scope.MaxSourceRefsPerArtifact > 0
            && scope.MaxGrantsPerResource > 0
            && scope.MaxGrantsPerInvocation > 0
            && scope.MaxResourceHandlesPerInvocation > 0
            && scope.MaxActiveResourceHandlesPerResource > 0
            && scope.MaxAuditFacts > 0
            && scope.MaxTagsPerResource > 0;

    /// <summary>
    /// Rolls back only artifacts created by this preparation. A retry may have
    /// reused an existing active handle/grant; revoking that artifact would
    /// invalidate a result already published to another invocation.
    /// </summary>
    protected static async ValueTask RevokeCreatedArtifactsAsync(
        IAgentMemoryResourceHandleStore handles,
        AgentMemoryResourceHandleIssueResult? handleResult,
        IAgentMemorySourceGrantStore grants,
        AgentMemoryGrantIssueResult? grantResult,
        CancellationToken cancellationToken = default)
    {
        if (handleResult is { ReusedExisting: false })
        {
            foreach (var handle in handleResult.Handles)
                await handles.RevokeAsync(handle.HandleId, cancellationToken).ConfigureAwait(false);
        }

        if (grantResult is { ReusedExisting: false })
        {
            foreach (var grant in grantResult.Grants)
                await grants.RevokeAsync(grant.GrantId, cancellationToken).ConfigureAwait(false);
        }
    }

    protected static bool IsTrustedSourceRefSubset(
        IReadOnlyList<AgentContextSourceRef> produced,
        IReadOnlyList<AgentContextSourceRef> allowed)
        => produced.All(item => allowed.Any(candidate => SourceRefEquals(item, candidate)));

    protected static bool SourceRefEquals(AgentContextSourceRef left, AgentContextSourceRef right)
        => left.SourceKind == right.SourceKind
            && string.Equals(left.TenantId, right.TenantId, StringComparison.Ordinal)
            && string.Equals(left.SourceId, right.SourceId, StringComparison.Ordinal)
            && left.RangeStart == right.RangeStart
            && left.RangeEnd == right.RangeEnd
            && string.Equals(left.CorrelationId, right.CorrelationId, StringComparison.Ordinal)
            && string.Equals(left.CausationId, right.CausationId, StringComparison.Ordinal)
            && Equals(left.CanonicalContentHash, right.CanonicalContentHash)
            && DescriptorRefsEqual(left.DescriptorRefs, right.DescriptorRefs);

    protected static bool DescriptorRefsEqual(
        IReadOnlyList<CrestCreates.Metadata.Abstractions.DescriptorRef> left,
        IReadOnlyList<CrestCreates.Metadata.Abstractions.DescriptorRef> right)
        => left.OrderBy(item => item.Namespace, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Version ?? -1)
            .SequenceEqual(
                right.OrderBy(item => item.Namespace, StringComparer.Ordinal)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .ThenBy(item => item.Version ?? -1));

    /// <summary>
    /// Adds only branch-invariant, non-resource facts. Result-dependent facts
    /// (status, counts, lifecycle, and content hashes) are projected from the
    /// final prepared envelope by the invoker and must not be added here.
    /// </summary>
    protected void AddBranchInvariantFacts(AgentMemoryToolAccessScope scope, string operation)
    {
        if (!Context.Items.TryGetValue(AgentCapabilityContextItemNames.InvocationFactBuffer, out var value)
            || value is not IAgentToolInvocationFactSink facts)
            return;

        var descriptorProjection = string.Join(';', scope.VisibleDescriptorRefs
            .OrderBy(item => item.Namespace, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Version)
            .Select(item => $"{item.Namespace}:{item.Id}:{item.Version}"));
        var scopeFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"memory-scope-v2|{Principal.TenantId}|{scope.AllowUnscopedMemory}|{descriptorProjection}")))
            .ToLowerInvariant();
        facts.AddTrustedFacts(
        [
            new AgentToolAuditFact { Code = "memory.scope-fingerprint", Value = scopeFingerprint, Kind = AgentToolAuditFactKind.BranchInvariant },
            new AgentToolAuditFact { Code = "memory.operation", Value = operation, Kind = AgentToolAuditFactKind.BranchInvariant }
        ], scope.MaxAuditFacts);
        Context.Items[AgentCapabilityContextItemNames.BranchInvariantFactsPrepared] = true;
    }

    /// <summary>
    /// Publishes the complete, bounded set of envelopes that can legally be
    /// returned after the first mutating operation. The invoker compares the
    /// final serialized bytes with these receipts; handlers must not create a
    /// new envelope after the domain transition.
    /// </summary>
    protected void PublishAllowedOutcomes<T>(
        params (string OutcomeCode, AgentToolPreparedOutput<T> Prepared)[] outcomes)
    {
        // Branch-invariant facts must be staged before the receipt set. This
        // makes the ordering requirement executable instead of relying on a
        // convention shared by individual handlers.
        if (!Context.Items.TryGetValue(AgentCapabilityContextItemNames.BranchInvariantFactsPrepared, out var factsPrepared)
            || factsPrepared is not true)
            throw new InvalidOperationException("Branch-invariant audit facts must be prepared before output receipts.");
        if (!Context.Items.TryGetValue(AgentCapabilityContextItemNames.OutputPreflightReceiptSink, out var value)
            || value is not IAgentToolOutputPreflightReceiptSink sink)
            throw new InvalidOperationException("Output preflight receipt sink is unavailable.");
        if (!Context.Items.TryGetValue(AgentCapabilityContextItemNames.ToolDescriptorId, out var idValue)
            || idValue is not string descriptorId
            || !Context.Items.TryGetValue(AgentCapabilityContextItemNames.ToolDescriptorVersion, out var versionValue)
            || versionValue is not int descriptorVersion
            || !Context.Items.TryGetValue(AgentCapabilityContextItemNames.OutputSchemaContractFingerprint, out var contractValue)
            || contractValue is not string contractFingerprint)
            throw new InvalidOperationException("Trusted output binding metadata is unavailable.");

        var receipts = new AgentToolPreparedOutcomeReceipt[outcomes.Length];
        for (var index = 0; index < outcomes.Length; index++)
        {
            var outcome = outcomes[index];
            if (!string.Equals(outcome.Prepared.Receipt.ToolDescriptorId, descriptorId, StringComparison.Ordinal)
                || outcome.Prepared.Receipt.ToolDescriptorVersion != descriptorVersion
                || !string.Equals(outcome.Prepared.Receipt.OutputContractFingerprint, contractFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("Prepared output binding does not match the trusted tool contract.");
            receipts[index] = new AgentToolPreparedOutcomeReceipt
            {
                OutcomeCode = outcome.OutcomeCode,
                Receipt = outcome.Prepared.Receipt,
                InternalFacts = outcome.Prepared.ProjectedOutputFacts
            };
        }

        Context.Items[AgentCapabilityContextItemNames.RequiresOutputPreflightReceipt] = true;
        sink.PublishAllowedOutcomes(receipts);
    }

    protected TOutput PrepareOutcome<TOutput>(
        AgentMemoryToolAccessScope scope,
        string operation,
        string outcomeCode,
        TOutput output,
        JsonTypeInfo<TOutput> typeInfo)
    {
        AddBranchInvariantFacts(scope, operation);
        PublishAllowedOutcomes((outcomeCode, PrepareOutput(output, typeInfo)));
        return output;
    }

    protected AgentToolPreparedOutput<TOutput> PrepareOutput<TOutput>(
        TOutput output,
        JsonTypeInfo<TOutput> typeInfo)
    {
        if (!Context.Items.TryGetValue(AgentCapabilityContextItemNames.OutputPreflightRuntime, out var value)
            || value is not IAgentToolOutputPreflightRuntime runtime)
            throw new InvalidOperationException("The shared output preflight runtime is unavailable.");
        return runtime.Prepare(output, typeInfo);
    }

    private static string ComputeLogicalKeyHash(AgentToolLogicalInvocationKey key)
    {
        var value = $"agent-tool-logical-key-v1|{key.TenantId}|{key.UserId}|{key.AgentId}|{key.ExecutionId}|{key.InvocationId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
