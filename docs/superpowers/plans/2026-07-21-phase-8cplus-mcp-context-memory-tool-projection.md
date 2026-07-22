# Phase 8c+ MCP Context and Memory Tool Projection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the MCP Context and Memory Tool Projection — 4 read-only MCP tools backed by a shared read core, with projection-neutral security infrastructure and composable handler registration.

**Architecture:** MCP tools delegate to a shared `AgentMemoryReadCore` which orchestrates visibility, budget, tenant isolation, and credential issuance through projection-neutral interfaces. Agent Tool handlers are refactored to use the same ReadCore with a governance wrapper. Old interfaces become adapters over the new canonical store. Handler registration shifts from replace-semantics to composable `ICapabilityHandlerModule`.

**Tech Stack:** .NET 10, Source Generators, xUnit 2.9.3, FluentAssertions, Moq, AutoFixture, NativeAOT publish fixtures

**Spec:** `docs/superpowers/specs/2026-07-21-phase-8cplus-mcp-context-memory-tool-projection-design.md` (APPROVED)

## Global Constraints

- SDK: .NET 10.0.100, `rollForward: latestMinor`, see `global.json`
- Solution: `CrestCreates.slnx` (XML `.slnx` format, not `.sln`)
- New E2E test projects must be added to both `CrestCreates.slnx` and `solutions/CrestCreates.All.slnx`
- Central package management: `Directory.Packages.props` — PackageReference entries MUST NOT include Version attributes
- AoT/Trim config: `Directory.Build.Aot.props`; test projects suppress AoT in `Directory.Build.targets`
- Source Generator target: `netstandard2.0`
- New projects follow existing namespace conventions: `CrestCreates.Agent.Memory.Projection.Abstractions`, etc.
- Never delete files — move to `./99_RecycleBin/`
- All new runtime code must be NativeAOT-compatible (Tier 2)
- `Mcp.Memory` has no direct project reference to the old `Agent.Memory.Tools` / `Agent.Memory.Tools.Abstractions` assemblies and does not use their old security/runtime interfaces. Forwarded shared DTOs physically owned by `Projection.Abstractions` (namespace `CrestCreates.Agent.Memory.Tools`) remain allowed.
- `Agent.Memory.Projection.Abstractions` does NOT reference `Capability.Abstractions`
- Principal construction must be fail-closed: all identity fields non-null/non-empty — use `RequireIdentity()` helper, never `?? string.Empty`
- Coordinator internal partial-failure compensation: revoke already-created artifacts before CompensationToken is returned
- Store `GetAsync` returns effective state view without persisting expiry transitions
- Adapter filtering: old interfaces only process `CallerKind.AgentTool` artifacts (including Revoke)
- `ICapabilityHandlerModule` lives in `CrestCreates.Capability.Abstractions`, not Memory Projection
- Generated modules use static `Instance` singleton, not DI reflection activation
- `AgentMemoryArtifactOriginKind` includes `TrustedHostOperation = 2`
- Lifetime policy is origin-aware: 60s for `McpInvocation`, session-lifetime cap for `McpSessionOperation`, existing scope lifetimes for `AgentToolInvocation`/`TrustedHostOperation`
- `CompensationToken` is non-forgeable: opaque `TokenId` with Coordinator-internal tracking, not a public record with forgeable fields
- `ICapabilityHandlerResolver` and `CapabilityHandlerResolver` must point to the same composed instance
- Test-after-each-phase: run relevant tests immediately after each phase, not deferred to the end
- Per-operation quota keys on `OriginBindingHash`, not `Principal.SecurityContextId`
- All stores and Coordinator inject `TimeProvider`, never use `DateTimeOffset.UtcNow` directly
- TypeForwarded types keep namespace `CrestCreates.Agent.Memory.Tools` (not `.Abstractions`)
- Generator emits `RegisterServices()` + `GeneratedCapabilityHandlerModule` — callers register the module instance, not the generator

## File Structure

### New Projects (4 source + 4 test)

```
src/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Abstractions/
  ├── CrestCreates.Agent.Memory.Projection.Abstractions.csproj
  ├── Security/
  │   ├── AgentMemoryAccessPrincipal.cs
  │   ├── AgentMemoryArtifactOrigin.cs
  │   ├── AgentMemoryCallerKind.cs
  │   ├── AgentMemoryArtifactOriginKind.cs
  │   ├── AgentMemoryAccessScope.cs
  │   ├── IAgentMemoryAccessScopeProvider.cs
  │   ├── IAgentMemoryAccessScopeProviderCapabilities.cs
  │   ├── IAgentMemoryAccessArtifactCoordinator.cs
  │   ├── IAgentMemoryAccessHandleResolver.cs
  │   ├── IAgentMemoryAccessGrantResolver.cs
  │   ├── IAgentMemoryAccessHandleStore.cs
  │   ├── IAgentMemoryAccessGrantStore.cs
  │   ├── IAgentMemoryAccessArtifactBatchStore.cs
  │   ├── AgentMemoryAccessResourceHandle.cs
  │   ├── AgentMemoryAccessSourceGrant.cs
  │   ├── AgentMemoryAccessPreparedArtifacts.cs
  │   ├── AgentMemoryAccessPreparedArtifact.cs
  │   ├── AgentMemoryAccessArtifactBatchKey.cs
  │   ├── AgentMemoryAccessArtifactBatchOriginKind.cs
  │   ├── AgentMemoryAccessArtifactState.cs
  │   ├── AgentMemoryAccessArtifactKind.cs
  │   ├── AgentMemoryAccessArtifact.cs
  │   ├── AgentMemoryAccessResolvedResource.cs
  │   ├── AgentMemoryAccessResolvedGrant.cs
  │   ├── AgentMemoryAccessHandleIssueResult.cs
  │   ├── AgentMemoryAccessGrantIssueResult.cs
  │   ├── AgentMemoryArtifactCompensationToken.cs
  │   ├── AgentMemorySecurityArtifactReceipt.cs
  │   ├── AgentMemoryArtifactBatchReceipt.cs
  │   └── IAgentMemoryArtifactLifetimePolicy.cs
  ├── ReadCore/
  │   ├── IAgentMemoryReadCore.cs
  │   ├── IAgentContextReadCore.cs
  │   ├── IAgentMemorySourceExpandCore.cs
  │   ├── AgentMemoryReadCoreOutcome.cs
  │   ├── AgentMemoryReadRequest.cs
  │   ├── AgentMemoryReadResult.cs
  │   ├── AgentContextReadRequest.cs
  │   ├── AgentContextReadResult.cs
  │   ├── AgentMemorySourceExpandRequest.cs
  │   ├── AgentMemorySourceExpandResult.cs
  │   ├── IAgentMemoryContextHandleIssuer.cs
  │   └── AgentMemoryContextHandleIssueResult.cs
  ├── Dto/
  │   ├── RecallAgentContextInput.cs               (protocol-neutral, not MCP-specific)
  │   ├── RecallAgentContextResult.cs              (protocol-neutral, not MCP-specific)
  │   ├── AgentMemoryToolOperationStatus.cs         (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── AgentMemoryToolKind.cs                    (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── AgentMemoryToolConfidence.cs              (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── AgentMemoryToolSourceKind.cs              (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── AgentMemoryToolMemoryStatus.cs            (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── AgentMemoryToolDiagnosticSeverity.cs      (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── AgentMemoryResourceKind.cs                (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── AgentMemoryToolItemDto.cs                 (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── AgentMemoryToolBlockDto.cs                (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── AgentMemorySourceGrantDto.cs              (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── AgentMemoryToolCanonicalHashDto.cs        (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── AgentMemoryToolDiagnosticDto.cs           (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── BuildAgentMemoryPackInput.cs              (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── BuildAgentMemoryPackResult.cs             (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   ├── ExpandAgentMemorySourceInput.cs           (physical source, namespace CrestCreates.Agent.Memory.Tools)
  │   └── ExpandAgentMemorySourceResult.cs          (physical source, namespace CrestCreates.Agent.Memory.Tools)
  └── Json/
      └── AgentMemoryToolEnumConverters.cs          (physical source, namespace CrestCreates.Agent.Memory.Tools — shared converters + base class)

src/Runtime/Agent/CrestCreates.Agent.Memory.Projection/
  ├── CrestCreates.Agent.Memory.Projection.csproj
  ├── Security/
  │   ├── AgentMemoryAccessArtifactCoordinator.cs
  │   ├── AgentMemoryAccessHandleStore.cs
  │   ├── AgentMemoryAccessGrantStore.cs
  │   ├── AgentMemoryAccessArtifactBatchStore.cs
  │   ├── AgentMemoryAccessHandleResolver.cs
  │   ├── AgentMemoryAccessGrantResolver.cs
  │   ├── DefaultAgentMemoryArtifactLifetimePolicy.cs
  │   └── DefaultAgentMemoryContextHandleIssuer.cs       (security credential issuance, not read orchestration)
  ├── Options/
  │   └── AgentMemoryProjectionSecurityOptions.cs
  ├── DescriptorProviders/
  │   └── AgentMemoryProjectionSchemaProviders.cs     (shared read schema ownership)
  └── ProjectionSecurityServiceCollectionExtensions.cs

src/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore/
  ├── CrestCreates.Agent.Memory.ReadCore.csproj
  ├── ReadCore/
  │   ├── AgentMemoryReadCore.cs
  │   ├── AgentContextReadCore.cs
  │   └── AgentMemorySourceExpandCore.cs
  └── ReadCoreServiceCollectionExtensions.cs

src/Integrations/CrestCreates.Mcp.Memory/
  ├── CrestCreates.Mcp.Memory.csproj
  ├── Handlers/
  │   ├── McpMemoryRecallHandler.cs
  │   ├── McpContextRecallHandler.cs
  │   └── McpSourceExpandHandler.cs                   (shared by ctx_expand and memory_source_expand)
  ├── Security/
  │   └── McpMemoryArtifactOriginFactory.cs           (centralized MCP Origin/BindingHash construction)
  ├── Json/
  │   └── McpMemoryJsonContextContributor.cs
  ├── Validation/
  │   └── McpMemoryStartupValidator.cs                (IBootstrapValidator for scope provider gate)
  ├── McpMemoryDescriptorProviders.cs
  ├── McpMemorySpecifications.cs
  └── McpMemoryServiceCollectionExtensions.cs

tests/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Tests/
  └── CrestCreates.Agent.Memory.Projection.Tests.csproj

tests/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore.Tests/
  └── CrestCreates.Agent.Memory.ReadCore.Tests.csproj

tests/Integrations/CrestCreates.Mcp.Memory.Tests/
  └── CrestCreates.Mcp.Memory.Tests.csproj

tests/Integrations/CrestCreates.Mcp.Memory.E2E.Tests/
  └── CrestCreates.Mcp.Memory.E2E.Tests.csproj
```

