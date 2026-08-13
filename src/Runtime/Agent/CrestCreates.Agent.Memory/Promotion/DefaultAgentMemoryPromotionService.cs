using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Identity;

namespace CrestCreates.Agent.Memory.Promotion;

public sealed class DefaultAgentMemoryPromotionService : IAgentMemoryPromotionService, IAgentMemoryCurationServiceCapabilities
{
    private const int MaxIdentifierLength = 256;
    private readonly IAgentMemoryStore _store;
    private readonly AgentMemoryCanonicalHashProjector _hashes;
    private readonly IAgentMemoryArtifactIdGenerator _ids;
    private readonly IAgentMemoryAccountabilityProducer _producer;
    private readonly AgentMemoryCurationFactProjector _factProjector;

    public DefaultAgentMemoryPromotionService(
        IAgentMemoryStore store,
        AgentMemoryCanonicalHashProjector hashes,
        IAgentMemoryArtifactIdGenerator? ids = null,
        IAgentMemoryAccountabilityProducer? producer = null,
        AgentMemoryCurationFactProjector? factProjector = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _hashes = hashes ?? throw new ArgumentNullException(nameof(hashes));
        _ids = ids ?? new DefaultAgentMemoryArtifactIdGenerator();
        _producer = producer ?? new NullAgentMemoryAccountabilityProducer();
        _factProjector = factProjector ?? new AgentMemoryCurationFactProjector();
    }

    public AgentMemoryCurationOutcomeGuarantee OutcomeGuarantee
        => _store is IAgentMemoryStoreCapabilities capabilities
            && _store is IAgentMemoryConditionalCurationStore
            ? capabilities.CurationOutcomeGuarantee
            : AgentMemoryCurationOutcomeGuarantee.Unknown;

