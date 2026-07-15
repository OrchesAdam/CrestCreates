# Phase 8e MCP Tool Projection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Project explicitly authored Capability descriptors into deterministic, NativeAOT-verified MCP 2025-06-18 tool contracts whose calls execute only through ICapabilityDispatcher and CapabilityPipeline.

**Architecture:** Add metadata-owned `CrestCreates.Metadata.Mcp.Abstractions` plus independent `CrestCreates.Mcp.Abstractions` and `CrestCreates.Mcp` projects under Integrations. Metadata governance references only the metadata-layer MCP Descriptor contract; no Metadata project references Integrations. A Source Generator emits descriptor providers and exact typed JSON binding registrations; startup resolves Capability/Schema versions, freezes JsonTypeInfo, validates schema parity, and publishes an Active-only immutable snapshot. Discovery and invocation consume that snapshot, apply Host exposure policy, and dispatch the captured CapabilityDescriptor.

**Tech Stack:** .NET 10, C# incremental Source Generators targeting netstandard2.0, System.Text.Json source generation, immutable/frozen collections, xUnit 2.9.3, FluentAssertions, Moq, NativeAOT.

## Global Constraints

- MCP protocol baseline is exactly `2025-06-18`; Tasks and `execution.taskSupport` are excluded.
- MCP runtime and generated output must not reference ASP.NET Core, DynamicApi, AppService, Agent Control Plane, or an official MCP SDK.
- Input, validation, and output on the MCP path must not use runtime reflection or reflection JSON fallback.
- Tool execution must call `ICapabilityDispatcher.DispatchAsync(capturedDescriptor, InvocationSource.Mcp, exactInput, ...)`; direct Handler invocation is forbidden.
- Only explicitly authored, Active, fully validated tools enter the runtime snapshot.
- Capability permissions, risk, tenant/user context, validation, idempotency, audit, rate limit, and events remain owned by CapabilityPipeline.
- Capability InputSchema and OutputSchema references must be Exact with positive versions; MCP snapshot publication rejects Pattern, ValidationRules, References, Compatible selection, ExpectedContractHash, and invalid/inapplicable constraint metadata without weakening the generic SchemaValidator's existing Pattern behavior.
- JSON Schema property ordering, ToolName lookup, duplicate detection, and required ordering use `StringComparer.Ordinal`.
- Issue #61 Generated CRUD JSON contracts remains separate and must not be claimed as fixed.
- No project under `src/Metadata` may reference a project under `src/Integrations`.
- Never directly delete files; move obsolete files to `99_RecycleBin/`.

---

## File Structure

### New production projects

- `src/Metadata/CrestCreates.Metadata.Mcp.Abstractions/` — `McpToolDescriptor`, annotation overrides, and metadata-governance contracts consumed by canonical hashing and topology.
- `src/Integrations/CrestCreates.Mcp.Abstractions/` — authoring attributes, protocol-neutral call/discovery/result contracts, binding registration contracts, and exposure contracts; references the metadata-layer MCP Descriptor contract.
- `src/Integrations/CrestCreates.Mcp/` — registry, validators, snapshot, JSON Schema projection, discovery, exposure, invocation, idempotency, result mapping, DI, and topology extraction.

### New test projects

- `tests/Integrations/CrestCreates.Mcp.Tests/` — runtime unit tests.
- `tests/Integrations/CrestCreates.Mcp.E2E.Tests/` — generator-backed in-process E2E tests.
- `tests/Integrations/CrestCreates.Mcp.AotFixture/` — executable NativeAOT fixture.
- `tests/Integrations/CrestCreates.Mcp.AotFixture.Tests/` — NativeAOT publish and native-binary execution tests.

### Existing projects modified

- `src/Metadata/CrestCreates.Metadata.Abstractions/` — DescriptorKind/name.
- `src/Metadata/CrestCreates.Metadata/` — MCP canonical hash profiles and a project reference only to `Metadata.Mcp.Abstractions`, never to Integrations.
- `src/Metadata/CrestCreates.Schema.Abstractions/` and `src/Metadata/CrestCreates.Schema/` — JsonElement validator overload and shared validation core.
- `src/Runtime/Capability/CrestCreates.Capability.Abstractions/` and `src/Runtime/Capability/CrestCreates.Capability/` — InputJson, structured issues, exact Schema version validation.
- `src/Tooling/CrestCreates.CodeGenerator/` — MCP generator plus canonical Schema type mapping.
- `tests/Tooling/CrestCreates.CodeGenerator.Tests/`, `tests/Metadata/Core/CrestCreates.Schema.Tests/`, `tests/Runtime/Capability/CrestCreates.Capability.Tests/`, and boundary tests — TDD coverage.
- `CrestCreates.slnx`, `solutions/CrestCreates.All.slnx`, `memory.md`, and MCP usage documentation — integration and closure.

---

### Task 1: Descriptor Kernel, Projects, and Metadata Integration

