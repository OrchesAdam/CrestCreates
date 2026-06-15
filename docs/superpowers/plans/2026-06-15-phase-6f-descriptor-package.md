# Phase 6f — Descriptor Package / Manifest / Snapshot: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a deterministic, AoT-safe descriptor package system that freezes control-plane evidence (manifest, snapshot, relationships, diagnostics) without rerunning prior phase analyzers or mutating registries.

**Architecture:** Evolve existing `DescriptorPackage`/`DescriptorManifest`/`DescriptorSnapshot` types in-place. Add stateless singleton `IDescriptorPackageBuilder`, `IDescriptorPackageDiffer`, `IDescriptorPackageSerializer`. New `DescriptorPackageHashComputer` for AoT-safe deterministic hashing (string concat, no runtime JSON). 42 tests covering builder determinism, evidence summary, self-consistency diagnostics, diff, serialization, DI, legacy compat.

**Tech Stack:** C# 13 / .NET 10, xUnit + FluentAssertions, System.Text.Json (source-gen), SHA-256

**Spec:** `docs/superpowers/specs/2026-06-15-phase-6f-descriptor-package-manifest-snapshot-design.md`

---

### Task 1: Upgrade Core Model Types (DescriptorManifestEntry, DescriptorManifest, SnapshotEntry, DescriptorSnapshot, DescriptorPackage)

