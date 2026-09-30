using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.Agent.ControlPlane.Projections;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.DescriptorDraft.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using DraftCanonicalHashing = CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Tests;

public sealed class AgentReviewArtifactStoreTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task InMemoryStore_DetachesInputs_RejectsCollisions_AndOrdersLatestDeterministically()
    {
        var store = new InMemoryAgentReviewArtifactStore();
        var mutableDiagnostics = new List<DraftAbstractions.DescriptorDraftDiagnostic>();
        var first = CreateArtifact("review-a", DateTimeOffset.Parse("2026-09-24T01:00:00Z"));
        first = first with { ProjectedReview = first.ProjectedReview with { Diagnostics = mutableDiagnostics } };
        await store.InsertAsync(first);

        mutableDiagnostics.Add(new DraftAbstractions.DescriptorDraftDiagnostic
        {
            Code = new("CALLER_MUTATION"),
            Severity = SeverityLevel.Error,
            Message = "caller mutation"
        });

        var detached = await store.GetAsync(TestTenantId, "review-a");
        detached.Should().NotBeNull();
        detached!.ProjectedReview.Diagnostics.Should().BeEmpty();
        Func<Task> duplicateInsert = () => store.InsertAsync(first);
        await duplicateInsert.Should().ThrowAsync<InvalidOperationException>();

        await store.InsertAsync(CreateArtifact("review-b", DateTimeOffset.Parse("2026-09-24T02:00:00Z")));
        await store.InsertAsync(CreateArtifact("review-z", DateTimeOffset.Parse("2026-09-24T02:00:00Z")));
        await store.InsertAsync(CreateArtifact("other-tenant", DateTimeOffset.Parse("2026-09-24T03:00:00Z"), "tenant-other"));

        (await store.GetLatestAsync(TestTenantId, "draft-001"))!.ReviewResultId.Should().Be("review-z");
        (await store.GetAsync("tenant-other", "review-a")).Should().BeNull();
        (await store.ListAsync(TestTenantId, "draft-001")).Should().HaveCount(3);
    }

    [Fact]
    public async Task ArtifactValidation_RequiresSharedProjectedFacts_ButAllowsOriginalHashProjectionDifference()
    {
        var artifact = CreateArtifact("review-original");
        var originalInput = artifact.OriginalHashInput;
        var originalBinding = originalInput.SourceBinding;
        artifact = artifact with
        {
            OriginalHashInput = originalInput with
            {
                SourceBinding = originalBinding with
                {
                    IsActivationEligible = false,
                    Diagnostics = Array.AsReadOnly(new[]
                    {
                        new DraftCanonicalHashing.ReviewDiagnosticProjection { Code = "ORIGINAL_ONLY", Severity = "Warning" }
                    })
                }
            }
        };

        artifact.Validate();
        var realHashService = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        realHashService.ComputeSourceReviewHash(artifact.OriginalHashInput).Should()
            .NotBe(realHashService.ComputeSourceReviewHash(artifact.ReportInput.ToReviewHashInput()),
                "activation hashes must be derived from the original review input, not projected report facts");
        var invalidProjected = artifact with
        {
            ProjectedReview = artifact.ProjectedReview with { IsActivationEligible = false }
        };
        var act = () => invalidProjected.Validate();
        act.Should().Throw<ArgumentException>().WithMessage("*validity or eligibility*");

        var store = new InMemoryAgentReviewArtifactStore();
        await store.InsertAsync(artifact);
        var hashService = new Mock<DraftCanonicalHashing.IDescriptorDraftReviewHashService>();
        var expectedSource = CreateHash("original-source");
        var expectedManifest = CreateHash("original-manifest");
        DraftCanonicalHashing.DescriptorDraftReviewHashInput? sourceInputSeen = null;
        DraftCanonicalHashing.DescriptorDraftReviewHashInput? manifestInputSeen = null;
        hashService.Setup(x => x.ComputeSourceReviewHash(It.IsAny<DraftCanonicalHashing.DescriptorDraftReviewHashInput>()))
            .Callback<DraftCanonicalHashing.DescriptorDraftReviewHashInput>(input => sourceInputSeen = input)
            .Returns(expectedSource);
        hashService.Setup(x => x.ComputeReviewManifestHash(It.IsAny<DraftCanonicalHashing.DescriptorDraftReviewHashInput>()))
            .Callback<DraftCanonicalHashing.DescriptorDraftReviewHashInput>(input => manifestInputSeen = input)
            .Returns(expectedManifest);

        var resolver = new DefaultActivationBindingArtifactResolver(
            store,
            PackageArtifactStore,
            hashService.Object,
            new CrestCreates.Metadata.DescriptorPackage.DescriptorPackageSerializer(),
            new CrestCreates.Metadata.DescriptorPackage.CanonicalHashing.DefaultDescriptorPackageCanonicalHashComputer(
                new DefaultCanonicalHashComputer()));
        var resolved = await resolver.ResolveAsync(TestTenantId, new ActivationBindingSnapshot
        {
            TenantId = TestTenantId,
            DraftId = artifact.DraftId,
            DraftVersion = 1,
            ReviewResultId = artifact.ReviewResultId,
            PackagePreviewId = "missing-package",
            EvidencePreviewId = "missing-evidence",
            Hashes = CreateBindingHashes(),
            CreatedAt = DateTimeOffset.UtcNow
        });

        resolved.CurrentSourceReviewHash.Should().Be(expectedSource);
        resolved.CurrentReviewManifestHash.Should().Be(expectedManifest);
        sourceInputSeen.Should().BeEquivalentTo(artifact.OriginalHashInput);
        manifestInputSeen.Should().BeEquivalentTo(artifact.OriginalHashInput);
        sourceInputSeen!.SourceBinding.IsActivationEligible.Should().BeFalse();
        sourceInputSeen.SourceBinding.Diagnostics.Should().ContainSingle(d => d.Code == "ORIGINAL_ONLY");
    }

    private static AgentReviewArtifactEnvelope CreateArtifact(
        string reviewId,
        DateTimeOffset? createdAt = null,
        string tenantId = TestTenantId)
    {
        var owner = CreateTestDraft(tenantId: tenantId);
        var review = new DraftAbstractions.DescriptorDraftReviewResult
        {
            TenantId = tenantId,
            DraftId = owner.DraftId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true
        };
        var reportInput = DescriptorReviewReportInputSnapshot.Capture(new DescriptorReviewReportBuildRequest
        {
            ReviewResult = review,
            Draft = owner,
            VisibilityApplied = true
        });
        return new AgentReviewArtifactEnvelope
        {
            Version = AgentReviewArtifactEnvelope.CurrentVersion,
            TenantId = tenantId,
            ReviewResultId = reviewId,
            CreatedAt = createdAt ?? DateTimeOffset.Parse("2026-09-24T00:00:00Z"),
            ScopeFingerprint = "scope-fingerprint",
            ProjectedReview = AgentReviewResultDtoProjection.Project(review),
            ReportInput = reportInput,
            OriginalHashInput = DraftCanonicalHashing.DescriptorDraftReviewHashInput.Capture(review)
        };
    }

    private static CanonicalHash CreateHash(string value) => new()
    {
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = CanonicalHashArtifactNames.ReviewResult,
        Scope = CanonicalHashScopeNames.InternalFull,
        Purpose = CanonicalHashPurposeNames.SourceBinding,
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "test-review-hash-v1",
        Value = value
    };

    private static BindingHashes CreateBindingHashes() => new()
    {
        SourceReviewHash = CreateHash("source"),
        ReviewManifestHash = CreateHash("review-manifest"),
        PackageManifestHash = CreateHash("package-manifest"),
        PackageEvidenceHash = CreateHash("package-evidence"),
        PackageEvidenceEnvelopeHash = CreateHash("package-envelope"),
        ContractHash = CreateHash("contract"),
        DefinitionHash = CreateHash("definition")
    };
}