**Files:**
- Create: `src/Metadata/CrestCreates.Metadata.Mcp.Abstractions/CrestCreates.Metadata.Mcp.Abstractions.csproj`
- Create: `src/Metadata/CrestCreates.Metadata.Mcp.Abstractions/McpToolDescriptor.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Mcp.Abstractions/McpToolAnnotations.cs`
- Create: `src/Integrations/CrestCreates.Mcp.Abstractions/CrestCreates.Mcp.Abstractions.csproj`
- Create: `src/Integrations/CrestCreates.Mcp.Abstractions/McpToolAttributes.cs`
- Create: `src/Integrations/CrestCreates.Mcp.Abstractions/McpToolContracts.cs`
- Create: `src/Integrations/CrestCreates.Mcp/CrestCreates.Mcp.csproj`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolRegistry.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolDescriptorValidator.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolRelationshipExtractor.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKind.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKindNames.cs`
- Modify: `src/Metadata/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
- Create: `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/McpToolDescriptorCanonicalHashProfile.cs`
- Create: `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/McpToolCapabilityRefCanonicalHashProfile.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.Tests/CrestCreates.Mcp.Tests.csproj`
- Create: `tests/Integrations/CrestCreates.Mcp.Tests/McpToolDescriptorValidatorTests.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.Tests/McpToolRegistryTests.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.Tests/McpToolRelationshipExtractorTests.cs`
- Modify: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorStableHashCoverageTests.cs`
- Create: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorKindNamesTests.cs`
- Modify: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackageSerializerTests.cs`
- Modify: `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/McpToolDescriptorKindPolicyTests.cs`
- Modify: `CrestCreates.slnx`
- Modify: `solutions/CrestCreates.All.slnx`

**Interfaces:**
- Consumes: `IDescriptor`, `IVersionedDescriptor`, `McpCapabilityReference`, `RegistryBase<TDescriptor>`, canonical hash profile attributes.
- Produces: `McpToolDescriptor`, `McpToolSpecAttribute`, `McpToolAnnotations`, `IMcpToolRegistry`, DescriptorKind.McpTool, MCP101/MCP102/MCP116-MCP119 validation issues.

- [ ] **Step 1: Add failing descriptor and runtime-validator tests**

Create tests that construct handwritten descriptors and assert every selection rule:

```csharp
[Theory]
[InlineData(VersionSelectionMode.Exact, 0)]
[InlineData(VersionSelectionMode.Latest, 1)]
[InlineData(VersionSelectionMode.Compatible, 0)]
public void Validate_InvalidCapabilityReference_Fails(
    VersionSelectionMode mode,
    int version)
{
    var descriptor = McpToolTestData.ValidDescriptor() with
    {
        Capability = new McpCapabilityReference(
            "orders.get", version, mode)
    };

    var report = _validator.Validate([descriptor]);

    report.Issues.Should().Contain(i => i.Code == "MCP117");
}
```

Also test null AnnotationOverrides, blank Id/Name/Description/Capability.Id, invalid ToolName, non-positive descriptor version, non-null ExpectedContractHash, duplicate active ToolName using Ordinal comparison, and non-exact Capability Schema references.

Before creating production projects, add minimal project-reference boundary tests
for both new assemblies. Mcp.Abstractions must reject DynamicApi, ASP.NET,
AppService, Agent.ControlPlane, Platform, and every official/provider MCP SDK.
Mcp Runtime must additionally reject DynamicApi implementation, ASP.NET,
Agent.ControlPlane, and Platform. These guards run in Task 1 and remain active
through Tasks 2-6; Task 7 extends them with generated-source and trimming guards.

- [ ] **Step 2: Run tests and verify the projects/types do not exist yet**

Run: `dotnet test tests/Integrations/CrestCreates.Mcp.Tests --filter "FullyQualifiedName~McpToolDescriptorValidatorTests|FullyQualifiedName~McpToolRegistryTests"`

Expected: FAIL because the MCP projects and contracts are not defined.

- [ ] **Step 3: Create projects and implement the contracts**

Use these authoritative shapes:

```csharp
public sealed record McpToolAnnotationOverrides
{
    public bool? DestructiveHint { get; init; }
    public bool? IdempotentHint { get; init; }
    public bool? OpenWorldHint { get; init; }
}

