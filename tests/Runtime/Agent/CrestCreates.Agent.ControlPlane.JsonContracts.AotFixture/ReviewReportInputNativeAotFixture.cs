using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using CrestCreates.DescriptorDraft.CanonicalHashing;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Core.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ReviewReportInputNativeAotFixture
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 24, 4, 30, 0, TimeSpan.Zero);

    public static bool Run()
    {
        try
        {
            var services = new ServiceCollection();
            services.AddRelationshipKernel();
            services.AddTopologyKernel();
            services.AddDescriptorPackaging();
            using var provider = services.BuildServiceProvider();

            var baseSchema = new SchemaDescriptor
            {
                Id = "report-input-base-schema",
                Name = "CapturedBaseSchema",
                Version = 1,
                ChangeKind = SchemaChangeKind.Additive
            };
            var schema = new SchemaDescriptor
            {
                Id = "report-input-schema",
                Name = "CapturedSchema",
                Version = 2,
                ChangeKind = SchemaChangeKind.Additive,
                References = [new VersionedDescriptorRef<SchemaDescriptor>(baseSchema.Id, baseSchema.Version)]
            };
            var eventDescriptor = new EventDescriptor
            {
                Id = "report-input-event",
                Name = "CapturedEvent",
                Version = 3,
                State = DescriptorState.Active,
                Category = EventCategory.Domain,
                Semantic = EventSemantic.Fact,
                Importance = EventImportance.Business,
                ChangeKind = SchemaChangeKind.Additive,
                PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>(schema.Id, schema.Version)
            };
            var descriptors = new IDescriptor[] { eventDescriptor, schema, baseSchema };
            var topology = provider.GetRequiredService<IDescriptorTopologyBuilder>().Build(descriptors);

            var validationDiagnostic = Diagnostic("AOT-VALIDATION-INFO", SeverityLevel.Info, "Validation captured independently.");
            var reviewDiagnostic = Diagnostic("AOT-REVIEW-WARNING", SeverityLevel.Warning, "Review captured a useful warning.");
            var impact = new DescriptorImpactAnalysisReport
            {
                ChangeSet = new DescriptorChangeSet { Changes = [] },
                AffectedDescriptors =
                [
                    new AffectedDescriptor
                    {
                        Ref = new DescriptorRef("capability", "report-impacted-capability", 1),
                        Kind = DescriptorKind.Capability,
                        Name = "CapturedImpactCapability",
                        Severity = DescriptorImpactSeverity.Medium,
                        RuntimeAreas = [],
                        Paths = [],
                        Reason = "Captured transitive dependency impact."
                    }
                ],
                Paths = [],
                MaxSeverity = DescriptorImpactSeverity.Medium,
                Diagnostics = []
            };
            var compatibility = new DescriptorCompatibilityReport
            {
                ChangeSet = new DescriptorChangeSet { Changes = [] },
                ImpactReport = impact,
                Findings =
                [
                    new DescriptorCompatibilityFinding
                    {
                        Subject = new DescriptorRef("schema", schema.Id, schema.Version),
                        ChangeKind = DescriptorChangeKind.Updated,
                        Level = DescriptorCompatibilityLevel.Risky,
                        Kind = DescriptorCompatibilityFindingKind.Structural,
                        RuleId = "AOT-COMPAT-001",
                        Message = "Captured schema compatibility finding."
                    }
                ],
                MaxLevel = DescriptorCompatibilityLevel.Risky,
                Diagnostics = []
            };
            var transition = new DescriptorLifecycleTransition
            {
                Subject = new DescriptorRef("event", eventDescriptor.Id, eventDescriptor.Version),
                Operation = DescriptorLifecycleOperation.SubmitForReview,
                FromState = DescriptorState.Draft,
                ToState = DescriptorState.Active,
                Reason = "Captured typed transition."
            };
            var governance = new DescriptorLifecycleGovernanceReport
            {
                Decisions =
                [
                    new DescriptorLifecycleDecision
                    {
                        Transition = transition,
                        Decision = DescriptorLifecycleDecisionKind.ReviewRequired,
                        Findings =
                        [
                            new DescriptorLifecycleFinding
                            {
                                Severity = SeverityLevel.Warning,
                                Code = new DiagnosticCode("AOT-GOVERNANCE-001"),
                                Message = "Human review is required.",
                                Subject = new DescriptorRef("event", eventDescriptor.Id, eventDescriptor.Version),
                                RelatedRefs = [new DescriptorRef("schema", schema.Id, schema.Version)]
                            }
                        ]
                    }
                ],
                MaxDecision = DescriptorLifecycleDecisionKind.ReviewRequired,
                PackageFindings =
                [
                    new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Info,
                        Code = new DiagnosticCode("AOT-PACKAGE-FINDING-001"),
                        Message = "Captured package finding.",
                        Subject = new DescriptorRef("schema", schema.Id, schema.Version)
                    }
                ]
            };

            var draft = new DescriptorDraft
            {
                TenantId = "report-input-tenant",
                DraftId = "report-input-draft",
                DescriptorKind = DescriptorKind.Event,
                DescriptorId = eventDescriptor.Id,
                Operation = DescriptorDraftOperation.Update,
                AuthorKind = DescriptorDraftAuthorKind.Agent,
                AuthorId = "report-input-author",
                CreatedAt = FixedNow.AddMinutes(-5),
                Payload = new EventDescriptorDraftPayload(eventDescriptor),
                BaseVersion = "2",
                ProposedVersion = "3",
                Status = DescriptorDraftStatus.Reviewed,
                Intent = "Intent is not part of captured owner facts."
            };
            var review = new DescriptorDraftReviewResult
            {
                TenantId = draft.TenantId,
                DraftId = draft.DraftId,
                ValidationResult = new DescriptorDraftValidationResult
                {
                    IsValid = true,
                    Diagnostics = [validationDiagnostic]
                },
                MaterializationResult = DescriptorDraftMaterializationResult.Success(descriptors),
                ProposedInventory = descriptors,
                TopologySnapshot = topology,
                ImpactAnalysisResult = impact,
                CompatibilityResult = compatibility,
                GovernanceDecision = governance,
                StableHashes = StableHashes(),
                PackagePreview = new DescriptorPackagePreview
                {
                    DescriptorIds = [eventDescriptor.Id, schema.Id, baseSchema.Id],
                    PackageManifestHash = Hash("package-manifest", "Manifest"),
                    PackageEvidenceHash = Hash("package-evidence", "Evidence"),
                    PackageEvidenceEnvelopeHash = Hash("package-envelope", "Envelope")
                },
                Diagnostics = [reviewDiagnostic],
                IsActivationEligible = true
            };
            var request = new DescriptorReviewReportBuildRequest
            {
                ReviewResult = review,
                Draft = draft,
                VisibilityApplied = true
            };

            var builder = CreateBuilder();
            var captured = DescriptorReviewReportInputSnapshot.Capture(request);
            var snapshotTypeInfo = DescriptorReviewReportInputSnapshotJsonSerializerContext.Default
                .DescriptorReviewReportInputSnapshot;
            var snapshotJson = JsonSerializer.Serialize(captured, snapshotTypeInfo);
            var restored = JsonSerializer.Deserialize(snapshotJson, snapshotTypeInfo);
            if (restored is null)
                return Fail("captured report input JSON deserialized to null");

            var baselineReport = builder.Build(request);
            var snapshotReport = builder.Build(restored);
            var reportTypeInfo = (JsonTypeInfo<DescriptorReviewReportDto>?)
                AgentControlPlaneToolJsonSerializerContext.Default.GetTypeInfo(typeof(DescriptorReviewReportDto));
            if (reportTypeInfo is null)
                return Fail("source-generated report DTO metadata was not available");
            var baselineJson = JsonSerializer.Serialize(baselineReport, reportTypeInfo);
            var snapshotReportJson = JsonSerializer.Serialize(snapshotReport, reportTypeInfo);
            if (!StringComparer.Ordinal.Equals(baselineJson, snapshotReportJson))
                return Fail("report output changed after generated snapshot JSON roundtrip");

            if (baselineReport.ProposedChangesSection.IsEmpty
                || baselineReport.DependencySummarySection.IsEmpty
                || baselineReport.ImpactAnalysisSection.IsEmpty
                || baselineReport.CompatibilitySection.IsEmpty
                || baselineReport.GovernanceSection.IsEmpty
                || baselineReport.PackagePreviewSection.IsEmpty
                || baselineReport.StableHashesSection.IsEmpty
                || !baselineJson.Contains("CapturedImpactCapability", StringComparison.Ordinal)
                || !baselineReport.CompatibilitySection.Items.Any(item =>
                    item.Parameters.TryGetValue("DescriptorId", out var descriptorId)
                    && StringComparer.Ordinal.Equals(descriptorId, schema.Id)
                    && item.Parameters.TryGetValue("Level", out var level)
                    && StringComparer.Ordinal.Equals(level, DescriptorCompatibilityLevel.Risky.ToString()))
                || !baselineJson.Contains("report-input-schema", StringComparison.Ordinal))
                return Fail("rich report output omitted a captured fact section");

            var failedReview = review with
            {
                ValidationResult = DescriptorDraftValidationResult.Failure(
                    Diagnostic("AOT-VALIDATION-ERROR", SeverityLevel.Error, "Captured failed review.")),
                MaterializationResult = null,
                ProposedInventory = null,
                TopologySnapshot = null,
                ImpactAnalysisResult = null,
                CompatibilityResult = null,
                GovernanceDecision = null,
                StableHashes = null,
                PackagePreview = null,
                Diagnostics = []
            };
            var failedRequest = request with { ReviewResult = failedReview };
            var failedSnapshot = DescriptorReviewReportInputSnapshot.Capture(failedRequest);
            var failedSnapshotJson = JsonSerializer.Serialize(failedSnapshot, snapshotTypeInfo);
            var failedRestored = JsonSerializer.Deserialize(failedSnapshotJson, snapshotTypeInfo);
            if (failedRestored is null || failedRestored.Materialization is not null
                || failedRestored.Topology is not null || failedRestored.Impact is not null
                || failedRestored.CompatibilityFindings is not null || failedRestored.GovernanceMaxDecision is not null
                || failedRestored.PackagePreview is not null || failedRestored.StableHashes is not null)
                return Fail("failed review did not preserve missing optional analysis");
            var failedBaseline = builder.Build(failedRequest);
            var failedAfterRoundTrip = builder.Build(failedRestored);
            if (!StringComparer.Ordinal.Equals(
                    JsonSerializer.Serialize(failedBaseline, reportTypeInfo),
                    JsonSerializer.Serialize(failedAfterRoundTrip, reportTypeInfo)))
                return Fail("failed report output changed after generated snapshot JSON roundtrip");

            var unsupportedVersionRejected = false;
            try
            {
                _ = builder.Build(restored with { Version = DescriptorReviewReportInputSnapshot.CurrentVersion + 1 });
            }
            catch (NotSupportedException)
            {
                unsupportedVersionRejected = true;
            }
            if (!unsupportedVersionRejected)
                return Fail("unsupported report input version was accepted");

            Console.WriteLine("CONTROL_PLANE_REPORT_INPUT_NATIVEAOT_OK");
            return true;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static DefaultDescriptorReviewReportBuilder CreateBuilder() => new(
        new DefaultDescriptorReviewMessageTemplateCatalog(
            null,
            NullLogger<DefaultDescriptorReviewMessageTemplateCatalog>.Instance),
        new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer()),
        new FixedTimeProvider(FixedNow));

    private static DescriptorDraftDiagnostic Diagnostic(DiagnosticCode code, SeverityLevel severity, string message) => new()
    {
        Code = code,
        Severity = severity,
        Message = message
    };

    private static DescriptorDraftDiagnostic Diagnostic(string code, SeverityLevel severity, string message) =>
        Diagnostic(new DiagnosticCode(code), severity, message);

    private static DescriptorStableHashes StableHashes() => new()
    {
        ContractHash = Hash("stable-contract", "Contract"),
        DefinitionHash = Hash("stable-definition", "Definition"),
        RuntimeHash = Hash("stable-runtime", "Runtime"),
        BindingHash = Hash("stable-binding", "Binding")
    };

    private static CanonicalHash Hash(string value, string purpose) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "aot-report-input-v1",
        ArtifactKind = "ReviewResult",
        Scope = "TenantVisible",
        Purpose = purpose,
        ContractVersion = "aot-report-input-v1",
        CanonicalShapeVersion = "aot-report-input-v1"
    };

    private static bool Fail(string message)
    {
        Console.Error.WriteLine($"FAIL [ReviewReportInputNativeAot]: {message}");
        return false;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
