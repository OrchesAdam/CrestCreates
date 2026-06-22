using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Static, deterministic tool manifest provider.
/// No runtime reflection, no assembly scanning, no dynamic plugin loading.
/// Every tool is declared with its permissions and audit requirements upfront.
/// Permission requirements carry ToolCategory and IsReadOnly so that consumers
/// (e.g., MCP/HTTP adapters) can make authorization decisions without the facade.
/// </summary>
public sealed class StaticAgentToolManifestProvider : IAgentToolManifestProvider
{
    private static readonly IReadOnlyList<AgentToolDescriptor> Tools = BuildToolList();
    private static readonly Dictionary<string, AgentToolDescriptor> ToolsByName =
        Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public IReadOnlyList<AgentToolDescriptor> GetAllTools() => Tools;

    public AgentToolDescriptor? GetToolByName(string name)
        => ToolsByName.TryGetValue(name, out var tool) ? tool : null;

    private static IReadOnlyList<AgentToolDescriptor> BuildToolList()
    {
        var allActors = new[] { AgentToolActorKind.Human, AgentToolActorKind.Agent,
                        AgentToolActorKind.System, AgentToolActorKind.Import,
                        AgentToolActorKind.Generator };

        var tools = new List<AgentToolDescriptor>
        {
            // ── Context / Read ──
            new()
            {
                Name = AgentToolName.BuildMetadataContextPack,
                Description = "Build a metadata context pack for the specified focus descriptors and scope.",
                Category = AgentToolCategory.Context,
                Permissions = [Perm(AgentToolPermissionName.ContextRead, AgentToolCategory.Context, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.BuildRuntimeScenarioContextPack,
                Description = "Build a runtime scenario context pack following a traversal recipe.",
                Category = AgentToolCategory.Context,
                Permissions = [Perm(AgentToolPermissionName.ContextRead, AgentToolCategory.Context, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.GetDescriptorByRef,
                Description = "Get bounded descriptor information by descriptor reference.",
                Category = AgentToolCategory.Context,
                Permissions = [Perm(AgentToolPermissionName.DescriptorRead, AgentToolCategory.Context, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.SearchDescriptors,
                Description = "Search descriptors with bounded, deterministic results.",
                Category = AgentToolCategory.Context,
                Permissions = [Perm(AgentToolPermissionName.DescriptorSearch, AgentToolCategory.Context, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.ListDescriptorRelationships,
                Description = "List relationships for a descriptor with version-aware refs.",
                Category = AgentToolCategory.Context,
                Permissions = [Perm(AgentToolPermissionName.DescriptorRead, AgentToolCategory.Context, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.GetTopologySummary,
                Description = "Get a summary of the current descriptor topology.",
                Category = AgentToolCategory.Context,
                Permissions = [Perm(AgentToolPermissionName.ContextRead, AgentToolCategory.Context, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },

            // ── Draft ──
            new()
            {
                Name = AgentToolName.CreateDescriptorDraft,
                Description = "Create a new descriptor draft. Does not activate or mutate runtime registries.",
                Category = AgentToolCategory.Draft,
                Permissions = [Perm(AgentToolPermissionName.DraftCreate, AgentToolCategory.Draft, false)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.UpdateDescriptorDraft,
                Description = "Update an existing descriptor draft. Creates a new revision only.",
                Category = AgentToolCategory.Draft,
                Permissions = [Perm(AgentToolPermissionName.DraftUpdate, AgentToolCategory.Draft, false)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.GetDescriptorDraft,
                Description = "Retrieve a descriptor draft by ID.",
                Category = AgentToolCategory.Draft,
                Permissions = [Perm(AgentToolPermissionName.DraftRead, AgentToolCategory.Draft, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.ListDescriptorDrafts,
                Description = "List descriptor drafts with optional query filters. Bounded and deterministic.",
                Category = AgentToolCategory.Draft,
                Permissions = [Perm(AgentToolPermissionName.DraftList, AgentToolCategory.Draft, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.CancelDescriptorDraft,
                Description = "Cancel a descriptor draft. Does not affect active descriptors.",
                Category = AgentToolCategory.Draft,
                Permissions = [Perm(AgentToolPermissionName.DraftCancel, AgentToolCategory.Draft, false)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.CompareDescriptorDraft,
                Description = "Compare a descriptor draft against the current active descriptor.",
                Category = AgentToolCategory.Draft,
                Permissions = [Perm(AgentToolPermissionName.DraftRead, AgentToolCategory.Draft, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },

            // ── Review ──
            new()
            {
                Name = AgentToolName.ValidateDescriptorDraft,
                Description = "Validate a descriptor draft without running the full review pipeline.",
                Category = AgentToolCategory.Review,
                Permissions = [Perm(AgentToolPermissionName.ReviewValidate, AgentToolCategory.Review, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.ReviewDescriptorDraft,
                Description = "Run the full review pipeline on a descriptor draft. Persists review result and updates draft status to Reviewed. Review pass does not imply activation approval.",
                Category = AgentToolCategory.Review,
                Permissions = [Perm(AgentToolPermissionName.ReviewRun, AgentToolCategory.Review, false)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.GetDraftReviewResult,
                Description = "Retrieve a stored draft review result.",
                Category = AgentToolCategory.Review,
                Permissions = [Perm(AgentToolPermissionName.ReviewRead, AgentToolCategory.Review, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.ListDraftReviewResults,
                Description = "List stored draft review results.",
                Category = AgentToolCategory.Review,
                Permissions = [Perm(AgentToolPermissionName.ReviewRead, AgentToolCategory.Review, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.ExplainDiagnostics,
                Description = "Explain diagnostic codes with human/LLM-readable descriptions and remediation.",
                Category = AgentToolCategory.Review,
                Permissions = [Perm(AgentToolPermissionName.DiagnosticExplain, AgentToolCategory.Review, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },

            // ── Review Report (Phase 7d) ──
            new()
            {
                Name = AgentToolName.BuildDescriptorReviewReport,
                Description = "Build a structured review report from a draft review result.",
                Category = AgentToolCategory.ReviewReport,
                Permissions = [Perm(AgentToolPermissionName.ReviewReportBuild, AgentToolCategory.ReviewReport, true)],
                AllowedActors = allActors,
                IsReadOnly = true,
                MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.RenderDescriptorReviewReport,
                Description = "Render a review report as Markdown or PlainText.",
                Category = AgentToolCategory.ReviewReport,
                Permissions = [Perm(AgentToolPermissionName.ReviewReportRender, AgentToolCategory.ReviewReport, true)],
                AllowedActors = allActors,
                IsReadOnly = true,
                MutatesRuntimeRegistry = false
            },

            // ── Fix Proposal ──
            new()
            {
                Name = AgentToolName.SuggestDescriptorDraftFixes,
                Description = "Suggest fix proposals for a descriptor draft based on diagnostics. Creates and persists retrievable proposals.",
                Category = AgentToolCategory.FixProposal,
                Permissions = [Perm(AgentToolPermissionName.FixSuggest, AgentToolCategory.FixProposal, false)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.GetFixProposal,
                Description = "Retrieve a stored fix proposal.",
                Category = AgentToolCategory.FixProposal,
                Permissions = [Perm(AgentToolPermissionName.FixSuggest, AgentToolCategory.FixProposal, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.ListFixProposals,
                Description = "List stored fix proposals for a draft.",
                Category = AgentToolCategory.FixProposal,
                Permissions = [Perm(AgentToolPermissionName.FixSuggest, AgentToolCategory.FixProposal, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.ApplyFixProposalToDraft,
                Description = "Apply a fix proposal to a descriptor draft. Updates draft/revision only, never active descriptors.",
                Category = AgentToolCategory.FixProposal,
                Permissions = [Perm(AgentToolPermissionName.FixApplyToDraft, AgentToolCategory.FixProposal, false)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },

            // ── Package Preview ──
            new()
            {
                Name = AgentToolName.PreviewDescriptorPackage,
                Description = "Preview a descriptor package for a draft. Persists package preview for activation handoff reference.",
                Category = AgentToolCategory.PackagePreview,
                Permissions = [Perm(AgentToolPermissionName.PackagePreview, AgentToolCategory.PackagePreview, false)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.BuildPackageEvidencePreview,
                Description = "Build a package evidence preview for a draft. Persists evidence for activation handoff reference.",
                Category = AgentToolCategory.PackagePreview,
                Permissions = [Perm(AgentToolPermissionName.PackagePreview, AgentToolCategory.PackagePreview, false)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.BuildActivationReadinessPreview,
                Description = "Build an activation readiness preview. Reports blockers but does not submit activation request.",
                Category = AgentToolCategory.PackagePreview,
                Permissions = [Perm(AgentToolPermissionName.PackagePreview, AgentToolCategory.PackagePreview, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.GetPackagePreview,
                Description = "Retrieve a stored package preview.",
                Category = AgentToolCategory.PackagePreview,
                Permissions = [Perm(AgentToolPermissionName.PackagePreview, AgentToolCategory.PackagePreview, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },

            // ── Activation Handoff ──
            new()
            {
                Name = AgentToolName.SubmitActivationRequest,
                Description = "Submit an activation request handoff record. Does not approve or execute activation.",
                Category = AgentToolCategory.ActivationHandoff,
                Permissions = [Perm(AgentToolPermissionName.ActivationRequestSubmit, AgentToolCategory.ActivationHandoff, false)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.GetActivationRequestStatus,
                Description = "Get the status of an activation request. Read-only.",
                Category = AgentToolCategory.ActivationHandoff,
                Permissions = [Perm(AgentToolPermissionName.ActivationRequestRead, AgentToolCategory.ActivationHandoff, true)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.CancelActivationRequest,
                Description = "Cancel an activation request. Cancels handoff only, does not affect runtime registry.",
                Category = AgentToolCategory.ActivationHandoff,
                Permissions = [Perm(AgentToolPermissionName.ActivationRequestCancel, AgentToolCategory.ActivationHandoff, false)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },

            // ── Manifest ──
            new()
            {
                Name = AgentToolName.ListAgentTools,
                Description = "List all available Agent Control Plane tools. No permission required.",
                Category = AgentToolCategory.Manifest,
                Permissions = [],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = AgentToolName.GetAgentToolDescriptor,
                Description = "Get the descriptor for a specific Agent Control Plane tool. No permission required.",
                Category = AgentToolCategory.Manifest,
                Permissions = [],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            }
        };

        return tools.AsReadOnly();
    }

    private static AgentToolPermissionRequirement Perm(
        string name,
        AgentToolCategory category,
        bool isReadOnly,
        string? description = null)
        => new()
        {
            PermissionName = name,
            ToolCategory = category,
            IsReadOnly = isReadOnly,
            Description = description
        };
}