public sealed class McpToolDescriptor : IDescriptor, IVersionedDescriptor
{
    public string Namespace => "mcp-tool";
    public DescriptorKind Kind => DescriptorKind.McpTool;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; } = 1;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }
    public required McpCapabilityReference Capability { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public McpToolAnnotationOverrides AnnotationOverrides { get; init; } = new();
}
```

Implement the validator as an `IRegistryValidator<McpToolDescriptor>` and keep all comparisons Ordinal. Registry build must retain all versions/states; Active ToolName uniqueness is a validator concern.

- [ ] **Step 4: Add DescriptorKind, topology, canonical hash profiles, and dependency guard**

Place Descriptor and stored annotation types in
`CrestCreates.Metadata.Mcp.Abstractions`. `CrestCreates.Metadata` references this
metadata sibling for its closed canonical hash dispatcher. Add a boundary test
that scans every project under `src/Metadata` and fails on any direct
`src/Integrations` ProjectReference. Do not add a Metadata reference to
`CrestCreates.Mcp.Abstractions`.

Add enum value `McpTool = 8`, `DescriptorKindNames.McpTool`, and the
`ToCanonicalString` switch arm atomically in the same change. Add tests proving
`ToCanonicalString(DescriptorKind.McpTool) == "McpTool"` while Agent-specific
`AgentDescriptorKindPolicyEvaluator.IsValidDescriptorKind(DescriptorKind.McpTool)`
remains false. The latter is an Agent Draft/Authoring/Control Plane allowlist,
not the global enum-definition check its historical name suggests.

Add `McpToolDescriptor` and `McpToolAnnotationOverrides` entries to
`DescriptorStableHashCoverageTests`, plus a strong `References/Capability`
relationship. The MCP-specific Capability-ref profile must write fields in this order:

```text
Id, Version, SelectionMode, ExpectedContractHash
```

The descriptor ContractHash fields are Id, Name, Version, State, SupersededById, ToolName, Capability, Description, and three annotation overrides. Title is DefinitionOnly.

DescriptorPackage serializes manifest/snapshot/hash entries rather than concrete
Descriptor payloads. Add a package build plus serializer round-trip test proving
an McpTool entry retains its Ref, Kind, ContractHash, and DefinitionHash. Do not
invent polymorphic McpToolDescriptor payload deserialization merely to test the
`required init` property; generated providers construct that Descriptor directly.

- [ ] **Step 5: Run focused tests**

Run: `dotnet test tests/Integrations/CrestCreates.Mcp.Tests && dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~CanonicalHash|FullyQualifiedName~DescriptorKindNames|FullyQualifiedName~DescriptorPackageSerializer" && dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests && dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~McpToolDescriptorKindPolicy"`

Expected: PASS; Exact and Latest refs hash differently, and refs differing only by ExpectedContractHash hash differently.

- [ ] **Step 6: Commit Task 1**

```bash
git add CrestCreates.slnx solutions/CrestCreates.All.slnx src/Integrations/CrestCreates.Mcp.Abstractions src/Integrations/CrestCreates.Mcp src/Metadata/CrestCreates.Metadata.Abstractions src/Metadata/CrestCreates.Metadata.Mcp.Abstractions src/Metadata/CrestCreates.Metadata tests/Integrations/CrestCreates.Mcp.Tests tests/Metadata/Core/CrestCreates.Metadata.Tests tests/Boundary/CrestCreates.DependencyBoundaries.Tests tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/McpToolDescriptorKindPolicyTests.cs
git commit -m "feat(mcp): add tool projection descriptor kernel"
```

### Task 2: Capability and Schema Validation Closure

**Files:**
- Modify: `src/Metadata/CrestCreates.Schema.Abstractions/ISchemaValidator.cs`
- Modify: `src/Metadata/CrestCreates.Schema.Abstractions/SchemaValidationErrorCodes.cs`
- Modify: `src/Metadata/CrestCreates.Schema/SchemaValidator.cs`
- Modify: `src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs`
- Modify: `src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityExecutionResult.cs`
- Create: `src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityExecutionIssue.cs`
- Modify: `src/Runtime/Capability/CrestCreates.Capability/Middleware/ValidationMiddleware.cs`
- Modify: `tests/Metadata/Core/CrestCreates.Schema.Tests/SchemaValidatorTests.cs`
- Modify: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorStableHashBuilderTests.cs`
- Create: `tests/Runtime/Capability/CrestCreates.Capability.Tests/ValidationMiddlewareJsonInputTests.cs`
- Modify: `tests/Tooling/CrestCreates.CodeGenerator.Tests/SchemaCapabilityGenerator/SchemaCapabilityFormRemovalTests.cs`

**Interfaces:**
- Consumes: `SchemaDescriptor`, `SchemaFieldDescriptor`, `CapabilityExecutionContext`.
- Produces: `ISchemaValidator.Validate(SchemaDescriptor, JsonElement)`, `CapabilityExecutionContext.InputJson`, `CapabilityExecutionResult.Issues`, exact InputSchema version resolution, and regression coverage preserving existing Schema generator type tokens and hashes.

- [ ] **Step 1: Write failing JsonElement validator tests**

Add tests for object root, duplicate fields, integer/number distinction, Int32/Int64 overflow, collections, null elements, strict date/date-time/guid lexical forms, unknown types, and all existing string/numeric constraints. Preserve the legacy Pattern test for non-MCP callers; Pattern rejection belongs to McpTool startup validation, not the generic SchemaValidator.

```csharp
[Fact]
public void Validate_CollectionElementTypeMismatch_Fails()
{
    var schema = SchemaWith(new SchemaFieldDescriptor
    {
        Name = "ids",
        FieldType = "IList<Guid>",
        IsCollection = true,
        CollectionElementType = "guid"
    });
    using var json = JsonDocument.Parse("{\"ids\":[\"not-a-guid\"]}");

    var result = _validator.Validate(schema, json.RootElement);

    result.Errors.Should().Contain(e => e.ErrorCode == SchemaValidationErrorCodes.TypeMismatch);
}
```

- [ ] **Step 2: Write failing Capability middleware tests**

Assert that InputJson is preferred, the object fallback is not serialized on the MCP path, InputSchema is resolved by exact version, and validation issues contain only Code/FieldPath.

- [ ] **Step 3: Run focused tests to verify failure**

Run: `dotnet test tests/Metadata/Core/CrestCreates.Schema.Tests --filter "FullyQualifiedName~SchemaValidatorTests" && dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests --filter "FullyQualifiedName~ValidationMiddlewareJsonInputTests"`

Expected: FAIL because the overload, InputJson, and Issues do not exist.

- [ ] **Step 4: Implement one shared JsonElement validation core**

Add the interface overload and refactor the legacy object overload to serialize for compatibility, parse, then delegate to the same core:

```csharp
public interface ISchemaValidator
{
    SchemaValidationResult Validate(SchemaDescriptor schema, object? payload);
    SchemaValidationResult Validate(SchemaDescriptor schema, JsonElement payload);
}
```

Use `EnumerateObject()` plus an Ordinal HashSet for duplicate detection. Validate exact lexical forms: `yyyy-MM-dd`, RFC3339 with Z/offset, and case-insensitive hexadecimal Guid D form via `Guid.TryParseExact(value, "D", out _)`. Recognize only the Spec's closed token table, including existing generated `Guid`/`DateTime`/`DateTimeOffset`/`DateOnly` tokens and their listed lowercase format tokens; do not apply general case folding or accept unlisted aliases. For collections, ignore FieldType as shape authority and validate CollectionElementType for every non-null element. Preserve the shared validator's existing .NET Regex Pattern behavior.

- [ ] **Step 5: Implement Capability context/result changes**

```csharp
public JsonElement? InputJson { get; set; }

public sealed record CapabilityExecutionIssue(string Code, string? FieldPath);

public static CapabilityExecutionResult Failure(
    string errorCode,
    string errorMessage,
    TimeSpan duration,
    IReadOnlyList<CapabilityExecutionIssue>? issues = null)
```

ValidationMiddleware must use `GetByVersion(inputSchema.Id, inputSchema.Version)`
and implement the precedence literally:

```csharp
var validationResult = context.InputJson.HasValue
    ? _schemaValidator.Validate(schema, context.InputJson.Value)
    : _schemaValidator.Validate(schema, context.Input);
```

When both are populated, InputJson wins. A test payload whose object-path
serialization throws must still validate successfully from InputJson. Map
SchemaValidationError to issues without copying raw Message.

- [ ] **Step 6: Freeze existing generated Schema type identity**

Add generator regression tests proving SchemaCapabilitySourceGenerator continues
to emit its established display tokens:

```text
System.Guid → Guid
System.DateTime → DateTime
System.DateTimeOffset → DateTimeOffset
System.DateOnly → DateOnly
IList<Guid> → FieldType IList<Guid>, CollectionElementType Guid
```

Do not modify `SchemaCapabilitySourceGenerator`, `SchemaFieldDescriptor`, or the
Schema canonical hash profiles. Add golden ContractHash and DefinitionHash
regressions for representative required and optional fields using the established
tokens, so any accidental 8e rewrite fails visibly.

- [ ] **Step 7: Run tests and commit**

Run: `dotnet test tests/Metadata/Core/CrestCreates.Schema.Tests && dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests && dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~SchemaCapability"`

Expected: PASS.

```bash
git add src/Metadata/CrestCreates.Schema.Abstractions src/Metadata/CrestCreates.Schema src/Runtime/Capability tests/Metadata/Core/CrestCreates.Schema.Tests tests/Runtime/Capability/CrestCreates.Capability.Tests tests/Tooling/CrestCreates.CodeGenerator.Tests tests/Metadata/Core/CrestCreates.Metadata.Tests
git commit -m "refactor(capability): validate canonical JSON inputs"
```

### Task 3: MCP Source Generator and Binding Registration

**Files:**
- Create: `src/Tooling/CrestCreates.CodeGenerator/McpToolGenerator/McpToolGenerator.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/McpToolGenerator/McpToolModels.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/McpToolGenerator/McpToolNormalizer.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/McpToolGenerator/McpToolDiagnostics.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/McpToolGenerator/McpToolProviderEmitter.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/McpToolGenerator/McpToolBindingEmitter.cs`
- Create: `src/Integrations/CrestCreates.Mcp.Abstractions/McpToolBindingContract.cs`
- Create: `src/Integrations/CrestCreates.Mcp.Abstractions/McpToolBindingRegistry.cs`
- Create: `tests/Tooling/CrestCreates.CodeGenerator.Tests/McpToolGenerator/McpToolGeneratorTests.cs`
- Create: `tests/Tooling/CrestCreates.CodeGenerator.Tests/McpToolGenerator/McpToolDiagnosticTests.cs`
- Modify: `tests/Tooling/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj`

**Interfaces:**
- Consumes: Task 1 attributes/descriptors and `DescriptorProviderRegistry`.
- Produces: generated descriptor providers, `McpToolBindingContract`, module-initializer registrations, MCP001-MCP012.

- [ ] **Step 1: Write generator happy-path snapshot tests**

Compile a legal static partial container with typed input/output and assert two generated files contain Provider registration and exact JsonTypeInfo-aware binding delegates. Add no-input and void-output cases.

Freeze `McpToolBindingContract` as a sealed class with reference identity. Tests
must not compare contract instances for value equality; registry behavior is
keyed only by DescriptorId and DescriptorVersion, followed by explicit startup
identity/type validation.

- [ ] **Step 2: Write diagnostic tests**

Cover invalid container/spec declarations, duplicate ToolName/DescriptorId, invalid CrestCreates ToolName regex, blank description, non-positive DescriptorVersion, negative CapabilityVersion, bad enum values, interface/abstract/open-generic/dynamic-dictionary roots, and whole-container fail-closed output.

- [ ] **Step 3: Run tests and verify failure**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~McpTool"`

Expected: FAIL because McpToolGenerator is absent.

- [ ] **Step 4: Implement incremental discovery and normalization**