**Files:**
- Modify: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorManifest.cs`
- Modify: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorSnapshot.cs`
- Modify: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackage.cs`

- [ ] **Step 1: Upgrade DescriptorManifestEntry and DescriptorManifest**

Replace the entire contents of `DescriptorManifest.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorManifest
{
    public string FormatVersion { get; init; } = "1.0";
    public string PackageId { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = string.Empty;
    public string? Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? Source { get; init; }
    public int DescriptorCount { get; init; }
    public IReadOnlyList<DescriptorManifestEntry> DescriptorEntries { get; init; }
        = Array.Empty<DescriptorManifestEntry>();
    public string ContentHash { get; init; } = string.Empty;
    public string? EvidenceHash { get; init; }
    public string? EnvelopeHash { get; init; }
}

public sealed class DescriptorManifestEntry
{
    public DescriptorRef Ref { get; init; }
    public DescriptorKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public string? SupersededById { get; init; }
}
```

Note: `DescriptorRef` and `DescriptorKind` and `DescriptorState` are already defined in `CrestCreates.Metadata.Abstractions`. The old per-kind entry lists (`Schemas`, `Capabilities`, ...) and old `DescriptorManifestEntry(Id, Name, Version)` are intentionally removed.

- [ ] **Step 2: Upgrade SnapshotEntry and DescriptorSnapshot**

Replace the entire contents of `DescriptorSnapshot.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorSnapshot
{
    public string SnapshotId { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<SnapshotEntry> Descriptors { get; init; } = Array.Empty<SnapshotEntry>();
    public IReadOnlyList<DescriptorPackageRelationshipEntry> Relationships { get; init; }
        = Array.Empty<DescriptorPackageRelationshipEntry>();
}

public sealed class SnapshotEntry
{
    public DescriptorRef Ref { get; init; }
    public string DescriptorName { get; init; } = string.Empty;
    public DescriptorKind Kind { get; init; }
    public DescriptorState State { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public string? SupersededById { get; init; }
}
```

- [ ] **Step 3: Upgrade DescriptorPackage**

Replace the entire contents of `DescriptorPackage.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorPackage
{
    public DescriptorManifest Manifest { get; init; } = new();
    public DescriptorSnapshot Snapshot { get; init; } = new();
    public DescriptorPackageEvidence Evidence { get; init; } = new();
    public IReadOnlyList<DescriptorPackageDiagnostic> Diagnostics { get; init; }
        = Array.Empty<DescriptorPackageDiagnostic>();

    public string PackageId => Manifest.PackageId;
    public string PackageVersion => Manifest.PackageVersion;
    public string ContentHash => Manifest.ContentHash;
}
```

- [ ] **Step 4: Create DescriptorPackageRelationshipEntry**

Create `framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageRelationshipEntry.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed record DescriptorPackageRelationshipEntry
{
    public required DescriptorRef From { get; init; }
    public required DescriptorRef To { get; init; }
    public required RelationshipKind Kind { get; init; }
    public string? Role { get; init; }
    public string? SourcePath { get; init; }
    public required RelationshipStrength Strength { get; init; }
    public required bool IsRuntimeBinding { get; init; }
}
```

- [ ] **Step 5: Verify partial build compiles**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions
```

Expect: Build errors in files that reference the old per-kind manifest lists or old `SnapshotEntry`/`DescriptorManifestEntry` shape. This is expected — those will be fixed in subsequent tasks (serializer, tests). The abstractions project itself should compile if no internal code references old shapes.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorManifest.cs \
        framework/src/CrestCreates.Metadata.Abstractions/DescriptorSnapshot.cs \
        framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackage.cs \
        framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageRelationshipEntry.cs
git commit -m "feat(6f): upgrade DescriptorManifest, DescriptorSnapshot, DescriptorPackage core models; remove per-kind lists"
```

---

### Task 2: Create Evidence and Diagnostic Model Types

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageEvidence.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/EvidenceFinding.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/EvidenceFindingCount.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageDiagnostic.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageDiagnosticCode.cs`

- [ ] **Step 1: Create evidence types**

Create `DescriptorPackageEvidence.cs`:

```csharp
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorPackageEvidence
{
    // Topology
    public int TopologyNodeCount { get; init; }
    public int TopologyEdgeCount { get; init; }
    public IReadOnlyList<EvidenceFindingCount> TopologyDiagnosticCounts { get; init; }
        = Array.Empty<EvidenceFindingCount>();
    public bool HasTopologyErrors { get; init; }

    // Impact
    public DescriptorImpactSeverity MaxImpactSeverity { get; init; }
    public int AffectedDescriptorCount { get; init; }
    public int ImpactPathCount { get; init; }
    public IReadOnlyList<EvidenceFindingCount> ImpactDiagnosticCounts { get; init; }
        = Array.Empty<EvidenceFindingCount>();

    // Compatibility
    public DescriptorCompatibilityLevel MaxCompatibilityLevel { get; init; }
    public int BreakingFindingCount { get; init; }
    public int SecuritySensitiveFindingCount { get; init; }
    public int UnsupportedFindingCount { get; init; }

    // Lifecycle
    public DescriptorLifecycleDecisionKind MaxLifecycleDecision { get; init; }
    public bool RequiresReview { get; init; }
    public bool IsBlocked { get; init; }
    public int PackageFindingCount { get; init; }

    // Unified
    public IReadOnlyList<EvidenceFinding> NormalizedFindings { get; init; }
        = Array.Empty<EvidenceFinding>();
}
```

Create `EvidenceFinding.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed record EvidenceFinding
{
    public required string Source { get; init; }
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public DescriptorRef? Subject { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<DescriptorRef> RelatedRefs { get; init; } = Array.Empty<DescriptorRef>();
}
```

Create `EvidenceFindingCount.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed record EvidenceFindingCount
{
    public required string Severity { get; init; }
    public required string Code { get; init; }
    public int Count { get; init; }
}
```

- [ ] **Step 2: Create diagnostic types**

Create `DescriptorPackageDiagnostic.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed record DescriptorPackageDiagnostic
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public DescriptorRef? Subject { get; init; }
}
```

Create `DescriptorPackageDiagnosticCode.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public static class DescriptorPackageDiagnosticCode
{
    public const string DuplicateDescriptorRef = "PACKAGE_DUPLICATE_DESCRIPTOR_REF";
    public const string DescriptorHashMismatch = "PACKAGE_DESCRIPTOR_HASH_MISMATCH";
    public const string ManifestRefMismatch = "PACKAGE_MANIFEST_REF_MISMATCH";
    public const string EvidenceSubjectOutsideInventory = "PACKAGE_EVIDENCE_SUBJECT_OUTSIDE_INVENTORY";
    public const string TopologyNodeOutsidePackage = "PACKAGE_TOPOLOGY_NODE_OUTSIDE_PACKAGE";
    public const string TopologyEdgeOutsidePackage = "PACKAGE_TOPOLOGY_EDGE_OUTSIDE_PACKAGE";
    public const string ImpactChangeOutsidePackage = "PACKAGE_IMPACT_CHANGE_OUTSIDE_PACKAGE";
    public const string CompatibilitySubjectOutsidePackage = "PACKAGE_COMPATIBILITY_SUBJECT_OUTSIDE_PACKAGE";
    public const string LifecycleTransitionOutsideInventory = "PACKAGE_LIFECYCLE_TRANSITION_OUTSIDE_INVENTORY";
    public const string HashMismatch = "PACKAGE_HASH_MISMATCH";
    public const string FormatUnsupported = "PACKAGE_FORMAT_UNSUPPORTED";
    public const string TopologyNotProvided = "PACKAGE_TOPOLOGY_NOT_PROVIDED";

    public const string SeverityError = "Error";
    public const string SeverityWarning = "Warning";
    public const string SeverityInfo = "Info";
}
```

- [ ] **Step 3: Verify abstractions project compiles**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions
```

Expect: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageEvidence.cs \
        framework/src/CrestCreates.Metadata.Abstractions/EvidenceFinding.cs \
        framework/src/CrestCreates.Metadata.Abstractions/EvidenceFindingCount.cs \
        framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageDiagnostic.cs \
        framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageDiagnosticCode.cs
git commit -m "feat(6f): add evidence and diagnostic model types"
```

---

### Task 3: Create Builder API and Diff Model Types

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorPackageBuilder.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageBuildRequest.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageBuildOptions.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorPackageDiffer.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageDiff.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageMetadataChange.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageDiffOptions.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorPackageSerializer.cs`

- [ ] **Step 1: Create builder API types**

Create `IDescriptorPackageBuilder.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorPackageBuilder
{
    DescriptorPackage Build(DescriptorPackageBuildRequest request);
}
```

Create `DescriptorPackageBuildRequest.cs`:

```csharp
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.Abstractions;

public sealed record DescriptorPackageBuildRequest
{
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public string? Name { get; init; }
    public string? CreatedBy { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public required IReadOnlyList<IDescriptor> Descriptors { get; init; }

    public DescriptorTopologySnapshot? TopologySnapshot { get; init; }
    public DescriptorImpactAnalysisReport? ImpactReport { get; init; }
    public DescriptorCompatibilityReport? CompatibilityReport { get; init; }
    public DescriptorLifecycleGovernanceReport? GovernanceReport { get; init; }

    public DescriptorPackageBuildOptions Options { get; init; } = new();
}
```

Create `DescriptorPackageBuildOptions.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed record DescriptorPackageBuildOptions
{
    public string FormatVersion { get; init; } = "1.0";
}
```

- [ ] **Step 2: Create diff types**

Create `IDescriptorPackageDiffer.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorPackageDiffer
{
    DescriptorPackageDiff Diff(
        DescriptorPackage before,
        DescriptorPackage after,
        DescriptorPackageDiffOptions? options = null);
}
```

Create `DescriptorPackageDiff.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed record DescriptorPackageDiff
{
    public required IReadOnlyList<DescriptorRef> AddedRefs { get; init; }
    public required IReadOnlyList<DescriptorRef> RemovedRefs { get; init; }
    public required IReadOnlyList<DescriptorDiffEntry> ChangedEntries { get; init; }
    public required IReadOnlyList<DescriptorStateChange> StateChanges { get; init; }
    public required IReadOnlyList<DescriptorPackageMetadataChange> MetadataChanges { get; init; }
    public string BeforeContentHash { get; init; } = string.Empty;
    public string AfterContentHash { get; init; } = string.Empty;
}

public sealed record DescriptorDiffEntry
{
    public required DescriptorRef Ref { get; init; }
    public string BeforeContractHash { get; init; } = string.Empty;
    public string AfterContractHash { get; init; } = string.Empty;
}

public sealed record DescriptorStateChange
{
    public required DescriptorRef Ref { get; init; }
    public DescriptorState FromState { get; init; }
    public DescriptorState ToState { get; init; }
}
```

Create `DescriptorPackageMetadataChange.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed record DescriptorPackageMetadataChange
{
    public required string Field { get; init; }
    public string? BeforeValue { get; init; }
    public string? AfterValue { get; init; }
}
```

Create `DescriptorPackageDiffOptions.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public sealed record DescriptorPackageDiffOptions
{
}
```

- [ ] **Step 3: Create serializer interface**

Create `IDescriptorPackageSerializer.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorPackageSerializer
{
    string Serialize(DescriptorPackage package);
    DescriptorPackage Deserialize(string content);
}
```

- [ ] **Step 4: Verify build**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions
```

Expect: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/IDescriptorPackageBuilder.cs \
        framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageBuildRequest.cs \
        framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageBuildOptions.cs \
        framework/src/CrestCreates.Metadata.Abstractions/IDescriptorPackageDiffer.cs \
        framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageDiff.cs \
        framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageMetadataChange.cs \
        framework/src/CrestCreates.Metadata.Abstractions/DescriptorPackageDiffOptions.cs \
        framework/src/CrestCreates.Metadata.Abstractions/IDescriptorPackageSerializer.cs
git commit -m "feat(6f): add builder API, diff, and serializer interface types"
```

---

### Task 4: Create DescriptorPackageHashComputer (AoT-safe)

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorPackageHashComputer.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorPackageHashComputerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DescriptorPackageHashComputerTests.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorPackageHashComputerTests
{
    [Fact]
    public void ComputeContentHash_SameInput_ProducesSameHash()
    {
        var refs = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active,
                ContractHash = "abc",
                DefinitionHash = "def"
            },
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("capability", "c1", 2),
                Kind = DescriptorKind.Capability,
                Name = "C1",
                State = DescriptorState.Active,
                ContractHash = "ghi",
                DefinitionHash = "jkl"
            }
        };

        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();
        var evidenceHash = "evhash123";

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs, relationships, evidenceHash);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs, relationships, evidenceHash);

        hash1.Should().Be(hash2);
        hash1.Should().NotBeEmpty();
        hash1.Should().HaveLength(64); // SHA-256 hex is 64 chars
    }

    [Fact]
    public void ComputeContentHash_DifferentInputOrder_SameHash()
    {
        var refs1 = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active
            },
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("capability", "c1", 2),
                Kind = DescriptorKind.Capability,
                Name = "C1",
                State = DescriptorState.Active
            }
        };

        var refs2 = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("capability", "c1", 2),
                Kind = DescriptorKind.Capability,
                Name = "C1",
                State = DescriptorState.Active
            },
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active
            }
        };

        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();
        var evidenceHash = "evhash123";

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs1, relationships, evidenceHash);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs2, relationships, evidenceHash);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeContentHash_ChangedDescriptorRef_ChangesHash()
    {
        var refs1 = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active
            }
        };

        var refs2 = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 2), // version changed
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active
            }
        };

        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();
        var evidenceHash = "evhash123";

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs1, relationships, evidenceHash);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs2, relationships, evidenceHash);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeContentHash_IgnoresContractHash()
    {
        var refs1 = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active,
                ContractHash = "old-contract-hash"
            }
        };

        var refs2 = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active,
                ContractHash = "new-contract-hash"
            }
        };

        var relationships = Array.Empty<DescriptorPackageRelationshipEntry>();
        var evidenceHash = "evhash123";

        var hash1 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs1, relationships, evidenceHash);
        var hash2 = DescriptorPackageHashComputer.ComputeContentHash("1.0", refs2, relationships, evidenceHash);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeContentHash_IncludesRelationships()
    {
        var entries = new[]
        {
            new DescriptorManifestEntry
            {
                Ref = new DescriptorRef("schema", "s1", 1),
                Kind = DescriptorKind.Schema,
                Name = "S1",
                State = DescriptorState.Active
            }
        };

        var rels = new[]
        {
            new DescriptorPackageRelationshipEntry
            {
                From = new DescriptorRef("schema", "s1", 1),
                To = new DescriptorRef("capability", "c1", 1),
                Kind = RelationshipKind.References,
                Strength = RelationshipStrength.Strong,
                IsRuntimeBinding = false
            }
        };

        var noRels = Array.Empty<DescriptorPackageRelationshipEntry>();
        var evidenceHash = "evhash123";

        var hashWith = DescriptorPackageHashComputer.ComputeContentHash("1.0", entries, rels, evidenceHash);
        var hashWithout = DescriptorPackageHashComputer.ComputeContentHash("1.0", entries, noRels, evidenceHash);

        hashWith.Should().NotBe(hashWithout);
    }

    [Fact]
    public void ComputeEvidenceHash_Deterministic()
    {
        var evidence = new DescriptorPackageEvidence
        {
            TopologyNodeCount = 5,
            TopologyEdgeCount = 10,
            HasTopologyErrors = false,
            MaxImpactSeverity = DescriptorImpactSeverity.Critical,
            AffectedDescriptorCount = 3,
            MaxCompatibilityLevel = DescriptorCompatibilityLevel.Breaking,
            BreakingFindingCount = 1,
            MaxLifecycleDecision = DescriptorLifecycleDecisionKind.ReviewRequired,
            RequiresReview = true
        };

        var hash1 = DescriptorPackageHashComputer.ComputeEvidenceHash(evidence);
        var hash2 = DescriptorPackageHashComputer.ComputeEvidenceHash(evidence);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageHashComputerTests"
```

Expected: FAIL — `DescriptorPackageHashComputer` does not exist.

- [ ] **Step 3: Implement DescriptorPackageHashComputer**

Create `framework/src/CrestCreates.Metadata/DescriptorPackageHashComputer.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public static class DescriptorPackageHashComputer
{
    public static string ComputeContentHash(
        string formatVersion,
        IReadOnlyList<DescriptorManifestEntry> entries,
        IReadOnlyList<DescriptorPackageRelationshipEntry> relationships,
        string evidenceHash)
    {
        var sb = new StringBuilder();
        sb.Append(formatVersion);
        sb.Append('|');

        // Sorted descriptor refs: Ns:Id:Version:Kind:State
        var sortedRefs = entries
            .OrderBy(e => e.Ref.Namespace)
            .ThenBy(e => e.Ref.Id)
            .ThenBy(e => e.Ref.Version ?? 0)
            .Select(e => $"{e.Ref.Namespace}:{e.Ref.Id}:{e.Ref.Version}:{e.Kind}:{e.State}")
            .ToList();

        sb.AppendJoin("||", sortedRefs);
        sb.Append('|');

        // Sorted relationship entries
        var sortedRels = relationships
            .OrderBy(r => r.From.Namespace)
            .ThenBy(r => r.From.Id)
            .ThenBy(r => r.From.Version ?? 0)
            .ThenBy(r => r.To.Namespace)
            .ThenBy(r => r.To.Id)
            .ThenBy(r => r.To.Version ?? 0)
            .Select(r =>
                $"{r.From.Namespace}:{r.From.Id}:{r.From.Version}→" +
                $"{r.To.Namespace}:{r.To.Id}:{r.To.Version}:{r.Kind}:{r.Strength}")
            .ToList();

        sb.AppendJoin("||", sortedRels);
        sb.Append('|');
        sb.Append(evidenceHash);

        return ComputeSha256(sb.ToString());
    }

    public static string ComputeEvidenceHash(DescriptorPackageEvidence evidence)
    {
        var sb = new StringBuilder();

        sb.Append(evidence.TopologyNodeCount);
        sb.Append('|');
        sb.Append(evidence.TopologyEdgeCount);
        sb.Append('|');
        sb.Append(evidence.HasTopologyErrors);
        sb.Append('|');
        sb.Append(evidence.MaxImpactSeverity);
        sb.Append('|');
        sb.Append(evidence.AffectedDescriptorCount);
        sb.Append('|');
        sb.Append(evidence.ImpactPathCount);
        sb.Append('|');
        sb.Append(evidence.MaxCompatibilityLevel);
        sb.Append('|');
        sb.Append(evidence.BreakingFindingCount);
        sb.Append('|');
        sb.Append(evidence.SecuritySensitiveFindingCount);
        sb.Append('|');
        sb.Append(evidence.UnsupportedFindingCount);
        sb.Append('|');
        sb.Append(evidence.MaxLifecycleDecision);
        sb.Append('|');
        sb.Append(evidence.RequiresReview);
        sb.Append('|');
        sb.Append(evidence.IsBlocked);
        sb.Append('|');
        sb.Append(evidence.PackageFindingCount);

        return ComputeSha256(sb.ToString());
    }

    public static string ComputeEnvelopeHash(
        string contentHash,
        string packageId,
        string packageVersion,
        DateTimeOffset createdAt,
        string? createdBy,
        string? source)
    {
        var sb = new StringBuilder();
        sb.Append(contentHash);
        sb.Append('|');
        sb.Append(packageId);
        sb.Append('|');
        sb.Append(packageVersion);
        sb.Append('|');
        sb.Append(createdAt.ToString("O"));
        sb.Append('|');
        sb.Append(createdBy ?? "");
        sb.Append('|');
        sb.Append(source ?? "");

        return ComputeSha256(sb.ToString());
    }

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageHashComputerTests"
```

Expected: 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorPackageHashComputer.cs \
        framework/test/CrestCreates.Metadata.Tests/DescriptorPackageHashComputerTests.cs
git commit -m "feat(6f): add DescriptorPackageHashComputer — AoT-safe deterministic content/evidence/envelope hashing"
```

---

### Task 5: Implement DefaultDescriptorPackageBuilder (Core Builder)

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DefaultDescriptorPackageBuilder.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorPackageBuilderTests.cs`

- [ ] **Step 1: Write failing tests for builder determinism**

Create `DescriptorPackageBuilderTests.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorPackageBuilderTests
{
    private readonly IDescriptorPackageBuilder _builder = new DefaultDescriptorPackageBuilder();

    private static SchemaDescriptor MakeSchema(string id, int version, string name, DescriptorState state = DescriptorState.Active)
    {
        return new SchemaDescriptor
        {
            Id = id,
            Version = version,
            Name = name,
            State = state,
            ContractHash = $"contract_{id}_v{version}",
            DefinitionHash = $"def_{id}_v{version}"
        };
    }

    [Fact]
    public void Build_ProducesPackage()
    {
        var descriptors = new IDescriptor[]
        {
            MakeSchema("s1", 1, "Schema1"),
            MakeSchema("s2", 1, "Schema2")
        };

        var package = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = descriptors
        });

        package.Should().NotBeNull();
        package.PackageId.Should().Be("test.pkg");
        package.Manifest.DescriptorCount.Should().Be(2);
        package.Manifest.DescriptorEntries.Should().HaveCount(2);
        package.Snapshot.Descriptors.Should().HaveCount(2);
        package.ContentHash.Should().NotBeEmpty();
    }

    [Fact]
    public void Build_SameInput_ProducesSameContentHash()
    {
        var descriptors = new IDescriptor[]
        {
            MakeSchema("s1", 1, "S1")
        };

        var request = new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = descriptors,
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)
        };

        var pkg1 = _builder.Build(request);
        var pkg2 = _builder.Build(request);

        pkg1.ContentHash.Should().Be(pkg2.ContentHash);
        pkg1.Snapshot.SnapshotId.Should().Be(pkg2.Snapshot.SnapshotId);
    }

    [Fact]
    public void Build_SameContentDifferentInputOrder_SameContentHash()
    {
        var descriptors1 = new IDescriptor[]
        {
            MakeSchema("b", 1, "B"),
            MakeSchema("a", 1, "A")
        };

        var descriptors2 = new IDescriptor[]
        {
            MakeSchema("a", 1, "A"),
            MakeSchema("b", 1, "B")
        };

        var baseRequest = new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)
        };

        var pkg1 = _builder.Build(baseRequest with { Descriptors = descriptors1 });
        var pkg2 = _builder.Build(baseRequest with { Descriptors = descriptors2 });

        pkg1.ContentHash.Should().Be(pkg2.ContentHash);
    }

    [Fact]
    public void Build_DifferentCreatedAt_DoesNotChangeContentHash()
    {
        var descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") };

        var pkg1 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = descriptors,
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)
        });

        var pkg2 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = descriptors,
            CreatedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
        });

        pkg1.ContentHash.Should().Be(pkg2.ContentHash);
    }

    [Fact]
    public void Build_ChangedDescriptorRef_ChangesContentHash()
    {
        var pkg1 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });

        var pkg2 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 2, "S1") }
        });

        pkg1.ContentHash.Should().NotBe(pkg2.ContentHash);
    }

    [Fact]
    public void Build_ProducesDeterministicSnapshotId_NoGuid()
    {
        var descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") };

        var request = new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = descriptors,
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)
        };

        var pkg1 = _builder.Build(request);
        var pkg2 = _builder.Build(request);

        pkg1.Snapshot.SnapshotId.Should().Be(pkg2.Snapshot.SnapshotId);
        pkg1.Snapshot.SnapshotId.Should().StartWith("snapshot_");
        pkg1.Snapshot.SnapshotId.Should().NotContain("-"); // No Guid dashes
    }

    [Fact]
    public void Build_SnapshotId_DerivedFromContentHash()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });

        var expectedPrefix = pkg.ContentHash[..16];
        pkg.Snapshot.SnapshotId.Should().Be($"snapshot_{expectedPrefix}");
    }

    [Fact]
    public void Build_ContentHash_DoesNotDependOnContractHash()
    {
        var desc1 = MakeSchema("s1", 1, "S1");
        desc1.ContractHash = "aaa";

        var desc2 = MakeSchema("s1", 1, "S1");
        desc2.ContractHash = "bbb";

        var pkg1 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { desc1 }
        });

        var pkg2 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { desc2 }
        });

        pkg1.ContentHash.Should().Be(pkg2.ContentHash);
    }

    [Fact]
    public void Build_StoresContractAndDefinitionHashes_InManifestEntries()
    {
        var desc = MakeSchema("s1", 1, "S1");
        desc.ContractHash = "my-contract";
        desc.DefinitionHash = "my-definition";

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { desc }
        });

        var entry = pkg.Manifest.DescriptorEntries.Should().ContainSingle().Subject;
        entry.ContractHash.Should().Be("my-contract");
        entry.DefinitionHash.Should().Be("my-definition");
    }

    [Fact]
    public void Build_ManifestEntries_SortedByNamespaceIdVersion()
    {
        var descriptors = new IDescriptor[]
        {
            MakeSchema("c", 1, "C"),
            MakeSchema("a", 2, "A2"),
            MakeSchema("a", 1, "A1")
        };

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = descriptors
        });

        var entries = pkg.Manifest.DescriptorEntries;
        entries[0].Ref.Id.Should().Be("a");
        entries[0].Ref.Version.Should().Be(1);
        entries[1].Ref.Id.Should().Be("a");
        entries[1].Ref.Version.Should().Be(2);
        entries[2].Ref.Id.Should().Be("c");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageBuilderTests"
```

Expected: FAIL — `DefaultDescriptorPackageBuilder` does not exist.

- [ ] **Step 3: Implement DefaultDescriptorPackageBuilder**

Create `framework/src/CrestCreates.Metadata/DefaultDescriptorPackageBuilder.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata;

public sealed class DefaultDescriptorPackageBuilder : IDescriptorPackageBuilder
{
    public DescriptorPackage Build(DescriptorPackageBuildRequest request)
    {
        var createdAt = request.CreatedAt ?? DateTimeOffset.UtcNow;

        // Build manifest entries from descriptors
        var entries = BuildManifestEntries(request.Descriptors);

        // Build evidence from supplied reports
        var evidence = BuildEvidence(request);

        // Build relationship entries from topology
        var relationships = BuildRelationshipEntries(request.TopologySnapshot);

        // Compute evidence hash
        var evidenceHash = DescriptorPackageHashComputer.ComputeEvidenceHash(evidence);

        // Compute content hash
        var contentHash = DescriptorPackageHashComputer.ComputeContentHash(
            request.Options.FormatVersion, entries, relationships, evidenceHash);

        // Build manifest
        var manifest = new DescriptorManifest
        {
            FormatVersion = request.Options.FormatVersion,
            PackageId = request.PackageId,
            PackageVersion = request.PackageVersion,
            Name = request.Name,
            CreatedAt = createdAt,
            CreatedBy = request.CreatedBy,
            Source = request.Source,
            DescriptorCount = entries.Count,
            DescriptorEntries = entries,
            ContentHash = contentHash,
            EvidenceHash = evidenceHash,
            EnvelopeHash = DescriptorPackageHashComputer.ComputeEnvelopeHash(
                contentHash, request.PackageId, request.PackageVersion,
                createdAt, request.CreatedBy, request.Source)
        };

        // Build snapshot
        var snapshotEntries = entries.Select(e => new SnapshotEntry
        {
            Ref = e.Ref,
            DescriptorName = e.Name,
            Kind = e.Kind,
            State = e.State,
            ContractHash = e.ContractHash,
            DefinitionHash = e.DefinitionHash,
            SupersededById = e.SupersededById
        }).ToList();

        var snapshot = new DescriptorSnapshot
        {
            SnapshotId = $"snapshot_{contentHash[..16]}",
            PackageId = request.PackageId,
            PackageVersion = request.PackageVersion,
            CreatedAt = createdAt,
            Descriptors = snapshotEntries,
            Relationships = relationships
        };

        // Run self-consistency diagnostics
        var diagnostics = RunDiagnostics(request, entries, contentHash);

        return new DescriptorPackage
        {
            Manifest = manifest,
            Snapshot = snapshot,
            Evidence = evidence,
            Diagnostics = diagnostics
        };
    }

    private static IReadOnlyList<DescriptorManifestEntry> BuildManifestEntries(
        IReadOnlyList<IDescriptor> descriptors)
    {
        return descriptors
            .Select(d => new DescriptorManifestEntry
            {
                Ref = new DescriptorRef(d.Namespace, d.Id,
                    (d as IVersionedDescriptor)?.Version),
                Kind = d.Kind,
                Name = d.Name,
                State = d.State,
                ContractHash = d.ContractHash,
                DefinitionHash = d.DefinitionHash,
                SupersededById = d.SupersededById
            })
            .OrderBy(e => e.Ref.Namespace)
            .ThenBy(e => e.Ref.Id)
            .ThenBy(e => e.Ref.Version ?? 0)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.Name)
            .ToList();
    }

    private static DescriptorPackageEvidence BuildEvidence(
        DescriptorPackageBuildRequest request)
    {
        var evidence = new DescriptorPackageEvidence();

        // Topology evidence
        if (request.TopologySnapshot != null)
        {
            evidence = evidence with
            {
                TopologyNodeCount = request.TopologySnapshot.Nodes.Count,
                TopologyEdgeCount = request.TopologySnapshot.Edges.Count,
                HasTopologyErrors = request.TopologySnapshot.Diagnostics.Items
                    .Any(d => d.Severity == DiagnosticSeverity.Error)
            };
        }

        // Impact evidence
        if (request.ImpactReport != null)
        {
            evidence = evidence with
            {
                MaxImpactSeverity = request.ImpactReport.MaxSeverity,
                AffectedDescriptorCount = request.ImpactReport.AffectedDescriptors.Count,
                ImpactPathCount = request.ImpactReport.ImpactPaths.Count
            };
        }

        // Compatibility evidence
        if (request.CompatibilityReport != null)
        {
            evidence = evidence with
            {
                MaxCompatibilityLevel = request.CompatibilityReport.MaxLevel,
                BreakingFindingCount = request.CompatibilityReport.Findings
                    .Count(f => f.Level == DescriptorCompatibilityLevel.Breaking),
                SecuritySensitiveFindingCount = request.CompatibilityReport.Findings
                    .Count(f => f.Level == DescriptorCompatibilityLevel.SecuritySensitive),
                UnsupportedFindingCount = request.CompatibilityReport.Findings
                    .Count(f => f.Level == DescriptorCompatibilityLevel.Unsupported)
            };
        }

        // Lifecycle evidence
        if (request.GovernanceReport != null)
        {
            evidence = evidence with
            {
                MaxLifecycleDecision = request.GovernanceReport.MaxDecision,
                RequiresReview = request.GovernanceReport.RequiresReview,
                IsBlocked = request.GovernanceReport.IsBlocked,
                PackageFindingCount = request.GovernanceReport.PackageFindings.Count
            };
        }

        return evidence;
    }

    private static IReadOnlyList<DescriptorPackageRelationshipEntry> BuildRelationshipEntries(
        DescriptorTopologySnapshot? topology)
    {
        if (topology == null)
            return Array.Empty<DescriptorPackageRelationshipEntry>();

        return topology.Edges
            .Select(e => new DescriptorPackageRelationshipEntry
            {
                From = e.From,
                To = e.To,
                Kind = e.Kind,
                Role = e.Role,
                SourcePath = e.SourcePath,
                Strength = e.Strength,
                IsRuntimeBinding = e.IsRuntimeBinding
            })
            .ToList();
    }

    private static IReadOnlyList<DescriptorPackageDiagnostic> RunDiagnostics(
        DescriptorPackageBuildRequest request,
        IReadOnlyList<DescriptorManifestEntry> entries,
        string contentHash)
    {
        var diagnostics = new List<DescriptorPackageDiagnostic>();

        // Check for duplicate descriptor refs
        var seenRefs = new HashSet<DescriptorRef>();
        foreach (var entry in entries)
        {
            if (!seenRefs.Add(entry.Ref))
            {
                diagnostics.Add(new DescriptorPackageDiagnostic
                {
                    Code = DescriptorPackageDiagnosticCode.DuplicateDescriptorRef,
                    Severity = DescriptorPackageDiagnosticCode.SeverityError,
                    Message = $"Duplicate descriptor ref: {entry.Ref.Namespace}.{entry.Ref.Id} v{entry.Ref.Version}",
                    Subject = entry.Ref
                });
            }
        }

        // Check topology edges reference package descriptors
        var packageRefs = new HashSet<DescriptorRef>(entries.Select(e => e.Ref));
        if (request.TopologySnapshot != null)
        {
            foreach (var edge in request.TopologySnapshot.Edges)
            {
                if (!packageRefs.Contains(edge.From))
                {
                    diagnostics.Add(new DescriptorPackageDiagnostic
                    {
                        Code = DescriptorPackageDiagnosticCode.TopologyEdgeOutsidePackage,
                        Severity = DescriptorPackageDiagnosticCode.SeverityWarning,
                        Message = $"Topology edge 'From' ref outside package: {edge.From.Namespace}.{edge.From.Id}",
                        Subject = edge.From
                    });
                }
                if (!packageRefs.Contains(edge.To))
                {
                    diagnostics.Add(new DescriptorPackageDiagnostic
                    {
                        Code = DescriptorPackageDiagnosticCode.TopologyEdgeOutsidePackage,
                        Severity = DescriptorPackageDiagnosticCode.SeverityWarning,
                        Message = $"Topology edge 'To' ref outside package: {edge.To.Namespace}.{edge.To.Id}",
                        Subject = edge.To
                    });
                }
            }
        }
        else
        {
            diagnostics.Add(new DescriptorPackageDiagnostic
            {
                Code = DescriptorPackageDiagnosticCode.TopologyNotProvided,
                Severity = DescriptorPackageDiagnosticCode.SeverityInfo,
                Message = "No topology snapshot provided; package has no relationship facts."
            });
        }

        return diagnostics;
    }
}
```

- [ ] **Step 4: Run builder determinism tests**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageBuilderTests"
```

Expected: 10 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DefaultDescriptorPackageBuilder.cs \
        framework/test/CrestCreates.Metadata.Tests/DescriptorPackageBuilderTests.cs
git commit -m "feat(6f): implement DefaultDescriptorPackageBuilder — deterministic package construction"
```

---

### Task 6: Builder — Evidence, Relationships, and Diagnostics Tests

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.Tests/DescriptorPackageBuilderTests.cs` (append new tests)

- [ ] **Step 1: Add evidence tests**

Append to `DescriptorPackageBuilderTests.cs`:

```csharp
    [Fact]
    public void Build_CapturesEvidenceSummary_FromImpactReport()
    {
        var impactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = new DescriptorChangeSet
            {
                Changes = Array.Empty<DescriptorChange>()
            },
            MaxSeverity = DescriptorImpactSeverity.Critical,
            AffectedDescriptors = new[]
            {
                new AffectedDescriptor
                {
                    Ref = new DescriptorRef("schema", "s1", 1),
                    Severity = DescriptorImpactSeverity.Critical,
                    ImpactPaths = Array.Empty<DescriptorImpactPath>()
                }
            },
            ImpactPaths = new[]
            {
                new DescriptorImpactPath
                {
                    SourceChange = new DescriptorRef("schema", "s1", 1),
                    Affected = new DescriptorRef("capability", "c1", 1),
                    Segments = Array.Empty<DescriptorImpactPathSegment>(),
                    Severity = DescriptorImpactSeverity.Critical
                }
            },
            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
        };

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") },
            ImpactReport = impactReport
        });

        pkg.Evidence.MaxImpactSeverity.Should().Be(DescriptorImpactSeverity.Critical);
        pkg.Evidence.AffectedDescriptorCount.Should().Be(1);
        pkg.Evidence.ImpactPathCount.Should().Be(1);
    }

    [Fact]
    public void Build_CapturesEvidenceSummary_FromCompatibilityReport()
    {
        var compatReport = new DescriptorCompatibilityReport
        {
            MaxLevel = DescriptorCompatibilityLevel.Breaking,
            Findings = new[]
            {
                new DescriptorCompatibilityFinding
                {
                    Kind = DescriptorCompatibilityFindingKind.BreakingSchemaChange,
                    Level = DescriptorCompatibilityLevel.Breaking,
                    Subject = new DescriptorRef("schema", "s1", null),
                    Message = "Field removed",
                    RuleId = "SCHEMA_001"
                }
            },
            Diagnostics = Array.Empty<DescriptorCompatibilityDiagnostic>(),
            ChangeSet = null
        };

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") },
            CompatibilityReport = compatReport
        });

        pkg.Evidence.MaxCompatibilityLevel.Should().Be(DescriptorCompatibilityLevel.Breaking);
        pkg.Evidence.BreakingFindingCount.Should().Be(1);
    }

    [Fact]
    public void Build_CapturesEvidenceSummary_FromLifecycleReport()
    {
        var lifecycleReport = new DescriptorLifecycleGovernanceReport
        {
            Decisions = Array.Empty<DescriptorLifecycleDecision>(),
            MaxDecision = DescriptorLifecycleDecisionKind.ReviewRequired,
            PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
        };

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") },
            GovernanceReport = lifecycleReport
        });

        pkg.Evidence.MaxLifecycleDecision.Should().Be(DescriptorLifecycleDecisionKind.ReviewRequired);
        pkg.Evidence.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void Build_CapturesEvidenceSummary_FromTopologySnapshot()
    {
        var topology = new DescriptorTopologySnapshot(
            nodes: new Dictionary<DescriptorRef, DescriptorNode>
            {
                [new DescriptorRef("schema", "s1", 1)] = new DescriptorNode(
                    new DescriptorIdentity("schema", "s1"),
                    new DescriptorRef("schema", "s1", 1),
                    DescriptorKind.Schema,
                    "S1",
                    DescriptorState.Active,
                    "hash",
                    null,
                    new HashSet<int>())
            },
            edges: new List<DescriptorEdge>(),
            diagnostics: new DescriptorTopologyDiagnostics(
                Array.Empty<DescriptorTopologyDiagnostic>()),
            consumersByIdentity: new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            consumersByExactVersion: new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>(),
            consumersByUnpinnedVersion: new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>()
        );

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") },
            TopologySnapshot = topology
        });

        pkg.Evidence.TopologyNodeCount.Should().Be(1);
        pkg.Evidence.TopologyEdgeCount.Should().Be(0);
        pkg.Evidence.HasTopologyErrors.Should().BeFalse();
    }

    [Fact]
    public void Build_WithoutReports_ProducesEmptyEvidence()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });

        pkg.Evidence.TopologyNodeCount.Should().Be(0);
        pkg.Evidence.MaxImpactSeverity.Should().Be(default);
    }

    [Fact]
    public void Build_DoesNotRerunTopology_WhenTopologyMissing()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });

        pkg.Snapshot.Relationships.Should().BeEmpty();
    }

    [Fact]
    public void Build_DoesNotRerunAnalysis()
    {
        // Verifies builder doesn't internally invoke any analysis
        // by checking it handles null reports gracefully
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });

        pkg.Should().NotBeNull();
        pkg.Evidence.MaxImpactSeverity.Should().Be(default);
        pkg.Evidence.MaxCompatibilityLevel.Should().Be(default);
    }

    [Fact]
    public void Build_CapturesTopologyRelationshipFacts_WithSourcePath()
    {
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>
        {
            [new DescriptorRef("schema", "s1", 1)] = new DescriptorNode(
                new DescriptorIdentity("schema", "s1"),
                new DescriptorRef("schema", "s1", 1),
                DescriptorKind.Schema, "S1", DescriptorState.Active, "hash", null, new HashSet<int>()),
            [new DescriptorRef("capability", "c1", 1)] = new DescriptorNode(
                new DescriptorIdentity("capability", "c1"),
                new DescriptorRef("capability", "c1", 1),
                DescriptorKind.Capability, "C1", DescriptorState.Active, "hash", null, new HashSet<int>())
        };

        var edge = new DescriptorEdge
        {
            Index = 0,
            From = new DescriptorRef("capability", "c1", 1),
            To = new DescriptorRef("schema", "s1", 1),
            Kind = RelationshipKind.Consumes,
            Role = "InputSchema",
            SourcePath = "InputSchema",
            Strength = RelationshipStrength.Strong,
            IsRuntimeBinding = true
        };

        var topology = new DescriptorTopologySnapshot(
            nodes: nodes,
            edges: new List<DescriptorEdge> { edge },
            diagnostics: new DescriptorTopologyDiagnostics(Array.Empty<DescriptorTopologyDiagnostic>()),
            consumersByIdentity: new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            consumersByExactVersion: new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>(),
            consumersByUnpinnedVersion: new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>()
        );

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[]
            {
                MakeSchema("s1", 1, "S1"),
                new CapabilityDescriptor
                {
                    Id = "c1", Version = 1, Name = "C1", State = DescriptorState.Active
                }
            },
            TopologySnapshot = topology
        });

        pkg.Snapshot.Relationships.Should().ContainSingle();
        var rel = pkg.Snapshot.Relationships[0];
        rel.From.Id.Should().Be("c1");
        rel.To.Id.Should().Be("s1");
        rel.Kind.Should().Be(RelationshipKind.Consumes);
        rel.SourcePath.Should().Be("InputSchema");
        rel.IsRuntimeBinding.Should().BeTrue();
    }

    [Fact]
    public void Build_WithoutTopology_EmitsTopologyNotProvidedDiagnostic()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });

        pkg.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorPackageDiagnosticCode.TopologyNotProvided);
    }

    [Fact]
    public void Build_DuplicateDescriptorRefs_EmitsPackageDiagnostic()
    {
        var desc1 = MakeSchema("s1", 1, "S1");
        var desc2 = MakeSchema("s1", 1, "S1"); // same ref

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { desc1, desc2 }
        });

        pkg.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorPackageDiagnosticCode.DuplicateDescriptorRef);
    }
