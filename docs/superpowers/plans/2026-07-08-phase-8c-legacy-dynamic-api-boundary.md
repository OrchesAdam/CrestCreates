# Phase 8c — Implementation Plan

**Date**: 2026-07-08
**Spec**: `docs/superpowers/specs/2026-07-08-phase-8c-legacy-dynamic-api-boundary-design.md`
**Parent Issue**: #21

---

## Dependency Graph

```text
PR-A (XML docs) ─────────────────────────────────────────┐
PR-B (Boundary tests) ───────────────────────────────────┤ depends on PR-A (XML docs must not contain forbidden symbols)
PR-C (EndpointId/EndpointVersion) ───────────────────────┤ independent
PR-D (TargetProperty + CEP018/CEP019) ──────────────────┤ independent
PR-E (CEP013 Error + Dictionary removal) ───────────────┤ independent
PR-F (DynamicApiSourceGenerator recycle + test rename) ──┤ independent
PR-G (VersionSelectionMode docs + BindingRegistry note) ─┘ independent

Build verification depends on ALL PRs.
```

PR-C, PR-D, PR-E, PR-F, PR-G are fully independent and can run in parallel.
PR-B depends on PR-A because boundary tests scan source files for forbidden symbols, and XML docs must not contain those symbols.

---

## PR-A: Legacy Boundary Documentation

### Scope
Add XML docs to 6 legacy public APIs marking them as compatibility-only. Create architecture note in docs.

### Files to modify

1. **`src/Framework/Api/CrestCreates.DynamicApi/DynamicApiExtensions.cs`**
   - Add XML doc to `AddCrestDynamicApi`:
     ```csharp
     /// <summary>
     /// Legacy AppService-oriented HTTP exposure path.
     /// This API is kept for AppService compatibility.
     /// New HTTP exposure should use the Capability-first endpoint projection path.
     /// Do not extend this path with Capability runtime, topology, activation,
     /// agent authoring, or MCP projection semantics.
     /// </summary>
     ```
   - Add XML doc to `MapCrestDynamicApi`:
     ```csharp
     /// <summary>
     /// Legacy AppService-oriented HTTP endpoint mapping.
     /// This API is kept for AppService compatibility.
     /// New HTTP endpoint mapping should use the Capability-first endpoint projection path.
     /// Do not extend this path with Capability runtime, topology, activation,
     /// agent authoring, or MCP projection semantics.
     /// </summary>
     ```

2. **`src/Framework/Api/CrestCreates.DynamicApi/DynamicApiGeneratedRegistryStore.cs`**
   - Add XML doc to class:
     ```csharp
     /// <summary>
     /// Legacy static registry for AppService-oriented Dynamic API generated providers.
     /// New Capability Endpoint projection uses its own generated binding registry.
     /// </summary>
     ```

3. **`src/Framework/Api/CrestCreates.DynamicApi/DynamicApiGeneratedRuntime.cs`**
   - Add XML doc to class:
     ```csharp
     /// <summary>
     /// Legacy runtime helpers for AppService-oriented Dynamic API endpoints.
     /// New Capability Endpoint projection uses its own endpoint JSON binding runtime.
     /// </summary>
     ```

4. **`src/Framework/Api/CrestCreates.DynamicApi/IDynamicApiGeneratedProvider.cs`**
   - Add XML doc to interface:
     ```csharp
     /// <summary>
     /// Legacy provider interface for AppService-oriented Dynamic API generated endpoints.
     /// New Capability Endpoint projection uses ICapabilityEndpointDescriptorProvider.
     /// </summary>
     ```

5. **`src/Framework/Api/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointDescriptor.cs`**
   - Add XML doc to record:
     ```csharp
     /// <summary>
     /// Legacy endpoint descriptor for AppService-oriented Dynamic API.
     /// New Capability Endpoint projection uses CapabilityEndpointDescriptor.
     /// </summary>
     ```

6. **`src/Framework/Api/CrestCreates.DynamicApi/DynamicApiServiceDescriptor.cs`**
   - Add XML doc:
     ```csharp
     /// <summary>
     /// Legacy service descriptor for AppService-oriented Dynamic API.
     /// New Capability Endpoint projection uses CapabilityEndpointDescriptor.
     /// </summary>
     ```

