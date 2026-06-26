using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using CrestCreates.DescriptorDraft.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DescriptorDraft.Tests.CanonicalHashing;

public sealed class DescriptorDraftReviewHashServiceTests
{
    [Fact]
    public void ComputeSourceReviewHash_Uses_ReviewResult_SourceBinding_Metadata()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var review = CreateReview();

        var hash = service.ComputeSourceReviewHash(review);

        hash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.ReviewResult);
        hash.Purpose.Should().Be(CanonicalHashPurposeNames.SourceBinding);
        hash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
        hash.Algorithm.Should().Be("SHA-256");
        hash.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");
        hash.CanonicalShapeVersion.Should().Be(DescriptorDraftReviewCanonicalShapeVersions.SourceBindingV1);
        hash.ContractVersion.Should().NotBeNullOrWhiteSpace();
        hash.Value.Should().NotBeEmpty();
    }

    [Fact]
    public void ComputeReviewManifestHash_Uses_ReviewResult_Integrity_Metadata()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());

        var hash = service.ComputeReviewManifestHash(CreateReview());

        hash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.ReviewResult);
        hash.Purpose.Should().Be(CanonicalHashPurposeNames.Integrity);
        hash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
        hash.CanonicalShapeVersion.Should().Be(DescriptorDraftReviewCanonicalShapeVersions.IntegrityV1);
        hash.Value.Should().NotBeEmpty();
    }

    [Fact]
    public void ComputeSourceReviewHash_Is_Deterministic()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var review = CreateReview();

        var first = service.ComputeSourceReviewHash(review);
        var second = service.ComputeSourceReviewHash(review);

        first.Value.Should().Be(second.Value);
    }

    [Fact]
    public void ComputeReviewManifestHash_Is_Deterministic()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var review = CreateReview();

        var first = service.ComputeReviewManifestHash(review);
        var second = service.ComputeReviewManifestHash(review);

        first.Value.Should().Be(second.Value);
    }

    [Fact]
    public void SourceBinding_And_Integrity_Produce_Different_Hashes()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var review = CreateReview();

        var sourceBinding = service.ComputeSourceReviewHash(review);
        var integrity = service.ComputeReviewManifestHash(review);

        sourceBinding.Value.Should().NotBe(integrity.Value);
    }

    // ── Source binding sensitivity tests ─────────────────────────────

    [Fact]
    public void ComputeSourceReviewHash_DifferentTenantId_ProducesDifferentHash()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var r1 = CreateReview();
        var r2 = CreateReview() with { TenantId = "tenant-2" };

        var h1 = service.ComputeSourceReviewHash(r1);
        var h2 = service.ComputeSourceReviewHash(r2);

        h1.Value.Should().NotBe(h2.Value);
    }

    [Fact]
    public void ComputeSourceReviewHash_DifferentDraftId_ProducesDifferentHash()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var r1 = CreateReview();
        var r2 = CreateReview() with { DraftId = "draft-2" };

        var h1 = service.ComputeSourceReviewHash(r1);
        var h2 = service.ComputeSourceReviewHash(r2);

        h1.Value.Should().NotBe(h2.Value);
    }

    [Fact]
    public void ComputeSourceReviewHash_DifferentIsActivationEligible_ProducesDifferentHash()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var r1 = CreateReview();
        var r2 = CreateReview() with { IsActivationEligible = false };

        var h1 = service.ComputeSourceReviewHash(r1);
        var h2 = service.ComputeSourceReviewHash(r2);

        h1.Value.Should().NotBe(h2.Value);
    }

    [Fact]
    public void ComputeSourceReviewHash_DifferentIsValid_ProducesDifferentHash()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var r1 = CreateReview();
        var r2 = CreateReview() with
        {
            ValidationResult = new DescriptorDraftValidationResult { IsValid = false, Diagnostics = [] }
        };

        var h1 = service.ComputeSourceReviewHash(r1);
        var h2 = service.ComputeSourceReviewHash(r2);

        h1.Value.Should().NotBe(h2.Value);
    }

    [Fact]
    public void ComputeSourceReviewHash_AddedDiagnostic_ProducesDifferentHash()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var r1 = CreateReview();
        var r2 = CreateReview() with
        {
            Diagnostics = new List<DescriptorDraftDiagnostic>
            {
                new() { Code = "ERR-01", Severity = DescriptorDraftDiagnosticSeverity.Error, Message = "msg" }
            }
        };

        var h1 = service.ComputeSourceReviewHash(r1);
        var h2 = service.ComputeSourceReviewHash(r2);

        h1.Value.Should().NotBe(h2.Value);
    }

    [Fact]
    public void ComputeSourceReviewHash_DiagnosticOrderChange_ProducesSameHash()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var r1 = CreateReview() with
        {
            Diagnostics = new List<DescriptorDraftDiagnostic>
            {
                new() { Code = "ERR-01", Severity = DescriptorDraftDiagnosticSeverity.Error, Message = "msg" },
                new() { Code = "WARN-01", Severity = DescriptorDraftDiagnosticSeverity.Warning, Message = "msg" }
            }
        };
        var r2 = CreateReview() with
        {
            Diagnostics = new List<DescriptorDraftDiagnostic>
            {
                new() { Code = "WARN-01", Severity = DescriptorDraftDiagnosticSeverity.Warning, Message = "msg" },
                new() { Code = "ERR-01", Severity = DescriptorDraftDiagnosticSeverity.Error, Message = "msg" }
            }
        };

        var h1 = service.ComputeSourceReviewHash(r1);
        var h2 = service.ComputeSourceReviewHash(r2);

        h1.Value.Should().Be(h2.Value,
            "canonical sorting of diagnostics must produce the same hash regardless of input order");
    }

    // ── Integrity sensitivity tests ──────────────────────────────────

    [Fact]
    public void ComputeReviewManifestHash_DifferentTenantId_ProducesDifferentHash()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var r1 = CreateReview();
        var r2 = CreateReview() with { TenantId = "tenant-2" };

        var h1 = service.ComputeReviewManifestHash(r1);
        var h2 = service.ComputeReviewManifestHash(r2);

        h1.Value.Should().NotBe(h2.Value);
    }

    [Fact]
    public void ComputeReviewManifestHash_DifferentDiagnosticCount_ProducesDifferentHash()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var r1 = CreateReview();
        var r2 = CreateReview() with
        {
            Diagnostics = new List<DescriptorDraftDiagnostic>
            {
                new() { Code = "ERR-01", Severity = DescriptorDraftDiagnosticSeverity.Error, Message = "msg" },
                new() { Code = "WARN-01", Severity = DescriptorDraftDiagnosticSeverity.Warning, Message = "msg" }
            }
        };

        var h1 = service.ComputeReviewManifestHash(r1);
        var h2 = service.ComputeReviewManifestHash(r2);

        h1.Value.Should().NotBe(h2.Value);
    }

    private static DescriptorDraftReviewResult CreateReview() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-1",
        IsActivationEligible = true,
        ValidationResult = new DescriptorDraftValidationResult { IsValid = true, Diagnostics = [] },
        Diagnostics = Array.Empty<DescriptorDraftDiagnostic>()
    };
}