### Modified Files

```
src/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Abstractions/
  ├── TypeForwards.cs                                  (NEW — [assembly: TypeForwardedTo] declarations)
  └── (migrated type source files moved to 99_RecycleBin, NOT deleted)

src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/
  ├── AgentMemoryToolServiceCollectionExtensions.cs   (register adapters + module, not old stores)
  ├── AgentMemoryToolDescriptorProviders.cs           (remove shared read schemas)
  ├── Adapters/
  │   ├── AgentMemoryResourceHandleStoreAdapter.cs
  │   ├── AgentMemorySourceGrantStoreAdapter.cs
  │   ├── AgentMemorySecurityArtifactBatchStoreAdapter.cs
  │   ├── AgentMemoryResourceHandleResolverAdapter.cs
  │   ├── AgentMemorySourceGrantResolverAdapter.cs
  │   ├── AgentMemorySecurityArtifactCoordinatorAdapter.cs
  │   └── LegacyAgentMemoryAccessScopeProviderAdapter.cs  (implements NEW interface, wraps OLD provider)
  ├── Handlers/
  │   ├── AgentMemoryToolHandlerBase.cs               (add ToReadPrincipal, ToArtifactOrigin with RequireIdentity)
  │   ├── BuildAgentMemoryPackHandler.cs              (refactor to use IAgentMemoryReadCore)
  │   └── ExpandAgentMemorySourceHandler.cs           (refactor to use IAgentMemorySourceExpandCore)

src/Runtime/Capability/CrestCreates.Capability.Abstractions/
  ├── ICapabilityHandlerModule.cs                     (NEW)
  ├── CapabilityHandlerResolver.cs                    (add internal CopyRegistrationsTo)
  └── CapabilityHandlerResolverProvider.cs            (add public ApplyLegacyRegistrations)

src/Runtime/Capability/CrestCreates.Capability/
  ├── CapabilityServiceCollectionExtensions.cs        (module-aware factory, both concrete + interface resolver)
  └── LegacyCapabilityHandlerModule.cs                (NEW)

src/Tooling/CrestCreates.CodeGenerator/
  └── SchemaCapabilityGenerator/HandlerInvokerSourceGenerator.cs  (emit module + RegisterServices, remove RemoveAll)

tests/Tooling/CrestCreates.CodeGenerator.Tests/
  └── SchemaCapabilityGenerator/HandlerInvokerSourceGeneratorTests.cs  (update snapshot tests)

src/Integrations/CrestCreates.Mcp.Abstractions/
  └── IMcpJsonContextContributor.cs                    (NEW)

src/Integrations/CrestCreates.Mcp/
  ├── McpToolRuntimeSnapshotBuilder.cs                (add IMcpJsonContextContributor composition)
  ├── McpToolSchemaClosureResolver.cs                 (NEW)
  ├── McpJsonSchemaProjector.cs                       (MODIFY — add referencedSchemas parameter)
  ├── McpToolSchemaParityValidator.cs                 (MODIFY — add referencedSchemas parameter)
  └── McpServiceCollectionExtensions.cs               (inject contributors into builder)

tests/Boundary/CrestCreates.DependencyBoundaries.Tests/
  └── (add Mcp.Memory boundary rules)
```

---

## Phase 1: Foundation — Projection.Abstractions Assembly + TypeForward

### Task 1: Create project scaffold

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Abstractions/CrestCreates.Agent.Memory.Projection.Abstractions.csproj`
- Modify: `CrestCreates.slnx`, `solutions/CrestCreates.All.slnx`, `solutions/CrestCreates.Runtime.slnx`

- [ ] **Step 1: Create the .csproj file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>CrestCreates.Agent.Memory.Projection.Abstractions</RootNamespace>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../Core/CrestCreates.Core.Abstractions/CrestCreates.Core.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="../CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

Note: Does NOT reference `CrestCreates.Capability.Abstractions` or `CrestCreates.Agent.Tools.Abstractions`. Directly references `Metadata.Abstractions` because `DescriptorRef` and `CanonicalHash` appear in Projection's public contracts. Relative paths use `../../../Core/` (3 levels up from `src/Runtime/Agent/<ProjectDir>/`).

- [ ] **Step 2: Add to solution files**

Run:
```bash
dotnet sln CrestCreates.slnx add src/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Abstractions/CrestCreates.Agent.Memory.Projection.Abstractions.csproj
dotnet sln solutions/CrestCreates.All.slnx add src/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Abstractions/CrestCreates.Agent.Memory.Projection.Abstractions.csproj
dotnet sln solutions/CrestCreates.Runtime.slnx add src/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Abstractions/CrestCreates.Agent.Memory.Projection.Abstractions.csproj
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Abstractions`
Expected: Build succeeds with no errors

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: scaffold Agent.Memory.Projection.Abstractions project"
```

---

### Task 2: Define projection-neutral security types (Principal, Origin, Scope, enums)

**Files:**
- Create: `Security/AgentMemoryCallerKind.cs`
- Create: `Security/AgentMemoryArtifactOriginKind.cs`
- Create: `Security/AgentMemoryAccessPrincipal.cs`
- Create: `Security/AgentMemoryArtifactOrigin.cs`
- Create: `Security/AgentMemoryAccessScope.cs`

- [ ] **Step 1: Write the enums**

```csharp
// AgentMemoryCallerKind.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public enum AgentMemoryCallerKind
{
    Unknown = 0,
    AgentTool = 1,
    Mcp = 2
}
```

```csharp
// AgentMemoryArtifactOriginKind.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public enum AgentMemoryArtifactOriginKind
{
    Unknown = 0,
    AgentToolInvocation = 1,
    TrustedHostOperation = 2,
    McpInvocation = 3,
    McpSessionOperation = 4
}
```

- [ ] **Step 2: Write AgentMemoryAccessPrincipal**

```csharp
// AgentMemoryAccessPrincipal.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryAccessPrincipal
{
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public required AgentMemoryCallerKind CallerKind { get; init; }
    public required string CallerId { get; init; }
    public required string SecurityContextId { get; init; }
}
```

- [ ] **Step 3: Write AgentMemoryArtifactOrigin**

```csharp
// AgentMemoryArtifactOrigin.cs
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryArtifactOrigin
{
    public required AgentMemoryArtifactOriginKind Kind { get; init; }
    public required CanonicalHash BindingHash { get; init; }
    public required string OperationId { get; init; }
}
```

- [ ] **Step 4: Write AgentMemoryAccessScope**

Must preserve ALL fields from existing `AgentMemoryToolAccessScope` (namespace `CrestCreates.Agent.Memory.Tools`). The existing type uses `IReadOnlyList<DescriptorRef>` (not `IReadOnlyList<string>`), and includes `MaxRecallCharacters` and `MaxExpansionCharacters`. `DescriptorRef` is in `CrestCreates.Metadata.Abstractions`.

