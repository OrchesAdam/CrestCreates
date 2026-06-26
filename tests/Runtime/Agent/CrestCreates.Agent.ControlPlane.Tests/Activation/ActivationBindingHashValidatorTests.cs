using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests.Activation;

public class ActivationBindingHashValidatorTests
{
    private readonly ActivationBindingHashValidator _validator = new();

    [Fact]
    public void Validate_CompleteValidHashes_ReturnsNoIssues()
    {
        var hashes = CreateValidBindingHashes();

        var result = _validator.Validate(hashes);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingSourceReviewHash_ReturnsError()
    {
        var hashes = CreateValidBindingHashes() with
        {
            SourceReviewHash = CreateSlotHash(SlotMetadata.SourceReviewHash, value: "")
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "SourceReviewHash" &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_MissingPackageManifestHash_ReturnsError()
    {
        var hashes = CreateValidBindingHashes() with
        {
            PackageManifestHash = CreateSlotHash(SlotMetadata.PackageManifestHash, value: "")
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i => i.Slot == "PackageManifestHash" && i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_MissingPackageEvidenceHash_ReturnsError()
    {
        var hashes = CreateValidBindingHashes() with
        {
            PackageEvidenceHash = CreateSlotHash(SlotMetadata.PackageEvidenceHash, value: "")
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "PackageEvidenceHash" &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_AlgorithmVersionMismatch_ReturnsError()
    {
        var hashes = CreateValidBindingHashes() with
        {
            SourceReviewHash = CreateSlotHash(SlotMetadata.SourceReviewHash, algorithmVersion: "sha256-canonical-json-v1"),
            ReviewManifestHash = CreateSlotHash(SlotMetadata.ReviewManifestHash, algorithmVersion: "sha256-canonical-json-v2")
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "AlgorithmVersion" &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_ContractVersionMismatch_ReturnsWarning()
    {
        var hashes = CreateValidBindingHashes() with
        {
            SourceReviewHash = CreateSlotHash(SlotMetadata.SourceReviewHash, contractVersion: "canonical-hash-v1"),
            ContractHash = CreateSlotHash(SlotMetadata.ContractHash, contractVersion: "canonical-hash-v2")
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "ContractVersion" &&
            i.Severity == BindingHashValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_AllEmptyHashes_ReturnsMultipleErrors()
    {
        var hashes = new BindingHashes
        {
            SourceReviewHash = CreateSlotHash(SlotMetadata.SourceReviewHash, value: ""),
            ReviewManifestHash = CreateSlotHash(SlotMetadata.ReviewManifestHash, value: ""),
            PackageManifestHash = CreateSlotHash(SlotMetadata.PackageManifestHash, value: ""),
            PackageEvidenceHash = CreateSlotHash(SlotMetadata.PackageEvidenceHash, value: ""),
            PackageEvidenceEnvelopeHash = CreateSlotHash(SlotMetadata.PackageEvidenceEnvelopeHash, value: ""),
            ContractHash = CreateSlotHash(SlotMetadata.ContractHash, value: ""),
            DefinitionHash = CreateSlotHash(SlotMetadata.DefinitionHash, value: "")
        };

        var result = _validator.Validate(hashes);

        result.Should().HaveCountGreaterThanOrEqualTo(7);
        result.Should().AllSatisfy(i => i.Severity.Should().Be(BindingHashValidationSeverity.Error));
    }

    [Fact]
    public void Validate_MissingReviewManifestHash_ReturnsError()
    {
        var hashes = CreateValidBindingHashes() with
        {
            ReviewManifestHash = CreateSlotHash(SlotMetadata.ReviewManifestHash, value: "")
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "ReviewManifestHash" &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_MissingContractHash_ReturnsError()
    {
        var hashes = CreateValidBindingHashes() with
        {
            ContractHash = CreateSlotHash(SlotMetadata.ContractHash, value: "")
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "ContractHash" &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_MissingDefinitionHash_ReturnsError()
    {
        var hashes = CreateValidBindingHashes() with
        {
            DefinitionHash = CreateSlotHash(SlotMetadata.DefinitionHash, value: "")
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "DefinitionHash" &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_WrongArtifactKind_ReturnsError()
    {
        // SourceReviewHash expects ArtifactKind=ReviewResult, but we put Descriptor
        var hashes = CreateValidBindingHashes() with
        {
            SourceReviewHash = CreateSlotHash(SlotMetadata.SourceReviewHash) with
            {
                ArtifactKind = CanonicalHashArtifactNames.Descriptor // Wrong!
            }
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "SourceReviewHash" &&
            i.Description.Contains("ArtifactKind") &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_WrongPurpose_ReturnsError()
    {
        // ContractHash expects Purpose=Contract, but we put Definition
        var hashes = CreateValidBindingHashes() with
        {
            ContractHash = CreateSlotHash(SlotMetadata.ContractHash) with
            {
                Purpose = CanonicalHashPurposeNames.Definition // Wrong for Contract slot!
            }
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "ContractHash" &&
            i.Description.Contains("Purpose") &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_DescriptorHashInReviewSlot_ReturnsError()
    {
        // A Descriptor hash (ArtifactKind=Descriptor, Purpose=Contract) placed in
        // the SourceReviewHash slot must be detected — the exact bug Finding #2 targets.
        var descriptorHash = new CanonicalHash
        {
            Value = "some-digest",
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = CanonicalHashArtifactNames.Descriptor,
            Scope = CanonicalHashScopeNames.InternalFull,
            Purpose = CanonicalHashPurposeNames.Contract,
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "test-shape-v1"
        };

        var hashes = CreateValidBindingHashes() with
        {
            SourceReviewHash = descriptorHash
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "SourceReviewHash" &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_MissingAllPackageSubHashes_ReturnsMultipleErrors()
    {
        var hashes = CreateValidBindingHashes() with
        {
            PackageManifestHash = CreateSlotHash(SlotMetadata.PackageManifestHash, value: ""),
            PackageEvidenceHash = CreateSlotHash(SlotMetadata.PackageEvidenceHash, value: ""),
            PackageEvidenceEnvelopeHash = CreateSlotHash(SlotMetadata.PackageEvidenceEnvelopeHash, value: "")
        };

        var result = _validator.Validate(hashes);

        result.Should().HaveCount(3);
        result.Should().AllSatisfy(i => i.Severity.Should().Be(BindingHashValidationSeverity.Error));
    }

    [Fact]
    public void Validate_WrongScope_ReturnsError()
    {
        // SourceReviewHash expects Scope=InternalFull, but we put TenantVisible
        var hashes = CreateValidBindingHashes() with
        {
            SourceReviewHash = CreateSlotHash(SlotMetadata.SourceReviewHash) with
            {
                Scope = "TenantVisible" // Wrong!
            }
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "SourceReviewHash" &&
            i.Description.Contains("Scope") &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_EmptyAlgorithm_ReturnsError()
    {
        var hashes = CreateValidBindingHashes() with
        {
            ContractHash = CreateSlotHash(SlotMetadata.ContractHash) with { Algorithm = "" }
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "ContractHash" &&
            i.Description.Contains("Algorithm") &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_EmptyAlgorithmVersion_ReturnsError()
    {
        var hashes = CreateValidBindingHashes() with
        {
            ContractHash = CreateSlotHash(SlotMetadata.ContractHash) with { AlgorithmVersion = "" }
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "ContractHash" &&
            i.Description.Contains("AlgorithmVersion") &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_EmptyContractVersion_ReturnsError()
    {
        var hashes = CreateValidBindingHashes() with
        {
            ContractHash = CreateSlotHash(SlotMetadata.ContractHash) with { ContractVersion = "" }
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "ContractHash" &&
            i.Description.Contains("ContractVersion") &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    [Fact]
    public void Validate_EmptyCanonicalShapeVersion_ReturnsError()
    {
        var hashes = CreateValidBindingHashes() with
        {
            ContractHash = CreateSlotHash(SlotMetadata.ContractHash) with { CanonicalShapeVersion = "" }
        };

        var result = _validator.Validate(hashes);

        result.Should().Contain(i =>
            i.Slot == "ContractHash" &&
            i.Description.Contains("CanonicalShapeVersion") &&
            i.Severity == BindingHashValidationSeverity.Error);
    }

    // ── Helpers ──

    private static class SlotMetadata
    {
        public static readonly (string ArtifactKind, string Purpose) SourceReviewHash =
            (CanonicalHashArtifactNames.ReviewResult, CanonicalHashPurposeNames.SourceBinding);
        public static readonly (string ArtifactKind, string Purpose) ReviewManifestHash =
            (CanonicalHashArtifactNames.ReviewResult, CanonicalHashPurposeNames.Integrity);
        public static readonly (string ArtifactKind, string Purpose) PackageManifestHash =
            (CanonicalHashArtifactNames.PackageManifest, CanonicalHashPurposeNames.Integrity);
        public static readonly (string ArtifactKind, string Purpose) PackageEvidenceHash =
            (CanonicalHashArtifactNames.PackageEvidence, CanonicalHashPurposeNames.AuditEvidence);
        public static readonly (string ArtifactKind, string Purpose) PackageEvidenceEnvelopeHash =
            (CanonicalHashArtifactNames.PackageEvidenceEnvelope, CanonicalHashPurposeNames.AuditEvidence);
        public static readonly (string ArtifactKind, string Purpose) ContractHash =
            (CanonicalHashArtifactNames.Descriptor, CanonicalHashPurposeNames.Contract);
        public static readonly (string ArtifactKind, string Purpose) DefinitionHash =
            (CanonicalHashArtifactNames.Descriptor, CanonicalHashPurposeNames.Definition);
    }

    private static BindingHashes CreateValidBindingHashes(
        string algorithmVersion = "sha256-canonical-json-v1",
        string contractVersion = "canonical-hash-v1")
    {
        return new BindingHashes
        {
            SourceReviewHash = CreateSlotHash(SlotMetadata.SourceReviewHash, "source-review-hash", algorithmVersion, contractVersion),
            ReviewManifestHash = CreateSlotHash(SlotMetadata.ReviewManifestHash, "review-manifest-hash", algorithmVersion, contractVersion),
            PackageManifestHash = CreateSlotHash(SlotMetadata.PackageManifestHash, "pkg-manifest-hash", algorithmVersion, contractVersion),
            PackageEvidenceHash = CreateSlotHash(SlotMetadata.PackageEvidenceHash, "pkg-evidence-hash", algorithmVersion, contractVersion),
            PackageEvidenceEnvelopeHash = CreateSlotHash(SlotMetadata.PackageEvidenceEnvelopeHash, "pkg-evidence-envelope-hash", algorithmVersion, contractVersion),
            ContractHash = CreateSlotHash(SlotMetadata.ContractHash, "contract-hash", algorithmVersion, contractVersion),
            DefinitionHash = CreateSlotHash(SlotMetadata.DefinitionHash, "definition-hash", algorithmVersion, contractVersion)
        };
    }

    private static CanonicalHash CreateSlotHash(
        (string ArtifactKind, string Purpose) slotMeta,
        string value = "test-hash",
        string algorithmVersion = "sha256-canonical-json-v1",
        string contractVersion = "canonical-hash-v1")
    {
        return new CanonicalHash
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = algorithmVersion,
            ArtifactKind = slotMeta.ArtifactKind,
            Scope = CanonicalHashScopeNames.InternalFull,
            Purpose = slotMeta.Purpose,
            ContractVersion = contractVersion,
            CanonicalShapeVersion = "test-shape-v1"
        };
    }
}
