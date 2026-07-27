# Control Plane JSON Contract Root Generation Design

- **Date**: 2026-07-24
- **Status**: APPROVED
- **Approved for implementation**: 2026-07-24
- **Issue**: [#58 — tech-debt: Auto-generate JsonSerializerContext for ControlPlane.Abstractions (P1-6)](https://github.com/OrchesAdam/CrestCreates/issues/58)
- **Design supplement**: [Issue comment — Pre-CoreCompile JSON Contract Root Generation](https://github.com/OrchesAdam/CrestCreates/issues/58#issuecomment-5066700070)
- **Depends on**: Phase 7c Tool DTO JSON Contract, Phase 8 Body Binding JSON ownership decision, Phase 8d+/8c+ composable Agent/MCP JSON contexts

## 1. Goal and positioning

Issue #58 removes the handwritten direct-root maintenance burden from
`AgentControlPlaneToolJsonSerializerContext` without introducing runtime
reflection, assembly scanning, a second JSON resolver mainline, or an invalid
Source Generator to Source Generator dependency.

The selected design baseline is:

```text
Handwritten contract surface
        ↓
Dedicated semantic MSBuild Task before CoreCompile
        ↓
Ordinary obj/<Configuration>/<TFM>/*.g.cs
        ↓
Official System.Text.Json Source Generator
        ↓
JsonTypeInfo<T> / NativeAOT
```

The build task derives only direct serialization roots from explicitly marked
interfaces. It emits ordinary C# source containing `[JsonSerializable]`
attributes before Roslyn generators run. The official System.Text.Json Source
Generator remains the only component that creates `JsonTypeInfo`.

The developer outcome for Control Plane is:

1. Add or change a request/result DTO.
2. Add or change the corresponding interface method.
3. Build.
4. The direct request/result roots are generated and consumed by STJ in that
   same build.

No context edit, runtime registry edit, assembly scan, or reflection fallback is
part of this path.

### 1.1 Why this is build-time generation, not another Source Generator

Roslyn Source Generators run without ordering and see the same input
compilation. A generator cannot depend on source emitted by another ordinary
generator in the same round. Therefore a CrestCreates Source Generator cannot
emit `[JsonSerializable]` attributes and expect the STJ Source Generator to
consume them in that compilation.

An MSBuild task that writes a normal `@(Compile)` item before `CoreCompile`
changes the input compilation itself. The generated partial context is then
visible to the official STJ generator.

This design is intentionally tied to the repository's .NET 10.0.100 toolchain.
It does not depend on proposed or future Roslyn pre-compilation generator APIs.

## 2. Repository facts constraining the design

The design is grounded in the following current code facts.

### 2.1 Control Plane context

`AgentControlPlaneToolJsonSerializerContext.cs` is 157 lines and currently
mixes four different concerns:

- direct request/result roots from `IAgentControlPlaneToolService`;
- direct manifest query roots from `IAgentToolManifestProvider`;
- member types that STJ already reaches transitively;
- non-Tool-Surface roots used directly by Activation/HumanTask parsing.

The handwritten list also omits roots that existing tests special-case, such as
`AgentToolResult<string>`, because the tests treat some BCL shapes differently
from CrestCreates DTOs.

The generated design removes those policy exceptions. A closed generic,
collection, scalar, enum, nullable value, or reference type is a direct root
whenever it appears in an included method signature.

### 2.2 Surface shape

`IAgentControlPlaneToolService` is a facade over:

- `IReadOnlyControlPlaneTools`;
- `IMutationControlPlaneTools`;
- `IActivationControlPlaneTools`.

The generator must therefore traverse inherited interfaces, including diamond
graphs, and deduplicate methods and roots.

Tool methods consistently contain:

- `AgentToolInvocationContext`, which is infrastructure and must be excluded by
  the context declaration;
- zero or more business parameters;
- `CancellationToken`, which is globally excluded;
- `Task<AgentToolResult<TResult>>` results.

`RenderDescriptorReviewReportAsync` proves that all business parameters matter:
both `DescriptorReviewReportDto` and `DescriptorReviewReportFormat` are direct
input roots. A "first request parameter only" rule is incorrect.

`IAgentToolManifestProvider` is a separate direct contract surface:

```csharp
IReadOnlyList<AgentToolDescriptor> GetAllTools();
AgentToolDescriptor? GetToolByName(string name);
```

It contributes the collection result, single result, and `string` parameter
roots. It does not use `AgentToolResult<T>`.

### 2.3 Explicit non-surface roots

`DescriptorActivationReviewDecisionParser` directly requests:

```csharp
AgentControlPlaneToolJsonSerializerContext.Default
    .DescriptorActivationReviewDecision

AgentControlPlaneToolJsonSerializerContext.Default
    .CanonicalHash
```

These are not Tool Surface roots. Both remain explicit
`[JsonSerializable]` Extras. They must not rely on incidental transitive
generation.

Other existing handwritten Activation, enum, diagnostic, and member entries
are not automatically retained as direct roots. They remain available as
transitive metadata when reachable from a direct root. If a repository audit
finds another direct serializer call, that type becomes an explicit Extra with
a corresponding test.

### 2.4 Existing coverage tests

`ToolContractCoverageTests` currently reimplements the root model with runtime
reflection:

- it reflects the service interface and unwraps result shapes;
- it inspects `[JsonSerializable]` attributes;
- it reflects generated `JsonTypeInfo<T>` properties;
- it scans all public sealed records in the assembly;
- it maintains exclusions for authorization records;
- it special-cases BCL results;
- it recursively reconstructs supporting property graphs;
- it maintains a handwritten list of non-tool supporting roots.

Those tests have protected the handwritten design, but they must not remain a
second root-definition authority after generation. Rule inference moves into
the semantic build model and is covered there. Control Plane tests consume the
generated root manifest and verify STJ output and behavior.

### 2.5 Existing build infrastructure

`CrestCreates.BuildTasks` currently owns module and entity-permission scanning.
Its implementation is primarily source text/Regex based and has:

- no Roslyn semantic dependency;
- no complete task dependency package;
- no general input-set manifest;
- no `Inputs`/`Outputs` contract for its main generation;
- broad direct-import use across framework projects.

Adding Roslyn and semantic JSON contract generation to that package would make
unrelated module consumers pay its build, package, and design-time costs.

Issue #58 therefore creates a separate opt-in build package.

### 2.6 Existing runtime JSON composition

Phase 8d+/8c+ already defines explicit source-generated JSON context
contributors:

- `IAgentToolJsonContextContributor`;
- `IMcpToolJsonContextContributor`;
- stable contributor identity/order;
- exact Binding Root ownership;
- source-generated resolver-only composition;
- resolver-chain freezing;
- explicit module opt-in.

Issue #58 changes how one context obtains its direct root attributes. It does
not add another contributor API, discover contexts, register contributors, or
change resolver composition.

## 3. Scope and non-goals

### 3.1 In scope

```text
Explicit interface surface
  → semantic direct-root model
  → deterministic pre-CoreCompile C#
  → official STJ source generation
  → Control Plane context migration
  → package and NativeAOT proof
```

This includes:

- the reusable `JsonContractSurfaceAttribute`;
- a dedicated semantic MSBuild task and package;
- deterministic direct-root inference;
- explicit diagnostics;
- incremental and multi-targeting behavior;
- a generated root manifest with Internal default and explicit Public extension,
  with Control Plane fixed to Internal;
- migration of `AgentControlPlaneToolJsonSerializerContext`;
- replacement of reflection-based root-definition tests;
- real pack/restore/build and NativeAOT publish-and-run fixtures.

### 3.2 Non-goals

Issue #58 does not:

- scan all public records, classes, structs, or enums in an assembly;
- infer roots from namespaces, naming conventions, folders, or visibility;
- generate `JsonTypeInfo` itself;
- generate serialization logic or custom converters;
- consume ordinary Source Generator output from the same compilation;
- add runtime reflection or `DefaultJsonTypeInfoResolver`;
- add a runtime fallback when generation fails;
- auto-discover or auto-register Agent/MCP contributors;
- change Agent/MCP Binding Root ownership;
- recursively claim nested member types as direct roots;
- generate Tool DTO projections;
- generate CanonicalHash profiles;
- solve generated CRUD JSON contracts tracked separately;
- perform the Activation assembly split tracked by #57;
- make design-time build the correctness authority;
- change Control Plane JSON naming, null-ignore behavior, or wire shape.

## 4. Project and package architecture

Add one task project whose assembly and package have intentionally different
names:

```text
src/Tooling/
└── CrestCreates.JsonContracts.BuildTasks/
    ├── CrestCreates.JsonContracts.BuildTasks.csproj
    ├── BuildModel/
    ├── Diagnostics/
    ├── Generation/
    ├── GenerateJsonContracts.cs
    ├── WriteJsonContractInputManifest.cs
    └── build/
        ├── CrestCreates.JsonContracts.Build.props
        └── CrestCreates.JsonContracts.Build.targets
```

The identities are:

```text
AssemblyName = CrestCreates.JsonContracts.BuildTasks
PackageId    = CrestCreates.JsonContracts.Build
Target       = net10.0
```

A second packaging-only project is not required. The task project packs its
build assets and private task dependencies directly.

Add focused tests:

```text
tests/Tooling/
├── CrestCreates.JsonContracts.BuildTasks.Tests/
└── CrestCreates.JsonContracts.Build.PackageTests/

tests/Runtime/Agent/
├── CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/
└── CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.Tests/
```

The unit/MSBuild test split may be represented by two test projects or by one
test project with isolated fixture helpers. The package fixture remains a
separate test project because it must not gain an accidental ProjectReference
to the task assembly.

### 4.1 Dependency rules

`CrestCreates.JsonContracts.BuildTasks` may reference:

- `Microsoft.CodeAnalysis.Common`;
- `Microsoft.CodeAnalysis.CSharp`;
- `Microsoft.Build.Framework`;
- `Microsoft.Build.Utilities.Core`;
- BCL libraries available on `net10.0`.

It must not reference:

- `CrestCreates.CodeGenerator`;
- `CrestCreates.BuildTasks`;
- Runtime, Metadata runtime, Persistence, Platform, or Integrations projects;
- `CrestCreates.Agent.ControlPlane.Abstractions`;
- application projects.

The semantic contract is recognized by fully qualified metadata name:

```text
CrestCreates.Core.Abstractions.Serialization.JsonContractSurfaceAttribute
```

The task does not need a compile-time project reference to
`CrestCreates.Core.Abstractions`.

### 4.2 Source-repository and package consumption

The formal consumer contract is a direct private package reference:

```xml
<PackageReference Include="CrestCreates.JsonContracts.Build"
                  PrivateAssets="all" />
```

The package uses `build/`, not `buildTransitive/`. Only the project that owns a
marked context runs generation.

Within the CrestCreates source repository, the Control Plane project may use a
non-runtime `ProjectReference` plus the exact same props/targets files so clean
repository builds do not require a pre-published local package. That authoring
transport must:

- set `ReferenceOutputAssembly="false"`;
- establish task-project build ordering;
- point `CrestCreatesJsonContractsTaskAssembly` at the built task output;
- reuse the packaged task and target implementation unchanged.

This is not a second generation path. Only task assembly transport differs.
The local-feed fixture is authoritative for the published package contract.

For any one consumer inner build, exactly one task transport and one effective
copy of the build targets may be active:

```text
Repository transport
    = non-runtime ProjectReference
    + repository task assembly path
    + explicit import

Package transport
    = PackageReference
    + package-relative task assembly path
    + NuGet build-asset import
```

The transports are mutually exclusive. A project must not combine the package
reference with the repository override. The props/targets contract reserves a
transport/import sentinel so a mixed transport, two competing task assembly
paths, or a second effective target import fails before the first custom task
invocation or any task-owned side effect. Specifically, failure must precede:

- input-manifest write;
- generated-source write;
- success-stamp update;
- generated `@(Compile)` inclusion.

`UsingTask` is an evaluation-time mapping declaration, so the validation target
does not claim to prevent that declaration from being evaluated. It prevents
the first task invocation; therefore the custom task assembly is not loaded on
the conflict path. Transport validation uses the built-in MSBuild `Error` task
and does not depend on loading the CrestCreates task assembly. The exact
sentinel property and import-guard mechanics belong to the implementation
Plan.

Repeated evaluation of the exact same guarded import may be idempotent, but it
must still produce only:

- one selected task assembly;
- one input preparation target;
- one generation target;
- one generated `@(Compile)` inclusion.

No "last imported transport wins" behavior is allowed.

## 5. Developer-facing declaration

Add the attribute to
`CrestCreates.Core.Abstractions.Serialization`:

```csharp
namespace CrestCreates.Core.Abstractions.Serialization;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class JsonContractSurfaceAttribute : Attribute
{
    public JsonContractSurfaceAttribute(Type surfaceType)
        => SurfaceType = surfaceType;

    public Type SurfaceType { get; }

    public Type[] ExcludedParameterTypes { get; set; } = [];
}
```

`CancellationToken` is permanently excluded by the platform rule. The property
exists only for surface-specific infrastructure parameters.

Exclusions use exact CLR type identity. They are not assignability filters and
do not apply to return types.

### 5.1 Control Plane declaration after migration

```csharp
using System.Text.Json.Serialization;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Core.Abstractions.Serialization;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

[JsonContractSurface(
    typeof(IAgentControlPlaneToolService),
    ExcludedParameterTypes = new[] { typeof(AgentToolInvocationContext) })]
[JsonContractSurface(typeof(IAgentToolManifestProvider))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]

// Explicit Extras: direct serialization roots outside the marked surfaces.
[JsonSerializable(typeof(DescriptorActivationReviewDecision))]
[JsonSerializable(typeof(CanonicalHash))]
public sealed partial class AgentControlPlaneToolJsonSerializerContext
    : JsonSerializerContext
{
}
```

`AgentControlPlaneToolJsonSerializerOptions.CreateDefault()` keeps its existing
source-generated resolver behavior. Issue #58 does not change the options
factory or introduce a reflection resolver.

### 5.2 Direct-root ownership terms

This design uses three precise terms.

**Surface Root**

A normalized parameter or return type obtained directly from a method on a
marked interface.

**Explicit Extra**

A handwritten `[JsonSerializable(typeof(T))]` on the context for a type that is
directly serialized outside the marked interfaces.

**Transitive Metadata Type**

A member, collection element, dictionary component, enum, or other nested type
reached by the official STJ generator from a Surface Root or Explicit Extra.

Only Surface Roots are generated. Explicit Extras remain reviewable source.
Transitive Metadata Types are not treated as direct-root ownership and are not
repeated in the handwritten context.

## 6. Semantic discovery and validation

The build task constructs a minimal Roslyn `CSharpCompilation` from:

- the current `@(Compile)` items visible before its target;
- `@(ReferencePathWithRefAssemblies)`;
- the project's parse and compilation settings.

It does not:

- run analyzers;
- run Source Generators;
- emit an assembly;
- enumerate loaded runtime assemblies;
- call `Assembly.GetTypes()`.

### 6.1 Context discovery

The task finds class declarations with
`JsonContractSurfaceAttribute` by semantic attribute identity.

A marked context must be:

- a top-level class;
- non-generic;
- `partial`;
- directly or indirectly derived from
  `System.Text.Json.Serialization.JsonSerializerContext`;
- declared in current-project source;
- uniquely identifiable by fully qualified metadata name.

Nested and generic contexts are deferred. They fail generation rather than
receiving a partially correct output.

The context may contain multiple handwritten partial declarations. Every
declaration must satisfy normal C# partial rules, and at least one source
declaration must carry the surface attribute.

If a project contains no marked context, generation succeeds and writes the
deterministic empty generated file. This overwrites any stale output left after
removing the final attribute.

### 6.2 Surface discovery

Each `SurfaceType` must resolve to a closed interface.

For every declared surface, the task:

1. includes the interface itself;
2. recursively includes all inherited interfaces;
3. visits each interface once by symbol identity;
4. collects public instance ordinary methods;
5. ignores static members and property/event accessors;
6. deduplicates inherited diamond methods by original symbol/signature;
7. preserves every method-to-root provenance edge for diagnostics and generated
   comments.

Default interface method bodies do not change root inference. Only their public
instance signatures matter.

Generic methods are rejected. A closed generic interface may be supported only
when all substituted method signatures are closed and valid; unbound generic
interfaces are rejected.

Before parameter or return-root inference, the task rejects methods whose
return is `ref T` or `ref readonly T` by checking Roslyn
`IMethodSymbol.ReturnsByRef` and `ReturnsByRefReadonly`. Inspecting only
`ReturnType` is insufficient because it is still `T` for those signatures.
These unsupported return signature shapes fail with `CJC005`.

### 6.3 Parameter inference

All parameters are considered, not only the first business parameter.

For each parameter:

1. reject `ref`, `out`, `in`, pointer, function-pointer, and ref-like shapes;
2. skip exact `System.Threading.CancellationToken`;
3. skip exact types listed in `ExcludedParameterTypes`;
4. normalize nullability;
5. add the resulting type as a Surface Root.

`params T[]` is a normal array root.

For nullable reference annotations, root identity is the underlying runtime CLR
type. For nullable value types, root identity remains `Nullable<T>`.

### 6.4 Return inference

Return types are normalized as follows:

| Declared return | Surface Root |
|---|---|
| `void` | none |
| `Task` | none |
| `ValueTask` | none |
| `Task<T>` | `T` |
| `ValueTask<T>` | `T` |
| any other closed valid type `T` | `T` |

The task unwraps exactly one `Task<T>` or `ValueTask<T>` layer. It does not
recognize arbitrary custom awaitables.

For Control Plane:

```text
Task<AgentToolResult<DescriptorInfo>>
    → AgentToolResult<DescriptorInfo>
```

It does not add `DescriptorInfo` separately. STJ reaches the member metadata
transitively.

`AgentToolResult<string>` is a valid closed generic Surface Root. There is no
BCL-result exception.

### 6.5 Root validity

A normalized root is valid when:

- it has no unbound type parameters anywhere in its graph;
- it is not an error symbol;
- it is not a pointer or function pointer;
- it is not ref-like;
- it is accessible from the generated partial context;
- a valid `typeof(...)` expression can be emitted.

Arrays, enums, scalars, tuples, collections, dictionaries, nullable value
types, and closed generics are not special-cased away.

The task does not attempt to predict every STJ diagnostic. After root
generation, the official STJ generator and C# compiler remain authoritative
for serialization-shape validity.

### 6.6 Same-project generated types

The pre-CoreCompile compilation can consume:

- handwritten source in the current project;
- referenced assemblies already built by MSBuild;
- MSBuild-generated source added to `@(Compile)` before this target.

It cannot consume ordinary Source Generator output from the same compilation.
The current Control Plane is compatible with this boundary because generated
Draft DTOs are compiled into the referenced
`CrestCreates.Agent.DraftContracts` assembly before
`ControlPlane.Abstractions` is analyzed.

If a surface signature contains an unresolved/ErrorType root, generation fails
with a diagnostic that explicitly explains this limitation. The developer must:

- move the generated contract into a referenced contract assembly;
- arrange for an earlier MSBuild-generated compile item; or
- keep an explicit root only when the type is otherwise visible before
  `CoreCompile`.

The task must not silently skip the method or emit an incomplete context.

### 6.7 Explicit root deduplication

The task reads existing semantic `JsonSerializableAttribute` declarations on
the marked context.

If a Surface Root is already an Explicit Extra:

- the handwritten attribute wins;
- the generated partial does not emit a duplicate attribute;
- the root remains present in both provenance sets in the generated manifest.

Attribute configuration such as `TypeInfoPropertyName` or per-type generation
mode remains owned by the handwritten declaration.

## 7. Deterministic generated output

Each inner build writes one project-level file:

```text
obj/<Configuration>/<TargetFramework>/CrestCreates.JsonContracts.g.cs
```

The file:

- begins with `// <auto-generated />`;
- uses UTF-8 without BOM;
- uses `\n` line endings;
- contains no timestamps, absolute paths, machine names, or nondeterministic
  values;
- uses fully qualified `global::` type names;
- sorts contexts by fully qualified metadata name using Ordinal comparison;
- sorts roots by canonical fully qualified metadata name using Ordinal
  comparison;
- deduplicates roots by `SymbolEqualityComparer.Default`;
- records sorted source method names in comments for traceability.

Example:

```csharp
// <auto-generated />
#nullable enable

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json
{
    // Surface:
    //   IAgentToolManifestProvider.GetToolByName(string)
    [global::System.Text.Json.Serialization.JsonSerializable(
        typeof(global::System.String))]

    // Surface:
    //   IReadOnlyControlPlaneTools.GetDescriptorByRefAsync(...)
    [global::System.Text.Json.Serialization.JsonSerializable(
        typeof(global::CrestCreates.Metadata.Abstractions.DescriptorRef))]

    // Surface:
    //   IMutationControlPlaneTools.RenderDescriptorReviewReportAsync(...)
    [global::System.Text.Json.Serialization.JsonSerializable(
        typeof(global::CrestCreates.Agent.ControlPlane.Abstractions.AgentToolResult<
            global::System.String>))]
    public sealed partial class AgentControlPlaneToolJsonSerializerContext
    {
    }
}
```

The generated declaration reproduces the context's accessibility and compatible
type modifiers. It does not restate the base class or
`JsonSourceGenerationOptions`.

### 7.1 Generated root manifest

For each context, generate a root manifest next to the partial class. Manifest
accessibility is a generation setting with two supported values:

| Value | Generated API | Intended use |
|---|---|---|
| `Internal` | internal class and members | context-owning assembly and friend tests |
| `Public` | public class and members | an explicitly separate contributor assembly |

`Internal` is the default. Issue #58 fixes Control Plane to `Internal`.

```csharp
internal static class AgentControlPlaneToolJsonContractRoots
{
    private static readonly
        global::System.Collections.Frozen.FrozenSet<global::System.Type>
        s_surfaceRootTypes = ...;
    private static readonly
        global::System.Collections.Frozen.FrozenSet<global::System.Type>
        s_explicitRootTypes = ...;
    private static readonly
        global::System.Collections.Frozen.FrozenSet<global::System.Type>
        s_allDirectRootTypes = ...;

    internal static global::System.Collections.Generic.IReadOnlySet<
        global::System.Type> SurfaceRootTypes => s_surfaceRootTypes;
    internal static global::System.Collections.Generic.IReadOnlySet<
        global::System.Type> ExplicitRootTypes => s_explicitRootTypes;
    internal static global::System.Collections.Generic.IReadOnlySet<
        global::System.Type> AllDirectRootTypes => s_allDirectRootTypes;
}
```

Rules:

- `SurfaceRootTypes` includes every inferred root, even when an explicit
  attribute suppresses duplicate emission;
- `ExplicitRootTypes` is derived from handwritten attributes;
- `AllDirectRootTypes` is their set union;
- iteration order is not a contract; tests compare sets;
- construction uses generated `typeof(...)` expressions only;
- each backing instance is an immutable
  `System.Collections.Frozen.FrozenSet<Type>` created with `ToFrozenSet()`;
- public mode may expose `IReadOnlySet<Type>`, but the returned instance must
  not be downcastable to a mutable collection;
- no runtime reflection or assembly scan is used.

Issue #58 uses the manifest only from the context assembly and its friend test
assembly. It does not wire the manifest into Agent/MCP contributors.

The `Public` option is an extension point for later work such as #62, where a
context may live in an Abstractions assembly and its contributor in a separate
runtime assembly. Enabling `Public` is an explicit public API decision by the
context-owning project; the task never promotes a manifest automatically.

Issue #58 proves both writer modes at the reusable task layer but emits only the
Internal mode in Control Plane. Cross-assembly contributor consumption,
registration, and ownership migration remain outside #58.

For multiple contexts with the same simple name in different namespaces, each
manifest is emitted in its context namespace. A same-namespace name collision
is a build diagnostic.

## 8. MSBuild target contract

The package provides:

```text
CrestCreates.JsonContracts.Build.props
CrestCreates.JsonContracts.Build.targets
```

### 8.1 Public MSBuild properties

```xml
<PropertyGroup>
  <CrestCreatesJsonContractGenerationEnabled>true</CrestCreatesJsonContractGenerationEnabled>
  <CrestCreatesJsonContractGeneratedFile>
    $(IntermediateOutputPath)CrestCreates.JsonContracts.g.cs
  </CrestCreatesJsonContractGeneratedFile>
  <CrestCreatesJsonContractInputManifest>
    $(IntermediateOutputPath)CrestCreates.JsonContracts.inputs.json
  </CrestCreatesJsonContractInputManifest>
  <CrestCreatesJsonContractGenerationStamp>
    $(IntermediateOutputPath)CrestCreates.JsonContracts.stamp
  </CrestCreatesJsonContractGenerationStamp>
  <CrestCreatesJsonContractTemporaryDirectory>
    $(IntermediateOutputPath)CrestCreates.JsonContracts.tmp
  </CrestCreatesJsonContractTemporaryDirectory>
  <CrestCreatesJsonContractManifestAccessibility>
    Internal
  </CrestCreatesJsonContractManifestAccessibility>
</PropertyGroup>
```

Project-local override is allowed for diagnostics or fixtures, but every
task-owned source, manifest, stamp, and temporary path must remain under
`$(IntermediateOutputPath)`.

`CrestCreatesJsonContractManifestAccessibility` accepts only `Internal` or
`Public`. It participates in the deterministic input manifest. Any other value
fails generation. Control Plane must set or inherit `Internal`; #58 does not
add a public root-manifest API to that package.

An extension property allows a project to name earlier MSBuild generation
targets whose `@(Compile)` outputs must exist before semantic analysis:

```xml
<CrestCreatesJsonContractGenerationDependsOn>
  $(CrestCreatesJsonContractGenerationDependsOn)
</CrestCreatesJsonContractGenerationDependsOn>
```

It is empty by default. It does not make ordinary Source Generator output
visible.

### 8.2 Output-path safety

Every task-owned path must be contained by the normalized full path of
`$(IntermediateOutputPath)`:

```text
CrestCreatesJsonContractGeneratedFile
CrestCreatesJsonContractInputManifest
CrestCreatesJsonContractGenerationStamp
CrestCreatesJsonContractTemporaryDirectory
```

Containment is a directory-boundary check, not a raw string-prefix check. For
example, an allowed root ending in `obj/net10.0` must not admit
`obj/net10.0-evil`. The implementation computes full paths and uses a
root-relative result: reject a candidate when the relative path is rooted,
equals `..`, or begins with a complete `..` directory segment. The comparison
uses platform-appropriate path semantics.

`ValidateCrestCreatesJsonContractPaths` uses only built-in MSBuild property
functions and the built-in `Error` task. It runs before transport validation,
custom task invocation, file write, stamp update, or Compile-item inclusion:

```text
ValidateCrestCreatesJsonContractPaths
    ↓
ValidateCrestCreatesJsonContractTransport
    ↓
PrepareCrestCreatesJsonContractInputs
    ↓
GenerateCrestCreatesJsonContracts
    ↓
update successful-generation stamp
    ↓
IncludeCrestCreatesJsonContractGeneratedSource
```

Both custom tasks independently perform defense-in-depth validation through a
required task property:

```csharp
[Required]
public string AllowedOutputRoot { get; set; } = string.Empty;
```

MSBuild passes:

```xml
AllowedOutputRoot="$(IntermediateOutputPath)"
```

`WriteJsonContractInputManifest` validates its `OutputPath`;
`GenerateJsonContracts` validates its `OutputPath` and task-owned temporary
location. The target-level validation is authoritative for the success stamp,
which is updated with a built-in MSBuild task rather than either custom task.
An invalid or escaping path fails with `CJC012` without touching the candidate
path or an existing valid output.

The normalized `AllowedOutputRoot` and normalized temporary directory
participate in the deterministic input manifest. Changing the allowed root or
temporary location therefore invalidates incremental state.

### 8.3 Target sequence

```text
PrepareForBuild
    ↓
GenerateGlobalUsings
    ↓
ResolveReferences
    ↓
configured pre-semantic Compile generators
    ↓
ValidateCrestCreatesJsonContractPaths
    ↓
ValidateCrestCreatesJsonContractTransport
    ↓
PrepareCrestCreatesJsonContractInputs
    ↓
GenerateCrestCreatesJsonContracts
    ↓
update successful-generation stamp
    ↓
IncludeCrestCreatesJsonContractGeneratedSource
    ↓
@(Compile) += CrestCreates.JsonContracts.g.cs exactly once
    ↓
CoreCompile
    ↓
official STJ Source Generator
```

`GenerateCrestCreatesJsonContracts` must:

- use `BeforeTargets="CoreCompile"`;
- execute only after `PrepareForBuild`, `GenerateGlobalUsings`,
  `ResolveReferences`, and
  `$(CrestCreatesJsonContractGenerationDependsOn)` have completed;
- run only when generation is enabled;
- run only when `$(IsCrossTargetingBuild) != 'true'`;
- not run as a correctness action when `$(DesignTimeBuild) == 'true'`.

`IncludeCrestCreatesJsonContractGeneratedSource` runs before `CoreCompile`,
depends on generation for formal builds, and, when generation is enabled, adds
the existing generated file to `@(Compile)` exactly once. It still performs the
item addition when the incremental generation target is skipped. In
design-time builds it adds only an already existing output.

Each target framework inner build owns an isolated output and input manifest.
Formal generation creates a filtered source item set from the current
`@(Compile)` items and always excludes
`$(CrestCreatesJsonContractGeneratedFile)` by normalized full path. A previous
task output must never become semantic input to the next generation pass.

`GenerateGlobalUsings` is a required semantic dependency, not merely another
target that happens to use `BeforeTargets="CoreCompile"`. The .NET SDK adds
`$(GeneratedGlobalUsingsFile)` to `@(Compile)` from that target. The JSON input
snapshot must be taken afterward so ordinary handwritten source using implicit
`Task<T>`, `CancellationToken`, `IReadOnlyList<T>`, and similar types binds the
same way in the provisional compilation as it does in the formal C#
compilation.

When `GenerateGlobalUsings` produces a file, the normalized full path of that
actual `@(Compile)` item must appear in the filtered formal source set, the
input manifest, and the generation target's direct inputs. Recording only the
`ImplicitUsings` property is insufficient.

### 8.4 Input-set manifest

MSBuild's incremental check does not automatically detect that an item was
removed from the previous input list. Therefore source/reference paths cannot
be the only `Inputs`.

`PrepareCrestCreatesJsonContractInputs` runs on every formal inner build and
writes a deterministic manifest only when its bytes changed. It includes:

- sorted normalized full paths from the filtered formal source item set;
- sorted normalized paths from `@(ReferencePathWithRefAssemblies)`;
- `LangVersion`;
- `DefineConstants`;
- `Nullable`;
- `AllowUnsafeBlocks`;
- `ImplicitUsings`;
- normalized `AllowedOutputRoot`;
- normalized `CrestCreatesJsonContractTemporaryDirectory`;
- `CrestCreatesJsonContractManifestAccessibility`;
- `TargetFramework`;
- task semantic model version;
- task assembly version or content identity.

Path contents are not copied into the manifest. Source and reference files
remain direct target inputs, so normal timestamp changes invalidate generation.
The manifest exists to detect input-set and compilation-option changes,
including source deletion.

The manifest writer:

- sorts with Ordinal comparison;
- uses an unambiguous structured encoding;
- normalizes path separators;
- compares existing bytes before writing;
- does not change its timestamp when content is unchanged.

The prepare target also checks that the generated source exists. If the source
is missing while a prior success stamp exists, it invalidates only that
task-owned stamp so the generation target cannot incorrectly skip.

### 8.5 Incremental target

The generation target declares:

```text
Inputs:
  input manifest
  every filtered formal source item
  every ReferencePathWithRefAssemblies item
  task assembly

Outputs:
  successful-generation stamp
```

The generated `.g.cs` is the semantic artifact; the stamp is the incremental
completion artifact. On every successful execution, the target updates the
stamp after the task has produced or verified the generated source.

This separation is required because a source/reference input can change while
the inferred roots remain byte-identical. The generated source must preserve
its timestamp in that case, but MSBuild still needs a newer output proving that
the changed inputs were processed.

Behavior:

- unchanged inputs cause the target to skip;
- changing a source or reference regenerates;
- adding or deleting a source changes the manifest and regenerates;
- changing a compilation property changes the manifest and regenerates;
- changing the task implementation regenerates;
- removing a method overwrites stale roots;
- removing all marked contexts overwrites stale output with an empty generated
  file;
- unchanged generated bytes do not change the output timestamp.
- a successful run updates the stamp even when generated source bytes are
  unchanged;
- a missing generated source invalidates the stamp and forces regeneration.

The task writes through a temporary file under the same intermediate directory
and atomically replaces the output only when bytes differ.

### 8.6 Compilation construction

The task receives explicit item/property values from MSBuild. It does not infer
the project by opening the `.csproj`. The source list is the filtered formal
source set described above, never the task's previous generated output.

It creates parse options using the project's:

- language version;
- preprocessor symbols.

It creates compilation options using the project's:

- nullable context;
- unsafe setting.

References come from `@(ReferencePathWithRefAssemblies)`. A missing or unreadable
source/reference is a build error with the exact path.

The task must not treat every diagnostic in the provisional compilation as its
own failure. The official compilation may legitimately depend on Source
Generators, including STJ implementing abstract `JsonSerializerContext`
members. The task fails only on diagnostics that prevent its own context,
surface, method, or root model from being resolved safely.

### 8.7 Design-time behavior

Design-time builds are not the correctness authority.

When a prior formal build output exists, the targets include it as a compile
item for IDE reuse without rerunning semantic generation. When it does not
exist, the IDE may show incomplete generated-context diagnostics until a formal
build runs.

The formal gates are:

- `dotnet build`;
- `dotnet test`;
- `dotnet publish`;
- clean local-feed package fixture;
- NativeAOT publish-and-run fixture.

### 8.8 Clean

An `AfterTargets="CoreClean"` target removes only:

- `$(CrestCreatesJsonContractGeneratedFile)`;
- `$(CrestCreatesJsonContractInputManifest)`;
- `$(CrestCreatesJsonContractGenerationStamp)`;
- task-owned temporary files beneath the same
  `$(IntermediateOutputPath)`.

It must not enumerate or delete user source paths.

## 9. NuGet package contract

The package layout is:

```text
CrestCreates.JsonContracts.Build.nupkg
├── build/
│   ├── CrestCreates.JsonContracts.Build.props
│   └── CrestCreates.JsonContracts.Build.targets
└── tasks/net10.0/
    ├── CrestCreates.JsonContracts.BuildTasks.dll
    ├── CrestCreates.JsonContracts.BuildTasks.deps.json
    ├── Microsoft.CodeAnalysis.dll
    ├── Microsoft.CodeAnalysis.CSharp.dll
    └── required private runtime dependencies
```

Package rules:

- package metadata sets `DevelopmentDependency=true`;
- the task project sets `IncludeBuildOutput=false` and packs task files
  explicitly;
- build assets live in `build/`, not `buildTransitive/`;
- the task project enables dependency-file generation;
- Roslyn/private runtime dependencies are copied and packed beside the task;
- `Microsoft.Build.Framework` and `Microsoft.Build.Utilities.Core` are supplied
  by the MSBuild host and are not duplicated in the package;
- no task assembly or dependency appears under `lib/`;
- no task assembly or dependency is copied to consumer `bin/`;
- no task assembly or dependency appears in application publish output;
- the package contributes no runtime assembly reference;
- `UsingTask` resolves the assembly through a package-relative
  `tasks/net10.0` path unless the source-repository override is set;
- every task mapping uses the fully qualified task name plus:

  ```xml
  Runtime="NET"
  Architecture="*"
  ```

  This package intentionally requires .NET SDK 10 / MSBuild 18 and uses the
  .NET TaskHost to isolate the task's private Roslyn dependency set from the
  MSBuild process.

MSBuild semantic references:

- [UsingTask element](https://learn.microsoft.com/en-us/visualstudio/msbuild/usingtask-element-msbuild?view=visualstudio)
  — first applicable mapping wins; assembly loads when the task is first used.
- [Configure targets and tasks](https://learn.microsoft.com/en-us/visualstudio/msbuild/how-to-configure-targets-and-tasks?view=visualstudio)
  — `Runtime="NET"` support and .NET TaskHost behavior in MSBuild 18.

The package contract test must execute:

```text
pack
  → create isolated local feed
  → create isolated consumer with PackageReference only
  → restore with that feed
  → clean build once
  → inspect generated source and STJ output
  → inspect bin/publish leakage
```

A ProjectReference-only test is not sufficient because it hides missing
`.deps.json` and private task dependencies.

## 10. Diagnostics

All correctness diagnostics are errors. Generation never logs an error and then
continues with a partial root set.

| ID | Condition |
|---|---|
| `CJC001` | Marked context is not a top-level, non-generic, partial `JsonSerializerContext` |
| `CJC002` | `SurfaceType` is missing, unresolved, not an interface, or unbound |
| `CJC003` | Surface contains an unsupported generic method |
| `CJC004` | Parameter uses `ref`, `out`, `in`, pointer, function-pointer, or ref-like shape |
| `CJC005` | A return/parameter root contains unbound type parameters or is ref-like/pointer-shaped, or a method returns by `ref`/`ref readonly` |
| `CJC006` | A root is inaccessible from the generated context |
| `CJC007` | A root is unresolved before `CoreCompile`; possible same-compilation SG-only type |
| `CJC008` | Generated context or manifest name collides in its namespace |
| `CJC009` | A source file cannot be read or a syntax tree cannot be constructed with the supplied settings |
| `CJC010` | A metadata reference cannot be loaded |
| `CJC011` | A candidate declaration uses the contract marker but the semantic model cannot resolve the required STJ/Core attribute identities |
| `CJC012` | A task-owned path escapes the allowed output root, or the input manifest/generated output cannot be written safely |
| `CJC013` | Manifest accessibility is not exactly `Internal` or `Public` |
| `CJC014` | More than one task transport, task assembly path, or effective targets import is active; validation fails before the first custom task invocation or side effect |

Diagnostics include:

- context metadata name;
- surface metadata name;
- method signature;
- parameter name or return position;
- offending normalized type;
- source location when available;
- actionable remediation for same-compilation generated types.

The task also emits low-importance MSBuild messages for:

- no marked contexts;
- output unchanged;
- generated context/root counts;
- incremental skip, when available from target logging.

It does not log every inferred root at normal importance.

## 11. Control Plane migration

### 11.1 Generated Surface Roots

The context declares two surfaces:

```text
IAgentControlPlaneToolService
IAgentToolManifestProvider
```

For `IAgentControlPlaneToolService`:

- recursively include all three inherited sub-interfaces;
- exclude `AgentToolInvocationContext`;
- permanently exclude `CancellationToken`;
- include every other parameter;
- unwrap every `Task<T>` to `T`;
- keep each closed `AgentToolResult<TResult>` as the result root.

For `IAgentToolManifestProvider`:

- include `IReadOnlyList<AgentToolDescriptor>`;
- include `AgentToolDescriptor` from the nullable single result;
- include `string` from `GetToolByName`.

Duplicate scalar or DTO roots across methods are emitted once, with all source
methods retained in deterministic comments.

### 11.2 Explicit Extras

The initial reviewed list is:

```text
DescriptorActivationReviewDecision
CanonicalHash
```

Before removing the handwritten registrations, implementation must search the
repository for:

- strongly typed generated context property access;
- `JsonSerializer.Serialize/Deserialize` overloads using this context;
- `JsonSerializerOptions` created from this context and used for a non-surface
  root;
- documented adapter contracts that promise a non-surface direct root.

Any additional actual direct root is retained as an explicit Extra and covered
by a named test. A public record merely existing in the assembly is not enough.

### 11.3 Removed handwritten categories

The migration removes handwritten entries whose only purpose is:

- member traversal;
- enum traversal;
- nested collections;
- nested Activation records;
- nested diagnostics;
- stable upstream value objects reachable from a direct root.

STJ continues to generate their metadata transitively where required.

Generated `JsonTypeInfo<T>` properties for incidental nested types are not a
separate root-ownership contract. The supported contract is that every Surface
Root and Explicit Extra resolves through the context and preserves its existing
wire shape.

### 11.4 Runtime behavior unchanged

The following stay unchanged:

- `AgentControlPlaneToolJsonSerializerOptions.CreateDefault()`;
- camelCase property naming;
- null-ignore behavior;
- metadata generation mode;
- Activation parser use of strongly typed `JsonTypeInfo`;
- Control Plane service and manifest interfaces;
- Agent/MCP contributor registration and ordering;
- resolver freezing and Binding Root ownership.

No runtime manifest scan or service-signature reflection is introduced.

## 12. Test strategy

### 12.1 Semantic model and writer tests

These tests create in-memory Roslyn compilations for the task's pure model and
writer layers:

```text
Build_InheritedInterfaceMethods
Build_DiamondInheritance_DeduplicatesMethods
Build_AllBusinessParameters
Build_UnwrapsTaskAndValueTask
Build_TaskValueTaskAndVoidProduceNoReturnRoot
Build_IncludesClosedGenericCollectionScalarEnumAndNullableRoots
Build_ExcludesCancellationToken
Build_ExcludesConfiguredInfrastructureTypesByExactIdentity
Build_ExcludedTypesDoNotSuppressReturnRoots
Build_DoesNotExpandNestedPropertyGraph
Build_DeduplicatesAndOrdinalSortsRoots
Build_TracksAllMethodProvenanceForSharedRoot
Build_DoesNotDuplicateExplicitJsonSerializableRoots
Build_ExplicitRootStillAppearsInManifestUnion
Build_MultipleContextsAreIsolatedAndSorted
Build_NoMarkedContextWritesDeterministicEmptyOutput
Build_InternalManifestAccessibility
Build_PublicManifestAccessibility
Fail_NonPartialContext
Fail_NestedOrGenericContext
Fail_NonInterfaceOrUnboundSurface
Fail_GenericMethod
Fail_ByRefPointerOrRefLikeRoot
Fail_ByRefReturn
Fail_ByRefReadonlyReturn
Fail_InaccessibleRoot
Fail_UnresolvedPreCoreCompileRoot
Fail_InvalidManifestAccessibility
Write_IsByteStable
Write_DoesNotContainPathOrTimestamp
```

Writer snapshots normalize line endings and compare complete bytes.

### 12.2 MSBuild contract tests

Fixture projects invoke `dotnet build` as a separate process:

```text
Build_GeneratedAttributesParticipateInSameCompilation
Build_StjGeneratorProducesJsonTypeInfoForGeneratedRoots
Build_CleanCheckoutSucceedsOnFirstInvocation
Build_ImplicitUsingsOnlySurfaceBindsAfterGenerateGlobalUsings
Build_GlobalUsingsFileParticipatesInInputManifestAndTargetInputs
Build_AddMethodAddsRootWithoutEditingContext
Build_RemoveMethodRemovesStaleRoot
Build_SourceDeletionInvalidatesInputManifest
Build_CompilationPropertyChangeInvalidatesInputManifest
Build_UnchangedInputSkipsOrDoesNotRewriteOutput
Build_UnchangedSemanticOutputDoesNotRewriteTimestamp
Build_MultiTargetingOutputsAreIsolated
Build_DesignTimeReusesExistingGeneratedFile
Build_CleanRemovesOnlyTaskOwnedIntermediateFiles
Build_TaskFailureStopsCoreCompile
Build_SameProjectSourceGeneratorOnlyRootFailsClearly
Build_PublicManifestIsConsumableFromSeparateAssembly
Build_PublicManifestSetsAreImmutable
Build_InternalManifestRemainsAssemblyScoped
Fail_GeneratedPathOutsideIntermediateOutputPath
Build_RepositoryAndPackageTransportConflictFailsBeforeGeneration
Build_DuplicateImportCannotRunGenerationOrAddCompileTwice
```

The same-compilation test includes both:

- the dedicated task adding attributes before `CoreCompile`;
- an intentionally ordinary test Source Generator whose emitted type is not
  visible to the task.

This proves the boundary rather than documenting it only.

### 12.3 Package contract tests

```text
Pack_ContainsBuildAssetsTaskAssemblyDepsAndRoslynDependencies
Pack_DoesNotContainLibRuntimeAssembly
Pack_LocalFeedRestoreAndFirstBuildSucceeds
Pack_ConsumerNeedsNoProjectReference
Pack_TaskDependenciesDoNotLeakToBin
Pack_TaskDependenciesDoNotLeakToPublish
Pack_TransitiveConsumerDoesNotRunGeneration
Pack_ExactlyOnePackageTransportAndTargetsSetIsActive
```

The transitive test proves the `build/` rather than `buildTransitive/`
decision.

### 12.4 Control Plane tests

Replace root-definition reflection with generated-manifest assertions:

```text
GeneratedSurfaceRoots_AreResolvedByContext
ExplicitExtras_AreResolvedByContext
AllDirectRoots_EqualSurfaceUnionExplicit
RepresentativeSurfaceRequests_RoundTrip
RepresentativeSurfaceResults_RoundTrip
AgentToolResultOfString_RoundTripsWithoutSpecialCase
ManifestListAndSingleResults_RoundTrip
DescriptorReviewReportFormat_IsDirectInputRoot
DescriptorActivationReviewDecisionParser_UsesExplicitAotMetadata
CanonicalHashParserRoot_IsExplicit
SerializerOptions_ContainOnlyGeneratedResolverChain
NoAssemblyWidePublicRecordScanOrKnownExclusionList
```

The build-task rule tests, not runtime reflection, prove that interface
signatures map to the manifest. Control Plane tests prove that the generated
manifest and official STJ output work together in the real assembly.

DTO boundary and semantic preservation tests from Phase 7c remain. They test
contract safety and wire meaning, not root inference.

### 12.5 NativeAOT fixture

Add a console fixture with:

```xml
<PublishAot>true</PublishAot>
<IsAotCompatible>true</IsAotCompatible>
```

The test performs:

```text
dotnet publish
  -c Release
  -r linux-x64
  --self-contained true
  -p:CrestCreatesPublishMode=aot
  --disable-build-servers
```

It then executes the original native binary and verifies:

- representative request serialization/deserialization;
- representative `AgentToolResult<T>` round-trip;
- `AgentToolResult<string>` round-trip;
- manifest list/single roots;
- `DescriptorActivationReviewDecision`;
- direct `CanonicalHash` deserialization;
- missing unrelated runtime types fail rather than using reflection fallback.

Passing analyzers or `PublishTrimmed` alone does not satisfy this gate.

## 13. Case matrix

| Case | Expected |
|---|---|
| Add request + result tool method | request and closed result envelope become Surface Roots |
| Inherited sub-interface method | included recursively |
| Diamond interface inheritance | method and roots emitted once |
| Multiple business parameters | every non-excluded parameter included |
| `AgentToolResult<string>` | closed generic root generated |
| DTO adds nested record | direct root file unchanged unless signature changes; STJ updates transitive metadata |
| Manifest provider list/single result | collection and item roots generated |
| Explicit Activation parser root | handwritten Extra retained, no duplicate generated attribute |
| Same type from multiple methods | one attribute, all provenance comments |
| `CancellationToken` | always ignored |
| Configured infrastructure parameter | ignored by exact type identity |
| `Task`/`ValueTask`/`void` return | no return root |
| Closed collection/scalar/enum | generated as a normal root |
| Nullable reference | normalized to runtime CLR type |
| Nullable value | `Nullable<T>` root |
| Open generic/generic method | build fails |
| `ref`/`out`/`in`/pointer/ref-like parameter | build fails |
| `ref`/`ref readonly` return | build fails with `CJC005` |
| Inaccessible/ErrorType root | build fails |
| Same-project ordinary SG-only type | fails before `CoreCompile` with actionable diagnostic |
| Surface uses only implicit `Task`/`CancellationToken`/collection usings | binds after the generated Global Usings file enters `@(Compile)` |
| Manifest accessibility omitted | internal class and members |
| Manifest accessibility `Public` | separate assembly can consume the generated sets |
| Public manifest collections | immutable instances; no mutable downcast path |
| Invalid manifest accessibility | build fails |
| No marked context | deterministic empty output replaces stale file |
| Input unchanged | target skips or writer preserves timestamp |
| Source removed | input manifest changes and stale root disappears |
| Multi-TFM | independent manifest/output per TFM |
| Design-time after formal build | reuses existing obj output |
| Direct package consumer | task runs |
| Transitive package consumer | task does not run |
| Repository and package transports both active | build fails before the first custom task invocation, write, stamp update, or Compile inclusion |
| Task-owned path escapes `IntermediateOutputPath` | build fails with `CJC012` before touching the candidate path |
| Same targets imported twice | one guarded effective target set; never double generation or double Compile inclusion |
| Runtime/publish output | no task or Roslyn assemblies |

## 14. Invariants

1. STJ Source Generator is the only `JsonTypeInfo` implementation authority.
2. Every generated root is traceable to an explicit marked interface method.
3. Every non-surface direct root remains a handwritten, reviewable Extra.
4. Nested member metadata is not promoted to direct-root ownership.
5. The same semantic input produces byte-identical generated source.
6. Generated source exists only under the intermediate output directory.
7. Formal generation failure stops the build.
8. No runtime reflection, assembly scan, or resolver fallback is introduced.
9. Root inference has no BCL/CrestCreates namespace policy exceptions.
10. All business parameters participate unless explicitly excluded by exact
    type.
11. Interface inheritance is recursive and deterministic.
12. File-system and reflection enumeration order cannot affect output.
13. Input deletion invalidates incremental generation.
14. Multi-TFM input, output, and cache state are isolated.
15. Design-time output is advisory; formal build/test/publish is authoritative.
16. Task and Roslyn dependencies never enter runtime or publish outputs.
17. The build package is direct opt-in and does not flow generation
    transitively.
18. Agent/MCP contributor order, module opt-in, Binding Root ownership, and
    resolver freezing are unchanged.
19. Control Plane JSON wire naming and null behavior are unchanged.
20. A NativeAOT claim requires native link and execution of the original
    published binary.
21. Semantic discovery runs after all SDK-generated compile inputs required to
    bind ordinary handwritten source, including `GenerateGlobalUsings`, and
    analyzes the actual resulting `@(Compile)` item.
22. Root manifests are internal by default; public cross-assembly visibility is
    an explicit context-project opt-in, and Control Plane remains internal in
    #58.
23. One consumer inner build has exactly one task transport, one selected task
    assembly path, and one effective targets set.
24. Every task-owned output, stamp, and temporary path is boundary-contained by
    the normalized intermediate output root and validated before side effects.
25. Public manifest sets expose immutable backing instances.
26. Transport conflict validation precedes the first custom task invocation;
    it does not claim to prevent evaluation-time `UsingTask` declarations.

## 15. Implementation slices

### Slice 0 — Acceptance skeleton

- add the task/unit test project;
- add isolated MSBuild fixture infrastructure;
- add package fixture without a task ProjectReference;
- add Control Plane AOT fixture skeleton;
- wire implementation and test projects into `CrestCreates.slnx`,
  `solutions/CrestCreates.All.slnx`, and the relevant Tooling/Runtime layered
  solutions;
- first prove that an ordinary pre-CoreCompile `.g.cs` is consumed by STJ in
  the same compilation.

### Slice 1 — Public declaration and pure semantic model

- add `JsonContractSurfaceAttribute`;
- implement context and surface discovery;
- implement interface traversal;
- implement parameter/return normalization;
- implement explicit-root deduplication;
- implement diagnostics;
- keep the model independent from MSBuild task types where practical.

### Slice 2 — Deterministic writer and manifest

- emit project-level partial contexts;
- emit provenance comments;
- emit Surface/Explicit/All root sets;
- implement byte-stable write-if-changed;
- cover multiple contexts, empty output, and Internal/Public manifest modes.

### Slice 3 — MSBuild and NuGet integration

- implement input manifest writer;
- implement `Inputs`/`Outputs`;
- order semantic input capture after `GenerateGlobalUsings`;
- add multi-TFM and design-time conditions;
- add repository/package transport and duplicate-import guards;
- add clean behavior;
- pack task dependencies and `.deps.json`;
- pass the local-feed first-build fixture;
- prove no runtime/publish leakage.

### Slice 4 — Control Plane migration

- apply both surface attributes;
- keep the Control Plane generated manifest Internal;
- retain reviewed explicit Extras;
- remove transitive handwritten registrations;
- generate the real root manifest;
- simplify `ToolContractCoverageTests`;
- retain DTO boundary and semantic round-trip tests;
- keep serializer options unchanged.

### Slice 5 — NativeAOT and closure review

- publish and run the linux-x64 native fixture;
- run clean/rebuild/incremental/source-deletion/multi-TFM gates;
- audit all direct context usages for missing Extras;
- verify Agent/MCP JSON composition behavior remains unchanged;
- update `memory.md` only after the design is approved and implementation is
  complete.

## 16. Exit criteria

Issue #58 is complete only when:

- adding a Control Plane tool requires changing its DTO/interface contract but
  not the serializer context;
- a clean first build generates direct roots and STJ consumes them in that same
  compilation;
- surface signatures that rely only on SDK Global Usings bind identically in
  the task and formal compilation;
- `AgentToolResult<string>` and multiple-parameter methods need no special
  cases;
- handwritten context entries contain only reviewed Explicit Extras and STJ
  options;
- no public-record assembly scan, known exclusion list, or handwritten
  supporting-type list defines root correctness;
- source deletion removes stale roots;
- unchanged inputs do not rewrite output;
- multi-TFM outputs are isolated;
- Control Plane emits an Internal manifest while the reusable writer proves an
  explicit Public mode for future separate contributor assemblies;
- public manifest sets expose immutable backing instances;
- by-ref and by-ref-readonly returns fail closed;
- generated source, input manifest, stamp, and temporary paths cannot escape
  normalized `IntermediateOutputPath`;
- repository and package transports cannot both become active in one inner
  build, conflict failure precedes every custom task invocation/side effect,
  and duplicate imports cannot run or include generation twice;
- custom tasks execute through the .NET TaskHost with `Runtime="NET"` and
  `Architecture="*"`;
- a real local NuGet feed consumer restores and builds successfully;
- task/Roslyn dependencies do not leak into runtime or publish output;
- the Control Plane context uses no reflection fallback;
- the native fixture completes publish, native link, and execution;
- Phase 8d+/8c+ contributor and Binding Root semantics do not change;
- representative existing Control Plane JSON shapes round-trip without
  unintended wire changes.

## 17. Reference evidence

- [Roslyn Source Generator cookbook: generators are unordered and cannot see
  files created by other generators](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md)
- [System.Text.Json source generation:
  `JsonSerializerContext`, `JsonSerializable`, and transitive member
  metadata](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
- [MSBuild incremental builds: `Inputs`, `Outputs`, and the fact that current
  item-list changes are not remembered automatically](https://learn.microsoft.com/en-us/visualstudio/msbuild/incremental-builds)
- [.NET SDK `GenerateGlobalUsings` target: runs after `PrepareForBuild` and
  adds the generated Global Usings file to
  `@(Compile)`](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.GenerateGlobalUsings.targets)
- [NuGet PackageReference build and `buildTransitive` asset
  behavior](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)
