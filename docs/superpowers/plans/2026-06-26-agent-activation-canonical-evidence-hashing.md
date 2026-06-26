# Agent Activation Canonical Evidence Hashing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Agent activation/package/evidence string digest binding with end-to-end canonical `CanonicalHash` production and validation.

**Architecture:** ReviewResult hashing is owned by DescriptorDraft, package/evidence/envelope hashing is owned by Metadata DescriptorPackage, and Agent Control Plane only consumes, stores, validates, and compares hashes. Package hashes move as an atomic `DescriptorPackageHashSet`, and activation stale detection keeps full `CanonicalHash` equality.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, `Utf8JsonWriter`, `ICanonicalHashComputer.ComputeFromProjection`, source-generated JSON contexts where existing serialization requires them.

## Global Constraints

- This is a breaking migration for #47 and supersedes #45.
- Do not preserve `sha256-adhoc-v1` compatibility.
- Do not leave package/evidence string digests as production source of truth.
- ReviewResult canonical hashing is owned by `CrestCreates.DescriptorDraft`.
- Package/evidence/envelope canonical hashing is owned by Metadata DescriptorPackage.
- Agent Control Plane production code must not manually instantiate production `CanonicalHash` values for computed review, package, evidence, envelope, contract, or definition hashes.
- Agent Control Plane may accept, clone, compare, validate, serialize, and pass through `CanonicalHash` values produced elsewhere.
- `BindingHashes` must use `SourceReviewHash`, `ReviewManifestHash`, `PackageManifestHash`, `PackageEvidenceHash`, `PackageEvidenceEnvelopeHash`, `ContractHash`, and `DefinitionHash`.
- `DescriptorPackageHashSet` must use `PackageManifestHash`, `PackageEvidenceHash`, and `PackageEvidenceEnvelopeHash`.
- `PackageEvidenceEnvelopeHash` purpose is fixed to `AuditEvidence`.
- `DescriptorManifest` must not contain its own `PackageManifestHash`.
- `ContentHash` is allowed only in DTO/display projections, not source-of-truth models.
- `DescriptorPackageHashComputer` must be removed, renamed, or downgraded to tests/migration-only code. No production package builder may call it after this migration.
- `ActivationBindingHashValidator` must run at submit, recheck, and pre-gate boundaries.
- Canonical writers must use `Utf8JsonWriter`, fixed property order, explicit collection ordering, UTC-normalized `DateTimeOffset` `O` formatting, invariant number formatting, and no `JsonSerializer`, `JsonTypeInfo`, runtime `Type`, reflection, ambient context, or current time.
- Golden tests must assert canonical JSON bytes and `CanonicalHash` metadata.

---

## File Structure

Create DescriptorDraft hashing contracts:

- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CanonicalHashing/IDescriptorDraftReviewHashService.cs`
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CanonicalHashing/ReviewDiagnosticProjection.cs`
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CanonicalHashing/ReviewResultSourceBindingProjection.cs`
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CanonicalHashing/ReviewResultIntegrityProjection.cs`
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CanonicalHashing/DescriptorDraftReviewCanonicalShapeVersions.cs`

Create DescriptorDraft hashing implementation:

- `src/Metadata/Draft/CrestCreates.DescriptorDraft/CanonicalHashing/DefaultDescriptorDraftReviewHashService.cs`
- `src/Metadata/Draft/CrestCreates.DescriptorDraft/CanonicalHashing/ReviewResultSourceBindingCanonicalHashWriter.cs`
- `src/Metadata/Draft/CrestCreates.DescriptorDraft/CanonicalHashing/ReviewResultIntegrityCanonicalHashWriter.cs`

Modify package models and create package hashing contracts:

- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorManifest.cs`
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackage.cs`
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageHashSet.cs`
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageEvidenceEnvelope.cs`
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageEvidenceEnvelopeMetadata.cs`
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/IDescriptorPackageCanonicalHashComputer.cs`
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageCanonicalShapeVersions.cs`
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/DescriptorPackagePreview.cs`

Create package hashing implementation:

- `src/Metadata/CrestCreates.Metadata/DescriptorPackage/CanonicalHashing/DefaultDescriptorPackageCanonicalHashComputer.cs`
- `src/Metadata/CrestCreates.Metadata/DescriptorPackage/CanonicalHashing/DescriptorPackageManifestCanonicalHashWriter.cs`
- `src/Metadata/CrestCreates.Metadata/DescriptorPackage/CanonicalHashing/DescriptorPackageEvidenceCanonicalHashWriter.cs`
- `src/Metadata/CrestCreates.Metadata/DescriptorPackage/CanonicalHashing/DescriptorPackageEvidenceEnvelopeCanonicalHashWriter.cs`

Modify package builder and DI:

- `src/Metadata/CrestCreates.Metadata/DescriptorPackage/DefaultDescriptorPackageBuilder.cs`
- `src/Metadata/CrestCreates.Metadata/Bootstrap/MetadataServiceCollectionExtensions.cs`
- `src/Metadata/Draft/CrestCreates.DescriptorDraft/DescriptorDraftServiceCollectionExtensions.cs`

Modify activation contracts and runtime:

- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Activation/BindingHashes.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Activation/IActivationBindingArtifactResolver.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/InMemoryActivationBindingArtifactResolver.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/ActivationBindingHashValidator.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/DefaultActivationEvidenceRechecker.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/DefaultDescriptorActivationRequestService.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewReportBuilder.cs`

Create/update tests:

- `tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/CanonicalHashing/DescriptorDraftReviewHashServiceTests.cs`
- `tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/CanonicalHashing/ReviewResultCanonicalHashWriterTests.cs`
- `tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/CanonicalHashing/GoldenFiles/review-result-source-binding-v1.json`
- `tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/CanonicalHashing/GoldenFiles/review-result-integrity-v1.json`
- `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/DescriptorPackageCanonicalHashComputerTests.cs`
- `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/DescriptorPackageCanonicalHashWriterTests.cs`
- `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/GoldenFiles/descriptor-package-manifest-v1.json`
- `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/GoldenFiles/descriptor-package-evidence-v1.json`
- `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/GoldenFiles/descriptor-package-evidence-envelope-v1.json`
- `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Activation/ActivationBindingHashValidatorTests.cs`
- `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Activation/ActivationEvidenceRecheckerCanonicalHashTests.cs`
- `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Activation/ActivationBindingCanonicalHashFlowTests.cs`
- `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/AgentActivationCanonicalHashGuardTests.cs`

---

### Task 1: Canonical Artifact Names and Package Hash Contracts

**Files:**
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashing/CanonicalHashArtifactKind.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/CanonicalHashing/CanonicalHashArtifactNames.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageHashSet.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageEvidenceEnvelope.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageEvidenceEnvelopeMetadata.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/IDescriptorPackageCanonicalHashComputer.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageCanonicalShapeVersions.cs`
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/DescriptorPackageCanonicalHashComputerTests.cs`

