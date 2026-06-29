using System.Text;
using System.Text.Json;
using Xunit;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.Evidence;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;
using FluentAssertions;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Metadata.Tests.DescriptorPackage.CanonicalHashing;

public sealed class DescriptorPackageCanonicalHashWriterTests
{
    [Fact]
    public void Manifest_Writer_Produces_Deterministic_CanonicalJson()
    {
        var manifest = CreateManifest();
        var json1 = WriteCanonicalJson(w => DescriptorPackageManifestCanonicalHashWriter.WritePayload(w, manifest));
        var json2 = WriteCanonicalJson(w => DescriptorPackageManifestCanonicalHashWriter.WritePayload(w, manifest));

        json1.Should().Be(json2);
        json1.Should().NotBeEmpty();
        json1.Should().Contain("FormatVersion");
        json1.Should().Contain("PackageId");
        json1.Should().Contain("DescriptorEntries");
    }

    [Fact]
    public void Manifest_Writer_Sorts_DescriptorEntries()
    {
        var manifest = new DescriptorManifest
        {
            FormatVersion = "1.0",
            PackageId = "pkg",
            PackageVersion = "1.0",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DescriptorCount = 2,
            DescriptorEntries = new[]
            {
                new DescriptorManifestEntry { Ref = new DescriptorRef("zzz", "zzz", 1), Kind = DescriptorKind.Schema, Name = "Z", State = DescriptorState.Active },
                new DescriptorManifestEntry { Ref = new DescriptorRef("aaa", "aaa", 1), Kind = DescriptorKind.Schema, Name = "A", State = DescriptorState.Active }
            }
        };

        var json = WriteCanonicalJson(w => DescriptorPackageManifestCanonicalHashWriter.WritePayload(w, manifest));
        var aaaIndex = json.IndexOf("\"aaa\"", StringComparison.Ordinal);
        var zzzIndex = json.IndexOf("\"zzz\"", StringComparison.Ordinal);

        aaaIndex.Should().BeLessThan(zzzIndex, "entries should be sorted by namespace then id");
    }

    [Fact]
    public void Manifest_Writer_Null_Version_Writes_JsonNull()
    {
        var manifest = new DescriptorManifest
        {
            FormatVersion = "1.0",
            PackageId = "pkg",
            PackageVersion = "1.0",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DescriptorCount = 1,
            DescriptorEntries = new[]
            {
                new DescriptorManifestEntry { Ref = new DescriptorRef("ns", "id", null), Kind = DescriptorKind.Schema, Name = "S", State = DescriptorState.Active }
            }
        };

        var json = WriteCanonicalJson(w => DescriptorPackageManifestCanonicalHashWriter.WritePayload(w, manifest));
        json.Should().Contain("\"Version\":null");
    }

    [Fact]
    public void Evidence_Writer_Produces_Deterministic_CanonicalJson()
    {
        var evidence = CreateEvidence();
        var json1 = WriteCanonicalJson(w => DescriptorPackageEvidenceCanonicalHashWriter.WritePayload(w, evidence));
        var json2 = WriteCanonicalJson(w => DescriptorPackageEvidenceCanonicalHashWriter.WritePayload(w, evidence));

        json1.Should().Be(json2);
        json1.Should().NotBeEmpty();
        json1.Should().Contain("TopologyNodeCount");
        json1.Should().Contain("MaxImpactSeverity");
        json1.Should().Contain("NormalizedFindings");
    }

    [Fact]
    public void Evidence_Writer_NormalizedFindings_CanonicalOrder()
    {
        var findingA = new EvidenceFinding
        {
            Severity = SeverityLevel.Error, Code = new DiagnosticCode("C"), Source = "test", Message = "M3",
            RelatedRefs = new[] { new DescriptorRef("z", "z", 1) }
        };
        var findingB = new EvidenceFinding
        {
            Severity = SeverityLevel.Error, Code = new DiagnosticCode("C"), Source = "test", Message = "M1",
            RelatedRefs = new[] { new DescriptorRef("a", "a", 1) }
        };
        var evidence = new DescriptorPackageEvidence
        {
            NormalizedFindings = new[] { findingA, findingB }
        };

        var json = WriteCanonicalJson(w => DescriptorPackageEvidenceCanonicalHashWriter.WritePayload(w, evidence));
        var m1Index = json.IndexOf("\"M1\"", StringComparison.Ordinal);
        var m3Index = json.IndexOf("\"M3\"", StringComparison.Ordinal);

        m1Index.Should().BeLessThan(m3Index, "findings should be sorted by severity, code, source, message");
    }