```csharp
// AgentMemoryAccessScope.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryAccessScope
{
    public required string TenantId { get; init; }
    public required IReadOnlyList<DescriptorRef> VisibleDescriptorRefs { get; init; }
    public required bool AllowUnscopedMemory { get; init; }

    public required int MaxVisibleDescriptorRefs { get; init; }
    public required int MaxRecallCount { get; init; }
    public required int MaxRecallCharacters { get; init; }
    public required int MaxExpansionCharacters { get; init; }
    public required int MaxContextRecallCharacters { get; init; }
    public required int MaxCompressedBlockCount { get; init; }
    public required int MaxCompressedBlockCharacters { get; init; }
    public required int MaxCandidateCount { get; init; }
    public required int MaxCandidateCharacters { get; init; }
    public required int MaxSourceRefsPerArtifact { get; init; }
    public required int MaxGrantsPerResource { get; init; }
    public required int MaxResourceHandlesPerOperation { get; init; }
    public required int MaxGrantsPerOperation { get; init; }
    public required int MaxActiveResourceHandlesPerResource { get; init; }
    public required int MaxAuditFacts { get; init; }
    public required int MaxTagsPerResource { get; init; }
    public required TimeSpan ExpansionGrantLifetime { get; init; }
    public required TimeSpan ResourceHandleLifetime { get; init; }
}
```

- [ ] **Step 5: Build and commit**

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Abstractions`
Expected: Build succeeds

```bash
git add -A && git commit -m "feat: add projection-neutral security types (Principal, Origin, Scope, enums)"
```

---

### Task 3: TypeForward migration — move shared DTOs, Enums, Converters

**CRITICAL: This task must execute BEFORE Task 4 (security interfaces) and Task 5 (ReadCore interfaces)** because Tasks 4-5 reference TypeForwarded types (`AgentMemoryResourceKind`, `AgentMemoryToolKind`, `AgentMemoryToolConfidence`, `AgentMemoryToolOperationStatus`, `AgentMemoryToolItemDto`, etc.) that only become available in Projection.Abstractions after this migration.

**Files:**
- Create: `Dto/` — all TypeForwarded DTO and enum files (namespace `CrestCreates.Agent.Memory.Tools`)
- Create: `Json/AgentMemoryToolEnumConverters.cs` — shared converters + base class (namespace `CrestCreates.Agent.Memory.Tools`)
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Abstractions/TypeForwards.cs` — `[assembly: TypeForwardedTo]` declarations
- Modify: `Tools.Abstractions.csproj` — add ProjectReference to Projection.Abstractions
- Move: Migrated type source files from Tools.Abstractions to `99_RecycleBin/` (NOT deleted)

**Namespace rule:** All TypeForwarded types keep their original namespace `CrestCreates.Agent.Memory.Tools`. The TypeForward declarations in the old assembly must use this same namespace. No `TypeForwards.cs` in the new assembly — only physical source files.

**Types to TypeForward** (public shape unchanged):
- Enums: `AgentMemoryToolOperationStatus`, `AgentMemoryToolKind`, `AgentMemoryToolConfidence`, `AgentMemoryToolSourceKind`, `AgentMemoryToolMemoryStatus`, `AgentMemoryToolDiagnosticSeverity`, `AgentMemoryResourceKind`
- DTOs: `AgentMemoryToolItemDto`, `AgentMemoryToolBlockDto`, `AgentMemorySourceGrantDto`, `AgentMemoryToolCanonicalHashDto`, `AgentMemoryToolDiagnosticDto`
- Input/Result: `BuildAgentMemoryPackInput`, `BuildAgentMemoryPackResult`, `ExpandAgentMemorySourceInput`, `ExpandAgentMemorySourceResult`
- Converters: `AgentMemoryToolEnumConverter<T>`, `AgentMemoryToolOperationStatusJsonConverter`, `AgentMemoryToolKindJsonConverter`, `AgentMemoryToolConfidenceJsonConverter`, `AgentMemoryToolSourceKindJsonConverter`, `AgentMemoryToolMemoryStatusJsonConverter`, `AgentMemoryToolDiagnosticSeverityJsonConverter`

**Types that STAY in Tools.Abstractions** (public shape changes or write-specific):
- `AgentMemoryToolCandidateStatus`, `AgentMemoryToolCandidateStatusJsonConverter`
- All security types with `AgentMemoryToolPrincipal` in their public API
- `AgentMemoryToolJsonSerializerContext` (covers both read and write DTOs)

- [ ] **Step 0: Build pre-migration baseline and compile binary consumer**

Before any migration: build the current `Tools.Abstractions` assembly. Compile a binary consumer test fixture against it. Store only the compiled fixture under test assets. This fixture will NOT be recompiled after migration — it validates true binary compatibility.

- [ ] **Step 1: Copy enum and DTO source files to Projection.Abstractions Dto/ directory**

Keep the same namespace: `CrestCreates.Agent.Memory.Tools`.

- [ ] **Step 2: Copy converter types to Projection.Abstractions Json/ directory**

Move `AgentMemoryToolEnumConverter<T>` and the 6 shared converters. Leave `AgentMemoryToolCandidateStatusJsonConverter` in Tools.Abstractions.

- [ ] **Step 3: Add ProjectReference in Tools.Abstractions csproj**

```xml
<ProjectReference Include="..\CrestCreates.Agent.Memory.Projection.Abstractions\CrestCreates.Agent.Memory.Projection.Abstractions.csproj" />
```

- [ ] **Step 4: Move migrated type source files from Tools.Abstractions to 99_RecycleBin**

For files containing both migrated and non-migrated types, split them carefully. Move fully-migrated files to `99_RecycleBin/src/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Abstractions/`. Do NOT delete files.

- [ ] **Step 5: Add TypeForwards.cs in Tools.Abstractions**

```csharp
// TypeForwards.cs
using System.Runtime.CompilerServices;

[assembly: TypeForwardedTo(typeof(CrestCreates.Agent.Memory.Tools.AgentMemoryToolOperationStatus))]
[assembly: TypeForwardedTo(typeof(CrestCreates.Agent.Memory.Tools.AgentMemoryToolKind))]
// ... one per migrated type including converters
```

- [ ] **Step 6: Build and verify no breakage**

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Abstractions`
Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.Memory.Tools`
Expected: Both build successfully.

- [ ] **Step 7: Run existing tests to verify source-level compatibility**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests`
Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.E2E.Tests`
Expected: All tests pass

- [ ] **Step 8: Run precompiled binary consumer against new assemblies**

Run the precompiled consumer (from Step 0) against:
- new `Projection.Abstractions` (physical type location)
- forwarding `Tools.Abstractions` (TypeForward declarations)

