using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.Agent.ControlPlane.PackageArtifacts;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.Evidence;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorPackage;
using CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;
using FluentAssertions;
using Moq;
using Xunit;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftPackagePreview = CrestCreates.DescriptorDraft.Abstractions.DescriptorPackagePreview;

namespace CrestCreates.Agent.ControlPlane.Tests;

public sealed class PackageArtifactStoreRuntimeTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task InMemoryStore_DetachesMutableInputsAndReturnedGraphs()
    {
        var owner = CreateTestDraft(draftId: "draft-detached");
        var artifacts = CreateArtifacts(owner, "package-detached", "evidence-detached", includeDescriptor: true);
        await PackageArtifactStore.InsertPackageAndEvidenceAsync(artifacts.Package, artifacts.Evidence);

        artifacts.DescriptorIds[0] = "mutated-input-id";
        artifacts.Findings[0] = CreateFinding("mutated-input-message");

        var packageKey = new AgentPackageArtifactKey(TestTenantId, "package-detached");
        var evidenceKey = new AgentEvidenceArtifactKey(TestTenantId, "evidence-detached");
        var firstPackageRead = (await PackageArtifactStore.GetPackageAsync(packageKey))!;
        var firstEvidenceRead = (await PackageArtifactStore.GetEvidenceAsync(evidenceKey))!;
        ((IList<string>)firstPackageRead.ProjectedPreview.DescriptorIds)[0] = "mutated-output-id";
        ((IList<EvidenceFinding>)firstEvidenceRead.ProjectedEvidence.Evidence.NormalizedFindings)[0] =
            CreateFinding("mutated-output-message");

        var secondPackageRead = (await PackageArtifactStore.GetPackageAsync(packageKey))!;
        var secondEvidenceRead = (await PackageArtifactStore.GetEvidenceAsync(evidenceKey))!;
        secondPackageRead.ProjectedPreview.DescriptorIds.Should().ContainSingle().Which.Should().Be("descriptor-detached");
        secondEvidenceRead.ProjectedEvidence.Evidence.NormalizedFindings.Should().ContainSingle()
            .Which.Message.Should().Be("original-evidence");
    }

    [Fact]
    public async Task InMemoryStore_EvidenceCollisionDoesNotLeaveNewPackage()
    {
        var owner = CreateTestDraft(draftId: "draft-atomic-collision");
        var existing = CreateArtifacts(owner, "package-existing", "evidence-collision");
        var attempted = CreateArtifacts(owner, "package-must-rollback", "evidence-collision");
        await PackageArtifactStore.InsertPackageAndEvidenceAsync(existing.Package, existing.Evidence);

        var act = () => PackageArtifactStore.InsertPackageAndEvidenceAsync(attempted.Package, attempted.Evidence);
        await act.Should().ThrowAsync<InvalidOperationException>();

        (await PackageArtifactStore.GetPackageAsync(
            new AgentPackageArtifactKey(TestTenantId, "package-must-rollback"))).Should().BeNull();
    }

    [Fact]
    public async Task SubmitActivationRequest_RejectsWrongScopePairBeforeActivationRequestCreation()
    {
        var (service, reviewResultId, _, _) = await CreateServiceWithFullBindingArtifacts();
        var owner = CreateTestDraft(draftId: "draft-001");
        var wrongScopePair = CreateArtifacts(owner, "package-wrong-scope", "evidence-wrong-scope", scope: "other-scope");
        await PackageArtifactStore.InsertPackageAndEvidenceAsync(wrongScopePair.Package, wrongScopePair.Evidence);

        var request = new SubmitActivationRequestRequest
        {
            DraftId = owner.DraftId,
            BindingSnapshot = CreateBindingSnapshot(owner.DraftId, reviewResultId, "package-wrong-scope", "evidence-wrong-scope")
        };
        var result = await service.SubmitActivationRequestAsync(CreateContext("SubmitActivationRequest"), request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == DescriptorActivationDiagnosticCodes.PackagePreviewScopeMismatch);
        result.Diagnostics.Should().Contain(d => d.Code == DescriptorActivationDiagnosticCodes.EvidencePreviewScopeMismatch);
        ActivationRequestServiceMock.Verify(service => service.CreateActivationRequestAsync(
            It.IsAny<AgentToolInvocationContext>(),
            It.IsAny<SubmitActivationRequestRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolver_PropagatesPackageProviderFailureWithoutFallback()
    {
        var packageStore = new Mock<IAgentPackageArtifactStore>();
        packageStore.Setup(store => store.GetPackageAsync(
                It.IsAny<AgentPackageArtifactKey>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider unavailable"));
        var canonicalHashComputer = new DefaultCanonicalHashComputer();
        var packageHashComputer = new DefaultDescriptorPackageCanonicalHashComputer(canonicalHashComputer);
        var resolver = new DefaultActivationBindingArtifactResolver(
            ReviewArtifactStore,
            packageStore.Object,
            ReviewHashServiceMock.Object,
            new DescriptorPackageSerializer(),
            packageHashComputer);

        var act = () => resolver.ResolveAsync(TestTenantId,
            CreateBindingSnapshot("draft-provider-failure", "missing-review", "provider-package", ""));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("provider unavailable");
        packageStore.Verify(store => store.GetPackageAsync(
            It.IsAny<AgentPackageArtifactKey>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private (AgentPackageArtifactEnvelope Package, AgentEvidenceArtifactEnvelope Evidence,
        List<string> DescriptorIds, List<EvidenceFinding> Findings) CreateArtifacts(
        Draft owner,
        string packageId,
        string evidenceId,
        bool includeDescriptor = false,
        string scope = "test-scope")
    {
        var descriptor = CreateTestDescriptor("test", "descriptor-detached", DescriptorKind.Event);
        var descriptors = includeDescriptor ? new IDescriptor[] { descriptor } : Array.Empty<IDescriptor>();
        var content = BuildValidTestPackage(new DescriptorPackageBuildRequest
        {
            PackageId = owner.DraftId,
            PackageVersion = owner.ProposedVersion ?? "1",
            CreatedBy = owner.AuthorId,
            Source = owner.Source,
            CreatedAt = owner.CreatedAt,
            Descriptors = descriptors
        }, new DescriptorPackageEvidence());
        var ids = includeDescriptor ? new List<string> { descriptor.Id } : new List<string>();
        var preview = new DraftPackagePreview
        {
            PackageManifestHash = content.Hashes!.PackageManifestHash,
            PackageEvidenceHash = content.Hashes.PackageEvidenceHash,
            PackageEvidenceEnvelopeHash = content.Hashes.PackageEvidenceEnvelopeHash,
            DescriptorIds = ids
        };
        var package = PackageArtifactFactory.CreatePackage(
            packageId, DateTimeOffset.UtcNow, owner, scope, "test-catalog", preview, content);
        var findings = new List<EvidenceFinding> { CreateFinding("original-evidence") };
        var projectedEvidence = new PackageEvidencePreview
        {
            DraftId = owner.DraftId,
            TenantId = owner.TenantId,
            PackagePreview = preview,
            Evidence = new DescriptorPackageEvidence { NormalizedFindings = findings },
            Diagnostics = Array.Empty<AgentToolDiagnostic>()
        };
        var evidence = PackageArtifactFactory.CreateEvidence(evidenceId, DateTimeOffset.UtcNow, package, projectedEvidence);
        return (package, evidence, ids, findings);
    }

    private static EvidenceFinding CreateFinding(string message) => new()
    {
        Source = "impact",
        Code = new DiagnosticCode("FINDING"),
        Severity = SeverityLevel.Warning,
        Subject = new DescriptorRef("test", "descriptor-detached"),
        Message = message
    };

    private static ActivationBindingSnapshot CreateBindingSnapshot(
        string draftId, string reviewResultId, string packagePreviewId, string evidencePreviewId) => new()
    {
        TenantId = TestTenantId,
        DraftId = draftId,
        DraftVersion = 1,
        ReviewResultId = reviewResultId,
        PackagePreviewId = packagePreviewId,
        EvidencePreviewId = evidencePreviewId,
        Hashes = new BindingHashes
        {
            SourceReviewHash = TestHash("source"),
            ReviewManifestHash = TestHash("review"),
            PackageManifestHash = TestHash("manifest"),
            PackageEvidenceHash = TestHash("evidence"),
            PackageEvidenceEnvelopeHash = TestHash("envelope"),
            ContractHash = TestHash("contract"),
            DefinitionHash = TestHash("definition")
        },
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static CanonicalHash TestHash(string value) => new()
    {
        Algorithm = "SHA-256",
        AlgorithmVersion = "test-v1",
        ArtifactKind = "test",
        Scope = "test",
        Purpose = "test",
        ContractVersion = "test-v1",
        CanonicalShapeVersion = "test-v1",
        Value = value
    };
}