Use `ForAttributeWithMetadataName` for `CrestCreates.Mcp.McpToolSpecsAttribute`. Validate that the container is non-generic static partial and every spec is a direct non-generic nested class. Normalize CapabilityVersion exactly:

```csharp
var selection = capabilityVersion == 0
    ? VersionSelectionMode.Latest
    : VersionSelectionMode.Exact;
```

Never emit a provider or binding file when any Error diagnostic exists in the container.

- [ ] **Step 5: Implement emitters**

Provider output registers `IDescriptorProvider<McpToolDescriptor>`. Implement
`McpToolBindingContract` as a sealed class, not a record; its delegates and the
contract itself use reference identity. Binding output registers and looks up by
DescriptorId/DescriptorVersion and accepts startup-supplied JsonTypeInfo. For
output use a strict runtime type test:

```csharp
if (output is null || output.GetType() != typeof(TOutput))
    throw new InvalidOperationException("MCP output contract mismatch.");
```

Generated code must not mention concrete CLR type names in public exception messages.

- [ ] **Step 6: Run generator and forbidden-symbol tests**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~McpTool"`

Expected: PASS and generated-source assertions find no DynamicApi, ASP.NET, Handler, Dictionary fallback, or reflection serializer APIs.

- [ ] **Step 7: Commit Task 3**

```bash
git add src/Tooling/CrestCreates.CodeGenerator/McpToolGenerator src/Integrations/CrestCreates.Mcp.Abstractions tests/Tooling/CrestCreates.CodeGenerator.Tests
git commit -m "feat(mcp): generate tool descriptors and typed bindings"
```

### Task 4: JSON Schema Projection and Immutable Runtime Snapshot

**Files:**
- Create: `src/Integrations/CrestCreates.Mcp/McpJsonOptions.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolJsonContractRegistry.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolJsonContractValidator.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpJsonSchemaProjector.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolSchemaParityValidator.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolRuntimeBinding.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolRuntimeEntry.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolRuntimeSnapshot.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolRuntimeSnapshotBuilder.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.Tests/McpJsonSchemaProjectorTests.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.Tests/McpToolSchemaParityValidatorTests.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.Tests/McpToolRuntimeSnapshotBuilderTests.cs`

**Interfaces:**
- Consumes: Task 1 registry, Task 2 Schema validator rules, Task 3 binding registry, application JsonSerializerContext.
- Produces: `McpToolRuntimeSnapshot`, Active-only Ordinal FrozenDictionary, frozen runtime bindings and contract hashes, MCP103-MCP115 and MCP120-MCP121.

- [ ] **Step 1: Write byte-exact JSON Schema tests**

Assert exact JSON for empty input, all four required/nullable combinations, nullable arrays, strict keyword ordering, Ordinal property ordering, Int32/Int64 bounds, primitive arrays, formats, and additionalProperties=false. Prove inherent Int64 bounds are emitted as exact integer literals rather than doubles. Explicit integer bounds must be finite, integral, checked-convertible to decimal, and inside the CLR range; reject the rounded double immediately above Int64.MaxValue instead of clamping it. Prove `Guid`/`guid`, `DateTime`/`DateTimeOffset`/`datetime`, and `DateOnly`/`date` pairs project identically while `GUID`, `System.Guid`, and `uuid` fail closed. Freeze both upper- and lowercase UUID D values as valid and brace/compact values as invalid. Add MCP startup failures for Pattern, ValidationRules, References, duplicate fields, bad scalar/element vocabulary, negative or contradictory ranges, non-finite numeric bounds, and constraints applied to an inapplicable type (MCP121).

- [ ] **Step 2: Write directional parity tests**

Use a source-generated test JsonSerializerContext and assert input checks set-side metadata while output checks get-side metadata. Include missing getter/setter, unconditional ignore, JsonTypeInfo.Kind not Object, and different JSON names.

- [ ] **Step 3: Write snapshot tests**

Assert Latest Capability resolves once, Schema refs require Exact positive versions, only Active runtime-ready tools enter the FrozenDictionary, all JsonTypeInfo is cached, resolver entries must be JsonSerializerContext, options become read-only, and Tool/Capability/InputSchema/OutputSchema hashes are captured. Also prove Superseded/Deprecated descriptors still receive structural/hash validation but do not require current bindings, JsonTypeInfo, schema parity, or discovery contracts and therefore do not block an otherwise valid Active snapshot.

- [ ] **Step 4: Run tests and verify failure**

Run: `dotnet test tests/Integrations/CrestCreates.Mcp.Tests --filter "FullyQualifiedName~McpJsonSchema|FullyQualifiedName~McpToolSchemaParity|FullyQualifiedName~McpToolRuntimeSnapshot"`

Expected: FAIL because projector, parity validator, and snapshot builder are absent.

- [ ] **Step 5: Implement canonical projector and parity validator**

Write JSON with Utf8JsonWriter in the exact order from the spec. Implement one explicit token switch for the closed legacy-generated and handwritten token table; never rewrite the captured Schema Descriptor. Reject unlisted spellings and non-empty Pattern in MCP snapshot validation rather than emitting .NET Regex; do not change generic SchemaValidator Pattern semantics. Validate constraint applicability and finite/ordered ranges before projection. Write inherent Int32/Int64 bounds with integer writer overloads; checked-convert explicit integral double bounds to decimal for comparison and reject unrepresentable/out-of-range values. Use `JsonPropertyInfo.IsSetNullable` for input and `IsGetNullable` for output; require setter/constructor binding for input and getter for output.

