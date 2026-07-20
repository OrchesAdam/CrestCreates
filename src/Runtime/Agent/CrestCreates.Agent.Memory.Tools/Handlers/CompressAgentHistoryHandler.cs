using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

[CapabilityName(AgentMemoryToolCapabilityIds.CompressHistory)]
internal sealed class CompressAgentHistoryHandler : AgentMemoryToolHandlerBase, ICapabilityHandler<CompressAgentHistoryInput, CompressAgentHistoryResult>
{
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemoryHistoryAccessAuthorizer _authorizer;
    private readonly IAgentMemoryResourceHandleStore _handles;
    private readonly IAgentMemorySourceGrantStore _grants;
    private readonly IAgentConversationStore _conversations;
    private readonly IAgentTaskHistoryStore _tasks;
    private readonly IAgentContextCompressor _compressor;
    private readonly IAgentCompressedContextStore _contexts;
    private readonly IAgentMemoryContentSanitizer _sanitizer;
    private readonly IAgentMemoryArtifactIdGenerator _ids;
    private readonly TimeProvider _time;

    public CompressAgentHistoryHandler(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemoryHistoryAccessAuthorizer authorizer,
        IAgentMemoryResourceHandleStore handles,
        IAgentMemorySourceGrantStore grants,
        IAgentConversationStore conversations,
        IAgentTaskHistoryStore tasks,
        IAgentContextCompressor compressor,
        IAgentCompressedContextStore contexts,
        IAgentMemoryContentSanitizer sanitizer,
        IAgentMemoryArtifactIdGenerator ids,
        TimeProvider time)
        : base(capabilityContext, agentExecution)
    {
        _scopeProvider = scopeProvider; _authorizer = authorizer; _handles = handles; _grants = grants;
        _conversations = conversations; _tasks = tasks; _compressor = compressor; _contexts = contexts;
        _sanitizer = sanitizer; _ids = ids; _time = time;
    }