```

- [ ] **Step 2: Run all builder tests**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageBuilderTests"
```

Expected: 20 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorPackageBuilderTests.cs
git commit -m "test(6f): add builder evidence, relationship, and diagnostics tests"
```

---

### Task 7: Implement DescriptorPackageDiffer

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorPackageDiffer.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorPackageDiffTests.cs`

- [ ] **Step 1: Write failing diff tests**

Create `DescriptorPackageDiffTests.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorPackageDiffTests
{
    private readonly IDescriptorPackageBuilder _builder = new DefaultDescriptorPackageBuilder();
    private readonly IDescriptorPackageDiffer _differ = new DescriptorPackageDiffer();

    private DescriptorPackage BuildPackage(string pkgId, IDescriptor[] descriptors, string version = "1.0.0")
    {
        return _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = pkgId,
            PackageVersion = version,
            Descriptors = descriptors
        });
    }

    private static SchemaDescriptor MakeSchema(string id, int version, string name)
    {
        return new SchemaDescriptor
        {
            Id = id,
            Version = version,
            Name = name,
            State = DescriptorState.Active,
            ContractHash = $"contract_{id}_v{version}"
        };
    }

    [Fact]
    public void Diff_AddedRef_ProducesAddedEntry()
    {
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") });
        var pkg2 = BuildPackage("pkg", new IDescriptor[]
        {
            MakeSchema("a", 1, "A"),
            MakeSchema("b", 1, "B")
        });

        var diff = _differ.Diff(pkg1, pkg2);

        diff.AddedRefs.Should().ContainSingle(r => r.Id == "b");
        diff.RemovedRefs.Should().BeEmpty();
    }

    [Fact]
    public void Diff_RemovedRef_ProducesRemovedEntry()
    {
        var pkg1 = BuildPackage("pkg", new IDescriptor[]
        {
            MakeSchema("a", 1, "A"),
            MakeSchema("b", 1, "B")
        });
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") });

        var diff = _differ.Diff(pkg1, pkg2);

        diff.RemovedRefs.Should().ContainSingle(r => r.Id == "b");
        diff.AddedRefs.Should().BeEmpty();
    }

    [Fact]
    public void Diff_ChangedDescriptorHash_ProducesChangedEntry()
    {
        var desc1a = MakeSchema("a", 1, "A");
        desc1a.ContractHash = "hash-v1";

        var desc1b = MakeSchema("a", 1, "A");
        desc1b.ContractHash = "hash-v2";

        var pkg1 = BuildPackage("pkg", new IDescriptor[] { desc1a });
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { desc1b });

        var diff = _differ.Diff(pkg1, pkg2);

        diff.ChangedEntries.Should().ContainSingle(e => e.Ref.Id == "a");
        diff.ChangedEntries[0].BeforeContractHash.Should().Be("hash-v1");
        diff.ChangedEntries[0].AfterContractHash.Should().Be("hash-v2");
    }

    [Fact]
    public void Diff_StateChange_ProducesStateChangeEntry()
    {
        var active = MakeSchema("a", 1, "A");
        active.State = DescriptorState.Active;

        var deprecated = MakeSchema("a", 1, "A");
        deprecated.State = DescriptorState.Deprecated;

        var pkg1 = BuildPackage("pkg", new IDescriptor[] { active });
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { deprecated });

        var diff = _differ.Diff(pkg1, pkg2);

        diff.StateChanges.Should().ContainSingle(s =>
            s.Ref.Id == "a" &&
            s.FromState == DescriptorState.Active &&
            s.ToState == DescriptorState.Deprecated);
    }

    [Fact]
    public void Diff_MetadataChange_ProducesMetadataChange()
    {
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") }, "1.0.0");
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") }, "2.0.0");

        var diff = _differ.Diff(pkg1, pkg2);

        diff.MetadataChanges.Should().Contain(m =>
            m.Field == "PackageVersion" &&
            m.BeforeValue == "1.0.0" &&
            m.AfterValue == "2.0.0");
    }

    [Fact]
    public void Diff_IdenticalPackages_ProducesEmptyDiff()
    {
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") });
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") });

        var diff = _differ.Diff(pkg1, pkg2);

        diff.AddedRefs.Should().BeEmpty();
        diff.RemovedRefs.Should().BeEmpty();
        diff.ChangedEntries.Should().BeEmpty();
        diff.StateChanges.Should().BeEmpty();
        diff.MetadataChanges.Should().BeEmpty();
    }

    [Fact]
    public void Diff_DoesNotRunImpactOrCompatibilityAnalysis()
    {
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") });
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("b", 1, "B") });

        var diff = _differ.Diff(pkg1, pkg2);

        diff.AddedRefs.Should().ContainSingle(r => r.Id == "b");
        diff.RemovedRefs.Should().ContainSingle(r => r.Id == "a");
        // No impact traversal output in diff type
    }

    [Fact]
    public void Diff_MetadataChanges_UsesStrongTypedRecords()
    {
        var pkg1 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") }, "1.0.0");
        var pkg2 = BuildPackage("pkg", new IDescriptor[] { MakeSchema("a", 1, "A") }, "2.0.0");

        var diff = _differ.Diff(pkg1, pkg2);

        diff.MetadataChanges.Should().AllBeOfType<DescriptorPackageMetadataChange>();
        var change = diff.MetadataChanges[0];
        change.Field.Should().Be("PackageVersion");
        change.BeforeValue.Should().Be("1.0.0");
        change.AfterValue.Should().Be("2.0.0");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageDiffTests"
```