- [ ] **Step 6: Implement frozen JSON configuration and snapshot**

Copy options, accept only JsonSerializerContext resolver entries, resolve all JsonTypeInfo, call `MakeReadOnly()`, then construct `FrozenDictionary.ToFrozenDictionary(StringComparer.Ordinal)`. Never resolve JsonTypeInfo from IServiceProvider during invocation.

- [ ] **Step 7: Run tests and commit**

Run: `dotnet test tests/Integrations/CrestCreates.Mcp.Tests`

Expected: PASS.

```bash
git add src/Integrations/CrestCreates.Mcp tests/Integrations/CrestCreates.Mcp.Tests
git commit -m "feat(mcp): build canonical runtime tool snapshot"
```

### Task 5: Discovery and Host Exposure Policy

**Files:**
- Create: `src/Integrations/CrestCreates.Mcp.Abstractions/McpToolExposureContracts.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolDiscoveryService.cs`
- Create: `src/Integrations/CrestCreates.Mcp/DefaultMcpToolExposurePolicy.cs`
- Create: `src/Integrations/CrestCreates.Mcp.Abstractions/McpToolProtocolException.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.Tests/McpToolDiscoveryServiceTests.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.Tests/McpToolExposurePolicyTests.cs`

**Interfaces:**
- Consumes: Task 4 runtime snapshot.
- Produces: `IMcpToolDiscoveryService`, `IMcpToolExposurePolicy`, Host/discovery contexts, UnknownTool versus InternalServer classifications.

`McpToolProtocolException` and `McpToolProtocolFailureKind` live in Abstractions. Its constructor is protected so Runtime can derive internal concrete exceptions; adapters only catch and classify the public base.

- [ ] **Step 1: Write discovery and exposure tests**

Test Ordinal ordering, different HostId/Profile visibility, default allow, denied Tool omission, exception omission plus internal diagnostics, and no permission/risk fields in public discovery contracts.

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/Integrations/CrestCreates.Mcp.Tests --filter "FullyQualifiedName~McpToolDiscovery|FullyQualifiedName~McpToolExposure"`

Expected: FAIL because discovery and policy services are absent.

- [ ] **Step 3: Implement contracts and discovery**

Use the exact signatures from the approved spec. Validate non-empty HostId/EnvironmentName before policy execution. Evaluate policy per snapshot entry, omit denied or faulted entries, log fault code MCP_TOOL_EXPOSURE_POLICY_FAILURE, and return Ordinal-sorted contracts.

- [ ] **Step 4: Implement stable protocol exception classification**

Add `McpToolProtocolFailureKind.UnknownTool`, `InvalidRequest`, and `InternalServer`; base exceptions carry FailureKind and InternalCode. Denied and nonexistent use UnknownTool. Policy exceptions use InternalServer.

- [ ] **Step 5: Run tests and commit**

Run: `dotnet test tests/Integrations/CrestCreates.Mcp.Tests --filter "FullyQualifiedName~McpToolDiscovery|FullyQualifiedName~McpToolExposure"`

Expected: PASS.

```bash
git add src/Integrations/CrestCreates.Mcp.Abstractions src/Integrations/CrestCreates.Mcp tests/Integrations/CrestCreates.Mcp.Tests
git commit -m "feat(mcp): add host-filtered tool discovery"
```

### Task 6: Invocation, Idempotency, Output Validation, and Safe Results

**Files:**
- Create: `src/Integrations/CrestCreates.Mcp.Abstractions/McpToolInvocationContracts.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpCapabilityContextItemNames.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolInvoker.cs`
- Create: `src/Integrations/CrestCreates.Mcp/DefaultMcpIdempotencyKeyBuilder.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolResultMapper.cs`
- Create: `src/Integrations/CrestCreates.Mcp/McpToolContractViolationException.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.Tests/McpToolInvokerTests.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.Tests/DefaultMcpIdempotencyKeyBuilderTests.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.Tests/McpToolResultMapperTests.cs`

**Interfaces:**
- Consumes: Task 2 InputJson/Issues, Task 4 snapshot/runtime binding, Task 5 exposure policy, ICapabilityDispatcher.
- Produces: `IMcpToolInvoker`, canonical idempotency builder, output contract enforcement, safe MCP Tool outcomes.

`McpToolCallContext`, `McpToolHostContext`, `McpToolInvocationOutcome`, `McpToolProtocolFailureKind`, and `IMcpToolInvoker` remain in Abstractions. `McpToolRuntimeEntry`, `McpToolRuntimeBinding`, `McpToolRuntimeSnapshot`, `IMcpIdempotencyKeyBuilder`, and `DefaultMcpIdempotencyKeyBuilder` live in Runtime; no duplicate idempotency DTO crosses the assembly boundary.

- [ ] **Step 1: Write idempotency tests**

Assert identical logical redelivery produces the same `mcp:v1:` key; colon-containing Host/Descriptor/Invocation tuples do not collide; changes to Tool, Capability, or either Schema ContractHash change the key; output is Base64Url SHA-256 without padding.

- [ ] **Step 2: Write invocation tests**

Cover absent arguments normalization, empty no-input arguments, non-object root protocol error, unknown/duplicate property Tool errors, denied versus faulted policy classification, exact typed input, InputJson, `InvocationSource.Mcp`, ambient TenantId/UserId, constant context item keys, RequestId→CausationId, and no caller-provided idempotency key.

- [ ] **Step 3: Write output/result tests**

Cover void success, typed success with StructuredContent and JSON TextContent, unexpected output, missing output, base TOutput with derived instance, invalid serialized OutputSchema value, safe validation issues, and generic authorization/rate-limit/timeout/business messages.

- [ ] **Step 4: Run tests and verify failure**

Run: `dotnet test tests/Integrations/CrestCreates.Mcp.Tests --filter "FullyQualifiedName~McpToolInvoker|FullyQualifiedName~McpIdempotency|FullyQualifiedName~McpToolResultMapper"`

Expected: FAIL because invocation services are absent.

- [ ] **Step 5: Implement canonical idempotency and invocation flow**

Canonical payload field order is shapeVersion, hostId, toolContractHash, capabilityContractHash, nullable input/output Schema hashes, invocationId. Hash UTF-8 canonical JSON with SHA-256 and encode Base64Url. Invocation must set InputJson, then call only:

```csharp
await dispatcher.DispatchAsync(
    entry.Capability,
    InvocationSource.Mcp,
    typedInput,
    ctx => ConfigureContext(ctx, entry, callContext, normalizedArguments),
    cancellationToken);
