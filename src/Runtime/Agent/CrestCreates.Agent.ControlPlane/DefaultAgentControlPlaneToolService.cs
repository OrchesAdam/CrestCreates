using System.Collections.Concurrent;
using CrestCreates.Agent.ControlPlane.Abstractions;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.ContextPack.Abstractions;
using Microsoft.Extensions.Logging;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftStore = CrestCreates.DescriptorDraft.Abstractions.IDescriptorDraftStore;
using DraftValidator = CrestCreates.DescriptorDraft.Abstractions.IDescriptorDraftValidator;
using DraftReviewService = CrestCreates.DescriptorDraft.Abstractions.IDescriptorDraftReviewService;
using DraftMaterializer = CrestCreates.DescriptorDraft.Abstractions.IDescriptorDraftMaterializer;
using DraftValidationResult = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraftValidationResult;
using DraftReviewResult = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraftReviewResult;
using DraftPackagePreview = CrestCreates.DescriptorDraft.Abstractions.DescriptorPackagePreview;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Default implementation of the Agent Control Plane tool surface.
/// Every method enforces: manifest lookup → permission check → service invocation → audit recording.
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
    private readonly ILogger<DefaultAgentControlPlaneToolService> _logger;

    // Local stores for review results, fix proposals, package previews, activation requests
    private readonly ConcurrentDictionary<(string TenantId, string Id), DraftReviewResult> _reviewResults = new();
    private readonly ConcurrentDictionary<(string TenantId, string Id), FixProposal> _fixProposals = new();
    private readonly ConcurrentDictionary<(string TenantId, string Id), DraftPackagePreview> _packagePreviews = new();
    private readonly ConcurrentDictionary<(string TenantId, string Id), PackageEvidencePreview> _evidencePreviews = new();
    private readonly ConcurrentDictionary<(string TenantId, string Id), ActivationRequest> _activationRequests = new();

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
        ILogger<DefaultAgentControlPlaneToolService> logger)
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
        _logger = logger;
    }

    // ── Helpers ──

    private async Task<AgentToolResult<T>> ExecuteAsync<T>(
        AgentToolInvocationContext context,
        string permissionName,
        Func<Task<AgentToolResult<T>>> action)
        where T : class
    {
        // 1. Manifest lookup
        var tool = _manifestProvider.GetToolByName(context.ToolName);
        if (tool is null)
        {
            var notFoundDiag = new AgentToolDiagnostic
            {
                Code = "TOOL_NOT_FOUND",
                Severity = AgentToolDiagnosticSeverity.Warning,
                Message = $"Tool '{context.ToolName}' is not a known Agent Control Plane tool."
            };
            var notFoundAudit = BuildAudit(context, AgentToolResultStatus.NotFound, [notFoundDiag]);
            await _auditor.RecordAsync(notFoundAudit);
            return new AgentToolResult<T>
            {
                Status = AgentToolResultStatus.NotFound,
                Value = null,
                Diagnostics = [notFoundDiag],
                AuditRecord = notFoundAudit
            };
        }

        // 2. Permission check
        var permission = new AgentToolPermissionRequirement
        {
            PermissionName = permissionName,
            Description = tool.Description
        };
        var authResult = await _authorizationService.AuthorizeAsync(context, permission);
        if (!authResult.IsAllowed)
        {
            var deniedAudit = BuildAudit(context, AgentToolResultStatus.Denied, authResult.DenialDiagnostics);
            await _auditor.RecordAsync(deniedAudit);
            return AgentToolResult<T>.Denied(authResult.DenialDiagnostics, deniedAudit);
        }

        // 3. Service invocation
        try
        {
            var result = await action();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool '{ToolName}' invocation failed", context.ToolName);
            var errorDiag = new AgentToolDiagnostic
            {
                Code = "TOOL_INVOCATION_FAILED",
                Severity = AgentToolDiagnosticSeverity.Error,
                Message = $"Tool '{context.ToolName}' invocation failed: {ex.Message}"
            };
            var errorAudit = BuildAudit(context, AgentToolResultStatus.Failed, [errorDiag]);
            await _auditor.RecordAsync(errorAudit);
            return AgentToolResult<T>.Failed([errorDiag], errorAudit);
        }
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
        AgentToolInvocationAuditRecord? baseAudit = null)
        where T : class
    {
        var audit = baseAudit ?? BuildAudit(context, result.Status, result.Diagnostics);
        await _auditor.RecordAsync(audit);
        return result with { AuditRecord = audit };
    }

    // ── Wave 1 — Context / Read ──

    public async Task<AgentToolResult<MetadataContextPack>> BuildMetadataContextPackAsync(
        AgentToolInvocationContext context, MetadataContextPackRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.ContextRead, async () =>
        {
            var descriptors = _descriptorCatalog.GetAll().ToList();
            var topology = _topologyBuilder.Build(descriptors);
            var pack = _contextPackBuilder.Build(request, topology, descriptors);

            var toolDiags = pack.Diagnostics.Select(MapFromContextPackDiagnostic).ToList();
            var audit = BuildAudit(context, AgentToolResultStatus.Success, toolDiags);
            audit = audit with { TouchedDescriptorRefs = pack.Summary.FocusRefs };
            await _auditor.RecordAsync(audit);

            return AgentToolResult<MetadataContextPack>.Success(pack, audit);
        });
    }

    public async Task<AgentToolResult<MetadataContextPack>> BuildRuntimeScenarioContextPackAsync(
        AgentToolInvocationContext context, MetadataContextPackRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.ContextRead, async () =>
        {
            var descriptors = _descriptorCatalog.GetAll().ToList();
            var topology = _topologyBuilder.Build(descriptors);
            var pack = _contextPackBuilder.Build(request, topology, descriptors);

            var toolDiags = pack.Diagnostics.Select(MapFromContextPackDiagnostic).ToList();
            var audit = BuildAudit(context, AgentToolResultStatus.Success, toolDiags);
            audit = audit with { TouchedDescriptorRefs = pack.Summary.FocusRefs };
            await _auditor.RecordAsync(audit);

            return AgentToolResult<MetadataContextPack>.Success(pack, audit);
        });
    }

    public async Task<AgentToolResult<DescriptorInfo>> GetDescriptorByRefAsync(
        AgentToolInvocationContext context, DescriptorRef descriptorRef, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.DescriptorRead, async () =>
        {
            // Version-aware resolution: build topology to resolve refs with version semantics
            var allDescriptors = _descriptorCatalog.GetAll().ToList();
            IDescriptor? descriptor;
            List<AgentToolDiagnostic> versionDiagnostics = [];

            if (descriptorRef.Version.HasValue)
            {
                // Version-pinned: match Namespace + Id + Version (IVersionedDescriptor)
                descriptor = allDescriptors.FirstOrDefault(d =>
                    d.Namespace == descriptorRef.Namespace &&
                    d.Id == descriptorRef.Id &&
                    d is IVersionedDescriptor vd &&
                    vd.Version == descriptorRef.Version.Value);
            }
            else
            {
                // Unpinned: match Namespace + Id, check for ambiguity
                var matches = allDescriptors
                    .Where(d => d.Namespace == descriptorRef.Namespace && d.Id == descriptorRef.Id)
                    .ToList();

                if (matches.Count > 1)
                {
                    // Ambiguous unpinned ref — multiple versions exist
                    versionDiagnostics.Add(new AgentToolDiagnostic
                    {
                        Code = "DESCRIPTOR_REF_AMBIGUOUS",
                        Severity = AgentToolDiagnosticSeverity.Warning,
                        Message = $"Descriptor ref '{descriptorRef.FullId}' is ambiguous: {matches.Count} versions found. Specify a version to disambiguate."
                    });
                    // Return the latest active version as the best match
                    descriptor = matches
                        .Where(d => d.State == DescriptorState.Active)
                        .OrderByDescending(d => d is IVersionedDescriptor vd ? vd.Version : 0)
                        .FirstOrDefault() ?? matches.First();
                }
                else
                {
                    descriptor = matches.FirstOrDefault();
                }
            }

            if (descriptor is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DescriptorInfo>.NotFound(
                        $"Descriptor '{descriptorRef.FullId}' not found."));
            }

            // Use the resolved descriptor's actual ref (with correct version)
            var resolvedRef = descriptor is IVersionedDescriptor vdesc
                ? new DescriptorRef(descriptor.Namespace, descriptor.Id, vdesc.Version)
                : new DescriptorRef(descriptor.Namespace, descriptor.Id);

            var info = new DescriptorInfo
            {
                Ref = resolvedRef,
                Kind = descriptor.Kind,
                Name = descriptor.Name,
                State = descriptor.State,
                ContractHash = descriptor.ContractHash,
                DefinitionHash = descriptor.DefinitionHash
            };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, versionDiagnostics);
            audit = audit with { TouchedDescriptorRefs = [resolvedRef] };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<DescriptorInfo>.Success(info, versionDiagnostics, audit);
        });
    }

    public async Task<AgentToolResult<DescriptorSearchResult>> SearchDescriptorsAsync(
        AgentToolInvocationContext context, DescriptorSearchRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.DescriptorSearch, async () =>
        {
            IEnumerable<IDescriptor> results = _descriptorCatalog.GetAll();

            if (request.Namespace is not null)
                results = results.Where(d => d.Namespace == request.Namespace);
            if (request.Kind.HasValue)
                results = results.Where(d => d.Kind == request.Kind.Value);
            if (request.NameContains is not null)
                results = results.Where(d => d.Name.Contains(request.NameContains, StringComparison.Ordinal));
            if (request.State.HasValue)
                results = results.Where(d => d.State == request.State.Value);

            var totalCount = results.Count();
            var wasTruncated = totalCount > request.MaxResults;
            var truncated = results.Take(request.MaxResults).ToList();

            var infos = truncated.Select(d =>
            {
                // Preserve version in DescriptorRef for versioned descriptors
                var refWithVersion = d is IVersionedDescriptor vd
                    ? new DescriptorRef(d.Namespace, d.Id, vd.Version)
                    : new DescriptorRef(d.Namespace, d.Id);
                return new DescriptorInfo
                {
                    Ref = refWithVersion,
                    Kind = d.Kind,
                    Name = d.Name,
                    State = d.State,
                    ContractHash = d.ContractHash,
                    DefinitionHash = d.DefinitionHash
                };
            }).ToList().AsReadOnly();

            var searchResult = new DescriptorSearchResult
            {
                Descriptors = infos,
                TotalCount = totalCount,
                WasTruncated = wasTruncated
            };

            var audit = BuildAudit(context, AgentToolResultStatus.Success,
                wasTruncated
                    ? [new AgentToolDiagnostic
                        {
                            Code = "SEARCH_TRUNCATED",
                            Severity = AgentToolDiagnosticSeverity.Info,
                            Message = $"Search returned {totalCount} results, truncated to {request.MaxResults}."
                        }]
                    : Array.Empty<AgentToolDiagnostic>());
            await _auditor.RecordAsync(audit);
            return AgentToolResult<DescriptorSearchResult>.Success(searchResult, audit);
        });
    }

    public async Task<AgentToolResult<DescriptorRelationshipsResult>> ListDescriptorRelationshipsAsync(
        AgentToolInvocationContext context, DescriptorRef descriptorRef, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.DescriptorRead, async () =>
        {
            // Use topology for reliable relationship discovery.
            // Direct extraction from a single descriptor misses incoming edges
            // from other descriptors (dependents owned by other descriptors).
            var allDescriptors = _descriptorCatalog.GetAll().ToList();
            var topology = _topologyBuilder.Build(allDescriptors.AsReadOnly());

            if (!topology.Contains(descriptorRef))
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DescriptorRelationshipsResult>.NotFound(
                        $"Descriptor '{descriptorRef.FullId}' not found in topology."));
            }

            var node = topology.FindNode(descriptorRef)!; // Guaranteed non-null after Contains check

            // Dependencies: outgoing edges from subject (what this descriptor depends on)
            var dependencies = node.OutgoingEdgeIndices
                .Select(i => topology.Edges[i])
                .Select(e => new DescriptorRelationship(e.From, e.To, e.Kind, e.Role, e.SourcePath, e.Strength, e.IsRuntimeBinding))
                .ToList().AsReadOnly();

            // Dependents: incoming edges to subject (what depends on this descriptor)
            var dependents = node.IncomingEdgeIndices
                .Select(i => topology.Edges[i])
                .Select(e => new DescriptorRelationship(e.From, e.To, e.Kind, e.Role, e.SourcePath, e.Strength, e.IsRuntimeBinding))
                .ToList().AsReadOnly();

            var result = new DescriptorRelationshipsResult
            {
                Subject = descriptorRef,
                Dependencies = dependencies,
                Dependents = dependents
            };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDescriptorRefs = [descriptorRef] };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<DescriptorRelationshipsResult>.Success(result, audit);
        });
    }

    public async Task<AgentToolResult<TopologySummaryResult>> GetTopologySummaryAsync(
        AgentToolInvocationContext context, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.ContextRead, async () =>
        {
            var descriptors = _descriptorCatalog.GetAll().ToList();
            var topology = _topologyBuilder.Build(descriptors);

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

            var audit = BuildAudit(context, AgentToolResultStatus.Success, result.TopologyDiagnostics);
            await _auditor.RecordAsync(audit);
            return AgentToolResult<TopologySummaryResult>.Success(result, audit);
        });
    }

    // ── Wave 2 — Draft ──

    public async Task<AgentToolResult<Draft>> CreateDescriptorDraftAsync(
        AgentToolInvocationContext context, CreateDescriptorDraftRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.DraftCreate, async () =>
        {
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
                Payload = request.Payload,
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
            await _auditor.RecordAsync(audit);
            return AgentToolResult<Draft>.Success(draft, audit);
        });
    }

    public async Task<AgentToolResult<Draft>> UpdateDescriptorDraftAsync(
        AgentToolInvocationContext context, UpdateDescriptorDraftRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.DraftUpdate, async () =>
        {
            var existing = await _draftStore.GetAsync(context.TenantId, request.DraftId, ct);
            if (existing is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<Draft>.NotFound($"Draft '{request.DraftId}' not found."));
            }

            var updated = existing with
            {
                Payload = request.Payload ?? existing.Payload,
                ProposedVersion = request.ProposedVersion ?? existing.ProposedVersion,
                Intent = request.Intent ?? existing.Intent,
                Rationale = request.Rationale ?? existing.Rationale,
                Metadata = request.Metadata ?? existing.Metadata
            };

            await _draftStore.SaveAsync(updated, ct);

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDraftIds = [updated.DraftId] };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<Draft>.Success(updated, audit);
        });
    }

    public async Task<AgentToolResult<Draft>> GetDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.DraftRead, async () =>
        {
            var draft = await _draftStore.GetAsync(context.TenantId, draftId, ct);
            if (draft is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<Draft>.NotFound($"Draft '{draftId}' not found."));
            }

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDraftIds = [draftId] };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<Draft>.Success(draft, audit);
        });
    }

    public async Task<AgentToolResult<DescriptorDraftListResult>> ListDescriptorDraftsAsync(
        AgentToolInvocationContext context, DraftAbstractions.DraftQuery? query, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.DraftList, async () =>
        {
            var drafts = await _draftStore.ListAsync(context.TenantId, query, ct);
            var result = new DescriptorDraftListResult
            {
                Drafts = drafts,
                TotalCount = drafts.Count
            };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDraftIds = drafts.Select(d => d.DraftId).ToList().AsReadOnly() };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<DescriptorDraftListResult>.Success(result, audit);
        });
    }

    public async Task<AgentToolResult<Draft>> CancelDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.DraftCancel, async () =>
        {
            var existing = await _draftStore.GetAsync(context.TenantId, draftId, ct);
            if (existing is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<Draft>.NotFound($"Draft '{draftId}' not found."));
            }

            var cancelled = existing with { Status = DraftAbstractions.DescriptorDraftStatus.Cancelled };
            await _draftStore.SaveAsync(cancelled, ct);

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDraftIds = [draftId] };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<Draft>.Success(cancelled, audit);
        });
    }

    public async Task<AgentToolResult<DraftComparisonResult>> CompareDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.DraftRead, async () =>
        {
            var draft = await _draftStore.GetAsync(context.TenantId, draftId, ct);
            if (draft is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DraftComparisonResult>.NotFound($"Draft '{draftId}' not found."));
            }

            var currentActive = _descriptorCatalog.Get(draft.DescriptorId);

            var differences = new List<DraftDifference>();
            if (currentActive is not null)
            {
                differences.Add(new DraftDifference
                {
                    Path = "Name",
                    CurrentValue = currentActive.Name ?? "",
                    ProposedValue = draft.Payload.GetDescriptor().Name ?? "",
                    Kind = DraftDifferenceKind.Modified
                });
            }

            var result = new DraftComparisonResult
            {
                Draft = draft,
                CurrentActiveDescriptor = currentActive,
                Differences = differences.AsReadOnly()
            };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDraftIds = [draftId] };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<DraftComparisonResult>.Success(result, audit);
        });
    }

    // ── Wave 3 — Review ──

    public async Task<AgentToolResult<DraftValidationResult>> ValidateDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.ReviewValidate, async () =>
        {
            var draft = await _draftStore.GetAsync(context.TenantId, draftId, ct);
            if (draft is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DraftValidationResult>.NotFound($"Draft '{draftId}' not found."));
            }

            var validationResult = _draftValidator.Validate(draft);

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedDraftIds = [draftId] };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<DraftValidationResult>.Success(validationResult, audit);
        });
    }

    public async Task<AgentToolResult<DraftReviewResult>> ReviewDescriptorDraftAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.ReviewRun, async () =>
        {
            var draft = await _draftStore.GetAsync(context.TenantId, draftId, ct);
            if (draft is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DraftReviewResult>.NotFound($"Draft '{draftId}' not found."));
            }

            var currentInventory = _descriptorCatalog.GetAll().ToList();
            var reviewResult = await _draftReviewService.ReviewAsync(draft, currentInventory, ct);

            // Store review result for later retrieval
            var reviewId = Guid.NewGuid().ToString("N");
            _reviewResults[(context.TenantId, reviewId)] = reviewResult;

            // Update draft status to Reviewed
            var reviewed = draft with { Status = DraftAbstractions.DescriptorDraftStatus.Reviewed };
            await _draftStore.SaveAsync(reviewed, ct);

            var toolDiags = reviewResult.Diagnostics.Select(MapFromDraftDiagnostic).ToList();
            var audit = BuildAudit(context, AgentToolResultStatus.Success, toolDiags);
            audit = audit with
            {
                TouchedDraftIds = [draftId],
                TouchedReviewResultIds = [reviewId]
            };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<DraftReviewResult>.Success(reviewResult, audit);
        });
    }

    public async Task<AgentToolResult<DraftReviewResult>> GetDraftReviewResultAsync(
        AgentToolInvocationContext context, string reviewResultId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.ReviewRead, async () =>
        {
            if (!_reviewResults.TryGetValue((context.TenantId, reviewResultId), out var result))
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DraftReviewResult>.NotFound($"Review result '{reviewResultId}' not found."));
            }

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedReviewResultIds = [reviewResultId] };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<DraftReviewResult>.Success(result, audit);
        });
    }

    public async Task<AgentToolResult<ReviewResultListResult>> ListDraftReviewResultsAsync(
        AgentToolInvocationContext context, string? draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.ReviewRead, async () =>
        {
            var results = _reviewResults
                .Where(kvp => kvp.Key.TenantId == context.TenantId)
                .Select(kvp => kvp.Value)
                .ToList();

            if (draftId is not null)
                results = results.Where(r => r.DraftId == draftId).ToList();

            var result = new ReviewResultListResult { Results = results.AsReadOnly() };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            await _auditor.RecordAsync(audit);
            return AgentToolResult<ReviewResultListResult>.Success(result, audit);
        });
    }

    public async Task<AgentToolResult<DiagnosticExplanation>> ExplainDiagnosticsAsync(
        AgentToolInvocationContext context, ExplainDiagnosticsRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.DiagnosticExplain, async () =>
        {
            var entries = request.Diagnostics.Select(d => new DiagnosticExplanationEntry
            {
                Code = d.Code,
                Explanation = ExplainCode(d.Code),
                Remediation = SuggestRemediation(d.Code),
                Severity = d.Severity,
                SuggestedFixToolNames = SuggestFixTools(d.Code)
            }).ToList().AsReadOnly();

            var result = new DiagnosticExplanation { Explanations = entries };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            if (request.DraftId is not null)
            {
                audit = audit with { TouchedDraftIds = [request.DraftId] };
            }
            await _auditor.RecordAsync(audit);
            return AgentToolResult<DiagnosticExplanation>.Success(result, audit);
        });
    }

    // ── Wave 4 — Fix Proposal ──

    public async Task<AgentToolResult<FixProposalListResult>> SuggestDescriptorDraftFixesAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.FixSuggest, async () =>
        {
            var draft = await _draftStore.GetAsync(context.TenantId, draftId, ct);
            if (draft is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<FixProposalListResult>.NotFound($"Draft '{draftId}' not found."));
            }

            var validationResult = _draftValidator.Validate(draft);
            var proposals = new List<FixProposal>();

            foreach (var diag in validationResult.Diagnostics)
            {
                var proposalId = Guid.NewGuid().ToString("N");
                var proposal = new FixProposal
                {
                    ProposalId = proposalId,
                    DraftId = draftId,
                    TenantId = context.TenantId,
                    RiskLevel = MapDiagnosticToRiskLevel(diag.Severity),
                    RequiresHumanApproval = diag.Severity is DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error
                        or DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker,
                    Actions = GenerateFixActions(diag),
                    Diagnostics = [MapFromDraftDiagnostic(diag)],
                    CreatedAt = DateTimeOffset.UtcNow,
                    Rationale = $"Fix for diagnostic: {diag.Message}"
                };
                _fixProposals[(context.TenantId, proposalId)] = proposal;
                proposals.Add(proposal);
            }

            var result = new FixProposalListResult { Proposals = proposals.AsReadOnly() };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with
            {
                TouchedDraftIds = [draftId],
                TouchedFixProposalIds = proposals.Select(p => p.ProposalId).ToList().AsReadOnly()
            };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<FixProposalListResult>.Success(result, audit);
        });
    }

    public async Task<AgentToolResult<FixProposal>> GetFixProposalAsync(
        AgentToolInvocationContext context, string proposalId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.FixSuggest, async () =>
        {
            if (!_fixProposals.TryGetValue((context.TenantId, proposalId), out var proposal))
            {
                return await RecordAndReturn(context,
                    AgentToolResult<FixProposal>.NotFound($"Fix proposal '{proposalId}' not found."));
            }

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedFixProposalIds = [proposalId] };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<FixProposal>.Success(proposal, audit);
        });
    }

    public async Task<AgentToolResult<FixProposalListResult>> ListFixProposalsAsync(
        AgentToolInvocationContext context, string? draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.FixSuggest, async () =>
        {
            var proposals = _fixProposals
                .Where(kvp => kvp.Key.TenantId == context.TenantId)
                .Select(kvp => kvp.Value);

            if (draftId is not null)
                proposals = proposals.Where(p => p.DraftId == draftId);

            var result = new FixProposalListResult { Proposals = proposals.ToList().AsReadOnly() };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            await _auditor.RecordAsync(audit);
            return AgentToolResult<FixProposalListResult>.Success(result, audit);
        });
    }

    public async Task<AgentToolResult<Draft>> ApplyFixProposalToDraftAsync(
        AgentToolInvocationContext context, ApplyFixProposalRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.FixApplyToDraft, async () =>
        {
            if (!_fixProposals.TryGetValue((context.TenantId, request.ProposalId), out var proposal))
            {
                return await RecordAndReturn(context,
                    AgentToolResult<Draft>.NotFound($"Fix proposal '{request.ProposalId}' not found."));
            }

            var draft = await _draftStore.GetAsync(context.TenantId, request.DraftId, ct);
            if (draft is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<Draft>.NotFound($"Draft '{request.DraftId}' not found."));
            }

            if (proposal.DraftId != request.DraftId)
            {
                var mismatchDiag = new AgentToolDiagnostic
                {
                    Code = "PROPOSAL_DRAFT_MISMATCH",
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Fix proposal '{request.ProposalId}' is for draft '{proposal.DraftId}', not '{request.DraftId}'."
                };
                var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [mismatchDiag]);
                await _auditor.RecordAsync(audit);
                return AgentToolResult<Draft>.InvalidRequest([mismatchDiag], audit);
            }

            // Apply fix proposal to draft (creates updated revision only, never patches active descriptors)
            // First apply proposal-level rationale as baseline, then apply individual actions
            // (actions take precedence over proposal-level rationale for the same field).
            // Payload-level mutations require typed payload support not yet available;
            // those actions are recorded but not applied automatically.
            var updatedDraft = draft;

            // Record rationale from the proposal as baseline
            if (proposal.Rationale is not null)
                updatedDraft = updatedDraft with { Rationale = proposal.Rationale };

            var appliedPaths = new List<string>();
            var skippedPaths = new List<string>();

            foreach (var action in proposal.Actions)
            {
                var applied = ApplyActionToDraft(ref updatedDraft, action);
                if (applied)
                    appliedPaths.Add(action.Path);
                else
                    skippedPaths.Add(action.Path);
            }

            await _draftStore.SaveAsync(updatedDraft, ct);

            var successDiags = new List<AgentToolDiagnostic>();
            if (appliedPaths.Count > 0)
            {
                successDiags.Add(new AgentToolDiagnostic
                {
                    Code = "FIX_ACTIONS_APPLIED",
                    Severity = AgentToolDiagnosticSeverity.Info,
                    Message = $"Applied {appliedPaths.Count} action(s) to draft fields: {string.Join(", ", appliedPaths)}."
                });
            }
            if (skippedPaths.Count > 0)
            {
                successDiags.Add(new AgentToolDiagnostic
                {
                    Code = "FIX_ACTIONS_SKIPPED",
                    Severity = AgentToolDiagnosticSeverity.Warning,
                    Message = $"Skipped {skippedPaths.Count} action(s) requiring payload mutation (not yet supported): {string.Join(", ", skippedPaths)}. Apply these manually."
                });
            }

            var successAudit = BuildAudit(context, AgentToolResultStatus.Success, successDiags);
            successAudit = successAudit with
            {
                TouchedDraftIds = [request.DraftId],
                TouchedFixProposalIds = [request.ProposalId]
            };
            await _auditor.RecordAsync(successAudit);
            return AgentToolResult<Draft>.Success(updatedDraft, successDiags, successAudit);
        });
    }

    // ── Wave 5 — Package Preview ──

    public async Task<AgentToolResult<DraftPackagePreview>> PreviewDescriptorPackageAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.PackagePreview, async () =>
        {
            var draft = await _draftStore.GetAsync(context.TenantId, draftId, ct);
            if (draft is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DraftPackagePreview>.NotFound($"Draft '{draftId}' not found."));
            }

            var currentInventory = _descriptorCatalog.GetAll().ToList();
            var materializationResult = _draftMaterializer.Materialize(draft, currentInventory);
            if (!materializationResult.IsMaterialized)
            {
                var failDiags = materializationResult.Diagnostics.Select(MapFromDraftDiagnostic).ToList();
                var audit = BuildAudit(context, AgentToolResultStatus.Failed, failDiags);
                await _auditor.RecordAsync(audit);
                return AgentToolResult<DraftPackagePreview>.Failed(failDiags, audit);
            }

            var pkgRequest = new DescriptorPackageBuildRequest
            {
                PackageId = draftId,
                PackageVersion = draft.ProposedVersion ?? "1",
                Name = draft.Intent,
                CreatedBy = draft.AuthorId,
                Source = draft.Source,
                CreatedAt = draft.CreatedAt,
                Descriptors = materializationResult.ProposedInventory
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

            var previewId = Guid.NewGuid().ToString("N");
            _packagePreviews[(context.TenantId, previewId)] = preview;

            var audit2 = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit2 = audit2 with
            {
                TouchedDraftIds = [draftId],
                TouchedPackagePreviewIds = [previewId]
            };
            await _auditor.RecordAsync(audit2);
            return AgentToolResult<DraftPackagePreview>.Success(preview, audit2);
        });
    }

    public async Task<AgentToolResult<PackageEvidencePreview>> BuildPackageEvidencePreviewAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.PackagePreview, async () =>
        {
            var draft = await _draftStore.GetAsync(context.TenantId, draftId, ct);
            if (draft is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<PackageEvidencePreview>.NotFound($"Draft '{draftId}' not found."));
            }

            var currentInventory = _descriptorCatalog.GetAll().ToList();
            var reviewResult = await _draftReviewService.ReviewAsync(draft, currentInventory, ct);

            var pkgRequest = new DescriptorPackageBuildRequest
            {
                PackageId = draftId,
                PackageVersion = draft.ProposedVersion ?? "1",
                Name = draft.Intent,
                CreatedBy = draft.AuthorId,
                Source = draft.Source,
                CreatedAt = draft.CreatedAt,
                Descriptors = reviewResult.ProposedInventory ?? currentInventory
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

            var result = new PackageEvidencePreview
            {
                DraftId = draftId,
                TenantId = context.TenantId,
                PackagePreview = preview,
                Evidence = pkg.Evidence,
                Diagnostics = reviewResult.Diagnostics.Select(MapFromDraftDiagnostic).ToList().AsReadOnly()
            };

            // Store evidence preview for later retrieval and activation reference validation
            var evidencePreviewId = Guid.NewGuid().ToString("N");
            _evidencePreviews[(context.TenantId, evidencePreviewId)] = result;

            var audit = BuildAudit(context, AgentToolResultStatus.Success, result.Diagnostics);
            audit = audit with
            {
                TouchedDraftIds = [draftId],
                TouchedPackagePreviewIds = [evidencePreviewId]
            };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<PackageEvidencePreview>.Success(result, audit);
        });
    }

    public async Task<AgentToolResult<ActivationReadinessPreview>> BuildActivationReadinessPreviewAsync(
        AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.PackagePreview, async () =>
        {
            var draft = await _draftStore.GetAsync(context.TenantId, draftId, ct);
            if (draft is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<ActivationReadinessPreview>.NotFound($"Draft '{draftId}' not found."));
            }

            var currentInventory = _descriptorCatalog.GetAll().ToList();
            var reviewResult = await _draftReviewService.ReviewAsync(draft, currentInventory, ct);

            var blockers = new List<ActivationReadinessBlocker>();

            if (!reviewResult.ValidationResult.IsValid)
            {
                blockers.Add(new ActivationReadinessBlocker
                {
                    Code = "VALIDATION_FAILED",
                    Message = "Draft validation failed.",
                    Severity = ActivationReadinessBlockerSeverity.Blocker,
                    Remedy = "Fix validation errors before requesting activation."
                });
            }

            if (reviewResult.Diagnostics.Any(d =>
                    d.Severity is DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error
                        or DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker))
            {
                blockers.Add(new ActivationReadinessBlocker
                {
                    Code = "REVIEW_HAS_ERRORS",
                    Message = "Review produced error or blocker diagnostics.",
                    Severity = ActivationReadinessBlockerSeverity.Blocker,
                    Remedy = "Resolve error/blocker diagnostics before requesting activation."
                });
            }

            if (!reviewResult.IsActivationEligible)
            {
                blockers.Add(new ActivationReadinessBlocker
                {
                    Code = "NOT_ACTIVATION_ELIGIBLE",
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
                Diagnostics = reviewResult.Diagnostics.Select(MapFromDraftDiagnostic).ToList().AsReadOnly()
            };

            var audit = BuildAudit(context, AgentToolResultStatus.Success, result.Diagnostics);
            audit = audit with { TouchedDraftIds = [draftId] };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<ActivationReadinessPreview>.Success(result, audit);
        });
    }

    public async Task<AgentToolResult<DraftPackagePreview>> GetPackagePreviewAsync(
        AgentToolInvocationContext context, string previewId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.PackagePreview, async () =>
        {
            if (!_packagePreviews.TryGetValue((context.TenantId, previewId), out var preview))
            {
                return await RecordAndReturn(context,
                    AgentToolResult<DraftPackagePreview>.NotFound($"Package preview '{previewId}' not found."));
            }

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedPackagePreviewIds = [previewId] };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<DraftPackagePreview>.Success(preview, audit);
        });
    }

    // ── Wave 6 — Activation Handoff ──

    public async Task<AgentToolResult<ActivationRequest>> SubmitActivationRequestAsync(
        AgentToolInvocationContext context, SubmitActivationRequestRequest request, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.ActivationRequestSubmit, async () =>
        {
            var draft = await _draftStore.GetAsync(context.TenantId, request.DraftId, ct);
            if (draft is null)
            {
                return await RecordAndReturn(context,
                    AgentToolResult<ActivationRequest>.NotFound($"Draft '{request.DraftId}' not found."));
            }

            // Requires at least one reference
            if (request.ReviewResultId is null &&
                request.PackagePreviewId is null &&
                request.EvidencePreviewId is null)
            {
                var diag = new AgentToolDiagnostic
                {
                    Code = "ACTIVATION_MISSING_REFERENCES",
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = "Activation request requires at least one reference (review result, package preview, or evidence preview)."
                };
                var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [diag]);
                await _auditor.RecordAsync(audit);
                return AgentToolResult<ActivationRequest>.InvalidRequest([diag], audit);
            }

            // Validate that referenced artifacts exist, belong to this tenant, and match the draft
            var refDiagnostics = new List<AgentToolDiagnostic>();

            if (request.ReviewResultId is not null)
            {
                if (!_reviewResults.TryGetValue((context.TenantId, request.ReviewResultId), out var reviewRef))
                {
                    refDiagnostics.Add(new AgentToolDiagnostic
                    {
                        Code = "ACTIVATION_REVIEW_RESULT_NOT_FOUND",
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Referenced review result '{request.ReviewResultId}' not found for this tenant."
                    });
                }
                else if (reviewRef.DraftId != request.DraftId)
                {
                    refDiagnostics.Add(new AgentToolDiagnostic
                    {
                        Code = "ACTIVATION_REVIEW_RESULT_DRAFT_MISMATCH",
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Referenced review result '{request.ReviewResultId}' belongs to draft '{reviewRef.DraftId}', not '{request.DraftId}'."
                    });
                }
            }

            if (request.PackagePreviewId is not null)
            {
                if (!_packagePreviews.TryGetValue((context.TenantId, request.PackagePreviewId), out _))
                {
                    refDiagnostics.Add(new AgentToolDiagnostic
                    {
                        Code = "ACTIVATION_PACKAGE_PREVIEW_NOT_FOUND",
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Referenced package preview '{request.PackagePreviewId}' not found for this tenant."
                    });
                }
                // Package previews are not directly keyed by DraftId, so we cannot check draft match here
            }

            if (request.EvidencePreviewId is not null)
            {
                if (!_evidencePreviews.TryGetValue((context.TenantId, request.EvidencePreviewId), out var evidenceRef))
                {
                    refDiagnostics.Add(new AgentToolDiagnostic
                    {
                        Code = "ACTIVATION_EVIDENCE_PREVIEW_NOT_FOUND",
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Referenced evidence preview '{request.EvidencePreviewId}' not found for this tenant."
                    });
                }
                else if (evidenceRef.DraftId != request.DraftId)
                {
                    refDiagnostics.Add(new AgentToolDiagnostic
                    {
                        Code = "ACTIVATION_EVIDENCE_PREVIEW_DRAFT_MISMATCH",
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"Referenced evidence preview '{request.EvidencePreviewId}' belongs to draft '{evidenceRef.DraftId}', not '{request.DraftId}'."
                    });
                }
            }

            if (refDiagnostics.Count > 0)
            {
                var refAudit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, refDiagnostics);
                await _auditor.RecordAsync(refAudit);
                return AgentToolResult<ActivationRequest>.InvalidRequest(refDiagnostics, refAudit);
            }

            var activationRequest = new ActivationRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                TenantId = context.TenantId,
                DraftId = request.DraftId,
                Status = Abstractions.ActivationRequestStatus.Submitted,
                SubmittedAt = DateTimeOffset.UtcNow,
                SubmittedBy = context.ActorId,
                ReviewResultId = request.ReviewResultId,
                PackagePreviewId = request.PackagePreviewId,
                EvidencePreviewId = request.EvidencePreviewId,
                CorrelationId = request.CorrelationId ?? context.CorrelationId,
                Diagnostics = []
            };

            _activationRequests[(context.TenantId, activationRequest.RequestId)] = activationRequest;

            var successAudit = BuildAudit(context, AgentToolResultStatus.Success, []);
            successAudit = successAudit with
            {
                TouchedDraftIds = [request.DraftId],
                TouchedActivationRequestIds = [activationRequest.RequestId]
            };
            await _auditor.RecordAsync(successAudit);
            return AgentToolResult<ActivationRequest>.Success(activationRequest, successAudit);
        });
    }

    public async Task<AgentToolResult<ActivationRequest>> GetActivationRequestStatusAsync(
        AgentToolInvocationContext context, string requestId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.ActivationRequestRead, async () =>
        {
            if (!_activationRequests.TryGetValue((context.TenantId, requestId), out var request))
            {
                return await RecordAndReturn(context,
                    AgentToolResult<ActivationRequest>.NotFound($"Activation request '{requestId}' not found."));
            }

            var audit = BuildAudit(context, AgentToolResultStatus.Success, []);
            audit = audit with { TouchedActivationRequestIds = [requestId] };
            await _auditor.RecordAsync(audit);
            return AgentToolResult<ActivationRequest>.Success(request, audit);
        });
    }

    public async Task<AgentToolResult<ActivationRequest>> CancelActivationRequestAsync(
        AgentToolInvocationContext context, string requestId, CancellationToken ct = default)
    {
        return await ExecuteAsync(context, AgentToolPermissionName.ActivationRequestCancel, async () =>
        {
            if (!_activationRequests.TryGetValue((context.TenantId, requestId), out var request))
            {
                return await RecordAndReturn(context,
                    AgentToolResult<ActivationRequest>.NotFound($"Activation request '{requestId}' not found."));
            }

            if (request.Status is Abstractions.ActivationRequestStatus.Approved
                or Abstractions.ActivationRequestStatus.Rejected)
            {
                var diag = new AgentToolDiagnostic
                {
                    Code = "ACTIVATION_REQUEST_TERMINAL",
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Activation request '{requestId}' is in terminal state '{request.Status}' and cannot be cancelled."
                };
                var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [diag]);
                await _auditor.RecordAsync(audit);
                return AgentToolResult<ActivationRequest>.InvalidRequest([diag], audit);
            }

            var cancelled = request with { Status = Abstractions.ActivationRequestStatus.Cancelled };
            _activationRequests[(context.TenantId, requestId)] = cancelled;

            var successAudit = BuildAudit(context, AgentToolResultStatus.Success, []);
            successAudit = successAudit with { TouchedActivationRequestIds = [requestId] };
            await _auditor.RecordAsync(successAudit);
            return AgentToolResult<ActivationRequest>.Success(cancelled, successAudit);
        });
    }

    // ── Mappers ──

    /// <summary>
    /// Applies a single fix proposal action to a draft's scalar fields.
    /// Returns true if the action was applied, false if it requires payload-level
    /// mutation that is not yet supported by the typed payload infrastructure.
    /// </summary>
    private static bool ApplyActionToDraft(ref Draft draft, FixProposalAction action)
    {
        if (action.ActionKind != FixProposalActionKind.Set)
            return false; // Remove/Add not yet supported for draft fields

        return action.Path switch
        {
            "Intent" => ApplySetField(ref draft, d => d with { Intent = action.ProposedValue }),
            "Rationale" => ApplySetField(ref draft, d => d with { Rationale = action.ProposedValue }),
            "ProposedVersion" => ApplySetField(ref draft, d => d with { ProposedVersion = action.ProposedValue }),
            "CorrelationId" => ApplySetField(ref draft, d => d with { CorrelationId = action.ProposedValue }),
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

    private static IReadOnlyList<FixProposalAction> GenerateFixActions(
        DraftAbstractions.DescriptorDraftDiagnostic diagnostic)
    {
        var actions = new List<FixProposalAction>();

        if (diagnostic.Code == "DRAFT_ID_EMPTY")
        {
            actions.Add(new FixProposalAction
            {
                Path = "DraftId",
                ActionKind = FixProposalActionKind.Set,
                CurrentValue = "",
                ProposedValue = "(auto-generated)",
                Description = "Set DraftId to a generated value."
            });
        }
        else if (diagnostic.Code == "DESCRIPTOR_ID_EMPTY")
        {
            actions.Add(new FixProposalAction
            {
                Path = "DescriptorId",
                ActionKind = FixProposalActionKind.Set,
                CurrentValue = "",
                ProposedValue = "(must be specified)",
                Description = "Provide a DescriptorId."
            });
        }
        else if (diagnostic.Code == "AUTHOR_ID_EMPTY")
        {
            actions.Add(new FixProposalAction
            {
                Path = "AuthorId",
                ActionKind = FixProposalActionKind.Set,
                CurrentValue = "",
                ProposedValue = "(must be specified)",
                Description = "Provide an AuthorId."
            });
        }
        else if (diagnostic.Code == "RATIONALE_EMPTY")
        {
            actions.Add(new FixProposalAction
            {
                Path = "Rationale",
                ActionKind = FixProposalActionKind.Set,
                CurrentValue = "",
                ProposedValue = "(provide rationale)",
                Description = "Provide a rationale for the draft."
            });
        }
        else if (diagnostic.Code == "INTENT_EMPTY")
        {
            actions.Add(new FixProposalAction
            {
                Path = "Intent",
                ActionKind = FixProposalActionKind.Set,
                CurrentValue = "",
                ProposedValue = "(provide intent)",
                Description = "Provide an intent for the draft."
            });
        }

        return actions.AsReadOnly();
    }

    private static string ExplainCode(string code) => code switch
    {
        "DRAFT_ID_EMPTY" => "The draft identifier must not be empty.",
        "DESCRIPTOR_ID_EMPTY" => "The descriptor identifier must not be empty.",
        "AUTHOR_ID_EMPTY" => "The author identifier must not be empty.",
        "KIND_PAYLOAD_MISMATCH" => "The declared DescriptorKind does not match the Payload's DescriptorKind.",
        "PAYLOAD_ID_MISMATCH" => "The Payload descriptor Id does not match the draft's DescriptorId.",
        "PROPOSED_VERSION_MISSING" => "ProposedVersion is required for Create and Update operations.",
        "PROPOSED_VERSION_NOT_INTEGER" => "ProposedVersion must be a valid integer.",
        "PROPOSED_VERSION_MISMATCH" => "ProposedVersion does not match the payload descriptor version.",
        "CREATE_BASE_VERSION_MUST_BE_EMPTY" => "Create operation must not specify BaseVersion.",
        "UPDATE_BASE_VERSION_REQUIRED" => "Update operation requires BaseVersion.",
        _ => $"No detailed explanation available for code '{code}'."
    };

    private static string SuggestRemediation(string code) => code switch
    {
        "DRAFT_ID_EMPTY" => "Provide a non-empty DraftId or use auto-generation.",
        "DESCRIPTOR_ID_EMPTY" => "Provide the descriptor identifier this draft targets.",
        "AUTHOR_ID_EMPTY" => "Provide the author identifier.",
        "KIND_PAYLOAD_MISMATCH" => "Ensure DescriptorKind matches the Payload type.",
        "PAYLOAD_ID_MISMATCH" => "Ensure the Payload descriptor Id matches the draft DescriptorId.",
        "PROPOSED_VERSION_MISSING" => "Set ProposedVersion on the draft.",
        "PROPOSED_VERSION_NOT_INTEGER" => "Set ProposedVersion to an integer string.",
        "PROPOSED_VERSION_MISMATCH" => "Ensure ProposedVersion matches the payload descriptor version.",
        "CREATE_BASE_VERSION_MUST_BE_EMPTY" => "Remove BaseVersion for Create operations.",
        "UPDATE_BASE_VERSION_REQUIRED" => "Set BaseVersion for Update operations.",
        _ => "Review the diagnostic and adjust the draft accordingly."
    };

    private static IReadOnlyList<string> SuggestFixTools(string code) => code switch
    {
        "DRAFT_ID_EMPTY" or "DESCRIPTOR_ID_EMPTY" or "AUTHOR_ID_EMPTY"
            => ["SuggestDescriptorDraftFixes"],
        _ => Array.Empty<string>()
    };
}
