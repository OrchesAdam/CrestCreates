using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.Agent.ControlPlane.Projections;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.ContextPack.Abstractions;
using Microsoft.Extensions.Logging;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftStore = CrestCreates.DescriptorDraft.Abstractions.IDescriptorDraftStore;
using DraftValidator = CrestCreates.DescriptorDraft.Abstractions.IDescriptorDraftValidator;
using DraftReviewService = CrestCreates.DescriptorDraft.Abstractions.IDescriptorDraftReviewService;
using DraftMaterializer = CrestCreates.DescriptorDraft.Abstractions.IDescriptorDraftMaterializer;
using DraftValidationResult = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraftValidationResult;
using DraftPackagePreview = CrestCreates.DescriptorDraft.Abstractions.DescriptorPackagePreview;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Default implementation of the Agent Control Plane tool surface.
/// Every method enforces a staged pipeline:
/// manifest lookup → coarse authorization → visibility scope →
/// resource resolution (snapshot) → kind visibility check → service invocation → audit recording.
/// No tool may bypass descriptor governance, draft review, package evidence,
/// human approval, or activation gates.
/// </summary>
public sealed class DefaultAgentControlPlaneToolService : IAgentControlPlaneToolService
{
    private readonly IAgentToolManifestProvider _manifestProvider;
    private readonly IAgentToolAuthorizationService _authorizationService;
    private readonly IAgentToolInvocationAuditor _auditor;
    private readonly DraftStore _draftStore;
    private readonly DraftValidator _draftValidator;
    private readonly DraftReviewService _draftReviewService;
    private readonly DraftMaterializer _draftMaterializer;
    private readonly IMetadataContextPackBuilder _contextPackBuilder;
    private readonly IDescriptorCatalog _descriptorCatalog;
    private readonly IDescriptorRelationshipProvider _relationshipProvider;
    private readonly IDescriptorTopologyBuilder _topologyBuilder;
    private readonly IDescriptorPackageBuilder _packageBuilder;
    private readonly IDescriptorStableHashBuilder _hashBuilder;
    private readonly ILogger<DefaultAgentControlPlaneToolService> _logger;
    private readonly AgentControlPlaneResourceResolver _resourceResolver;
    private readonly AgentTopologyVisibilityProjector _topologyProjector;
    private readonly AgentDraftArtifactVisibilityProjector _artifactProjector;
    private readonly AgentDiagnosticExplanationPolicy _explanationPolicy = new();
    private readonly Func<AgentToolAuthorizationOptions> _optionsFactory;
    private readonly IDescriptorReviewReportBuilder _reportBuilder;
    private readonly IDescriptorReviewReportRenderer _reportRenderer;
    private readonly IDescriptorActivationRequestService _activationRequestService;
    private readonly IActivationReviewOrchestrator _activationReviewOrchestrator;
    private readonly IActivationBindingArtifactResolver _artifactResolver;

    // Local stores for review results, fix proposals, package previews, activation requests
    // Keyed by (TenantId, ArtifactId) for tenant isolation.
    // Each entry carries the owning Draft for owner-kind visibility resolution.
    private readonly ConcurrentDictionary<(string TenantId, string Id), ReviewResourceSnapshot> _reviewResults = new();
    private readonly ConcurrentDictionary<(string TenantId, string Id), FixProposalResourceSnapshot> _fixProposals = new();
    private readonly ConcurrentDictionary<(string TenantId, string Id), PackagePreviewResourceSnapshot> _packagePreviews = new();
    private readonly ConcurrentDictionary<(string TenantId, string Id), EvidencePreviewResourceSnapshot> _evidencePreviews = new();
    private readonly ConcurrentDictionary<(string TenantId, string Id), ReportResourceSnapshot> _reports = new();

    public DefaultAgentControlPlaneToolService(
        IAgentToolManifestProvider manifestProvider,
        IAgentToolAuthorizationService authorizationService,
        IAgentToolInvocationAuditor auditor,
        DraftStore draftStore,
        DraftValidator draftValidator,
        DraftReviewService draftReviewService,
        DraftMaterializer draftMaterializer,
        IMetadataContextPackBuilder contextPackBuilder,
        IDescriptorCatalog descriptorCatalog,
        IDescriptorRelationshipProvider relationshipProvider,
        IDescriptorTopologyBuilder topologyBuilder,
        IDescriptorPackageBuilder packageBuilder,
        ILogger<DefaultAgentControlPlaneToolService> logger,
        IDescriptorStableHashBuilder hashBuilder,
        IDescriptorReviewReportBuilder reportBuilder,
        IDescriptorReviewReportRenderer reportRenderer,
        IDescriptorActivationRequestService activationRequestService,
        IActivationReviewOrchestrator activationReviewOrchestrator,
        IActivationBindingArtifactResolver artifactResolver,
        AgentToolAuthorizationOptions? authorizationOptions = null)
    {
        _manifestProvider = manifestProvider;
        _authorizationService = authorizationService;
        _auditor = auditor;
        _draftStore = draftStore;
        _draftValidator = draftValidator;
        _draftReviewService = draftReviewService;
        _draftMaterializer = draftMaterializer;
        _contextPackBuilder = contextPackBuilder;
        _descriptorCatalog = descriptorCatalog;
        _relationshipProvider = relationshipProvider;
        _topologyBuilder = topologyBuilder;
        _packageBuilder = packageBuilder;
        _hashBuilder = hashBuilder;
        _logger = logger;
        _reportBuilder = reportBuilder;
        _reportRenderer = reportRenderer;
        _activationRequestService = activationRequestService;
        _activationReviewOrchestrator = activationReviewOrchestrator;
        _artifactResolver = artifactResolver;
        _resourceResolver = new AgentControlPlaneResourceResolver(draftStore, descriptorCatalog);
        _topologyProjector = new AgentTopologyVisibilityProjector();
        _artifactProjector = new AgentDraftArtifactVisibilityProjector(_topologyProjector, topologyBuilder);
        var capturedOptions = authorizationOptions ?? AgentToolAuthorizationOptions.ProductionDefaults;
        _optionsFactory = () => capturedOptions;
    }

    // ── Helpers ──

    private async Task<AgentToolResult<T>> ExecuteAsync<T>(
        AgentToolInvocationContext context,
        string expectedToolName,
        string permissionName,
        Func<AgentDescriptorVisibilityScope, CancellationToken, Task<AgentToolResult<T>>> action,
        CancellationToken ct)
        where T : class
    {
        // 0. Tool name integrity check: the caller-supplied ToolName must match
        //    the expected tool name for this facade method.
        if (!StringComparer.Ordinal.Equals(context.ToolName, expectedToolName))
        {
            var mismatchDiag = new AgentToolDiagnostic
            {
                Code = AgentToolDiagnosticCodes.ToolNameMismatchValue,
                Severity = AgentToolDiagnosticSeverity.Blocker,
                Message = $"Invocation context tool '{context.ToolName}' does not match expected tool '{expectedToolName}'."
            };
            var mismatchAudit = BuildAudit(context with { ToolName = expectedToolName },
                AgentToolResultStatus.InvalidRequest, [mismatchDiag]);
            await _auditor.RecordAsync(mismatchAudit, ct);
            return AgentToolResult<T>.InvalidRequest([mismatchDiag], mismatchAudit);
        }

        // 1. Manifest lookup
        var tool = _manifestProvider.GetToolByName(expectedToolName);
        if (tool is null)
        {
            var notFoundDiag = new AgentToolDiagnostic
            {
                Code = AgentToolDiagnosticCodes.ToolNotFoundValue,
                Severity = AgentToolDiagnosticSeverity.Warning,
                Message = $"Tool '{expectedToolName}' is not a known Agent Control Plane tool."
            };
            var notFoundAudit = BuildAudit(context, AgentToolResultStatus.NotFound, [notFoundDiag]);
            await _auditor.RecordAsync(notFoundAudit, ct);
            return new AgentToolResult<T>
            {
                Status = AgentToolResultStatus.NotFound,
                Value = null,
                Diagnostics = [notFoundDiag],
                AuditRecord = notFoundAudit
            };
        }

        // 2. Coarse authorization — permission/category/mode checks without descriptor kind.
        //    This validates tool identity, permission, actor, and mode-based defaults
        //    before any resource access (store/catalog reads).
        var coarsePermission = new AgentToolPermissionRequirement
        {
            PermissionName = permissionName,
            Description = tool.Description,
            ToolCategory = tool.Category,
            IsReadOnly = tool.IsReadOnly
        };
        var authResult = await _authorizationService.AuthorizeAsync(context, coarsePermission, expectedToolName, ct);
        if (!authResult.IsAllowed)
        {
            var deniedAudit = BuildAudit(context, AgentToolResultStatus.Denied, authResult.DenialDiagnostics);
            await _auditor.RecordAsync(deniedAudit, ct);
            return AgentToolResult<T>.Denied(authResult.DenialDiagnostics, deniedAudit);
        }

        // 3. Create immutable visibility scope after coarse authorization.
        //    The scope captures the tenant and effective kind policy snapshot.
        var options = _optionsFactory();
        var evaluator = new AgentDescriptorKindPolicyEvaluator(options);
        var scope = new AgentDescriptorVisibilityScope(context.TenantId, evaluator);

        // 4. Execute action with visibility scope and cancellation token.
        //    The action is responsible for resolving resources, applying kind visibility
        //    checks, and reusing snapshots for both authorization and execution.
        try
        {
            var result = await action(scope, ct);

            // Ensure the audit record is persisted if the action did not do so.
            // DenyIfInvisible produces results with audit records but does not
            // persist them (it is synchronous); other paths persist directly.
            if (result.AuditRecord is not null)
            {
                // The auditor is idempotent for the same audit ID, so
                // double-recording is safe — the auditor deduplicates.
                await _auditor.RecordAsync(result.AuditRecord, ct);
            }

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool '{ToolName}' invocation failed", expectedToolName);
            var errorDiag = new AgentToolDiagnostic
            {
                Code = AgentToolDiagnosticCodes.ToolInvocationFailedValue,
                Severity = AgentToolDiagnosticSeverity.Error,
                Message = $"Tool '{expectedToolName}' invocation failed: {ex.Message}"
            };
            var errorAudit = BuildAudit(context, AgentToolResultStatus.Failed, [errorDiag]);
            await _auditor.RecordAsync(errorAudit, ct);
            return AgentToolResult<T>.Failed([errorDiag], errorAudit);
        }
    }

    /// <summary>
    /// Returns a denial result if the given descriptor kind is not visible under the scope.
    /// Returns null if the kind is visible (caller should proceed).
    /// The denial audit record is embedded in the returned result. Callers returning
    /// this result through <see cref="ExecuteAsync{T}"/> must ensure the audit is
    /// persisted — either by returning the denial result directly (carried in
    /// <see cref="AgentToolResult{T}.AuditRecord"/>) or by calling
    /// <see cref="RecordAndReturn{T}"/> with the denial result.
    /// </summary>
    private AgentToolResult<T>? DenyIfInvisible<T>(
        AgentToolInvocationContext context,
        AgentDescriptorVisibilityScope scope,
        DescriptorKind kind)
        where T : class
    {
        var decision = scope.EvaluateExplicit(kind);
        if (decision == AgentDescriptorKindDecision.Visible)
            return null;

        var code = decision == AgentDescriptorKindDecision.Denied
            ? "DESC_KIND_DENIED"
            : "AUTHORIZATION_CONTEXT_UNAVAILABLE";

        var diagnostic = new AgentToolDiagnostic
        {
            Code = code,
            Severity = AgentToolDiagnosticSeverity.Error,
            Message = decision == AgentDescriptorKindDecision.Denied
                ? "The requested descriptor kind is not visible to this invocation."
                : "The descriptor kind could not be validated and is denied by fail-closed policy."
        };
        var audit = BuildAudit(context, AgentToolResultStatus.Denied, [diagnostic]);

        return AgentToolResult<T>.Denied([diagnostic], audit);
    }

    private AgentToolInvocationAuditRecord BuildAudit(
        AgentToolInvocationContext context,
        AgentToolResultStatus status,
        IReadOnlyList<AgentToolDiagnostic> diagnostics)
    {
        return new AgentToolInvocationAuditRecord
        {
            AuditId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTimeOffset.UtcNow,
            Context = context,
            ResultStatus = status,
            Diagnostics = diagnostics
        };
    }

    private async Task<AgentToolResult<T>> RecordAndReturn<T>(
        AgentToolInvocationContext context,
        AgentToolResult<T> result,
        AgentToolInvocationAuditRecord? baseAudit = null,
        CancellationToken ct = default)
        where T : class
    {
        var audit = baseAudit ?? BuildAudit(context, result.Status, result.Diagnostics);
        await _auditor.RecordAsync(audit, ct);
        return result with { AuditRecord = audit };
    }

    // ── Draft payload projection helpers ──

    /// <summary>
    /// Project a <see cref="Draft"/> to <see cref="AgentDescriptorDraftDto"/> using
    /// the generated <see cref="AgentDraftPayloadProjection.FromDomain"/> to map the payload.
    /// </summary>
    private static AgentDescriptorDraftDto? BuildDraftDto(Draft draft)
    {
        var payloadResult = AgentDraftPayloadProjection.FromDomain(draft.Payload);
        if (!payloadResult.IsSuccess)
            return null;

        var dto = new AgentDescriptorDraftDto
        {
            TenantId = draft.TenantId,
            DraftId = draft.DraftId,
            DescriptorKind = draft.DescriptorKind,
            DescriptorId = draft.DescriptorId,
            Operation = draft.Operation,
            AuthorKind = draft.AuthorKind,
            AuthorId = draft.AuthorId,
            CreatedAt = draft.CreatedAt,
            Payload = payloadResult.Value!,
            BaseVersion = draft.BaseVersion,
            ProposedVersion = draft.ProposedVersion,
            Intent = draft.Intent,
            Rationale = draft.Rationale,
            CorrelationId = draft.CorrelationId,
            Source = draft.Source,
            Metadata = draft.Metadata,
            Status = draft.Status,
        };
        return dto;
    }