```

- [ ] **Step 6: Implement strict output and result mapping**

Require `output.GetType() == binding.OutputType`, serialize with cached JsonTypeInfo, validate the resulting JsonElement with the captured OutputSchema, and throw McpToolContractViolationException with stable internal codes. Never place errors in StructuredContent or expose raw Capability ErrorMessage.

- [ ] **Step 7: Run tests and commit**

Run: `dotnet test tests/Integrations/CrestCreates.Mcp.Tests`

Expected: PASS.

```bash
git add src/Integrations/CrestCreates.Mcp.Abstractions src/Integrations/CrestCreates.Mcp tests/Integrations/CrestCreates.Mcp.Tests
git commit -m "feat(mcp): invoke tools through capability pipeline"
```

### Task 7: DI, Boundary Guards, Generator-Backed E2E, and NativeAOT Closure

**Files:**
- Create: `src/Integrations/CrestCreates.Mcp/McpServiceCollectionExtensions.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.E2E.Tests/CrestCreates.Mcp.E2E.Tests.csproj`
- Create: `tests/Integrations/CrestCreates.Mcp.E2E.Tests/McpToolProjectionE2ETests.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.E2E.Tests/TestMcpContracts.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.AotFixture/CrestCreates.Mcp.AotFixture.csproj`
- Create: `tests/Integrations/CrestCreates.Mcp.AotFixture/Program.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.AotFixture/McpFixtureContracts.cs`
- Create: `tests/Integrations/CrestCreates.Mcp.AotFixture.Tests/CrestCreates.Mcp.AotFixture.Tests.csproj`
- Create: `tests/Integrations/CrestCreates.Mcp.AotFixture.Tests/McpAotFixtureTests.cs`
- Modify: `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/McpToolDescriptorKindPolicyTests.cs`
- Modify: `CrestCreates.slnx`
- Modify: `solutions/CrestCreates.All.slnx`

**Interfaces:**
- Consumes: Tasks 1-6 complete runtime and generator.
- Produces: `AddCrestMcpToolProjection()`, startup fail-closed integration, architectural guards, end-to-end proof, NativeAOT proof.

- [ ] **Step 1: Write DI/startup tests**

Assert AddCrestMcpToolProjection registers registry, snapshot builder, default exposure policy, discovery, invoker, idempotency builder, Schema validators, and relationship extractor once. Snapshot build must fail before publication on any startup issue.

- [ ] **Step 2: Write boundary tests**

Scan MCP project references and generated source to reject DynamicApi, ASP.NET, AppService, Agent.ControlPlane, official MCP SDK, direct Handler calls, Dictionary fallback, reflection serializer APIs, and `Results.Json(object)`. Scan every project under `src/Metadata` and reject direct references into `src/Integrations`; this reinforces the Task 1 guard after all solution wiring is present. Assert Agent Draft/Authoring/Control Plane supported-kind allowlists exclude DescriptorKind.McpTool.

- [ ] **Step 3: Build generator-backed E2E host and tests**

Define Query+input/output, Command+input/output, no-input Query, and void Command through `[McpToolSpecs]`. Register an application JsonSerializerContext in Metadata mode. Test authorization denial, validation failure, two Host profiles, Latest Capability capture, high-risk policy input, output contract violations, and deterministic discovery without starting an MCP transport.

- [ ] **Step 4: Run E2E and boundary tests**

Run: `dotnet test tests/Integrations/CrestCreates.Mcp.E2E.Tests && dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests && dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~McpToolDescriptorKindPolicyTests"`

Expected: PASS.

- [ ] **Step 5: Create NativeAOT fixture**

Use a non-generated application DTO that is visible to STJ, register it in `McpFixtureJsonContext`, invoke discovery and a typed MCP call in Program, and exit non-zero unless input binding, InputJson validation, Pipeline execution, output validation, and StructuredContent all succeed. Configure warnings as errors for IL2026, IL2070, IL2072, IL2075, IL3050, and SYSLIB1034.

- [ ] **Step 6: Publish and execute the fixture from a test**

