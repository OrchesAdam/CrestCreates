using System.Reflection;
using System.Text.Json;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.Event.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class ToolDtoSemanticPreservationTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        AgentControlPlaneToolJsonSerializerOptions.CreateDefault();

    private static T RoundTrip<T>(T value) where T : class
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<T>(json, JsonOptions);
        return deserialized!;
    }

    // ── Test 1: All_ControlPlaneToolDtos_RoundTrip_With_SourceGeneratedJson ──

    [Fact]
    public void All_ControlPlaneToolDtos_RoundTrip_With_SourceGeneratedJson()
    {
        // DescriptorRef (struct)
        var ref1 = new DescriptorRef("test.ns", "desc-001", 3);
        var ref1RoundTripped = RoundTripDescriptorRef(ref1);
        ref1RoundTripped.Namespace.Should().Be("test.ns");
        ref1RoundTripped.Id.Should().Be("desc-001");
        ref1RoundTripped.Version.Should().Be(3);

        // DescriptorSummaryDto
        var summaryDto = BuildSampleSummaryDto();
        var summaryRoundTripped = RoundTrip(summaryDto);
        summaryRoundTripped.Should().BeEquivalentTo(summaryDto);

        // AgentDescriptorDraftDto
        var draftDto = BuildSampleDraftDto();
        var draftRoundTripped = RoundTrip(draftDto);
        draftRoundTripped.Should().BeEquivalentTo(draftDto);

        // AgentDraftPayloadDto
        var payloadDto = BuildSampleCapabilityPayloadDto();
        var payloadRoundTripped = RoundTrip(payloadDto);
        payloadRoundTripped.Should().BeEquivalentTo(payloadDto);

        // AgentReviewResultDto
        var reviewDto = BuildSampleReviewResultDto();
        var reviewRoundTripped = RoundTrip(reviewDto);
        reviewRoundTripped.Should().BeEquivalentTo(reviewDto);

        // FixProposal
        var fixProposal = BuildSampleFixProposal();
        var fixRoundTripped = RoundTrip(fixProposal);
        fixRoundTripped.Should().BeEquivalentTo(fixProposal);

        // ActivationRequest
        var activationRequest = BuildSampleActivationRequest();
        var activationRoundTripped = RoundTrip(activationRequest);
        activationRoundTripped.Should().BeEquivalentTo(activationRequest);

        // SubmitActivationRequestRequest
        var submitRequest = BuildSampleSubmitActivationRequest();
        var submitRoundTripped = RoundTrip(submitRequest);
        submitRoundTripped.Should().BeEquivalentTo(submitRequest);

        // AgentToolResult<T>
        var toolResult = BuildSampleToolResult();
        var toolResultRoundTripped = RoundTrip(toolResult);
        toolResultRoundTripped.Should().BeEquivalentTo(toolResult);

        // MetadataContextPack (complex nested)
        var contextPack = BuildSampleContextPack();
        var packRoundTripped = RoundTrip(contextPack);
        packRoundTripped.Should().BeEquivalentTo(contextPack);

        // CreateDescriptorDraftRequest
        var createDraftReq = BuildSampleCreateDraftRequest();
        var createDraftReqRoundTripped = RoundTrip(createDraftReq);
        createDraftReqRoundTripped.Should().BeEquivalentTo(createDraftReq);

        // DescriptorDraftValidationResult
        var validationResult = BuildSampleValidationResult();
        var valRoundTripped = RoundTrip(validationResult);
        valRoundTripped.Should().BeEquivalentTo(validationResult);

        // AgentProposedInventorySummaryDto
        var proposedInventory = BuildSampleProposedInventorySummaryDto();
        var inventoryRoundTripped = RoundTrip(proposedInventory);
        inventoryRoundTripped.Should().BeEquivalentTo(proposedInventory);

        // AgentTopologySummaryDto
        var topologySummary = BuildSampleTopologySummaryDto();
        var topologyRoundTripped = RoundTrip(topologySummary);
        topologyRoundTripped.Should().BeEquivalentTo(topologySummary);

        // AgentMaterializationSummaryDto
        var matSummary = BuildSampleMaterializationSummaryDto();
        var matRoundTripped = RoundTrip(matSummary);
        matRoundTripped.Should().BeEquivalentTo(matSummary);

        // AgentImpactAnalysisSummaryDto
        var impactSummary = BuildSampleImpactAnalysisSummaryDto();
        var impactRoundTripped = RoundTrip(impactSummary);
        impactRoundTripped.Should().BeEquivalentTo(impactSummary);

        // AgentCompatibilitySummaryDto
        var compatSummary = BuildSampleCompatibilitySummaryDto();
        var compatRoundTripped = RoundTrip(compatSummary);
        compatRoundTripped.Should().BeEquivalentTo(compatSummary);

        // AgentGovernanceSummaryDto
        var govSummary = BuildSampleGovernanceSummaryDto();
        var govRoundTripped = RoundTrip(govSummary);
        govRoundTripped.Should().BeEquivalentTo(govSummary);

        // FixProposalListResult
        var fixListResult = BuildSampleFixProposalListResult();
        var fixListRoundTripped = RoundTrip(fixListResult);
        fixListRoundTripped.Should().BeEquivalentTo(fixListResult);

        // AgentToolDiagnostic
        var diagnostic = BuildSampleToolDiagnostic();
        var diagRoundTripped = RoundTrip(diagnostic);
        diagRoundTripped.Should().BeEquivalentTo(diagnostic);

        // AgentToolInvocationAuditRecord
        var auditRecord = BuildSampleAuditRecord();
        var auditRoundTripped = RoundTrip(auditRecord);
        auditRoundTripped.Should().BeEquivalentTo(auditRecord);

        // AgentCapabilityDraftPayloadDto (standalone)
        var capPayload = BuildSampleCapabilityPayload();
        var capPayloadRoundTripped = RoundTrip(capPayload);
        capPayloadRoundTripped.Should().BeEquivalentTo(capPayload);

        // AgentWorkflowDraftPayloadDto (standalone)
        var wfPayload = BuildSampleWorkflowPayload();
        var wfPayloadRoundTripped = RoundTrip(wfPayload);
        wfPayloadRoundTripped.Should().BeEquivalentTo(wfPayload);

        // AgentHumanTaskDraftPayloadDto (standalone)
        var htPayload = BuildSampleHumanTaskPayload();
        var htPayloadRoundTripped = RoundTrip(htPayload);
        htPayloadRoundTripped.Should().BeEquivalentTo(htPayload);

        // AgentFormDraftPayloadDto (standalone)
        var formPayload = BuildSampleFormPayload();
        var formPayloadRoundTripped = RoundTrip(formPayload);
        formPayloadRoundTripped.Should().BeEquivalentTo(formPayload);

        // AgentEventDraftPayloadDto (standalone)
        var eventPayload = BuildSampleEventPayload();
        var eventPayloadRoundTripped = RoundTrip(eventPayload);
        eventPayloadRoundTripped.Should().BeEquivalentTo(eventPayload);

        // AgentSchemaDraftPayloadDto (standalone)
        var schemaPayload = BuildSampleSchemaPayload();
        var schemaPayloadRoundTripped = RoundTrip(schemaPayload);
        schemaPayloadRoundTripped.Should().BeEquivalentTo(schemaPayload);
    }

    // ── Test 2: ContextPackDto_Preserves_CanonicalRefs ──

    [Fact]
    public void ContextPackDto_Preserves_CanonicalRefs()
    {
        var pack = new MetadataContextPack
        {
            Request = new MetadataContextPackRequest
            {
                Scope = MetadataContextPackScope.DirectDependencies,
                FocusDescriptors = new[]
                {
                    new DescriptorRef("foo.bar", "comp-A", 1),
                    new DescriptorRef("baz.qux", "comp-B", 2)
                },
                TenantId = "tenant-ctx",
                MaxTraversalDepth = 3
            },
            Descriptors = new[]
            {
                new MetadataContextPackDescriptorEntry
                {
                    Ref = new DescriptorRef("foo.bar", "comp-A", 1),
                    Kind = DescriptorKind.Capability,
                    Name = "Component A",
                    State = DescriptorState.Active,
                    IsFocus = true
                },
                new MetadataContextPackDescriptorEntry
                {
                    Ref = new DescriptorRef("baz.qux", "comp-B", 2),
                    Kind = DescriptorKind.Event,
                    Name = "Component B",
                    State = DescriptorState.Draft,
                    IsFocus = true
                }
            },
            Relationships = new[]
            {
                new MetadataContextPackRelationshipEntry
                {
                    From = new DescriptorRef("foo.bar", "comp-A", 1),
                    To = new DescriptorRef("baz.qux", "comp-B", 2),
                    Kind = RelationshipKind.Produces,
                    Strength = RelationshipStrength.Strong
                }
            },
            Summary = new MetadataContextPackSummary
            {
                TotalDescriptorCount = 2,
                DescriptorCountsByKind = new Dictionary<DescriptorKind, int>
                {
                    [DescriptorKind.Capability] = 1,
                    [DescriptorKind.Event] = 1
                },
                TotalRelationshipCount = 1,
                RelationshipCountsByKind = new Dictionary<RelationshipKind, int>
                {
                    [RelationshipKind.Produces] = 1
                },
                FocusRefs = new[]
                {
                    new DescriptorRef("foo.bar", "comp-A", 1),
                    new DescriptorRef("baz.qux", "comp-B", 2)
                },
                WasTruncated = false,
                TruncatedAtCount = null,
                TraversalDepthReached = 1
            },
            Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
        };

        var roundTripped = RoundTrip(pack);

        roundTripped.Should().BeEquivalentTo(pack);

        // Explicit canonical ref checks
        roundTripped.Request.FocusDescriptors.Should().HaveCount(2);
        roundTripped.Request.FocusDescriptors[0].Namespace.Should().Be("foo.bar");
        roundTripped.Request.FocusDescriptors[0].Id.Should().Be("comp-A");
        roundTripped.Request.FocusDescriptors[0].Version.Should().Be(1);
        roundTripped.Request.FocusDescriptors[1].Namespace.Should().Be("baz.qux");
        roundTripped.Request.FocusDescriptors[1].Id.Should().Be("comp-B");
        roundTripped.Request.FocusDescriptors[1].Version.Should().Be(2);

        roundTripped.Descriptors.Should().HaveCount(2);
        roundTripped.Descriptors[0].Ref.Namespace.Should().Be("foo.bar");
        roundTripped.Descriptors[0].Ref.Id.Should().Be("comp-A");
        roundTripped.Descriptors[1].Ref.Namespace.Should().Be("baz.qux");
        roundTripped.Descriptors[1].Ref.Id.Should().Be("comp-B");

        roundTripped.Summary.FocusRefs.Should().HaveCount(2);
    }

    // ── Test 3: DraftReviewResultDto_Preserves_Diagnostics ──

    [Fact]
    public void DraftReviewResultDto_Preserves_Diagnostics()
    {
        var reviewDto = new AgentReviewResultDto
        {
            DraftId = "draft-diag",
            TenantId = "tenant-diag",
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
                new DraftAbstractions.DescriptorDraftDiagnostic
                {
                    Code = "CONTRACT_BREAKING_CHANGE",
                    Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker,
                    Message = "Contract hash changed on compatible version increment",
                    Path = "contractHash",
                    DescriptorKind = DescriptorKind.Capability,
                    DescriptorId = "test.cap-001"
                },
                new DraftAbstractions.DescriptorDraftDiagnostic
                {
                    Code = "MISSING_DESCRIPTION",
                    Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning,
                    Message = "Descriptor does not have a description",
                    DescriptorId = "test.cap-001"
                },
                new DraftAbstractions.DescriptorDraftDiagnostic
                {
                    Code = "UNVERSIONED_REFERENCE",
                    Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Info,
                    Message = "Reference to 'test.dep' is not version-pinned",
                    RelatedDiagnosticCode = "AMBIGUOUS_REF_PINNING"
                }
            ),
            Diagnostics = new[]
            {
                new DraftAbstractions.DescriptorDraftDiagnostic
                {
                    Code = "TOPOLOGY_ORPHAN",
                    Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error,
                    Message = "Descriptor has no incoming or outgoing edges",
                    DescriptorKind = DescriptorKind.Capability
                }
            },
            IsActivationEligible = false
        };

        var roundTripped = RoundTrip(reviewDto);

        roundTripped.Should().BeEquivalentTo(reviewDto);

        // Explicit diagnostic validation
        roundTripped.ValidationResult.IsValid.Should().BeFalse();
        roundTripped.ValidationResult.Diagnostics.Should().HaveCount(3);
        roundTripped.ValidationResult.Diagnostics[0].Code.Should().Be("CONTRACT_BREAKING_CHANGE");
        roundTripped.ValidationResult.Diagnostics[0].Severity.Should()
            .Be(DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker);
        roundTripped.ValidationResult.Diagnostics[0].Message.Should()
            .Be("Contract hash changed on compatible version increment");
        roundTripped.ValidationResult.Diagnostics[0].Path.Should().Be("contractHash");

        roundTripped.ValidationResult.Diagnostics[1].Code.Should().Be("MISSING_DESCRIPTION");
        roundTripped.ValidationResult.Diagnostics[1].Severity.Should()
            .Be(DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning);

        roundTripped.ValidationResult.Diagnostics[2].Code.Should().Be("UNVERSIONED_REFERENCE");
        roundTripped.ValidationResult.Diagnostics[2].Severity.Should()
            .Be(DraftAbstractions.DescriptorDraftDiagnosticSeverity.Info);
        roundTripped.ValidationResult.Diagnostics[2].RelatedDiagnosticCode.Should()
            .Be("AMBIGUOUS_REF_PINNING");

        roundTripped.Diagnostics.Should().HaveCount(1);
        roundTripped.Diagnostics[0].Code.Should().Be("TOPOLOGY_ORPHAN");
        roundTripped.Diagnostics[0].Severity.Should()
            .Be(DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error);

        roundTripped.IsActivationEligible.Should().BeFalse();
    }

    // ── Test 4: FixProposalDto_Preserves_RiskAndApprovalSemantics ──

    [Fact]
    public void FixProposalDto_Preserves_RiskAndApprovalSemantics()
    {
        var fixProposal = new FixProposal
        {
            ProposalId = "fix-high-risk",
            DraftId = "draft-001",
            TenantId = "tenant-001",
            RiskLevel = FixProposalRiskLevel.High,
            RequiresHumanApproval = true,
            Actions = new[]
            {
                new FixProposalAction
                {
                    Path = "contractHash",
                    ActionKind = FixProposalActionKind.Set,
                    CurrentValue = "old-hash-abc123",
                    ProposedValue = "new-hash-def456",
                    Description = "Update contract hash to reflect breaking change"
                },
                new FixProposalAction
                {
                    Path = "version",
                    ActionKind = FixProposalActionKind.Set,
                    CurrentValue = "2",
                    ProposedValue = "3",
                    Description = "Bump major version for breaking change"
                }
            },
            Diagnostics = new[]
            {
                new AgentToolDiagnostic
                {
                    Code = "FIX_REQUIRES_REVIEW",
                    Severity = AgentToolDiagnosticSeverity.Warning,
                    Message = "This fix proposal requires human review before application"
                }
            },
            CreatedAt = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero),
            Rationale = "Contract hash must be updated to reflect the changed schema"
        };

        var roundTripped = RoundTrip(fixProposal);

        roundTripped.RiskLevel.Should().Be(FixProposalRiskLevel.High);
        roundTripped.RequiresHumanApproval.Should().BeTrue();
        roundTripped.Actions.Should().HaveCount(2);
        roundTripped.Actions[0].Path.Should().Be("contractHash");
        roundTripped.Actions[0].ActionKind.Should().Be(FixProposalActionKind.Set);
        roundTripped.Actions[0].CurrentValue.Should().Be("old-hash-abc123");
        roundTripped.Actions[0].ProposedValue.Should().Be("new-hash-def456");
        roundTripped.Actions[1].Path.Should().Be("version");
        roundTripped.Actions[1].CurrentValue.Should().Be("2");
        roundTripped.Actions[1].ProposedValue.Should().Be("3");
        roundTripped.Rationale.Should().Be("Contract hash must be updated to reflect the changed schema");
        roundTripped.ProposalId.Should().Be("fix-high-risk");
    }

    // ── Test 5: ActivationRequestDto_Remains_HandoffOnly ──

    [Fact]
    public void ActivationRequestDto_Remains_HandoffOnly()
    {
        // ActivationRequest: only submit/cancel handoff, no approve/execute/activate
        var arProperties = typeof(ActivationRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        arProperties.Should().NotContain(p =>
            p.Contains("Approve", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Execute", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Activate", StringComparison.OrdinalIgnoreCase));

        // Should have handoff-only members: submit/cancel and status tracking
        arProperties.Should().Contain("RequestId");
        arProperties.Should().Contain("Status");
        arProperties.Should().Contain("SubmittedAt");
        arProperties.Should().Contain("SubmittedBy");

        // ActivationRequest should have no methods with approve/execute/activate
        var arMethods = typeof(ActivationRequest)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToList();

        arMethods.Should().NotContain(m =>
            m.Contains("Approve", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("Execute", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("Activate", StringComparison.OrdinalIgnoreCase));

        // SubmitActivationRequestRequest: only submit/cancel handoff
        var sarrProperties = typeof(SubmitActivationRequestRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        sarrProperties.Should().NotContain(p =>
            p.Contains("Approve", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Execute", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Activate", StringComparison.OrdinalIgnoreCase));

        // SubmitActivationRequestRequest should have no methods with approve/execute/activate
        var sarrMethods = typeof(SubmitActivationRequestRequest)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToList();

        sarrMethods.Should().NotContain(m =>
            m.Contains("Approve", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("Execute", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("Activate", StringComparison.OrdinalIgnoreCase));
    }

    // ── Test 6: ReviewEligibility_DoesNotGrantActivationAuthority ──

    [Fact]
    public void ReviewEligibility_DoesNotGrantActivationAuthority()
    {
        // Check XML doc comment on IsActivationEligible
        var isActivationEligibleProp = typeof(AgentReviewResultDto)
            .GetProperty(nameof(AgentReviewResultDto.IsActivationEligible));
        isActivationEligibleProp.Should().NotBeNull();

        // Verify structural constraint:
        // AgentReviewResultDto does NOT have properties like CanActivate,
        // ActivationApproved, ExecuteActivation.
        var reviewProperties = typeof(AgentReviewResultDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        reviewProperties.Should().NotContain("CanActivate");
        reviewProperties.Should().NotContain("ActivationApproved");
        reviewProperties.Should().NotContain("ExecuteActivation");

        // Verify IsActivationEligible IS present (it is the agent-facing signal)
        reviewProperties.Should().Contain("IsActivationEligible");

        // Verify IsActivationEligible is a bool (readiness signal, not an action)
        isActivationEligibleProp!.PropertyType.Should().Be(typeof(bool));
    }

    // ── Test 7: DescriptorSummaryDto_RoundTrip_With_SourceGeneratedJson ──

    [Fact]
    public void DescriptorSummaryDto_RoundTrip_With_SourceGeneratedJson()
    {
        var dto = new DescriptorSummaryDto
        {
            Ref = new DescriptorRef("system.platform", "auth-service", 5),
            Kind = DescriptorKind.Capability,
            Name = "Authentication Service",
            DisplayName = "Auth Service v5",
            LifecycleState = "Active"
        };

        var roundTripped = RoundTrip(dto);

        roundTripped.Should().BeEquivalentTo(dto);
        roundTripped.Ref.Namespace.Should().Be("system.platform");
        roundTripped.Ref.Id.Should().Be("auth-service");
        roundTripped.Ref.Version.Should().Be(5);
        roundTripped.Kind.Should().Be(DescriptorKind.Capability);
        roundTripped.Name.Should().Be("Authentication Service");
        roundTripped.DisplayName.Should().Be("Auth Service v5");
        roundTripped.LifecycleState.Should().Be("Active");
    }

    // ── Test 8: AgentDescriptorDraftDto_RoundTrip_With_SourceGeneratedJson ──

    [Fact]
    public void AgentDescriptorDraftDto_RoundTrip_With_SourceGeneratedJson()
    {
        var dto = new AgentDescriptorDraftDto
        {
            TenantId = "tenant-1",
            DraftId = "draft-1",
            DescriptorKind = DescriptorKind.Capability,
            DescriptorId = "cap-1",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            AuthorKind = DraftAbstractions.DescriptorDraftAuthorKind.Agent,
            AuthorId = "agent-1",
            CreatedAt = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero),
            Payload = new AgentDraftPayloadDto
            {
                Discriminator = DescriptorKind.Capability,
                Capability = new AgentCapabilityDraftPayloadDto
                {
                    Name = "TestCapability",
                    CapabilityKind = CapabilityKind.Command,
                    RiskLevel = CapabilityRiskLevel.Low,
                    State = DescriptorState.Active,
                    Version = 1
                }
            },
            BaseVersion = "0.9.0",
            ProposedVersion = "1.0.0",
            Intent = "Create initial capability",
            Rationale = "Foundation for auth module",
            CorrelationId = "corr-abc",
            Source = "AgentFlow/Planner",
            Metadata = new Dictionary<string, string>
            {
                ["priority"] = "high",
                ["scope"] = "platform"
            },
            Status = DraftAbstractions.DescriptorDraftStatus.Created
        };

        var roundTripped = RoundTrip(dto);

        roundTripped.Should().BeEquivalentTo(dto);

        // Verify sub-record fidelity
        roundTripped.Payload.Discriminator.Should().Be(DescriptorKind.Capability);
        roundTripped.Payload.Capability.Should().NotBeNull();
        roundTripped.Payload.Capability!.Name.Should().Be("TestCapability");
        roundTripped.Payload.Capability.CapabilityKind.Should().Be(CapabilityKind.Command);
        roundTripped.Payload.Capability.RiskLevel.Should().Be(CapabilityRiskLevel.Low);
        roundTripped.Payload.Capability.Version.Should().Be(1);
        roundTripped.Payload.Workflow.Should().BeNull();
        roundTripped.Payload.HumanTask.Should().BeNull();
        roundTripped.Payload.Form.Should().BeNull();
        roundTripped.Payload.Event.Should().BeNull();
        roundTripped.Payload.Schema.Should().BeNull();

        // Verify metadata
        roundTripped.Metadata.Should().ContainKeys("priority", "scope");
        roundTripped.Metadata!["priority"].Should().Be("high");

        // Verify status and operation enums
        roundTripped.Status.Should().Be(DraftAbstractions.DescriptorDraftStatus.Created);
        roundTripped.Operation.Should().Be(DraftAbstractions.DescriptorDraftOperation.Create);
        roundTripped.AuthorKind.Should().Be(DraftAbstractions.DescriptorDraftAuthorKind.Agent);
    }

    // ── Test 9: AgentDraftPayloadDto_RoundTrip_DoesNot_Lose_KindSpecific_Fields ──

    [Fact]
    public void AgentDraftPayloadDto_RoundTrip_DoesNot_Lose_KindSpecific_Fields()
    {
        var payload = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadDto
            {
                Name = "TestCapability",
                State = DescriptorState.Active,
                InputSchema = new DescriptorRef("schema", "input-schema-1", 1),
                OutputSchema = new DescriptorRef("schema", "output-schema-1", 1),
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Medium,
                Produces = new[] { new DescriptorRef("test", "output-1", 1) },
                Consumes = new[] { new DescriptorRef("test", "input-1", 2) },
                ContractHash = "ch-cap-001",
                DefinitionHash = "dh-cap-001",
                Version = 2,
            }
        };

        var roundTripped = RoundTrip(payload);

        roundTripped.Discriminator.Should().Be(DescriptorKind.Capability);
        roundTripped.Capability.Should().NotBeNull();

        var cap = roundTripped.Capability!;
        cap.Name.Should().Be("TestCapability");
        cap.State.Should().Be(DescriptorState.Active);
        cap.InputSchema.Should().NotBeNull();
        cap.InputSchema!.Value.Id.Should().Be("input-schema-1");
        cap.InputSchema.Value.Version.Should().Be(1);
        cap.OutputSchema.Should().NotBeNull();
        cap.OutputSchema!.Value.Id.Should().Be("output-schema-1");
        cap.OutputSchema.Value.Version.Should().Be(1);
        cap.CapabilityKind.Should().Be(CapabilityKind.Command);
        cap.Produces.Should().NotBeNull();
        cap.Produces!.Should().HaveCount(1);
        cap.Produces[0].Namespace.Should().Be("test");
        cap.Produces[0].Id.Should().Be("output-1");
        cap.Consumes.Should().NotBeNull();
        cap.Consumes!.Should().HaveCount(1);
        cap.Consumes[0].Id.Should().Be("input-1");
        cap.RiskLevel.Should().Be(CapabilityRiskLevel.Medium);
        cap.ContractHash.Should().Be("ch-cap-001");
        cap.DefinitionHash.Should().Be("dh-cap-001");
        cap.Version.Should().Be(2);

        // Other sub-records must remain null
        roundTripped.Workflow.Should().BeNull();
        roundTripped.HumanTask.Should().BeNull();
        roundTripped.Form.Should().BeNull();
        roundTripped.Event.Should().BeNull();
        roundTripped.Schema.Should().BeNull();
    }

    // ── Test 10: ContractVersion_Is_7c_v1 ──

    [Fact]
    public void ContractVersion_Is_7c_v1()
    {
        AgentControlPlaneContractVersion.Current.Should().Be("7c.v1");
    }

    // ── Helper: DescriptorRef round-trip (value type) ──

    private static DescriptorRef RoundTripDescriptorRef(DescriptorRef value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<DescriptorRef>(json, JsonOptions);
    }

    // ── Builders ──

    private static DescriptorSummaryDto BuildSampleSummaryDto() => new()
    {
        Ref = new DescriptorRef("sample.ns", "sample-id", 42),
        Kind = DescriptorKind.Workflow,
        Name = "Sample Workflow",
        DisplayName = "My Workflow",
        LifecycleState = "Draft"
    };

    private static AgentDescriptorDraftDto BuildSampleDraftDto() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-1",
        DescriptorKind = DescriptorKind.Capability,
        DescriptorId = "cap-1",
        Operation = DraftAbstractions.DescriptorDraftOperation.Create,
        AuthorKind = DraftAbstractions.DescriptorDraftAuthorKind.Agent,
        AuthorId = "agent-1",
        CreatedAt = new DateTimeOffset(2026, 6, 21, 10, 30, 0, TimeSpan.Zero),
        Payload = BuildSampleCapabilityPayloadDto(),
        Status = DraftAbstractions.DescriptorDraftStatus.Created
    };

    private static AgentDraftPayloadDto BuildSampleCapabilityPayloadDto() => new()
    {
        Discriminator = DescriptorKind.Capability,
        Capability = BuildSampleCapabilityPayload()
    };

    private static AgentCapabilityDraftPayloadDto BuildSampleCapabilityPayload() => new()
    {
        Name = "TestCapability",
        CapabilityKind = CapabilityKind.Command,
        RiskLevel = CapabilityRiskLevel.Low,
        State = DescriptorState.Active,
        Version = 1
    };

    private static AgentWorkflowDraftPayloadDto BuildSampleWorkflowPayload() => new()
    {
        Name = "TestWorkflow",
        State = DescriptorState.Draft,
        VariableSchema = new DescriptorRef("schema", "wf-schema-1", 1),
    };

    private static AgentHumanTaskDraftPayloadDto BuildSampleHumanTaskPayload() => new()
    {
        Name = "TestHumanTask",
        State = DescriptorState.Draft,
        AssigneeStrategy = AssigneeStrategy.SingleUser,
        InputSchema = new DescriptorRef("schema", "ht-input-schema", 1),
        OutputSchema = new DescriptorRef("schema", "ht-output-schema", 1),
        Interaction = new DescriptorRef("form", "ht-interaction", 1),
        Timeout = "00:30:00",
    };

    private static AgentFormDraftPayloadDto BuildSampleFormPayload() => new()
    {
        Name = "TestForm",
        State = DescriptorState.Draft,
    };

    private static AgentEventDraftPayloadDto BuildSampleEventPayload() => new()
    {
        Name = "TestEvent",
        State = DescriptorState.Draft,
        Category = EventCategory.Domain,
        Semantic = EventSemantic.Fact,
        Importance = EventImportance.Business,
        ChangeKind = SchemaChangeKind.Additive,
        PayloadSchema = new DescriptorRef("schema", "evt-payload-schema", 1),
    };

    private static AgentSchemaDraftPayloadDto BuildSampleSchemaPayload() => new()
    {
        Name = "TestSchema",
        State = DescriptorState.Draft,
        ChangeKind = SchemaChangeKind.Additive
    };

    private static AgentReviewResultDto BuildSampleReviewResultDto() => new()
    {
        DraftId = "draft-review",
        TenantId = "tenant-review",
        ValidationResult = BuildSampleValidationResult(),
        Diagnostics = new[]
        {
            new DraftAbstractions.DescriptorDraftDiagnostic
            {
                Code = "REVIEW_MISSING_DESCRIPTION",
                Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning,
                Message = "Descriptor lacks description"
            }
        },
        IsActivationEligible = false
    };

    private static DraftAbstractions.DescriptorDraftValidationResult BuildSampleValidationResult() =>
        DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic
            {
                Code = "DRAFT_NAME_EMPTY",
                Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error,
                Message = "Name must not be empty"
            });

    private static FixProposal BuildSampleFixProposal() => new()
    {
        ProposalId = "fix-001",
        DraftId = "draft-001",
        TenantId = "tenant-001",
        RiskLevel = FixProposalRiskLevel.Low,
        RequiresHumanApproval = false,
        Actions = new[]
        {
            new FixProposalAction
            {
                Path = "name",
                ActionKind = FixProposalActionKind.Set,
                CurrentValue = "OldName",
                ProposedValue = "NewName",
                Description = "Rename descriptor"
            }
        },
        Diagnostics = Array.Empty<AgentToolDiagnostic>(),
        CreatedAt = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero)
    };

    private static ActivationRequest BuildSampleActivationRequest() => new()
    {
        RequestId = "ar-001",
        TenantId = "tenant-001",
        DraftId = "draft-001",
        Status = ActivationRequestStatus.Submitted,
        SubmittedAt = new DateTimeOffset(2026, 6, 21, 15, 0, 0, TimeSpan.Zero),
        SubmittedBy = "agent-001",
        CorrelationId = "corr-ar-001"
    };

    private static SubmitActivationRequestRequest BuildSampleSubmitActivationRequest() => new()
    {
        DraftId = "draft-001",
        ReviewResultId = "rr-001",
        PackagePreviewId = "pp-001",
        EvidencePreviewId = "ep-001",
        Rationale = "Ready for activation"
    };

    private static AgentToolResult<AgentDescriptorDraftDto> BuildSampleToolResult() =>
        AgentToolResult<AgentDescriptorDraftDto>.Success(
            BuildSampleDraftDto(),
            new AgentToolInvocationAuditRecord
            {
                AuditId = "audit-001",
                Timestamp = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero),
                Context = new AgentToolInvocationContext
                {
                    TenantId = "tenant-1",
                    ActorId = "agent-1",
                    ActorKind = AgentToolActorKind.Agent,
                    CorrelationId = "corr-001",
                    ToolName = "CreateDescriptorDraft",
                    InvocationSource = AgentToolInvocationSource.Direct
                },
                ResultStatus = AgentToolResultStatus.Success,
                Diagnostics = Array.Empty<AgentToolDiagnostic>()
            });

    private static MetadataContextPack BuildSampleContextPack() => new()
    {
        Request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { new DescriptorRef("test", "desc-1", 1) },
            MaxTraversalDepth = 2,
            MaxDescriptorCount = 32
        },
        Descriptors = new[]
        {
            new MetadataContextPackDescriptorEntry
            {
                Ref = new DescriptorRef("test", "desc-1", 1),
                Kind = DescriptorKind.Capability,
                Name = "Test Descriptor",
                State = DescriptorState.Active,
                IsFocus = true
            }
        },
        Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
        Summary = new MetadataContextPackSummary
        {
            TotalDescriptorCount = 1,
            DescriptorCountsByKind = new Dictionary<DescriptorKind, int>
            {
                [DescriptorKind.Capability] = 1
            },
            TotalRelationshipCount = 0,
            RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
            FocusRefs = new[] { new DescriptorRef("test", "desc-1", 1) },
            WasTruncated = false,
            TruncatedAtCount = null,
            TraversalDepthReached = 0
        },
        Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
    };

    private static CreateDescriptorDraftRequest BuildSampleCreateDraftRequest() => new()
    {
        DescriptorKind = DescriptorKind.Capability,
        DescriptorId = "test.new-cap",
        Operation = DraftAbstractions.DescriptorDraftOperation.Create,
        Payload = BuildSampleCapabilityPayloadDto(),
        ProposedVersion = "1.0.0",
        Intent = "Create new capability"
    };

    private static AgentProposedInventorySummaryDto BuildSampleProposedInventorySummaryDto() => new()
    {
        DescriptorRefs = new[]
        {
            new DescriptorRef("test", "desc-1", 1),
            new DescriptorRef("test", "desc-2", 2)
        },
        TotalCount = 2,
        CountsByKind = new Dictionary<DescriptorKind, int>
        {
            [DescriptorKind.Capability] = 1,
            [DescriptorKind.Event] = 1
        }
    };

    private static AgentTopologySummaryDto BuildSampleTopologySummaryDto() => new()
    {
        TotalNodeCount = 3,
        TotalEdgeCount = 2,
        NodeCountsByKind = new Dictionary<DescriptorKind, int>
        {
            [DescriptorKind.Capability] = 2,
            [DescriptorKind.Event] = 1
        },
        EdgeCountsByKind = new Dictionary<RelationshipKind, int>
        {
            [RelationshipKind.Produces] = 1,
            [RelationshipKind.Consumes] = 1
        }
    };

    private static AgentMaterializationSummaryDto BuildSampleMaterializationSummaryDto() => new()
    {
        IsMaterialized = true,
        ProposedInventoryRefs = new[]
        {
            new DescriptorRef("test", "mat-1", 1)
        },
        Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>()
    };

    private static AgentImpactAnalysisSummaryDto BuildSampleImpactAnalysisSummaryDto() => new()
    {
        AffectedDescriptors = new[]
        {
            new DescriptorRef("test", "affected-1", 1),
            new DescriptorRef("test", "affected-2", 1)
        },
        TotalAffectedCount = 2,
        Severity = "Medium",
        Summary = "Two descriptors may be affected"
    };

    private static AgentCompatibilitySummaryDto BuildSampleCompatibilitySummaryDto() => new()
    {
        IsCompatible = true,
        IncompatibilityCount = 0,
        Summary = "No incompatibilities found"
    };

    private static AgentGovernanceSummaryDto BuildSampleGovernanceSummaryDto() => new()
    {
        IsApproved = true,
        Decision = "Approved",
        Rationale = "All policy checks passed"
    };

    private static FixProposalListResult BuildSampleFixProposalListResult() => new()
    {
        Proposals = new[] { BuildSampleFixProposal() }
    };

    private static AgentToolDiagnostic BuildSampleToolDiagnostic() => new()
    {
        Code = "TEST_DIAG",
        Severity = AgentToolDiagnosticSeverity.Warning,
        Message = "Test diagnostic",
        Path = "some.field",
        RelatedDiagnosticCode = "RELATED_TEST"
    };

    private static AgentToolInvocationAuditRecord BuildSampleAuditRecord() => new()
    {
        AuditId = "audit-002",
        Timestamp = new DateTimeOffset(2026, 6, 21, 14, 0, 0, TimeSpan.Zero),
        Context = new AgentToolInvocationContext
        {
            TenantId = "tenant-1",
            ActorId = "actor-1",
            ActorKind = AgentToolActorKind.Human,
            CorrelationId = "corr-002",
            ToolName = "GetDescriptorDraft",
            InvocationSource = AgentToolInvocationSource.HttpAdapter
        },
        ResultStatus = AgentToolResultStatus.Success,
        Diagnostics = Array.Empty<AgentToolDiagnostic>(),
        InputSummaryHash = "hash-abc",
        TouchedDescriptorRefs = new[] { new DescriptorRef("test", "desc-1", 1) },
        TouchedDraftIds = new[] { "draft-1" }
    };
}