Do NOT rebuild the consumer. Verify: consumer can still load and use the forwarded types at runtime (DTOs, enums, enum converters, JSON context).

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat: TypeForward shared types to Projection.Abstractions, preserve binary compatibility"
```

---

### Task 4: Define projection-neutral security interfaces

**Depends on**: Task 3 (TypeForwarded types must be available for `AgentMemoryResourceKind` etc.)

**Files:**
- Create: All security interface files in `Security/` directory

All files using TypeForwarded types must include `using CrestCreates.Agent.Memory.Tools;` because the forwarded types retain their original namespace.

- [ ] **Step 1: Write IAgentMemoryAccessScopeProvider and IAgentMemoryAccessScopeProviderCapabilities**

```csharp
// IAgentMemoryAccessScopeProvider.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemoryAccessScopeProvider
{
    ValueTask<AgentMemoryAccessScope> ResolveAsync(
        AgentMemoryAccessPrincipal principal,
        CancellationToken ct = default);
}
```

```csharp
// IAgentMemoryAccessScopeProviderCapabilities.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemoryAccessScopeProviderCapabilities
{
    bool Supports(AgentMemoryCallerKind callerKind);
}
```

Note: `IAgentMemoryAccessScopeProviderCapabilities` is implemented by the actual Scope Provider itself (e.g., `LegacyAgentMemoryAccessScopeProviderAdapter`). There is no separate "Capabilities Service". The startup validator checks: `scopeProvider is IAgentMemoryAccessScopeProviderCapabilities caps && caps.Supports(Mcp)`.

- [ ] **Step 2: Write IAgentMemoryAccessArtifactCoordinator**

```csharp
// IAgentMemoryAccessArtifactCoordinator.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemoryAccessArtifactCoordinator
{
    ValueTask<AgentMemoryAccessPreparedArtifacts> PrepareAsync(
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        string purpose,
        int ordinal,
        IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
        IReadOnlyList<AgentMemoryAccessSourceGrant> grants,
        CancellationToken ct = default);

    ValueTask RevokeCreatedAsync(
        AgentMemoryArtifactCompensationToken token,
        CancellationToken ct = default);
}
```

- [ ] **Step 3: Write IAgentMemoryAccessHandleResolver and IAgentMemoryAccessGrantResolver**

```csharp
// IAgentMemoryAccessHandleResolver.cs
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemoryAccessHandleResolver
{
    ValueTask<AgentMemoryAccessResolvedResource?> ResolveAsync(
        string handleId,
        AgentMemoryResourceKind expectedKind,
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        CancellationToken ct = default);
}
```

```csharp
// IAgentMemoryAccessGrantResolver.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemoryAccessGrantResolver
{
    ValueTask<AgentMemoryAccessResolvedGrant?> ResolveAsync(
        string grantId,
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Write store interfaces**

```csharp
// IAgentMemoryAccessHandleStore.cs
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemoryAccessHandleStore
{
    ValueTask<AgentMemoryAccessResourceHandle?> GetAsync(string handleId, CancellationToken ct = default);
    ValueTask<AgentMemoryAccessHandleIssueResult> TryIssueBatchAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
        int maxActivePerResource,
        int maxActivePerOperation,
        CancellationToken ct = default);
    ValueTask RevokeAsync(string handleId, AgentMemoryCallerKind expectedCallerKind, CancellationToken ct = default);
}
```

```csharp
// IAgentMemoryAccessGrantStore.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemoryAccessGrantStore
{
    ValueTask<AgentMemoryAccessSourceGrant?> GetAsync(string grantId, CancellationToken ct = default);
    ValueTask<AgentMemoryAccessGrantIssueResult> TryIssueBatchAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessSourceGrant> grants,
        int maxActivePerResource,
        int maxActivePerOperation,
        CancellationToken ct = default);
    ValueTask RevokeAsync(string grantId, AgentMemoryCallerKind expectedCallerKind, CancellationToken ct = default);
}
```

```csharp
// IAgentMemoryAccessArtifactBatchStore.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemoryAccessArtifactBatchStore
{
    ValueTask<IReadOnlyList<AgentMemoryAccessPreparedArtifact>> PrepareAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessPreparedArtifact> plan,
        CancellationToken ct = default);

    ValueTask RevokeCreatedAsync(
        AgentMemoryAccessArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryAccessPreparedArtifact> artifacts,
        CancellationToken ct = default);
}
```

Note: Uses `AgentMemoryAccessPreparedArtifact` (new Projection type), NOT `AgentMemoryPreparedSecurityArtifact` (old Tools type). The Batch Store preserves full batch plan idempotency, per-artifact `CreatedByBatch`/`ReusedExisting` tracking, and precise compensation capability. No `GetAsync`/`StoreAsync` simple key-store methods — those responsibilities are internal to the implementation.

- [ ] **Step 5: Write receipt and compensation types**

```csharp
// AgentMemorySecurityArtifactReceipt.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemorySecurityArtifactReceipt
{
    public AgentMemoryArtifactBatchReceipt? HandleBatch { get; init; }
    public AgentMemoryArtifactBatchReceipt? GrantBatch { get; init; }
}
```

```csharp
// AgentMemoryArtifactBatchReceipt.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryArtifactBatchReceipt
{
    public required string BatchHash { get; init; }
    public required int Count { get; init; }
    public required bool ReusedExisting { get; init; }
}
```

```csharp
// AgentMemoryArtifactCompensationToken.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Non-forgeable compensation token. TokenId is cryptographically random opaque.
/// Coordinator maintains internal short-lived mapping from TokenId to newly-created artifact IDs.
/// RevokeCreatedAsync is one-shot and idempotent. Tokens expire after tracking window.
/// </summary>
public sealed record AgentMemoryArtifactCompensationToken
{
    public required string TokenId { get; init; }
}
```

CompensationToken lifecycle:
- All batch entries reused existing → `CompensationToken = null`
- Partial reuse, partial new creation → Token covers only newly-created artifacts
- `RevokeCreatedAsync` → one-shot, idempotent; repeated calls do not expand revocation scope
- Normal success → Token not used; artifacts await consumption or natural expiry
- Token tracking window expires → Coordinator discards internal mapping; subsequent `RevokeCreatedAsync` is no-op

- [ ] **Step 6: Write IAgentMemoryArtifactLifetimePolicy**

```csharp
// IAgentMemoryArtifactLifetimePolicy.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemoryArtifactLifetimePolicy
{
    TimeSpan GetHandleLifetime(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        string purpose);

    TimeSpan GetGrantLifetime(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        string purpose);
}
```

- [ ] **Step 7: Write remaining security types**

Write: `AgentMemoryAccessResourceHandle`, `AgentMemoryAccessSourceGrant`, `AgentMemoryAccessPreparedArtifacts`, `AgentMemoryAccessPreparedArtifact`, `AgentMemoryAccessArtifactBatchKey`, `AgentMemoryAccessArtifactBatchOriginKind`, `AgentMemoryAccessArtifactState`, `AgentMemoryAccessArtifactKind`, `AgentMemoryAccessArtifact`, `AgentMemoryAccessResolvedResource`, `AgentMemoryAccessResolvedGrant`, `AgentMemoryAccessHandleIssueResult`, `AgentMemoryAccessGrantIssueResult`.

Each follows the pattern of its old counterpart but uses `AgentMemoryAccessPrincipal` instead of `AgentMemoryToolPrincipal`. Include `using CrestCreates.Agent.Memory.Tools;` where referencing TypeForwarded types.

- [ ] **Step 8: Build and commit**

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Abstractions`
Expected: Build succeeds

```bash
git add -A && git commit -m "feat: add projection-neutral security interfaces and supporting types"
```

---

### Task 5: Define ReadCore interfaces and protocol-neutral DTOs

**Depends on**: Task 3 (TypeForwarded types) and Task 4 (security interfaces)

**Files:**
- Create: All ReadCore interface files in `ReadCore/` directory
- Create: `Dto/RecallAgentContextInput.cs` and `Dto/RecallAgentContextResult.cs` (protocol-neutral, not MCP-specific)

All files using TypeForwarded types must include `using CrestCreates.Agent.Memory.Tools;`.

- [ ] **Step 1: Write ReadCore interfaces**

```csharp
// IAgentMemoryReadCore.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemoryReadCore
{
    ValueTask<AgentMemoryReadCoreOutcome<AgentMemoryReadResult>> RecallAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryReadRequest request,
        CancellationToken ct = default);
}
```

```csharp
// IAgentContextReadCore.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentContextReadCore
{
    ValueTask<AgentMemoryReadCoreOutcome<AgentContextReadResult>> RecallContextAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentContextReadRequest request,
        CancellationToken ct = default);
}
```

```csharp
// IAgentMemorySourceExpandCore.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemorySourceExpandCore
{
    ValueTask<AgentMemoryReadCoreOutcome<AgentMemorySourceExpandResult>> ExpandAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemorySourceExpandRequest request,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Write ReadCore outcome**

```csharp
// AgentMemoryReadCoreOutcome.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryReadCoreOutcome<T>
{
    public required T Result { get; init; }
    public required string ScopeFingerprint { get; init; }
    public required int MaximumAuditFacts { get; init; }
    public required AgentMemorySecurityArtifactReceipt ArtifactReceipt { get; init; }
    public AgentMemoryArtifactCompensationToken? CompensationToken { get; init; }
}
```

- [ ] **Step 3: Write request/result types**

```csharp
// AgentMemoryReadRequest.cs
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryReadRequest
{
    public required IReadOnlyList<string> MemoryHandles { get; init; }
    public required IReadOnlyList<AgentMemoryToolKind> Kinds { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required int MaximumCount { get; init; }
    public required int CharacterBudget { get; init; }
    public required AgentMemoryToolConfidence MinimumConfidence { get; init; }
}
```

```csharp
// AgentMemoryReadResult.cs
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryReadResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public required IReadOnlyList<AgentMemoryToolItemDto> Items { get; init; }
    public required int ReturnedCount { get; init; }
    public required bool WasTruncated { get; init; }
    public required bool IsAuthoritative { get; init; } // always false
    public required IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; }
}
```

```csharp
// AgentContextReadRequest.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentContextReadRequest
{
    public required string ContextHandle { get; init; }
    public required int MaximumBlockCount { get; init; }
    public required int CharacterBudget { get; init; }
    public int? StartBlockIndex { get; init; }
    public int? EndBlockIndexExclusive { get; init; }
}
```

```csharp
// AgentContextReadResult.cs
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentContextReadResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public required IReadOnlyList<AgentMemoryToolBlockDto> Blocks { get; init; }
    public required int BlockCount { get; init; }
    public required bool WasTruncated { get; init; }
    public required IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; }
}
```

```csharp
// AgentMemorySourceExpandRequest.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemorySourceExpandRequest
{
    public required string GrantId { get; init; }
    public required int MaximumCharacters { get; init; }
}
```

Note: `MaximumCharacters` matches existing `ExpandAgentMemorySourceInput.MaximumCharacters`. Budget: `MaximumCharacters > 0` and `MaximumCharacters <= scope.MaxExpansionCharacters`.

```csharp
// AgentMemorySourceExpandResult.cs
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemorySourceExpandResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public string? SanitizedContent { get; init; }
    public AgentMemoryToolCanonicalHashDto? CanonicalContentHash { get; init; }
    public required bool WasTruncated { get; init; }
    public required IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; }
        = Array.Empty<AgentMemoryToolDiagnosticDto>();
}
```

Note: Matches existing `ExpandAgentMemorySourceResult` — `CanonicalContentHash` is `AgentMemoryToolCanonicalHashDto?` (not `string?`), no `SourceKind`.

- [ ] **Step 4: Write protocol-neutral RecallAgentContext DTOs**

These are protocol-neutral Capability Contracts (not MCP-specific), placed in Projection.Abstractions per the APPROVED spec:

```csharp
// RecallAgentContextInput.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record RecallAgentContextInput
{
    public required string ContextHandle { get; init; }
    public required int MaximumBlockCount { get; init; }
    public required int CharacterBudget { get; init; }
    public int? StartBlockIndex { get; init; }
    public int? EndBlockIndexExclusive { get; init; }
}
```

```csharp
// RecallAgentContextResult.cs
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record RecallAgentContextResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public required IReadOnlyList<AgentMemoryToolBlockDto> Blocks { get; init; }
    public required int BlockCount { get; init; }
    public required bool WasTruncated { get; init; }
    public required IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; }
        = Array.Empty<AgentMemoryToolDiagnosticDto>();
}
```

- [ ] **Step 5: Write IAgentMemoryContextHandleIssuer**

```csharp
// IAgentMemoryContextHandleIssuer.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemoryContextHandleIssuer
{
    ValueTask<AgentMemoryContextHandleIssueResult> IssueForCallerAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        string trustedContextId,
        CancellationToken ct = default);
}
```

```csharp
// AgentMemoryContextHandleIssueResult.cs
namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryContextHandleIssueResult
{
    public required string HandleId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
```

- [ ] **Step 6: Build and commit**

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Abstractions`
Expected: Build succeeds

```bash
git add -A && git commit -m "feat: add ReadCore interfaces, request/result types, ContextHandleIssuer, and RecallAgentContext DTOs"
```

---

## Phase 2: Projection Implementation Assembly

### Task 6: Create Projection project and implement canonical security infrastructure

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Projection/CrestCreates.Agent.Memory.Projection.csproj`
- Create: All implementation files in `Security/` directory
- Create: `Options/AgentMemoryProjectionSecurityOptions.cs`
- Create: `DescriptorProviders/AgentMemoryProjectionSchemaProviders.cs`
- Create: `ProjectionSecurityServiceCollectionExtensions.cs`
- Modify: Solution files

- [ ] **Step 1: Create .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>CrestCreates.Agent.Memory.Projection</RootNamespace>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Agent.Memory.Projection.Abstractions\CrestCreates.Agent.Memory.Projection.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Agent.Memory.Abstractions\CrestCreates.Agent.Memory.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata\CrestCreates.Metadata.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options" />
  </ItemGroup>
</Project>
```

Note: References `Metadata` (not just `Metadata.Abstractions`) because `AgentMemoryProjectionSchemaProviders` uses `DescriptorProviderRegistry`. No Version attributes on PackageReference (central package management).

- [ ] **Step 2: Add to solution files**

- [ ] **Step 3: Write AgentMemoryProjectionSecurityOptions**

```csharp
// AgentMemoryProjectionSecurityOptions.cs
namespace CrestCreates.Agent.Memory.Projection;

public sealed class AgentMemoryProjectionSecurityOptions
{
    public TimeSpan McpInvocationProvisionalLifetime { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan McpSessionLifetimeCap { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan CompensationTokenTrackingLifetime { get; set; } = TimeSpan.FromMinutes(2);
}
```

- [ ] **Step 4: Implement canonical security infrastructure**

All stores and Coordinator inject `TimeProvider` (Host dependency — production registers `TimeProvider.System`, tests register `FakeTimeProvider`). Projection does NOT register `TimeProvider` itself.

Key implementation notes:
- `AgentMemoryAccessHandleStore.GetAsync` returns effective state (Active→Expired if past expiry) WITHOUT persisting the transition
- `AgentMemoryAccessArtifactCoordinator.PrepareAsync` internally compensates on partial failure
- `AgentMemoryAccessArtifactCoordinator.RevokeCreatedAsync` is one-shot and idempotent
- `AgentMemoryAccessHandleResolver` uses full Principal record equality for authorization
- `DefaultAgentMemoryArtifactLifetimePolicy` is origin-aware
- `DefaultAgentMemoryContextHandleIssuer` self-resolves scope via `IAgentMemoryAccessScopeProvider`, routes through Coordinator — never directly accesses HandleStore. Returns only opaque `HandleId` + `ExpiresAt`.
- Per-operation quota keys on `BatchKey.OriginBindingHash`, not `Principal.SecurityContextId`

- [ ] **Step 5: Implement AgentMemoryProjectionSchemaProviders**

Register shared read DTO schemas as the unique owner.

- [ ] **Step 6: Write ProjectionSecurityServiceCollectionExtensions**

```csharp
public static IServiceCollection AddAgentMemoryProjectionSecurity(
    this IServiceCollection services,
    Action<AgentMemoryProjectionSecurityOptions>? configure = null)
```

Registers all canonical stores, coordinator, resolver, lifetime policy, `DefaultAgentMemoryContextHandleIssuer`, options. Does NOT register `IAgentMemoryAccessScopeProvider` — Host must provide it (or `AddAgentMemoryTools()` registers the legacy adapter). Does NOT register `TimeProvider` — Host must register it (`TimeProvider.System` for production, `FakeTimeProvider` for tests). Missing `IAgentMemoryAccessScopeProvider` fails startup. `DenyAllAgentMemoryAccessScopeProvider` is available for explicit test registration only — never auto-registered as fallback.

- [ ] **Step 7: Build and commit**

```bash
git add -A && git commit -m "feat: implement projection-neutral security infrastructure (canonical store, coordinator, resolver, lifetime policy, options, schema providers)"
```

---

### Task 6b: Create Projection.Tests and run immediately

- [ ] **Step 1: Create test project and add to solutions**

- [ ] **Step 2: Write and run tests**

Key test areas:
- Coordinator internal partial-failure compensation
- CompensationToken lifecycle (null when all reused, partial coverage, one-shot revocation, expired token no-op)
- Store read purification (GetAsync does not persist expiry transitions)
- Full Principal record equality for authorization
- Unknown CallerKind/OriginKind rejection
- Batch Store plan idempotency and precise compensation
- Lifetime policy origin-awareness
- Per-operation quota: same Principal + same OriginBindingHash → over limit fails; same Principal + different OriginBindingHash → separate quotas
- TimeProvider injection (not DateTimeOffset.UtcNow)
- DefaultAgentMemoryContextHandleIssuer: self-resolves scope via IAgentMemoryAccessScopeProvider, routes through Coordinator (never directly accesses HandleStore), McpSessionOperation lifetime cap, multiple SessionOperationId issuance without Batch identity conflict

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "refactor: update AddAgentMemoryTools to use module registration, adapters, and RegisterServices"
```

- [ ] **Step 4: Update existing Agent Memory Tool test hosts to register TimeProvider**

Projection/ReadCore do not auto-register `TimeProvider`. All test hosts calling `AddAgentMemoryTools()` must explicitly register:

```csharp
// Unit/integration tests
services.AddSingleton<TimeProvider>(fakeTimeProvider);

// Or for simple startup tests
services.AddSingleton(TimeProvider.System);
```

Current startup tests call `AddAgentMemoryTools()` without registering `TimeProvider` — they will fail at resolution time after this refactor.

---

### Task 7: Implement ReadCore

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore/` — all files
- Modify: Solution files

- [ ] **Step 1: Create .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>CrestCreates.Agent.Memory.ReadCore</RootNamespace>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Agent.Memory.Projection.Abstractions\CrestCreates.Agent.Memory.Projection.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Agent.Memory.Projection\CrestCreates.Agent.Memory.Projection.csproj" />
    <ProjectReference Include="..\CrestCreates.Agent.Memory.Abstractions\CrestCreates.Agent.Memory.Abstractions.csproj" />
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Implement AgentMemoryReadCore, AgentContextReadCore, AgentMemorySourceExpandCore**

Key: budget fail-closed BEFORE store access; scope resolution via `IAgentMemoryAccessScopeProvider`; handle/grant issuance through Coordinator; `IsAuthoritative` always false; `AgentMemorySourceExpandCore` zero security artifact store writes. ReadCore consumes `IAgentMemoryContextHandleIssuer` (implemented by Projection) — does not own the default implementation.

- [ ] **Step 3: Write ReadCoreServiceCollectionExtensions**

`AddAgentMemoryReadCore()` chains `AddAgentMemoryProjectionSecurity()` internally.

- [ ] **Step 4: Build and commit**

```bash
git add -A && git commit -m "feat: implement shared ReadCore (memory recall, context recall, source expand)"
```

---

### Task 7b: Create ReadCore.Tests and run immediately

- [ ] **Step 1: Create test project and add to solutions**

- [ ] **Step 2: Write and run tests**

Key: budget fail-closed; closed-world descriptor visibility; IsAuthoritative always false; SourceExpandCore zero writes; CompensationToken present when artifacts created. Note: ContextHandleIssuer tests belong in Projection.Tests (Task 6b), not here — ReadCore only consumes `IAgentMemoryContextHandleIssuer`.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "test: add ReadCore.Tests — budget, visibility, expand zero-write"
```

---

## Phase 3: Composable Handler Registration (MUST precede adapter/Di refactoring)

### Task 8: Add ICapabilityHandlerModule to Capability.Abstractions

**Files:**
- Create: `ICapabilityHandlerModule.cs`
- Modify: `CapabilityHandlerResolver.cs` (add internal CopyRegistrationsTo)
- Modify: `CapabilityHandlerResolverProvider.cs` (add public ApplyLegacyRegistrations)

- [ ] **Step 1: Write ICapabilityHandlerModule**

```csharp
namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityHandlerModule
{
    string Id { get; }
    void Apply(CapabilityHandlerResolver resolver);
}
```

- [ ] **Step 2: Add internal CopyRegistrationsTo to CapabilityHandlerResolver**

- [ ] **Step 3: Add public ApplyLegacyRegistrations to CapabilityHandlerResolverProvider**

- [ ] **Step 4: Build and commit**

```bash
git add -A && git commit -m "feat: add ICapabilityHandlerModule and composable registration infrastructure"
```

---

### Task 9: Modify HandlerInvokerSourceGenerator

**Files:**
- Modify: `src/Tooling/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/HandlerInvokerSourceGenerator.cs`
- Modify: `tests/Tooling/CrestCreates.CodeGenerator.Tests/SchemaCapabilityGenerator/HandlerInvokerSourceGeneratorTests.cs`

- [ ] **Step 1: Replace generated `Apply()` with `RegisterServices()` + `GeneratedCapabilityHandlerModule`**

The generator must emit:

```csharp
internal sealed class GeneratedCapabilityHandlerModule
    : ICapabilityHandlerModule
{
    private const string ProviderId = "<AssemblyName>";

    internal static GeneratedCapabilityHandlerModule Instance { get; } = new();

    private GeneratedCapabilityHandlerModule() { }

    public string Id => ProviderId;

    public void Apply(CapabilityHandlerResolver resolver)
        => CapabilityHandlerResolverProvider.ApplyDefinition(ProviderId, resolver);
}

internal static void RegisterServices(IServiceCollection services)
{
    services.TryAddScoped<HandlerA>();
    services.TryAddScoped<HandlerB>();
}
```

Note: `ProviderId` is a private const on the Module class itself, not referencing `GeneratedHandlerRegistry.ProviderId` (which is internal to the generated registry). The Module does NOT register itself in DI — callers are responsible for:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<ICapabilityHandlerModule>(
        GeneratedCapabilityHandlerModule.Instance));
GeneratedHandlerRegistry.RegisterServices(services);
```

- [ ] **Step 2: Replace old `Apply()` with compatibility shim**

```csharp
[Obsolete("Use module registration and RegisterServices(IServiceCollection).")]
internal static void Apply(IServiceCollection services)
{
    services.TryAddEnumerable(
        ServiceDescriptor.Singleton<ICapabilityHandlerModule>(
            GeneratedCapabilityHandlerModule.Instance));

    RegisterServices(services);
}
```

All `RemoveAll<CapabilityHandlerResolver>()` and `RemoveAll<ICapabilityHandlerResolver>()` calls are removed entirely.

- [ ] **Step 3: Update generator snapshot tests**

- [ ] **Step 4: Build and run generator tests**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests`
Expected: All pass

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: modify HandlerInvokerSourceGenerator to emit composable module registration"
```

---

### Task 10: Implement LegacyCapabilityHandlerModule and module-aware factory

**Files:**
- Create: `src/Runtime/Capability/CrestCreates.Capability/LegacyCapabilityHandlerModule.cs`
- Modify: `src/Runtime/Capability/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`

- [ ] **Step 1: Write LegacyCapabilityHandlerModule**

```csharp
internal sealed class LegacyCapabilityHandlerModule : ICapabilityHandlerModule
{
    internal static LegacyCapabilityHandlerModule Instance { get; } = new();
    private LegacyCapabilityHandlerModule() { }

    public string Id => "legacy-capability-pipeline";

    public void Apply(CapabilityHandlerResolver resolver)
        => CapabilityHandlerResolverProvider.ApplyLegacyRegistrations(resolver);
}
```

Only copies legacy `Register()` invokers — does NOT read or apply Generated Definitions.

- [ ] **Step 2: Update AddCapabilityPipeline() to use module-aware factory**

Both `CapabilityHandlerResolver` and `ICapabilityHandlerResolver` must point to the SAME composed instance:

```csharp
services.TryAddSingleton<CapabilityHandlerResolver>(sp =>
{
    var resolver = new CapabilityHandlerResolver();
    foreach (var module in sp.GetServices<ICapabilityHandlerModule>()
             .OrderBy(x => x.Id, StringComparer.Ordinal))
    {
        module.Apply(resolver);
    }
    return resolver;
});

services.TryAddSingleton<ICapabilityHandlerResolver>(
    sp => sp.GetRequiredService<CapabilityHandlerResolver>());

services.TryAddEnumerable(
    ServiceDescriptor.Singleton<ICapabilityHandlerModule>(
        LegacyCapabilityHandlerModule.Instance));
```

- [ ] **Step 3: Add test verifying concrete and interface resolver are same instance**

- [ ] **Step 4: Build and run capability tests**

Run: `dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests`
Expected: All pass

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add LegacyCapabilityHandlerModule and module-aware resolver factory"
```

---

## Phase 4: Adapters and Handler Refactoring

### Task 11: Implement old-interface adapters

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/Adapters/` — all 7 adapter files

**Complete adapter list:**

| Old Interface | Adapter | Implements | Wraps |
|---|---|---|---|
| `IAgentMemoryResourceHandleStore` | `AgentMemoryResourceHandleStoreAdapter` | OLD interface | NEW canonical store |
| `IAgentMemorySourceGrantStore` | `AgentMemorySourceGrantStoreAdapter` | OLD interface | NEW canonical store |
| `IAgentMemorySecurityArtifactBatchStore` | `AgentMemorySecurityArtifactBatchStoreAdapter` | OLD interface | NEW canonical store |
| `IAgentMemoryResourceHandleResolver` | `AgentMemoryResourceHandleResolverAdapter` | OLD interface | NEW canonical resolver |
| `IAgentMemorySourceGrantResolver` | `AgentMemorySourceGrantResolverAdapter` | OLD interface | NEW canonical resolver |
| `IAgentMemorySecurityArtifactCoordinator` | `AgentMemorySecurityArtifactCoordinatorAdapter` | OLD interface | NEW canonical coordinator |
| `IAgentMemoryToolAccessScopeProvider` | `LegacyAgentMemoryAccessScopeProviderAdapter` | **NEW** `IAgentMemoryAccessScopeProvider` + `IAgentMemoryAccessScopeProviderCapabilities` | **OLD** `IAgentMemoryToolAccessScopeProvider` |

**Legacy Scope Adapter direction** (implements NEW, wraps OLD):

```csharp
internal sealed class LegacyAgentMemoryAccessScopeProviderAdapter
    : IAgentMemoryAccessScopeProvider,
      IAgentMemoryAccessScopeProviderCapabilities
{
    private readonly IAgentMemoryToolAccessScopeProvider _legacy;

    public LegacyAgentMemoryAccessScopeProviderAdapter(
        IAgentMemoryToolAccessScopeProvider legacy)
    {
        _legacy = legacy;
    }

    public bool Supports(AgentMemoryCallerKind callerKind)
        => callerKind == AgentMemoryCallerKind.AgentTool;

    public async ValueTask<AgentMemoryAccessScope> ResolveAsync(
        AgentMemoryAccessPrincipal principal,
        CancellationToken ct = default)
    {
        // Convert principal → old principal, resolve, map ALL fields
    }
}
```

DI registration: `services.TryAddSingleton<IAgentMemoryAccessScopeProvider, LegacyAgentMemoryAccessScopeProviderAdapter>();`

**Revoke/Get filtering**: All Store adapters filter by CallerKind:
- `CallerKind == AgentTool` → convert and delegate
- `CallerKind == Mcp` → return null for Get/Resolve, no-op for Revoke

- [ ] **Step 1-7: Implement all 7 adapters**

- [ ] **Step 8: Build and commit**

```bash
git add -A && git commit -m "feat: add old-interface adapters (filtered views, AgentTool-only, conditional revoke, legacy scope adapter)"
```

---

### Task 12: Update AgentMemoryToolServiceCollectionExtensions

**Depends on**: Tasks 9-10 (Generator and module factory must exist first)

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/AgentMemoryToolServiceCollectionExtensions.cs`

- [ ] **Step 1: Refactor DI registration**

Replace direct old store/coordinator/resolver registrations with:
1. Call `AddAgentMemoryReadCore()` (which chains `AddAgentMemoryProjectionSecurity()`)
2. Register all 6 adapters (Store, GrantStore, BatchStore, HandleResolver, GrantResolver, Coordinator)
3. Register `LegacyAgentMemoryAccessScopeProviderAdapter` via `TryAddSingleton`
4. Register `GeneratedCapabilityHandlerModule.Instance` via `TryAddEnumerable`
5. Call `GeneratedHandlerRegistry.RegisterServices(services)` for handler DI registrations
6. Keep `IAgentMemoryHistoryResourceHandleIssuer` registration unchanged

- [ ] **Step 2: Build and run existing tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests`
Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.E2E.Tests`
Expected: All pass

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: refactor AddAgentMemoryTools() to use canonical store via adapters + module registration"
```

---

### Task 13: Update AgentMemoryToolDescriptorProviders

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/AgentMemoryToolDescriptorProviders.cs`

Explicit Provider split — remove shared read schema definitions. Keep only write-only schemas and all 7 Agent Tool Capability descriptors.

- [ ] **Step 1: Remove shared read schemas from provider**

- [ ] **Step 2: Build and run tests**

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: split schema ownership — shared read schemas to Projection, write-only to Tools"
```

---

### Task 14: Refactor BuildAgentMemoryPackHandler and ExpandAgentMemorySourceHandler

**Files:**
- Modify: `AgentMemoryToolHandlerBase.cs`, `BuildAgentMemoryPackHandler.cs`, `ExpandAgentMemorySourceHandler.cs`

- [ ] **Step 1: Add ToReadPrincipal() and ToArtifactOrigin() to AgentMemoryToolHandlerBase**

```csharp
private static string RequireIdentity(string? value, [CallerArgumentExpression(nameof(value))] string fieldName = "")
{
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException(
            $"Trusted identity field '{fieldName}' is required for AgentMemoryAccessPrincipal construction.");

    return value;
}

protected AgentMemoryAccessPrincipal ToReadPrincipal()
{
    var key = InvocationBinding.LogicalKey;
    return new AgentMemoryAccessPrincipal
    {
        TenantId = RequireIdentity(key.TenantId),
        UserId = RequireIdentity(key.UserId),
        CallerKind = AgentMemoryCallerKind.AgentTool,
        CallerId = RequireIdentity(key.AgentId),
        SecurityContextId = RequireIdentity(key.ExecutionId)
    };
}

protected AgentMemoryArtifactOrigin ToArtifactOrigin()
{
    return new AgentMemoryArtifactOrigin
    {
        Kind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
        BindingHash = ComputeAgentToolOriginBindingHash(),
        OperationId = RequireIdentity(InvocationBinding.LogicalKey.InvocationId)
    };
}
```

Note: Uses `RequireIdentity()` — never `?? string.Empty`. `InvocationBinding` has `LogicalKey` (type `AgentToolLogicalInvocationKey`) and `InvocationFingerprint`. Identity fields accessed through `LogicalKey`.

- [ ] **Step 2: Refactor BuildAgentMemoryPackHandler** — delegate to `IAgentMemoryReadCore.RecallAsync`, wrap with governance.

- [ ] **Step 3: Refactor ExpandAgentMemorySourceHandler** — delegate to `IAgentMemorySourceExpandCore.ExpandAsync`, wrap with governance.

- [ ] **Step 4: Run existing Agent Memory Tools tests**

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: refactor BuildPack and ExpandSource handlers to use shared ReadCore with governance wrapper"
```

---

## Phase 5: MCP Memory Tools

### Task 15: Create IMcpJsonContextContributor and McpToolSchemaClosureResolver

**Files:**
- Create: `IMcpJsonContextContributor.cs` in Mcp.Abstractions
- Create: `McpToolSchemaClosureResolver.cs` in Mcp
- Modify: `McpToolRuntimeSnapshotBuilder.cs`, `McpServiceCollectionExtensions.cs`
- Modify: `McpJsonSchemaProjector.cs` — add `referencedSchemas` parameter
- Modify: `McpToolSchemaParityValidator.cs` — add `referencedSchemas` parameter

- [ ] **Step 1: Write IMcpJsonContextContributor**

```csharp
namespace CrestCreates.Mcp.Abstractions;

public interface IMcpJsonContextContributor
{
    string Id { get; }
    int Order { get; }
    JsonSerializerContext Create(JsonSerializerOptions options);
    IReadOnlyCollection<Type> BindingRootTypes { get; }
}
```

Composition rules: ordinal-unique Id; stable sort by Order then Id; one owner per BindingRootTypes entry; all contexts source-generated; composition before MakeReadOnly(); input/output schemas resolved separately; circular reference detection.

- [ ] **Step 2: Add closure-aware overloads to IMcpJsonSchemaProjector (preserve existing root-only facade)**

```csharp
public interface IMcpJsonSchemaProjector
{
    // Existing root-only overloads — preserved for backward compatibility
    JsonElement ProjectInput(SchemaDescriptor? schema);
    JsonElement? ProjectOutput(SchemaDescriptor? schema);

    // Closure-aware overloads — new
    JsonElement ProjectInput(
        SchemaDescriptor? schema,
        IReadOnlyList<SchemaDescriptor> referencedSchemas);

    JsonElement? ProjectOutput(
        SchemaDescriptor? schema,
        IReadOnlyList<SchemaDescriptor> referencedSchemas);
}
```

Implementation delegates root-only to closure overload with `Array.Empty<SchemaDescriptor>()`:
```csharp
public JsonElement ProjectInput(SchemaDescriptor? schema)
    => ProjectInput(schema, Array.Empty<SchemaDescriptor>());

public JsonElement? ProjectOutput(SchemaDescriptor? schema)
    => ProjectOutput(schema, Array.Empty<SchemaDescriptor>());
```

- [ ] **Step 3: Add closure-aware overloads to McpToolSchemaParityValidator (preserve existing root-only facade)**

```csharp
// Existing root-only overloads — preserved for backward compatibility
public void ValidateInput(SchemaDescriptor schema, JsonTypeInfo typeInfo);
public void ValidateOutput(SchemaDescriptor schema, JsonTypeInfo typeInfo);

// Closure-aware overloads — new
public void ValidateInput(
    SchemaDescriptor schema,
    JsonTypeInfo typeInfo,
    IReadOnlyList<SchemaDescriptor> referencedSchemas)
{
    Validate(() =>
        Validator.ValidateInput(schema, typeInfo, referencedSchemas));
}

public void ValidateOutput(
    SchemaDescriptor schema,
    JsonTypeInfo typeInfo,
    IReadOnlyList<SchemaDescriptor> referencedSchemas)
{
    Validate(() =>
        Validator.ValidateOutput(schema, typeInfo, referencedSchemas));
}
```

Root-only overloads delegate to closure overload with `Array.Empty<SchemaDescriptor>()`.

- [ ] **Step 4: Implement McpToolSchemaClosureResolver**

Resolves closure for a given root Schema. Input and output schemas must each resolve their own closure independently — never reuse a single merged closure.

- [ ] **Step 5: Update McpToolRuntimeSnapshotBuilder**

Builder must:
1. Resolve input closure: `var inputClosure = closureResolver.Resolve(inputSchema);`
2. Resolve output closure: `var outputClosure = closureResolver.Resolve(outputSchema);`
3. Validate: `_parity.ValidateInput(inputSchema, inputTypeInfo, inputClosure);` / `_parity.ValidateOutput(outputSchema, outputTypeInfo, outputClosure);`
4. Project: `_projector.ProjectInput(inputSchema, inputClosure);` / `_projector.ProjectOutput(outputSchema, outputClosure);`
5. Inject `IEnumerable<IMcpJsonContextContributor>` into builder
6. Compose contributors: sort by Order then Id, validate unique Id, validate unique BindingRootTypes owner
7. Build resolver chain before `JsonSerializerOptions.MakeReadOnly()`

- [ ] **Step 6: Update McpServiceCollectionExtensions**

Register `IMcpJsonContextContributor` implementations, inject into builder.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: add IMcpJsonContextContributor, McpToolSchemaClosureResolver, upgrade Projector/Parity for closure"
```

---

### Task 15b: Add MCP JSON infrastructure tests to existing Mcp.Tests

**Files:**
- Modify: `tests/Integrations/CrestCreates.Mcp.Tests/` — add test files

- [ ] **Step 1: Add test files to existing project**

Add to `tests/Integrations/CrestCreates.Mcp.Tests/`:
- `McpJsonContextContributorCompositionTests.cs`
- `McpToolSchemaClosureResolverTests.cs`

- [ ] **Step 2: Write and run tests**

Key: Contributor ID duplicate → startup failure; Binding root duplicate owner → startup failure; contributor order stability; source-generated context resolves all roots; input/output separate closure; Schema circular reference failure; projector and parity validator receive same closure; resolver chain completes before MakeReadOnly().

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "test: add MCP JSON infrastructure tests — contributor composition, schema closure"
```

---

### Task 16: Create Mcp.Memory project and implement 4 MCP tools

**Files:**
- Create: `src/Integrations/CrestCreates.Mcp.Memory/` — all files
- Modify: Solution files

- [ ] **Step 1: Create .csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>CrestCreates.Mcp.Memory</RootNamespace>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Mcp.Abstractions\CrestCreates.Mcp.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Mcp\CrestCreates.Mcp.csproj" />
    <ProjectReference Include="..\..\Runtime\Agent\CrestCreates.Agent.Memory.ReadCore\CrestCreates.Agent.Memory.ReadCore.csproj" />
    <ProjectReference Include="..\..\Runtime\Agent\CrestCreates.Agent.Memory.Projection.Abstractions\CrestCreates.Agent.Memory.Projection.Abstractions.csproj" />
    <ProjectReference Include="..\..\Runtime\Capability\CrestCreates.Capability.Abstractions\CrestCreates.Capability.Abstractions.csproj" />
    <ProjectReference Include="..\..\Metadata\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\..\Metadata\CrestCreates.Metadata.Mcp.Abstractions\CrestCreates.Metadata.Mcp.Abstractions.csproj" />
    <ProjectReference Include="..\..\Metadata\CrestCreates.Metadata\CrestCreates.Metadata.csproj" />
    <ProjectReference Include="..\..\Metadata\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../../src/Tooling/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

Note: Direct reference to `Projection.Abstractions` (for Principal, Origin, BuildAgentMemoryPackInput, etc.) — does not rely solely on ReadCore's transitive reference. Direct reference to `Metadata` (for DescriptorProviderRegistry). No Version attributes on PackageReference.

- [ ] **Step 2: Implement McpMemoryArtifactOriginFactory**

Centralized MCP Origin/BindingHash construction — handlers must NOT construct Origin independently.

```csharp
internal sealed class McpMemoryArtifactOriginFactory
{
    /// <summary>
    /// Creates Principal from CapabilityExecutionContext trusted snapshot.
    /// All fields validated via RequireIdentity() — fail-closed on any missing field.
    /// </summary>
    public AgentMemoryAccessPrincipal CreatePrincipal(
        CapabilityExecutionContext context);

    /// <summary>
    /// Creates Origin for MCP Tool Invocation (memory_recall, ctx_recall, ctx_expand, memory_source_expand).
    /// Reads directly from CapabilityExecutionContext — no Descriptor object queries.
    /// BindingHash binds: TenantId, UserId, HostId, SessionId, InvocationId, RequestId,
    /// ToolDescriptorId, ToolDescriptorVersion, CapabilityId, CapabilityVersion.
    /// String fields validated via RequireIdentity(); version fields validated via RequirePositiveVersion().
    /// </summary>
    public AgentMemoryArtifactOrigin CreateInvocationOrigin(
        CapabilityExecutionContext context);

    /// <summary>
    /// Creates Origin for MCP Session ContextHandle issuance.
    /// Internal extension point for future same-assembly Session Hosting.
    /// Not host-facing — if future external Host needs this, extract to
    /// a public projection-neutral interface at that time.
    /// BindingHash binds: TenantId, UserId, HostId, SessionId, SessionOperationId.
    /// SessionOperationId is Host-generated per issuance, participates in BindingHash,
    /// allowing multiple ContextHandle issuances within the same session without Batch identity conflict.
    /// </summary>
    public AgentMemoryArtifactOrigin CreateSessionOperationOrigin(
        AgentMemoryAccessPrincipal principal,
        string sessionOperationId);
}
```

Principal construction:
- `TenantId` from `CapabilityExecutionContext.TenantId`
- `UserId` from `CapabilityExecutionContext.UserId`
- `CallerKind = Mcp`
- `CallerId = HostId` from `context.Items["HostId"]`
- `SecurityContextId = SessionId` from `context.Items["SessionId"]`
- All fields validated via `RequireIdentity()` — fail-closed on any missing field

- [ ] **Step 3-10: Implement all MCP Memory components**

Handler derives Principal from `CapabilityExecutionContext.Items` (HostId, SessionId, InvocationId). Fail-closed validation using `RequireIdentity()` for string fields and `RequirePositiveVersion()` for version fields. Handlers use `McpMemoryArtifactOriginFactory.CreateInvocationOrigin(context)` for Origin construction — never construct Origin independently. Invocation handlers use `CreateInvocationOrigin()`; MCP session setup uses `CreateSessionOperationOrigin()` before calling `IAgentMemoryContextHandleIssuer` (if this Phase does not implement MCP session hosting, `CreateSessionOperationOrigin()` remains as a host-facing helper). `McpMemoryStartupValidator` implements `IBootstrapValidator`. `AddMcpMemoryTools()` chains `AddAgentMemoryReadCore()`, registers module instance, calls `RegisterServices()`, registers startup validator.

- [ ] **Step 11: Build and commit**

```bash
git add -A && git commit -m "feat: implement MCP Memory tools (ctx_recall, ctx_expand, memory_recall, memory_source_expand)"
```

---

### Task 16b: Create Mcp.Memory.Tests and run immediately

- [ ] **Step 1: Create test project and add to solutions**

- [ ] **Step 2: Write and run tests**

Key: discovery (4 tools, all read-only); budget fail-closed; security (cross-tenant, expired, revoked, forged, foreign-host, foreign-session, scope-stale → unavailable); cross-call grant reuse; zero-write for expand; IsAuthoritative always false; Principal fail-closed; startup validator; Origin factory BindingHash tests:
- Same session, different InvocationId → different McpInvocation BindingHash
- Same InvocationId, different RequestId → different BindingHash
- ToolDescriptor Version change → different BindingHash
- Capability Version change → different BindingHash
- Same session, different SessionOperationId → different McpSessionOperation BindingHash
- SessionOperationId not in Hash → test must fail
- Invocation A creates Grant → Invocation B under same Principal can use it (Principal.SecurityContextId governs reuse; Origin.BindingHash governs idempotency/quota)

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "test: add Mcp.Memory.Tests — handler unit tests"
```

---

## Phase 6: E2E, Boundary, and AOT Verification

### Task 17: Create Mcp.Memory.E2E.Tests

- [ ] **Step 1: Create test project and add to solutions**
- [ ] **Step 2: Write and run E2E tests**
- [ ] **Step 3: Commit**

---

### Task 18: Add dependency boundary tests for Mcp.Memory

Enforces: `CrestCreates.Mcp.Memory` must not reference `IAgentMemoryStore`, `IAgentCompressedContextStore`, `IAgentMemoryRetriever`, `IAgentContextSourceExpander`.

- [ ] **Step 1: Add boundary rules**
- [ ] **Step 2: Run boundary tests**
- [ ] **Step 3: Commit**

---

### Task 19: Run full regression suite

- [ ] **Step 1: Run all Agent Memory Tools tests** (separate commands per project)
- [ ] **Step 2: Run all MCP tests** (separate commands per project)
- [ ] **Step 3: Run full solution build**
- [ ] **Step 4: Run full test suite**

---

### Task 20: AOT verification

- [ ] **Step 1: Add MCP Memory JSON context to AOT fixture**
- [ ] **Step 2: Update Agent.Memory.Tools.AotFixture and new Mcp.Memory AOT fixture to register TimeProvider.System explicitly**

Projection/ReadCore do not auto-register `TimeProvider`. AOT fixtures must register:
```csharp
services.AddSingleton(TimeProvider.System);
```

- [ ] **Step 3: Run AOT publish**
- [ ] **Step 4: Run published binary**
- [ ] **Step 5: Commit**

---

## Self-Review Checklist

1. **Spec coverage**: Every section of the APPROVED spec maps to at least one task.
2. **Placeholder scan**: No TBD, TODO, or "implement later" steps.
3. **Type consistency**: All types use APPROVED spec names. `using CrestCreates.Agent.Memory.Tools;` included wherever TypeForwarded types are referenced.
4. **Scope contract**: `AgentMemoryAccessScope` preserves ALL fields from existing type including `IReadOnlyList<DescriptorRef>`, `MaxRecallCharacters`, `MaxExpansionCharacters`.
5. **Expand contract**: `AgentMemorySourceExpandRequest` includes `MaximumCharacters`. `AgentMemorySourceExpandResult` uses `AgentMemoryToolCanonicalHashDto?`, no `SourceKind`.
6. **TypeForward**: Namespace `CrestCreates.Agent.Memory.Tools` preserved. `TypeForwards.cs` only in old assembly. Binary compatibility fixture included.
7. **Task ordering**: TypeForward (Task 3) before Security Interfaces (Task 4) and ReadCore Interfaces (Task 5). Generator/Module (Tasks 8-10) before Adapter/DI refactoring (Tasks 11-12).
8. **Generator**: Emits `RegisterServices()` + `GeneratedCapabilityHandlerModule` with own `ProviderId` const. Old `Apply()` is `[Obsolete]` shim without RemoveAll. Callers register module instance.
9. **Resolver identity**: Both `ICapabilityHandlerResolver` and `CapabilityHandlerResolver` point to same composed instance.
10. **CompensationToken**: Non-forgeable opaque `TokenId`. Batch Store uses `AgentMemoryAccessPreparedArtifact` (not old type).
11. **DescriptorRef**: Uses `CrestCreates.Metadata.Abstractions` directly, not through `Agent.Memory.Abstractions` transitive reference.
12. **RecallAgentContext DTOs**: In `Projection.Abstractions/Dto/` (protocol-neutral), not in `Mcp.Memory`.
13. **Legacy Scope Adapter**: Implements NEW interface, wraps OLD provider. No separate Capabilities Service.
14. **RequireIdentity**: Never `?? string.Empty`. Fail-closed on missing identity fields.
15. **Central package management**: No Version attributes on PackageReference.
16. **TimeProvider**: All stores and Coordinator inject `TimeProvider`, never `DateTimeOffset.UtcNow`.
17. **Per-operation quota**: Keys on `OriginBindingHash`, not `Principal.SecurityContextId`.
18. **Test-after-each-phase**: Tests run immediately (6b, 7b, 15b, 16b).
19. **Project references**: Correct relative paths (`../../../Core/` = 3 levels). Mcp.Memory includes CodeGenerator analyzer, `Projection.Abstractions`, `Metadata` direct references.
20. **Startup validation**: `McpMemoryStartupValidator` implements `IBootstrapValidator` — no `BuildServiceProvider` in DI extensions.