    [Fact]
    public void Evidence_Writer_Sorts_DiagnosticCounts()
    {
        var evidence = new DescriptorPackageEvidence
        {
            TopologyDiagnosticCounts = new[]
            {
                new EvidenceFindingCount { Severity = SeverityLevel.Error, Code = new DiagnosticCode("E002"), Count = 2 },
                new EvidenceFindingCount { Severity = SeverityLevel.Error, Code = new DiagnosticCode("E001"), Count = 1 },
                new EvidenceFindingCount { Severity = SeverityLevel.Info, Code = new DiagnosticCode("I001"), Count = 3 }
            }
        };

        var json = WriteCanonicalJson(w => DescriptorPackageEvidenceCanonicalHashWriter.WritePayload(w, evidence));
        var i001Index = json.IndexOf("\"I001\"", StringComparison.Ordinal);
        var e001Index = json.IndexOf("\"E001\"", StringComparison.Ordinal);
        var e002Index = json.IndexOf("\"E002\"", StringComparison.Ordinal);

        e001Index.Should().BeLessThan(e002Index);
        e002Index.Should().BeLessThan(i001Index);
    }

    [Fact]
    public void Envelope_Writer_Produces_Deterministic_CanonicalJson()
    {
        var envelope = CreateEnvelope(CreateHash("abc123"), CreateHash("def456"));
        var json1 = WriteCanonicalJson(w => DescriptorPackageEvidenceEnvelopeCanonicalHashWriter.WritePayload(w, envelope));
        var json2 = WriteCanonicalJson(w => DescriptorPackageEvidenceEnvelopeCanonicalHashWriter.WritePayload(w, envelope));

        json1.Should().Be(json2);
        json1.Should().NotBeEmpty();
        json1.Should().Contain("PackageId");
        json1.Should().Contain("PackageManifestHash");
        json1.Should().Contain("PackageEvidenceHash");
    }

    [Fact]
    public void Envelope_Writer_HashObjects_Are_Full_Metadata()
    {
        var hash = CreateHash("deadbeef", "Descriptor", "Contract");
        var envelope = CreateEnvelope(hash, hash);
        var json = WriteCanonicalJson(w => DescriptorPackageEvidenceEnvelopeCanonicalHashWriter.WritePayload(w, envelope));

        json.Should().Contain("\"Algorithm\":\"SHA-256\"");
        json.Should().Contain("\"Value\":\"deadbeef\"");
        json.Should().Contain("\"ArtifactKind\":\"Descriptor\"");
        json.Should().NotBe("\"deadbeef\"", "hash should be written as full object, not just the value string");
    }

    [Fact]
    public void Envelope_Writer_NullFields_Distinguished_From_Empty()
    {
        var hash = CreateHash("abc");
        var envelopeNull = CreateEnvelope(hash, hash, createdBy: null, source: null);
        var envelopeEmpty = CreateEnvelope(hash, hash, createdBy: "", source: "");

        var jsonNull = WriteCanonicalJson(w => DescriptorPackageEvidenceEnvelopeCanonicalHashWriter.WritePayload(w, envelopeNull));
        var jsonEmpty = WriteCanonicalJson(w => DescriptorPackageEvidenceEnvelopeCanonicalHashWriter.WritePayload(w, envelopeEmpty));

        jsonNull.Should().NotBe(jsonEmpty, "null and empty string must produce different JSON");
    }

    // ── Golden file tests (write-once, verify-forever) ──────────────