**Interfaces:**
- Produces: `DescriptorPackageHashSet`, `DescriptorPackageEvidenceEnvelope`, `DescriptorPackageEvidenceEnvelopeMetadata`, `IDescriptorPackageCanonicalHashComputer.ComputeHashSet(DescriptorManifest, DescriptorPackageEvidence, DescriptorPackageEvidenceEnvelopeMetadata)`.
- Consumes: existing `CanonicalHash`, `DescriptorManifest`, `DescriptorPackageEvidence`.

- [ ] **Step 1: Write failing metadata tests for package hash set shape**

Add this test file:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using FluentAssertions;

namespace CrestCreates.Metadata.Tests.DescriptorPackage.CanonicalHashing;

public sealed class DescriptorPackageCanonicalHashComputerTests
{
    [Fact]
    public void DescriptorPackageHashSet_Exposes_Strongly_Named_Package_Hashes()
    {
        var hash = CreateHash("PackageManifest", "Integrity", "manifest-value");

        var set = new DescriptorPackageHashSet
        {
            PackageManifestHash = hash,
            PackageEvidenceHash = CreateHash("PackageEvidence", "AuditEvidence", "evidence-value"),
            PackageEvidenceEnvelopeHash = CreateHash("PackageEvidenceEnvelope", "AuditEvidence", "envelope-value")
        };

        set.PackageManifestHash.ArtifactKind.Should().Be("PackageManifest");
        set.PackageEvidenceHash.ArtifactKind.Should().Be("PackageEvidence");
        set.PackageEvidenceEnvelopeHash.ArtifactKind.Should().Be("PackageEvidenceEnvelope");
    }

