using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.ReadCore.Accountability;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.ReadCore;

/// <summary>
/// Shared source expand core. Protocol-neutral. Zero artifact writes —
/// ctx_expand and memory_source_expand must not issue any handles/grants.
/// The caller-visible Expansion result (sanitize -> truncate -> effective
/// visible-content hash) is constructed before any best-effort Accountability
/// projection; failures in that construction are fail-closed and produce no
/// final result.
/// </summary>
internal sealed class AgentMemorySourceExpandCore : IAgentMemorySourceExpandCore
{
    private const int MaxDiagnosticCodes = 32;

    private readonly IAgentMemoryAccessGrantResolver _grantResolver;
    private readonly IAgentContextSourceExpander _expander;
    private readonly IAgentMemoryAccountabilityProducer _producer;
    private readonly AgentMemoryEffectiveResultHashProjector _effectiveResultHashProjector;
    private readonly IAgentMemoryContentSanitizer _sanitizer;

    public AgentMemorySourceExpandCore(
        IAgentMemoryAccessGrantResolver grantResolver,
        IAgentContextSourceExpander expander,
        IAgentMemoryAccountabilityProducer producer,
        AgentMemoryEffectiveResultHashProjector effectiveResultHashProjector,
        IAgentMemoryContentSanitizer sanitizer)
    {
        _grantResolver = grantResolver;
        _expander = expander;
        _producer = producer;
        _effectiveResultHashProjector = effectiveResultHashProjector;
        _sanitizer = sanitizer;
    }