7. **`src/Framework/Api/CrestCreates.DynamicApi/DynamicApiActionDescriptor.cs`**
   - Add XML doc:
     ```csharp
     /// <summary>
     /// Legacy action descriptor for AppService-oriented Dynamic API.
     /// New Capability Endpoint projection uses CapabilityEndpointInputBinding.
     /// </summary>
     ```

8. **`src/Framework/Web/CrestCreates.AspNetCore/AspNetCoreModuleExtensions.cs`**
   - Add XML doc to `AddCrestAspNetCoreDynamicApi` and `MapCrestAspNetCoreDynamicApi`:
     ```csharp
     /// <summary>
     /// Legacy AppService-oriented Dynamic API service registration.
     /// Delegates to AddCrestDynamicApi which is a compatibility-only path.
     /// </summary>
     ```

### Architecture note
Create `docs/superpowers/specs/2026-07-08-phase-8c-legacy-dynamic-api-boundary-architecture-note.md` with:
- DynamicApi legacy path definition
- CapabilityEndpoint mainline definition
- Forbidden extension list
- Allowed coexistence rules

### Constraints
- XML docs MUST NOT mention forbidden CapabilityEndpoint concrete symbol names (MapCrestCapabilityEndpoints, CapabilityEndpointMapper, CapabilityEndpointBindingRegistry, ICapabilityDispatcher)
- Use conceptual wording only
- No `<see cref>` to cross-assembly types that would cause XML doc warnings

---

## PR-B: Boundary Tests

### Scope
4 categories of boundary tests proving CapabilityEndpoint projection and legacy DynamicApi are isolated.

### Test project
`tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/`

### New test file
`tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointBoundaryTests.cs`

### Test cases

**5.1 Assembly reference boundary**
```csharp
[Fact]
public void DynamicApi_Abstractions_DoesNotReference_DynamicApi_Implementation()
{
    var refs = typeof(CapabilityEndpointDescriptor)
        .Assembly
        .GetReferencedAssemblies()
        .Select(x => x.Name)
        .ToArray();

    var forbiddenRefs = new[]
    {
        "CrestCreates.DynamicApi"
    };

    foreach (var forbidden in forbiddenRefs)
    {
        refs.Should().NotContain(forbidden, because: "Abstractions must not reference implementation");
    }
}
```

**5.2 Project reference boundary**
```csharp
[Fact]
public void DynamicApi_Abstractions_Csproj_DoesNotReference_DynamicApi_Csproj()
{
    var abstractionsCsproj = FindProjectFile("CrestCreates.DynamicApi.Abstractions.csproj");
    var content = File.ReadAllText(abstractionsCsproj);

    content.Should().NotContain("CrestCreates.DynamicApi.csproj",
        because: "Abstractions project must not reference implementation project");
}
```

**5.3 Legacy source does not reference CapabilityEndpoint runtime symbols**
```csharp
[Fact]
public void Legacy_DynamicApi_Source_DoesNotReference_CapabilityEndpoint_Runtime()
{
    var files = new[]
    {
        "DynamicApiExtensions.cs",
        "DynamicApiGeneratedRegistryStore.cs",
        "DynamicApiGeneratedRuntime.cs"
    };

    var forbidden = new[]
    {
        "ICapabilityDispatcher",
        "CapabilityEndpointMapper",
        "MapCrestCapabilityEndpoints",
        "CapabilityEndpointBindingRegistry"
    };

    foreach (var file in files)
    {
        var path = Path.Combine(FindRepoRoot(), "src/Framework/Api/CrestCreates.DynamicApi", file)
            ... // read and assert NotContain for each forbidden symbol
    }
}
```

