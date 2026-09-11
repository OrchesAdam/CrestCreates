using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.DescriptorPackage;
using CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Bounded acceptance proof that package content is the content used by the
/// owning preview/evidence APIs. This exercises the real control-plane service,
/// draft materializer, source-generated canonical hash builders, and default
/// package builder. Draft storage, descriptor catalog, and review collaborators
/// remain fixture-local in-memory substitutes; this does not prove durable
/// artifact storage, approval, deployment, or host loading.
/// </summary>
public sealed class RetainedPackageContentBindingAcceptanceTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task ActualPreviewAndEvidenceDetectDefinitionSubstitution()
    {
        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(DescriptorKind.Capability)
            }
        };

        var canonicalHashComputer = new DefaultCanonicalHashComputer();
        var stableHashBuilder = new DescriptorStableHashBuilder(canonicalHashComputer);
        var packageHashComputer = new DefaultDescriptorPackageCanonicalHashComputer(canonicalHashComputer);
        var realPackageBuilder = new DefaultDescriptorPackageBuilder(stableHashBuilder, packageHashComputer);
        var capturingBuilder = new CapturingPackageBuilder(realPackageBuilder);

        var service = CreateService(options);

        // CreateService installs the base fixture's fixed hash setup. Replace it
        // on the same mock object with the production canonical implementation so
        // request-side and package-side hash observations share one authority.
        HashBuilderMock.Reset();
        HashBuilderMock
            .Setup(builder => builder.Build(It.IsAny<IDescriptor>()))
            .Returns((IDescriptor descriptor) => stableHashBuilder.Build(descriptor));

        PackageBuilderMock.Reset();
        PackageBuilderMock
            .Setup(builder => builder.Build(It.IsAny<DescriptorPackageBuildRequest>()))
            .Returns((DescriptorPackageBuildRequest request) => capturingBuilder.Build(request));

        Draft? createdDraft = null;
        DraftStoreMock
            .Setup(store => store.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Callback<Draft, CancellationToken>((saved, _) => createdDraft = saved)
            .Returns(Task.CompletedTask);

        var createResult = await service.CreateDescriptorDraftAsync(
            CreateContext(AgentToolName.CreateDescriptorDraft),
            new CreateDescriptorDraftRequest
            {
                DescriptorKind = DescriptorKind.Event,
                DescriptorId = "candidate.event",
                Operation = DescriptorDraftOperation.Create,
                Payload = CreateTestPayloadDto(DescriptorKind.Event, "candidate.event", "TestDraft"),
                ProposedVersion = "1",
                Intent = "Retain package content binding"
            });
        createResult.Status.Should().Be(AgentToolResultStatus.Success);
        createdDraft.Should().NotBeNull();

        var draft = createdDraft!;
        var proposedVersion = int.Parse(draft.ProposedVersion!, System.Globalization.CultureInfo.InvariantCulture);
        var visibleCatalogEvent = new EventDescriptor
        {
            Id = "catalog.event",
            Name = "CatalogEvent",
            Version = 1,
            State = DescriptorState.Active,
            Category = EventCategory.Domain,
            Semantic = EventSemantic.Fact,
            Importance = EventImportance.Operational,
            ChangeKind = SchemaChangeKind.Additive
        };
        var deniedCatalogCapability = new CapabilityDescriptor
        {
            Id = "catalog.capability",
            Name = "DeniedCapability",
            Version = 1,
            State = DescriptorState.Active,
            CapabilityKind = CapabilityKind.Command,
            RiskLevel = CapabilityRiskLevel.Low
        };

        DraftStoreMock
            .Setup(store => store.GetAsync(TestTenantId, draft.DraftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        DescriptorCatalogMock
            .Setup(catalog => catalog.GetAll())
            .Returns([visibleCatalogEvent, deniedCatalogCapability]);

        var realMaterializer = new DefaultDescriptorDraftMaterializer();
        IReadOnlyList<IDescriptor>? materializerInput = null;
        DraftMaterializerMock
            .Setup(materializer => materializer.Materialize(
                It.IsAny<Draft>(), It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns((Draft materializedDraft, IReadOnlyList<IDescriptor> currentInventory) =>
            {
                materializerInput = currentInventory;
                return realMaterializer.Materialize(materializedDraft, currentInventory);
            });

        var preview = await service.PreviewDescriptorPackageAsync(
            CreateContext(AgentToolName.PreviewDescriptorPackage), draft.DraftId);

        preview.Status.Should().Be(AgentToolResultStatus.Success,
            string.Join(" | ", preview.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        preview.Value.Should().NotBeNull();
        materializerInput.Should().ContainSingle(
            descriptor => descriptor.Id == visibleCatalogEvent.Id,
            "the denied Capability descriptor must be filtered before authoring materialization");
        materializerInput.Should().NotContain(descriptor => descriptor.Id == deniedCatalogCapability.Id);

        capturingBuilder.Requests.Should().ContainSingle();
        var capturedRequest = capturingBuilder.Requests.Single();
        var capturedPackage = capturingBuilder.Packages.Single();
        capturedRequest.Descriptors.Should().Contain(descriptor => descriptor.Id == visibleCatalogEvent.Id);
        var candidate = capturedRequest.Descriptors.Single(descriptor =>
            descriptor.Kind == draft.DescriptorKind && descriptor.Name == "TestDraft");
        candidate.Id.Should().Be(draft.DescriptorId,
            $"the real CreateDescriptorDraftAsync path saved payload id '{candidate.Id}', payload version '{(candidate as IVersionedDescriptor)?.Version}' while request/draft identity was '{draft.DescriptorId}' v'{draft.ProposedVersion}'");
        candidate.Should().BeAssignableTo<IVersionedDescriptor>().Which.Version.Should().Be(
            proposedVersion,
            $"the real CreateDescriptorDraftAsync path saved payload version '{(candidate as IVersionedDescriptor)?.Version}' while ProposedVersion was '{draft.ProposedVersion}'");
        capturedRequest.Descriptors.Should().NotContain(descriptor => descriptor.Id == deniedCatalogCapability.Id);
        capturedPackage.Hashes.Should().NotBeNull();

        var packagePreviewId = InMemoryAuditor
            .GetRecordsByToolName(AgentToolName.PreviewDescriptorPackage)
            .Single(record => record.TouchedPackagePreviewIds is { Count: 1 })
            .TouchedPackagePreviewIds![0];
        var storedPreview = await service.GetPackagePreviewAsync(
            CreateContext(AgentToolName.GetPackagePreview), packagePreviewId);
        storedPreview.Status.Should().Be(AgentToolResultStatus.Success);

        var evidence = await service.BuildPackageEvidencePreviewAsync(
            CreateContext(AgentToolName.BuildPackageEvidencePreview), draft.DraftId);
        evidence.Status.Should().Be(AgentToolResultStatus.Success);
        evidence.Value.Should().NotBeNull();

        var evidencePreviewId = InMemoryAuditor
            .GetRecordsByToolName(AgentToolName.BuildPackageEvidencePreview)
            .Single(record => record.TouchedPackagePreviewIds is { Count: 1 })
            .TouchedPackagePreviewIds![0];

        var claimedHashes = capturedPackage.Hashes!;
        var packageHashes = InMemoryArtifactResolver.GetPackageHashSet(TestTenantId, packagePreviewId);
        var evidenceHashes = InMemoryArtifactResolver.GetEvidenceHashSet(TestTenantId, evidencePreviewId);
        packageHashes.Should().BeEquivalentTo(claimedHashes);
        evidenceHashes.Should().BeEquivalentTo(claimedHashes);
        storedPreview.Value!.PackageManifestHash.Should().Be(claimedHashes.PackageManifestHash);
        storedPreview.Value.PackageEvidenceHash.Should().Be(claimedHashes.PackageEvidenceHash);
        storedPreview.Value.PackageEvidenceEnvelopeHash.Should().Be(claimedHashes.PackageEvidenceEnvelopeHash);
        evidence.Value!.PackagePreview.PackageManifestHash.Should().Be(claimedHashes.PackageManifestHash);
        evidence.Value.PackagePreview.PackageEvidenceHash.Should().Be(claimedHashes.PackageEvidenceHash);
        evidence.Value.PackagePreview.PackageEvidenceEnvelopeHash.Should().Be(claimedHashes.PackageEvidenceEnvelopeHash);

        var rebuiltOriginal = realPackageBuilder.Build(capturedRequest);
        rebuiltOriginal.Hashes.Should().BeEquivalentTo(claimedHashes,
            "the retained request must reproduce all three claimed package hashes");

        candidate = capturedRequest.Descriptors.Single(descriptor => descriptor.Kind == draft.DescriptorKind && descriptor.Name == "TestDraft");
        var candidateVersion = candidate.Should().BeAssignableTo<IVersionedDescriptor>().Subject;
        candidateVersion.Version.Should().Be(proposedVersion);
        var candidateManifestEntry = capturedPackage.Manifest.DescriptorEntries
            .Single(entry => entry.Ref.Id == draft.DescriptorId && entry.Ref.Version == proposedVersion);
        var candidateEvent = candidate.Should().BeOfType<EventDescriptor>().Subject;
        var replacement = new EventDescriptor
        {
            Id = candidateEvent.Id,
            Name = "CandidateEventReplaced",
            State = candidateEvent.State,
            SupersededById = candidateEvent.SupersededById,
            Version = candidateEvent.Version,
            PayloadSchema = candidateEvent.PayloadSchema,
            Category = candidateEvent.Category,
            Semantic = candidateEvent.Semantic,
            Importance = candidateEvent.Importance,
            ChangeKind = candidateEvent.ChangeKind
        };
        var substitutedRequest = capturedRequest with
        {
            Descriptors = capturedRequest.Descriptors
                .Select(descriptor => ReferenceEquals(descriptor, candidate) ? replacement : descriptor)
                .ToList()
                .AsReadOnly()
        };

        var rebuiltSubstitution = realPackageBuilder.Build(substitutedRequest);
        rebuiltSubstitution.Hashes.Should().NotBeEquivalentTo(claimedHashes,
            "the claimed hashes must reject a changed retained descriptor definition");
        rebuiltSubstitution.Manifest.DescriptorEntries
            .Single(entry => entry.Ref == candidateManifestEntry.Ref)
            .DefinitionHash
            .Should().NotBe(candidateManifestEntry.DefinitionHash);
    }

    private sealed class CapturingPackageBuilder : IDescriptorPackageBuilder
    {
        private readonly IDescriptorPackageBuilder _inner;

        public List<DescriptorPackageBuildRequest> Requests { get; } = [];
        public List<DescriptorPackage> Packages { get; } = [];

        public CapturingPackageBuilder(IDescriptorPackageBuilder inner)
            => _inner = inner;

        public DescriptorPackage Build(DescriptorPackageBuildRequest request)
        {
            Requests.Add(request);
            var package = _inner.Build(request);
            Packages.Add(package);
            return package;
        }
    }
}