    public async ValueTask<AgentMemoryReadCoreOutcome<ExpandAgentMemorySourceResult>> ExpandAsync(
        AgentMemorySourceExpansionOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        AgentMemoryOperationRequestValidator.Validate(
            request.Principal, request.Scope, request.Identity, request.InvocationContext);
        var principal = request.Principal;
        var scope = request.Scope;
        var input = request.Input;

        // Validate budget — reject zero/negative before scope checks
        if (input.MaximumCharacters <= 0)
            throw new AgentMemoryReadCoreException("budget-invalid", "MaximumCharacters must be positive");
        if (input.MaximumCharacters > scope.MaxExpansionCharacters)
            throw new AgentMemoryReadCoreException("budget-invalid", "MaximumCharacters exceeds scope limit");

        // Resolve grant — validates the caller holds a grant to expand this source
        var grant = await _grantResolver.ResolveAsync(
            input.GrantId, principal, scope, cancellationToken);
        if (grant is null)
            throw new AgentMemoryReadCoreException("resource-unavailable", "Grant not resolvable");

        // Expand — zero artifact writes. Only the authorized grant SourceRef is exposed.
        var expansion = await _expander.ExpandAsync(grant.SourceRef, cancellationToken);

        // Safety result construction OUTSIDE the best-effort fence:
        // sanitize -> truncate -> caller-visible effective hash. Any failure here
        // is fail-closed because no final Expansion result exists yet.
        var (result, state) = BuildResultAndAccountabilityState(request, grant, expansion, input);

        // Post-result fence: only after the exact final Expansion result exists may
        // typed Accountability payload/fact construction and producer invocation run.
        // Best-effort — a failure here must never replace the established result.
        try
        {
            var payload = CreatePayload(request, state);
            await _producer.PublishSourceExpansionAsync(request.Identity, request.InvocationContext, payload);
        }
        catch
        {
            // Swallow: an Accountability failure must not change the Expansion result.
        }

        // Zero artifact writes — no handles, no grants, no compensation token
        return new AgentMemoryReadCoreOutcome<ExpandAgentMemorySourceResult>
        {
            Result = result,
            ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope),
            MaximumAuditFacts = scope.MaxAuditFacts,
            Receipt = new AgentMemoryArtifactBatchReceipt
            {
                HandleBatch = null,
                GrantBatch = null
            },
            CompensationToken = null
        };
    }

    private (ExpandAgentMemorySourceResult Result, ExpansionAccountabilityState State) BuildResultAndAccountabilityState(
        AgentMemorySourceExpansionOperationRequest request,
        AgentMemoryAccessSourceGrant grant,
        AgentSourceExpansionResult expansion,
        ExpandAgentMemorySourceInput input)
    {
        var sourceRef = grant.SourceRef;
        var sourceKind = AgentMemoryStableWireMappings.MapSourceKind(sourceRef.SourceKind);
        var sourceId = sourceRef.SourceId;
        var rangeStart = sourceRef.RangeStart;
        var rangeEnd = sourceRef.RangeEnd;
        var expanderDiagnostics = expansion.Diagnostics.Select(d => new AgentMemoryToolDiagnosticDto
        {
            Code = d.Code.RequireValue(),
            Severity = MapSeverity(d.Severity)
        }).ToList();
        var expanderDiagnosticCodes = NormalizeCodes(expansion.Diagnostics.Select(d => d.Code.RequireValue()));

        if (expansion.Status != AgentMemorySourceExpansionStatus.Expanded)
        {
            var payloadStatus = expansion.Status switch
            {
                AgentMemorySourceExpansionStatus.Redacted => "redacted",
                AgentMemorySourceExpansionStatus.NotFound => "not-found",
                AgentMemorySourceExpansionStatus.ExternalSourceNotSupported => "external-source-not-supported",
                _ => "not-expandable"
            };
            var callerStatus = expansion.Status == AgentMemorySourceExpansionStatus.Redacted
                ? AgentMemoryToolOperationStatus.Redacted
                : AgentMemoryToolOperationStatus.NotExpandable;
            var terminalSanitization = new AgentMemoryAccountabilitySanitizationSummary
            {
                State = expansion.Status == AgentMemorySourceExpansionStatus.Redacted ? "redacted" : "none",
                RedactionCodes = Array.Empty<string>(),
                DiagnosticCodes = Array.Empty<string>()
            };

            var terminalResult = new ExpandAgentMemorySourceResult
            {
                OperationStatus = callerStatus,
                WasTruncated = false,
                Diagnostics = expanderDiagnostics
            };

            var terminalState = new ExpansionAccountabilityState
            {
                Status = payloadStatus,
                SourceKind = sourceKind,
                SourceId = sourceId,
                RangeStart = rangeStart,
                RangeEnd = rangeEnd,
                EffectiveVisibleContentHash = null,
                WasTruncated = false,
                MaximumCharacters = input.MaximumCharacters,
                Sanitization = terminalSanitization,
                DiagnosticCodes = expanderDiagnosticCodes
            };

            return (terminalResult, terminalState);
        }

        // Expanded: re-sanitize the expander content with the authorized SourceRef.
        var sanitized = _sanitizer.Sanitize(
            request.Principal.TenantId,
            expansion.SanitizedContent ?? string.Empty,
            new[] { grant.SourceRef });

        // Sanitizer rejection is a terminal fail-closed state: the caller sees
        // Redacted with no content and no hash, and the payload records
        // Sanitization.State = rejected.
        if (sanitized.Rejected)
        {
            var rejectedSanitization = new AgentMemoryAccountabilitySanitizationSummary
            {
                State = "rejected",
                RedactionCodes = NormalizeCodes(sanitized.RedactionKinds),
                DiagnosticCodes = NormalizeCodes(sanitized.Diagnostics.Select(d => d.Code.RequireValue()))
            };

            var rejectedResult = new ExpandAgentMemorySourceResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.Redacted,
                WasTruncated = false,
                Diagnostics = expanderDiagnostics
            };

            var rejectedState = new ExpansionAccountabilityState
            {
                Status = "redacted",
                SourceKind = sourceKind,
                SourceId = sourceId,
                RangeStart = rangeStart,
                RangeEnd = rangeEnd,
                EffectiveVisibleContentHash = null,
                WasTruncated = false,
                MaximumCharacters = input.MaximumCharacters,
                Sanitization = rejectedSanitization,
                DiagnosticCodes = expanderDiagnosticCodes
            };

            return (rejectedResult, rejectedState);
        }

        var sanitizationState = sanitized.RedactionKinds.Count > 0 ? "redacted" : "none";
        var sanitization = new AgentMemoryAccountabilitySanitizationSummary
        {
            State = sanitizationState,
            RedactionCodes = NormalizeCodes(sanitized.RedactionKinds),
            DiagnosticCodes = NormalizeCodes(sanitized.Diagnostics.Select(d => d.Code.RequireValue()))
        };

        // Truncate the sanitized content to the operation budget, then project the
        // exact final caller-visible value. Never reuses the sanitizer/domain hash.
        var finalContent = sanitized.SanitizedContent;
        var wasTruncated = finalContent.Length > input.MaximumCharacters;
        if (wasTruncated)
            finalContent = finalContent[..input.MaximumCharacters];

        var effectiveHash = _effectiveResultHashProjector.ComputeEffectiveVisibleContentHash(
            request.Principal.TenantId,
            finalContent);

        var result = new ExpandAgentMemorySourceResult
        {
            OperationStatus = AgentMemoryToolOperationStatus.Completed,
            SanitizedContent = finalContent,
            CanonicalContentHash = MapCanonicalHashDto(effectiveHash),
            WasTruncated = wasTruncated,
            Diagnostics = expanderDiagnostics
        };

        var state = new ExpansionAccountabilityState
        {
            Status = "expanded",
            SourceKind = sourceKind,
            SourceId = sourceId,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            EffectiveVisibleContentHash = effectiveHash,
            WasTruncated = wasTruncated,
            MaximumCharacters = input.MaximumCharacters,
            Sanitization = sanitization,
            DiagnosticCodes = expanderDiagnosticCodes
        };

        return (result, state);
    }

    private static AgentMemorySourceExpansionAccountabilityPayload CreatePayload(
        AgentMemorySourceExpansionOperationRequest request,
        ExpansionAccountabilityState state)
    {
        return new AgentMemorySourceExpansionAccountabilityPayload
        {
            OperationId = request.Identity.OperationId,
            SourceKind = state.SourceKind,
            SourceId = state.SourceId,
            RangeStart = state.RangeStart,
            RangeEnd = state.RangeEnd,
            Status = state.Status,
            EffectiveVisibleContentHash = state.EffectiveVisibleContentHash,
            MaximumCharacters = state.MaximumCharacters,
            WasTruncated = state.WasTruncated,
            Sanitization = state.Sanitization,
            DiagnosticCodes = state.DiagnosticCodes
        };
    }

    private static string[] NormalizeCodes(IEnumerable<string> codes)
        => codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .Take(MaxDiagnosticCodes)
            .ToArray();

    private static AgentMemoryToolCanonicalHashDto MapCanonicalHashDto(CanonicalHash hash)
        => new AgentMemoryToolCanonicalHashDto
        {
            Value = hash.Value,
            AlgorithmVersion = hash.AlgorithmVersion,
            ContractVersion = hash.ContractVersion,
            CanonicalShapeVersion = hash.CanonicalShapeVersion
        };

    private static AgentMemoryToolDiagnosticSeverity MapSeverity(SeverityLevel severity)
    {
        var value = severity.RequireValue();
        if (string.Equals(value, "Error", StringComparison.Ordinal) || string.Equals(value, "Blocker", StringComparison.Ordinal))
            return AgentMemoryToolDiagnosticSeverity.Error;
        if (string.Equals(value, "Warning", StringComparison.Ordinal) || string.Equals(value, "Review", StringComparison.Ordinal))
            return AgentMemoryToolDiagnosticSeverity.Warning;
        if (string.Equals(value, "Info", StringComparison.Ordinal))
            return AgentMemoryToolDiagnosticSeverity.Info;
        return AgentMemoryToolDiagnosticSeverity.Unknown;
    }

    private sealed record ExpansionAccountabilityState
    {
        public required string Status { get; init; }
        public required string SourceKind { get; init; }
        public required string SourceId { get; init; }
        public int? RangeStart { get; init; }
        public int? RangeEnd { get; init; }
        public CanonicalHash? EffectiveVisibleContentHash { get; init; }
        public required int MaximumCharacters { get; init; }
        public required bool WasTruncated { get; init; }
        public required AgentMemoryAccountabilitySanitizationSummary Sanitization { get; init; }
        public IReadOnlyList<string> DiagnosticCodes { get; init; } = Array.Empty<string>();
    }
}
