using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using CrestCreates.DescriptorDraft.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using System.Text.Json;
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
        hash.CanonicalShapeVersion.Should().Be(DescriptorDraftReviewCanonicalShapeVersions.SourceBindingV2);
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
        hash.CanonicalShapeVersion.Should().Be(DescriptorDraftReviewCanonicalShapeVersions.IntegrityV2);
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
    public void HashInput_Computations_Preserve_Preexisting_V2_Digest_Values()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var input = service.CaptureInput(CreateReview());

        service.ComputeSourceReviewHash(input).Value.Should()
            .Be("f60d35fe1819288f66a980da68dc45e3889627c645fb443a0fbf1de4ccb7acd2");
        service.ComputeReviewManifestHash(input).Value.Should()
            .Be("be9a5f2d978704c9205528bdbb5391c8b13b70579c34a75423073777dc216cec");
    }

    [Fact]
    public void HashInput_SourceGeneratedJson_RoundTrips_AndComputesSameHashes()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var review = CreateReview() with
        {
            IsActivationEligible = false,
            ValidationResult = new DescriptorDraftValidationResult { IsValid = false, Diagnostics = [] },
            Diagnostics = new List<DescriptorDraftDiagnostic>
            {
                new() { Code = new DiagnosticCode("ERR-01"), Severity = SeverityLevel.Error, Message = "blocked" },
                new() { Code = new DiagnosticCode("WARN-01"), Severity = SeverityLevel.Warning, Message = "review" }
            }
        };
        var input = service.CaptureInput(review);
        var typeInfo = DescriptorDraftReviewHashInputJsonSerializerContext.Default.DescriptorDraftReviewHashInput;
        var json = JsonSerializer.Serialize(input, typeInfo);
        var restored = JsonSerializer.Deserialize(json, typeInfo);

        restored.Should().NotBeNull();
        restored!.Version.Should().Be(input.Version);
        restored.SourceBinding.Should().BeEquivalentTo(input.SourceBinding);
        restored.SourceBinding.IsActivationEligible.Should().BeFalse();
        restored.SourceBinding.IsValid.Should().BeFalse();
        restored.SourceBinding.Diagnostics.Should().HaveCount(2);
        restored.SourceBinding.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Severity == "Error");
        restored.SourceBinding.GovernanceDecision.Should().BeNull();
        restored.SourceBinding.ImpactSeverity.Should().BeNull();
        service.ComputeSourceReviewHash(restored).Value.Should().Be(service.ComputeSourceReviewHash(input).Value);
        service.ComputeReviewManifestHash(restored).Value.Should().Be(service.ComputeReviewManifestHash(input).Value);
    }

    [Fact]
    public void CaptureInput_Is_Isolated_From_Later_DiagnosticCollection_Mutation()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var diagnostics = new List<DescriptorDraftDiagnostic>
        {
            new() { Code = new DiagnosticCode("ERR-01"), Severity = SeverityLevel.Error, Message = "first" }
        };
        var review = CreateReview() with { Diagnostics = diagnostics };
        var captured = service.CaptureInput(review);
        var expected = service.CaptureInput(CreateReview() with { Diagnostics = diagnostics.ToArray() });

        diagnostics.Add(new DescriptorDraftDiagnostic
        {
            Code = new DiagnosticCode("ERR-02"), Severity = SeverityLevel.Error, Message = "later"
        });

        service.ComputeSourceReviewHash(captured).Value.Should().Be(service.ComputeSourceReviewHash(expected).Value);
        service.ComputeReviewManifestHash(captured).Value.Should().Be(service.ComputeReviewManifestHash(expected).Value);
    }

    [Fact]
    public void HashInput_Computations_Reject_Unsupported_Version_And_Malformed_Required_Fields()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var input = service.CaptureInput(CreateReview());

        var unsupported = input with { Version = DescriptorDraftReviewHashInput.CurrentVersion + 1 };
        var unsupportedCall = () => service.ComputeSourceReviewHash(unsupported);
        unsupportedCall.Should().Throw<NotSupportedException>();

        var malformedProjection = input.SourceBinding with { Diagnostics = null! };
        var malformed = input with { SourceBinding = malformedProjection };
        var malformedCall = () => service.ComputeReviewManifestHash(malformed);
        malformedCall.Should().Throw<ArgumentNullException>();

        var missingDiagnosticField = input.SourceBinding with
        {
            Diagnostics = [new ReviewDiagnosticProjection { Code = "ERR-01", Severity = null! }]
        };
        var malformedDiagnostic = input with { SourceBinding = missingDiagnosticField };
        var diagnosticCall = () => service.ComputeSourceReviewHash(malformedDiagnostic);
        diagnosticCall.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Captured_Original_Inputs_Preserve_Hashes_When_Visible_Review_Is_Filtered()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var original = CreateReview() with
        {
            Diagnostics = new List<DescriptorDraftDiagnostic>
            {
                new() { Code = new DiagnosticCode("ERR-01"), Severity = SeverityLevel.Error, Message = "first" },
                new() { Code = new DiagnosticCode("ERR-02"), Severity = SeverityLevel.Warning, Message = "second" }
            }
        };
        var originalInput = service.CaptureInput(original);
        var visibleReview = original with
        {
            IsActivationEligible = false,
            Diagnostics = original.Diagnostics.Take(1).ToArray()
        };
        var visibleInput = service.CaptureInput(visibleReview);

        service.ComputeSourceReviewHash(originalInput).Value.Should().Be(service.ComputeSourceReviewHash(original).Value);
        service.ComputeReviewManifestHash(originalInput).Value.Should().Be(service.ComputeReviewManifestHash(original).Value);
        service.ComputeSourceReviewHash(originalInput).Value.Should().NotBe(service.ComputeSourceReviewHash(visibleInput).Value);
        service.ComputeReviewManifestHash(originalInput).Value.Should().NotBe(service.ComputeReviewManifestHash(visibleInput).Value);
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
                new() { Code = new DiagnosticCode("ERR-01"), Severity = SeverityLevel.Error, Message = "msg" }
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
                new() { Code = new DiagnosticCode("ERR-01"), Severity = SeverityLevel.Error, Message = "msg" },
                new() { Code = new DiagnosticCode("WARN-01"), Severity = SeverityLevel.Warning, Message = "msg" }
            }
        };
        var r2 = CreateReview() with
        {
            Diagnostics = new List<DescriptorDraftDiagnostic>
            {
                new() { Code = new DiagnosticCode("WARN-01"), Severity = SeverityLevel.Warning, Message = "msg" },
                new() { Code = new DiagnosticCode("ERR-01"), Severity = SeverityLevel.Error, Message = "msg" }
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
                new() { Code = new DiagnosticCode("ERR-01"), Severity = SeverityLevel.Error, Message = "msg" },
                new() { Code = new DiagnosticCode("WARN-01"), Severity = SeverityLevel.Warning, Message = "msg" }
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