    private static CanonicalHash CreateHash(string artifactKind, string purpose, string value) => new()
    {
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = artifactKind,
        Scope = CanonicalHashScopeNames.InternalFull,
        Purpose = purpose,
        ContractVersion = "test-contract-v1",
        CanonicalShapeVersion = "test-shape-v1",
        Value = value
    };
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageCanonicalHashComputerTests"
```

Expected: build fails because `DescriptorPackageHashSet` does not exist.

- [ ] **Step 3: Add artifact names and package hash contracts**

Extend `CanonicalHashArtifactKind` with explicit values:

```csharp
public enum CanonicalHashArtifactKind
{
    Descriptor = 1,
    ReviewResult = 2,
    Package = 3,
    Report = 4,
    PackageManifest = 5,
    PackageEvidence = 6,
    PackageEvidenceEnvelope = 7
}
```

Extend `CanonicalHashArtifactNames`:

```csharp
public const string PackageManifest = nameof(CanonicalHashArtifactKind.PackageManifest);
public const string PackageEvidence = nameof(CanonicalHashArtifactKind.PackageEvidence);
public const string PackageEvidenceEnvelope = nameof(CanonicalHashArtifactKind.PackageEvidenceEnvelope);
```

Create `DescriptorPackageHashSet.cs`:

```csharp
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public sealed record DescriptorPackageHashSet
{
    public required CanonicalHash PackageManifestHash { get; init; }
    public required CanonicalHash PackageEvidenceHash { get; init; }
    public required CanonicalHash PackageEvidenceEnvelopeHash { get; init; }
}
```

Create `DescriptorPackageEvidenceEnvelope.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public sealed class DescriptorPackageEvidenceEnvelope
{
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? Source { get; init; }
    public required CanonicalHash PackageManifestHash { get; init; }
    public required CanonicalHash PackageEvidenceHash { get; init; }
}
```

Add `using CrestCreates.Metadata.Abstractions.CanonicalHashing;` to that file.

Create `DescriptorPackageEvidenceEnvelopeMetadata.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public sealed class DescriptorPackageEvidenceEnvelopeMetadata
{
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? Source { get; init; }
}
```

Create `IDescriptorPackageCanonicalHashComputer.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public interface IDescriptorPackageCanonicalHashComputer
{
    DescriptorPackageHashSet ComputeHashSet(
        DescriptorManifest manifest,
        DescriptorPackageEvidence evidence,
        DescriptorPackageEvidenceEnvelopeMetadata envelopeMetadata);
}
```

Create `DescriptorPackageCanonicalShapeVersions.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public static class DescriptorPackageCanonicalShapeVersions
{
    public const string PackageManifestV1 = "descriptor-package-manifest-v1";
    public const string PackageEvidenceV1 = "descriptor-package-evidence-v1";
    public const string PackageEvidenceEnvelopeV1 = "descriptor-package-evidence-envelope-v1";
}
```

- [ ] **Step 4: Run the focused test and verify it passes**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageCanonicalHashComputerTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Metadata/CrestCreates.Metadata.Abstractions tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/DescriptorPackageCanonicalHashComputerTests.cs
git commit -m "feat: add package canonical hash contracts"
```

### Task 2: Package Canonical Writers and Hash Computer

**Files:**
- Create: `src/Metadata/CrestCreates.Metadata/DescriptorPackage/CanonicalHashing/DefaultDescriptorPackageCanonicalHashComputer.cs`
- Create: `src/Metadata/CrestCreates.Metadata/DescriptorPackage/CanonicalHashing/DescriptorPackageManifestCanonicalHashWriter.cs`
- Create: `src/Metadata/CrestCreates.Metadata/DescriptorPackage/CanonicalHashing/DescriptorPackageEvidenceCanonicalHashWriter.cs`
- Create: `src/Metadata/CrestCreates.Metadata/DescriptorPackage/CanonicalHashing/DescriptorPackageEvidenceEnvelopeCanonicalHashWriter.cs`
- Modify: `src/Metadata/CrestCreates.Metadata/Bootstrap/MetadataServiceCollectionExtensions.cs`
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/DescriptorPackageCanonicalHashWriterTests.cs`
- Test data: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/GoldenFiles/*.json`

**Interfaces:**
- Consumes: `IDescriptorPackageCanonicalHashComputer`, `DescriptorPackageHashSet`, `ICanonicalHashComputer.ComputeFromProjection`.
- Produces: `DefaultDescriptorPackageCanonicalHashComputer`.

- [ ] **Step 1: Write failing tests for metadata and golden bytes**

Create `DescriptorPackageCanonicalHashWriterTests.cs` with:

```csharp
using System.Text;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;
using FluentAssertions;

namespace CrestCreates.Metadata.Tests.DescriptorPackage.CanonicalHashing;

public sealed class DescriptorPackageCanonicalHashWriterTests
{
    [Fact]
    public void ComputeHashSet_Uses_Explicit_Package_Metadata()
    {
        var computer = new DefaultDescriptorPackageCanonicalHashComputer(new DefaultCanonicalHashComputer());
        var manifest = CreateManifest();
        var evidence = new DescriptorPackageEvidence { AffectedDescriptorCount = 2 };
        var envelopeMetadata = CreateEnvelopeMetadata();

        var hashes = computer.ComputeHashSet(manifest, evidence, envelopeMetadata);

        hashes.PackageManifestHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageManifest);
        hashes.PackageManifestHash.Purpose.Should().Be(CanonicalHashPurposeNames.Integrity);
        hashes.PackageEvidenceHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageEvidence);
        hashes.PackageEvidenceHash.Purpose.Should().Be(CanonicalHashPurposeNames.AuditEvidence);
        hashes.PackageEvidenceEnvelopeHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageEvidenceEnvelope);
        hashes.PackageEvidenceEnvelopeHash.Purpose.Should().Be(CanonicalHashPurposeNames.AuditEvidence);
        hashes.PackageEvidenceEnvelopeHash.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");
    }

    [Fact]
    public void Manifest_Writer_Matches_Golden_Json()
    {
        var manifest = CreateManifest();
        var json = Write(w => DescriptorPackageManifestCanonicalHashWriter.WritePayload(w, manifest));

        json.Should().Be(File.ReadAllText(
            "DescriptorPackage/CanonicalHashing/GoldenFiles/descriptor-package-manifest-v1.json"));
    }

    private static string Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        write(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static DescriptorManifest CreateManifest() => new()
    {
        FormatVersion = "1.0",
        PackageId = "pkg-001",
        PackageVersion = "1",
        Name = "Package",
        CreatedAt = new DateTimeOffset(2026, 6, 26, 1, 2, 3, TimeSpan.FromHours(8)),
        CreatedBy = "agent",
        Source = "",
        DescriptorCount = 0,
        DescriptorEntries = Array.Empty<DescriptorManifestEntry>()
    };

    private static DescriptorPackageEvidenceEnvelopeMetadata CreateEnvelopeMetadata() => new()
    {
        PackageId = "pkg-001",
        PackageVersion = "1",
        CreatedAt = new DateTimeOffset(2026, 6, 26, 1, 2, 3, TimeSpan.FromHours(8)),
        CreatedBy = "agent",
        Source = ""
    };

    private static CanonicalHash CreateHash(string artifactKind, string purpose, string value) => new()
    {
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = artifactKind,
        Scope = CanonicalHashScopeNames.InternalFull,
        Purpose = purpose,
        ContractVersion = "test-contract-v1",
        CanonicalShapeVersion = "test-shape-v1",
        Value = value
    };
}
```

Create golden file `descriptor-package-manifest-v1.json`:

```json
{"formatVersion":"1.0","packageId":"pkg-001","packageVersion":"1","name":"Package","createdAt":"2026-06-25T17:02:03.0000000Z","createdBy":"agent","source":"","descriptorCount":0,"descriptorEntries":[]}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageCanonicalHashWriterTests"
```

Expected: build fails because the writer/computer types do not exist.

- [ ] **Step 3: Implement package canonical writers and computer**

Create writer methods with this exact shape:

```csharp
public static void WritePayload(Utf8JsonWriter writer, DescriptorManifest manifest)
{
    writer.WriteStartObject();
    writer.WriteString("formatVersion", manifest.FormatVersion);
    writer.WriteString("packageId", manifest.PackageId);
    writer.WriteString("packageVersion", manifest.PackageVersion);
    writer.WriteString("name", manifest.Name);
    writer.WriteString("createdAt", manifest.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    writer.WriteString("createdBy", manifest.CreatedBy);
    writer.WriteString("source", manifest.Source);
    writer.WriteNumber("descriptorCount", manifest.DescriptorCount);
    writer.WritePropertyName("descriptorEntries");
    writer.WriteStartArray();
    foreach (var entry in manifest.DescriptorEntries.OrderBy(e => e.Ref.Namespace, StringComparer.Ordinal)
                 .ThenBy(e => e.Ref.Id, StringComparer.Ordinal)
                 .ThenBy(e => e.Ref.Version)
                 .ThenBy(e => e.Kind)
                 .ThenBy(e => e.Name, StringComparer.Ordinal))
    {
        writer.WriteStartObject();
        writer.WriteString("namespace", entry.Ref.Namespace);
        writer.WriteString("id", entry.Ref.Id);
        if (entry.Ref.Version is null) writer.WriteNull("version"); else writer.WriteNumber("version", entry.Ref.Version.Value);
        writer.WriteString("kind", entry.Kind.ToString());
        writer.WriteString("name", entry.Name);
        writer.WriteString("state", entry.State.ToString());
        writer.WriteString("contractHash", entry.ContractHash);
        writer.WriteString("definitionHash", entry.DefinitionHash);
        writer.WriteString("supersededById", entry.SupersededById);
        writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WriteEndObject();
}
```

Create `DescriptorPackageEvidenceCanonicalHashWriter.WritePayload` with fixed property order for all `DescriptorPackageEvidence` fields: topology counts, topology diagnostic counts, impact fields, impact diagnostic counts, compatibility counts, lifecycle fields, and normalized findings. Sort finding/count collections by severity, code, source, message, and related refs using `StringComparer.Ordinal`.

Create `DescriptorPackageEvidenceEnvelopeCanonicalHashWriter.WritePayload` with fixed property order: `packageId`, `packageVersion`, `createdAt`, `createdBy`, `source`, `packageManifestHash`, `packageEvidenceHash`. The `packageManifestHash` and `packageEvidenceHash` properties must be full canonical hash metadata objects containing `algorithm`, `algorithmVersion`, `artifactKind`, `descriptorKind`, `scope`, `purpose`, `contractVersion`, `canonicalShapeVersion`, and `value`, not only `.Value`.

Implement `DefaultDescriptorPackageCanonicalHashComputer` by creating `CanonicalHashProjectionResult` objects with:

```csharp
new CanonicalHashMetadata
{
    ArtifactKind = CanonicalHashArtifactNames.PackageManifest,
    Purpose = CanonicalHashPurposeNames.Integrity,
    Scope = CanonicalHashScopeNames.InternalFull,
    AlgorithmVersion = "sha256-canonical-json-v1",
    ContractVersion = ContractVersions.DescriptorHash,
    CanonicalShapeVersion = DescriptorPackageCanonicalShapeVersions.PackageManifestV1
}
```

Use `PackageEvidence/AuditEvidence` and `PackageEvidenceEnvelope/AuditEvidence` for the other two hashes.

- [ ] **Step 4: Register package canonical hash computer in DI**

In `MetadataServiceCollectionExtensions`, add:

```csharp
services.AddSingleton<IDescriptorPackageCanonicalHashComputer, DefaultDescriptorPackageCanonicalHashComputer>();
```

Add this using to `MetadataServiceCollectionExtensions.cs`:

```csharp
using CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;
```

- [ ] **Step 5: Run focused tests**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageCanonicalHash"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Metadata/CrestCreates.Metadata tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing
git commit -m "feat: compute package canonical hash set"
```

### Task 3: Package Models and Builder Migration

**Files:**
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorManifest.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackage.cs`
- Modify: `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/DescriptorPackagePreview.cs`
- Modify: `src/Metadata/CrestCreates.Metadata/DescriptorPackage/DefaultDescriptorPackageBuilder.cs`
- Modify: `src/Metadata/CrestCreates.Metadata/DescriptorPackage/DescriptorPackageDiffer.cs`
- Modify: `src/Metadata/CrestCreates.Metadata/DescriptorPackage/DescriptorPackageSerializer.cs`
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackageBuilderTests.cs`
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackageSerializerTests.cs`

**Interfaces:**
- Consumes: `IDescriptorPackageCanonicalHashComputer`, `DescriptorPackageHashSet`.
- Produces: `DescriptorPackage.Hashes`, canonical hash package preview fields.

- [ ] **Step 1: Update tests to expect `DescriptorPackage.Hashes`**

In `DescriptorPackageBuilderTests`, replace content/evidence/envelope string assertions with:

```csharp
pkg.Hashes.PackageManifestHash.Value.Should().NotBeEmpty();
pkg.Hashes.PackageEvidenceHash.Value.Should().NotBeEmpty();
pkg.Hashes.PackageEvidenceEnvelopeHash.Value.Should().NotBeEmpty();
pkg.Hashes.PackageManifestHash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.PackageManifest);
```

For snapshot id tests, derive from:

```csharp
var expectedPrefix = pkg.Hashes.PackageManifestHash.Value[..16];
```

- [ ] **Step 2: Run package builder tests and verify failures**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageBuilderTests"
```

Expected: build or assertion failures referencing removed string hash fields.

- [ ] **Step 3: Remove string hash source fields from manifest/package**

In `DescriptorManifest`, remove:

```csharp
public string ContentHash { get; init; } = string.Empty;
public string? EvidenceHash { get; init; }
public string? EnvelopeHash { get; init; }
```

In `DescriptorPackage`, replace `ContentHash` passthrough with:

```csharp
public required DescriptorPackageHashSet Hashes { get; init; }
public required DescriptorPackageEvidenceEnvelope EvidenceEnvelope { get; init; }

public string PackageId => Manifest.PackageId;
public string PackageVersion => Manifest.PackageVersion;
```

- [ ] **Step 4: Inject canonical hash computer into package builder**

Change constructor:

```csharp
private readonly IDescriptorStableHashBuilder _hashBuilder;
private readonly IDescriptorPackageCanonicalHashComputer _packageHashComputer;

public DefaultDescriptorPackageBuilder(
    IDescriptorStableHashBuilder hashBuilder,
    IDescriptorPackageCanonicalHashComputer packageHashComputer)
{
    _hashBuilder = hashBuilder;
    _packageHashComputer = packageHashComputer;
}
```

Build manifest without hash fields, then compute hash set from manifest,
evidence, and envelope metadata:

```csharp
var envelopeMetadata = new DescriptorPackageEvidenceEnvelopeMetadata
{
    PackageId = request.PackageId,
    PackageVersion = request.PackageVersion,
    CreatedAt = createdAt,
    CreatedBy = request.CreatedBy,
    Source = request.Source
};

var hashSet = _packageHashComputer.ComputeHashSet(
    manifest,
    evidence,
    envelopeMetadata);

var envelope = new DescriptorPackageEvidenceEnvelope
{
    PackageId = envelopeMetadata.PackageId,
    PackageVersion = envelopeMetadata.PackageVersion,
    CreatedAt = envelopeMetadata.CreatedAt,
    CreatedBy = envelopeMetadata.CreatedBy,
    Source = envelopeMetadata.Source,
    PackageManifestHash = hashSet.PackageManifestHash,
    PackageEvidenceHash = hashSet.PackageEvidenceHash
};
```

- [ ] **Step 5: Remove production calls to `DescriptorPackageHashComputer`**

Delete these calls from `DefaultDescriptorPackageBuilder`:

```csharp
DescriptorPackageHashComputer.ComputeEvidenceHash(...)
DescriptorPackageHashComputer.ComputeContentHash(...)
DescriptorPackageHashComputer.ComputeEnvelopeHash(...)
```

Return:

```csharp
return new Package
{
    Manifest = manifest,
    Snapshot = snapshot,
    Evidence = evidence,
    EvidenceEnvelope = envelope,
    Hashes = hashSet,
    Diagnostics = diagnostics
};
```

Set `SnapshotId` from `hashSet.PackageManifestHash.Value[..16]`.

- [ ] **Step 6: Update preview model**

Change `DescriptorPackagePreview` to:

```csharp
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorPackagePreview
{
    public required CanonicalHash PackageManifestHash { get; init; }
    public required CanonicalHash PackageEvidenceHash { get; init; }
    public required CanonicalHash PackageEvidenceEnvelopeHash { get; init; }
    public required IReadOnlyList<string> DescriptorIds { get; init; }
}
```

- [ ] **Step 7: Run metadata tests**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests
```

Expected: package tests pass after updating serializer/differ assertions to `pkg.Hashes.*`.

- [ ] **Step 8: Commit**

```bash
git add src/Metadata tests/Metadata/Core/CrestCreates.Metadata.Tests
git commit -m "feat: migrate descriptor package hashes to canonical hash set"
```

### Task 4: DescriptorDraft Review Hash Service

**Files:**
- Create: `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CanonicalHashing/IDescriptorDraftReviewHashService.cs`
- Create: `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CanonicalHashing/ReviewDiagnosticProjection.cs`
- Create: `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CanonicalHashing/ReviewResultSourceBindingProjection.cs`
- Create: `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CanonicalHashing/ReviewResultIntegrityProjection.cs`
- Create: `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CanonicalHashing/DescriptorDraftReviewCanonicalShapeVersions.cs`
- Create: `src/Metadata/Draft/CrestCreates.DescriptorDraft/CanonicalHashing/DefaultDescriptorDraftReviewHashService.cs`
- Create: `src/Metadata/Draft/CrestCreates.DescriptorDraft/CanonicalHashing/ReviewResultSourceBindingCanonicalHashWriter.cs`
- Create: `src/Metadata/Draft/CrestCreates.DescriptorDraft/CanonicalHashing/ReviewResultIntegrityCanonicalHashWriter.cs`
- Modify: `src/Metadata/Draft/CrestCreates.DescriptorDraft/DescriptorDraftServiceCollectionExtensions.cs`
- Test: `tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/CanonicalHashing/DescriptorDraftReviewHashServiceTests.cs`
- Test: `tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/CanonicalHashing/ReviewResultCanonicalHashWriterTests.cs`

**Interfaces:**
- Produces: `IDescriptorDraftReviewHashService.ComputeSourceReviewHash`, `ComputeReviewManifestHash`.
- Consumes: `DescriptorDraftReviewResult`, `ICanonicalHashComputer`.

- [ ] **Step 1: Write failing ReviewResult hash service tests**

Create `DescriptorDraftReviewHashServiceTests.cs`:

```csharp
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;

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
        hash.CanonicalShapeVersion.Should().Be(DescriptorDraftReviewCanonicalShapeVersions.SourceBindingV1);
    }

    [Fact]
    public void ComputeReviewManifestHash_Uses_ReviewResult_Integrity_Metadata()
    {
        var service = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());

        var hash = service.ComputeReviewManifestHash(CreateReview());

        hash.ArtifactKind.Should().Be(CanonicalHashArtifactNames.ReviewResult);
        hash.Purpose.Should().Be(CanonicalHashPurposeNames.Integrity);
        hash.CanonicalShapeVersion.Should().Be(DescriptorDraftReviewCanonicalShapeVersions.IntegrityV1);
    }

    private static DescriptorDraftReviewResult CreateReview() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-1",
        IsActivationEligible = true,
        ValidationResult = new DescriptorDraftValidationResult { IsValid = true },
        Diagnostics = Array.Empty<DescriptorDraftDiagnostic>()
    };
}
```

- [ ] **Step 2: Run DescriptorDraft tests and verify failure**

Run:

```bash
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests --filter "FullyQualifiedName~DescriptorDraftReviewHashServiceTests"
```

Expected: build fails because new service types do not exist.

- [ ] **Step 3: Add DescriptorDraft review hash contracts**

Create `IDescriptorDraftReviewHashService`:

```csharp
namespace CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

public interface IDescriptorDraftReviewHashService
{
    CanonicalHash ComputeSourceReviewHash(DescriptorDraftReviewResult reviewResult);
    CanonicalHash ComputeReviewManifestHash(DescriptorDraftReviewResult reviewResult);
}
```

Add `using CrestCreates.Metadata.Abstractions.CanonicalHashing;`.

Create `DescriptorDraftReviewCanonicalShapeVersions`:

```csharp
namespace CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

public static class DescriptorDraftReviewCanonicalShapeVersions
{
    public const string SourceBindingV1 = "descriptor-draft-review-source-binding-v1";
    public const string IntegrityV1 = "descriptor-draft-review-integrity-v1";
}
```

Create projection records with exact property names:

```csharp
public sealed record ReviewDiagnosticProjection
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
}
```

```csharp
public sealed record ReviewResultSourceBindingProjection
{
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required bool IsActivationEligible { get; init; }
    public required bool IsValid { get; init; }
    public required IReadOnlyList<ReviewDiagnosticProjection> Diagnostics { get; init; }
    public string? GovernanceDecision { get; init; }
    public string? ImpactSeverity { get; init; }
}
```

```csharp
public sealed record ReviewResultIntegrityProjection
{
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required bool IsActivationEligible { get; init; }
    public required bool IsValid { get; init; }
    public required int DiagnosticCount { get; init; }
}
```

- [ ] **Step 4: Implement review writers and service**

Implement `DefaultDescriptorDraftReviewHashService`:

```csharp
public sealed class DefaultDescriptorDraftReviewHashService : IDescriptorDraftReviewHashService
{
    private readonly ICanonicalHashComputer _hashComputer;

    public DefaultDescriptorDraftReviewHashService(ICanonicalHashComputer hashComputer)
        => _hashComputer = hashComputer;

    public CanonicalHash ComputeSourceReviewHash(DescriptorDraftReviewResult reviewResult)
    {
        var projection = ReviewResultSourceBindingProjectionFactory.Create(reviewResult);
        return _hashComputer.ComputeFromProjection(CanonicalHashProjectionResult.Create(
            Metadata(CanonicalHashPurposeNames.SourceBinding, DescriptorDraftReviewCanonicalShapeVersions.SourceBindingV1),
            writer => ReviewResultSourceBindingCanonicalHashWriter.WritePayload(writer, projection)));
    }

    public CanonicalHash ComputeReviewManifestHash(DescriptorDraftReviewResult reviewResult)
    {
        var projection = ReviewResultIntegrityProjectionFactory.Create(reviewResult);
        return _hashComputer.ComputeFromProjection(CanonicalHashProjectionResult.Create(
            Metadata(CanonicalHashPurposeNames.Integrity, DescriptorDraftReviewCanonicalShapeVersions.IntegrityV1),
            writer => ReviewResultIntegrityCanonicalHashWriter.WritePayload(writer, projection)));
    }

    private static CanonicalHashMetadata Metadata(string purpose, string shapeVersion) => new()
    {
        ArtifactKind = CanonicalHashArtifactNames.ReviewResult,
        Purpose = purpose,
        Scope = CanonicalHashScopeNames.InternalFull,
        AlgorithmVersion = "sha256-canonical-json-v1",
        ContractVersion = ContractVersions.DescriptorHash,
        CanonicalShapeVersion = shapeVersion
    };
}
```

If `ContractVersions.DescriptorHash` is not visible from DescriptorDraft, add a local constant class in DescriptorDraft abstractions named `DescriptorDraftReviewHashContractVersions` with `public const string Current = "descriptor-draft-review-hash-v1";` and use it consistently.

- [ ] **Step 5: Register DescriptorDraft review hash service**

In `DescriptorDraftServiceCollectionExtensions`, add:

```csharp
services.AddSingleton<IDescriptorDraftReviewHashService, DefaultDescriptorDraftReviewHashService>();
```

- [ ] **Step 6: Run DescriptorDraft tests**

Run:

```bash
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Metadata/Draft tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/CanonicalHashing
git commit -m "feat: add descriptor draft review canonical hashing"
```

### Task 5: Activation Binding Contracts, Resolver, and Validator

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Activation/BindingHashes.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Activation/IActivationBindingArtifactResolver.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/InMemoryActivationBindingArtifactResolver.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/ActivationBindingHashValidator.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Activation/ActivationBindingHashValidatorTests.cs`

**Interfaces:**
- Consumes: `DescriptorPackageHashSet`.
- Produces: 7-slot `BindingHashes`, package preview/evidence preview hash set resolver storage, validator.

- [ ] **Step 1: Write failing validator tests**

Create `ActivationBindingHashValidatorTests.cs`:

```csharp
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;

namespace CrestCreates.Agent.ControlPlane.Tests.Activation;

public sealed class ActivationBindingHashValidatorTests
{
    [Fact]
    public void Validate_Rejects_Correct_Value_With_Wrong_Purpose()
    {
        var hashes = CreateValidHashes() with
        {
            PackageEvidenceHash = CreateHash(CanonicalHashArtifactNames.PackageEvidence, CanonicalHashPurposeNames.Integrity, "same-value")
        };

        var result = ActivationBindingHashValidator.Validate(hashes);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("PackageEvidenceHash"));
    }