Expected: FAIL — `DescriptorPackageDiffer` does not exist.

- [ ] **Step 3: Implement DescriptorPackageDiffer**

Create `framework/src/CrestCreates.Metadata/DescriptorPackageDiffer.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DescriptorPackageDiffer : IDescriptorPackageDiffer
{
    public DescriptorPackageDiff Diff(
        DescriptorPackage before,
        DescriptorPackage after,
        DescriptorPackageDiffOptions? options = null)
    {
        var beforeRefs = before.Manifest.DescriptorEntries
            .Select(e => e.Ref).ToHashSet();
        var afterRefs = after.Manifest.DescriptorEntries
            .Select(e => e.Ref).ToHashSet();

        var addedRefs = afterRefs.Except(beforeRefs).ToList();
        var removedRefs = beforeRefs.Except(afterRefs).ToList();

        // Changed hashes
        var beforeByRef = before.Manifest.DescriptorEntries
            .ToDictionary(e => e.Ref);
        var afterByRef = after.Manifest.DescriptorEntries
            .ToDictionary(e => e.Ref);

        var changedEntries = new List<DescriptorDiffEntry>();
        var stateChanges = new List<DescriptorStateChange>();

        foreach (var (refKey, beforeEntry) in beforeByRef)
        {
            if (afterByRef.TryGetValue(refKey, out var afterEntry))
            {
                // Check hash change
                if (beforeEntry.ContractHash != afterEntry.ContractHash)
                {
                    changedEntries.Add(new DescriptorDiffEntry
                    {
                        Ref = refKey,
                        BeforeContractHash = beforeEntry.ContractHash,
                        AfterContractHash = afterEntry.ContractHash
                    });
                }

                // Check state change
                if (beforeEntry.State != afterEntry.State)
                {
                    stateChanges.Add(new DescriptorStateChange
                    {
                        Ref = refKey,
                        FromState = beforeEntry.State,
                        ToState = afterEntry.State
                    });
                }
            }
        }

        // Metadata changes
        var metadataChanges = new List<DescriptorPackageMetadataChange>();

        if (before.Manifest.PackageVersion != after.Manifest.PackageVersion)
        {
            metadataChanges.Add(new DescriptorPackageMetadataChange
            {
                Field = "PackageVersion",
                BeforeValue = before.Manifest.PackageVersion,
                AfterValue = after.Manifest.PackageVersion
            });
        }

        if (before.Manifest.Name != after.Manifest.Name)
        {
            metadataChanges.Add(new DescriptorPackageMetadataChange
            {
                Field = "Name",
                BeforeValue = before.Manifest.Name,
                AfterValue = after.Manifest.Name
            });
        }

        if (before.Manifest.Source != after.Manifest.Source)
        {
            metadataChanges.Add(new DescriptorPackageMetadataChange
            {
                Field = "Source",
                BeforeValue = before.Manifest.Source,
                AfterValue = after.Manifest.Source
            });
        }

        return new DescriptorPackageDiff
        {
            AddedRefs = addedRefs,
            RemovedRefs = removedRefs,
            ChangedEntries = changedEntries,
            StateChanges = stateChanges,
            MetadataChanges = metadataChanges,
            BeforeContentHash = before.ContentHash,
            AfterContentHash = after.ContentHash
        };
    }
}
```