    /// <summary>
    /// Converts an <see cref="AgentDraftContractError"/> to an <see cref="AgentToolDiagnostic"/>.
    /// </summary>
    private static AgentToolDiagnostic ConvertErrorToDiagnostic(AgentDraftContractError error)
        => new()
        {
            Code = error.Code,
            Severity = AgentToolDiagnosticSeverity.Error,
            Message = error.Message,
        };

    // ── Aggregate visibility helpers ──

    /// <summary>
    /// Non-probing diagnostic emitted whenever the invocation scope has active
    /// descriptor kind restrictions. The message intentionally contains no
    /// kind name, count, or any information a caller could use to probe
    /// whether a denied descriptor kind exists in the catalog.
    /// </summary>
    private static readonly IReadOnlyList<AgentToolDiagnostic> SecurityTrimmedDiagnostics =
    [
        new AgentToolDiagnostic
        {
            Code = AgentToolDiagnosticCodes.ResultsSecurityTrimmedValue,
            Severity = AgentToolDiagnosticSeverity.Info,
            Message = "Results reflect the invocation's descriptor visibility scope."
        }
    ];

    /// <summary>
    /// Records a failed aggregate construction and returns the failure result.
    /// Used when a catalog or store returns data that cannot be safely
    /// projected into the visible universe.
    /// </summary>
    private async Task<AgentToolResult<T>> RecordAggregateFailure<T>(
        AgentToolInvocationContext context, string code, CancellationToken ct)
        where T : class
    {
        var diagnostic = new AgentToolDiagnostic
        {
            Code = code,
            Severity = AgentToolDiagnosticSeverity.Error,
            Message = "The visible aggregate could not be constructed safely."
        };
        var audit = BuildAudit(context, AgentToolResultStatus.Failed, [diagnostic]);
        await _auditor.RecordAsync(audit, ct);
        return AgentToolResult<T>.Failed([diagnostic], audit);
    }

    // ── Wave 1 — Context / Read ──

    /// <summary>
    /// Shared implementation for building a metadata or runtime scenario context
    /// pack from the visible descriptor universe. Resolves focus refs, validates
    /// kind visibility for explicit IncludeKinds, and builds topology/context
    /// from visible descriptors only.
    /// </summary>
    private async Task<AgentToolResult<MetadataContextPack>> BuildContextPackAsync(
        AgentToolInvocationContext context,
        MetadataContextPackRequest request,
        AgentDescriptorVisibilityScope scope,
        CancellationToken ct)
    {
        var universeResult = AgentVisibleDescriptorUniverse.TryCreate(
            _descriptorCatalog.GetAll(), scope);
        if (!universeResult.IsSuccess)
            return await RecordAggregateFailure<MetadataContextPack>(context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);
        var universe = universeResult.Universe!;

        // Resolve every focus ref; reject absent or denied refs
        if (request.FocusDescriptors is not null)
        {
            foreach (var focusRef in request.FocusDescriptors)
            {
                var focusResolution = _resourceResolver.ResolveDescriptor(focusRef, scope, universe.AllTenantDescriptors);
                if (focusResolution.Status == ResourceResolutionStatus.NotFound)
                {
                    return await RecordAndReturn(context,
                        AgentToolResult<MetadataContextPack>.NotFound(
                            $"Focus descriptor '{focusRef.FullId}' not found."));
                }
                if (focusResolution.Status == ResourceResolutionStatus.Ambiguous)
                {
                    var ambigDiag = new AgentToolDiagnostic
                    {
                        Code = AgentToolDiagnosticCodes.DescriptorRefAmbiguousValue,
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Focus descriptor ref '{focusRef.FullId}' is ambiguous."
                    };
                    var ambigAudit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [ambigDiag]);
                    await _auditor.RecordAsync(ambigAudit, ct);
                    return AgentToolResult<MetadataContextPack>.InvalidRequest([ambigDiag], ambigAudit);
                }

                var focusSnapshot = focusResolution.Snapshot!;
                var denyResult = DenyIfInvisible<MetadataContextPack>(context, scope, focusSnapshot.Descriptor.Kind);
                if (denyResult is not null)
                    return denyResult;
            }
        }

        // Evaluate every explicit IncludeKinds entry
        if (request.IncludeKinds is not null)
        {
            foreach (var explicitKind in request.IncludeKinds)
            {
                if (!scope.IsVisible(explicitKind))
                {
                    var denyDiag = new AgentToolDiagnostic
                    {
                        Code = AgentToolDiagnosticCodes.DescKindDeniedValue,
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"IncludeKinds value '{explicitKind}' is not visible to this invocation."
                    };
                    var denyAudit = BuildAudit(context, AgentToolResultStatus.Denied, [denyDiag]);
                    await _auditor.RecordAsync(denyAudit, ct);
                    return AgentToolResult<MetadataContextPack>.Denied([denyDiag], denyAudit);
                }
            }
        }

        // Build topology and context from visible descriptors only
        var topology = _topologyProjector.BuildVisible(universe, _topologyBuilder);
        var pack = _contextPackBuilder.Build(request, topology, universe.VisibleDescriptors);

        var toolDiags = pack.Diagnostics.Select(MapFromContextPackDiagnostic).ToList();
        if (scope.IsRestricted)
            toolDiags.AddRange(SecurityTrimmedDiagnostics);

        var audit = BuildAudit(context, AgentToolResultStatus.Success, toolDiags.AsReadOnly());
        audit = audit with { TouchedDescriptorRefs = pack.Summary.FocusRefs };
        await _auditor.RecordAsync(audit, ct);