    [Fact]
    public void Validate_Accepts_All_Expected_Slots()
    {
        var result = ActivationBindingHashValidator.Validate(CreateValidHashes());

        result.IsValid.Should().BeTrue();
    }

    private static BindingHashes CreateValidHashes() => new()
    {
        SourceReviewHash = CreateHash(CanonicalHashArtifactNames.ReviewResult, CanonicalHashPurposeNames.SourceBinding, "source"),
        ReviewManifestHash = CreateHash(CanonicalHashArtifactNames.ReviewResult, CanonicalHashPurposeNames.Integrity, "review"),
        PackageManifestHash = CreateHash(CanonicalHashArtifactNames.PackageManifest, CanonicalHashPurposeNames.Integrity, "manifest"),
        PackageEvidenceHash = CreateHash(CanonicalHashArtifactNames.PackageEvidence, CanonicalHashPurposeNames.AuditEvidence, "evidence"),
        PackageEvidenceEnvelopeHash = CreateHash(CanonicalHashArtifactNames.PackageEvidenceEnvelope, CanonicalHashPurposeNames.AuditEvidence, "envelope"),
        ContractHash = CreateHash(CanonicalHashArtifactNames.Descriptor, CanonicalHashPurposeNames.Contract, "contract"),
        DefinitionHash = CreateHash(CanonicalHashArtifactNames.Descriptor, CanonicalHashPurposeNames.Definition, "definition")
    };