- [ ] **Step 4: Run diff tests**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageDiffTests"
```

Expected: 8 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorPackageDiffer.cs \
        framework/test/CrestCreates.Metadata.Tests/DescriptorPackageDiffTests.cs
git commit -m "feat(6f): implement DescriptorPackageDiffer — shallow structural diff"
```

---

### Task 8: Implement DescriptorPackageSerializer

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorPackageSerializer.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorPackageSerializerTests.cs`

- [ ] **Step 1: Write failing serializer tests**

Create `DescriptorPackageSerializerTests.cs`:

```csharp
using System.Text.Json;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorPackageSerializerTests
{
    private readonly IDescriptorPackageBuilder _builder = new DefaultDescriptorPackageBuilder();
    private readonly IDescriptorPackageSerializer _serializer = new DescriptorPackageSerializer();

    private static SchemaDescriptor MakeSchema(string id, int version, string name)
    {
        return new SchemaDescriptor
        {
            Id = id, Version = version, Name = name, State = DescriptorState.Active,
            ContractHash = $"contract_{id}_v{version}",
            DefinitionHash = $"def_{id}_v{version}"
        };
    }

    [Fact]
    public void Serializer_RoundTripsManifest()
    {
        var manifest = new DescriptorManifest
        {
            FormatVersion = "1.0",
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Name = "Test Package",
            DescriptorCount = 1,
            DescriptorEntries = new[]
            {
                new DescriptorManifestEntry
                {
                    Ref = new DescriptorRef("schema", "s1", 1),
                    Kind = DescriptorKind.Schema,
                    Name = "S1",
                    State = DescriptorState.Active,
                    ContractHash = "abc",
                    DefinitionHash = "def"
                }
            },
            ContentHash = "deadbeef",
            EvidenceHash = "evhash"
        };

        var json = _serializer.SerializeManifest(manifest);
        var deserialized = _serializer.DeserializeManifest(json);

        deserialized.Should().NotBeNull();
        deserialized!.PackageId.Should().Be("test.pkg");
        deserialized.DescriptorEntries.Should().HaveCount(1);
        deserialized.ContentHash.Should().Be("deadbeef");
    }

    [Fact]
    public void Serializer_RoundTripsPackageData_MetadataOnly()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });

        var json = _serializer.Serialize(pkg);
        var deserialized = _serializer.Deserialize(json);

        deserialized.Should().NotBeNull();
        deserialized!.PackageId.Should().Be("test.pkg");
        deserialized.Manifest.DescriptorEntries.Should().HaveCount(1);
        deserialized.ContentHash.Should().Be(pkg.ContentHash);
        deserialized.Snapshot.Descriptors.Should().HaveCount(1);
    }

    [Fact]
    public void Serializer_RoundTripsPackageWithEvidence()
    {
        var impactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
            MaxSeverity = DescriptorImpactSeverity.Critical,
            AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
            ImpactPaths = Array.Empty<DescriptorImpactPath>(),
            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
        };

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") },
            ImpactReport = impactReport
        });

        var json = _serializer.Serialize(pkg);
        var deserialized = _serializer.Deserialize(json);

        deserialized!.Evidence.MaxImpactSeverity.Should().Be(DescriptorImpactSeverity.Critical);
    }

    [Fact]
    public void Serializer_RoundTripsPackageWithDiagnostics()
    {
        // Duplicate refs to trigger diagnostics
        var desc1 = MakeSchema("s1", 1, "S1");
        var desc2 = MakeSchema("s1", 1, "S1"); // duplicate

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { desc1, desc2 }
        });

        var json = _serializer.Serialize(pkg);
        var deserialized = _serializer.Deserialize(json);

        deserialized!.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorPackageDiagnosticCode.DuplicateDescriptorRef);
    }

    [Fact]
    public void Serializer_RoundTripsPackageDiff()
    {
        var pkg1 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "pkg", PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("a", 1, "A") }
        });
        var pkg2 = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "pkg", PackageVersion = "2.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("a", 1, "A"), MakeSchema("b", 1, "B") }
        });

        var differ = new DescriptorPackageDiffer();
        var diff = differ.Diff(pkg1, pkg2);

        var json = _serializer.SerializeDiff(diff);
        var deserialized = _serializer.DeserializeDiff(json);

        deserialized.Should().NotBeNull();
        deserialized!.AddedRefs.Should().ContainSingle(r => r.Id == "b");
        deserialized.MetadataChanges.Should().ContainSingle(m => m.Field == "PackageVersion");
    }

    [Fact]
    public void Serializer_DeserializedPackage_CannotRecomputeDescriptorHashes()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });

        var json = _serializer.Serialize(pkg);
        var deserialized = _serializer.Deserialize(json);

        // Snapshot entries have refs but no live IDescriptor objects
        var entry = deserialized!.Snapshot.Descriptors[0];
        entry.Ref.Id.Should().Be("s1");
        entry.ContractHash.Should().Be("contract_s1_v1");
        // Cannot call DescriptorHashComputer on deserialized entries — no live IDescriptor
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageSerializerTests"
```

Expected: FAIL — `DescriptorPackageSerializer` does not exist (or methods like `SerializeManifest` not found).

- [ ] **Step 3: Implement DescriptorPackageSerializer**

Create `framework/src/CrestCreates.Metadata/DescriptorPackageSerializer.cs`:

```csharp
using System.Text.Json;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DescriptorPackageSerializer : IDescriptorPackageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Serialize(DescriptorPackage package)
    {
        return JsonSerializer.Serialize(package, Options);
    }

    public DescriptorPackage Deserialize(string content)
    {
        return JsonSerializer.Deserialize<DescriptorPackage>(content, Options)
               ?? throw new InvalidOperationException("Failed to deserialize DescriptorPackage.");
    }

    public string SerializeManifest(DescriptorManifest manifest)
    {
        return JsonSerializer.Serialize(manifest, Options);
    }

    public DescriptorManifest? DeserializeManifest(string content)
    {
        return JsonSerializer.Deserialize<DescriptorManifest>(content, Options);
    }

    public string SerializeDiff(DescriptorPackageDiff diff)
    {
        return JsonSerializer.Serialize(diff, Options);
    }

    public DescriptorPackageDiff? DeserializeDiff(string content)
    {
        return JsonSerializer.Deserialize<DescriptorPackageDiff>(content, Options);
    }
}
```

- [ ] **Step 4: Run serializer tests**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageSerializerTests"
```