        return AgentToolResult<MetadataContextPack>.Success(pack, toolDiags.AsReadOnly(), audit);
    }

    public async Task<AgentToolResult<MetadataContextPack>> BuildMetadataContextPackAsync(
        AgentToolInvocationContext context, MetadataContextPackRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.BuildMetadataContextPack, AgentToolPermissionNames.ContextRead, async (scope, ct) =>
        {
            return await BuildContextPackAsync(context, request, scope, ct);
        }, ct);
    }

    public async Task<AgentToolResult<MetadataContextPack>> BuildRuntimeScenarioContextPackAsync(
        AgentToolInvocationContext context, MetadataContextPackRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.BuildRuntimeScenarioContextPack, AgentToolPermissionNames.ContextRead, async (scope, ct) =>
        {
            return await BuildContextPackAsync(context, request, scope, ct);
        }, ct);
    }

    public async Task<AgentToolResult<DescriptorInfo>> GetDescriptorByRefAsync(
        AgentToolInvocationContext context, DescriptorRef descriptorRef, CancellationToken ct = default)
    {
        // Complete — snapshot reuse via _resourceResolver
        return await ExecuteAsync(context, AgentToolName.GetDescriptorByRef, AgentToolPermissionNames.DescriptorRead, async (scope, ct) =>
        {
            // Resolve against visible descriptors only — prevents ambiguous ref
            // from leaking existence of denied descriptor versions.
            var resolution = _resourceResolver.ResolveDescriptor(descriptorRef, scope);

            if (resolution.Status == ResourceResolutionStatus.Ambiguous)
            {
                // Ambiguous: multiple candidates exist for an unpinned ref.
                // Do NOT list versions (that would leak existence information
                // before the kind visibility check). Require the caller to
                // specify a version without revealing which versions exist.
                var ambiguousDiag = new AgentToolDiagnostic
                {
                    Code = AgentToolDiagnosticCodes.DescriptorRefAmbiguousValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Descriptor ref '{descriptorRef.FullId}' is ambiguous. Specify a version to disambiguate."
                };
                var ambiguousAudit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [ambiguousDiag]);
                await _auditor.RecordAsync(ambiguousAudit, ct);
                return AgentToolResult<DescriptorInfo>.InvalidRequest([ambiguousDiag], ambiguousAudit);
            }

            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DescriptorInfo>.NotFound(
                        $"Descriptor '{descriptorRef.FullId}' not found."));
            }

            var snapshot = resolution.Snapshot!;

            // Kind visibility check on the resolved snapshot
            var denyResult = DenyIfInvisible<DescriptorInfo>(context, scope, snapshot.Descriptor.Kind);
            if (denyResult is not null)
                return denyResult;

            // Build result from snapshot — no second catalog read
            var hashes = _hashBuilder.Build(snapshot.Descriptor);
            var info = new DescriptorInfo
            {
                Ref = snapshot.Ref,
                Kind = snapshot.Descriptor.Kind,
                Name = snapshot.Descriptor.Name,
                State = snapshot.Descriptor.State,
                ContractHash = hashes.ContractHash.Value,
                DefinitionHash = hashes.DefinitionHash.Value
            };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDescriptorRefs = [snapshot.Ref] };
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<DescriptorInfo>.Success(info, audit);
        }, ct);
    }

    public async Task<AgentToolResult<DescriptorSearchResult>> SearchDescriptorsAsync(
        AgentToolInvocationContext context, DescriptorSearchRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.SearchDescriptors, AgentToolPermissionNames.DescriptorSearch, async (scope, ct) =>
        {
            // If the caller explicitly specifies a denied kind, return Denied
            // rather than an empty Success. Spec §6.2: "If a caller explicitly
            // supplies a denied DescriptorKind filter, return Denied."
            if (request.Kind.HasValue && !scope.IsVisible(request.Kind.Value))
            {
                var denyResult = DenyIfInvisible<DescriptorSearchResult>(context, scope, request.Kind.Value);
                if (denyResult is not null)
                    return denyResult;
            }

            // Build visible universe — filters out denied descriptor kinds
            var universeResult = AgentVisibleDescriptorUniverse.TryCreate(
                _descriptorCatalog.GetAll(), scope);
            if (!universeResult.IsSuccess)
                return await RecordAggregateFailure<DescriptorSearchResult>(context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);
            var universe = universeResult.Universe!;

            // Apply request filters on visible descriptors
            IEnumerable<IDescriptor> results = universe.VisibleDescriptors;

            if (request.Namespace is not null)
                results = results.Where(d => d.Namespace == request.Namespace);
            if (request.Kind.HasValue)
                results = results.Where(d => d.Kind == request.Kind.Value);
            if (request.NameContains is not null)
                results = results.Where(d => d.Name.Contains(request.NameContains, StringComparison.Ordinal));
            if (request.State.HasValue)
                results = results.Where(d => d.State == request.State.Value);

            // Order deterministically before computing total and truncation
            var ordered = results
                .OrderBy(d => d.Namespace, StringComparer.Ordinal)
                .ThenBy(d => d.Id, StringComparer.Ordinal)
                .ThenBy(d => d is IVersionedDescriptor vd ? vd.Version : 0)
                .ToList();

            var totalCount = ordered.Count;
            var wasTruncated = totalCount > request.MaxResults;
            var truncated = ordered.Take(request.MaxResults).ToList();

            var infos = truncated.Select(d =>
            {
                var refWithVersion = d is IVersionedDescriptor vd
                    ? new DescriptorRef(d.Namespace, d.Id, vd.Version)
                    : new DescriptorRef(d.Namespace, d.Id);
                var hashes = _hashBuilder.Build(d);
                return new DescriptorInfo
                {
                    Ref = refWithVersion,
                    Kind = d.Kind,
                    Name = d.Name,
                    State = d.State,
                    ContractHash = hashes.ContractHash.Value,
                    DefinitionHash = hashes.DefinitionHash.Value
                };
            }).ToList().AsReadOnly();

            var searchResult = new DescriptorSearchResult
            {
                Descriptors = infos,
                TotalCount = totalCount,
                WasTruncated = wasTruncated
            };

            // Build diagnostics: security-trimmed when restricted, truncation info
            var diags = new List<AgentToolDiagnostic>();
            if (scope.IsRestricted)
                diags.AddRange(SecurityTrimmedDiagnostics);
            if (wasTruncated)
            {
                diags.Add(new AgentToolDiagnostic
                {
                    Code = AgentToolDiagnosticCodes.SearchTruncatedValue,
                    Severity = AgentToolDiagnosticSeverity.Info,
                    Message = "Search results were truncated to the maximum allowed count."
                });
            }

            var audit = BuildAudit(context, AgentToolResultStatus.Success, diags.AsReadOnly());
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<DescriptorSearchResult>.Success(searchResult, diags.AsReadOnly(), audit);
        }, ct);
    }

    public async Task<AgentToolResult<DescriptorRelationshipsResult>> ListDescriptorRelationshipsAsync(
        AgentToolInvocationContext context, DescriptorRef descriptorRef, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.ListDescriptorRelationships, AgentToolPermissionNames.DescriptorRead, async (scope, ct) =>
        {
            // Build visible universe — resolved subject must be visible
            var universeResult = AgentVisibleDescriptorUniverse.TryCreate(
                _descriptorCatalog.GetAll(), scope);
            if (!universeResult.IsSuccess)
                return await RecordAggregateFailure<DescriptorRelationshipsResult>(context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);
            var universe = universeResult.Universe!;

            // Resolve the subject using the same catalog snapshot used to
            // build the universe — eliminates TOCTOU between auth and construction.
            var resolution = _resourceResolver.ResolveDescriptor(descriptorRef, scope, universe.AllTenantDescriptors);
            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DescriptorRelationshipsResult>.NotFound(
                        $"Descriptor '{descriptorRef.FullId}' not found."));
            }
            if (resolution.Status == ResourceResolutionStatus.Ambiguous)
            {
                var ambiguousDiag = new AgentToolDiagnostic
                {
                    Code = AgentToolDiagnosticCodes.DescriptorRefAmbiguousValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Descriptor ref '{descriptorRef.FullId}' is ambiguous. Specify a version to disambiguate."
                };
                var ambiguousAudit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [ambiguousDiag]);
                await _auditor.RecordAsync(ambiguousAudit, ct);
                return AgentToolResult<DescriptorRelationshipsResult>.InvalidRequest([ambiguousDiag], ambiguousAudit);
            }

            var snapshot = resolution.Snapshot!;
            var denyResult = DenyIfInvisible<DescriptorRelationshipsResult>(context, scope, snapshot.Descriptor.Kind);
            if (denyResult is not null)
                return denyResult;

            // Build topology from visible descriptors only
            var topology = _topologyProjector.BuildVisible(universe, _topologyBuilder);
            var resolvedRef = snapshot.Ref;

            if (!topology.Contains(resolvedRef))
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DescriptorRelationshipsResult>.NotFound(
                        $"Descriptor '{resolvedRef.FullId}' not found in visible topology."));
            }

            var node = topology.FindNode(resolvedRef)!;

            var dependencies = node.OutgoingEdgeIndices
                .Select(i => topology.Edges[i])
                .Select(e => new DescriptorRelationship(e.From, e.To, e.Kind, e.Role, e.SourcePath, e.Strength, e.IsRuntimeBinding))
                .ToList().AsReadOnly();

            var dependents = node.IncomingEdgeIndices
                .Select(i => topology.Edges[i])
                .Select(e => new DescriptorRelationship(e.From, e.To, e.Kind, e.Role, e.SourcePath, e.Strength, e.IsRuntimeBinding))
                .ToList().AsReadOnly();

            var result = new DescriptorRelationshipsResult
            {
                Subject = resolvedRef,
                Dependencies = dependencies,
                Dependents = dependents
            };

            var diags = scope.IsRestricted ? SecurityTrimmedDiagnostics : [];
            var audit = BuildAudit(context, AgentToolResultStatus.Success, diags) with
            {
                TouchedDescriptorRefs = [resolvedRef]
            };
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<DescriptorRelationshipsResult>.Success(result, diags, audit);
        }, ct);
    }

    public async Task<AgentToolResult<TopologySummaryResult>> GetTopologySummaryAsync(
        AgentToolInvocationContext context, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.GetTopologySummary, AgentToolPermissionNames.ContextRead, async (scope, ct) =>
        {
            var universeResult = AgentVisibleDescriptorUniverse.TryCreate(
                _descriptorCatalog.GetAll(), scope);
            if (!universeResult.IsSuccess)
                return await RecordAggregateFailure<TopologySummaryResult>(context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);
            var universe = universeResult.Universe!;
            var topology = _topologyProjector.BuildVisible(universe, _topologyBuilder);

            var result = new TopologySummaryResult
            {
                TotalNodeCount = topology.Nodes.Count,
                TotalEdgeCount = topology.Edges.Count,
                NodeCountsByKind = topology.Nodes.Values
                    .GroupBy(n => n.Kind)
                    .ToDictionary(g => g.Key, g => g.Count()),
                EdgeCountsByKind = topology.Edges
                    .GroupBy(e => e.Kind)
                    .ToDictionary(g => g.Key, g => g.Count()),
                TopologyDiagnostics = topology.Diagnostics.All
                    .Select(d => new AgentToolDiagnostic
                    {
                        Code = d.Code,
                        Severity = MapTopologyDiagnosticSeverity(d.Severity),
                        Message = d.Message
                    }).ToList().AsReadOnly()
            };

            var diags = scope.IsRestricted ? SecurityTrimmedDiagnostics : [];
            var audit = BuildAudit(context, AgentToolResultStatus.Success, diags);
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<TopologySummaryResult>.Success(result, diags, audit);
        }, ct);
    }

    // ── Wave 2 — Draft ──

    public async Task<AgentToolResult<AgentDescriptorDraftDto>> CreateDescriptorDraftAsync(
        AgentToolInvocationContext context, CreateDescriptorDraftRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.CreateDescriptorDraft, AgentToolPermissionNames.DraftCreate, async (scope, ct) =>
        {
            // Kind visibility check on the request's declared kind
            var denyResult = DenyIfInvisible<AgentDescriptorDraftDto>(context, scope, request.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            // Consistency check: declared kind must match payload discriminator
            if (request.DescriptorKind != request.Payload.Discriminator)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest(
                    [
                        new AgentToolDiagnostic
                        {
                            Code = "KindDiscriminatorMismatch",
                            Message = $"DescriptorKind ({request.DescriptorKind}) does not match Payload.Discriminator ({request.Payload.Discriminator}). " +
                                      "The declared kind and payload type must be consistent.",
                            Severity = AgentToolDiagnosticSeverity.Error,
                        }
                    ]));
            }

            // One-of invariant check: discriminator must match exactly one populated sub-record
            var (isCreateValid, validationError) = AgentDraftPayloadProjection.TryValidatePayload(request.Payload);
            if (!isCreateValid)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest([ConvertErrorToDiagnostic(validationError!)]));
            }

            var createResult = AgentDraftPayloadProjection.Create(request.Payload);
            if (!createResult.IsSuccess)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest(createResult.Errors.Select(ConvertErrorToDiagnostic).ToList()));
            }
            var domainPayload = createResult.Value!;

            var draft = new Draft
            {
                TenantId = context.TenantId,
                DraftId = Guid.NewGuid().ToString("N"),
                DescriptorKind = request.DescriptorKind,
                DescriptorId = request.DescriptorId,
                Operation = request.Operation,
                AuthorKind = MapActorKind(context.ActorKind),
                AuthorId = context.ActorId,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = domainPayload,
                BaseVersion = request.BaseVersion,
                ProposedVersion = request.ProposedVersion,
                Intent = request.Intent,
                Rationale = request.Rationale,
                CorrelationId = request.CorrelationId,
                Source = context.InvocationSource.ToString(),
                Metadata = request.Metadata,
                Status = DraftAbstractions.DescriptorDraftStatus.Created
            };

            await _draftStore.SaveAsync(draft, ct);

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDraftIds = [draft.DraftId] };
            await _auditor.RecordAsync(audit, ct);
            var dto = BuildDraftDto(draft);
            if (dto is null)
            {
                return AgentToolResult<AgentDescriptorDraftDto>.Failed(
                    [new AgentToolDiagnostic { Code = AgentToolDiagnosticCodes.DraftProjectionFailedValue, Severity = AgentToolDiagnosticSeverity.Error, Message = "Failed to project draft to DTO." }],
                    BuildAudit(context, AgentToolResultStatus.Failed, [new AgentToolDiagnostic { Code = AgentToolDiagnosticCodes.DraftProjectionFailedValue, Severity = AgentToolDiagnosticSeverity.Error, Message = "Failed to project draft to DTO." }]));
            }
            return AgentToolResult<AgentDescriptorDraftDto>.Success(dto, audit);
        }, ct);
    }

    public async Task<AgentToolResult<AgentDescriptorDraftDto>> UpdateDescriptorDraftAsync(
        AgentToolInvocationContext context, UpdateDescriptorDraftRequest request, CancellationToken ct = default)
    {
        // Complete — SingleDraft: snapshot reuse via _resourceResolver
        return await ExecuteAsync(context, AgentToolName.UpdateDescriptorDraft, AgentToolPermissionNames.DraftUpdate, async (scope, ct) =>
        {
            var resolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, request.DraftId, ct);

            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<AgentDescriptorDraftDto>.NotFound($"Draft '{request.DraftId}' not found."));
            }

            var snapshot = resolution.Snapshot!;

            // Kind visibility check on the resolved snapshot
            var denyResult = DenyIfInvisible<AgentDescriptorDraftDto>(context, scope, snapshot.Draft.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            // Consistency check: if payload provided, its discriminator must match the draft's kind
            if (request.Payload is not null && snapshot.Draft.DescriptorKind != request.Payload.Discriminator)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest(
                    [
                        new AgentToolDiagnostic
                        {
                            Code = "KindDiscriminatorMismatch",
                            Message = $"Draft DescriptorKind ({snapshot.Draft.DescriptorKind}) does not match Payload.Discriminator ({request.Payload.Discriminator}). " +
                                      "The payload type must be consistent with the draft's declared kind.",
                            Severity = AgentToolDiagnosticSeverity.Error,
                        }
                    ]));
            }

            DraftAbstractions.DescriptorDraftPayload? domainPayload = null;
            if (request.Payload is not null)
            {
                var mergeResult = AgentDraftPayloadProjection.Merge(request.Payload, snapshot.Draft.Payload);
                if (!mergeResult.IsSuccess)
                {
                    return await RecordAndReturn(context,
                        AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest(mergeResult.Errors.Select(ConvertErrorToDiagnostic).ToList()));
                }
                domainPayload = mergeResult.Value!;
            }

            // Execute from snapshot — no second store read
            var updated = snapshot.Draft with
            {
                Payload = domainPayload ?? snapshot.Draft.Payload,
                ProposedVersion = request.ProposedVersion ?? snapshot.Draft.ProposedVersion,
                Intent = request.Intent ?? snapshot.Draft.Intent,
                Rationale = request.Rationale ?? snapshot.Draft.Rationale,
                Metadata = request.Metadata ?? snapshot.Draft.Metadata
            };

            await _draftStore.SaveAsync(updated, ct);

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDraftIds = [updated.DraftId] };
            await _auditor.RecordAsync(audit, ct);
            var dto = BuildDraftDto(updated);
            if (dto is null)
            {
                return AgentToolResult<AgentDescriptorDraftDto>.Failed(
                    [new AgentToolDiagnostic { Code = AgentToolDiagnosticCodes.DraftProjectionFailedValue, Severity = AgentToolDiagnosticSeverity.Error, Message = "Failed to project draft to DTO." }],
                    BuildAudit(context, AgentToolResultStatus.Failed, [new AgentToolDiagnostic { Code = AgentToolDiagnosticCodes.DraftProjectionFailedValue, Severity = AgentToolDiagnosticSeverity.Error, Message = "Failed to project draft to DTO." }]));
            }
            return AgentToolResult<AgentDescriptorDraftDto>.Success(dto, audit);
        }, ct);
    }

    public async Task<AgentToolResult<AgentDescriptorDraftDto>> GetDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        // Complete — SingleDraft: snapshot reuse via _resourceResolver
        return await ExecuteAsync(context, AgentToolName.GetDescriptorDraft, AgentToolPermissionNames.DraftRead, async (scope, ct) =>
        {
            var resolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);

            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<AgentDescriptorDraftDto>.NotFound($"Draft '{draftId}' not found."));
            }

            var snapshot = resolution.Snapshot!;

            // Kind visibility check on the resolved snapshot
            var denyResult = DenyIfInvisible<AgentDescriptorDraftDto>(context, scope, snapshot.Draft.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            // Return from snapshot — no second store read
            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDraftIds = [draftId] };
            await _auditor.RecordAsync(audit, ct);
            var dto = BuildDraftDto(snapshot.Draft);
            if (dto is null)
            {
                return AgentToolResult<AgentDescriptorDraftDto>.Failed(
                    [new AgentToolDiagnostic { Code = AgentToolDiagnosticCodes.DraftProjectionFailedValue, Severity = AgentToolDiagnosticSeverity.Error, Message = "Failed to project draft to DTO." }],
                    BuildAudit(context, AgentToolResultStatus.Failed, [new AgentToolDiagnostic { Code = AgentToolDiagnosticCodes.DraftProjectionFailedValue, Severity = AgentToolDiagnosticSeverity.Error, Message = "Failed to project draft to DTO." }]));
            }
            return AgentToolResult<AgentDescriptorDraftDto>.Success(dto, audit);
        }, ct);
    }

    public async Task<AgentToolResult<DescriptorDraftListResult>> ListDescriptorDraftsAsync(
        AgentToolInvocationContext context, DraftAbstractions.DraftQuery? query, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.ListDescriptorDrafts, AgentToolPermissionNames.DraftList, async (scope, ct) =>
        {
            // Per spec §6.2: explicitly filtering by a denied DescriptorKind
            // must return Denied, not an empty Success. Invalid (undefined)
            // DescriptorKind values return AUTHORIZATION_CONTEXT_UNAVAILABLE.
            if (query?.DescriptorKind is { } kind)
            {
                if (!AgentDescriptorKindPolicyEvaluator.IsValidDescriptorKind(kind))
                {
                    return await RecordAggregateFailure<DescriptorDraftListResult>(
                        context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);
                }
                if (!scope.IsVisible(kind))
                {
                    var denyDiag = new AgentToolDiagnostic
                    {
                        Code = AgentToolDiagnosticCodes.DescKindDeniedValue,
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Descriptor kind '{kind}' is denied for this invocation."
                    };
                    var denyAudit = BuildAudit(context, AgentToolResultStatus.Denied, [denyDiag]);
                    await _auditor.RecordAsync(denyAudit, ct);
                    return AgentToolResult<DescriptorDraftListResult>.Denied([denyDiag], denyAudit);
                }
            }

            var drafts = await _draftStore.ListAsync(context.TenantId, query, ct);

            // Reject any draft with an invalid descriptor kind
            if (drafts.Any(d => !AgentDescriptorKindPolicyEvaluator.IsValidDescriptorKind(d.DescriptorKind)))
                return await RecordAggregateFailure<DescriptorDraftListResult>(
                    context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);

            // Filter to visible descriptor kinds only
            var visible = scope.Filter(drafts, d => d.DescriptorKind);
            var result = new DescriptorDraftListResult
            {
                Drafts = visible.Select(BuildDraftDto).Where(d => d is not null).Select(d => d!).ToList().AsReadOnly(),
                TotalCount = visible.Count
            };

            var diagnostics = scope.IsRestricted ? SecurityTrimmedDiagnostics : [];
            var audit = BuildAudit(context, AgentToolResultStatus.Success, diagnostics) with
            {
                TouchedDraftIds = visible.Select(d => d.DraftId).ToList().AsReadOnly()
            };
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<DescriptorDraftListResult>.Success(result, diagnostics, audit);
        }, ct);
    }

    public async Task<AgentToolResult<AgentDescriptorDraftDto>> CancelDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        // Complete — SingleDraft: snapshot reuse via _resourceResolver
        return await ExecuteAsync(context, AgentToolName.CancelDescriptorDraft, AgentToolPermissionNames.DraftCancel, async (scope, ct) =>
        {
            var resolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);

            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<AgentDescriptorDraftDto>.NotFound($"Draft '{draftId}' not found."));
            }

            var snapshot = resolution.Snapshot!;

            // Kind visibility check on the resolved snapshot
            var denyResult = DenyIfInvisible<AgentDescriptorDraftDto>(context, scope, snapshot.Draft.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            // Execute from snapshot — no second store read
            var cancelled = snapshot.Draft with { Status = DraftAbstractions.DescriptorDraftStatus.Cancelled };
            await _draftStore.SaveAsync(cancelled, ct);

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDraftIds = [draftId] };
            await _auditor.RecordAsync(audit, ct);
            var dto = BuildDraftDto(cancelled);
            if (dto is null)
            {
                return AgentToolResult<AgentDescriptorDraftDto>.Failed(
                    [new AgentToolDiagnostic { Code = AgentToolDiagnosticCodes.DraftProjectionFailedValue, Severity = AgentToolDiagnosticSeverity.Error, Message = "Failed to project draft to DTO." }],
                    BuildAudit(context, AgentToolResultStatus.Failed, [new AgentToolDiagnostic { Code = AgentToolDiagnosticCodes.DraftProjectionFailedValue, Severity = AgentToolDiagnosticSeverity.Error, Message = "Failed to project draft to DTO." }]));
            }
            return AgentToolResult<AgentDescriptorDraftDto>.Success(dto, audit);
        }, ct);
    }

    public async Task<AgentToolResult<DraftComparisonResult>> CompareDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        // Complete — Nested: snapshot reuse via _resourceResolver
        return await ExecuteAsync(context, AgentToolName.CompareDescriptorDraft, AgentToolPermissionNames.DraftRead, async (scope, ct) =>
        {
            var resolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);
            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DraftComparisonResult>.NotFound($"Draft '{draftId}' not found."));
            }

            var snapshot = resolution.Snapshot!;

            var denyResult = DenyIfInvisible<DraftComparisonResult>(context, scope, snapshot.Draft.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            var currentActive = _descriptorCatalog.Get(snapshot.Draft.DescriptorId);

            // Validate full descriptor identity: the catalog may return a
            // descriptor by bare ID, but we must verify namespace and BaseVersion
            // match. Bare-ID-only matching risks comparing a v1-targeting draft
            // against a v2 descriptor, leaking future-version data.
            var draftDescriptor = snapshot.Draft.Payload.GetDescriptor();
            var namespaceMismatch = currentActive is not null &&
                !string.Equals(currentActive.Namespace, draftDescriptor.Namespace, StringComparison.Ordinal);
            if (namespaceMismatch)
            {
                currentActive = null; // Wrong namespace — treat as no match
            }

            // BaseVersion verification: if the draft targets a specific version
            // (e.g. updating v1), ensure the catalog returned that version.
            // If the active version doesn't match BaseVersion, treat as no match.
            var baseVersionMismatch = currentActive is not null &&
                snapshot.Draft.BaseVersion is not null &&
                currentActive is IVersionedDescriptor vd &&
                (!int.TryParse(snapshot.Draft.BaseVersion, out var baseVer) ||
                 vd.Version != baseVer);
            if (baseVersionMismatch)
            {
                currentActive = null; // BaseVersion doesn't match — compare nothing
            }

            // If the active descriptor is of a denied kind, mask it —
            // CurrentActiveDescriptor must not leak denied descriptor data.
            IDescriptor? visibleActive = null;
            var differences = new List<DraftDifference>();
            if (currentActive is not null && scope.IsVisible(currentActive.Kind))
            {
                visibleActive = currentActive;
                differences.Add(new DraftDifference
                {
                    Path = "Name",
                    CurrentValue = currentActive.Name ?? "",
                    ProposedValue = draftDescriptor.Name ?? "",
                    Kind = DraftDifferenceKind.Modified
                });
            }
            else if (namespaceMismatch || baseVersionMismatch || currentActive is not null)
            {
                // Active exists but namespace mismatch or kind is denied —
                // show draft as Added, no current values.
                differences.Add(new DraftDifference
                {
                    Path = "Descriptor",
                    CurrentValue = "",
                    ProposedValue = draftDescriptor.Name ?? "",
                    Kind = DraftDifferenceKind.Added
                });
            }

            var compareDraftDto = BuildDraftDto(snapshot.Draft);
            if (compareDraftDto is null)
            {
                return AgentToolResult<DraftComparisonResult>.Failed(
                    [new AgentToolDiagnostic { Code = AgentToolDiagnosticCodes.DraftProjectionFailedValue, Severity = AgentToolDiagnosticSeverity.Error, Message = "Failed to project draft for comparison." }],
                    BuildAudit(context, AgentToolResultStatus.Failed, [new AgentToolDiagnostic { Code = AgentToolDiagnosticCodes.DraftProjectionFailedValue, Severity = AgentToolDiagnosticSeverity.Error, Message = "Failed to project draft for comparison." }]));
            }
            var result = new DraftComparisonResult
            {
                Draft = compareDraftDto,
                CurrentActiveDescriptor = DescriptorSummaryDtoProjection.FromDescriptor(visibleActive),
                Differences = differences.AsReadOnly()
            };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDraftIds = [draftId] };
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<DraftComparisonResult>.Success(result, audit);
        }, ct);
    }

    // ── Wave 3 — Review ──

    public async Task<AgentToolResult<DraftValidationResult>> ValidateDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        // Complete — SingleDraft: snapshot reuse via _resourceResolver
        return await ExecuteAsync(context, AgentToolName.ValidateDescriptorDraft, AgentToolPermissionNames.ReviewValidate, async (scope, ct) =>
        {
            var resolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);

            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DraftValidationResult>.NotFound($"Draft '{draftId}' not found."));
            }

            var snapshot = resolution.Snapshot!;

            // Kind visibility check on the resolved snapshot
            var denyResult = DenyIfInvisible<DraftValidationResult>(context, scope, snapshot.Draft.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            // Execute from snapshot — no second store read
            var validationResult = _draftValidator.Validate(snapshot.Draft);

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDraftIds = [draftId] };
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<DraftValidationResult>.Success(validationResult, audit);
        }, ct);
    }

    public async Task<AgentToolResult<AgentReviewResultDto>> ReviewDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.ReviewDescriptorDraft, AgentToolPermissionNames.ReviewRun, async (scope, ct) =>
        {
            var resolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);
            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<AgentReviewResultDto>.NotFound($"Draft '{draftId}' not found."));
            }

            var snapshot = resolution.Snapshot!;

            var denyResult = DenyIfInvisible<AgentReviewResultDto>(context, scope, snapshot.Draft.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            var currentInventory = _descriptorCatalog.GetAll().ToList();

            // Build visible universe before projecting review — nested artifacts
            // (topology, impact, compatibility, governance, package) must be
            // filtered through the visible descriptor identities.
            var universeResult = AgentVisibleDescriptorUniverse.TryCreate(currentInventory, scope);
            if (!universeResult.IsSuccess)
                return await RecordAggregateFailure<AgentReviewResultDto>(context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);
            var universe = universeResult.Universe!;

            var reviewResult = await _draftReviewService.ReviewAsync(snapshot.Draft, universe.VisibleDescriptors, ct);

            // Project nested review data through visibility.
            // Null return means projection failure — caller must return
            // Failed and NOT persist review or mutate draft state.
            var projectedReview = _artifactProjector.ProjectReview(reviewResult, scope, universe);
            if (projectedReview is null)
            {
                return await RecordAggregateFailure<AgentReviewResultDto>(
                    context, "PACKAGE_PROJECTION_FAILURE", ct);
            }

            var reviewId = Guid.NewGuid().ToString("N");
            _reviewResults[(context.TenantId, reviewId)] = new ReviewResourceSnapshot(projectedReview, snapshot.Draft, DateTimeOffset.UtcNow);

            // Store review hashes for evidence recheck
            var sourceReviewHash = ComputeSourceReviewHash(reviewResult);
            var manifestHash = ComputeReviewManifestHash(reviewResult);
            _artifactResolver.StoreReviewHashes(context.TenantId, reviewId, sourceReviewHash, manifestHash);

            var reviewed = snapshot.Draft with { Status = DraftAbstractions.DescriptorDraftStatus.Reviewed };
            await _draftStore.SaveAsync(reviewed, ct);

            var toolDiags = projectedReview.Diagnostics.Select(MapFromDraftDiagnostic).ToList();
            var audit = BuildAudit(context, AgentToolResultStatus.Success, toolDiags) with
            {
                TouchedDraftIds = [draftId],
                TouchedReviewResultIds = [reviewId]
            };
            await _auditor.RecordAsync(audit, ct);
            var reviewDto = AgentReviewResultDtoProjection.Project(projectedReview);
            return AgentToolResult<AgentReviewResultDto>.Success(reviewDto, audit);
        }, ct);
    }

    public async Task<AgentToolResult<AgentReviewResultDto>> GetDraftReviewResultAsync(
        AgentToolInvocationContext context, string reviewResultId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.GetDraftReviewResult, AgentToolPermissionNames.ReviewRead, async (scope, ct) =>
        {
            if (!_reviewResults.TryGetValue((context.TenantId, reviewResultId), out var snapshot))
            {
                return await RecordAndReturn(context,
                    AgentToolResult<AgentReviewResultDto>.NotFound($"Review result '{reviewResultId}' not found."));
            }

            var denyResult = DenyIfInvisible<AgentReviewResultDto>(context, scope, snapshot.Owner.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []) with
            {
                TouchedReviewResultIds = [reviewResultId]
            };
            await _auditor.RecordAsync(audit, ct);
            var reviewDto = AgentReviewResultDtoProjection.Project(snapshot.Review);
            return AgentToolResult<AgentReviewResultDto>.Success(reviewDto, audit);
        }, ct);
    }

    public async Task<AgentToolResult<ReviewResultListResult>> ListDraftReviewResultsAsync(
        AgentToolInvocationContext context, string? draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.ListDraftReviewResults, AgentToolPermissionNames.ReviewRead, async (scope, ct) =>
        {
            // When an explicit draftId is provided, it is an explicit target.
            // Spec §6.2: "If the target resolves to a denied kind, return Denied."
            if (draftId is not null)
            {
                var ownerResolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);
                if (ownerResolution.Status == ResourceResolutionStatus.NotFound)
                {
                    return await RecordAndReturn(context,
                        AgentToolResult<ReviewResultListResult>.NotFound($"Draft '{draftId}' not found."));
                }
                var denyResult = DenyIfInvisible<ReviewResultListResult>(
                    context, scope, ownerResolution.Snapshot!.Draft.DescriptorKind);
                if (denyResult is not null)
                    return denyResult;
            }

            var reviews = _reviewResults
                .Where(kvp => kvp.Key.TenantId == context.TenantId)
                .ToList();

            if (draftId is not null)
                reviews = reviews.Where(r => r.Value.Review.DraftId == draftId).ToList();

            // Batch-load owner drafts for all stored reviews
            var owners = await _resourceResolver.ResolveOwnersAsync(
                context.TenantId, reviews.Select(r => r.Value.Review.DraftId), ct);

            if (reviews.Any(r => !owners.ContainsKey(r.Value.Review.DraftId)))
                return await RecordAggregateFailure<ReviewResultListResult>(
                    context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);

            var visible = reviews
                .Where(r => scope.IsVisible(owners[r.Value.Review.DraftId].DescriptorKind))
                .OrderBy(r => r.Value.Review.DraftId, StringComparer.Ordinal)
                .Select(r => r.Value.Review)
                .ToList().AsReadOnly();

            var result = new ReviewResultListResult { Results = visible.Select(AgentReviewResultDtoProjection.Project).ToList().AsReadOnly() };

            var diags = scope.IsRestricted ? SecurityTrimmedDiagnostics : [];
            var audit = BuildAudit(context, AgentToolResultStatus.Success, diags);
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<ReviewResultListResult>.Success(result, diags, audit);
        }, ct);
    }

    public async Task<AgentToolResult<DiagnosticExplanation>> ExplainDiagnosticsAsync(
        AgentToolInvocationContext context, ExplainDiagnosticsRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.ExplainDiagnostics, AgentToolPermissionNames.DiagnosticExplain, async (scope, ct) =>
        {
            // If a draft is referenced, verify tenant and owner visibility
            if (request.DraftId is not null)
            {
                var resolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, request.DraftId, ct);
                if (resolution.Status == ResourceResolutionStatus.NotFound)
                {
                    return await RecordAndReturn(context,
                        AgentToolResult<DiagnosticExplanation>.NotFound(
                            $"Draft '{request.DraftId}' not found."));
                }

                var denyResult = DenyIfInvisible<DiagnosticExplanation>(context, scope, resolution.Snapshot!.Draft.DescriptorKind);
                if (denyResult is not null)
                    return denyResult;
            }

            // Use allowlisted code-table — never echoes caller's code/message/path
            var entries = request.Diagnostics
                .Select(d => _explanationPolicy.Explain(d))
                .ToList().AsReadOnly();

            var result = new DiagnosticExplanation { Explanations = entries };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            if (request.DraftId is not null)
            {
                audit = audit with { TouchedDraftIds = [request.DraftId] };
            }
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<DiagnosticExplanation>.Success(result, audit);
        }, ct);
    }

    // ── Wave 3d — Review Report (Phase 7d) ──

    public async Task<AgentToolResult<DescriptorReviewReportDto>> BuildDescriptorReviewReportAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.BuildDescriptorReviewReport,
            AgentToolPermissionNames.ReviewReportBuild, async (scope, ct) =>
        {
            var draftResolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);
            if (draftResolution.Status == ResourceResolutionStatus.NotFound)
                return await RecordAndReturn(context,
                    AgentToolResult<DescriptorReviewReportDto>.NotFound($"Draft '{draftId}' not found."));

            var snapshot = draftResolution.Snapshot!;
            var denyResult = DenyIfInvisible<DescriptorReviewReportDto>(context, scope, snapshot.Draft.DescriptorKind);
            if (denyResult is not null) return denyResult;

            // Find the latest review result for this draft (most recent by timestamp)
            var reviewSnapshot = _reviewResults.Values
                .Where(r => r.Owner.DraftId == draftId && r.Owner.TenantId == context.TenantId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefault();
            if (reviewSnapshot is null)
            {
                var noReviewDiag = new AgentToolDiagnostic
                {
                    Code = AgentToolDiagnosticCodes.NoReviewResultValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"No review result found for draft '{draftId}'. Run ReviewDescriptorDraft first."
                };
                var noReviewAudit = BuildAudit(context, AgentToolResultStatus.Failed, [noReviewDiag]);
                await _auditor.RecordAsync(noReviewAudit, ct);
                return AgentToolResult<DescriptorReviewReportDto>.Failed([noReviewDiag], noReviewAudit);
            }

            var request = new DescriptorReviewReportBuildRequest
            {
                ReviewResult = reviewSnapshot.Review,
                Draft = reviewSnapshot.Owner,  // Use the draft snapshot captured at review time, not the current draft
                VisibilityApplied = true
            };

            var report = _reportBuilder.Build(request);
            _reports[(context.TenantId, report.ReportId)] = new ReportResourceSnapshot(report, reviewSnapshot.Owner);

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<DescriptorReviewReportDto>.Success(report, [], audit);
        }, ct);
    }

    public async Task<AgentToolResult<string>> RenderDescriptorReviewReportAsync(
        AgentToolInvocationContext context, DescriptorReviewReportDto report,
        DescriptorReviewReportFormat format, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.RenderDescriptorReviewReport,
            AgentToolPermissionNames.ReviewReportRender, async (scope, ct) =>
        {
            // Validate contract version
            if (report.ContractVersion != AgentControlPlaneContractVersion.Current)
            {
                var diag = new AgentToolDiagnostic
                {
                    Code = AgentToolDiagnosticCodes.UnsupportedReportContractVersionValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Report contract version '{report.ContractVersion}' is not supported. Current: '{AgentControlPlaneContractVersion.Current}'."
                };
                var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [diag]);
                await _auditor.RecordAsync(audit, ct);
                return AgentToolResult<string>.InvalidRequest([diag], audit);
            }

            var rendered = format switch
            {
                DescriptorReviewReportFormat.Markdown => _reportRenderer.RenderMarkdown(report),
                DescriptorReviewReportFormat.PlainText => _reportRenderer.RenderPlainText(report),
                _ => null
            };

            if (rendered is null)
            {
                var diag = new AgentToolDiagnostic
                {
                    Code = AgentToolDiagnosticCodes.UnsupportedReportFormatValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Report format '{format}' is not supported."
                };
                var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [diag]);
                await _auditor.RecordAsync(audit, ct);
                return AgentToolResult<string>.InvalidRequest([diag], audit);
            }

            var successAudit = BuildAudit(context, AgentToolResultStatus.Success, []);
            await _auditor.RecordAsync(successAudit, ct);
            return AgentToolResult<string>.Success(rendered, [], successAudit);
        }, ct);
    }

    // ── Wave 4 — Fix Proposal ──

    public async Task<AgentToolResult<FixProposalListResult>> SuggestDescriptorDraftFixesAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.SuggestDescriptorDraftFixes, AgentToolPermissionNames.FixSuggest, async (scope, ct) =>
        {
            var resolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);
            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<FixProposalListResult>.NotFound($"Draft '{draftId}' not found."));
            }

            var snapshot = resolution.Snapshot!;
            var denyResult = DenyIfInvisible<FixProposalListResult>(context, scope, snapshot.Draft.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            var validationResult = _draftValidator.Validate(snapshot.Draft);
            var proposals = new List<FixProposal>();

            foreach (var diag in validationResult.Diagnostics.Where(d => !IsIdentityFieldDiagnostic(d.Code)))
            {
                var proposalId = Guid.NewGuid().ToString("N");
                var actions = GenerateFixActions(diag);
                var hasActions = actions.Count > 0;
                var applicability = hasActions
                    ? FixProposalApplicability.CurrentMutableDraft
                    : FixProposalApplicability.ManualActionRequired;
                var isExecutable = hasActions
                    && applicability == FixProposalApplicability.CurrentMutableDraft
                    && actions.All(a => a.IsExecutable);
                var proposal = new FixProposal
                {
                    Id = proposalId,
                    Kind = MapDiagnosticToFixProposalKind(diag.Code),
                    Title = $"Fix: {diag.Code}",
                    Explanation = $"Auto-generated fix for draft diagnostic '{diag.Code}': {diag.Message}",
                    ReasonCode = diag.Code,
                    DraftId = draftId,
                    TenantId = context.TenantId,
                    Applicability = applicability,
                    IsExecutable = isExecutable,
                    RequiresManualAction = !hasActions,
                    RequiresHumanReview = diag.Severity is DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error
                        or DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker,
                    BlocksActivationUntilResolved = false,
                    RiskLevel = MapDiagnosticToRiskLevel(diag.Severity),
                    ContractVersion = AgentControlPlaneContractVersion.Current,
                    Actions = actions,
                    Diagnostics = [MapFromDraftDiagnostic(diag)],
                    CreatedAt = DateTimeOffset.UtcNow,
                    Rationale = hasActions ? $"Fix for diagnostic: {diag.Message}" : $"Manual intervention required for diagnostic: {diag.Message}"
                };
                _fixProposals[(context.TenantId, proposalId)] = new FixProposalResourceSnapshot(proposal, snapshot.Draft);
                proposals.Add(proposal);
            }

            var result = new FixProposalListResult { Proposals = proposals.AsReadOnly() };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []) with
            {
                TouchedDraftIds = [draftId],
                TouchedFixProposalIds = proposals.Select(p => p.Id).ToList().AsReadOnly()
            };
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<FixProposalListResult>.Success(result, audit);
        }, ct);
    }

    public async Task<AgentToolResult<FixProposal>> GetFixProposalAsync(
        AgentToolInvocationContext context, string proposalId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.GetFixProposal, AgentToolPermissionNames.FixSuggest, async (scope, ct) =>
        {
            if (!_fixProposals.TryGetValue((context.TenantId, proposalId), out var snapshot))
            {
                return await RecordAndReturn(context,
                    AgentToolResult<FixProposal>.NotFound($"Fix proposal '{proposalId}' not found."));
            }

            var denyResult = DenyIfInvisible<FixProposal>(context, scope, snapshot.Owner.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []) with
            {
                TouchedFixProposalIds = [proposalId]
            };
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<FixProposal>.Success(snapshot.Proposal, audit);
        }, ct);
    }

    public async Task<AgentToolResult<FixProposalListResult>> ListFixProposalsAsync(
        AgentToolInvocationContext context, string? draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolName.ListFixProposals, AgentToolPermissionNames.FixSuggest, async (scope, ct) =>
        {
            // When an explicit draftId is provided, it is an explicit target.
            // Spec §6.2: "If the target resolves to a denied kind, return Denied."
            if (draftId is not null)
            {
                var ownerResolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);
                if (ownerResolution.Status == ResourceResolutionStatus.NotFound)
                {
                    return await RecordAndReturn(context,
                        AgentToolResult<FixProposalListResult>.NotFound($"Draft '{draftId}' not found."));
                }
                var denyResult = DenyIfInvisible<FixProposalListResult>(
                    context, scope, ownerResolution.Snapshot!.Draft.DescriptorKind);
                if (denyResult is not null)
                    return denyResult;
            }

            var proposals = _fixProposals
                .Where(kvp => kvp.Key.TenantId == context.TenantId)
                .ToList();

            if (draftId is not null)
                proposals = proposals.Where(p => p.Value.Proposal.DraftId == draftId).ToList();

            // Batch-load owner drafts
            var owners = await _resourceResolver.ResolveOwnersAsync(
                context.TenantId, proposals.Select(p => p.Value.Proposal.DraftId), ct);

            if (proposals.Any(p => !owners.ContainsKey(p.Value.Proposal.DraftId)))
                return await RecordAggregateFailure<FixProposalListResult>(
                    context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);

            var visible = proposals
                .Where(p => scope.IsVisible(owners[p.Value.Proposal.DraftId].DescriptorKind))
                .OrderBy(p => p.Value.Proposal.DraftId, StringComparer.Ordinal)
                .Select(p => p.Value.Proposal)
                .ToList().AsReadOnly();

            var result = new FixProposalListResult { Proposals = visible };

            var diags = scope.IsRestricted ? SecurityTrimmedDiagnostics : [];
            var audit = BuildAudit(context, AgentToolResultStatus.Success, diags);
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<FixProposalListResult>.Success(result, diags, audit);
        }, ct);
    }

    public async Task<AgentToolResult<AgentDescriptorDraftDto>> ApplyFixProposalToDraftAsync(
        AgentToolInvocationContext context, ApplyFixProposalRequest request, CancellationToken ct = default)
    {
        // Complete — Indirect: owner-kind visibility + snapshot reuse
        return await ExecuteAsync(context, AgentToolName.ApplyFixProposalToDraft, AgentToolPermissionNames.FixApplyToDraft, async (scope, ct) =>
        {
            if (!_fixProposals.TryGetValue((context.TenantId, request.ProposalId), out var proposalSnapshot))
            {
                return await RecordAndReturn(context,
                    AgentToolResult<AgentDescriptorDraftDto>.NotFound($"Fix proposal '{request.ProposalId}' not found."));
            }

            var denyResult = DenyIfInvisible<AgentDescriptorDraftDto>(context, scope, proposalSnapshot.Owner.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            var draftResolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, request.DraftId, ct);
            if (draftResolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<AgentDescriptorDraftDto>.NotFound($"Draft '{request.DraftId}' not found."));
            }

            var draft = draftResolution.Snapshot!.Draft;
            var proposal = proposalSnapshot.Proposal;

            if (proposal.DraftId != request.DraftId)
            {
                var mismatchDiag = new AgentToolDiagnostic
                {
                    Code = AgentToolDiagnosticCodes.ProposalDraftMismatchValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Fix proposal '{request.ProposalId}' is for draft '{proposal.DraftId}', not '{request.DraftId}'."
                };
                var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [mismatchDiag]);
                await _auditor.RecordAsync(audit, ct);
                return AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest([mismatchDiag], audit);
            }

            // Single-action constraint: multi-action proposals are not supported
            if (proposal.Actions.Count > 1)
            {
                var multiActionDiag = new AgentToolDiagnostic
                {
                    Code = AgentToolDiagnosticCodes.UnsupportedMultiActionFixProposalValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Fix proposal '{request.ProposalId}' contains {proposal.Actions.Count} actions. Only single-action fix proposals are supported."
                };
                var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [multiActionDiag]);
                await _auditor.RecordAsync(audit, ct);
                return AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest([multiActionDiag], audit);
            }

            // Zero-action proposals cannot be applied
            if (proposal.Actions.Count == 0)
            {
                var noActionDiag = new AgentToolDiagnostic
                {
                    Code = AgentToolDiagnosticCodes.FixProposalHasNoActionsValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Fix proposal '{request.ProposalId}' has no actions to apply."
                };
                var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [noActionDiag]);
                await _auditor.RecordAsync(audit, ct);
                return AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest([noActionDiag], audit);
            }

            // Check applicability — only CurrentMutableDraft proposals can be applied
            if (proposal.Applicability != FixProposalApplicability.CurrentMutableDraft)
            {
                var applicabilityDiag = new AgentToolDiagnostic
                {
                    Code = AgentToolDiagnosticCodes.FixProposalNotApplicableValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Fix proposal applicability is '{proposal.Applicability}', but only '{FixProposalApplicability.CurrentMutableDraft}' proposals can be applied."
                };
                var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [applicabilityDiag]);
                await _auditor.RecordAsync(audit, ct);
                return AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest([applicabilityDiag], audit);
            }

            var updatedDraft = draft;

            var appliedPaths = new List<string>();

            if (proposal.Actions.Count == 1)
            {
                var action = proposal.Actions[0];

                if (!action.IsExecutable)
                {
                    var nonExecDiag = new AgentToolDiagnostic
                    {
                        Code = AgentToolDiagnosticCodes.NonExecutableFixActionValue,
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Fix action for target '{action.TargetPath}' is not executable."
                    };
                    var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [nonExecDiag]);
                    await _auditor.RecordAsync(audit, ct);
                    return AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest([nonExecDiag], audit);
                }

                if (action.Kind != FixProposalActionKind.SetValue)
                {
                    var unsupportedKindDiag = new AgentToolDiagnostic
                    {
                        Code = AgentToolDiagnosticCodes.UnsupportedFixActionKindValue,
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Fix action kind '{action.Kind}' is not supported for draft field mutation. Only SetValue is currently supported."
                    };
                    var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [unsupportedKindDiag]);
                    await _auditor.RecordAsync(audit, ct);
                    return AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest([unsupportedKindDiag], audit);
                }
                if (action.SafetyLevel == FixProposalActionSafetyLevel.Unsafe)
                {
                    var unsafeDiag = new AgentToolDiagnostic
                    {
                        Code = AgentToolDiagnosticCodes.UnsafeFixActionRejectedValue,
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Fix action for target '{action.TargetPath}' has Unsafe safety level and is rejected."
                    };
                    var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [unsafeDiag]);
                    await _auditor.RecordAsync(audit, ct);
                    return AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest([unsafeDiag], audit);
                }

                var allowedPaths = new HashSet<string>(StringComparer.Ordinal)
                    { "Intent", "Rationale", "ProposedVersion", "CorrelationId" };

                // Boundary violation: TargetDescriptorId references a descriptor other than the draft itself.
                // ApplyFixProposalToDraftAsync can only mutate the draft's own fields.
                if (action.TargetDescriptorId is not null
                    && !string.Equals(action.TargetDescriptorId, draft.DescriptorId, StringComparison.Ordinal))
                {
                    var boundaryDiag = new AgentToolDiagnostic
                    {
                        Code = AgentToolDiagnosticCodes.FixActionTargetBoundaryViolationValue,
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Fix action targets descriptor '{action.TargetDescriptorId}', but draft fix proposals can only mutate the draft's own fields (descriptor '{draft.DescriptorId}')."
                    };
                    var boundaryAudit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [boundaryDiag]);
                    await _auditor.RecordAsync(boundaryAudit, ct);
                    return AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest([boundaryDiag], boundaryAudit);
                }

                // Boundary violation: TargetPath references active/runtime registry namespace
                if (action.TargetPath.StartsWith("registry.", StringComparison.Ordinal)
                    || action.TargetPath.StartsWith("active.", StringComparison.Ordinal)
                    || action.TargetPath.StartsWith("runtime.", StringComparison.Ordinal))
                {
                    var boundaryDiag = new AgentToolDiagnostic
                    {
                        Code = AgentToolDiagnosticCodes.FixActionTargetBoundaryViolationValue,
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Fix action targets boundary-violating path '{action.TargetPath}'. Draft fix proposals cannot mutate the active descriptor registry."
                    };
                    var boundaryAudit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [boundaryDiag]);
                    await _auditor.RecordAsync(boundaryAudit, ct);
                    return AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest([boundaryDiag], boundaryAudit);
                }

                if (!allowedPaths.Contains(action.TargetPath))
                {
                    var targetDiag = new AgentToolDiagnostic
                    {
                        Code = AgentToolDiagnosticCodes.FixActionTargetNotAllowedValue,
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Fix action target '{action.TargetPath}' is not an allowed draft field."
                    };
                    var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [targetDiag]);
                    await _auditor.RecordAsync(audit, ct);
                    return AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest([targetDiag], audit);
                }

                if (action.Kind == FixProposalActionKind.SetValue)
                {
                    var value = action.ProposedValue?.GetString();
                    updatedDraft = action.TargetPath switch
                    {
                        "Intent" => updatedDraft with { Intent = value },
                        "Rationale" => updatedDraft with { Rationale = value },
                        "ProposedVersion" => updatedDraft with { ProposedVersion = value },
                        "CorrelationId" => updatedDraft with { CorrelationId = value },
                        _ => updatedDraft
                    };
                    appliedPaths.Add(action.TargetPath);
                }
            }

            // Apply proposal rationale only if Rationale was not already set by an action
            if (proposal.Rationale is not null && !appliedPaths.Contains("Rationale"))
                updatedDraft = updatedDraft with { Rationale = proposal.Rationale };

            await _draftStore.SaveAsync(updatedDraft, ct);

            var successDiags = new List<AgentToolDiagnostic>();
            if (appliedPaths.Count > 0)
            {
                successDiags.Add(new AgentToolDiagnostic
                {
                    Code = AgentToolDiagnosticCodes.FixActionsAppliedValue,
                    Severity = AgentToolDiagnosticSeverity.Info,
                    Message = $"Applied {appliedPaths.Count} action(s) to draft fields: {string.Join(", ", appliedPaths)}."
                });
            }

            var successAudit = BuildAudit(context, AgentToolResultStatus.Success, successDiags);
            successAudit = successAudit with
            {
                TouchedDraftIds = [request.DraftId],
                TouchedFixProposalIds = [request.ProposalId]
            };
            await _auditor.RecordAsync(successAudit, ct);
            var dto = BuildDraftDto(updatedDraft);
            if (dto is null)
            {
                return AgentToolResult<AgentDescriptorDraftDto>.Failed(
                    [new AgentToolDiagnostic { Code = AgentToolDiagnosticCodes.DraftProjectionFailedValue, Severity = AgentToolDiagnosticSeverity.Error, Message = "Failed to project draft to DTO." }],
                    BuildAudit(context, AgentToolResultStatus.Failed, [new AgentToolDiagnostic { Code = AgentToolDiagnosticCodes.DraftProjectionFailedValue, Severity = AgentToolDiagnosticSeverity.Error, Message = "Failed to project draft to DTO." }]));
            }
            return AgentToolResult<AgentDescriptorDraftDto>.Success(dto, successDiags, successAudit);
        }, ct);
    }

    // ── Wave 5 — Package Preview ──

    public async Task<AgentToolResult<DraftPackagePreview>> PreviewDescriptorPackageAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        // Complete — Nested: snapshot reuse via _resourceResolver
        return await ExecuteAsync(context, AgentToolName.PreviewDescriptorPackage, AgentToolPermissionNames.PackagePreview, async (scope, ct) =>
        {
            var resolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);
            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DraftPackagePreview>.NotFound($"Draft '{draftId}' not found."));
            }

            var snapshot = resolution.Snapshot!;

            var denyResult = DenyIfInvisible<DraftPackagePreview>(context, scope, snapshot.Draft.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            var draft = snapshot.Draft;

            // Build visible universe BEFORE constructing the package.
            // The package builder must receive only visible descriptors so that
            // hashes, evidence, and manifest entries reflect the visible universe
            // and cannot serve as a side-channel for denied descriptor existence.
            var currentInventory = _descriptorCatalog.GetAll().ToList();
            var universeResult = AgentVisibleDescriptorUniverse.TryCreate(currentInventory, scope);
            if (!universeResult.IsSuccess)
                return await RecordAggregateFailure<DraftPackagePreview>(context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);
            var universe = universeResult.Universe!;

            var materializationResult = _draftMaterializer.Materialize(draft, universe.VisibleDescriptors);
            if (!materializationResult.IsMaterialized)
            {
                var failDiags = materializationResult.Diagnostics.Select(MapFromDraftDiagnostic).ToList();
                var audit = BuildAudit(context, AgentToolResultStatus.Failed, failDiags);
                await _auditor.RecordAsync(audit, ct);
                return AgentToolResult<DraftPackagePreview>.Failed(failDiags, audit);
            }

            // Filter the proposed inventory through visibility before building the package.
            // This ensures ContentHash/EvidenceHash/EnvelopeHash are derived from visible
            // descriptors only — no hash side-channel for denied kinds.
            var visibleProposed = scope.Filter(
                materializationResult.ProposedInventory, d => d.Kind);

            var pkgRequest = new DescriptorPackageBuildRequest
            {
                PackageId = draftId,
                PackageVersion = draft.ProposedVersion ?? "1",
                Name = draft.Intent,
                CreatedBy = draft.AuthorId,
                Source = draft.Source,
                CreatedAt = draft.CreatedAt,
                Descriptors = visibleProposed
            };

            var pkg = _packageBuilder.Build(pkgRequest);
            var preview = new DraftPackagePreview
            {
                ManifestHash = pkg.Manifest.ContentHash,
                SnapshotHash = null,
                EvidenceHash = pkg.Manifest.EvidenceHash ?? "",
                EnvelopeHash = pkg.Manifest.EnvelopeHash ?? "",
                DescriptorIds = pkg.Manifest.DescriptorEntries.Select(e => e.Ref.Id).ToList().AsReadOnly()
            };

            // Project nested package data through visibility (filters DescriptorIds).
            // Returns null on projection failure (denied descriptor in package).
            var projectedPreview = _artifactProjector.ProjectPackage(preview, universe);
            if (projectedPreview is null)
            {
                return await RecordAggregateFailure<DraftPackagePreview>(
                    context, "PACKAGE_PROJECTION_FAILURE", ct);
            }
            preview = projectedPreview;

            var previewId = Guid.NewGuid().ToString("N");
            _packagePreviews[(context.TenantId, previewId)] = new PackagePreviewResourceSnapshot(
                new PackagePreviewEntry(draftId, context.TenantId, preview), snapshot.Draft);

            // Store package hash for evidence recheck
            var evidenceHash = CreatePackageCanonicalHash(preview.EvidenceHash, CanonicalHashPurposeNames.AuditEvidence);
            _artifactResolver.StorePackageHash(context.TenantId, previewId, evidenceHash);

            var audit2 = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit2 = audit2 with
            {
                TouchedDraftIds = [draftId],
                TouchedPackagePreviewIds = [previewId]
            };
            await _auditor.RecordAsync(audit2, ct);
            return AgentToolResult<DraftPackagePreview>.Success(preview, audit2);
        }, ct);
    }

    public async Task<AgentToolResult<PackageEvidencePreview>> BuildPackageEvidencePreviewAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        // Complete — Nested: snapshot reuse via _resourceResolver
        return await ExecuteAsync(context, AgentToolName.BuildPackageEvidencePreview, AgentToolPermissionNames.PackagePreview, async (scope, ct) =>
        {
            var resolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);
            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<PackageEvidencePreview>.NotFound($"Draft '{draftId}' not found."));
            }

            var snapshot = resolution.Snapshot!;

            var denyResult = DenyIfInvisible<PackageEvidencePreview>(context, scope, snapshot.Draft.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            var draft = snapshot.Draft;

            // Build visible universe BEFORE constructing the package.
            var currentInventory = _descriptorCatalog.GetAll().ToList();
            var universeResult = AgentVisibleDescriptorUniverse.TryCreate(currentInventory, scope);
            if (!universeResult.IsSuccess)
                return await RecordAggregateFailure<PackageEvidencePreview>(context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);
            var universe = universeResult.Universe!;

            var reviewResult = await _draftReviewService.ReviewAsync(draft, universe.VisibleDescriptors, ct);

            // If review validation failed, evidence cannot be meaningfully computed.
            // Return Failed instead of producing a misleading zero-value evidence preview.
            if (!reviewResult.ValidationResult.IsValid)
            {
                var validationDiag = new AgentToolDiagnostic
                {
                    Code = AgentToolDiagnosticCodes.ReviewValidationFailedValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = "Review validation failed; evidence preview cannot be computed."
                };
                var failedAudit = BuildAudit(context, AgentToolResultStatus.Failed, [validationDiag]) with
                {
                    TouchedDraftIds = [draftId]
                };
                await _auditor.RecordAsync(failedAudit, ct);
                return AgentToolResult<PackageEvidencePreview>.Failed([validationDiag], failedAudit);
            }

            // Project review through visibility before building package
            var projectedReview = _artifactProjector.ProjectReview(reviewResult, scope, universe);
            if (projectedReview is null)
            {
                return await RecordAggregateFailure<PackageEvidencePreview>(
                    context, "PACKAGE_PROJECTION_FAILURE", ct);
            }

            // If materialization failed, the proposed inventory is not available.
            // Return Failed instead of falling back to currentInventory, which would
            // produce a misleading evidence preview that doesn't reflect the draft.
            if (projectedReview.MaterializationResult is not null
                && !projectedReview.MaterializationResult.IsMaterialized)
            {
                var matDiags = projectedReview.MaterializationResult.Diagnostics
                    .Select(MapFromDraftDiagnostic).ToList();
                var matAudit = BuildAudit(context, AgentToolResultStatus.Failed, matDiags) with
                {
                    TouchedDraftIds = [draftId]
                };
                await _auditor.RecordAsync(matAudit, ct);
                return AgentToolResult<PackageEvidencePreview>.Failed(matDiags, matAudit);
            }

            // Build the package from visible descriptors only — hashes and evidence
            // must not serve as a side-channel for denied descriptor existence.
            var visibleInventory = projectedReview.ProposedInventory ?? currentInventory;
            var filteredInventory = scope.Filter(visibleInventory, d => d.Kind);

            var pkgRequest = new DescriptorPackageBuildRequest
            {
                PackageId = draftId,
                PackageVersion = draft.ProposedVersion ?? "1",
                Name = draft.Intent,
                CreatedBy = draft.AuthorId,
                Source = draft.Source,
                CreatedAt = draft.CreatedAt,
                Descriptors = filteredInventory,
                TopologySnapshot = projectedReview.TopologySnapshot,
                ImpactReport = projectedReview.ImpactAnalysisResult,
                CompatibilityReport = projectedReview.CompatibilityResult,
                GovernanceReport = projectedReview.GovernanceDecision
            };

            var pkg = _packageBuilder.Build(pkgRequest);
            var preview = new DraftPackagePreview
            {
                ManifestHash = pkg.Manifest.ContentHash,
                SnapshotHash = null,
                EvidenceHash = pkg.Manifest.EvidenceHash ?? "",
                EnvelopeHash = pkg.Manifest.EnvelopeHash ?? "",
                DescriptorIds = pkg.Manifest.DescriptorEntries.Select(e => e.Ref.Id).ToList().AsReadOnly()
            };

            // Project nested package data through visibility.
            // Returns null on projection failure (denied descriptor in package).
            var projectedPreview = _artifactProjector.ProjectPackage(preview, universe);
            if (projectedPreview is null)
            {
                return await RecordAggregateFailure<PackageEvidencePreview>(
                    context, "PACKAGE_PROJECTION_FAILURE", ct);
            }
            preview = projectedPreview;

            var result = new PackageEvidencePreview
            {
                DraftId = draftId,
                TenantId = context.TenantId,
                PackagePreview = preview,
                Evidence = pkg.Evidence,
                Diagnostics = projectedReview.Diagnostics.Select(MapFromDraftDiagnostic).ToList().AsReadOnly()
            };

            // Project evidence through visibility (filters Subject/RelatedRefs)
            result = _artifactProjector.ProjectEvidence(result, universe);

            // Store evidence preview with owner for later retrieval and activation reference validation
            var evidencePreviewId = Guid.NewGuid().ToString("N");
            _evidencePreviews[(context.TenantId, evidencePreviewId)] = new EvidencePreviewResourceSnapshot(
                new EvidencePreviewEntry(draft.DraftId, context.TenantId, result), snapshot.Draft);

            // Store evidence hash for evidence recheck
            var envelopeHash = CreatePackageCanonicalHash(preview.EnvelopeHash, CanonicalHashPurposeNames.AuditEvidence);
            _artifactResolver.StoreEvidenceHash(context.TenantId, evidencePreviewId, envelopeHash);

            var audit = BuildAudit(context, AgentToolResultStatus.Success, result.Diagnostics);
            audit = audit with
            {
                TouchedDraftIds = [draftId],
                TouchedPackagePreviewIds = [evidencePreviewId]
            };
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<PackageEvidencePreview>.Success(result, audit);
        }, ct);
    }

    public async Task<AgentToolResult<ActivationReadinessPreview>> BuildActivationReadinessPreviewAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        // Complete — Nested: snapshot reuse via _resourceResolver
        return await ExecuteAsync(context, AgentToolName.BuildActivationReadinessPreview, AgentToolPermissionNames.PackagePreview, async (scope, ct) =>
        {
            var resolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);
            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<ActivationReadinessPreview>.NotFound($"Draft '{draftId}' not found."));
            }

            var snapshot = resolution.Snapshot!;

            var denyResult = DenyIfInvisible<ActivationReadinessPreview>(context, scope, snapshot.Draft.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            var draft = snapshot.Draft;

            // Build visible universe — readiness assessment must not be
            // influenced by denied descriptors in the catalog.
            var currentInventory = _descriptorCatalog.GetAll().ToList();
            var universeResult = AgentVisibleDescriptorUniverse.TryCreate(currentInventory, scope);
            if (!universeResult.IsSuccess)
                return await RecordAggregateFailure<ActivationReadinessPreview>(context, "AUTHORIZATION_CONTEXT_UNAVAILABLE", ct);
            var universe = universeResult.Universe!;

            // Review against visible descriptors only — denied descriptors
            // must not affect readiness or leak into diagnostics.
            var reviewResult = await _draftReviewService.ReviewAsync(draft, universe.VisibleDescriptors, ct);

            var blockers = new List<ActivationReadinessBlocker>();

            if (!reviewResult.ValidationResult.IsValid)
            {
                blockers.Add(new ActivationReadinessBlocker
                {
                    Code = AgentToolDiagnosticCodes.ValidationFailedValue,
                    Message = "Draft validation failed.",
                    Severity = ActivationReadinessBlockerSeverity.Blocker,
                    Remedy = "Fix validation errors before requesting activation."
                });
            }

            // Evaluate review diagnostics through visibility filter — denied-kind
            // diagnostics must not block activation or appear in output.
            var visibleDiagnostics = reviewResult.Diagnostics
                .Where(d => d.DescriptorKind is null || scope.IsVisible(d.DescriptorKind.Value))
                .ToList().AsReadOnly();

            if (visibleDiagnostics.Any(d =>
                    d.Severity is DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error
                        or DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker))
            {
                blockers.Add(new ActivationReadinessBlocker
                {
                    Code = AgentToolDiagnosticCodes.ReviewHasErrorsValue,
                    Message = "Review produced error or blocker diagnostics.",
                    Severity = ActivationReadinessBlockerSeverity.Blocker,
                    Remedy = "Resolve error/blocker diagnostics before requesting activation."
                });
            }

            if (!reviewResult.IsActivationEligible)
            {
                blockers.Add(new ActivationReadinessBlocker
                {
                    Code = AgentToolDiagnosticCodes.NotActivationEligibleValue,
                    Message = "Governance decision does not allow activation.",
                    Severity = ActivationReadinessBlockerSeverity.Error,
                    Remedy = "Review governance findings and adjust draft."
                });
            }

            var result = new ActivationReadinessPreview
            {
                DraftId = draftId,
                TenantId = context.TenantId,
                IsReady = blockers.Count == 0,
                Blockers = blockers.AsReadOnly(),
                Diagnostics = visibleDiagnostics.Select(MapFromDraftDiagnostic).ToList().AsReadOnly()
            };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, result.Diagnostics);
            audit = audit with { TouchedDraftIds = [draftId] };
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<ActivationReadinessPreview>.Success(result, audit);
        }, ct);
    }

    public async Task<AgentToolResult<DraftPackagePreview>> GetPackagePreviewAsync(
        AgentToolInvocationContext context, string previewId, CancellationToken ct = default)
    {
        // Complete — Indirect: owner-kind visibility resolution
        return await ExecuteAsync(context, AgentToolName.GetPackagePreview, AgentToolPermissionNames.PackagePreview, async (scope, ct) =>
        {
            if (!_packagePreviews.TryGetValue((context.TenantId, previewId), out var snapshot))
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DraftPackagePreview>.NotFound($"Package preview '{previewId}' not found."));
            }

            var denyResult = DenyIfInvisible<DraftPackagePreview>(context, scope, snapshot.Owner.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedPackagePreviewIds = [previewId] };
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<DraftPackagePreview>.Success(snapshot.Preview.Preview, audit);
        }, ct);
    }

    // ── Wave 6 — Activation Handoff ──

    public async Task<AgentToolResult<ActivationRequest>> SubmitActivationRequestAsync(
        AgentToolInvocationContext context, SubmitActivationRequestRequest request, CancellationToken ct = default)
    {
        // Complete — Indirect: snapshot reuse via _resourceResolver
        return await ExecuteAsync(context, AgentToolName.SubmitActivationRequest, AgentToolPermissionNames.ActivationRequestSubmit, async (scope, ct) =>
        {
            var resolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, request.DraftId, ct);
            if (resolution.Status == ResourceResolutionStatus.NotFound)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<ActivationRequest>.NotFound($"Draft '{request.DraftId}' not found."));
            }

            var draftSnapshot = resolution.Snapshot!;

            var denyResult = DenyIfInvisible<ActivationRequest>(context, scope, draftSnapshot.Draft.DescriptorKind);
            if (denyResult is not null)
                return denyResult;

            // Phase 7e: BindingSnapshot replaces individual reference fields.
            // Fail-closed: BindingSnapshot must be present (JSON/input-bound calls may bypass C# required constraints).
            if (request.BindingSnapshot is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<ActivationRequest>.InvalidRequest([
                        new AgentToolDiagnostic
                        {
                            Code = DescriptorActivationDiagnosticCodes.BindingSnapshotRequiredValue,
                            Severity = AgentToolDiagnosticSeverity.Error,
                            Message = "BindingSnapshot is required for activation request submission."
                        }
                    ]));
            }

            // Fail-closed: BindingSnapshot.Hashes must be present (JSON/input-bound calls may bypass C# required constraints).
            if (request.BindingSnapshot.Hashes is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<ActivationRequest>.InvalidRequest([
                        new AgentToolDiagnostic
                        {
                            Code = DescriptorActivationDiagnosticCodes.BindingHashesRequiredValue,
                            Severity = AgentToolDiagnosticSeverity.Error,
                            Message = "BindingSnapshot.Hashes is required for activation request submission."
                        }
                    ]));
            }

            // Validate that the binding snapshot references exist and match the draft.
            var refDiagnostics = new List<AgentToolDiagnostic>();

            if (!_reviewResults.TryGetValue((context.TenantId, request.BindingSnapshot.ReviewResultId), out var reviewRef))
            {
                refDiagnostics.Add(new AgentToolDiagnostic
                {
                    Code = DescriptorActivationDiagnosticCodes.ReviewResultNotFoundValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Referenced review result '{request.BindingSnapshot.ReviewResultId}' not found for this tenant."
                });
            }
            else if (reviewRef.Review.DraftId != request.DraftId)
            {
                refDiagnostics.Add(new AgentToolDiagnostic
                {
                    Code = DescriptorActivationDiagnosticCodes.ReviewResultDraftMismatchValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Referenced review result '{request.BindingSnapshot.ReviewResultId}' belongs to draft '{reviewRef.Review.DraftId}', not '{request.DraftId}'."
                });
            }

            // Fail-closed: binding references must be non-empty
            if (string.IsNullOrWhiteSpace(request.BindingSnapshot.PackagePreviewId))
            {
                refDiagnostics.Add(new AgentToolDiagnostic
                {
                    Code = DescriptorActivationDiagnosticCodes.PackagePreviewNotFoundValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = "PackagePreviewId is required for activation request submission."
                });
            }
            else if (!_packagePreviews.TryGetValue((context.TenantId, request.BindingSnapshot.PackagePreviewId), out var packageRef))
            {
                refDiagnostics.Add(new AgentToolDiagnostic
                {
                    Code = DescriptorActivationDiagnosticCodes.PackagePreviewNotFoundValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Referenced package preview '{request.BindingSnapshot.PackagePreviewId}' not found for this tenant."
                });
            }
            else if (packageRef.Preview.DraftId != request.DraftId)
            {
                refDiagnostics.Add(new AgentToolDiagnostic
                {
                    Code = DescriptorActivationDiagnosticCodes.PackagePreviewDraftMismatchValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Referenced package preview '{request.BindingSnapshot.PackagePreviewId}' belongs to draft '{packageRef.Preview.DraftId}', not '{request.DraftId}'."
                });
            }

            // Fail-closed: binding references must be non-empty
            if (string.IsNullOrWhiteSpace(request.BindingSnapshot.EvidencePreviewId))
            {
                refDiagnostics.Add(new AgentToolDiagnostic
                {
                    Code = DescriptorActivationDiagnosticCodes.EvidencePreviewNotFoundValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = "EvidencePreviewId is required for activation request submission."
                });
            }
            else if (!_evidencePreviews.TryGetValue((context.TenantId, request.BindingSnapshot.EvidencePreviewId), out var evidenceRef))
            {
                refDiagnostics.Add(new AgentToolDiagnostic
                {
                    Code = DescriptorActivationDiagnosticCodes.EvidencePreviewNotFoundValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Referenced evidence preview '{request.BindingSnapshot.EvidencePreviewId}' not found for this tenant."
                });
            }
            else if (evidenceRef.Evidence.DraftId != request.DraftId)
            {
                refDiagnostics.Add(new AgentToolDiagnostic
                {
                    Code = DescriptorActivationDiagnosticCodes.EvidencePreviewDraftMismatchValue,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Referenced evidence preview '{request.BindingSnapshot.EvidencePreviewId}' belongs to draft '{evidenceRef.Evidence.DraftId}', not '{request.DraftId}'."
                });
            }

            if (refDiagnostics.Count > 0)
            {
                var refAudit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, refDiagnostics);
                await _auditor.RecordAsync(refAudit, ct);
                return AgentToolResult<ActivationRequest>.InvalidRequest(refDiagnostics, refAudit);
            }

            // Extract governance decision from review result — the review pipeline already
            // evaluated governance via IDescriptorLifecycleGovernanceService.
            var governanceDecision = reviewRef?.Review?.GovernanceDecision?.MaxDecision;

            // Delegate to RequestService — single authority for activation request lifecycle.
            // Pass pre-evaluated governance decision so auto-activation is reachable.
            var governedRequest = request with { GovernanceDecision = governanceDecision };
            var result = await _activationRequestService.CreateActivationRequestAsync(context, governedRequest, ct);

            // If human review required, create review task
            if (result.Status == AgentToolResultStatus.Success
                && result.Value?.Status == ActivationRequestStatus.UnderReview)
            {
                var policy = result.Value.Policy ?? new DescriptorActivationPolicy
                {
                    RequireHumanReviewForAll = false,
                    ForbidSelfApproval = true,
                    AutoActivateAllowedWhenPolicyPermits = true
                };

                var reviewTaskResult = await _activationReviewOrchestrator.CreateActivationReviewTaskAsync(
                    context, result.Value, policy, ct);

                if (reviewTaskResult.Status != AgentToolResultStatus.Success)
                {
                    _logger.LogWarning(
                        "Failed to create activation review task for request {RequestId}: {Error}",
                        result.Value.RequestId,
                        reviewTaskResult.Diagnostics.FirstOrDefault()?.Message ?? "unknown error");
                }
            }

            // Record audit for the tool surface
            var audit = BuildAudit(context, result.Status, result.Diagnostics);
            audit = audit with
            {
                TouchedDraftIds = result.Value is not null ? [request.DraftId] : null,
                TouchedActivationRequestIds = result.Value is not null ? [result.Value.RequestId] : null
            };
            await _auditor.RecordAsync(audit, ct);

            return result;
        }, ct);
    }

    public async Task<AgentToolResult<ActivationRequest>> GetActivationRequestStatusAsync(
        AgentToolInvocationContext context, string requestId, CancellationToken ct = default)
    {
        // Phase 7e: delegate to RequestService — single authority for activation lifecycle
        return await ExecuteAsync(context, AgentToolName.GetActivationRequestStatus, AgentToolPermissionNames.ActivationRequestRead, async (scope, ct) =>
        {
            var result = await _activationRequestService.GetActivationRequestStatusAsync(context, requestId, ct);

            // Audit the tool-level invocation
            var audit = BuildAudit(context, result.Status, result.Diagnostics);
            audit = audit with { TouchedActivationRequestIds = [requestId] };
            await _auditor.RecordAsync(audit, ct);

            return result;
        }, ct);
    }

    public async Task<AgentToolResult<ActivationRequest>> CancelActivationRequestAsync(
        AgentToolInvocationContext context, string requestId, CancellationToken ct = default)
    {
        // Phase 7e: delegate to RequestService — single authority for activation lifecycle
        return await ExecuteAsync(context, AgentToolName.CancelActivationRequest, AgentToolPermissionNames.ActivationRequestCancel, async (scope, ct) =>
        {
            var result = await _activationRequestService.CancelActivationRequestAsync(context, requestId, "Cancelled via agent tool", ct);

            // Audit the tool-level invocation
            var audit = BuildAudit(context, result.Status, result.Diagnostics);
            audit = audit with { TouchedActivationRequestIds = [requestId] };
            await _auditor.RecordAsync(audit, ct);

            return result;
        }, ct);
    }

    // ── Mappers ──

    /// <summary>
    /// Applies a single fix proposal action to a draft's scalar fields.
    /// Returns true if the action was applied, false if it requires payload-level
    /// mutation that is not yet supported by the typed payload infrastructure.
    /// </summary>
    private static bool ApplyActionToDraft(ref Draft draft, FixProposalAction action)
    {
        if (action.Kind != FixProposalActionKind.SetValue)
            return false; // Remove/Add not yet supported for draft fields

        return action.TargetPath switch
        {
            "Intent" => ApplySetField(ref draft, d => d with { Intent = action.ProposedValue?.GetString() }),
            "Rationale" => ApplySetField(ref draft, d => d with { Rationale = action.ProposedValue?.GetString() }),
            "ProposedVersion" => ApplySetField(ref draft, d => d with { ProposedVersion = action.ProposedValue?.GetString() }),
            "CorrelationId" => ApplySetField(ref draft, d => d with { CorrelationId = action.ProposedValue?.GetString() }),
            // DraftId, DescriptorId, AuthorId, TenantId are identity fields — not mutable via fix proposals
            // Payload paths require typed payload support — deferred
            _ => false
        };
    }

    private static bool ApplySetField(ref Draft draft, Func<Draft, Draft> updater)
    {
        draft = updater(draft);
        return true;
    }

    private static DraftAbstractions.DescriptorDraftAuthorKind MapActorKind(AgentToolActorKind kind) => kind switch
    {
        AgentToolActorKind.Human => DraftAbstractions.DescriptorDraftAuthorKind.Human,
        AgentToolActorKind.Agent => DraftAbstractions.DescriptorDraftAuthorKind.Agent,
        AgentToolActorKind.System => DraftAbstractions.DescriptorDraftAuthorKind.System,
        AgentToolActorKind.Import => DraftAbstractions.DescriptorDraftAuthorKind.Import,
        AgentToolActorKind.Generator => DraftAbstractions.DescriptorDraftAuthorKind.Generator,
        _ => DraftAbstractions.DescriptorDraftAuthorKind.System
    };

    private static AgentToolDiagnostic MapFromDraftDiagnostic(DraftAbstractions.DescriptorDraftDiagnostic d)
        => new()
        {
            Code = d.Code,
            Severity = MapFromDraftSeverity(d.Severity),
            Message = d.Message,
            Path = d.Path,
            RelatedDiagnosticCode = d.RelatedDiagnosticCode
        };

    private static AgentToolDiagnosticSeverity MapFromDraftSeverity(
        DraftAbstractions.DescriptorDraftDiagnosticSeverity severity) => severity switch
        {
            DraftAbstractions.DescriptorDraftDiagnosticSeverity.Info => AgentToolDiagnosticSeverity.Info,
            DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning => AgentToolDiagnosticSeverity.Warning,
            DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error => AgentToolDiagnosticSeverity.Error,
            DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker => AgentToolDiagnosticSeverity.Blocker,
            _ => AgentToolDiagnosticSeverity.Info
        };

    private static AgentToolDiagnostic MapFromContextPackDiagnostic(MetadataContextPackDiagnostic d)
        => new()
        {
            Code = d.Code,
            Severity = MapFromContextPackSeverity(d.Severity),
            Message = d.Message,
            Path = d.Path
        };

    private static AgentToolDiagnosticSeverity MapFromContextPackSeverity(
        MetadataContextPackDiagnosticSeverity severity) => severity switch
    {
        MetadataContextPackDiagnosticSeverity.Info => AgentToolDiagnosticSeverity.Info,
        MetadataContextPackDiagnosticSeverity.Warning => AgentToolDiagnosticSeverity.Warning,
        MetadataContextPackDiagnosticSeverity.Error => AgentToolDiagnosticSeverity.Error,
        _ => AgentToolDiagnosticSeverity.Info
    };

    private static AgentToolDiagnosticSeverity MapTopologyDiagnosticSeverity(DiagnosticSeverity severity)
        => severity switch
        {
            DiagnosticSeverity.Info => AgentToolDiagnosticSeverity.Info,
            DiagnosticSeverity.Warning => AgentToolDiagnosticSeverity.Warning,
            DiagnosticSeverity.Error => AgentToolDiagnosticSeverity.Error,
            _ => AgentToolDiagnosticSeverity.Info
        };

    private static FixProposalRiskLevel MapDiagnosticToRiskLevel(
        DraftAbstractions.DescriptorDraftDiagnosticSeverity severity) => severity switch
    {
        DraftAbstractions.DescriptorDraftDiagnosticSeverity.Info => FixProposalRiskLevel.Safe,
        DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning => FixProposalRiskLevel.Low,
        DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error => FixProposalRiskLevel.High,
        DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker => FixProposalRiskLevel.Unsafe,
        _ => FixProposalRiskLevel.Medium
    };

    private static bool IsIdentityFieldDiagnostic(string code) =>
        code is DescriptorDraftDiagnosticCodes.DraftIdEmptyValue or DescriptorDraftDiagnosticCodes.DescriptorIdEmptyValue or DescriptorDraftDiagnosticCodes.AuthorIdEmptyValue;

    private static FixProposalKind MapDiagnosticToFixProposalKind(string diagnosticCode) => diagnosticCode switch
    {
        DescriptorDraftDiagnosticCodes.RationaleEmptyValue or DescriptorDraftDiagnosticCodes.IntentEmptyValue => FixProposalKind.SetRequiredField,
        _ => FixProposalKind.MarkRequiresReview
    };

    private static IReadOnlyList<FixProposalAction> GenerateFixActions(
        DraftAbstractions.DescriptorDraftDiagnostic diagnostic)
    {
        var actions = new List<FixProposalAction>();

        if (diagnostic.Code == DescriptorDraftDiagnosticCodes.RationaleEmptyValue)
        {
            actions.Add(new FixProposalAction
            {
                Kind = FixProposalActionKind.SetValue,
                TargetPath = "Rationale",
                CurrentValue = JsonSerializer.SerializeToElement(""),
                ProposedValue = JsonSerializer.SerializeToElement("(provide rationale)"),
                IsExecutable = true,
                SafetyLevel = FixProposalActionSafetyLevel.Safe,
                Description = "Provide a rationale for the draft."
            });
        }
        else if (diagnostic.Code == DescriptorDraftDiagnosticCodes.IntentEmptyValue)
        {
            actions.Add(new FixProposalAction
            {
                Kind = FixProposalActionKind.SetValue,
                TargetPath = "Intent",
                CurrentValue = JsonSerializer.SerializeToElement(""),
                ProposedValue = JsonSerializer.SerializeToElement("(provide intent)"),
                IsExecutable = true,
                SafetyLevel = FixProposalActionSafetyLevel.Safe,
                Description = "Provide an intent for the draft."
            });
        }

        return actions.AsReadOnly();
    }

    // ── Evidence recheck hash helpers ──

    private static CanonicalHash CreateReviewCanonicalHash(string digest, string purpose)
        => new()
        {
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-adhoc-v1",
            ArtifactKind = CanonicalHashArtifactNames.ReviewResult,
            Scope = CanonicalHashScopeNames.InternalFull,
            Purpose = purpose,
            ContractVersion = AgentControlPlaneContractVersion.Current,
            CanonicalShapeVersion = "sha256-adhoc-v1",
            Value = digest
        };

    private static CanonicalHash CreatePackageCanonicalHash(string digest, string purpose)
        => new()
        {
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-adhoc-v1",
            ArtifactKind = CanonicalHashArtifactNames.Package,
            Scope = CanonicalHashScopeNames.InternalFull,
            Purpose = purpose,
            ContractVersion = AgentControlPlaneContractVersion.Current,
            CanonicalShapeVersion = "sha256-adhoc-v1",
            Value = digest
        };

    // TODO: Migrate to canonical source-binding writer when #30 provides SourceBinding CanonicalHashProfile.
    // Current ad hoc pipe-delimited hash is interim; hash input format will change when canonical writer is available.
    private static CanonicalHash ComputeSourceReviewHash(DraftAbstractions.DescriptorDraftReviewResult reviewResult)
    {
        var sb = new StringBuilder();
        sb.Append(reviewResult.TenantId);
        sb.Append('|');
        sb.Append(reviewResult.DraftId);
        sb.Append('|');
        sb.Append(reviewResult.IsActivationEligible);
        sb.Append('|');
        sb.Append(reviewResult.ValidationResult.IsValid);

        foreach (var d in reviewResult.ValidationResult.Diagnostics.OrderBy(d => d.Code))
        {
            sb.Append('|');
            sb.Append(d.Code);
            sb.Append(':');
            sb.Append((int)d.Severity);
        }

        foreach (var d in (reviewResult.Diagnostics ?? Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>())
            .OrderBy(d => d.Code))
        {
            sb.Append('|');
            sb.Append(d.Code);
            sb.Append(':');
            sb.Append((int)d.Severity);
        }

        if (reviewResult.GovernanceDecision != null)
        {
            sb.Append('|');
            sb.Append(reviewResult.GovernanceDecision.MaxDecision);
            sb.Append('|');
            sb.Append(reviewResult.GovernanceDecision.Decisions.Count);
        }

        if (reviewResult.MaterializationResult != null)
        {
            sb.Append('|');
            sb.Append(reviewResult.MaterializationResult.IsMaterialized);
            sb.Append('|');
            sb.Append(reviewResult.MaterializationResult.ProposedInventory.Count);
        }

        if (reviewResult.ImpactAnalysisResult != null)
        {
            sb.Append('|');
            sb.Append(reviewResult.ImpactAnalysisResult.AffectedDescriptors.Count);
            sb.Append('|');
            sb.Append(reviewResult.ImpactAnalysisResult.MaxSeverity);
        }

        var digest = ComputeSha256(sb.ToString());
        return CreateReviewCanonicalHash(digest, CanonicalHashPurposeNames.SourceBinding);
    }

    private static CanonicalHash ComputeReviewManifestHash(DraftAbstractions.DescriptorDraftReviewResult reviewResult)
    {
        var sb = new StringBuilder();
        sb.Append(reviewResult.DraftId);
        sb.Append('|');
        sb.Append(reviewResult.ValidationResult.IsValid);
        sb.Append('|');
        sb.Append(reviewResult.IsActivationEligible);
        sb.Append('|');
        sb.Append(reviewResult.Diagnostics.Count);

        var digest = ComputeSha256(sb.ToString());
        return CreateReviewCanonicalHash(digest, CanonicalHashPurposeNames.Integrity);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