**5.4 CapabilityEndpoint generated source does not emit legacy symbols**
```csharp
[Fact]
public void CapabilityEndpoint_GeneratedSource_DoesNotEmit_Legacy_Symbols()
{
    // Scan the BindingEmitter and ProviderEmitter source code (not generated output)
    // to verify they never emit ServiceType, ActionName, DynamicApiEndpointDescriptor,
    // DynamicApiServiceDescriptor, DynamicApiActionDescriptor, IDynamicApiGeneratedProvider
    var emitterFiles = new[]
    {
        "CapabilityEndpointBindingEmitter.cs",
        "CapabilityEndpointProviderEmitter.cs"
    };

    var forbidden = new[]
    {
        "ServiceType",
        "ActionName",
        "DynamicApiEndpointDescriptor",
        "DynamicApiServiceDescriptor",
        "DynamicApiActionDescriptor",
        "IDynamicApiGeneratedProvider"
    };

    // Note: "ServiceType" may appear in comments or unrelated contexts.
    // The test should check for specific patterns like "ServiceType =" or ".ServiceType"
    // to avoid false positives from using statements or comments.
}
```

**5.5 Legacy mapping path does not call CapabilityEndpoint dispatcher**
```csharp
[Fact]
public void MapCrestDynamicApi_DoesNotCall_CapabilityDispatcher()
{
    var files = new[]
    {
        "DynamicApiExtensions.cs",
        "DynamicApiGeneratedRegistryStore.cs",
        "DynamicApiGeneratedRuntime.cs"
    };

    var forbidden = new[]
    {
        "ICapabilityDispatcher",
        "CapabilityEndpointMapper",
        "MapCrestCapabilityEndpoints",
        "CapabilityEndpointBindingRegistry"
    };

    foreach (var file in files)
    {
        var path = Path.Combine(FindRepoRoot(), "src/Framework/Api/CrestCreates.DynamicApi", file);
        var content = File.ReadAllText(path);

        foreach (var symbol in forbidden)
        {
            content.Should().NotContain(symbol,
                because: $"Legacy file {file} must not reference CapabilityEndpoint runtime symbol {symbol}");
        }
    }
}
```

### Helper
Add `FindRepoRoot()` helper that walks up from test assembly location to find `.git` directory.

### Constraints
- Tests must use FluentAssertions `foreach` + `NotContain(single, because)` pattern (not collection expression overload)
- Tests must work in both `dotnet test` and IDE contexts
- No test should depend on generated source output (only scan emitter source code)

---

## PR-C: EndpointId / EndpointVersion Independent Properties

### Scope
Add `EndpointId` and `EndpointVersion` init-only properties to attributes. SG uses them for endpoint identity. Add CEP017 and CEP020 diagnostics. Update validator.

### Files to modify

1. **`src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointSpecAttribute.cs`**
   - Add properties:
     ```csharp
     public string? EndpointId { get; init; }
     public int EndpointVersion { get; init; }
     ```

2. **`src/Framework/Api/CrestCreates.DynamicApi.Abstractions/GetAttribute.cs`** (and Post, Put, Patch, Delete)
   - Add properties:
     ```csharp
     public string? EndpointId { get; init; }
     public int EndpointVersion { get; init; }
     ```

3. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointSpecModels.cs`**
   - Add to `CapabilityEndpointSpecRecord`:
     ```csharp
     public string? EndpointId { get; init; }
     public int EndpointVersion { get; init; }
     ```

4. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointGenerator.cs`**
   - In `ExtractSpecRecord`: extract `EndpointId` and `EndpointVersion` from named args
   - In `ValidateLevel1SpecDiagnostics`: add CEP017 (EndpointId contains whitespace) and CEP020 (EndpointVersion < 0)
   - In `ValidateLevel2Diagnostics`: add CEP017 and CEP020 checks

5. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointDiagnosticCodes.cs`**
   - Add:
     ```csharp
     public const string EndpointIdContainsWhitespaceValue = "CEP017";
     public const string EndpointVersionNegativeValue = "CEP020";
     ```

6. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointDiagnostics.cs`**
   - Add descriptors:
     ```csharp
     // CEP017: EndpointId contains whitespace characters
     public static readonly DiagnosticDescriptor EndpointIdContainsWhitespace = new(
         id: "CEP017",
         title: "EndpointId contains whitespace",
         messageFormat: "EndpointId '{0}' on endpoint spec '{1}' contains whitespace characters. EndpointId must be a compact identifier without spaces.",
         category: Category,
         defaultSeverity: DiagnosticSeverity.Error,
         isEnabledByDefault: true);

     // CEP020: EndpointVersion must not be negative
     public static readonly DiagnosticDescriptor EndpointVersionNegative = new(
         id: "CEP020",
         title: "EndpointVersion must not be negative",
         messageFormat: "EndpointVersion '{0}' on endpoint spec '{1}' is negative. EndpointVersion must be zero (use CapabilityVersion) or a positive integer.",
         category: Category,
         defaultSeverity: DiagnosticSeverity.Error,
         isEnabledByDefault: true);
     ```

7. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointBindingEmitter.cs`**
   - In `EmitBindingSource` (ModuleInitializer section): use resolved endpoint identity:
     ```csharp
     var endpointId = !string.IsNullOrEmpty(spec.EndpointId) ? spec.EndpointId : $"endpoint:{spec.CapabilityId}";
     var version = spec.EndpointVersion > 0 ? spec.EndpointVersion
         : (spec.CapabilityVersion > 0 ? spec.CapabilityVersion : 1);
     ```

8. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointProviderEmitter.cs`**
   - In `EmitDescriptor`: use resolved endpoint identity:
     ```csharp
     var endpointId = !string.IsNullOrEmpty(spec.EndpointId) ? spec.EndpointId : $"endpoint:{spec.CapabilityId}";
     var version = spec.EndpointVersion > 0 ? spec.EndpointVersion
         : (spec.CapabilityVersion > 0 ? spec.CapabilityVersion : 1);
     ```

9. **`src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointDescriptorValidator.cs`**
   - In `ValidateIdentity`: add whitespace check for `descriptor.Id`:
     ```csharp
     if (descriptor.Id.Any(char.IsWhiteSpace))
         AddError(issues, $"Capability endpoint '{descriptor.Id}' Id must not contain whitespace characters.");
     ```

### Identity resolution rules
```text
EndpointId:
  - null/empty → default "endpoint:{CapabilityId}"
  - non-empty → use as-is
  - contains whitespace → CEP017 Error

EndpointVersion:
  - 0 → fallback to CapabilityVersion, then 1
  - > 0 → use as-is
  - < 0 → CEP020 Error
```

### Test additions
Add to `CapabilityEndpointDiagnosticTests`:
- CEP017 fires when EndpointId = "my endpoint" (contains space)
- CEP020 fires when EndpointVersion = -1
- EndpointId = "admin-books" works (no whitespace)
- EndpointVersion = 0 falls back to CapabilityVersion

---

## PR-D: TargetProperty Separation + CEP018/CEP019

### Scope
Add `TargetProperty` init-only property to `CapabilityEndpointInputAttribute`. BindingEmitter uses it for CLR property assignment. ProviderEmitter only emits `CapabilityInputPath`. Add CEP018 and CEP019 diagnostics.

### Files to modify

1. **`src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointInputAttribute.cs`**
   - Add property:
     ```csharp
     /// <summary>
     /// CLR property name on the body DTO to assign this scalar input value to.
     /// When set, the generated binding code uses this name for property assignment.
     /// When null, the binding emitter falls back to CapabilityInputPath or PascalCase(Name).
     /// This property is source-generator-only; it does not appear in the descriptor.
     /// </summary>
     public string? TargetProperty { get; init; }
     ```

2. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointSpecModels.cs`**
   - Add to `CapabilityEndpointInputRecord`:
     ```csharp
     public string? TargetProperty { get; init; }
     ```

3. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointGenerator.cs`**
   - In `ExtractInputRecords`: extract `TargetProperty` from named args
   - In `ValidateLevel1SpecDiagnostics` and `ValidateLevel2Diagnostics`: add CEP018 and CEP019 checks

4. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointDiagnosticCodes.cs`**
   - Add:
     ```csharp
     ```csharp
     public const string TargetPropertyMissingOnBodyValue = "CEP018";
     public const string TargetPropertyInvalidIdentifierValue = "CEP019";
     ```

5. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointDiagnostics.cs`**
   - Add descriptors:
     ```csharp
     // CEP018: TargetProperty does not exist as a public settable property on the body type
     public static readonly DiagnosticDescriptor TargetPropertyMissingOnBody = new(
         id: "CEP018",
         title: "TargetProperty not found on body type",
         messageFormat: "TargetProperty '{0}' on endpoint spec '{1}' does not exist as a public settable property on body type '{2}'.",
         category: Category,
         defaultSeverity: DiagnosticSeverity.Error,
         isEnabledByDefault: true);

     // CEP019: TargetProperty is not a valid C# property identifier
     public static readonly DiagnosticDescriptor TargetPropertyInvalidIdentifier = new(
         id: "CEP019",
         title: "TargetProperty is not a valid C# identifier",
         messageFormat: "TargetProperty '{0}' on endpoint spec '{1}' is not a valid simple C# property name. Only alphanumeric names with no dots or special characters are supported.",
         category: Category,
         defaultSeverity: DiagnosticSeverity.Error,
         isEnabledByDefault: true);
     ```

6. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointBindingEmitter.cs`**
   - In `EmitScalarPropertyAssignment`: resolve property assignment name with TargetProperty priority:
     ```csharp
     var propAssignmentName = !string.IsNullOrEmpty(input.TargetProperty)
         ? input.TargetProperty
         : !string.IsNullOrEmpty(input.CapabilityInputPath)
             ? input.CapabilityInputPath
             : !string.IsNullOrEmpty(sourceKey)
                 ? char.ToUpperInvariant(sourceKey[0]) + sourceKey.Substring(1)
                 : sourceKey;
     ```

7. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointProviderEmitter.cs`**
   - In `EmitInputBindings`: continue to emit only `CapabilityInputPath` (no TargetProperty in descriptor):
     ```csharp
     var capInputPath = input.CapabilityInputPath is not null
         ? $"\"{Escape(input.CapabilityInputPath)}\""
         : "null";
     // TargetProperty is NOT emitted into the descriptor
     ```

### Diagnostic rules
```text
CEP018: TargetProperty is non-empty but the body DTO does not have a public settable property with that name.
        Only fires when a Body type is present on the same spec.
        Nested paths (e.g., "Address.City") are NOT supported — CEP019 catches dots.

CEP019: TargetProperty is non-empty but is not a valid simple C# property identifier.
        Must start with letter or underscore, contain only letters/digits/underscores.
        Dots, dashes, spaces → CEP019 Error.

CEP008: Route/body convention missing property — only for default PascalCase(token) convention.
        NOT used when TargetProperty is explicitly set.
```

### Test additions
Add to `CapabilityEndpointDiagnosticTests`:
- CEP018 fires when TargetProperty = "NonExistentProp" and body type doesn't have it
- CEP019 fires when TargetProperty = "Address.City" (contains dot)
- CEP019 fires when TargetProperty = "my-prop" (contains dash)
- TargetProperty = "ValidProp" on body type that has it → no diagnostic
- BindingEmitter generates `model.ValidProp = ...` when TargetProperty is set

---

## PR-E: CEP013 Error + Delete Dictionary Fallback

### Scope
Change CEP013 from Warning to Error. Level 1: expand trigger to all scalar-only input combinations (Route+Route, Route+Query, Query+Header, etc.). Level 2: only route tokens + explicit Input on HTTP method attribute (Level 2 does not read class-level `[CapabilityEndpointInput]`). Delete Dictionary<string, object?> fallback from BindingEmitter. Replace with fail-closed throw.

### Files to modify

1. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointDiagnostics.cs`**
   - Change CEP013 `defaultSeverity` from `DiagnosticSeverity.Warning` to `DiagnosticSeverity.Error`
   - Update message format:
     ```csharp
     messageFormat: "Endpoint spec '{0}' declares {1} scalar inputs (Route/Query/Header) without a Body or Input type. Define a Body type with settable properties for these inputs. Dictionary<string, object?> binding is not supported."
     ```

2. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointGenerator.cs`**
   - In `ValidateRouteBindingTypes`: expand CEP013 trigger to cover all scalar-only combinations:
     ```csharp
     // Count all scalar inputs (Route + Query + Header), not just route tokens
     var allScalarCount = CountScalarInputs(attr, routeTokens);
     if (bodyType is null && inputType is null && allScalarCount > 1)
     {
         builder.Add(Diagnostic.Create(
             CapabilityEndpointDiagnostics.MultipleRouteParamsWithoutBody,
             location,
             classSymbol.Name,
             allScalarCount));
     }
     ```
   - Need helper `CountScalarInputs` that counts Route tokens + explicit Query/Header inputs from attribute named args

3. **`src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/CapabilityEndpointBindingEmitter.cs`**
   - In `EmitScalarOnlyBinding`: replace Dictionary fallback with fail-closed throw:
     ```csharp
     else
     {
         // Multiple scalar params without body — fail-closed (CEP013 prevents this at compile time)
         sb.AppendLine("        throw new InvalidOperationException(");
         sb.AppendLine("            \"CEP013: Multiple scalar inputs without a body/input DTO are not supported.\");");
     }
     ```
   - Delete the entire Dictionary<string, object?> generation block (lines 261-276 in current code)

### Emitter hard constraint
After 8c, the BindingEmitter MUST NOT contain any `Dictionary<string, object?>` fallback branch.
Even if CEP013 is suppressed by build configuration, generated binding code must fail-closed or not compile.
The throw is the only allowed behavior for multi-scalar-no-body path.

### Test additions
Add to `CapabilityEndpointDiagnosticTests`:
- CEP013 Error (not Warning) for Route+Route without Body (Level 2)
- CEP013 Error for Route+Query without Body (Level 1 only — Level 2 does not read class-level `[CapabilityEndpointInput]`)
- CEP013 Error for Query+Header without Body (Level 1 only)
- CEP013 Error for Header+Header without Body (Level 1 only)
- Single scalar Route → allowed (no CEP013)
- Single scalar Query → allowed (no CEP013, Level 1)
- Body + multiple scalar inputs → allowed (no CEP013)
- Verify generated binding source does NOT contain "Dictionary<string, object?>"

---

## PR-F: DynamicApiSourceGenerator Recycle + Legacy Test Rename

### Scope
Move `DynamicApiSourceGenerator` to 99_RecycleBin. Rename/move legacy test files.

### Part 1: DynamicApiSourceGenerator recycle

1. Move `src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiSourceGenerator.cs` to `99_RecycleBin/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiSourceGenerator.cs`
2. Verify no other file references `DynamicApiSourceGenerator` class name
3. Verify `DynamicApiGenerator` directory still has `DynamicApiAotSourceGenerator.cs` (the legacy-but-active one)

### Part 2: Legacy test rename/move

**Web.Tests moves:**
```text
tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/DynamicApiExtensionsTests.cs
  → tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/Legacy/LegacyDynamicApiExtensionsTests.cs

tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/DynamicApiGeneratedRegistryStoreTests.cs
  → tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/Legacy/LegacyDynamicApiGeneratedRegistryStoreTests.cs

tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/GeneratedApiControllerAbstractionsTests.cs
  → tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/Legacy/LegacyGeneratedApiControllerAbstractionsTests.cs

tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/DynamicApiModuleTests.cs
  → tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/Legacy/LegacyDynamicApiModuleTests.cs
```

**CodeGenerator.Tests moves (only legacy generator tests):**
```text
tests/Tooling/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiAotSourceGeneratorTests.cs
  → tests/Tooling/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/Legacy/LegacyDynamicApiAotSourceGeneratorTests.cs

tests/Tooling/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiCrudMainlineTests.cs
  → tests/Tooling/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/Legacy/LegacyDynamicApiCrudMainlineTests.cs