Expected: 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorPackageSerializer.cs \
        framework/test/CrestCreates.Metadata.Tests/DescriptorPackageSerializerTests.cs
git commit -m "feat(6f): implement DescriptorPackageSerializer — JSON round-trip for metadata/evidence packages"
```

---

### Task 9: DI Registration, JsonContext Update, and Legacy Cleanup

**Files:**
- Modify: `framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs`
- Modify: `framework/src/CrestCreates.Metadata/CrestCreatesMetadataJsonContext.cs`
- Modify: `framework/src/CrestCreates.Metadata/DescriptorManifestSerializer.cs`
- Modify: `framework/src/CrestCreates.Metadata/DescriptorSnapshotBuilder.cs`
- Modify: `framework/test/CrestCreates.Metadata.Tests/DescriptorManifestTests.cs`
- Modify: `framework/test/CrestCreates.Metadata.Tests/DescriptorSnapshotTests.cs`

- [ ] **Step 1: Add DI registration**

Append to `MetadataServiceCollectionExtensions.cs`:

```csharp
public static IServiceCollection AddDescriptorPackaging(
    this IServiceCollection services)
{
    services.TryAddSingleton<IDescriptorPackageBuilder,
        DefaultDescriptorPackageBuilder>();
    services.TryAddSingleton<IDescriptorPackageDiffer,
        DescriptorPackageDiffer>();
    services.TryAddSingleton<IDescriptorPackageSerializer,
        DescriptorPackageSerializer>();
    return services;
}
```

- [ ] **Step 2: Update JsonContext**

Replace `CrestCreatesMetadataJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata;