    public async Task<CompressAgentHistoryResult> ExecuteAsync(CompressAgentHistoryInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope)) return Unavailable("scope-invalid");
        var history = await _handles.GetAsync(input.HistorySourceHandle, ct).ConfigureAwait(false);
        if (history is null || history.Principal != principal || history.State != AgentMemorySecurityArtifactState.Active
            || history.ExpiresAt <= DateTimeOffset.UtcNow)
            return Unavailable("history-unavailable");
        var sourceKind = history.ResourceKind == AgentMemoryResourceKind.ConversationHistory
            ? AgentMemoryHistorySourceKind.Conversation : history.ResourceKind == AgentMemoryResourceKind.TaskHistory
                ? AgentMemoryHistorySourceKind.Task : AgentMemoryHistorySourceKind.Unknown;
        if (sourceKind == AgentMemoryHistorySourceKind.Unknown
            || !await _authorizer.IsAuthorizedAsync(principal, scope, sourceKind, history.ResourceId, ct).ConfigureAwait(false))
            return Unavailable("history-unavailable");

        AgentCompressedContext context;
        IReadOnlyList<AgentContextSourceRef> allowedSourceRefs;
        AgentSourceKind toolSourceKind;
        if (sourceKind == AgentMemoryHistorySourceKind.Conversation)
        {
            var conversation = await _conversations.GetConversationAsync(principal.TenantId, history.ResourceId, ct).ConfigureAwait(false);
            if (conversation is null) return Unavailable("history-unavailable");
            allowedSourceRefs = conversation.Turns.SelectMany((turn, index) =>
                turn.SourceRefs.Count > 0
                    ? turn.SourceRefs
                    : [new AgentContextSourceRef
                    {
                        SourceKind = AgentSourceKind.ConversationTurn, TenantId = conversation.TenantId,
                        SourceId = conversation.ConversationId, RangeStart = index, RangeEnd = index,
                        CanonicalContentHash = _sanitizer.Sanitize(conversation.TenantId, turn.Content, turn.SourceRefs).CanonicalContentHash
                    }]).ToArray();
            context = await _compressor.CompressConversationAsync(conversation, ct).ConfigureAwait(false);
            toolSourceKind = AgentSourceKind.ConversationTurn;
        }
        else
        {
            var task = await _tasks.GetTaskAsync(principal.TenantId, history.ResourceId, ct).ConfigureAwait(false);
            if (task is null) return Unavailable("history-unavailable");
            allowedSourceRefs =
            [
                new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.TaskRecord, TenantId = task.TenantId, SourceId = task.TaskId,
                    CanonicalContentHash = _sanitizer.Sanitize(task.TenantId, $"{task.Title}: {task.Summary ?? "No summary"}", Array.Empty<AgentContextSourceRef>()).CanonicalContentHash
                },
                .. task.Events.SelectMany((evt, index) => evt.SourceRefs.Count > 0
                    ? evt.SourceRefs
                    : [new AgentContextSourceRef
                    {
                        SourceKind = AgentSourceKind.TaskEvent, TenantId = task.TenantId,
                        SourceId = task.TaskId, RangeStart = index, RangeEnd = index,
                        CanonicalContentHash = _sanitizer.Sanitize(task.TenantId, evt.Content, evt.SourceRefs).CanonicalContentHash
                    }])
            ];
            context = await _compressor.CompressTaskAsync(task, ct).ConfigureAwait(false);
            toolSourceKind = AgentSourceKind.TaskRecord;
        }
        if (!string.Equals(context.TenantId, principal.TenantId, StringComparison.Ordinal)
            || context.Blocks.Count > scope.MaxCompressedBlockCount
            || context.Blocks.Any(block => block.Content.Length > scope.MaxCompressedBlockCharacters
                || block.SourceRefs.Count > scope.MaxSourceRefsPerArtifact
                || !string.Equals(block.TenantId, principal.TenantId, StringComparison.Ordinal)
                || !IsTrustedSourceRefSubset(block.SourceRefs, allowedSourceRefs)))
            return Unavailable("result-invalid");

        // Provider labels are request-local only. Normalize the complete graph
        // to framework identities and recompute hashes from final sanitized
        // content and the trusted provenance set before any artifact issuance.
        var normalizedBlocks = new List<AgentCompressedContextBlock>(context.Blocks.Count);
        foreach (var block in context.Blocks)
        {
            var sanitized = _sanitizer.Sanitize(principal.TenantId, block.Content, block.SourceRefs);
            if (sanitized.Rejected)
                return Unavailable("result-invalid");
            normalizedBlocks.Add(block with
            {
                BlockId = _ids.CreateBlockId(),
                TenantId = principal.TenantId,
                Content = sanitized.SanitizedContent,
                CanonicalContentHash = sanitized.CanonicalContentHash,
                Diagnostics = block.Diagnostics.Concat(sanitized.Diagnostics).ToArray()
            });
        }
        context = context with { ContextId = _ids.CreateContextId(), TenantId = principal.TenantId, Blocks = normalizedBlocks };

        var now = _time.GetUtcNow();
        var scopeFingerprint = ComputeScopeFingerprint(scope, principal);
        var contextHandle = new AgentMemoryResourceHandle
        {
            HandleId = AgentMemorySecurityArtifactIdGenerator.Create("hnd"), ResourceKind = AgentMemoryResourceKind.Context,
            ResourceId = context.ContextId, Principal = principal, ScopeFingerprint = scopeFingerprint,
            IssuingInvocationId = principal.ExecutionId, IssuedAt = now, ExpiresAt = now.Add(scope.ResourceHandleLifetime)
        };
        var grantInputs = context.Blocks.SelectMany(block => block.SourceRefs.Select(sourceRef => new AgentMemorySourceGrant
        {
            GrantId = AgentMemorySecurityArtifactIdGenerator.Create("grt"), SourceRef = sourceRef, Principal = principal,
            ScopeFingerprint = scopeFingerprint, RequiredDescriptorRefs = sourceRef.DescriptorRefs,
            IsUnscoped = sourceRef.DescriptorRefs.Count == 0, IssuingInvocationId = principal.ExecutionId,
            IssuedAt = now, ExpiresAt = now.Add(scope.ExpansionGrantLifetime)
        })).ToArray();
        AgentMemoryResourceHandleIssueResult? issuedContext = null;
        AgentMemoryGrantIssueResult? grantResult = null;
        try
        {
            issuedContext = await _handles.TryIssueBatchAsync(
                AgentToolBatchKey(Context, "compressed-context-handle", context.ContextId), [contextHandle],
                scope.MaxActiveResourceHandlesPerResource, scope.MaxResourceHandlesPerInvocation, ct).ConfigureAwait(false);
            grantResult = grantInputs.Length == 0 ? null
                : await _grants.TryIssueBatchAsync(AgentToolBatchKey(Context, "compressed-context-grants", context.ContextId), grantInputs, scope.MaxGrantsPerResource, scope.MaxGrantsPerInvocation, ct).ConfigureAwait(false);
        }
        catch
        {
            await RevokeCreatedArtifactsAsync(_handles, issuedContext, _grants, grantResult, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        var grants = grantResult?.Grants.ToArray() ?? Array.Empty<AgentMemorySourceGrant>();
        var grantsBySource = grants.GroupBy(item => item.SourceRef.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => new AgentMemorySourceGrantDto
            {
                GrantId = item.GrantId, SourceKind = AgentMemoryToolProjection.ToToolSourceKind(item.SourceRef.SourceKind), ExpiresAt = item.ExpiresAt
            }).ToArray(), StringComparer.Ordinal);
        var result = new CompressAgentHistoryResult
        {
            OperationStatus = AgentMemoryToolOperationStatus.Completed,
            ContextHandle = issuedContext.Handles[0].HandleId,
            SourceKind = AgentMemoryToolProjection.ToToolSourceKind(toolSourceKind),
            Blocks = context.Blocks.Select(block => new AgentMemoryToolBlockDto
            {
                Content = block.Content, CanonicalContentHash = AgentMemoryToolProjection.ToToolHash(block.CanonicalContentHash),
                SourceGrants = block.SourceRefs.SelectMany(source => grantsBySource.TryGetValue(source.SourceId, out var value) ? value : Array.Empty<AgentMemorySourceGrantDto>()).ToArray()
            }).ToArray(),
            BlockCount = context.Blocks.Count,
            Diagnostics = Array.Empty<AgentMemoryToolDiagnosticDto>()
        };
        AddBranchInvariantFacts(scope, "compress-agent-history");
        PublishAllowedOutcomes(("completed", result, AgentMemoryToolJsonSerializerContext.Default.CompressAgentHistoryResult));
        try
        {
            await _contexts.SaveCompressedContextAsync(context, ct).ConfigureAwait(false);
        }
        catch
        {
            await RevokeCreatedArtifactsAsync(_handles, issuedContext, _grants, grantResult, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        return result;
    }

    private static CompressAgentHistoryResult Unavailable(string code) => new()
    {
        OperationStatus = AgentMemoryToolOperationStatus.Unavailable,
        ContextHandle = null, SourceKind = null, Blocks = Array.Empty<AgentMemoryToolBlockDto>(), BlockCount = 0,
        Diagnostics = [Diagnostic(code)]
    };

    private static string ComputeScopeFingerprint(AgentMemoryToolAccessScope scope, AgentMemoryToolPrincipal principal)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"memory-scope-v2|{principal.TenantId}|{scope.AllowUnscopedMemory}|{string.Join('|', scope.VisibleDescriptorRefs)}"))).ToLowerInvariant();
}