    private static CanonicalHash CreateHash(string artifactKind, string purpose, string value) => new()
    {
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = artifactKind,
        Scope = CanonicalHashScopeNames.InternalFull,
        Purpose = purpose,
        ContractVersion = "test-contract-v1",
        CanonicalShapeVersion = "test-shape-v1",
        Value = value
    };
}
```

- [ ] **Step 2: Run validator tests and verify failure**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~ActivationBindingHashValidatorTests"
```

Expected: build fails because `PackageEvidenceHash`, `PackageEvidenceEnvelopeHash`, or validator does not exist.

- [ ] **Step 3: Update `BindingHashes`**

Replace `ManifestHash`, `EvidenceHash`, and `EnvelopeHash` with:

```csharp
public required CanonicalHash ReviewManifestHash { get; init; }
public required CanonicalHash PackageManifestHash { get; init; }
public required CanonicalHash PackageEvidenceHash { get; init; }
public required CanonicalHash PackageEvidenceEnvelopeHash { get; init; }
```

Keep `SourceReviewHash`, `ContractHash`, and `DefinitionHash`.

- [ ] **Step 4: Update resolver contract**

Change `IActivationBindingArtifactResolver` methods to:

```csharp
void StoreReviewHashes(
    string tenantId,
    string reviewResultId,
    CanonicalHash sourceReviewHash,
    CanonicalHash reviewManifestHash);

void StorePackagePreviewHashSet(
    string tenantId,
    string packagePreviewId,
    DescriptorPackageHashSet hashSet);

void StoreEvidencePreviewHashSet(
    string tenantId,
    string evidencePreviewId,
    DescriptorPackageHashSet hashSet);
```