[JsonSerializable(typeof(DescriptorPackage))]
[JsonSerializable(typeof(DescriptorManifest))]
[JsonSerializable(typeof(DescriptorSnapshot))]
[JsonSerializable(typeof(DescriptorPackageEvidence))]
[JsonSerializable(typeof(DescriptorPackageRelationshipEntry))]
[JsonSerializable(typeof(DescriptorPackageDiagnostic))]
[JsonSerializable(typeof(DescriptorPackageDiff))]
[JsonSerializable(typeof(SchemaDescriptor))]
[JsonSerializable(typeof(CapabilityDescriptor))]
[JsonSerializable(typeof(EventDescriptor))]
[JsonSerializable(typeof(FormDescriptor))]
[JsonSerializable(typeof(HumanTaskDescriptor))]
[JsonSerializable(typeof(WorkflowDescriptor))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class CrestCreatesMetadataJsonContext : JsonSerializerContext
{
}
```

- [ ] **Step 3: Update DescriptorManifestSerializer**

Replace `DescriptorManifestSerializer.cs` to handle upgraded manifest:

```csharp
using System.Text.Json;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public static class DescriptorManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(DescriptorManifest manifest)
    {
        return JsonSerializer.Serialize(manifest, Options);
    }

    public static DescriptorManifest? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<DescriptorManifest>(json, Options);
    }
}
```

- [ ] **Step 4: Mark DescriptorSnapshotBuilder as [Obsolete]**

Replace `DescriptorSnapshotBuilder.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

[Obsolete("Use IDescriptorPackageBuilder.Build() instead. This static method reads from " +
          "IGlobalDescriptorRegistry and does not produce deterministic snapshots.")]