    [Fact]
    public void Manifest_Writer_Matches_Golden_Json()
    {
        var manifest = CreateManifest();
        var json = WriteCanonicalJson(w => DescriptorPackageManifestCanonicalHashWriter.WritePayload(w, manifest));
        var golden = ReadGoldenFile("descriptor-package-manifest-v2.json");
        json.Should().Be(golden);

        // Verify hash metadata produced by the computer using this writer
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var hashes = computer.ComputeHashSet(manifest, CreateEvidence(), CreateEnvelopeMetadata());

        hashes.PackageManifestHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageManifest);
        hashes.PackageManifestHash.Purpose.Should().Be(CanonicalHashPurposeNames.Integrity);
        hashes.PackageManifestHash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
        hashes.PackageManifestHash.Algorithm.Should().Be("SHA-256");
        hashes.PackageManifestHash.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");
        hashes.PackageManifestHash.ContractVersion.Should().Be(CanonicalHashContractVersions.DescriptorHash);
        hashes.PackageManifestHash.CanonicalShapeVersion.Should().Be(DescriptorPackageCanonicalShapeVersions.PackageManifestV2);
    }

    [Fact]
    public void Evidence_Writer_Matches_Golden_Json()
    {
        var evidence = CreateEvidence();
        var json = WriteCanonicalJson(w => DescriptorPackageEvidenceCanonicalHashWriter.WritePayload(w, evidence));
        var golden = ReadGoldenFile("descriptor-package-evidence-v2.json");
        json.Should().Be(golden);

        // Verify hash metadata produced by the computer using this writer
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var hashes = computer.ComputeHashSet(CreateManifest(), evidence, CreateEnvelopeMetadata());

        hashes.PackageEvidenceHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageEvidence);
        hashes.PackageEvidenceHash.Purpose.Should().Be(CanonicalHashPurposeNames.AuditEvidence);
        hashes.PackageEvidenceHash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
        hashes.PackageEvidenceHash.Algorithm.Should().Be("SHA-256");
        hashes.PackageEvidenceHash.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");
        hashes.PackageEvidenceHash.ContractVersion.Should().Be(CanonicalHashContractVersions.DescriptorHash);
        hashes.PackageEvidenceHash.CanonicalShapeVersion.Should().Be(DescriptorPackageCanonicalShapeVersions.PackageEvidenceV2);
    }

    [Fact]
    public void Envelope_Writer_Matches_Golden_Json()
    {
        var envelope = CreateEnvelope(CreateHash("abc123"), CreateHash("def456"));
        var json = WriteCanonicalJson(w => DescriptorPackageEvidenceEnvelopeCanonicalHashWriter.WritePayload(w, envelope));
        var golden = ReadGoldenFile("descriptor-package-evidence-envelope-v2.json");
        json.Should().Be(golden);

        // Verify hash metadata produced by the computer for the envelope
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var hashes = computer.ComputeHashSet(CreateManifest(), CreateEvidence(), CreateEnvelopeMetadata());

        hashes.PackageEvidenceEnvelopeHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageEvidenceEnvelope);
        hashes.PackageEvidenceEnvelopeHash.Purpose.Should().Be(CanonicalHashPurposeNames.AuditEvidence);
        hashes.PackageEvidenceEnvelopeHash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
        hashes.PackageEvidenceEnvelopeHash.Algorithm.Should().Be("SHA-256");
        hashes.PackageEvidenceEnvelopeHash.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");
        hashes.PackageEvidenceEnvelopeHash.ContractVersion.Should().Be(CanonicalHashContractVersions.DescriptorHash);
        hashes.PackageEvidenceEnvelopeHash.CanonicalShapeVersion.Should().Be(DescriptorPackageCanonicalShapeVersions.PackageEvidenceEnvelopeV2);
    }

    // ── Hash value tests (manifest + evidence) ──────────────────────

    [Fact]
    public void Manifest_CanonicalHash_Matches_Expected()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var hashes = computer.ComputeHashSet(CreateManifest(), CreateEvidence(), CreateEnvelopeMetadata());

        hashes.PackageManifestHash.Value.Should().HaveLength(64);
        hashes.PackageManifestHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageManifest);
        hashes.PackageManifestHash.Purpose.Should().Be(CanonicalHashPurposeNames.Integrity);
        hashes.PackageManifestHash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
        hashes.PackageManifestHash.Algorithm.Should().Be("SHA-256");
        hashes.PackageManifestHash.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");
        hashes.PackageManifestHash.ContractVersion.Should().Be(CanonicalHashContractVersions.DescriptorHash);
        hashes.PackageManifestHash.CanonicalShapeVersion.Should().Be(DescriptorPackageCanonicalShapeVersions.PackageManifestV2);

        hashes.PackageEvidenceHash.Value.Should().HaveLength(64);
        hashes.PackageEvidenceHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageEvidence);
        hashes.PackageEvidenceHash.Purpose.Should().Be(CanonicalHashPurposeNames.AuditEvidence);
        hashes.PackageEvidenceHash.CanonicalShapeVersion.Should().Be(DescriptorPackageCanonicalShapeVersions.PackageEvidenceV2);

        hashes.PackageEvidenceEnvelopeHash.Value.Should().HaveLength(64);
        hashes.PackageEvidenceEnvelopeHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageEvidenceEnvelope);
        hashes.PackageEvidenceEnvelopeHash.Purpose.Should().Be(CanonicalHashPurposeNames.AuditEvidence);
        hashes.PackageEvidenceEnvelopeHash.CanonicalShapeVersion.Should().Be(DescriptorPackageCanonicalShapeVersions.PackageEvidenceEnvelopeV2);
    }

    [Fact]
    public void Manifest_CanonicalHash_Is_Different_From_PipeDelimited()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var hashes = computer.ComputeHashSet(CreateManifest(), CreateEvidence(), CreateEnvelopeMetadata());

        // The legacy pipe-delimited hash should NOT equal the new canonical JSON hash
        var legacyHash = DescriptorPackageHashComputer.ComputeEvidenceHash(CreateEvidence());

        hashes.PackageEvidenceHash.Value.Should().NotBe(legacyHash,
            "canonical JSON hash must be algorithmically distinct from pipe-delimited hash");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static string WriteCanonicalJson(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        write(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ReadGoldenFile(string fileName)
    {
        var path = Path.Combine("DescriptorPackage", "CanonicalHashing", "GoldenFiles", fileName);
        return File.ReadAllText(path);
    }

    private static CanonicalHash CreateHash(string value, string artifactKind = "Descriptor", string purpose = "Contract")
    {
        return new CanonicalHash
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = artifactKind,
            DescriptorKind = null,
            Scope = CanonicalHashScopeNames.InternalFull,
            Purpose = purpose,
            ContractVersion = CanonicalHashContractVersions.DescriptorHash,
            CanonicalShapeVersion = "test-shape-v1"
        };
    }

    private static DescriptorPackageEvidenceEnvelope CreateEnvelope(
        CanonicalHash manifestHash, CanonicalHash evidenceHash,
        string? createdBy = "test-user", string? source = "test-source")
    {
        return new DescriptorPackageEvidenceEnvelope
        {
            PackageId = "test-pkg-001",
            PackageVersion = "1.0.0",
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero),
            CreatedBy = createdBy,
            Source = source,
            PackageManifestHash = manifestHash,
            PackageEvidenceHash = evidenceHash
        };
    }

    private static DescriptorManifest CreateManifest()
    {
        return new DescriptorManifest
        {
            FormatVersion = "1.0",
            PackageId = "test-pkg-001",
            PackageVersion = "1.0.0",
            Name = "Test Package",
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero),
            CreatedBy = "test-user",
            Source = "test-source",
            DescriptorCount = 2,
            DescriptorEntries = new[]
            {
                new DescriptorManifestEntry
                {
                    Ref = new DescriptorRef("schema", "s1", 1),
                    Kind = DescriptorKind.Schema,
                    Name = "UserSchema",
                    State = DescriptorState.Active,
                    ContractHash = "abc123",
                    DefinitionHash = "def456"
                },
                new DescriptorManifestEntry
                {
                    Ref = new DescriptorRef("capability", "c1", 2),
                    Kind = DescriptorKind.Capability,
                    Name = "CreateUser",
                    State = DescriptorState.Active,
                    ContractHash = "ghi789",
                    DefinitionHash = "jkl012",
                    SupersededById = "capability.c1.1"
                }
            }
        };
    }

    private static DescriptorPackageEvidence CreateEvidence()
    {
        return new DescriptorPackageEvidence
        {
            TopologyNodeCount = 2,
            TopologyEdgeCount = 1,
            HasTopologyErrors = false,
            TopologyDiagnosticCounts = new[]
            {
                new EvidenceFindingCount { Severity = SeverityLevel.Info, Code = new DiagnosticCode("T001"), Count = 1 }
            },
            MaxImpactSeverity = DescriptorImpactSeverity.Low,
            AffectedDescriptorCount = 0,
            ImpactPathCount = 0,
            ImpactDiagnosticCounts = Array.Empty<EvidenceFindingCount>(),
            MaxCompatibilityLevel = DescriptorCompatibilityLevel.Compatible,
            BreakingFindingCount = 0,
            SecuritySensitiveFindingCount = 0,
            UnsupportedFindingCount = 0,
            MaxLifecycleDecision = DescriptorLifecycleDecisionKind.Allowed,
            RequiresReview = false,
            IsBlocked = false,
            PackageFindingCount = 0,
            NormalizedFindings = new[]
            {
                new EvidenceFinding
                {
                    Severity = SeverityLevel.Info,
                    Code = new DiagnosticCode("P001"),
                    Source = "test",
                    Message = "Package is valid",
                    Subject = new DescriptorRef("capability", "c1", 2),
                    RelatedRefs = new[]
                    {
                        new DescriptorRef("schema", "s1", 1)
                    }
                }
            }
        };
    }

    private static DescriptorPackageEvidenceEnvelopeMetadata CreateEnvelopeMetadata()
    {
        return new DescriptorPackageEvidenceEnvelopeMetadata
        {
            PackageId = "test-pkg-001",
            PackageVersion = "1.0.0",
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero),
            CreatedBy = "test-user",
            Source = "test-source"
        };
    }
}
