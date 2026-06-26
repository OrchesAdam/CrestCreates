using System.Buffers;
using System.Text;
using System.Text.Json;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using CrestCreates.DescriptorDraft.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DescriptorDraft.Tests.CanonicalHashing;

public sealed class ReviewResultCanonicalHashWriterTests
{
    [Fact]
    public void SourceBinding_Writer_Matches_Golden_Json()
    {
        var projection = CreateSourceBindingProjection();
        var json = WriteCanonicalJson(w => ReviewResultSourceBindingCanonicalHashWriter.WritePayload(w, projection));
        var golden = File.ReadAllText("CanonicalHashing/GoldenFiles/review-result-source-binding-v1.json");
        json.Should().Be(golden);

        // Verify hash metadata for source binding
        var review = CreateReview();
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var hash = service.ComputeSourceReviewHash(review);

        hash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.ReviewResult);
        hash.Purpose.Should().Be(CanonicalHashPurposeNames.SourceBinding);
        hash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
        hash.Algorithm.Should().Be("SHA-256");
        hash.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");
        hash.ContractVersion.Should().Be(CanonicalHashContractVersions.DescriptorHash);
        hash.CanonicalShapeVersion.Should().Be(DescriptorDraftReviewCanonicalShapeVersions.SourceBindingV1);
    }

    [Fact]
    public void Integrity_Writer_Matches_Golden_Json()
    {
        var projection = CreateIntegrityProjection();
        var json = WriteCanonicalJson(w => ReviewResultIntegrityCanonicalHashWriter.WritePayload(w, projection));
        var golden = File.ReadAllText("CanonicalHashing/GoldenFiles/review-result-integrity-v1.json");
        json.Should().Be(golden);

        // Verify hash metadata for integrity
        var review = CreateReview();
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var hash = service.ComputeReviewManifestHash(review);

        hash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.ReviewResult);
        hash.Purpose.Should().Be(CanonicalHashPurposeNames.Integrity);
        hash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
        hash.Algorithm.Should().Be("SHA-256");
        hash.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");
        hash.ContractVersion.Should().Be(CanonicalHashContractVersions.DescriptorHash);
        hash.CanonicalShapeVersion.Should().Be(DescriptorDraftReviewCanonicalShapeVersions.IntegrityV1);
    }

    // ── Sensitivity tests ────────────────────────────────────────────

    [Fact]
    public void DiagnosticOrderChange_DoesNotChange_SourceBinding_Json()
    {
        var swapped = new ReviewResultSourceBindingProjection
        {
            TenantId = "tenant-1",
            DraftId = "draft-1",
            IsActivationEligible = true,
            IsValid = true,
            Diagnostics = new List<ReviewDiagnosticProjection>
            {
                new() { Code = "ERR-01", Severity = "Error" },
                new() { Code = "WARN-01", Severity = "Warning" }
            }.AsReadOnly(),
            GovernanceDecision = null,
            ImpactSeverity = null
        };

        var json = WriteCanonicalJson(w => ReviewResultSourceBindingCanonicalHashWriter.WritePayload(w, swapped));
        var golden = File.ReadAllText("CanonicalHashing/GoldenFiles/review-result-source-binding-v1.json");
        json.Should().Be(golden, "canonical sorting should produce the same JSON regardless of diagnostic input order");
    }

    private static DescriptorDraftReviewResult CreateReview() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-1",
        IsActivationEligible = true,
        ValidationResult = new DescriptorDraftValidationResult { IsValid = true, Diagnostics = [] },
        Diagnostics = Array.Empty<DescriptorDraftDiagnostic>()
    };

    private static ReviewResultSourceBindingProjection CreateSourceBindingProjection() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-1",
        IsActivationEligible = true,
        IsValid = true,
        Diagnostics = new List<ReviewDiagnosticProjection>
        {
            new() { Code = "WARN-01", Severity = "Warning" },
            new() { Code = "ERR-01", Severity = "Error" }
        }.AsReadOnly(),
        GovernanceDecision = null,
        ImpactSeverity = null
    };

    private static ReviewResultIntegrityProjection CreateIntegrityProjection() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-1",
        IsActivationEligible = true,
        IsValid = true,
        DiagnosticCount = 2
    };

    private static string WriteCanonicalJson(Action<Utf8JsonWriter> write)
    {
        var bufferWriter = new ArrayBufferWriter<byte>(4096);
        using var jsonWriter = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = true
        });

        write(jsonWriter);
        jsonWriter.Flush();

        return Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
    }
}
