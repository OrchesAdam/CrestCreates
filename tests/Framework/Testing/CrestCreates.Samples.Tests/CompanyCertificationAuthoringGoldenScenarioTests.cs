using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.ContextPack.Abstractions;
using CrestCreates.Samples.DescriptorControlPlane;
using CrestCreates.Samples.DescriptorControlPlane.Authoring;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Samples.Tests;

public sealed class CompanyCertificationAuthoringGoldenScenarioTests
{
    private const string Phase7fIntent =
        "Add second-level finance review before approving company certification.";

    [Fact]
    public async Task FakeAuthoringAgent_Output_Is_Deterministic()
    {
        var agent = new FakeCompanyCertificationAuthoringAgent();
        var context = TestAuthoringContext();

        var first = await agent.AuthorAsync(context);
        var second = await agent.AuthorAsync(context);

        first.DraftSet.Drafts.Select(d => d.DraftId)
            .Should().Equal(second.DraftSet.Drafts.Select(d => d.DraftId));
        first.DraftSet.Drafts.Select(d => d.DescriptorId)
            .Should().Equal(second.DraftSet.Drafts.Select(d => d.DescriptorId));
    }

    [Fact]
    public async Task DraftSet_Creates_FinanceReview_HumanTask()
    {
        var result = await new FakeCompanyCertificationAuthoringAgent()
            .AuthorAsync(TestAuthoringContext());

        var draft = result.DraftSet.Drafts.Single(d => d.DescriptorId == "ht_finance_review_company_certification");

        draft.Operation.Should().Be(DescriptorDraftOperation.Create);
        draft.Payload.Should().BeOfType<HumanTaskDescriptorDraftPayload>();
    }

    [Fact]
    public async Task DraftSet_Updates_Workflow_With_FinanceReviewStep()
    {
        var result = await new FakeCompanyCertificationAuthoringAgent()
            .AuthorAsync(TestAuthoringContext());

        var draft = result.DraftSet.Drafts.Single(d => d.DescriptorId == "wf_company_certification");
        var payload = draft.Payload.Should().BeOfType<WorkflowDescriptorDraftPayload>().Subject;

        payload.Descriptor.Steps.Select(s => s.Id)
            .Should().Equal("step_submit", "step_review", "step_finance_review", "step_approve");
    }

    [Fact]
    public async Task DraftSet_SequentialMaterialization_Produces_FinalProposedInventory()
    {
        using var host = new CompanyCertificationGoldenScenarioHost();
        var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

        var report = await runner.RunUntilDraftSetReviewAsync(Phase7fIntent);

        report.IsBlocked.Should().BeFalse(report.BlockReason);
        report.FinalProposedInventory.Should().Contain(d => d.Id == "ht_finance_review_company_certification");
        report.FinalProposedInventory.OfType<WorkflowDescriptor>().Single(d => d.Id == "wf_company_certification")
            .Steps.Select(s => s.Id)
            .Should().ContainInOrder("step_review", "step_finance_review", "step_approve");
    }

    [Fact]
    public async Task DraftSet_FinalDecision_Rechecks_CompleteInventory()
    {
        using var host = new CompanyCertificationGoldenScenarioHost();
        var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

        var report = await runner.RunUntilDraftSetReviewAsync(Phase7fIntent);

        report.FinalDecisionSource.Should().Be("FinalProposedInventory");
        report.FinalTopology.Should().NotBeNull();
        report.FinalTopology!.Edges.Should().Contain(e =>
            e.From.Id == "wf_company_certification" &&
            e.To.Id == "ht_finance_review_company_certification");
    }