```

**Do NOT move:**
- `CapabilityEndpointGeneratorTests.cs`
- `CapabilityEndpointDiagnosticTests.cs`
- `CapabilityEndpointSpecNormalizer*` tests
- Any test in a `CapabilityEndpoint*` directory

### Class rename
Each moved test file should also rename its class:
- `DynamicApiExtensionsTests` → `LegacyDynamicApiExtensionsTests`
- `DynamicApiGeneratedRegistryStoreTests` → `LegacyDynamicApiGeneratedRegistryStoreTests`
- etc.

---

## PR-G: VersionSelectionMode Documentation + BindingRegistry Lifecycle Note

### Scope
Documentation-only changes. No code changes.

### Files to modify

1. **`src/Metadata/CrestCreates.Metadata.Abstractions/VersionSelectionMode.cs`**
   - Add XML doc to `Latest`:
     ```csharp
     /// <summary>
     /// Resolves to the latest active version of the referenced descriptor.
     /// At runtime, inactive versions are excluded from resolution.
     /// </summary>
     Latest,
     ```

2. **`docs/superpowers/specs/2026-07-08-phase-8c-legacy-dynamic-api-boundary-architecture-note.md`**
   - Add section 10 (BindingRegistry lifecycle boundary):
     ```text
     ## 10. BindingRegistry Lifecycle Boundary

     CapabilityEndpointBindingRegistry is a process-wide generated registry.
     It is populated by ModuleInitializer calls at assembly load time.
     It does not support runtime unload, reload, or hot projection.
     Dynamic rebuilding of the binding registry is deferred to a future phase.
     ```

---

## Execution Order

### Phase 1: Parallel (PR-C, PR-D, PR-E, PR-F, PR-G)
These are fully independent. Dispatch 5 @fixer agents in parallel.

### Phase 2: Sequential (PR-A → PR-B)
PR-A must complete before PR-B because boundary tests scan source files for forbidden symbols.

### Phase 3: Build verification
After all PRs, run:
```bash
dotnet build
dotnet test --filter "FullyQualifiedName~CapabilityEndpoint"
dotnet test --filter "FullyQualifiedName~Legacy"
dotnet test --filter "FullyQualifiedName~Boundary"
```

---

## Acceptance Criteria Mapping

| AC # | PR | Description |
|------|-----|-------------|
| 1 | PR-A | Legacy path public APIs have XML docs marking compatibility-only; conceptual wording, no forbidden symbols; no cross-assembly `<see cref>` warnings |
| 2 | PR-A | Architecture note in docs |
| 3 | PR-B | Assembly reference boundary test passes |
| 4 | PR-B | Project reference boundary test passes |
| 5 | PR-B | Legacy source symbol boundary test passes |
| 6 | PR-B | CapabilityEndpoint generated source boundary test passes |
| 7 | PR-B | Legacy mapping path boundary test passes |
| 8 | PR-C | CapabilityEndpointSpecAttribute has EndpointId/EndpointVersion |
| 9 | PR-C | Get/Post/Put/Delete/Patch attributes have EndpointId/EndpointVersion |
| 10 | PR-C | SG uses resolved endpoint identity everywhere |
| 11 | PR-C | CEP017 diagnostic fires for whitespace EndpointId |
| 12 | PR-C | CEP020 diagnostic fires for negative EndpointVersion |
| 13 | PR-C | Validator checks Id whitespace |
| 14 | PR-D | CapabilityEndpointInputAttribute has TargetProperty |
| 15 | PR-D | BindingEmitter uses TargetProperty for CLR assignment |
| 16 | PR-D | ProviderEmitter only emits CapabilityInputPath |
| 17 | PR-D | CEP018 diagnostic fires for missing TargetProperty on body |
| 18 | PR-D | CEP019 diagnostic fires for invalid TargetProperty identifier |
| 19 | PR-E | CEP013 is Error severity |
| 20 | PR-E | CEP013 covers Route/Query/Header scalar-only combinations |
| 21 | PR-E | Dictionary<string,object?> fallback deleted from BindingEmitter |
| 22 | PR-E | Multi-scalar-no-body generates fail-closed throw |
| 23 | PR-A | AddCrestDynamicApi/MapCrestDynamicApi not marked [Obsolete] |
| 24 | PR-F | DynamicApiSourceGenerator moved to 99_RecycleBin |
| 25 | PR-F | Legacy test files renamed with Legacy prefix |
| 26 | PR-G | BindingRegistry lifecycle boundary documented |
| 27 | PR-C | CEP020 diagnostic verified |
| 28 | PR-E | Emitter contains no Dictionary fallback branch |