Change `ResolvedBindingArtifacts` to expose:

```csharp
public CanonicalHash? CurrentSourceReviewHash { get; init; }
public CanonicalHash? CurrentReviewManifestHash { get; init; }
public CanonicalHash? CurrentPackageManifestHash { get; init; }
public CanonicalHash? CurrentPackageEvidenceHash { get; init; }
public CanonicalHash? CurrentPackageEvidenceEnvelopeHash { get; init; }
public CanonicalHash? CurrentContractHash { get; init; }
public CanonicalHash? CurrentDefinitionHash { get; init; }
```

- [ ] **Step 5: Update in-memory resolver**

Use these dictionaries:

```csharp
private readonly ConcurrentDictionary<(string TenantId, string ReviewResultId), (CanonicalHash SourceReviewHash, CanonicalHash ReviewManifestHash)> _reviewHashes = new();
private readonly ConcurrentDictionary<(string TenantId, string PackagePreviewId), DescriptorPackageHashSet> _packagePreviewHashSets = new();
private readonly ConcurrentDictionary<(string TenantId, string EvidencePreviewId), DescriptorPackageHashSet> _evidencePreviewHashSets = new();
```

When resolving, prefer package preview hash set for `PackageManifestHash` and `PackageEvidenceHash`, and evidence preview hash set for `PackageEvidenceEnvelopeHash`. If both references exist, validate later in rechecker that hash set values match.

- [ ] **Step 6: Implement `ActivationBindingHashValidator`**

Create:

```csharp
public sealed record ActivationBindingHashValidationResult(bool IsValid, IReadOnlyList<string> Errors);
```

Implement:

```csharp
public static ActivationBindingHashValidationResult Validate(BindingHashes? hashes)
{
    var errors = new List<string>();
    if (hashes is null)
        return new(false, ["BindingHashes is required."]);

    ValidateSlot(errors, nameof(BindingHashes.SourceReviewHash), hashes.SourceReviewHash, CanonicalHashArtifactNames.ReviewResult, CanonicalHashPurposeNames.SourceBinding);
    ValidateSlot(errors, nameof(BindingHashes.ReviewManifestHash), hashes.ReviewManifestHash, CanonicalHashArtifactNames.ReviewResult, CanonicalHashPurposeNames.Integrity);
    ValidateSlot(errors, nameof(BindingHashes.PackageManifestHash), hashes.PackageManifestHash, CanonicalHashArtifactNames.PackageManifest, CanonicalHashPurposeNames.Integrity);
    ValidateSlot(errors, nameof(BindingHashes.PackageEvidenceHash), hashes.PackageEvidenceHash, CanonicalHashArtifactNames.PackageEvidence, CanonicalHashPurposeNames.AuditEvidence);
    ValidateSlot(errors, nameof(BindingHashes.PackageEvidenceEnvelopeHash), hashes.PackageEvidenceEnvelopeHash, CanonicalHashArtifactNames.PackageEvidenceEnvelope, CanonicalHashPurposeNames.AuditEvidence);
    ValidateSlot(errors, nameof(BindingHashes.ContractHash), hashes.ContractHash, CanonicalHashArtifactNames.Descriptor, CanonicalHashPurposeNames.Contract);
    ValidateSlot(errors, nameof(BindingHashes.DefinitionHash), hashes.DefinitionHash, CanonicalHashArtifactNames.Descriptor, CanonicalHashPurposeNames.Definition);

    return new(errors.Count == 0, errors);
}
```

`ValidateSlot` must reject empty `Value`, `Algorithm`, `AlgorithmVersion`, `ArtifactKind`, `Purpose`, `Scope`, `ContractVersion`, and `CanonicalShapeVersion`, and must require `Scope == CanonicalHashScopeNames.InternalFull`.

