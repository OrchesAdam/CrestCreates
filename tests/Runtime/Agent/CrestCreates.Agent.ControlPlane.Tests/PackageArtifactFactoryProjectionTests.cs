using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Agent.ControlPlane.PackageArtifacts;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.Evidence;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

public sealed class PackageArtifactFactoryProjectionTests : AgentControlPlaneTestBase
{
    [Fact]
    public void CreateProjectedPair_UsesExactNarrowScopeForPreviewAndEvidence()
    {
        var owner = CreateTestDraft(kind: DescriptorKind.Event, draftId: "draft-narrow-capture");
        var visible = CreateTestDescriptor("ns", "visible-event", DescriptorKind.Event);
        var denied = CreateTestDescriptor("ns", "hidden-capability", DescriptorKind.Capability);
        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var evidence = new DescriptorPackageEvidence
        {
            NormalizedFindings = new[]
            {
                new EvidenceFinding
                {
                    Source = "impact",
                    Code = new DiagnosticCode("VISIBLE"),
                    Severity = SeverityLevel.Warning,
                    Subject = new DescriptorRef(visible.Namespace, visible.Id),
                    Message = "Visible finding"
                },
                new EvidenceFinding
                {
                    Source = "impact",
                    Code = new DiagnosticCode("DENIED"),
                    Severity = SeverityLevel.Error,
                    Subject = new DescriptorRef(denied.Namespace, denied.Id),
                    Message = "Denied finding"
                }
            },
            PackageFindingCount = 2,
            BreakingFindingCount = 1,
            RequiresReview = true
        };
        var package = BuildValidTestPackage(new CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackageBuildRequest
        {
            PackageId = owner.DraftId,
            PackageVersion = owner.ProposedVersion ?? "1",
            CreatedBy = owner.AuthorId,
            Source = owner.Source,
            CreatedAt = owner.CreatedAt,
            Descriptors = new[] { visible }
        }, evidence);

        var pair = PackageArtifactFactory.CreateProjectedPair(
            "pkg-preview-narrow",
            "evidence-preview-narrow",
            DateTimeOffset.UtcNow,
            owner,
            options,
            new[] { visible, denied },
            package);

        pair.Package.ScopeFingerprint.Should().Be(AgentPackageArtifactFactory.ComputeVisibilityScopeFingerprint(options));
        pair.Package.VisibleCatalogFingerprint.Should().Be(
            AgentPackageArtifactFactory.ComputeVisibleCatalogFingerprint(new[] { visible }));
        pair.Package.ProjectedPreview.DescriptorIds.Should().ContainSingle().Which.Should().Be(visible.Id);
        pair.Evidence.PackagePreviewId.Should().Be(pair.Package.PackagePreviewId);
        pair.Evidence.ProjectedEvidence.Evidence.NormalizedFindings.Should().ContainSingle()
            .Which.Code.Value.Should().Be("VISIBLE");
        pair.Evidence.ProjectedEvidence.Evidence.PackageFindingCount.Should().Be(1);
        pair.Evidence.ProjectedEvidence.Evidence.RequiresReview.Should().BeFalse();
    }

    [Fact]
    public void CreateProjectedPair_RejectsCanonicalPackageContentOutsideScope()
    {
        var owner = CreateTestDraft(kind: DescriptorKind.Event, draftId: "draft-hidden-package");
        var visible = CreateTestDescriptor("ns", "visible-event", DescriptorKind.Event);
        var denied = CreateTestDescriptor("ns", "hidden-capability", DescriptorKind.Capability);
        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var package = BuildValidTestPackage(new CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackageBuildRequest
        {
            PackageId = owner.DraftId,
            PackageVersion = owner.ProposedVersion ?? "1",
            CreatedBy = owner.AuthorId,
            Source = owner.Source,
            CreatedAt = owner.CreatedAt,
            Descriptors = new[] { visible, denied }
        }, new DescriptorPackageEvidence());

        var act = () => PackageArtifactFactory.CreateProjectedPair(
            "pkg-preview-hidden",
            "evidence-preview-hidden",
            DateTimeOffset.UtcNow,
            owner,
            options,
            new[] { visible, denied },
            package);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("The package contains descriptors outside the capture visibility scope.");
    }

    [Fact]
    public void CreateProjectedPair_AllowsVisibleProposedDescriptorAbsentFromExactCatalog()
    {
        var owner = CreateTestDraft(kind: DescriptorKind.Event, draftId: "draft-new-descriptor");
        var proposed = CreateTestDescriptor("new", "proposed-event", DescriptorKind.Event);
        var package = BuildValidTestPackage(new DescriptorPackageBuildRequest
        {
            PackageId = owner.DraftId,
            PackageVersion = owner.ProposedVersion ?? "1",
            CreatedBy = owner.AuthorId,
            Source = owner.Source,
            CreatedAt = owner.CreatedAt,
            Descriptors = new[] { proposed }
        }, new DescriptorPackageEvidence());

        var pair = PackageArtifactFactory.CreateProjectedPair(
            "pkg-preview-new",
            "evidence-preview-new",
            DateTimeOffset.UtcNow,
            owner,
            AgentToolAuthorizationOptions.DevelopmentDefaults,
            Array.Empty<IDescriptor>(),
            package);

        pair.Package.ProjectedPreview.DescriptorIds.Should().ContainSingle().Which.Should().Be(proposed.Id);
        pair.Package.VisibleCatalogFingerprint.Should().Be("empty");
    }
}
