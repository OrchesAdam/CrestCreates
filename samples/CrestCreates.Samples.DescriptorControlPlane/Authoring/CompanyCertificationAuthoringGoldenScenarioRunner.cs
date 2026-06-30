using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.ContextPack.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Samples.DescriptorControlPlane.Authoring;

/// <summary>
/// Orchestrates the Phase 7f golden scenario:
/// Intent → Authoring → Draft Store → Sequential Review → Final Proposed Inventory.
///
/// All-or-block: if any single draft fails validation, materialization, or review,
/// the entire draft set is blocked and no final inventory is produced.
/// </summary>
public sealed class CompanyCertificationAuthoringGoldenScenarioRunner
{
    private readonly IServiceProvider _serviceProvider;

    private static readonly DateTimeOffset GoldenScenarioCreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public CompanyCertificationAuthoringGoldenScenarioRunner(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Runs the authoring pipeline up to and including draft set review.
    /// Does NOT create activation requests or execute runtime proof.
    /// </summary>
    public async Task<CompanyCertificationDraftSetReviewResult> RunUntilDraftSetReviewAsync(
        string intentText,
        CancellationToken ct = default)
    {
        return await RunUntilDraftSetReviewAsync(intentText, CompanyCertificationDescriptorCloner.CopyAllDescriptors(), ct);
    }

    /// <summary>
    /// Runs the authoring pipeline up to and including draft set review,
    /// using the provided <paramref name="startingInventory"/> instead of the
    /// full Company Certification catalog clone.
    /// </summary>
    public async Task<CompanyCertificationDraftSetReviewResult> RunUntilDraftSetReviewAsync(
        string intentText,
        IReadOnlyList<IDescriptor> startingInventory,
        CancellationToken ct = default)
    {
        var authoringAgent = _serviceProvider.GetRequiredService<IDescriptorAuthoringAgent>();
        var draftStore = _serviceProvider.GetRequiredService<IDescriptorDraftStore>();
        var reviewService = _serviceProvider.GetRequiredService<IDescriptorDraftReviewService>();
        var topologyBuilder = _serviceProvider.GetRequiredService<IDescriptorTopologyBuilder>();
        var governanceService = _serviceProvider.GetRequiredService<IDescriptorLifecycleGovernanceService>();

        var tenantId = "tenant-company-certification";

        // ── Step 1: Build AgentAuthoringContext ──
        var context = await BuildAuthoringContext(tenantId, intentText, startingInventory, ct);

        // ── Step 2: Author drafts ──
        var authoringResult = await authoringAgent.AuthorAsync(context, ct);
        var draftSet = authoringResult.DraftSet;

        // ── Step 3: Save all drafts to store ──
        foreach (var draft in draftSet.Drafts)
        {
            await draftStore.SaveAsync(draft, ct);
        }

        // ── Step 4: Sequential review (all-or-block) ──
        var currentInventory = startingInventory.ToList();
        var perDraftResults = new List<DescriptorDraftReviewResult>();

        foreach (var draft in draftSet.Drafts)
        {
            var reviewResult = await reviewService.ReviewAsync(draft, currentInventory, ct);
            perDraftResults.Add(reviewResult);

            // Register review result in reference registry at creation point
            var referenceRegistry = _serviceProvider.GetRequiredService<ActivationBindingReferenceRegistry>();
            referenceRegistry.RegisterReviewResult(
                tenantId,
                $"review-result-{draft.DraftId}",
                draft.DraftId);

            // All-or-block: any failure blocks the entire set
            if (!reviewResult.ValidationResult.IsValid ||
                reviewResult.MaterializationResult is null ||
                !reviewResult.MaterializationResult.IsMaterialized)
            {
                var blockReasons = new List<string>();
                if (!reviewResult.ValidationResult.IsValid)
                    blockReasons.Add($"Draft {draft.DraftId} validation failed");
                if (reviewResult.MaterializationResult is null || !reviewResult.MaterializationResult.IsMaterialized)
                    blockReasons.Add($"Draft {draft.DraftId} materialization failed");

                return new CompanyCertificationDraftSetReviewResult
                {
                    DraftSet = draftSet,
                    PerDraftReviewResults = perDraftResults,
                    FinalProposedInventory = Array.Empty<IDescriptor>(),
                    IsBlocked = true,
                    FinalDecisionSource = "AllOrBlock",
                    BlockReason = string.Join("; ", blockReasons),
                };
            }

            // Update running inventory with materialized result
            if (reviewResult.MaterializationResult?.ProposedInventory is not null)
            {
                currentInventory = reviewResult.MaterializationResult.ProposedInventory.ToList();
            }
        }

        // ── Step 5: Build final topology and governance from complete inventory ──
        var finalInventory = currentInventory.ToList().AsReadOnly();

        DescriptorTopologySnapshot? finalTopology = null;
        try
        {
            finalTopology = topologyBuilder.Build(finalInventory);
        }
        catch (Exception ex)
        {
            return new CompanyCertificationDraftSetReviewResult
            {
                DraftSet = draftSet,
                PerDraftReviewResults = perDraftResults,
                FinalProposedInventory = Array.Empty<IDescriptor>(),
                IsBlocked = true,
                FinalDecisionSource = "AllOrBlock",
                BlockReason = $"Final topology build failed: {ex.Message}",
                FinalImpact = null,
                FinalCompat = null,
            };
        }

        // Check topology diagnostics for blocking findings
        if (finalTopology.Diagnostics.All.Any(d => d.Severity == SeverityLevel.Blocker || d.Severity == SeverityLevel.Error))
        {
            var blockingDiags = finalTopology.Diagnostics.All
                .Where(d => d.Severity == SeverityLevel.Blocker || d.Severity == SeverityLevel.Error)
                .Select(d => d.Message);
            return new CompanyCertificationDraftSetReviewResult
            {
                DraftSet = draftSet,
                PerDraftReviewResults = perDraftResults,
                FinalProposedInventory = Array.Empty<IDescriptor>(),
                IsBlocked = true,
                FinalDecisionSource = "AllOrBlock",
                BlockReason = $"Final topology has blocking findings: {string.Join("; ", blockingDiags)}",
                FinalTopology = finalTopology,
                FinalImpact = null,
                FinalCompat = null,
            };
        }

        // ── Compute final impact/compat from complete inventory diff ──
        var changeSetBuilder = _serviceProvider.GetRequiredService<IDescriptorChangeSetBuilder>();
        var impactAnalyzer = _serviceProvider.GetRequiredService<IDescriptorImpactAnalyzer>();
        var compatAnalyzer = _serviceProvider.GetRequiredService<IDescriptorCompatibilityAnalyzer>();

        var finalChangeSet = changeSetBuilder.Build(startingInventory, finalInventory);
        var finalImpact = impactAnalyzer.Analyze(finalTopology!, finalChangeSet);
        var finalCompat = compatAnalyzer.Analyze(startingInventory, finalInventory, finalChangeSet, finalImpact);

        DescriptorLifecycleGovernanceReport? finalGovernance = null;
        try
        {
            var governanceRequest = new DescriptorLifecycleGovernanceRequest
            {
                Transitions = BuildActivateTransitions(finalInventory),
                ValidationReport = ValidationReport.Empty,
                BindingReport = new RuntimeBindingReport(),
                TopologyDiagnostics = finalTopology?.Diagnostics
                    ?? new DescriptorTopologyDiagnostics
                    {
                        All = Array.Empty<DescriptorTopologyDiagnostic>()
                    },
                ImpactReport = finalImpact,
                CompatibilityReport = finalCompat,
            };
            finalGovernance = governanceService.Evaluate(governanceRequest);
        }
        catch (Exception ex)
        {
            return new CompanyCertificationDraftSetReviewResult
            {
                DraftSet = draftSet,
                PerDraftReviewResults = perDraftResults,
                FinalProposedInventory = Array.Empty<IDescriptor>(),
                IsBlocked = true,
                FinalDecisionSource = "AllOrBlock",
                BlockReason = $"Final governance evaluation failed: {ex.Message}",
                FinalTopology = finalTopology,
                FinalImpact = finalImpact,
                FinalCompat = finalCompat,
            };
        }

        // Check governance decision for blocking
        if (finalGovernance.MaxDecision == DescriptorLifecycleDecisionKind.Blocked)
        {
            return new CompanyCertificationDraftSetReviewResult
            {
                DraftSet = draftSet,
                PerDraftReviewResults = perDraftResults,
                FinalProposedInventory = Array.Empty<IDescriptor>(),
                IsBlocked = true,
                FinalDecisionSource = "AllOrBlock",
                BlockReason = "Final governance decision is Blocked",
                FinalTopology = finalTopology,
                FinalGovernance = finalGovernance,
                FinalImpact = finalImpact,
                FinalCompat = finalCompat,
            };
        }

        return new CompanyCertificationDraftSetReviewResult
        {
            DraftSet = draftSet,
            PerDraftReviewResults = perDraftResults,
            FinalProposedInventory = finalInventory,
            IsBlocked = false,
            FinalDecisionSource = "FinalProposedInventory",
            FinalTopology = finalTopology,
            FinalGovernance = finalGovernance,
            FinalImpact = finalImpact,
            FinalCompat = finalCompat,
        };
    }

    private async Task<AgentAuthoringContext> BuildAuthoringContext(
        string tenantId,
        string intentText,
        IReadOnlyList<IDescriptor> startingInventory,
        CancellationToken ct)
    {
        var contextPackBuilder = _serviceProvider.GetRequiredService<IMetadataContextPackBuilder>();
        var topologyBuilder = _serviceProvider.GetRequiredService<IDescriptorTopologyBuilder>();

        var topology = topologyBuilder.Build(startingInventory);

        var metadataRequest = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            TenantId = tenantId,
            Intent = intentText,
            FocusDescriptors = new[]
            {
                new DescriptorRef("workflow", "wf_company_certification", 1)
            }
        };

        var metadataContextPack = contextPackBuilder.Build(metadataRequest, topology, startingInventory);

        var memoryRetriever = _serviceProvider.GetRequiredService<IAgentMemoryRetriever>();
        var memoryQuery = new AgentMemoryQuery
        {
            TenantId = tenantId,
            IntentText = intentText,
            DescriptorRefs = new[]
            {
                new DescriptorRef("workflow", "wf_company_certification", 1)
            }
        };
        var memoryPack = await memoryRetriever.RecallAsync(memoryQuery, ct);

        var authoringContextBuilder = _serviceProvider.GetRequiredService<IAgentAuthoringContextBuilder>();
        var authoringRequest = new AgentAuthoringRequest
        {
            TenantId = tenantId,
            IntentText = intentText,
        };

        return await authoringContextBuilder.BuildAsync(authoringRequest, metadataContextPack, memoryPack, ct);
    }