- [ ] **Step 7: Run activation validator tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~ActivationBindingHashValidatorTests"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Activation src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Activation
git commit -m "feat: add activation binding hash validation"
```

### Task 6: Agent Tool Flow Migration

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/DefaultActivationEvidenceRechecker.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/DefaultDescriptorActivationRequestService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewReportBuilder.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Activation/ActivationBindingCanonicalHashFlowTests.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Activation/ActivationEvidenceRecheckerCanonicalHashTests.cs`

**Interfaces:**
- Consumes: `IDescriptorDraftReviewHashService`, `DescriptorPackageHashSet`, `ActivationBindingHashValidator`.
- Produces: Agent flow with no production hash construction.

- [ ] **Step 1: Write failing flow guard test**

Create `ActivationBindingCanonicalHashFlowTests.cs`:

```csharp
using System.IO;
using FluentAssertions;

namespace CrestCreates.Agent.ControlPlane.Tests.Activation;

public sealed class ActivationBindingCanonicalHashFlowTests
{
    [Fact]
    public void ToolService_Does_Not_Contain_AdHoc_Activation_Hash_Production()
    {
        var source = File.ReadAllText("../../../../src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs");

        source.Should().NotContain("sha256-adhoc-v1");
        source.Should().NotContain("ComputeSha256(");
        source.Should().NotContain("ComputeSourceReviewHash(");
        source.Should().NotContain("ComputeReviewManifestHash(");
        source.Should().NotContain("new CanonicalHash");
    }
}
```

- [ ] **Step 2: Run the guard test and verify failure**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~ToolService_Does_Not_Contain_AdHoc_Activation_Hash_Production"
```

Expected: FAIL because current ToolService contains those helpers.

- [ ] **Step 3: Inject DescriptorDraft review hash service into ToolService**

Add constructor dependency:

```csharp
private readonly IDescriptorDraftReviewHashService _reviewHashService;
```

Use it where review results are created:

```csharp
var sourceReviewHash = _reviewHashService.ComputeSourceReviewHash(reviewResult);
var reviewManifestHash = _reviewHashService.ComputeReviewManifestHash(reviewResult);
_artifactResolver.StoreReviewHashes(context.TenantId, reviewId, sourceReviewHash, reviewManifestHash);
```

Remove `CreateReviewCanonicalHash`, `CreatePackageCanonicalHash`, `ComputeSourceReviewHash`, `ComputeReviewManifestHash`, and local `ComputeSha256`.

- [ ] **Step 4: Store package hash sets from package builder output**

When creating package preview:

```csharp
var preview = new DescriptorPackagePreview
{
    PackageManifestHash = pkg.Hashes.PackageManifestHash,
    PackageEvidenceHash = pkg.Hashes.PackageEvidenceHash,
    PackageEvidenceEnvelopeHash = pkg.Hashes.PackageEvidenceEnvelopeHash,
    DescriptorIds = pkg.Manifest.DescriptorEntries.Select(e => e.Ref.Id).ToList().AsReadOnly()
};

_artifactResolver.StorePackagePreviewHashSet(context.TenantId, previewId, pkg.Hashes);
```

When creating evidence preview, prefer the matching package preview. If absent, build once and store:

```csharp
_artifactResolver.StoreEvidencePreviewHashSet(context.TenantId, evidencePreviewId, pkg.Hashes);
```

- [ ] **Step 5: Call validator at submit**

In `SubmitActivationRequestAsync`, after checking `BindingSnapshot.Hashes` is present:

```csharp
var hashValidation = ActivationBindingHashValidator.Validate(request.BindingSnapshot.Hashes);
if (!hashValidation.IsValid)
{
    return await RecordAndReturn(context,
        AgentToolResult<ActivationRequest>.InvalidRequest(hashValidation.Errors.Select(e => new AgentToolDiagnostic
        {
            Code = DescriptorActivationDiagnosticCodes.BindingHashesRequiredValue,
            Severity = AgentToolDiagnosticSeverity.Error,
            Message = e
        }).ToList()));
}
```

- [ ] **Step 6: Call validator in recheck and pre-gate**

In `DefaultActivationEvidenceRechecker.RecheckAsync`, validate bound hashes before resolving. Add drift entries for validation errors and return stale.

Update compare names:

```csharp
CompareHash(drifts, "ReviewManifestHash", bindingSnapshot.Hashes.ReviewManifestHash, resolvedArtifacts.CurrentReviewManifestHash);
CompareHash(drifts, "PackageManifestHash", bindingSnapshot.Hashes.PackageManifestHash, resolvedArtifacts.CurrentPackageManifestHash);
CompareHash(drifts, "PackageEvidenceHash", bindingSnapshot.Hashes.PackageEvidenceHash, resolvedArtifacts.CurrentPackageEvidenceHash);
CompareHash(drifts, "PackageEvidenceEnvelopeHash", bindingSnapshot.Hashes.PackageEvidenceEnvelopeHash, resolvedArtifacts.CurrentPackageEvidenceEnvelopeHash);
```

In `DefaultDescriptorActivationRequestService.ExecuteActivationGateAsync`, call validator immediately before `_activationGate.ActivateAsync(request, ct)`.

- [ ] **Step 7: Update report builder display projections**

In `DefaultDescriptorReviewReportBuilder`, any displayed package hash should use `.Value` from canonical objects. Do not compute report source review hash with local SHA-256 if it is meant for activation binding. If report id remains separate and not activation binding, leave it only if guard tests allow it.

- [ ] **Step 8: Run Agent ControlPlane tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests
```

Expected: PASS after updating fixture property names and assertions.

- [ ] **Step 9: Commit**

```bash
git add src/Runtime/Agent tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests
git commit -m "feat: consume canonical hashes in activation flow"
```

### Task 7: Golden Tests and Guard Tests

**Files:**
- Modify: `tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/CanonicalHashing/ReviewResultCanonicalHashWriterTests.cs`
- Modify: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/DescriptorPackageCanonicalHashWriterTests.cs`
- Create: `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/AgentActivationCanonicalHashGuardTests.cs`

**Interfaces:**
- Consumes: all production changes.
- Produces: regression coverage for bytes, metadata, and forbidden ad hoc paths.

- [ ] **Step 1: Strengthen golden tests with metadata assertions**

For each golden test, assert:

```csharp
hash.Algorithm.Should().Be("SHA-256");
hash.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");
hash.ArtifactKind.Should().Be(expectedArtifactKind);
hash.Purpose.Should().Be(expectedPurpose);
hash.Scope.Should().Be(CanonicalHashScopeNames.InternalFull);
hash.ContractVersion.Should().NotBeNullOrWhiteSpace();
hash.CanonicalShapeVersion.Should().Be(expectedShapeVersion);
```

- [ ] **Step 2: Add boundary guard tests**

Create `AgentActivationCanonicalHashGuardTests.cs`:

```csharp
using FluentAssertions;