    public async ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, AgentMemoryPromotionPlan plan, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateOperationRequest(tenantId, plan.Operation, nameof(PromoteAsync));
        }
        catch (AgentMemoryOperationException ex) when (IsRecordableValidation(ex.Code))
        {
            await PublishValidationFailureAsync(plan.Operation, () => _factProjector.PromoteFailure(plan.Operation, plan, ex.Code));
            throw;
        }
        if (_store is not IAgentMemoryConditionalCurationStore conditional)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.Unknown, "Store does not provide conditional curation transitions.");
        try
        {
            var committed = await conditional.PromoteAsync(tenantId, plan, cancellationToken);
            await PublishCommittedAsync(plan.Operation, () => _factProjector.PromoteCommitted(plan.Operation, plan, committed));
            return committed;
        }
        catch (AgentMemoryOperationException ex) when (IsRecordable(ex.Code))
        {
            await PublishFailureAsync(plan.Operation, () => _factProjector.PromoteFailure(plan.Operation, plan, ex.Code));
            throw;
        }
    }

    public async ValueTask RejectAsync(string tenantId, AgentMemoryCandidateExpectation candidate, AgentMemoryOperationRequest operation, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateOperationRequest(tenantId, operation, nameof(RejectAsync));
        }
        catch (AgentMemoryOperationException ex) when (IsRecordableValidation(ex.Code))
        {
            await PublishValidationFailureAsync(operation, () => _factProjector.RejectFailure(operation, candidate, ex.Code));
            throw;
        }
        if (_store is not IAgentMemoryConditionalCurationStore conditional)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.Unknown, "Store does not provide conditional curation transitions.");
        try
        {
            await conditional.RejectAsync(tenantId, candidate, operation, cancellationToken);
            await PublishCommittedAsync(operation, () => _factProjector.RejectCommitted(operation, candidate));
        }
        catch (AgentMemoryOperationException ex) when (IsRecordable(ex.Code))
        {
            await PublishFailureAsync(operation, () => _factProjector.RejectFailure(operation, candidate, ex.Code));
            throw;
        }
    }

    public async ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, AgentMemorySupersessionPlan plan, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateOperationRequest(tenantId, plan.Operation, nameof(SupersedeAsync));
        }
        catch (AgentMemoryOperationException ex) when (IsRecordableValidation(ex.Code))
        {
            await PublishValidationFailureAsync(plan.Operation, () => _factProjector.SupersedeFailure(plan.Operation, plan, ex.Code));
            throw;
        }
        if (_store is not IAgentMemoryConditionalCurationStore conditional)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.Unknown, "Store does not provide conditional curation transitions.");
        try
        {
            var committed = await conditional.SupersedeAsync(tenantId, plan, cancellationToken);
            await PublishCommittedAsync(plan.Operation, () => _factProjector.SupersedeCommitted(plan.Operation, plan, committed));
            return committed;
        }
        catch (AgentMemoryOperationException ex) when (IsRecordable(ex.Code))
        {
            await PublishFailureAsync(plan.Operation, () => _factProjector.SupersedeFailure(plan.Operation, plan, ex.Code));
            throw;
        }
    }

    public ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, string candidateId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
        => PromoteAsync(tenantId, candidateId, _ids.CreateMemoryId(), request, cancellationToken);

    public async ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, string candidateId, string newMemoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateOperationRequest(tenantId, request, nameof(PromoteAsync));
        }
        catch (AgentMemoryOperationException ex) when (IsRecordableValidation(ex.Code))
        {
            await PublishValidationFailureAsync(request, () => _factProjector.PromoteValidationFailure(request, candidateId, newMemoryId, ex.Code));
            throw;
        }
        var candidate = await _store.GetCandidateAsync(tenantId, candidateId, cancellationToken);
        if (candidate is null)
        {
            await PublishFailureAsync(request, () => _factProjector.PromoteValidationFailure(
                request, candidateId, newMemoryId, AgentMemoryOperationFailureCode.ResourceUnavailable));
            throw new AgentMemoryOperationException(
                AgentMemoryOperationFailureCode.ResourceUnavailable,
                "Candidate is unavailable.");
        }
        var memory = CreatePromotedMemory(candidate, newMemoryId, request);
        return await PromoteAsync(tenantId, new AgentMemoryPromotionPlan
        {
            Candidate = new AgentMemoryCandidateExpectation { CandidateId = candidate.CandidateId, ExpectedStateHash = _hashes.ComputeCandidateStateHash(candidate) },
            NewMemoryId = newMemoryId,
            ExpectedMemoryContentHash = memory.CanonicalContentHash,
            ExpectedMemoryStateHash = _hashes.ComputeMemoryStateHash(memory),
            Operation = request
        }, cancellationToken);
    }

    public async ValueTask RejectAsync(string tenantId, string candidateId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateOperationRequest(tenantId, request, nameof(RejectAsync));
        }
        catch (AgentMemoryOperationException ex) when (IsRecordableValidation(ex.Code))
        {
            await PublishValidationFailureAsync(request, () => _factProjector.RejectValidationFailure(request, candidateId, ex.Code));
            throw;
        }

        var candidate = await _store.GetCandidateAsync(tenantId, candidateId, cancellationToken);
        if (candidate is null)
        {
            await PublishFailureAsync(request, () => _factProjector.RejectValidationFailure(
                request, candidateId, AgentMemoryOperationFailureCode.ResourceUnavailable));
            throw new AgentMemoryOperationException(
                AgentMemoryOperationFailureCode.ResourceUnavailable,
                "Candidate is unavailable.");
        }
        await RejectAsync(tenantId,
            new AgentMemoryCandidateExpectation { CandidateId = candidate.CandidateId, ExpectedStateHash = _hashes.ComputeCandidateStateHash(candidate) },
            request, cancellationToken);
    }

    public ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, string memoryId, AgentMemoryCandidate replacement, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
        // Compatibility overload retained for existing domain callers. Tool paths
        // must use the trusted-identity overload below.
        => SupersedeAsync(tenantId, memoryId, replacement.CandidateId, replacement.CandidateId, request, cancellationToken);

    public async ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, string memoryId, string replacementCandidateId, string newMemoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateOperationRequest(tenantId, request, nameof(SupersedeAsync));
        }
        catch (AgentMemoryOperationException ex) when (IsRecordableValidation(ex.Code))
        {
            await PublishValidationFailureAsync(request, () => _factProjector.SupersedeValidationFailure(
                request, memoryId, replacementCandidateId, newMemoryId, ex.Code));
            throw;
        }
        var existing = await _store.GetMemoryAsync(tenantId, memoryId, cancellationToken);
        if (existing is null)
        {
            await PublishFailureAsync(request, () => _factProjector.SupersedeValidationFailure(
                request, memoryId, replacementCandidateId, newMemoryId, AgentMemoryOperationFailureCode.ResourceUnavailable));
            throw new AgentMemoryOperationException(
                AgentMemoryOperationFailureCode.ResourceUnavailable,
                "Target Memory is unavailable.");
        }

        var replacement = await _store.GetCandidateAsync(tenantId, replacementCandidateId, cancellationToken);
        if (replacement is null)
        {
            await PublishFailureAsync(request, () => _factProjector.SupersedeValidationFailure(
                request, memoryId, replacementCandidateId, newMemoryId, AgentMemoryOperationFailureCode.ResourceUnavailable));
            throw new AgentMemoryOperationException(
                AgentMemoryOperationFailureCode.ResourceUnavailable,
                "Replacement Candidate is unavailable.");
        }
        var newMemory = CreatePromotedMemory(replacement, newMemoryId, request) with { SupersedesMemoryId = existing.MemoryId };
        return await SupersedeAsync(tenantId, new AgentMemorySupersessionPlan
        {
            TargetMemory = new AgentMemoryItemExpectation { MemoryId = existing.MemoryId, ExpectedStateHash = _hashes.ComputeMemoryStateHash(existing) },
            ReplacementCandidate = new AgentMemoryCandidateExpectation { CandidateId = replacement.CandidateId, ExpectedStateHash = _hashes.ComputeCandidateStateHash(replacement) },
            NewMemoryId = newMemoryId,
            ExpectedMemoryContentHash = newMemory.CanonicalContentHash,
            ExpectedMemoryStateHash = _hashes.ComputeMemoryStateHash(newMemory),
            Operation = request
        }, cancellationToken);
    }

    public async ValueTask ArchiveAsync(string tenantId, string memoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateOperationRequest(tenantId, request, nameof(ArchiveAsync));
        }
        catch (AgentMemoryOperationException ex) when (IsRecordableValidation(ex.Code))
        {
            await PublishValidationFailureAsync(request, () => _factProjector.ArchiveValidationFailure(request, memoryId, ex.Code));
            throw;
        }

        var memory = await _store.GetMemoryAsync(tenantId, memoryId, cancellationToken);
        if (memory is null)
        {
            await PublishFailureAsync(request, () => _factProjector.ArchiveValidationFailure(
                request, memoryId, AgentMemoryOperationFailureCode.ResourceUnavailable));
            throw new AgentMemoryOperationException(
                AgentMemoryOperationFailureCode.ResourceUnavailable,
                "Memory is unavailable.");
        }

        if (_store is not IAgentMemoryConditionalCurationStore conditional)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.Unknown, "Store does not provide conditional curation transitions.");

        var expectation = new AgentMemoryItemExpectation
        {
            MemoryId = memory.MemoryId,
            ExpectedStateHash = _hashes.ComputeMemoryStateHash(memory)
        };
        await ArchiveCoreAsync(conditional, tenantId, expectation, memory.Status, request, cancellationToken);
    }

    private async ValueTask ArchiveCoreAsync(
        IAgentMemoryConditionalCurationStore conditional,
        string tenantId,
        AgentMemoryItemExpectation expectation,
        AgentMemoryStatus previousStatus,
        AgentMemoryOperationRequest operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var archived = await conditional.ArchiveAsync(tenantId, expectation, operation, cancellationToken);
            await PublishCommittedAsync(operation, () => _factProjector.ArchiveCommitted(operation, expectation, previousStatus, archived));
        }
        catch (AgentMemoryOperationException ex) when (IsRecordable(ex.Code))
        {
            await PublishFailureAsync(operation, () => _factProjector.ArchiveFailure(operation, expectation, ex.Code));
            throw;
        }
    }

    private async ValueTask PublishCommittedAsync(AgentMemoryOperationRequest operation, Func<AgentMemoryCurationAccountabilityPayload> projection)
    {
        try
        {
            await PublishAsync(operation, projection());
        }
        catch
        {
            // Projection or publication must never replace a confirmed committed outcome.
        }
    }

    private async ValueTask PublishFailureAsync(AgentMemoryOperationRequest operation, Func<AgentMemoryCurationAccountabilityPayload> projection)
    {
        try
        {
            await PublishAsync(operation, projection());
        }
        catch
        {
            // A failed fact must never mask the original typed rejection/conflict.
        }
    }

    private async ValueTask PublishValidationFailureAsync(
        AgentMemoryOperationRequest operation,
        Func<AgentMemoryCurationAccountabilityPayload> projection)
    {
        if (!CanFormAccountabilityEnvelope(operation))
            return;

        await PublishFailureAsync(operation, projection);
    }

    private async ValueTask PublishAsync(AgentMemoryOperationRequest operation, AgentMemoryCurationAccountabilityPayload payload)
    {
        try
        {
            await _producer.PublishCurationAsync(operation.Identity, operation.InvocationContext, payload);
        }
        catch
        {
            // Accountability publication is best-effort; it must never alter the operation outcome.
        }
    }

    private static bool IsRecordable(AgentMemoryOperationFailureCode code)
        => code != AgentMemoryOperationFailureCode.Unknown;

    private static bool IsRecordableValidation(AgentMemoryOperationFailureCode code)
        => code is AgentMemoryOperationFailureCode.MissingReason
            or AgentMemoryOperationFailureCode.MissingSourceOrExplanation;

    private static bool CanFormAccountabilityEnvelope(AgentMemoryOperationRequest operation)
        => operation.Identity is { } identity
            && !string.IsNullOrWhiteSpace(identity.OperationId)
            && identity.OccurredAt != default
            && operation.InvocationContext is { } context
            && string.Equals(operation.TenantId, context.TenantId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(context.TenantId)
            && !string.IsNullOrWhiteSpace(context.ActorId)
            && !string.IsNullOrWhiteSpace(context.ActorKind)
            && !string.IsNullOrWhiteSpace(context.CorrelationId)
            && !string.IsNullOrWhiteSpace(context.InvocationSource);

    private static AgentMemoryItem CreatePromotedMemory(AgentMemoryCandidate candidate, string memoryId, AgentMemoryOperationRequest operation)
        => new()
        {
            MemoryId = memoryId,
            TenantId = candidate.TenantId,
            Kind = candidate.Kind,
            Content = candidate.Content,
            CanonicalContentHash = candidate.CanonicalContentHash,
            PromotedAt = operation.Identity.OccurredAt,
            Confidence = candidate.Confidence,
            Status = AgentMemoryStatus.Active,
            IsAuthoritative = false,
            Tags = candidate.Tags,
            DescriptorRefs = candidate.DescriptorRefs,
            SourceRefs = candidate.SourceRefs,
            RedactionKinds = candidate.RedactionKinds,
            SanitizationDiagnostics = candidate.SanitizationDiagnostics
        };

    private static void ValidateOperationRequest(string tenantId, AgentMemoryOperationRequest request, string operationName)
    {
        // 1. Tenant match
        if (request.TenantId != tenantId)
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.TenantMismatch, $"{AgentMemoryDiagnosticCodes.InvalidOperationTenantMismatch}: operation tenant does not match the trusted tenant.");
        }

        // 2. InvocationContext
        if (request.InvocationContext is null)
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingActor, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingActor}: InvocationContext is required.");
        }
        // 2a. InvocationContext.TenantId must match operation tenantId
        if (request.InvocationContext.TenantId != tenantId)
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.TenantMismatch, $"{AgentMemoryDiagnosticCodes.InvalidOperationTenantMismatch}: InvocationContext tenant does not match the trusted tenant.");
        }
        if (!IsBoundedIdentifier(tenantId)
            || !IsBoundedIdentifier(request.InvocationContext.TenantId)
            || !IsBoundedIdentifier(request.InvocationContext.ActorId)
            || !IsStableActorKind(request.InvocationContext.ActorKind)
            || !IsBoundedIdentifier(request.InvocationContext.CorrelationId)
            || !IsBoundedIdentifier(request.InvocationContext.CausationId, required: false)
            || !IsBoundedIdentifier(request.InvocationContext.ParentAuditId, required: false)
            || !IsBoundedIdentifier(request.InvocationContext.InvocationId, required: false)
            || !IsBoundedIdentifier(request.InvocationContext.SessionId, required: false)
            || !IsStableInvocationSource(request.InvocationContext.InvocationSource))
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingActor, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingActor}: InvocationContext.ActorId is required.");
        }
        if (string.IsNullOrWhiteSpace(request.InvocationContext.ActorKind))
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingActor, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingActor}: InvocationContext.ActorKind is required.");
        }
        if (request.InvocationContext.InvocationSource is "agent" or "mcp")
        {
            var actorKindIsTrusted = request.InvocationContext.InvocationSource == "agent"
                ? string.Equals(request.InvocationContext.ActorKind, "agent", StringComparison.Ordinal)
                : string.Equals(request.InvocationContext.ActorKind, "mcp-client", StringComparison.Ordinal)
                    || string.Equals(request.InvocationContext.ActorKind, "user", StringComparison.Ordinal);
            if (!actorKindIsTrusted || string.IsNullOrWhiteSpace(request.InvocationContext.CorrelationId))
            {
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingActor, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingActor}: first-party invocation context is incomplete.");
            }
        }

        // 3. Reason
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingReason, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingReason}: Reason is required.");
        }

        // 4. Identity
        if (request.Identity is null
            || string.IsNullOrWhiteSpace(request.Identity.OperationId)
            || request.Identity.OperationId.Length > AgentMemoryOperationIdentity.MaxOperationIdLength
            || request.Identity.OccurredAt == default(DateTimeOffset))
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingTimestamp, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingTimestamp}: Operation identity is required and must carry a non-empty OperationId and non-default OccurredAt.");
        }

        // 5. SourceRefs or Explanation
        if (request.SourceRefs.Count == 0 && string.IsNullOrWhiteSpace(request.Explanation))
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingSourceOrExplanation, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingSourceOrExplanation}: SourceRefs or Explanation is required.");
        }
    }

    private static bool IsBoundedIdentifier(string? value, bool required = true)
        => (required ? !string.IsNullOrWhiteSpace(value) : string.IsNullOrWhiteSpace(value) || value.Length <= MaxIdentifierLength)
            && (string.IsNullOrWhiteSpace(value) || value.Length <= MaxIdentifierLength);

    private static bool IsStableInvocationSource(string? invocationSource)
        => invocationSource is "http" or "workflow" or "human-task" or "agent"
            or "mcp" or "integration" or "system";

    private static bool IsStableActorKind(string? actorKind)
        => actorKind is "user" or "anonymous" or "system" or "workflow"
            or "human-task" or "agent" or "integration" or "scheduler"
            or "mcp-client" or "unknown";
}