    /// <summary>
    /// Full pipeline: authoring → review → activation → fresh host runtime proof.
    /// </summary>
    public async Task<CompanyCertificationAuthoringGoldenScenarioReport> RunAsync(
        string intentText,
        CancellationToken ct = default)
    {
        // ── Authoring + Review ──
        var reviewReport = await RunUntilDraftSetReviewAsync(intentText, ct);

        if (reviewReport.IsBlocked)
        {
            return new CompanyCertificationAuthoringGoldenScenarioReport
            {
                AuthoringSucceeded = false,
                AuthoringError = reviewReport.BlockReason,
                DraftSetBlocked = true,
                BlockReason = reviewReport.BlockReason,
                FinalDecisionSource = reviewReport.FinalDecisionSource,
                FinalProposedInventory = reviewReport.FinalProposedInventory,
            };
        }

        // ── Find the workflow update draft review result ──
        var workflowReviewResult = reviewReport.PerDraftReviewResults
            .Single(r => r.DraftId == "draft_company_certification_workflow_finance_review");

        // ── Build BindingHashes ──
        var reviewHashService = _serviceProvider.GetRequiredService<IDescriptorDraftReviewHashService>();
        var sourceReviewHash = reviewHashService.ComputeSourceReviewHash(workflowReviewResult);
        var reviewManifestHash = reviewHashService.ComputeReviewManifestHash(workflowReviewResult);

        // Compute package hashes from the final proposed inventory
        var packageBuilder = _serviceProvider.GetRequiredService<IDescriptorPackageBuilder>();
        var stableHashBuilder = _serviceProvider.GetRequiredService<IDescriptorStableHashBuilder>();

        var buildRequest = new DescriptorPackageBuildRequest
        {
            PackageId = "pkg-phase7f-golden-scenario",
            PackageVersion = "1.0.0",
            CreatedBy = "phase7f-golden-scenario-runner",
            Source = "golden-scenario",
            CreatedAt = GoldenScenarioCreatedAt,
            Descriptors = reviewReport.FinalProposedInventory,
            TopologySnapshot = reviewReport.FinalTopology,
            ImpactReport = reviewReport.FinalImpact!,
            CompatibilityReport = reviewReport.FinalCompat!,
            GovernanceReport = reviewReport.FinalGovernance,
        };
        var descriptorPackage = packageBuilder.Build(buildRequest);
        var packageManifestHash = descriptorPackage.Hashes!.PackageManifestHash;
        var packageEvidenceHash = descriptorPackage.Hashes.PackageEvidenceHash;
        var packageEvidenceEnvelopeHash = descriptorPackage.Hashes.PackageEvidenceEnvelopeHash;

        // Compute stable hashes for the workflow descriptor from the final inventory
        var materializedDescriptor = reviewReport.FinalProposedInventory
            .FirstOrDefault(d => d.Kind == DescriptorKind.Workflow && d.Id == "wf_company_certification");
        if (materializedDescriptor is null)
        {
            return new CompanyCertificationAuthoringGoldenScenarioReport
            {
                AuthoringSucceeded = true,
                DraftSetBlocked = false,
                FinalDecisionSource = reviewReport.FinalDecisionSource,
                FinalProposedInventory = reviewReport.FinalProposedInventory,
                RuntimeActivationGateSucceeded = false,
                ErrorMessage = "Cannot compute contract/definition hashes: workflow descriptor not found in final inventory.",
            };
        }

        var stableHashes = stableHashBuilder.Build(materializedDescriptor);
        var contractHash = stableHashes.ContractHash;
        var definitionHash = stableHashes.DefinitionHash;

        var bindingHashes = new BindingHashes
        {
            SourceReviewHash = sourceReviewHash,
            ReviewManifestHash = reviewManifestHash,
            PackageManifestHash = packageManifestHash,
            PackageEvidenceHash = packageEvidenceHash,
            PackageEvidenceEnvelopeHash = packageEvidenceEnvelopeHash,
            ContractHash = contractHash,
            DefinitionHash = definitionHash,
        };

        // ── Build ActivationBindingSnapshot ──
        var tenantId = "tenant-company-certification";
        var reviewResultId = "review-result-draft_company_certification_workflow_finance_review";
        var packagePreviewId = "package-preview-draft_company_certification_workflow_finance_review";
        var evidencePreviewId = "evidence-preview-draft_company_certification_workflow_finance_review";
        var draftVersion = 1;

        var bindingSnapshot = new ActivationBindingSnapshot
        {
            TenantId = tenantId,
            DraftId = "draft_company_certification_workflow_finance_review",
            DraftVersion = draftVersion,
            ReviewResultId = reviewResultId,
            PackagePreviewId = packagePreviewId,
            EvidencePreviewId = evidencePreviewId,
            Hashes = bindingHashes,
            CorrelationId = "phase7f-golden-scenario",
            CreatedAt = GoldenScenarioCreatedAt,
        };

        // ── Store hashes in artifact resolver ──
        var artifactResolver = _serviceProvider.GetRequiredService<IActivationBindingArtifactResolver>();
        artifactResolver.StorePackageHashes(tenantId, packagePreviewId, new DescriptorPackageHashSet
        {
            PackageManifestHash = packageManifestHash,
            PackageEvidenceHash = packageEvidenceHash,
            PackageEvidenceEnvelopeHash = packageEvidenceEnvelopeHash,
        });
        artifactResolver.StoreEvidenceHashes(tenantId, evidencePreviewId, new DescriptorPackageHashSet
        {
            PackageManifestHash = packageManifestHash,
            PackageEvidenceHash = packageEvidenceHash,
            PackageEvidenceEnvelopeHash = packageEvidenceEnvelopeHash,
        });
        artifactResolver.StoreReviewHashes(tenantId, reviewResultId, sourceReviewHash, reviewManifestHash);

        // ── Register binding references at artifact creation point ──
        var referenceRegistryForArtifacts = _serviceProvider.GetRequiredService<ActivationBindingReferenceRegistry>();
        referenceRegistryForArtifacts.RegisterPackagePreview(tenantId, packagePreviewId, bindingSnapshot.DraftId);
        referenceRegistryForArtifacts.RegisterEvidencePreview(tenantId, evidencePreviewId, bindingSnapshot.DraftId);

        // ── Validate binding references with DraftId integrity ──
        var draftStoreForValidation = _serviceProvider.GetRequiredService<IDescriptorDraftStore>();
        var draftExists = await draftStoreForValidation.GetAsync(tenantId, bindingSnapshot.DraftId, ct);
        if (draftExists is null)
        {
            return new CompanyCertificationAuthoringGoldenScenarioReport
            {
                AuthoringSucceeded = true,
                DraftSetBlocked = false,
                FinalDecisionSource = reviewReport.FinalDecisionSource,
                FinalProposedInventory = reviewReport.FinalProposedInventory,
                RuntimeActivationGateSucceeded = false,
                ErrorMessage = "Binding reference validation failed: draft not found in store.",
            };
        }

        var referenceRegistry = _serviceProvider.GetRequiredService<ActivationBindingReferenceRegistry>();
        var refValidation = referenceRegistry.ValidateReferences(
            tenantId, bindingSnapshot.DraftId,
            reviewResultId, packagePreviewId, evidencePreviewId);
        if (!refValidation.IsValid)
        {
            return new CompanyCertificationAuthoringGoldenScenarioReport
            {
                AuthoringSucceeded = true,
                DraftSetBlocked = false,
                FinalDecisionSource = reviewReport.FinalDecisionSource,
                FinalProposedInventory = reviewReport.FinalProposedInventory,
                RuntimeActivationGateSucceeded = false,
                ErrorMessage = $"Binding reference validation failed: {string.Join("; ", refValidation.Errors)}",
            };
        }

        // ── Create activation request ──
        var activationService = _serviceProvider.GetRequiredService<IDescriptorActivationRequestService>();

        var invocationContext = new AgentToolInvocationContext
        {
            TenantId = tenantId,
            ActorId = "phase7f-golden-scenario-runner",
            ActorKind = AgentToolActorKind.System,
            CorrelationId = "phase7f-golden-scenario",
            ToolName = "SubmitActivationRequest",
            InvocationSource = AgentToolInvocationSource.Internal,
        };

        var submitRequest = new SubmitActivationRequestRequest
        {
            DraftId = "draft_company_certification_workflow_finance_review",
            BindingSnapshot = bindingSnapshot,
            GovernanceDecision = DescriptorLifecycleDecisionKind.Allowed,
            Rationale = "Phase7f golden scenario — deterministic activation",
        };

        var activationResult = await activationService.CreateActivationRequestAsync(
            invocationContext, submitRequest, ct);

        if (activationResult.Status != AgentToolResultStatus.Success || activationResult.Value is null)
        {
            return new CompanyCertificationAuthoringGoldenScenarioReport
            {
                AuthoringSucceeded = true,
                DraftSetBlocked = false,
                FinalDecisionSource = reviewReport.FinalDecisionSource,
                FinalProposedInventory = reviewReport.FinalProposedInventory,
                RuntimeActivationGateSucceeded = false,
                ErrorMessage = $"Activation request failed: {string.Join(", ", activationResult.Diagnostics.Select(d => d.Message))}",
            };
        }

        var activationRequest = activationResult.Value;

        // Handle manual review if needed
        if (activationRequest.Status == ActivationRequestStatus.UnderReview)
        {
            var approvalDecision = new DescriptorActivationReviewDecision
            {
                ActivationRequestId = activationRequest.RequestId,
                TenantId = tenantId,
                CorrelationId = "phase7f-golden-scenario",
                Decision = DescriptorActivationReviewOutcome.Approved,
                ActorKind = DescriptorActivationActorKind.Human,
                ActorId = "phase7f-human-approver",
                Reason = "Phase7f golden scenario approval",
                DecidedAt = GoldenScenarioCreatedAt,
                BoundEvidenceHash = packageEvidenceHash,
                BoundEnvelopeHash = packageEvidenceEnvelopeHash,
            };

            var approvalResult = await activationService.ApproveActivationRequestAsync(
                invocationContext, activationRequest.RequestId, approvalDecision, ct);

            if (approvalResult.Status != AgentToolResultStatus.Success || approvalResult.Value is null)
            {
                return new CompanyCertificationAuthoringGoldenScenarioReport
                {
                    AuthoringSucceeded = true,
                    DraftSetBlocked = false,
                    FinalDecisionSource = reviewReport.FinalDecisionSource,
                    FinalProposedInventory = reviewReport.FinalProposedInventory,
                    ActivationRequestId = activationRequest.RequestId,
                    ActivationSubjectDraftId = "draft_company_certification_workflow_finance_review",
                    BoundPackageEvidenceHash = packageEvidenceHash.Value,
                    BoundPackageEvidenceEnvelopeHash = packageEvidenceEnvelopeHash.Value,
                    RuntimeActivationGateSucceeded = false,
                    ErrorMessage = $"Activation approval failed: {string.Join(", ", approvalResult.Diagnostics.Select(d => d.Message))}",
                };
            }
            activationRequest = approvalResult.Value;
        }

        bool gateSucceeded = activationRequest.Status == ActivationRequestStatus.Activated;

        if (!gateSucceeded)
        {
            return new CompanyCertificationAuthoringGoldenScenarioReport
            {
                AuthoringSucceeded = true,
                DraftSetBlocked = false,
                FinalDecisionSource = reviewReport.FinalDecisionSource,
                FinalProposedInventory = reviewReport.FinalProposedInventory,
                ActivationRequestId = activationRequest.RequestId,
                ActivationSubjectDraftId = "draft_company_certification_workflow_finance_review",
                BoundPackageEvidenceHash = packageEvidenceHash.Value,
                BoundPackageEvidenceEnvelopeHash = packageEvidenceEnvelopeHash.Value,
                RuntimeActivationGateSucceeded = false,
                ErrorMessage = $"Activation gate did not succeed. Status: {activationRequest.Status}",
            };
        }

        // ── Compute activated inventory hash ──
        var descriptorHashes = reviewReport.FinalProposedInventory
            .Select(d => stableHashBuilder.Build(d))
            .OrderBy(h => h.DefinitionHash.Value, StringComparer.Ordinal)
            .Select(h => h.DefinitionHash.Value);
        var activatedInventoryHash = string.Join("|", descriptorHashes);

        // ── Fresh host runtime proof ──
        using var activatedHost = new CompanyCertificationGoldenScenarioHost(
            reviewReport.FinalProposedInventory,
            new InMemoryCompanyCertificationStore());
        var runtimeRunner = new CompanyCertificationGoldenScenarioRunner(activatedHost);
        var runtimeScenario = CompanyCertificationChangeScenario.FromInventory(
            "Phase7f activated inventory", reviewReport.FinalProposedInventory);
        var runtimeReport = await runtimeRunner.RunAsync(runtimeScenario, allowReviewRequired: true);

        return new CompanyCertificationAuthoringGoldenScenarioReport
        {
            AuthoringSucceeded = true,
            DraftSetBlocked = false,
            FinalDecisionSource = reviewReport.FinalDecisionSource,
            FinalProposedInventory = reviewReport.FinalProposedInventory,
            ActivationRequestId = activationRequest.RequestId,
            ActivationSubjectDraftId = "draft_company_certification_workflow_finance_review",
            BoundPackageEvidenceHash = packageEvidenceHash.Value,
            BoundPackageEvidenceEnvelopeHash = packageEvidenceEnvelopeHash.Value,
            RuntimeActivationGateSucceeded = true,
            RuntimeProofUsedFreshActivatedHost = true,
            ActivatedWorkflowDescriptorId = runtimeReport.ActivatedWorkflowDescriptorId,
            ActivatedWorkflowVersion = runtimeReport.ActivatedWorkflowVersion,
            ActivatedHumanTaskDescriptorIds = runtimeReport.ActivatedHumanTaskDescriptorIds,
            ObservedHumanTaskDescriptorIds = runtimeReport.ObservedHumanTaskDescriptorIds,
            WorkflowStepSequence = runtimeReport.WorkflowStepSequence,
            InitialReviewHumanTaskInstanceId = runtimeReport.InitialReviewHumanTaskInstanceId,
            FinanceReviewHumanTaskInstanceId = runtimeReport.FinanceReviewHumanTaskInstanceId,
            CompletedHumanTaskCount = runtimeReport.CompletedHumanTaskCount,
            ActivatedInventoryHash = activatedInventoryHash,
            ActivatedPackageEvidenceHash = packageEvidenceHash.Value,
            ApprovedEventCaptured = runtimeReport.ApprovedEventCaptured,
            RuntimeExecuted = runtimeReport.RuntimeExecuted,
            ErrorMessage = runtimeReport.ErrorMessage,
        };
    }