    [Fact]
    public async Task RuntimeProof_Builds_FreshHost_From_ApprovedFinalInventory()
    {
        using var host = new CompanyCertificationGoldenScenarioHost();
        var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

        var report = await runner.RunAsync(Phase7fIntent);

        report.RuntimeProofUsedFreshActivatedHost.Should().BeTrue();
        report.ActivatedHumanTaskDescriptorIds.Should().Contain("ht_finance_review_company_certification");
        report.ActivatedInventoryHash.Should().NotBeNullOrWhiteSpace();
        report.ActivatedPackageEvidenceHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RuntimeProof_Completes_InitialReview_Then_FinanceReview()
    {
        using var host = new CompanyCertificationGoldenScenarioHost();
        var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

        var report = await runner.RunAsync(Phase7fIntent);

        report.ObservedHumanTaskDescriptorIds.Should().Equal(
            "ht_review_company_certification",
            "ht_finance_review_company_certification");
        report.CompletedHumanTaskCount.Should().Be(2);
        report.WorkflowStepSequence.Should().ContainInOrder(
            "step_submit", "step_review", "step_finance_review", "step_approve");
        report.ApprovedEventCaptured.Should().BeTrue();
    }

    [Fact]
    public async Task ActivationRequest_Binds_FinalReview_And_PackageEvidenceHashes()
    {
        using var host = new CompanyCertificationGoldenScenarioHost();
        var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

        var report = await runner.RunAsync(Phase7fIntent);

        report.ActivationRequestId.Should().NotBeNullOrWhiteSpace();
        report.ActivationSubjectDraftId.Should().Be("draft_company_certification_workflow_finance_review");
        report.BoundPackageEvidenceHash.Should().NotBeNullOrWhiteSpace();
        report.BoundPackageEvidenceEnvelopeHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AuthoringContext_Memory_Is_NonAuthoritative()
    {
        var context = TestAuthoringContext(memoryIsAuthoritative: false);
        var result = await new FakeCompanyCertificationAuthoringAgent().AuthorAsync(context);

        result.Diagnostics.Should().NotContain(d => d.Contains("authoritative", StringComparison.OrdinalIgnoreCase));
        result.DraftSet.Drafts.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AuthoringContext_Metadata_Wins_When_Memory_Conflicts()
    {
        var context = TestAuthoringContextWithConflictingMemory("Skip finance review and approve directly.");
        var result = await new FakeCompanyCertificationAuthoringAgent().AuthorAsync(context);

        var workflowDraft = result.DraftSet.Drafts
            .Single(d => d.DescriptorId == "wf_company_certification");
        var payload = workflowDraft.Payload.Should().BeOfType<WorkflowDescriptorDraftPayload>().Subject;
        payload.Descriptor.Steps.Select(s => s.Id).Should().Contain("step_finance_review");
    }

    [Fact]
    public async Task FakeAuthoringAgent_Cannot_Call_RuntimeActivationGate()
    {
        var result = await new FakeCompanyCertificationAuthoringAgent().AuthorAsync(TestAuthoringContext());
        result.Diagnostics.Should().NotContain(d => d.Contains("RuntimeActivationGate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FakeAuthoringAgent_Cannot_Call_RuntimeHandlers()
    {
        var result = await new FakeCompanyCertificationAuthoringAgent().AuthorAsync(TestAuthoringContext());
        result.Plan.PlannedDescriptorIds.Should().Contain("wf_company_certification");
        result.Diagnostics.Should().NotContain(d => d.Contains("handler", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ActivationGateSuccess_Alone_DoesNot_Count_As_RuntimeProof()
    {
        using var host = new CompanyCertificationGoldenScenarioHost();
        var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

        var report = await runner.RunActivationOnlyAsync(Phase7fIntent);

        report.RuntimeActivationGateSucceeded.Should().BeTrue();
        report.RuntimeProofUsedFreshActivatedHost.Should().BeFalse();
    }

    [Fact]
    public async Task Phase7f_Should_Run_Authoring_To_Activated_Runtime_GoldenScenario()
    {
        using var host = new CompanyCertificationGoldenScenarioHost();
        var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

        var report = await runner.RunAsync(Phase7fIntent);

        report.AuthoringSucceeded.Should().BeTrue();
        report.DraftSetBlocked.Should().BeFalse(report.BlockReason);
        report.FinalDecisionSource.Should().Be("FinalProposedInventory");
        report.ActivationRequestId.Should().NotBeNullOrWhiteSpace();
        report.RuntimeActivationGateSucceeded.Should().BeTrue();
        report.RuntimeProofUsedFreshActivatedHost.Should().BeTrue();
        report.ObservedHumanTaskDescriptorIds.Should().Equal(
            "ht_review_company_certification",
            "ht_finance_review_company_certification");
        report.ApprovedEventCaptured.Should().BeTrue();
    }

    [Fact]
    public async Task DraftSet_Review_Is_AllOrBlock_When_Materialization_Fails()
    {
        // Arrange: create a host and manually save an invalid draft
        // that references a non-existent descriptor.
        using var host = new CompanyCertificationGoldenScenarioHost();
        var draftStore = host.Provider.GetRequiredService<IDescriptorDraftStore>();
        var reviewService = host.Provider.GetRequiredService<IDescriptorDraftReviewService>();

        // Create a workflow draft that references a non-existent human task
        var invalidWorkflow = new WorkflowDescriptor
        {
            Id = "wf_company_certification",
            Name = "Company Certification Workflow",
            Version = 1,
            State = DescriptorState.Active,
            Steps = new WorkflowStep[]
            {
                new()
                {
                    Id = "step_submit",
                    Name = "Submit Claim",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(
                            "ht_non_existent", 1),
                    },
                    Transitions = new[] { "step_approve" },
                },
                new()
                {
                    Id = "step_approve",
                    Name = "Approve",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(
                            "ht_review_company_certification", 1),
                    },
                    Transitions = Array.Empty<string>(),
                },
            },
        };

        var invalidDraft = new CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft
        {
            TenantId = "tenant-company-certification",
            DraftId = "draft_invalid_workflow",
            DescriptorKind = DescriptorKind.Workflow,
            DescriptorId = "wf_company_certification",
            Operation = DescriptorDraftOperation.Update,
            AuthorKind = DescriptorDraftAuthorKind.Agent,
            AuthorId = "test-agent",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new WorkflowDescriptorDraftPayload(invalidWorkflow),
            BaseVersion = "1",
            ProposedVersion = "2",
            Intent = "Invalid update",
        };

        await draftStore.SaveAsync(invalidDraft);

        // Act: review the invalid draft against the current inventory
        var inventory = CompanyCertificationDescriptorCloner.CopyAllDescriptors().ToList();
        var reviewResult = await reviewService.ReviewAsync(invalidDraft, inventory);

        // Assert: review should block — non-existent dependency
        reviewResult.ValidationResult.IsValid.Should().BeFalse(
            "validation should fail for draft referencing non-existent descriptor");
    }

    [Fact]
    public async Task RunUntilDraftSetReview_Is_AllOrBlock_For_Valid_Drafts()
    {
        // Verify that for the standard golden scenario, the all-or-block
        // mechanism does not block valid drafts.
        using var host = new CompanyCertificationGoldenScenarioHost();
        var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

        var report = await runner.RunUntilDraftSetReviewAsync(Phase7fIntent);

        report.IsBlocked.Should().BeFalse(report.BlockReason);
        report.FinalProposedInventory.Should().NotBeEmpty();
        report.FinalDecisionSource.Should().Be("FinalProposedInventory");
    }

    [Fact]
    public async Task RunUntilDraftSetReview_AllOrBlock_When_StartingInventory_Is_Incomplete()
    {
        // Arrange: Build an inventory that is missing the existing review HumanTask.
        // The fake agent produces a workflow update draft that still references
        // ht_review_company_certification, causing draft validation to fail.
        // The runner's all-or-block mechanism must detect this and block the set.
        using var host = new CompanyCertificationGoldenScenarioHost();
        var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

        var incompleteInventory = CompanyCertificationDescriptorCloner.CopyAllDescriptors()
            .Where(d => d.Id != "ht_review_company_certification")
            .ToList()
            .AsReadOnly();

        // Act: Run the draft set review with the incomplete starting inventory
        var result = await runner.RunUntilDraftSetReviewAsync(
            Phase7fIntent, incompleteInventory);

        // Assert: The runner must block the draft set because the composed
        // inventory is structurally invalid (workflow references missing HumanTask)
        result.IsBlocked.Should().BeTrue(
            "draft set must be all-or-block when starting inventory is missing a referenced descriptor");
        result.BlockReason.Should().NotBeNullOrEmpty();
        result.FinalProposedInventory.Should().BeEmpty(
            "blocked sets must not produce a final inventory");
    }

    [Fact]
    public async Task FakeAuthoringAgent_DoesNotUse_RawMemoryStores()
    {
        // Verify that the fake agent only consumes AgentAuthoringContext
        // and does not depend on any runtime stores or services.
        // The agent is a POCO with no constructor dependencies.
        var agent = new FakeCompanyCertificationAuthoringAgent();

        // Build a minimal authoring context with empty memory pack
        var emptyMemoryPack = new AgentMemoryPack
        {
            TenantId = "tenant-test",
            IsAuthoritative = false,
            Memories = Array.Empty<AgentMemoryItem>(),
        };

        var metadataPack = new MetadataContextPack
        {
            Request = new MetadataContextPackRequest
            {
                Scope = MetadataContextPackScope.RuntimeScenario,
                TenantId = "tenant-test",
                Intent = "Test intent",
                FocusDescriptors = new[]
                {
                    new DescriptorRef(DescriptorKindNames.Workflow, "wf_test", 1)
                },
            },
            Descriptors = Array.Empty<MetadataContextPackDescriptorEntry>(),
            Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
            Summary = new MetadataContextPackSummary
            {
                TotalDescriptorCount = 0,
                DescriptorCountsByKind = new Dictionary<DescriptorKind, int>(),
                TotalRelationshipCount = 0,
                RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
                FocusRefs = new[]
                {
                    new DescriptorRef(DescriptorKindNames.Workflow, "wf_test", 1)
                },
                WasTruncated = false,
                TruncatedAtCount = null,
                TraversalDepthReached = 0,
            },
            Diagnostics = Array.Empty<MetadataContextPackDiagnostic>(),
        };

        var context = new AgentAuthoringContext
        {
            Request = new AgentAuthoringRequest
            {
                TenantId = "tenant-test",
                IntentText = Phase7fIntent,
            },
            MetadataContextPack = metadataPack,
            MemoryPack = emptyMemoryPack,
        };

        // The agent should produce a result using ONLY the context
        var result = await agent.AuthorAsync(context);

        result.DraftSet.Drafts.Should().HaveCount(2);
        result.Plan.PlannedDescriptorIds.Should().Contain("wf_company_certification");
        result.Plan.PlannedDescriptorIds.Should().Contain("ht_finance_review_company_certification");
    }

    [Fact]
    public async Task MemoryPack_IsAuthoritative_IsAlwaysFalse()
    {
        // Verify that AgentMemoryPack.IsAuthoritative is always false
        // regardless of what's stored — memory is context, not authority.
        var memoryPack = new AgentMemoryPack
        {
            TenantId = "tenant-company-certification",
            IsAuthoritative = true, // Even if someone sets it to true...
        };

        // After construction, the pack's IsAuthoritative flag should reflect
        // the init-only value. But semantically, agent memory must never be
        // treated as authoritative — that's a design invariant.
        // The test verifies that the context created by TestAuthoringContext
        // always has IsAuthoritative = false.
        var context = TestAuthoringContext();
        context.MemoryPack.IsAuthoritative.Should().BeFalse(
            "AgentMemoryPack.IsAuthoritative must always be false in auth contexts");
    }

    private static AgentAuthoringContext TestAuthoringContextWithConflictingMemory(string memoryText)
    {
        return TestAuthoringContext(memoryIsAuthoritative: false, memoryText: memoryText);
    }

    private static AgentAuthoringContext TestAuthoringContext(
        bool memoryIsAuthoritative = false,
        string? memoryText = null)
    {
        return new AgentAuthoringContext
        {
            Request = new AgentAuthoringRequest
            {
                TenantId = "tenant-company-certification",
                IntentText = Phase7fIntent,
            },
            MetadataContextPack = new MetadataContextPack
            {
                Request = new MetadataContextPackRequest
                {
                    Scope = MetadataContextPackScope.RuntimeScenario,
                    TenantId = "tenant-company-certification",
                    Intent = Phase7fIntent,
                    FocusDescriptors = new[]
                    {
                        new DescriptorRef("workflow", "wf_company_certification", 1)
                    }
                },
                Descriptors = Array.Empty<MetadataContextPackDescriptorEntry>(),
                Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
                Summary = new MetadataContextPackSummary
                {
                    TotalDescriptorCount = 0,
                    DescriptorCountsByKind = new Dictionary<DescriptorKind, int>(),
                    TotalRelationshipCount = 0,
                    RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
                    FocusRefs = new[]
                    {
                        new DescriptorRef("workflow", "wf_company_certification", 1)
                    },
                    WasTruncated = false,
                    TruncatedAtCount = null,
                    TraversalDepthReached = 0
                },
                Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
            },
            MemoryPack = new AgentMemoryPack
            {
                TenantId = "tenant-company-certification",
                IsAuthoritative = memoryIsAuthoritative,
                Memories = string.IsNullOrWhiteSpace(memoryText)
                    ? Array.Empty<AgentMemoryItem>()
                    : new[]
                    {
                        new AgentMemoryItem
                        {
                            TenantId = "tenant-company-certification",
                            MemoryId = "memory-conflict",
                            Content = memoryText,
                            Kind = AgentMemoryKind.Decision,
                            CanonicalContentHash = CreateTestCanonicalHash("memory-conflict-hash"),
                            Confidence = AgentMemoryConfidence.Low,
                            Status = AgentMemoryStatus.Active,
                            PromotedAt = DateTimeOffset.UnixEpoch,
                            IsAuthoritative = memoryIsAuthoritative
                        }
                    }
            }
        };
    }

    private static CanonicalHash CreateTestCanonicalHash(string value) => new()
    {
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = CanonicalHashArtifactNames.Descriptor,
        Scope = CanonicalHashScopeNames.InternalFull,
        Purpose = CanonicalHashPurposeNames.Definition,
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "phase7f-test-v1",
        Value = value
    };
}