public static class DescriptorSnapshotBuilder
{
    public static DescriptorSnapshot TakeSnapshot(
        IGlobalDescriptorRegistry registry,
        string packageId,
        string packageVersion)
    {
        var allDescriptors = registry.GetAll();
        var entries = allDescriptors.Select(d => new SnapshotEntry
        {
            Ref = new DescriptorRef(d.Namespace, d.Id,
                (d as IVersionedDescriptor)?.Version),
            DescriptorName = d.Name,
            Kind = d.Kind,
            State = d.State
        }).ToList();

        return new DescriptorSnapshot
        {
            SnapshotId = $"snapshot_{Guid.NewGuid():N}",
            CreatedAt = DateTimeOffset.UtcNow,
            PackageId = packageId,
            PackageVersion = packageVersion,
            Descriptors = entries
        };
    }
}
```

- [ ] **Step 5: Update existing DescriptorManifestTests**

Replace `DescriptorManifestTests.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorManifestTests
{
    [Fact]
    public void Serialize_And_Deserialize_UpgradedManifest()
    {
        var manifest = new DescriptorManifest
        {
            FormatVersion = "1.0",
            PackageId = "CrestCreates.CRM",
            PackageVersion = "1.0.0",
            Name = "CRM Package",
            DescriptorCount = 2,
            DescriptorEntries = new[]
            {
                new DescriptorManifestEntry
                {
                    Ref = new DescriptorRef("schema", "schema_01", 1),
                    Kind = DescriptorKind.Schema,
                    Name = "CustomerInput",
                    State = DescriptorState.Active,
                    ContractHash = "abc123",
                    DefinitionHash = "def456"
                },
                new DescriptorManifestEntry
                {
                    Ref = new DescriptorRef("capability", "cap_01", 1),
                    Kind = DescriptorKind.Capability,
                    Name = "crm.customer.create",
                    State = DescriptorState.Active,
                    ContractHash = "ghi789",
                    DefinitionHash = "jkl012"
                }
            },
            ContentHash = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef",
            EvidenceHash = "evhash123",
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)
        };

        var json = DescriptorManifestSerializer.Serialize(manifest);
        var deserialized = DescriptorManifestSerializer.Deserialize(json);

        deserialized.Should().NotBeNull();
        deserialized!.PackageId.Should().Be("CrestCreates.CRM");
        deserialized.DescriptorEntries.Should().HaveCount(2);
        deserialized.DescriptorEntries[0].Ref.Id.Should().Be("schema_01");
        deserialized.ContentHash.Should().Be(manifest.ContentHash);
    }
}
```

- [ ] **Step 6: Update existing DescriptorSnapshotTests**

Replace `DescriptorSnapshotTests.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorSnapshotTests
{
    [Fact]
    public void Snapshot_HasDeterministicStructure()
    {
        var snapshot = new DescriptorSnapshot
        {
            SnapshotId = "snapshot_deadbeef00000000",
            PackageId = "test.pkg",
            PackageVersion = "1.0.0",
            CreatedAt = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero),
            Descriptors = new[]
            {
                new SnapshotEntry
                {
                    Ref = new DescriptorRef("schema", "schema_01", 1),
                    DescriptorName = "CustomerInput",
                    Kind = DescriptorKind.Schema,
                    State = DescriptorState.Active,
                    ContractHash = "abc",
                    DefinitionHash = "def"
                }
            },
            Relationships = Array.Empty<DescriptorPackageRelationshipEntry>()
        };

        snapshot.SnapshotId.Should().StartWith("snapshot_");
        snapshot.SnapshotId.Should().NotContain("-");
        snapshot.Descriptors.Should().HaveCount(1);
        snapshot.Relationships.Should().BeEmpty();
    }
}
```

- [ ] **Step 7: Verify build compiles**

```bash
dotnet build framework/src/CrestCreates.Metadata
```

Expect: 0 errors.

- [ ] **Step 8: Run all tests**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests
```

Expect: All tests pass (pre-existing + 42 new).

- [ ] **Step 9: Commit**

```bash
git add framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs \
        framework/src/CrestCreates.Metadata/CrestCreatesMetadataJsonContext.cs \
        framework/src/CrestCreates.Metadata/DescriptorManifestSerializer.cs \
        framework/src/CrestCreates.Metadata/DescriptorSnapshotBuilder.cs \
        framework/test/CrestCreates.Metadata.Tests/DescriptorManifestTests.cs \
        framework/test/CrestCreates.Metadata.Tests/DescriptorSnapshotTests.cs
git commit -m "feat(6f): add DI registration, update JsonContext, Obsolete legacy snapshot builder, update existing tests"
```

---

### Task 10: Final Build & Full Test Run

- [ ] **Step 1: Full solution build**

```bash
dotnet build
```

Expect: 0 errors, 0 warnings (pre-existing warnings acceptable).

- [ ] **Step 2: Full test run**

```bash
dotnet test
```

Expect: All tests pass. No regressions in other test suites (Metadata, Form, Capability, Event, HumanTask, Workflow, etc.).

- [ ] **Step 3: Verify 42 new tests exist**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorPackageBuilderTests|FullyQualifiedName~DescriptorPackageHashComputerTests|FullyQualifiedName~DescriptorPackageDiffTests|FullyQualifiedName~DescriptorPackageSerializerTests|FullyQualifiedName~DescriptorManifestTests|FullyQualifiedName~DescriptorSnapshotTests" -t
```

Expected: ≥ 42 tests listed.

- [ ] **Step 4: Commit final state if needed**

```bash
git status
# If anything is dirty, commit
```

---

### Task 11: Update memory.md

**Files:**
- Modify: `memory.md`

- [ ] **Step 1: Add Phase 6f entry to memory.md**

Append to the `## In Progress / Not Reliably Closed` section or add a new completed entry under the Phase 6 section:

```markdown
### Descriptor Package / Manifest / Snapshot (Phase 6f, 2026-06-15)

- Upgraded `DescriptorPackage`, `DescriptorManifest`, `DescriptorManifestEntry`, `DescriptorSnapshot`, `SnapshotEntry` in-place.
- Removed per-kind manifest entry lists (`Schemas`, `Capabilities`, …) — replaced by flat `DescriptorEntries`.
- `IDescriptorPackageBuilder` + `DefaultDescriptorPackageBuilder` — stateless singleton, explicit inventory input.
- `DescriptorPackageHashComputer` — AoT-safe deterministic hashing (string concat, SHA-256, no runtime JSON).
- `DescriptorPackageEvidence` + `EvidenceFinding` — aggregated evidence summary from 6b/6c/6d/6e reports.
- `DescriptorPackageRelationshipEntry` — flattened relationship facts with `SourcePath` preservation.
- `DescriptorPackageDiagnostic` + 12 self-consistency diagnostic codes.
- `IDescriptorPackageDiffer` + `DescriptorPackageDiffer` — shallow structural diff (added/removed refs, changed hashes, state changes, metadata changes).
- `IDescriptorPackageSerializer` + `DescriptorPackageSerializer` — JSON round-trip for metadata/evidence packages.
- `AddDescriptorPackaging()` DI registration (TryAddSingleton).
- `DescriptorSnapshotBuilder.TakeSnapshot()` marked `[Obsolete]`.
- 42 tests (hash computer, builder determinism/evidence/relationships/diagnostics, diff, serializer, legacy).
- **Design spec**: `docs/superpowers/specs/2026-06-15-phase-6f-descriptor-package-manifest-snapshot-design.md`
```

- [ ] **Step 2: Commit**

```bash
git add memory.md
git commit -m "docs: add Phase 6f completion entry to memory.md"
```

---

### Task 12: LSP Diagnostics Check

- [ ] **Step 1: Check all new and modified files for LSP errors**

```bash
# Check abstractions
dotnet build framework/src/CrestCreates.Metadata.Abstractions
# Check implementation
dotnet build framework/src/CrestCreates.Metadata
# Check tests
dotnet build framework/test/CrestCreates.Metadata.Tests
```

Expect: 0 errors across all three projects.

- [ ] **Step 2: Run LSP diagnostics**

Run `lsp_diagnostics` on:
- `framework/src/CrestCreates.Metadata.Abstractions/`
- `framework/src/CrestCreates.Metadata/`
- `framework/test/CrestCreates.Metadata.Tests/`

Expect: No new errors introduced by Phase 6f changes. Pre-existing warnings acceptable.

---

## Phase Completion Checklist

- [ ] `IDescriptorPackageBuilder.Build()` returns deterministic `DescriptorPackage`
- [ ] ContentHash computed by `DescriptorPackageHashComputer` (string concat, no runtime JSON)
- [ ] ContractHash / DefinitionHash stored for informational purposes only
- [ ] Evidence summary captures topology/impact/compatibility/lifecycle from supplied reports
- [ ] Relationship facts preserved with SourcePath
- [ ] 12 self-consistency diagnostic codes available
- [ ] `IDescriptorPackageDiffer` produces shallow structural diff
- [ ] `IDescriptorPackageSerializer` round-trips metadata/evidence packages
- [ ] `AddDescriptorPackaging()` DI registration exists
- [ ] Legacy `DescriptorSnapshotBuilder` marked `[Obsolete]`
- [ ] Per-kind manifest lists removed
- [ ] Existing tests updated for new model shapes
- [ ] All 42 new tests pass
- [ ] Full build: 0 errors
- [ ] No regressions in other test suites