    /// <summary>
    /// Pipeline that stops after activation gate success.
    /// Does NOT build a fresh host or execute runtime proof.
    /// </summary>
    public async Task<CompanyCertificationAuthoringGoldenScenarioReport> RunActivationOnlyAsync(
        string intentText,
        CancellationToken ct = default)
    {
        // ── Authoring + Review ──
        var reviewReport = await RunUntilDraftSetReviewAsync(intentText, ct);

        if (reviewReport.IsBlocked)
        {
            return new CompanyCertificationAuthoringGoldenScenarioReport
            {
                AuthoringSucceeded = false,
                AuthoringError = reviewReport.BlockReason,
                DraftSetBlocked = true,
                BlockReason = reviewReport.BlockReason,
                FinalDecisionSource = reviewReport.FinalDecisionSource,
                FinalProposedInventory = reviewReport.FinalProposedInventory,
            };
        }

        // ── Find the workflow update draft review result ──
        var workflowReviewResult = reviewReport.PerDraftReviewResults
            .Single(r => r.DraftId == "draft_company_certification_workflow_finance_review");

        // ── Build BindingHashes ──
        var reviewHashService = _serviceProvider.GetRequiredService<IDescriptorDraftReviewHashService>();
        var sourceReviewHash = reviewHashService.ComputeSourceReviewHash(workflowReviewResult);
        var reviewManifestHash = reviewHashService.ComputeReviewManifestHash(workflowReviewResult);

        // Compute package hashes from the final proposed inventory
        var packageBuilder = _serviceProvider.GetRequiredService<IDescriptorPackageBuilder>();
        var stableHashBuilder = _serviceProvider.GetRequiredService<IDescriptorStableHashBuilder>();

        var buildRequest = new DescriptorPackageBuildRequest
        {
            PackageId = "pkg-phase7f-golden-scenario",
            PackageVersion = "1.0.0",
            CreatedBy = "phase7f-golden-scenario-runner",
            Source = "golden-scenario",
            CreatedAt = GoldenScenarioCreatedAt,
            Descriptors = reviewReport.FinalProposedInventory,
            TopologySnapshot = reviewReport.FinalTopology,
            ImpactReport = reviewReport.FinalImpact!,
            CompatibilityReport = reviewReport.FinalCompat!,
            GovernanceReport = reviewReport.FinalGovernance,
        };
        var descriptorPackage = packageBuilder.Build(buildRequest);
        var packageManifestHash = descriptorPackage.Hashes!.PackageManifestHash;
        var packageEvidenceHash = descriptorPackage.Hashes.PackageEvidenceHash;
        var packageEvidenceEnvelopeHash = descriptorPackage.Hashes.PackageEvidenceEnvelopeHash;

        // Compute stable hashes for the workflow descriptor from the final inventory
        var materializedDescriptor = reviewReport.FinalProposedInventory
            .FirstOrDefault(d => d.Kind == DescriptorKind.Workflow && d.Id == "wf_company_certification");
        if (materializedDescriptor is null)
        {
            return new CompanyCertificationAuthoringGoldenScenarioReport
            {
                AuthoringSucceeded = true,
                DraftSetBlocked = false,
                FinalDecisionSource = reviewReport.FinalDecisionSource,
                FinalProposedInventory = reviewReport.FinalProposedInventory,
                RuntimeActivationGateSucceeded = false,
                ErrorMessage = "Cannot compute contract/definition hashes: workflow descriptor not found in final inventory.",
            };
        }

        var stableHashes = stableHashBuilder.Build(materializedDescriptor);
        var contractHash = stableHashes.ContractHash;
        var definitionHash = stableHashes.DefinitionHash;

        var bindingHashes = new BindingHashes
        {
            SourceReviewHash = sourceReviewHash,
            ReviewManifestHash = reviewManifestHash,
            PackageManifestHash = packageManifestHash,
            PackageEvidenceHash = packageEvidenceHash,
            PackageEvidenceEnvelopeHash = packageEvidenceEnvelopeHash,
            ContractHash = contractHash,
            DefinitionHash = definitionHash,
        };

        // ── Build ActivationBindingSnapshot ──
        var tenantId = "tenant-company-certification";
        var reviewResultId = "review-result-draft_company_certification_workflow_finance_review";
        var packagePreviewId = "package-preview-draft_company_certification_workflow_finance_review";
        var evidencePreviewId = "evidence-preview-draft_company_certification_workflow_finance_review";

        var bindingSnapshot = new ActivationBindingSnapshot
        {
            TenantId = tenantId,
            DraftId = "draft_company_certification_workflow_finance_review",
            DraftVersion = 1,
            ReviewResultId = reviewResultId,
            PackagePreviewId = packagePreviewId,
            EvidencePreviewId = evidencePreviewId,
            Hashes = bindingHashes,
            CorrelationId = "phase7f-golden-scenario",
            CreatedAt = GoldenScenarioCreatedAt,
        };

        // ── Store hashes in artifact resolver ──
        var artifactResolver = _serviceProvider.GetRequiredService<IActivationBindingArtifactResolver>();
        artifactResolver.StorePackageHashes(tenantId, packagePreviewId, new DescriptorPackageHashSet
        {
            PackageManifestHash = packageManifestHash,
            PackageEvidenceHash = packageEvidenceHash,
            PackageEvidenceEnvelopeHash = packageEvidenceEnvelopeHash,
        });
        artifactResolver.StoreEvidenceHashes(tenantId, evidencePreviewId, new DescriptorPackageHashSet
        {
            PackageManifestHash = packageManifestHash,
            PackageEvidenceHash = packageEvidenceHash,
            PackageEvidenceEnvelopeHash = packageEvidenceEnvelopeHash,
        });
        artifactResolver.StoreReviewHashes(tenantId, reviewResultId, sourceReviewHash, reviewManifestHash);

        // ── Register binding references at artifact creation point ──
        var referenceRegistryForArtifacts = _serviceProvider.GetRequiredService<ActivationBindingReferenceRegistry>();
        referenceRegistryForArtifacts.RegisterPackagePreview(tenantId, packagePreviewId, bindingSnapshot.DraftId);
        referenceRegistryForArtifacts.RegisterEvidencePreview(tenantId, evidencePreviewId, bindingSnapshot.DraftId);

        // ── Validate binding references with DraftId integrity ──
        var draftStoreForValidation = _serviceProvider.GetRequiredService<IDescriptorDraftStore>();
        var draftExists = await draftStoreForValidation.GetAsync(tenantId, bindingSnapshot.DraftId, ct);
        if (draftExists is null)
        {
            return new CompanyCertificationAuthoringGoldenScenarioReport
            {
                AuthoringSucceeded = true,
                DraftSetBlocked = false,
                FinalDecisionSource = reviewReport.FinalDecisionSource,
                FinalProposedInventory = reviewReport.FinalProposedInventory,
                RuntimeActivationGateSucceeded = false,
                ErrorMessage = "Binding reference validation failed: draft not found in store.",
            };
        }

        var referenceRegistry = _serviceProvider.GetRequiredService<ActivationBindingReferenceRegistry>();
        var refValidation = referenceRegistry.ValidateReferences(
            tenantId, bindingSnapshot.DraftId,
            reviewResultId, packagePreviewId, evidencePreviewId);
        if (!refValidation.IsValid)
        {
            return new CompanyCertificationAuthoringGoldenScenarioReport
            {
                AuthoringSucceeded = true,
                DraftSetBlocked = false,
                FinalDecisionSource = reviewReport.FinalDecisionSource,
                FinalProposedInventory = reviewReport.FinalProposedInventory,
                RuntimeActivationGateSucceeded = false,
                ErrorMessage = $"Binding reference validation failed: {string.Join("; ", refValidation.Errors)}",
            };
        }

        // ── Create activation request ──
        var activationService = _serviceProvider.GetRequiredService<IDescriptorActivationRequestService>();

        var invocationContext = new AgentToolInvocationContext
        {
            TenantId = tenantId,
            ActorId = "phase7f-golden-scenario-runner",
            ActorKind = AgentToolActorKind.System,
            CorrelationId = "phase7f-golden-scenario",
            ToolName = "SubmitActivationRequest",
            InvocationSource = AgentToolInvocationSource.Internal,
        };

        var submitRequest = new SubmitActivationRequestRequest
        {
            DraftId = "draft_company_certification_workflow_finance_review",
            BindingSnapshot = bindingSnapshot,
            GovernanceDecision = DescriptorLifecycleDecisionKind.Allowed,
            Rationale = "Phase7f golden scenario — deterministic activation",
        };

        var activationResult = await activationService.CreateActivationRequestAsync(
            invocationContext, submitRequest, ct);

        if (activationResult.Status != AgentToolResultStatus.Success || activationResult.Value is null)
        {
            return new CompanyCertificationAuthoringGoldenScenarioReport
            {
                AuthoringSucceeded = true,
                DraftSetBlocked = false,
                FinalDecisionSource = reviewReport.FinalDecisionSource,
                FinalProposedInventory = reviewReport.FinalProposedInventory,
                RuntimeActivationGateSucceeded = false,
                ErrorMessage = $"Activation request failed: {string.Join(", ", activationResult.Diagnostics.Select(d => d.Message))}",
            };
        }

        var activationRequest = activationResult.Value;

        // Handle manual review if needed
        if (activationRequest.Status == ActivationRequestStatus.UnderReview)
        {
            var approvalDecision = new DescriptorActivationReviewDecision
            {
                ActivationRequestId = activationRequest.RequestId,
                TenantId = tenantId,
                CorrelationId = "phase7f-golden-scenario",
                Decision = DescriptorActivationReviewOutcome.Approved,
                ActorKind = DescriptorActivationActorKind.Human,
                ActorId = "phase7f-human-approver",
                Reason = "Phase7f golden scenario approval",
                DecidedAt = GoldenScenarioCreatedAt,
                BoundEvidenceHash = packageEvidenceHash,
                BoundEnvelopeHash = packageEvidenceEnvelopeHash,
            };

            var approvalResult = await activationService.ApproveActivationRequestAsync(
                invocationContext, activationRequest.RequestId, approvalDecision, ct);

            if (approvalResult.Status != AgentToolResultStatus.Success || approvalResult.Value is null)
            {
                return new CompanyCertificationAuthoringGoldenScenarioReport
                {
                    AuthoringSucceeded = true,
                    DraftSetBlocked = false,
                    FinalDecisionSource = reviewReport.FinalDecisionSource,
                    FinalProposedInventory = reviewReport.FinalProposedInventory,
                    ActivationRequestId = activationRequest.RequestId,
                    ActivationSubjectDraftId = "draft_company_certification_workflow_finance_review",
                    BoundPackageEvidenceHash = packageEvidenceHash.Value,
                    BoundPackageEvidenceEnvelopeHash = packageEvidenceEnvelopeHash.Value,
                    RuntimeActivationGateSucceeded = false,
                    ErrorMessage = $"Activation approval failed: {string.Join(", ", approvalResult.Diagnostics.Select(d => d.Message))}",
                };
            }
            activationRequest = approvalResult.Value;
        }

        bool gateSucceeded = activationRequest.Status == ActivationRequestStatus.Activated;

        return new CompanyCertificationAuthoringGoldenScenarioReport
        {
            AuthoringSucceeded = true,
            DraftSetBlocked = false,
            FinalDecisionSource = reviewReport.FinalDecisionSource,
            FinalProposedInventory = reviewReport.FinalProposedInventory,
            ActivationRequestId = activationRequest.RequestId,
            ActivationSubjectDraftId = "draft_company_certification_workflow_finance_review",
            BoundPackageEvidenceHash = packageEvidenceHash.Value,
            BoundPackageEvidenceEnvelopeHash = packageEvidenceEnvelopeHash.Value,
            RuntimeActivationGateSucceeded = gateSucceeded,
            RuntimeProofUsedFreshActivatedHost = false,
            ErrorMessage = gateSucceeded ? null : $"Activation gate did not succeed. Status: {activationRequest.Status}",
        };
    }

    private static IReadOnlyList<DescriptorLifecycleTransition> BuildActivateTransitions(
        IReadOnlyList<IDescriptor> inventory)
    {
        return inventory
            .Select(d => new DescriptorLifecycleTransition
            {
                Subject = new DescriptorRef(
                    d.Namespace,
                    d.Id,
                    (d as IVersionedDescriptor)?.Version),
                Operation = DescriptorLifecycleOperation.Activate,
                Reason = "Phase7f draft set review — activate all in final inventory",
            })
            .ToList()
            .AsReadOnly();
    }
}