namespace CrestCreates.DependencyBoundaries.Tests;

public sealed class AgentActivationCanonicalHashGuardTests
{
    [Fact]
    public void Agent_ControlPlane_Does_Not_Produce_AdHoc_Activation_Hashes()
    {
        var files = Directory.GetFiles("../../../../src/Runtime/Agent/CrestCreates.Agent.ControlPlane", "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("DescriptorActivationReviewDecisionParser.cs", StringComparison.Ordinal))
            .ToArray();

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            source.Should().NotContain("sha256-adhoc-v1", file);
            source.Should().NotContain("ComputeSha256(", file);
            source.Should().NotContain("ComputeSourceReviewHash(", file);
            source.Should().NotContain("ComputeReviewManifestHash(", file);
            source.Should().NotContain("new CanonicalHash", file);
        }
    }

    [Fact]
    public void Package_Builder_Does_Not_Call_Legacy_DescriptorPackageHashComputer()
    {
        var source = File.ReadAllText("../../../../src/Metadata/CrestCreates.Metadata/DescriptorPackage/DefaultDescriptorPackageBuilder.cs");

        source.Should().NotContain("DescriptorPackageHashComputer");
        source.Should().NotContain("ContentHash");
    }

    [Fact]
    public void Hash_Protocol_Does_Not_Reintroduce_Pipe_Delimited_StringBuilder()
    {
        var files = Directory.GetFiles("../../../../src", "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains("CrestCreates.Agent.ControlPlane", StringComparison.Ordinal)
                || path.Contains("CrestCreates.DescriptorDraft", StringComparison.Ordinal)
                || path.Contains("DescriptorPackage", StringComparison.Ordinal))
            .ToArray();

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var suspicious = source.Contains("StringBuilder", StringComparison.Ordinal)
                && source.Contains("Append('|')", StringComparison.Ordinal)
                && (source.Contains("Hash", StringComparison.Ordinal) || source.Contains("SHA256", StringComparison.Ordinal));

            suspicious.Should().BeFalse(file);
        }
    }
}
```

- [ ] **Step 3: Run boundary tests**

Run:

```bash
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests --filter "FullyQualifiedName~AgentActivationCanonicalHashGuardTests"
```

Expected: PASS.

- [ ] **Step 4: Run golden tests**

Run:

```bash
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests --filter "FullyQualifiedName~CanonicalHashing"
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~CanonicalHashing"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/Boundary/CrestCreates.DependencyBoundaries.Tests tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests tests/Metadata/Core/CrestCreates.Metadata.Tests
git commit -m "test: guard activation canonical hash main chain"
```

### Task 8: Final Repository-Wide Migration and Verification

**Files:**
- Modify any remaining compile errors from renamed fields in `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/**/*.cs`
- Modify any remaining compile errors from package hash model changes in `tests/Metadata/Core/CrestCreates.Metadata.Tests/**/*.cs`
- Modify docs if examples reference removed hash fields:
  - `docs/Feature/AgentControlPlane/usage-guide.md`
  - `docs/Feature/AgentControlPlane/arch-design.md`
  - `memory.md`

**Interfaces:**
- Consumes: completed tasks 1-7.
- Produces: repository compiling and documented final state.

- [ ] **Step 1: Search for removed names**

Run:

```bash
rg -n "ManifestHash|EvidenceHash|EnvelopeHash|ContentHash|DescriptorPackageHashComputer|sha256-adhoc-v1|ComputeSha256|ComputeSourceReviewHash|ComputeReviewManifestHash" src tests docs memory.md
```

Expected remaining matches:

- Current-state notes in historical docs are acceptable only if clearly marked as historical.
- DTO/display projections may reference `ContentHash`.
- Tests may reference old names only when asserting they do not exist.

- [ ] **Step 2: Fix compile errors from renamed binding fields**

Replace production and test fixture usage:

```text
ManifestHash -> ReviewManifestHash or PackageManifestHash based on context
EvidenceHash -> PackageEvidenceHash
EnvelopeHash -> PackageEvidenceEnvelopeHash
```

For activation decisions, update:

```csharp
BoundEvidenceHash = request.BindingSnapshot.Hashes.PackageEvidenceHash;
BoundEnvelopeHash = request.BindingSnapshot.Hashes.PackageEvidenceEnvelopeHash;
```

Keep decision property names unless the implementation intentionally breaks review decision DTO names too.

- [ ] **Step 3: Run focused suites**

Run:

```bash
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

Expected: PASS.

- [ ] **Step 4: Run full build**

Run:

```bash
dotnet build
```

Expected: 0 errors.

- [ ] **Step 5: Update memory and feature docs**

In `memory.md`, add a short dated note:

```markdown
### Agent Activation Canonical Evidence Hashing (#47, 2026-06-26)

- Replaced activation/package/evidence string digest binding with canonical `CanonicalHash` production.
- ReviewResult hashes are owned by DescriptorDraft.
- Package manifest/evidence/envelope hashes are owned by Metadata DescriptorPackage and move as `DescriptorPackageHashSet`.
- Agent Control Plane consumes and validates canonical hashes; it does not produce ad hoc hashes.
```

Update Agent Control Plane docs to use `ReviewManifestHash`, `PackageManifestHash`, `PackageEvidenceHash`, and `PackageEvidenceEnvelopeHash`.

- [ ] **Step 6: Run final verification**

Run:

```bash
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
dotnet build
git status --short
```

Expected:

- all tests pass,
- build has 0 errors,
- `git status --short` shows only intentional changes before final commit.

- [ ] **Step 7: Commit**

```bash
git add src tests docs memory.md
git commit -m "feat: canonicalize agent activation evidence hashing"
```

---

## Self-Review Notes

- Spec coverage: tasks cover DescriptorDraft ownership, package hash ownership, `DescriptorPackageHashSet`, 7-slot `BindingHashes`, resolver naming, validator at submit/recheck/pre-gate, golden tests, and guard tests.
- Scope: this remains one breaking migration with tightly coupled model and flow changes; splitting further would leave temporary dual hash paths.
- Type consistency: use `PackageEvidenceHash` and `PackageEvidenceEnvelopeHash` everywhere in new package/domain/activation binding models. Existing review decision property names may remain `BoundEvidenceHash`/`BoundEnvelopeHash` as DTO wording unless deliberately broken in Task 8.
- Implementation warning: if `DescriptorPackageEvidenceEnvelope` cannot include computed hashes before computing the envelope hash, introduce `DescriptorPackageEvidenceEnvelopeMetadata` and let `DefaultDescriptorPackageCanonicalHashComputer` construct the full envelope projection internally.