Run from the test with a temporary output directory:

```bash
dotnet publish tests/Integrations/CrestCreates.Mcp.AotFixture/CrestCreates.Mcp.AotFixture.csproj -c Release -r linux-x64 --self-contained true -p:CrestCreatesPublishMode=aot
```

Expected: NativeAOT compilation and native linking succeed with no MCP-path IL2026/IL3050 warning, and the native executable exits 0 with `MCP_NATIVEAOT_OK`.

- [ ] **Step 7: Run the affected suite and commit**

Run: `dotnet test tests/Integrations/CrestCreates.Mcp.Tests && dotnet test tests/Integrations/CrestCreates.Mcp.E2E.Tests && dotnet test tests/Integrations/CrestCreates.Mcp.AotFixture.Tests && dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests`

Expected: PASS.

```bash
git add CrestCreates.slnx solutions/CrestCreates.All.slnx src/Integrations/CrestCreates.Mcp tests/Integrations tests/Boundary tests/Runtime/Agent
git commit -m "test(mcp): close projection e2e and trimming paths"
```

### Task 8: Documentation, Memory, and Full Verification

**Files:**
- Create: `docs/Feature/mcp-tool-projection.md`
- Modify: `memory.md`
- Modify: `docs/superpowers/specs/2026-07-15-phase-8e-mcp-tool-projection-design.md` only if implementation reveals a factual discrepancy; preserve Approved decisions.

**Interfaces:**
- Consumes: completed Phase 8e implementation.
- Produces: user-facing authoring/configuration guide, architecture status record, final verification evidence.

- [ ] **Step 1: Write the usage guide**

Document the legal container/spec declaration, application-owned JsonSerializerContext Metadata mode, Host context creation by trusted adapters, discovery/invocation APIs, exposure policy, supported Schema vocabulary, Pattern/References/ValidationRules limitations, Exact/Latest rules, and explicit separation from issue #61 and future Server hosting.

- [ ] **Step 2: Update memory.md**

Add Phase 8e status, core chain, project paths, diagnostics ranges, supported schema subset, canonical idempotency shape, test counts from actual output, and remaining non-goals. Do not claim Server hosting or Generated CRUD trimming closure.

- [ ] **Step 3: Run focused verification**

Run:

```bash
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~McpTool|FullyQualifiedName~SchemaCapability"
dotnet test tests/Metadata/Core/CrestCreates.Schema.Tests
dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests
dotnet test tests/Integrations/CrestCreates.Mcp.Tests
dotnet test tests/Integrations/CrestCreates.Mcp.E2E.Tests
dotnet test tests/Integrations/CrestCreates.Mcp.AotFixture.Tests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

Expected: every command exits 0.

- [ ] **Step 4: Run solution verification**

Run: `dotnet build && dotnet test`

Expected: both commands exit 0 with no build errors or failing tests.

- [ ] **Step 5: Inspect the final diff for forbidden paths**

Run:

```bash
rg -n "DynamicApi|HttpContext|Results\.Json|Dictionary<string, object|GetProperties\(|DefaultJsonTypeInfoResolver|ModelContextProtocol" src/Integrations/CrestCreates.Mcp* src/Tooling/CrestCreates.CodeGenerator/McpToolGenerator
```

Expected: only boundary-test strings or explanatory diagnostics appear; no production dependency or fallback path appears.

- [ ] **Step 6: Commit closure documentation**

```bash
git add docs/Feature/mcp-tool-projection.md memory.md docs/superpowers/specs/2026-07-15-phase-8e-mcp-tool-projection-design.md
git commit -m "docs(mcp): document tool projection mainline"
```

---

## Implementation Review Gates

After each task:

1. run the exact focused tests listed in that task;
2. inspect `git diff --check`;
3. verify only task-scoped files changed;
4. request a spec-conformance review before starting the next task;
5. preserve user changes and do not reset unrelated dirty files.

Task 8 completion requires the verification-before-completion skill before any claim that Phase 8e is complete.

### Task 9: Architecture Review Closure

- [x] Replace the Runtime-closed generic reference with Metadata-owned `McpCapabilityReference`, and forbid `Metadata.Mcp.Abstractions` references to Runtime or Integrations without moving the established public `CapabilityDescriptor` assembly.
- [x] Add failing Schema tests for unknown JSON properties, then enforce the projected `additionalProperties:false` contract with Ordinal comparison.
- [x] Add failing MCP parity tests for scalar/collection/element mismatches and extra directional JSON properties, then implement bidirectional direction-aware parity from source-generated `JsonTypeInfo`.
- [x] Centralize the supported Schema scalar-token vocabulary so the shared validator, MCP projector, and parity validator cannot drift, including `DateTimeOffset`.
- [x] Add a failing Host startup test, then idempotently build Schema → Capability → MCP Tool registries and the immutable snapshot through the hosting startup lifecycle.
- [x] Add a failing discovery policy-exception test, then fail the complete listing with `MCP_TOOL_EXPOSURE_POLICY_FAILURE` rather than returning a partial list.
- [x] Add a failing multi-context source-generated resolver-chain test, then accept the framework composite resolver while rejecting reflection resolvers.
- [x] Run focused Schema, Metadata, MCP, E2E, generator, boundary, and NativeAOT suites; build `CrestCreates.slnx`; update the Spec, feature guide, solutions, and `memory.md`.
