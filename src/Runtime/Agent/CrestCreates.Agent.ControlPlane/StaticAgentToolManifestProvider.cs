using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Static, deterministic tool manifest provider.
/// No runtime reflection, no assembly scanning, no dynamic plugin loading.
/// Every tool is declared with its permissions and audit requirements upfront.
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
                Name = "BuildMetadataContextPack",
                Description = "Build a metadata context pack for the specified focus descriptors and scope.",
                Category = AgentToolCategory.Context,
                Permissions = [Perm(AgentToolPermissionName.ContextRead)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "BuildRuntimeScenarioContextPack",
                Description = "Build a runtime scenario context pack following a traversal recipe.",
                Category = AgentToolCategory.Context,
                Permissions = [Perm(AgentToolPermissionName.ContextRead)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "GetDescriptorByRef",
                Description = "Get bounded descriptor information by descriptor reference.",
                Category = AgentToolCategory.Context,
                Permissions = [Perm(AgentToolPermissionName.DescriptorRead)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "SearchDescriptors",
                Description = "Search descriptors with bounded, deterministic results.",
                Category = AgentToolCategory.Context,
                Permissions = [Perm(AgentToolPermissionName.DescriptorSearch)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "ListDescriptorRelationships",
                Description = "List relationships for a descriptor with version-aware refs.",
                Category = AgentToolCategory.Context,
                Permissions = [Perm(AgentToolPermissionName.DescriptorRead)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "GetTopologySummary",
                Description = "Get a summary of the current descriptor topology.",
                Category = AgentToolCategory.Context,
                Permissions = [Perm(AgentToolPermissionName.ContextRead)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },

            // ── Draft ──
            new()
            {
                Name = "CreateDescriptorDraft",
                Description = "Create a new descriptor draft. Does not activate or mutate runtime registries.",
                Category = AgentToolCategory.Draft,
                Permissions = [Perm(AgentToolPermissionName.DraftCreate)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "UpdateDescriptorDraft",
                Description = "Update an existing descriptor draft. Creates a new revision only.",
                Category = AgentToolCategory.Draft,
                Permissions = [Perm(AgentToolPermissionName.DraftUpdate)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "GetDescriptorDraft",
                Description = "Retrieve a descriptor draft by ID.",
                Category = AgentToolCategory.Draft,
                Permissions = [Perm(AgentToolPermissionName.DraftRead)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "ListDescriptorDrafts",
                Description = "List descriptor drafts with optional query filters. Bounded and deterministic.",
                Category = AgentToolCategory.Draft,
                Permissions = [Perm(AgentToolPermissionName.DraftList)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "CancelDescriptorDraft",
                Description = "Cancel a descriptor draft. Does not affect active descriptors.",
                Category = AgentToolCategory.Draft,
                Permissions = [Perm(AgentToolPermissionName.DraftCancel)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "CompareDescriptorDraft",
                Description = "Compare a descriptor draft against the current active descriptor.",
                Category = AgentToolCategory.Draft,
                Permissions = [Perm(AgentToolPermissionName.DraftRead)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },

            // ── Review ──
            new()
            {
                Name = "ValidateDescriptorDraft",
                Description = "Validate a descriptor draft without running the full review pipeline.",
                Category = AgentToolCategory.Review,
                Permissions = [Perm(AgentToolPermissionName.ReviewValidate)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "ReviewDescriptorDraft",
                Description = "Run the full review pipeline on a descriptor draft. Review pass does not imply activation approval.",
                Category = AgentToolCategory.Review,
                Permissions = [Perm(AgentToolPermissionName.ReviewRun)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "GetDraftReviewResult",
                Description = "Retrieve a stored draft review result.",
                Category = AgentToolCategory.Review,
                Permissions = [Perm(AgentToolPermissionName.ReviewRead)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "ListDraftReviewResults",
                Description = "List stored draft review results.",
                Category = AgentToolCategory.Review,
                Permissions = [Perm(AgentToolPermissionName.ReviewRead)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "ExplainDiagnostics",
                Description = "Explain diagnostic codes with human/LLM-readable descriptions and remediation.",
                Category = AgentToolCategory.Review,
                Permissions = [Perm(AgentToolPermissionName.DiagnosticExplain)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },

            // ── Fix Proposal ──
            new()
            {
                Name = "SuggestDescriptorDraftFixes",
                Description = "Suggest fix proposals for a descriptor draft based on diagnostics. Creates proposals only.",
                Category = AgentToolCategory.FixProposal,
                Permissions = [Perm(AgentToolPermissionName.FixSuggest)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "GetFixProposal",
                Description = "Retrieve a stored fix proposal.",
                Category = AgentToolCategory.FixProposal,
                Permissions = [Perm(AgentToolPermissionName.FixSuggest)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "ListFixProposals",
                Description = "List stored fix proposals for a draft.",
                Category = AgentToolCategory.FixProposal,
                Permissions = [Perm(AgentToolPermissionName.FixSuggest)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "ApplyFixProposalToDraft",
                Description = "Apply a fix proposal to a descriptor draft. Updates draft/revision only, never active descriptors.",
                Category = AgentToolCategory.FixProposal,
                Permissions = [Perm(AgentToolPermissionName.FixApplyToDraft)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },

            // ── Package Preview ──
            new()
            {
                Name = "PreviewDescriptorPackage",
                Description = "Preview a descriptor package for a draft. Read/evidence generation only.",
                Category = AgentToolCategory.PackagePreview,
                Permissions = [Perm(AgentToolPermissionName.PackagePreview)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "BuildPackageEvidencePreview",
                Description = "Build a package evidence preview for a draft. Evidence only, not activation.",
                Category = AgentToolCategory.PackagePreview,
                Permissions = [Perm(AgentToolPermissionName.PackagePreview)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "BuildActivationReadinessPreview",
                Description = "Build an activation readiness preview. Reports blockers but does not submit activation request.",
                Category = AgentToolCategory.PackagePreview,
                Permissions = [Perm(AgentToolPermissionName.PackagePreview)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "GetPackagePreview",
                Description = "Retrieve a stored package preview.",
                Category = AgentToolCategory.PackagePreview,
                Permissions = [Perm(AgentToolPermissionName.PackagePreview)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },

            // ── Activation Handoff ──
            new()
            {
                Name = "SubmitActivationRequest",
                Description = "Submit an activation request handoff record. Does not approve or execute activation.",
                Category = AgentToolCategory.ActivationHandoff,
                Permissions = [Perm(AgentToolPermissionName.ActivationRequestSubmit)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "GetActivationRequestStatus",
                Description = "Get the status of an activation request. Read-only.",
                Category = AgentToolCategory.ActivationHandoff,
                Permissions = [Perm(AgentToolPermissionName.ActivationRequestRead)],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "CancelActivationRequest",
                Description = "Cancel an activation request. Cancels handoff only, does not affect runtime registry.",
                Category = AgentToolCategory.ActivationHandoff,
                Permissions = [Perm(AgentToolPermissionName.ActivationRequestCancel)],
                AllowedActors = allActors, IsReadOnly = false, MutatesRuntimeRegistry = false
            },

            // ── Manifest ──
            new()
            {
                Name = "ListAgentTools",
                Description = "List all available Agent Control Plane tools. No permission required.",
                Category = AgentToolCategory.Manifest,
                Permissions = [],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            },
            new()
            {
                Name = "GetAgentToolDescriptor",
                Description = "Get the descriptor for a specific Agent Control Plane tool. No permission required.",
                Category = AgentToolCategory.Manifest,
                Permissions = [],
                AllowedActors = allActors, IsReadOnly = true, MutatesRuntimeRegistry = false
            }
        };

        return tools.AsReadOnly();
    }

    private static AgentToolPermissionRequirement Perm(string name, string? description = null)
        => new() { PermissionName = name, Description = description };
}
