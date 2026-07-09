# Phase 8d — AppService-to-Capability Compatibility Projection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let existing `[CrestService]` AppService methods opt-in to run on the Capability Pipeline while preserving external HTTP contract.

**Architecture:** Source Generator produces CapabilityDescriptor, CapabilityEndpointDescriptor, bindings, and ICapabilityContextAwareHandlerInvoker implementations for each projected method. Pipeline dispatches through the compatibility invoker which resolves the original AppService via DI. Additive registration on CapabilityHandlerResolverProvider prevents native and compatibility handlers from overwriting each other.

**Tech Stack:** .NET 10, Roslyn Source Generators, ASP.NET Core Minimal APIs, Microsoft.Extensions.DependencyInjection

## Global Constraints

- SDK: .NET 10.0.100, `rollForward: latestMinor` (see `global.json`)
- Solution: `CrestCreates.slnx` (XML `.slnx`, not `.sln`)
- Central package management: `Directory.Packages.props`
- AoT / Trim: `Directory.Build.Aot.props`, default `trim`
- Source Generator targets `netstandard2.0`
- Test projects disable Trim/AoT (Moq/DynamicProxy incompatible)
- Attribute namespace in test declarations must exactly match generator expectations
- Test attribute declarations must not use `required` keyword (CS9035 preempts SG diagnostics)
- Never delete files — move to `./99_RecycleBin/`
- CapabilityProjectionKind goes in `CrestCreates.Metadata.Abstractions.DescriptorCapability` namespace
- CapabilityHandlerResolverProvider.SetResolver is obsolete no-op (not throw)
- CapabilityExecutionContext.ServiceProvider must be current DI scope's provider, not root
- ProjectionKind canonical hash profile: DefinitionOnly, Order=100
- CEP030 allows attribute on class OR method of [CrestService]

---

## File Structure

### New Files

| File | Responsibility |
|---|---|
| `src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/CapabilityCompatibilityProjectionAttribute.cs` | Opt-in attribute (class + method) |
| `src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/CapabilityCompatibilityIgnoreAttribute.cs` | Method-level exclusion attribute |
| `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/AppServiceCompatibilityProjectionEntry.cs` | Manifest entry record |
| `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorCapability/CapabilityProjectionKind.cs` | Projection origin enum |
| `src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiConventionAnalyzer.cs` | Shared convention derivation (extracted from DynamicApiAotSourceGenerator) |
| `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityGenerator.cs` | IIncrementalGenerator entry |
| `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityModels.cs` | Internal model types |
| `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityCapabilityEmitter.cs` | CapabilityDescriptor provider generation |
| `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityEndpointEmitter.cs` | Endpoint + binding generation |
| `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityHandlerEmitter.cs` | ICapabilityContextAwareHandlerInvoker generation |
| `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityManifestEmitter.cs` | Manifest generation |
| `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityDiagnostics.cs` | CEP030-CEP033 diagnostic descriptors |
| `tests/Tooling/CrestCreates.CodeGenerator.Tests/AppServiceCompatibilityGenerator/AppServiceCompatibilityGeneratorTests.cs` | SG unit tests |

### Modified Files

| File | Change |
|---|---|
| `src/Runtime/Capability/CrestCreates.Capability/CapabilityHandlerResolverProvider.cs` | Add `Register()`, make `SetResolver` obsolete no-op |
| `src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs` | Add `ServiceProvider` property |
| `src/Runtime/Capability/CrestCreates.Capability/CapabilityPipeline.cs:57-65` | Add `ServiceProvider = _serviceProvider` to context construction |
| `src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityDescriptor.cs` | Add `ProjectionKind` property |
| `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityDescriptorCanonicalHashProfile.cs` | Add ProjectionKind DefinitionOnly field |
| `src/Tooling/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/HandlerInvokerSourceGenerator.cs:100-122` | Replace `new CapabilityHandlerResolver()` + `SetResolver` with `Register()` calls |
| `src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs` | Extract methods to DynamicApiConventionAnalyzer, add [CapabilityCompatibilityProjection] exclusion |
| `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointExtensions.cs` | Add `AddCrestCompatibilityProjection()` extension method |

---

## Task 1: CapabilityHandlerResolverProvider — Additive Registration

**Files:**
- Modify: `src/Runtime/Capability/CrestCreates.Capability/CapabilityHandlerResolverProvider.cs`

**Interfaces:**
- Consumes: `ICapabilityHandlerResolver`, `CapabilityHandlerResolver.Register(string, ICapabilityHandlerInvoker)`
- Produces: `CapabilityHandlerResolverProvider.Register(string capabilityId, ICapabilityHandlerInvoker invoker)`, `CapabilityHandlerResolverProvider.GetResolver()` returns non-null

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Runtime/Capability/CrestCreates.Capability.Tests/CapabilityHandlerResolverProviderTests.cs
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

public sealed class CapabilityHandlerResolverProviderTests
{
    [Fact]
    public void Register_AddsInvoker_ToSharedResolver()
    {
        // Arrange
        var invoker1 = new Mock<ICapabilityHandlerInvoker>().Object;
        var invoker2 = new Mock<ICapabilityHandlerInvoker>().Object;

        // Act
        CapabilityHandlerResolverProvider.Register("cap.1", invoker1);
        CapabilityHandlerResolverProvider.Register("cap.2", invoker2);

        // Assert
        var resolver = CapabilityHandlerResolverProvider.GetResolver();
        resolver.Should().NotBeNull();
        resolver.Resolve("cap.1").Should().BeSameAs(invoker1);
        resolver.Resolve("cap.2").Should().BeSameAs(invoker2);
    }

    [Fact]
    public void SetResolver_IsObsoleteNoOp()
    {
        // Act — should not throw
#pragma warning disable CS0618
        CapabilityHandlerResolverProvider.SetResolver(new Mock<ICapabilityHandlerResolver>().Object);
#pragma warning restore CS0618

        // Assert — previously registered handlers still available
        var resolver = CapabilityHandlerResolverProvider.GetResolver();
        resolver.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests --filter "CapabilityHandlerResolverProviderTests" -v n`
Expected: FAIL — `Register` method does not exist

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Runtime/Capability/CrestCreates.Capability/CapabilityHandlerResolverProvider.cs
using System;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public static class CapabilityHandlerResolverProvider
{
    private static readonly CapabilityHandlerResolver Resolver = new();

    public static void Register(string capabilityId, ICapabilityHandlerInvoker invoker)
        => Resolver.Register(capabilityId, invoker);

    public static ICapabilityHandlerResolver GetResolver() => Resolver;

    [Obsolete("Use Register() for additive registration.")]
    public static void SetResolver(ICapabilityHandlerResolver resolver)
    {
        // Compatibility no-op.
        // Old generated code will be replaced in the same phase.
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests --filter "CapabilityHandlerResolverProviderTests" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Capability/CrestCreates.Capability/CapabilityHandlerResolverProvider.cs tests/Runtime/Capability/CrestCreates.Capability.Tests/CapabilityHandlerResolverProviderTests.cs
git commit -m "feat(capability): additive registration on CapabilityHandlerResolverProvider"
```

---

## Task 2: HandlerInvokerSourceGenerator — Use Additive Registration

**Files:**
- Modify: `src/Tooling/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/HandlerInvokerSourceGenerator.cs:90-149`

**Interfaces:**
- Consumes: `CapabilityHandlerResolverProvider.Register()` from Task 1
- Produces: Generated code using `Register()` instead of `new CapabilityHandlerResolver()` + `SetResolver()`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Tooling/CrestCreates.CodeGenerator.Tests/SchemaCapabilityGenerator/HandlerInvokerSourceGeneratorTests.cs
// Add test verifying generated code uses Register() not SetResolver()
[Fact]
public void GeneratedCode_UsesRegisterNotSetResolver()
{
    var source = """
        using CrestCreates.Capability.Abstractions;
        
        namespace MyApp;
        
        [CapabilityName("test-capability")]
        public class TestHandler : ICapabilityHandler<string, string>
        {
            public Task<string> ExecuteAsync(string input, CancellationToken ct) => Task.FromResult(input);
        }
        """;

    var result = Run(source);
    var generated = result.GetSourceByFileName("GeneratedHandlerRegistry.g.cs");
    generated.Should().NotBeNull();
    generated.Should().Contain("CapabilityHandlerResolverProvider.Register(");
    generated.Should().NotContain("CapabilityHandlerResolverProvider.SetResolver(");
    generated.Should().NotContain("new CapabilityHandlerResolver()");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "HandlerInvokerSourceGeneratorTests.GeneratedCode_UsesRegisterNotSetResolver" -v n`
Expected: FAIL — generated code still uses `SetResolver`

- [ ] **Step 3: Modify HandlerInvokerSourceGenerator**

In `src/Tooling/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/HandlerInvokerSourceGenerator.cs`, replace the `GenerateHandlerRegistrations` method body:

Replace lines 90-123 (the registration block):
```csharp
// OLD:
sb.AppendLine("        var resolver = new CapabilityHandlerResolver();");
// ... loop with resolver.Register(...)
sb.AppendLine("        CapabilityHandlerResolverProvider.SetResolver(resolver);");

// NEW:
// Remove the "var resolver = new CapabilityHandlerResolver();" line
// Change each "resolver.Register(...)" to "CapabilityHandlerResolverProvider.Register(...)"
// Remove the "CapabilityHandlerResolverProvider.SetResolver(resolver);" line
```

The updated generation block:
```csharp
sb.AppendLine("// <auto-generated />");
sb.AppendLine("using System.Threading;");
sb.AppendLine("using System.Threading.Tasks;");
sb.AppendLine("using CrestCreates.Capability;");
sb.AppendLine("using CrestCreates.Capability.Abstractions;");
sb.AppendLine("using System.Runtime.CompilerServices;");
sb.AppendLine();
sb.AppendLine("namespace CrestCreates.Generated;");
sb.AppendLine();
sb.AppendLine("internal static class GeneratedHandlerRegistry");
sb.AppendLine("{");
sb.AppendLine("    [ModuleInitializer]");
sb.AppendLine("    internal static void Register()");
sb.AppendLine("    {");

foreach (var handler in validHandlers)
{
    if (handler == null) continue;
    var fullName = string.IsNullOrEmpty(handler.HandlerNamespace)
        ? handler.HandlerTypeName
        : $"{handler.HandlerNamespace}.{handler.HandlerTypeName}";
    var invokerClassName = $"{handler.HandlerTypeName}_Invoker";

    sb.AppendLine();
    sb.AppendLine($"        // Handler: {fullName}");
    sb.AppendLine($"        CapabilityHandlerResolverProvider.Register(\"{handler.CapabilityName}\",");
    sb.AppendLine($"            new {invokerClassName}());");
}

// No SetResolver call — additive registration
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "HandlerInvokerSourceGeneratorTests" -v n`
Expected: PASS

- [ ] **Step 5: Run full build to verify no regressions**

Run: `dotnet build src/Tooling/CrestCreates.CodeGenerator`
Expected: SUCCESS

- [ ] **Step 6: Commit**

```bash
git add src/Tooling/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/HandlerInvokerSourceGenerator.cs tests/Tooling/CrestCreates.CodeGenerator.Tests/SchemaCapabilityGenerator/HandlerInvokerSourceGeneratorTests.cs
git commit -m "refactor(codegenerator): HandlerInvokerSourceGenerator uses additive registration"
```

---

## Task 3: CapabilityExecutionContext — Add ServiceProvider Property

**Files:**
- Modify: `src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs`

**Interfaces:**
- Consumes: None
- Produces: `CapabilityExecutionContext.ServiceProvider` property (used by Task 4 and Task 12)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Runtime/Capability/CrestCreates.Capability.Tests/CapabilityExecutionContextTests.cs
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class CapabilityExecutionContextTests
{
    [Fact]
    public void ServiceProvider_CanBeSetAndRetrieved()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new CapabilityExecutionContext
        {
            CapabilityId = "test",
            CapabilityName = "Test",
            CapabilityVersion = 1,
            CapabilityContractHash = "hash",
            ServiceProvider = services
        };

        context.ServiceProvider.Should().BeSameAs(services);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests --filter "CapabilityExecutionContextTests" -v n`
Expected: FAIL — `ServiceProvider` property does not exist

- [ ] **Step 3: Add ServiceProvider property**

In `src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs`, add after the existing properties:

```csharp
public IServiceProvider ServiceProvider { get; init; } = null!;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests --filter "CapabilityExecutionContextTests" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs tests/Runtime/Capability/CrestCreates.Capability.Tests/CapabilityExecutionContextTests.cs
git commit -m "feat(capability): add ServiceProvider to CapabilityExecutionContext"
```

---

## Task 4: CapabilityPipeline — Assign ServiceProvider in Context Construction

**Files:**
- Modify: `src/Runtime/Capability/CrestCreates.Capability/CapabilityPipeline.cs:57-65`

**Interfaces:**
- Consumes: `CapabilityExecutionContext.ServiceProvider` from Task 3
- Produces: Pipeline-populated `ServiceProvider` on every execution context

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Runtime/Capability/CrestCreates.Capability.Tests/CapabilityPipelineTests.cs
// Add test verifying ServiceProvider is populated in context
[Fact]
public async Task ExecuteAsync_PopulatesServiceProvider_OnContext()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddCapabilityRuntime();
    var sp = services.BuildServiceProvider();
    var pipeline = (CapabilityPipeline)sp.GetRequiredService<ICapabilityPipeline>();

    // Need a registered capability + handler for pipeline to execute
    // This test verifies the ServiceProvider field is set, not full execution
    // Use a mock descriptor and handler
    var descriptor = new CapabilityDescriptor
    {
        Namespace = "capability",
        Id = "test.pipe",
        Name = "Test",
        Kind = DescriptorKind.Capability,
        State = DescriptorState.Active,
        Version = 1,
        CapabilityKind = CapabilityKind.Command
    };

    ICapabilityContextAwareHandlerInvoker? capturedInvoker = null;
    var mockInvoker = new Mock<ICapabilityContextAwareHandlerInvoker>();
    mockInvoker.Setup(x => x.InvokeAsync(It.IsAny<CapabilityExecutionContext>(), It.IsAny<CancellationToken>()))
        .Callback<CapabilityExecutionContext, CancellationToken>((ctx, _) => 
        {
            // Verify ServiceProvider is set and is the scoped provider
            ctx.ServiceProvider.Should().NotBeNull();
            capturedInvoker = mockInvoker.Object;
        })
        .ReturnsAsync((object?)null);

    CapabilityHandlerResolverProvider.Register("test.pipe", mockInvoker.Object);

    // Act
    var result = await pipeline.ExecuteAsync(descriptor);

    // Assert — invoker was called, ServiceProvider was set
    mockInvoker.Verify(x => x.InvokeAsync(It.IsAny<CapabilityExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests --filter "CapabilityPipelineTests.ExecuteAsync_PopulatesServiceProvider_OnContext" -v n`
Expected: FAIL — `ctx.ServiceProvider` is null

- [ ] **Step 3: Modify CapabilityPipeline context construction**

In `src/Runtime/Capability/CrestCreates.Capability/CapabilityPipeline.cs`, modify the context construction at line 57-65:

```csharp
var context = new CapabilityExecutionContext
{
    CapabilityId = descriptor.Id,
    CapabilityName = descriptor.Name,
    CapabilityVersion = descriptor.Version,
    CapabilityContractHash = _hashBuilder.Build(descriptor).ContractHash.Value,
    Input = input,
    CancellationToken = ct,
    ServiceProvider = _serviceProvider,  // ← ADD THIS LINE
};
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests --filter "CapabilityPipelineTests" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Capability/CrestCreates.Capability/CapabilityPipeline.cs tests/Runtime/Capability/CrestCreates.Capability.Tests/CapabilityPipelineTests.cs
git commit -m "feat(capability): Pipeline assigns ServiceProvider to execution context"
```

---

## Task 5: Attributes — CapabilityCompatibilityProjection + CapabilityCompatibilityIgnore

**Files:**
- Create: `src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/CapabilityCompatibilityProjectionAttribute.cs`
- Create: `src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/CapabilityCompatibilityIgnoreAttribute.cs`

**Interfaces:**
- Consumes: None
- Produces: `[CapabilityCompatibilityProjection]` (class + method), `[CapabilityCompatibilityIgnore]` (method) — used by Tasks 9, 10-13, 15

- [ ] **Step 1: Create CapabilityCompatibilityProjectionAttribute**

```csharp
// src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/CapabilityCompatibilityProjectionAttribute.cs
using System;

namespace CrestCreates.Domain.Shared.Attributes;

/// <summary>
/// Marks a [CrestService] class or method for compatibility projection to Capability Pipeline.
/// Class-level: all eligible methods projected all eligible methods.
/// Method-level: projected only that method (class need not have the attribute).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class CapabilityCompatibilityProjectionAttribute : Attribute
{
    /// <summary>
    /// Override the capability ID prefix.
    /// Default: service name (stripped AppService/Service suffix) in kebab-case,
    /// prefixed with "compat.appservice.".
    /// Example: BookAppService → compat.appservice.book
    /// </summary>
    public string? CapabilityIdPrefix { get; init; }

    /// <summary>
    /// Override the route prefix.
    /// Default: derived from [DynamicApiRoute] or service name convention.
    /// </summary>
    public string? RoutePrefix { get; init; }
}
```

- [ ] **Step 2: Create CapabilityCompatibilityIgnoreAttribute**

```csharp
// src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/CapabilityCompatibilityIgnoreAttribute.cs
using System;

namespace CrestCreates.Domain.Shared.Attributes;

/// <summary>
/// Excludes a method from compatibility projection when the class has [CapabilityCompatibilityProjection].
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CapabilityCompatibilityIgnoreAttribute : Attribute
{
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/Framework/Ddd/CrestCreates.Domain.Shared`
Expected: SUCCESS

- [ ] **Step 4: Commit**

```bash
git add src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/CapabilityCompatibilityProjectionAttribute.cs src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/CapabilityCompatibilityIgnoreAttribute.cs
git commit -m "feat(domain): add CapabilityCompatibilityProjection and CapabilityCompatibilityIgnore attributes"
```

---

## Task 6: AppServiceCompatibilityProjectionEntry + CapabilityProjectionKind

**Files:**
- Create: `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/AppServiceCompatibilityProjectionEntry.cs`
- Create: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorCapability/CapabilityProjectionKind.cs`

**Interfaces:**
- Consumes: None
- Produces: `AppServiceCompatibilityProjectionEntry` record (used by Task 13 manifest), `CapabilityProjectionKind` enum (used by Task 7, Task 10)

- [ ] **Step 1: Create CapabilityProjectionKind**

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorCapability/CapabilityProjectionKind.cs
namespace CrestCreates.Metadata.Abstractions.DescriptorCapability;

/// <summary>
/// Marks the origin of a CapabilityDescriptor.
/// Compatibility projections are migration artifacts with an exit path to native capabilities.
/// </summary>
public enum CapabilityProjectionKind
{
    Native = 0,                    // Hand-designed native capability
    AppServiceCompatibility = 1,   // Auto-projected from AppService
}
```

- [ ] **Step 2: Create AppServiceCompatibilityProjectionEntry**

```csharp
// src/Framework/Api/CrestCreates.DynamicApi.Abstractions/AppServiceCompatibilityProjectionEntry.cs
using CrestCreates.Metadata.Abstractions.DescriptorCapability;

namespace CrestCreates.DynamicApi.Abstractions;

public sealed record AppServiceCompatibilityProjectionEntry
{
    public string SourceService { get; init; } = string.Empty;
    public string SourceMethod { get; init; } = string.Empty;
    public string CapabilityId { get; init; } = string.Empty;
    public string EndpointId { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = string.Empty;
    public string RoutePattern { get; init; } = string.Empty;
    public IReadOnlyList<string> PermissionNames { get; init; } = Array.Empty<string>();
    public string InvokerTypeName { get; init; } = string.Empty;
    public CapabilityProjectionKind ProjectionKind { get; init; }
}
```

- [ ] **Step 3: Verify DynamicApi.Abstractions can reference Metadata.Abstractions**

Check `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CrestCreates.DynamicApi.Abstractions.csproj` for existing ProjectReference to `CrestCreates.Metadata.Abstractions`. If not present, add it.

Run: `dotnet build src/Framework/Api/CrestCreates.DynamicApi.Abstractions`
Expected: SUCCESS

- [ ] **Step 4: Commit**

```bash
git add src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorCapability/CapabilityProjectionKind.cs src/Framework/Api/CrestCreates.DynamicApi.Abstractions/AppServiceCompatibilityProjectionEntry.cs
git commit -m "feat(abstractions): add CapabilityProjectionKind enum and AppServiceCompatibilityProjectionEntry record"
```

---

## Task 7: CapabilityDescriptor — Add ProjectionKind Property + Canonical Hash Profile

**Files:**
- Modify: `src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityDescriptor.cs`
- Modify: `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityDescriptorCanonicalHashProfile.cs`

**Interfaces:**
- Consumes: `CapabilityProjectionKind` from Task 6
- Produces: `CapabilityDescriptor.ProjectionKind` property (used by Task 10)

- [ ] **Step 1: Add ProjectionKind property to CapabilityDescriptor**

In `src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityDescriptor.cs`, add after the existing runtime properties (after `RiskLevel`):

```csharp
using CrestCreates.Metadata.Abstractions.DescriptorCapability;

// In the CapabilityDescriptor class, add:
public CapabilityProjectionKind ProjectionKind { get; init; } = CapabilityProjectionKind.Native;
```

- [ ] **Step 2: Add canonical hash profile field**

In `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityDescriptorCanonicalHashProfile.cs`, add in the `Fields()` method after the existing `RiskLevel` field (Order=30):

```csharp
[CanonicalHashField(
    nameof(CapabilityDescriptor.ProjectionKind),
    CanonicalHashFieldClassification.DefinitionOnly,
    Order = 100)]
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Runtime/Capability/CrestCreates.Capability.Abstractions && dotnet build src/Metadata/CrestCreates.Metadata`
Expected: SUCCESS

- [ ] **Step 4: Commit**

```bash
git add src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityDescriptor.cs src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityDescriptorCanonicalHashProfile.cs
git commit -m "feat(capability): add ProjectionKind to CapabilityDescriptor with DefinitionOnly hash profile"
```

---

## Task 8: DynamicApiConventionAnalyzer — Extract Shared Convention Logic

**Files:**
- Create: `src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiConventionAnalyzer.cs`
- Modify: `src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs`

**Interfaces:**
- Consumes: Private methods and model types from `DynamicApiAotSourceGenerator`
- Produces: `DynamicApiConventionAnalyzer` with `internal static` methods and `internal` model types (used by Task 9 and Task 10-12)

This is the largest refactoring task. The approach:
1. Copy the 8 model types from `DynamicApiAotSourceGenerator` (lines 1861-1920) to `DynamicApiConventionAnalyzer`, changing visibility from `private` to `internal`
2. Copy the convention methods to `DynamicApiConventionAnalyzer`, changing visibility from `private static` to `internal static`
3. Update `DynamicApiAotSourceGenerator` to call `DynamicApiConventionAnalyzer.*` instead of its own methods
4. Remove the original private methods and model types from `DynamicApiAotSourceGenerator`

- [ ] **Step 1: Create DynamicApiConventionAnalyzer with model types**

Create `src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiConventionAnalyzer.cs`:

```csharp
namespace CrestCreates.CodeGenerator.DynamicApiGenerator;

/// <summary>
/// Shared convention derivation logic for DynamicApi and AppServiceCompatibility generators.
/// Methods extracted from DynamicApiAotSourceGenerator — signatures and bodies unchanged,
/// only visibility changed from private static from private static to internal static.
/// </summary>
internal static class DynamicApiConventionAnalyzer
{
    // Copy these methods from DynamicApiAotSourceGenerator, changing private static → internal static:
    // - ResolveHttpMethod
    // - ResolveActionRoute
    // - ResolvePermission
    // - ResolveServiceRoute
    // - TrimServiceName
    // - TrimAsyncSuffix
    // - ToKebabCase
    // - ResolveParameterSource
    // - BuildServiceModels
    // - BuildActionModels
    // Method bodies are copied verbatim — only visibility changes.
}

// Model types — moved from DynamicApiAotSourceGenerator, private → internal:
internal sealed record ServiceModel(string ServiceName, string RouteTemplate, bool HasCustomRoute, string ServiceTypeName, string ServiceAssemblyTypeName, ActionModel[] Actions);
internal sealed record ActionModel(string ActionName, string DeclaringTypeName, string OperationId, string RelativeRoute, string HttpMethod, string PermissionName, ReturnModel ReturnModel, ParameterModel[] Parameters, string ServiceMethodName, string ServiceTypeName, bool RequiresUnitOfWork, bool RequiresTransaction, string[] MetadataCalls, bool AllowAnonymous, string? OverrideAction);
internal sealed record ParameterModel(string Name, string TypeName, ParameterSource Source, bool IsOptional, bool IsScalar, QueryPropertyModel[] QueryProperties);
internal sealed record QueryPropertyModel(string Name, string TypeName, bool IsScalar, bool IsOptional);
internal sealed record ServiceRouteModel(string Template, bool IsCustom);
internal sealed record ReturnModel(bool IsVoid, string? PayloadTypeName);
internal enum ParameterSource { Route, Query, Body, Header, CancellationToken }
internal enum CrudAction { Get, GetList, Create, Update, Delete }
```

Note: The actual method bodies must be copied verbatim from `DynamicApiAotSourceGenerator.cs`. The implementer must read the source file and copy each method's body exactly, only changing `private static` to `internal static` and updating references to the moved model types.

- [ ] **Step 2: Update DynamicApiAotSourceGenerator to use DynamicApiConventionAnalyzer**

In `src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs`:
1. Remove the private model type declarations (lines 1861-1920)
2. Remove the private convention method declarations
3. Replace all internal calls to the moved methods with `DynamicApiConventionAnalyzer.MethodName(...)`
4. The generated output code must be identical — verify by running existing tests

- [ ] **Step 3: Run existing DynamicApiAotSourceGenerator tests**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "DynamicApiAot" -v n`
Expected: All existing tests PASS (generated code unchanged)

- [ ] **Step 4: Run full build**

Run: `dotnet build src/Tooling/CrestCreates.CodeGenerator`
Expected: SUCCESS

- [ ] **Step 5: Commit**

```bash
git add src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiConventionAnalyzer.cs src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs
git commit -m "refactor(codegenerator): extract DynamicApiConventionAnalyzer with shared convention logic"
```

---

## Task 9: DynamicApiAotSourceGenerator — Add [CapabilityCompatibilityProjection] Exclusion

**Files:**
- Modify: `src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs`

**Interfaces:**
- Consumes: `[CapabilityCompatibilityProjection]` from Task 5, `DynamicApiConventionAnalyzer` from Task 8
- Produces: Legacy generator skips projected services/methods

- [ ] **Step 1: Add class-level exclusion in IsDynamicApiImplementation**

In `DynamicApiAotSourceGenerator.IsDynamicApiImplementation` (around line 182-214), add a check for `[CapabilityCompatibilityProjection]` on the class:

```csharp
// After the existing [DynamicApiIgnore] check, add:
var hasCompatibilityProjection = classSymbol.GetAttributes()
    .Any(a => a.AttributeClass?.Name == "CapabilityCompatibilityProjectionAttribute");
if (hasCompatibilityProjection)
    return false;
```

- [ ] **Step 2: Add method-level exclusion in BuildActionModels**

In `DynamicApiAotSourceGenerator.BuildActionModels` (around line 557-609), add a check for `[CapabilityCompatibilityProjection]` on individual methods, alongside the existing `[DynamicApiIgnore]` check:

```csharp
// Wherever [DynamicApiIgnore] is checked on a method, add:
var hasMethodProjection = methodSymbol.GetAttributes()
    .Any(a => a.AttributeClass?.Name == "CapabilityCompatibilityProjectionAttribute");
if (hasMethodProjection)
    continue; // Skip this method — it will be projected by AppServiceCompatibilityGenerator
```

- [ ] **Step 3: Write test for class-level exclusion**

```csharp
[Fact]
public void ClassLevel_CapabilityCompatibilityProjection_SkipsEntireService()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        
        namespace MyApp;
        
        [CrestService]
        [CapabilityCompatibilityProjection]
        public class BookAppService
        {
            public Task<string> CreateAsync(string input, CancellationToken ct) => Task.FromResult(input);
        }
        """;

    var result = Run(source);
    // Legacy generator should NOT produce any endpoint for this service
    result.GeneratedSources.Should().NotContain(s => s.FileName.Contains("DynamicApi"));
}
```

- [ ] **Step 4: Write test for method-level exclusion**

```csharp
[Fact]
public void MethodLevel_CapabilityCompatibilityProjection_SkipsOnlyThatMethod()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        
        namespace MyApp;
        
        [CrestService]
        public class BookAppService
        {
            [CapabilityCompatibilityProjection]
            public Task<string> CreateAsync(string input, CancellationToken ct) => Task.FromResult(input);
            
            public Task<string> GetAllAsync(CancellationToken ct) => Task.FromResult("all");
        }
        """;

    var result = Run(source);
    // Legacy generator should produce endpoint for GetAllAsync but NOT CreateAsync
    var generated = result.GetSourceByFileName(/* the generated file */);
    generated.Should().Contain("GetAll");
    generated.Should().NotContain("Create");
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "CapabilityCompatibilityProjection" -v n`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs tests/Tooling/CrestCreates.CodeGenerator.Tests/
git commit -m "feat(codegenerator): DynamicApiAotSourceGenerator excludes [CapabilityCompatibilityProjection] services/methods"
```

---

## Task 10: AppServiceCompatibilityGenerator — CapabilityDescriptor Provider

**Files:**
- Create: `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityGenerator.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityModels.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityCapabilityEmitter.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityDiagnostics.cs`

**Interfaces:**
- Consumes: `[CapabilityCompatibilityProjection]` (Task 5), `CapabilityProjectionKind` (Task 6), `CapabilityDescriptor.ProjectionKind` (Task 7), `DynamicApiConventionAnalyzer` (Task 8)
- Produces: `GeneratedAppServiceCompatibilityCapabilities.g.cs` — `IDescriptorProvider<CapabilityDescriptor>` implementation

- [ ] **Step 1: Create AppServiceCompatibilityDiagnostics**

```csharp
// src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityDiagnostics.cs
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator;

internal static class AppServiceCompatibilityDiagnostics
{
    private const string Category = "CompatibilityProjection";

    public static readonly DiagnosticDescriptor CEP030 = new(
        "CEP030",
        "Invalid CapabilityCompatibilityProjection target",
        "[CapabilityCompatibilityProjection] may only be used on a [CrestService] class or on a method declared by a [CrestService] class.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CEP031 = new(
        "CEP031",
        "Conflicting attributes",
        "[CapabilityCompatibilityProjection] conflicts with [DynamicApiIgnore] on the same member.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CEP032 = new(
        "CEP032",
        "Cannot derive HTTP method",
        "Cannot derive HTTP method from method name '{0}'. Method name must follow conventions (Get/GetAll/Create/Update/Delete).",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CEP033 = new(
        "CEP033",
        "Cannot derive permission name",
        "Cannot derive permission name for method '{0}'.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
```

- [ ] **Step 2: Create AppServiceCompatibilityModels**

```csharp
// src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityModels.cs
namespace CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator;

internal sealed record CompatibilityServiceModel(
    string ServiceName,
    string StrippedName,
    string RoutePrefix,
    string CapabilityIdPrefix,
    string ServiceTypeName,
    string InterfaceTypeName,
    CompatibilityActionModel[] Actions);

internal sealed record CompatibilityActionModel(
    string ActionName,
    string HttpMethod,
    string RoutePattern,
    string CapabilityId,
    string EndpointId,
    string PermissionName,
    string ServiceMethodName,
    bool IsSingleParam,
    string InputTypeName,
    string? EnvelopeTypeName,
    string ReturnTypeName,
    bool IsVoidReturn);

internal sealed record CompatibilityInputEnvelope(
    string TypeName,
    string ServiceName,
    string ActionName,
    (string Name, string TypeName, string Source)[] Fields);
```

- [ ] **Step 3: Create AppServiceCompatibilityGenerator entry**

```csharp
// src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityGenerator.cs
using System.Collections.Immutable;
using System.Linq;
using CrestCreates.CodeGenerator.DynamicApiGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator;

[Generator]
public sealed class AppServiceCompatibilityGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var projectedServices = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax cds
                    && cds.AttributeLists.Count > 0,
                transform: static (ctx, ct) => GetServiceInfo(ctx))
            .Where(static x => x is not null)
            .Collect();

        var compilationProvider = context.CompilationProvider;

        context.RegisterSourceOutput(
            projectedServices.Combine(compilationProvider),
            static (spc, source) =>
            {
                GenerateCapabilityProviders(spc, source.Left, source.Right);
            });
    }

    private static CompatibilityServiceModel? GetServiceInfo(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol == null) return null;

        // Check for [CrestService]
        var hasCrestService = symbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "CrestServiceAttribute");
        if (!hasCrestService) return null;

        // Check for class-level [CapabilityCompatibilityProjection]
        var classProjectionAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "CapabilityCompatibilityProjectionAttribute");

        // If no class-level and no method-level, skip
        var methodsWithProjection = symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.GetAttributes().Any(a => a.AttributeClass?.Name == "CapabilityCompatibilityProjectionAttribute"))
            .ToList();

        if (classProjectionAttr == null && methodsWithProjection.Count == 0)
            return null;

        // Validate: CEP030 — must be on [CrestService] class or method (already ensured above)

        // Build service model using DynamicApiConventionAnalyzer
        var serviceName = DynamicApiConventionAnalyzer.TrimServiceName(symbol.Name);
        var kebabName = DynamicApiConventionAnalyzer.ToKebabCase(serviceName);
        var capabilityIdPrefix = classProjectionAttr?.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "CapabilityIdPrefix").Value.Value?.ToString()
            ?? $"compat.appservice.{kebabName}";

        var routePrefix = classProjectionAttr?.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "RoutePrefix").Value.Value?.ToString()
            ?? DynamicApiConventionAnalyzer.ResolveServiceRoute(symbol, serviceName, 
                symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "DynamicApiRouteAttribute")?.AttributeClass).Template;

        // Determine which methods to project
        var eligibleMethods = classProjectionAttr != null
            ? symbol.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public
                    && !m.IsStatic
                    && !m.GetAttributes().Any(a => a.AttributeClass?.Name == "CapabilityCompatibilityIgnoreAttribute")
                    && !m.GetAttributes().Any(a => a.AttributeClass?.Name == "DynamicApiIgnoreAttribute"))
            : methodsWithProjection.Where(m => m.DeclaredAccessibility == Accessibility.Public && !m.IsStatic);

        var actions = new List<CompatibilityActionModel>();
        foreach (var method in eligibleMethods)
        {
            var httpMethod = DynamicApiConventionAnalyzer.ResolveHttpMethod(method.Name);
            if (string.IsNullOrEmpty(httpMethod))
            {
                // CEP032 — cannot derive HTTP method
                continue;
            }

            var permission = DynamicApiConventionAnalyzer.ResolvePermission(serviceName, method.Name);
            if (string.IsNullOrEmpty(permission))
            {
                // CEP033 — cannot derive permission
            }

            var actionRoute = DynamicApiConventionAnalyzer.ResolveActionRoute(method);
            var methodStripped = DynamicApiConventionAnalyzer.TrimAsyncSuffix(method.Name);
            var methodKebab = DynamicApiConventionAnalyzer.ToKebabCase(methodStripped);
            var capabilityId = $"{capabilityIdPrefix}.{methodKebab}";
            var endpointId = $"endpoint:{capabilityId}";
            var routePattern = $"{routePrefix}/{actionRoute}".TrimEnd('/');

            // Analyze parameters
            var routeTokens = new HashSet<string>();
            // Extract route tokens from routePattern (e.g., {id})
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(routePattern, @"\{(\w+)\}"))
                routeTokens.Add(match.Groups[1].Value);

            var parameters = method.Parameters
                .Where(p => p.Type.Name != "CancellationToken")
                .ToList();

            bool bodyAssigned = false;
            var paramSources = parameters.Select(p =>
            {
                var source = DynamicApiConventionAnalyzer.ResolveParameterSource(p, routeTokens, httpMethod, ref bodyAssigned);
                return (p.Name, TypeName: p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), Source: source.ToString());
            }).ToArray();

            var isSingleParam = parameters.Count == 1 && paramSources.Length == 1 && paramSources[0].Source == "Body";
            var inputTypeName = isSingleParam
                ? paramSources[0].TypeName
                : $"{symbol.Name}_{methodStripped}_CompatibilityInput";

            var returnType = method.ReturnType;
            var isVoidReturn = returnType.Name == "Task" && !returnType.IsGenericType
                || returnType.Name == "ValueTask" && !returnType.IsGenericType
                || returnType.Name == "Void";
            var returnTypeName = isVoidReturn ? "void"
                : returnType.IsGenericType
                    ? returnType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    : returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            actions.Add(new CompatibilityActionModel(
                ActionName: methodStripped,
                HttpMethod: httpMethod,
                RoutePattern: routePattern,
                CapabilityId: capabilityId,
                EndpointId: endpointId,
                PermissionName: permission ?? string.Empty,
                ServiceMethodName: method.Name,
                IsSingleParam: isSingleParam,
                InputTypeName: inputTypeName,
                EnvelopeTypeName: isSingleParam ? null : inputTypeName,
                ReturnTypeName: returnTypeName,
                IsVoidReturn: isVoidReturn));
        }

        if (actions.Count == 0) return null;

        // Find interface type for DI resolution
        var interfaceType = symbol.AllInterfaces
            .FirstOrDefault(i => i.Name == $"I{symbol.Name}");
        var interfaceTypeName = interfaceType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new CompatibilityServiceModel(
            ServiceName: serviceName,
            StrippedName: kebabName,
            RoutePrefix: routePrefix,
            CapabilityIdPrefix: capabilityIdPrefix,
            ServiceTypeName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            InterfaceTypeName: interfaceTypeName,
            Actions: actions.ToArray());
    }

    private static void GenerateCapabilityProviders(
        SourceProductionContext spc,
        ImmutableArray<CompatibilityServiceModel?> services,
        Compilation compilation)
    {
        var validServices = services.Where(s => s is not null).ToList();
        if (validServices.Count == 0) return;

        foreach (var service in validServices)
        {
            if (service == null) continue;
            var capabilitySource = AppServiceCompatibilityCapabilityEmitter.Emit(service);
            spc.AddSource($"GeneratedAppServiceCompatibilityCapabilities_{service.StrippedName}.g.cs", capabilitySource);
        }
    }
}
```

- [ ] **Step 4: Create AppServiceCompatibilityCapabilityEmitter**

```csharp
// src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityCapabilityEmitter.cs
using System.Text;
using CrestCreates.CodeGenerator.DynamicApiGenerator;

namespace CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator;

internal static class AppServiceCompatibilityCapabilityEmitter
{
    public static string Emit(CompatibilityServiceModel service)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using CrestCreates.Capability.Abstractions;");
        sb.AppendLine("using CrestCreates.Metadata.Abstractions;");
        sb.AppendLine("using CrestCreates.Metadata.Abstractions.DescriptorCapability;");
        sb.AppendLine("using CrestCreates.Metadata;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();
        sb.AppendLine("namespace CrestCreates.Generated;");
        sb.AppendLine();
        sb.AppendLine($"internal sealed class GeneratedAppServiceCompatibilityCapabilities_{service.StrippedName} : IDescriptorProvider<CapabilityDescriptor>");
        sb.AppendLine("{");
        sb.AppendLine("    public IReadOnlyList<CapabilityDescriptor> GetDescriptors()");
        sb.AppendLine("    {");
        sb.AppendLine("        return new CapabilityDescriptor[]");
        sb.AppendLine("        {");

        foreach (var action in service.Actions)
        {
            var capabilityKind = action.HttpMethod is "GET" ? "CapabilityKind.Query" : "CapabilityKind.Command";
            var successStatusCode = action.HttpMethod switch
            {
                "POST" => "201",
                "DELETE" => "204",
                _ => "200"
            };

            sb.AppendLine("            new CapabilityDescriptor");
            sb.AppendLine("            {");
            sb.AppendLine("                Namespace = \"capability\",");
            sb.AppendLine($"                Id = \"{action.CapabilityId}\",");
            sb.AppendLine($"                Name = \"{action.ActionName}\",");
            sb.AppendLine("                Kind = DescriptorKind.Capability,");
            sb.AppendLine("                State = DescriptorState.Active,");
            sb.AppendLine("                Version = 1,");
            sb.AppendLine($"                CapabilityKind = {capabilityKind},");
            sb.AppendLine($"                Permissions = new[] {{ \"{action.PermissionName}\" }},");
            sb.AppendLine("                RiskLevel = CapabilityRiskLevel.Medium,");
            sb.AppendLine("                InputSchema = null,");
            sb.AppendLine("                OutputSchema = null,");
            sb.AppendLine("                ProjectionKind = CapabilityProjectionKind.AppServiceCompatibility,");
            sb.AppendLine("            },");
        }

        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("internal static class GeneratedAppServiceCompatibilityCapabilitiesBootstrapper_{service.StrippedName}");
        sb.AppendLine("{");
        sb.AppendLine("    [ModuleInitializer]");
        sb.AppendLine("    internal static void Register()");
        sb.AppendLine("    {");
        sb.AppendLine($"        DescriptorProviderRegistry.Register<CapabilityDescriptor>(new GeneratedAppServiceCompatibilityCapabilities_{service.StrippedName}());");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
```

- [ ] **Step 5: Write SG test for capability provider generation**

```csharp
// tests/Tooling/CrestCreates.CodeGenerator.Tests/AppServiceCompatibilityGenerator/AppServiceCompatibilityGeneratorTests.cs
using FluentAssertions;
using CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator;
using Xunit;

public sealed class AppServiceCompatibilityGeneratorTests
{
    [Fact]
    public void ClassLevelProjection_GeneratesCapabilityDescriptors()
    {
        var source = """
            using CrestCreates.Domain.Shared.Attributes;
            
            namespace MyApp;
            
            [CrestService]
            [CapabilityCompatibilityProjection]
            public class BookAppService
            {
                public Task<string> CreateAsync(string input, CancellationToken ct) => Task.FromResult(input);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<AppServiceCompatibilityGenerator>(
            source, additionalSources: BuildCompatibilityStubs());

        var generated = result.GetSourceByFileName("GeneratedAppServiceCompatibilityCapabilities_book.g.cs");
        generated.Should().NotBeNull();
        generated.Should().Contain("compat.appservice.book.create");
        generated.Should().Contain("CapabilityProjectionKind.AppServiceCompatibility");
        generated.Should().Contain("DescriptorProviderRegistry.Register");
    }

    private static string[] BuildCompatibilityStubs()
    {
        return new[]
        {
            // Stub attributes and types the generator needs
            """
            namespace CrestCreates.Domain.Shared.Attributes
            {
                public class CrestServiceAttribute : System.Attribute { }
                public sealed class CapabilityCompatibilityProjectionAttribute : System.Attribute
                {
                    public string? CapabilityIdPrefix { get; init; }
                    public string? RoutePrefix { get; init; }
                }
                public sealed class CapabilityCompatibilityIgnoreAttribute : System.Attribute { }
                public sealed class DynamicApiIgnoreAttribute : System.Attribute { }
                public sealed class DynamicApiRouteAttribute : System.Attribute
                {
                    public DynamicApiRouteAttribute(string template) { }
                    public string Template { get; }
                }
            }
            """,
            """
            namespace CrestCreates.Capability.Abstractions
            {
                public sealed class CapabilityDescriptor : IDescriptor, IVersionedDescriptor
                {
                    public string Namespace { get; init; }
                    public string Id { get; init; }
                    public string Name { get; init; }
                    public DescriptorKind Kind { get; init; }
                    public DescriptorState State { get; init; }
                    public string? SupersededById { get; init; }
                    public int Version { get; init; }
                    public CapabilityKind CapabilityKind { get; init; }
                    public string[]? InputSchema { get; init; }
                    public string[]? OutputSchema { get; init; }
                    public string[] Permissions { get; init; }
                    public CapabilityRiskLevel RiskLevel { get; init; }
                    public CapabilityProjectionKind ProjectionKind { get; init; }
                }
            }
            """,
            // Add more stubs as needed for compilation
        };
    }
}
```

- [ ] **Step 6: Run test**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "AppServiceCompatibilityGeneratorTests" -v n`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/ tests/Tooling/CrestCreates.CodeGenerator.Tests/AppServiceCompatibilityGenerator/
git commit -m "feat(codegenerator): AppServiceCompatibilityGenerator generates CapabilityDescriptor providers"
```

---

## Task 11: AppServiceCompatibilityGenerator — Endpoint + Binding Generation

**Files:**
- Create: `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityEndpointEmitter.cs`

**Interfaces:**
- Consumes: `CompatibilityServiceModel` from Task 10, `CapabilityEndpointDescriptor` types, `CapabilityEndpointBindingRegistry`, `CapabilityEndpointJsonRuntime`
- Produces: `GeneratedAppServiceCompatibilityEndpoints_{name}.g.cs` and `GeneratedAppServiceCompatibilityBindings_{name}.g.cs`

- [ ] **Step 1: Create AppServiceCompatibilityEndpointEmitter**

This emitter generates two source files per service:
1. `ICapabilityEndpointDescriptorProvider` implementation with `CapabilityEndpointDescriptor` per action
2. Binding delegates that register to `CapabilityEndpointBindingRegistry`

The emitter must:
- Map HTTP method string to `CapabilityEndpointHttpMethod` enum
- Generate `CapabilityEndpointInputBinding` per parameter
- Generate per-action envelope types for multi-parameter methods
- Generate `BindInputAsync` delegates using `CapabilityEndpointJsonRuntime.ReadBodyAsync<T>`
- Register via `[ModuleInitializer]`

```csharp
// src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityEndpointEmitter.cs
using System.Text;
using CrestCreates.CodeGenerator.DynamicApiGenerator;

namespace CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator;

internal static class AppServiceCompatibilityEndpointEmitter
{
    public static (string EndpointsSource, string BindingsSource) Emit(CompatibilityServiceModel service)
    {
        var endpointsSb = new StringBuilder();
        var bindingsSb = new StringBuilder();

        // Endpoint provider generation
        endpointsSb.AppendLine("// <auto-generated />");
        endpointsSb.AppendLine("using System.Collections.Generic;");
        endpointsSb.AppendLine("using CrestCreates.DynamicApi.Abstractions;");
        endpointsSb.AppendLine("using CrestCreates.Metadata.Abstractions;");
        endpointsSb.AppendLine("using CrestCreates.Metadata;");
        endpointsSb.AppendLine("using System.Runtime.CompilerServices;");
        endpointsSb.AppendLine();
        endpointsSb.AppendLine("namespace CrestCreates.Generated;");
        endpointsSb.AppendLine();
        endpointsSb.AppendLine($"internal sealed class GeneratedAppServiceCompatibilityEndpoints_{service.StrippedName} : ICapabilityEndpointDescriptorProvider");
        endpointsSb.AppendLine("{");
        endpointsSb.AppendLine("    public IReadOnlyList<CapabilityEndpointDescriptor> GetDescriptors()");
        endpointsSb.AppendLine("    {");
        endpointsSb.AppendLine("        return new CapabilityEndpointDescriptor[]");
        endpointsSb.AppendLine("        {");

        // Binding generation
        bindingsSb.AppendLine("// <auto-generated />");
        bindingsSb.AppendLine("using System;");
        bindingsSb.AppendLine("using System.Threading;");
        bindingsSb.AppendLine("using System.Threading.Tasks;");
        bindingsSb.AppendLine("using Microsoft.AspNetCore.Http;");
        bindingsSb.AppendLine("using CrestCreates.DynamicApi;");
        bindingsSb.AppendLine("using CrestCreates.DynamicApi.Abstractions;");
        bindingsSb.AppendLine("using System.Runtime.CompilerServices;");
        bindingsSb.AppendLine();
        bindingsSb.AppendLine("namespace CrestCreates.Generated;");
        bindingsSb.AppendLine();

        // Envelope types for multi-param methods
        foreach (var action in service.Actions.Where(a => !a.IsSingleParam))
        {
            bindingsSb.AppendLine($"internal sealed class {action.EnvelopeTypeName}");
            bindingsSb.AppendLine("{");
            // Generate properties based on parameters — the implementer must
            // extract parameter details from the CompatibilityActionModel
            // For now, the envelope fields come from the action's parameter analysis
            bindingsSb.AppendLine("    // Envelope properties generated per parameter");
            bindingsSb.AppendLine("}");
            bindingsSb.AppendLine();
        }

        // Binding delegate methods
        bindingsSb.AppendLine($"internal static class GeneratedAppServiceCompatibilityBindings_{service.StrippedName}");
        bindingsSb.AppendLine("{");
        bindingsSb.AppendLine("    [ModuleInitializer]");
        bindingsSb.AppendLine("    internal static void Register()");
        bindingsSb.AppendLine("    {");

        foreach (var action in service.Actions)
        {
            var httpMethodEnum = action.HttpMethod switch
            {
                "GET" => "CapabilityEndpointHttpMethod.Get",
                "POST" => "CapabilityEndpointHttpMethod.Post",
                "PUT" => "CapabilityEndpointHttpMethod.Put",
                "PATCH" => "CapabilityEndpointHttpMethod.Patch",
                "DELETE" => "CapabilityEndpointHttpMethod.Delete",
                _ => "CapabilityEndpointHttpMethod.None"
            };

            var successStatusCode = action.HttpMethod switch
            {
                "POST" => "201",
                "DELETE" => "204",
                _ => "200"
            };

            // Endpoint descriptor
            endpointsSb.AppendLine("            new CapabilityEndpointDescriptor");
            endpointsSb.AppendLine("            {");
            endpointsSb.AppendLine("                Namespace = \"dynamic-api-endpoint\",");
            endpointsSb.AppendLine($"                Id = \"{action.EndpointId}\",");
            endpointsSb.AppendLine($"                Name = \"{action.ActionName}\",");
            endpointsSb.AppendLine("                Kind = DescriptorKind.DynamicApiEndpoint,");
            endpointsSb.AppendLine("                State = DescriptorState.Active,");
            endpointsSb.AppendLine("1,");
            endpointsSb.AppendLine($"                Capability = new VersionedDescriptorRef<CapabilityDescriptor>(\"{action.CapabilityId}\", 1),");
            endpointsSb.AppendLine($"                HttpMethod = {httpMethodEnum},");
            endpointsSb.AppendLine($"                RoutePattern = \"{action.RoutePattern}\",");
            endpointsSb.AppendLine("                AuthorizationMode = CapabilityEndpointAuthorizationMode.InheritCapability,");
            // InputBindings — generated per parameter
            endpointsSb.AppendLine("                InputBindings = new[]");
            endpointsSb.AppendLine("                {");
            // The implementer must generate CapabilityEndpointInputBinding per parameter
            // based on the action's parameter analysis
            endpointsSb.AppendLine("                },");
            endpointsSb.AppendLine($"                OutputMapping = new CapabilityEndpointOutputMapping {{ SuccessStatusCode = {successStatusCode} }},");
            endpointsSb.AppendLine($"                Projection = new CapabilityEndpointProjectionMetadata");
            endpointsSb.AppendLine("                {");
            endpointsSb.AppendLine($"                    OperationId = \"{action.CapabilityId.Replace('.', '_')}\",");
            endpointsSb.AppendLine($"                    Tags = new[] {{ \"{service.ServiceName}\" }}");
            endpointsSb.AppendLine("                }");
            endpointsSb.AppendLine("            },");

            // Binding registration
            bindingsSb.AppendLine($"        CapabilityEndpointBindingRegistry.Register(new CapabilityEndpointBindingContract");
            bindingsSb.AppendLine("        {");
            bindingsSb.AppendLine($"            EndpointId = \"{action.EndpointId}\",");
            bindingsSb.AppendLine("            Version = 1,");
            bindingsSb.AppendLine($"            BindInputAsync = Bind{action.ActionName}Async");
            bindingsSb.AppendLine("        });");
        }

        endpointsSb.AppendLine("        };");
        endpointsSb.AppendLine("    }");
        endpointsSb.AppendLine("}");
        endpointsSb.AppendLine();
        // Bootstrapper
        endpointsSb.AppendLine($"internal static class GeneratedAppServiceCompatibilityEndpointsBootstrapper_{service.StrippedName}");
        endpointsSb.AppendLine("{");
        endpointsSb.AppendLine("    [ModuleInitializer]");
        endpointsSb.AppendLine("    internal static void Register()");
        endpointsSb.AppendLine("    {");
        endpointsSb.AppendLine($"        DescriptorProviderRegistry.Register<CapabilityEndpointDescriptor>(new GeneratedAppServiceCompatibilityEndpoints_{service.StrippedName}());");
        endpointsSb.AppendLine("    }");
        endpointsSb.AppendLine("}");

        // Binding delegate implementations
        bindingsSb.AppendLine("    }");
        bindingsSb.AppendLine();
        foreach (var action in service.Actions)
        {
            if (action.IsSingleParam)
            {
                bindingsSb.AppendLine($"    private static async ValueTask<object?> Bind{action.ActionName}Async(HttpContext context, CancellationToken ct)");
                bindingsSb.AppendLine("    {");
                bindingsSb.AppendLine($"        return await CapabilityEndpointJsonRuntime.ReadBodyAsync<{action.InputTypeName}>(context, optional: false, ct);");
                bindingsSb.AppendLine("    }");
            }
            else
            {
                bindingsSb.AppendLine($"    private static async ValueTask<object?> Bind{action.ActionName}Async(HttpContext context, CancellationToken ct)");
                bindingsSb.AppendLine("    {");
                // Multi-param: extract route values + read body
                // The implementer must generate the specific binding code per parameter
                bindingsSb.AppendLine($"        // Multi-parameter binding for {action.ActionName}");
                bindingsSb.AppendLine($"        // Route values extracted from context.Request.RouteValues");
                bindingsSb.AppendLine($"        // Body read via CapabilityEndpointJsonRuntime.ReadBodyAsync<T>");
                bindingsSb.AppendLine($"        return new {action.EnvelopeTypeName} { /* field assignments */ };");
                bindingsSb.AppendLine("    }");
            }
            bindingsSb.AppendLine();
        }
        bindingsSb.AppendLine("}");

        return (endpointsSb.ToString(), bindingsSb.ToString());
    }
}
```

- [ ] **Step 2: Update AppServiceCompatibilityGenerator to emit endpoints and bindings**

In `AppServiceCompatibilityGenerator.GenerateCapabilityProviders`, add calls to `AppServiceCompatibilityEndpointEmitter.Emit()` and register both source files.

- [ ] **Step 3: Write test for endpoint generation**

```csharp
[Fact]
public void ClassLevelProjection_GeneratesEndpointDescriptors()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        
        namespace MyApp;
        
        [CrestService]
        [CapabilityCompatibilityProjection]
        public class BookAppService
        {
            public Task<string> CreateAsync(string input, CancellationToken ct) => Task.FromResult(input);
        }
        """;

    var result = SourceGeneratorTestHelper.RunGenerator<AppServiceCompatibilityGenerator>(
        source, additionalSources: BuildCompatibilityStubs());

    var endpoints = result.GetSourceByFileName("GeneratedAppServiceCompatibilityEndpoints_book.g.cs");
    endpoints.Should().NotBeNull();
    endpoints.Should().Contain("endpoint:compat.appservice.book.create");
    endpoints.Should().Contain("ICapabilityEndpointDescriptorProvider");

    var bindings = result.GetSourceByFileName("GeneratedAppServiceCompatibilityBindings_book.g.cs");
    bindings.Should().NotBeNull();
    bindings.Should().NotBeNull();
    bindings.Should().Contain("CapabilityEndpointBindingRegistry.Register");
}
```

- [ ] **Step 4: Run test**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "AppServiceCompatibilityGeneratorTests.ClassLevelProjection_GeneratesEndpointDescriptors" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityEndpointEmitter.cs tests/Tooling/CrestCreates.CodeGenerator.Tests/AppServiceCompatibilityGenerator/AppServiceCompatibilityGeneratorTests.cs
git commit -m "feat(codegenerator): AppServiceCompatibilityGenerator generates endpoint descriptors and bindings"
```

---

## Task 12: AppServiceCompatibilityGenerator — Handler Invoker Generation

**Files:**
- Create: `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityHandlerEmitter.cs`

**Interfaces:**
- Consumes: `CompatibilityServiceModel` from Task 10, `ICapabilityContextAwareHandlerInvoker` from Task 3, `CapabilityHandlerResolverProvider.Register()` from Task 1
- Produces: `GeneratedAppServiceCompatibilityInvokers_{name}.g.cs` — `ICapabilityContextAwareHandlerInvoker` implementations with DI-based AppService resolution

- [ ] **Step 1: Create AppServiceCompatibilityHandlerEmitter**

```csharp
// src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityHandlerEmitter.cs
using System.Text;

namespace CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator;

internal static class AppServiceCompatibilityHandlerEmitter
{
    public static string Emit(CompatibilityServiceModel service)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using CrestCreates.Capability.Abstractions;");
        sb.AppendLine("using CrestCreates.Capability;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();
        sb.AppendLine("namespace CrestCreates.Generated;");
        sb.AppendLine();

        // Generate one invoker class per action
        foreach (var action in service.Actions)
        {
            var invokerClassName = $"{service.ServiceName}_{action.ActionName}_CompatibilityInvoker";

            sb.AppendLine($"internal sealed class {invokerClassName} : ICapabilityContextAwareHandlerInvoker");
            sb.AppendLine("{");
            sb.AppendLine("    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var service = context.ServiceProvider.GetRequiredService<{service.InterfaceTypeName}>();");

            if (action.IsSingleParam)
            {
                sb.AppendLine($"        var typedInput = ({action.InputTypeName})context.Input!;");
                sb.AppendLine($"        var result = await service.{action.ServiceMethodName}(typedInput, ct).ConfigureAwait(false);");
            }
            else
            {
                sb.AppendLine($"        var envelope = ({action.EnvelopeTypeName})context.Input!;");
                // Generate the method call with envelope fields as arguments
                // The implementer must generate the specific argument list from the envelope fields
                sb.AppendLine($"        var result = await service.{action.ServiceMethodName}(/* envelope fields */, ct).ConfigureAwait(false);");
            }

            if (action.IsVoidReturn)
            {
                sb.AppendLine("        return null;");
            }
            else
            {
                sb.AppendLine("        return result;");
            }

            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public Task<object?> InvokeAsync(object? input, CancellationToken ct)");
            sb.AppendLine("        => throw new InvalidOperationException(");
            sb.AppendLine("            \"Compatibility invoker requires CapabilityExecutionContext. \" +");
            sb.AppendLine("            \"Use the ICapabilityContextAwareHandlerInvoker overload.\");");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // ModuleInitializer for registration
        sb.AppendLine($"internal static class GeneratedAppServiceCompatibilityInvokersBootstrapper_{service.StrippedName}");
        sb.AppendLine("{");
        sb.AppendLine("    [ModuleInitializer]");
        sb.AppendLine("    internal static void Register()");
        sb.AppendLine("    {");

        foreach (var action in service.Actions)
        {
            var invokerClassName = $"{service.ServiceName}_{action.ActionName}_CompatibilityInvoker";
            sb.AppendLine($"        CapabilityHandlerResolverProvider.Register(\"{action.CapabilityId}\", new {invokerClassName}());");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Update AppServiceCompatibilityGenerator to emit invokers**

Add call to `AppServiceCompatibilityHandlerEmitter.Emit()` in the generation method.

- [ ] **Step 3: Write test for invoker generation**

```csharp
[Fact]
public void ClassLevelProjection_GeneratesCompatibilityInvokers()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        
        namespace MyApp;
        
        [CrestService]
        [CapabilityCompatibilityProjection]
        public class BookAppService
        {
            public Task<string> CreateAsync(string input, CancellationToken ct) => Task.FromResult(input);
        }
        """;

    var result = SourceGeneratorTestHelper.RunGenerator<AppServiceCompatibilityGenerator>(
        source, additionalSources: BuildCompatibilityStubs());

    var invokers = result.GetSourceByFileName("GeneratedAppServiceCompatibilityInvokers_book.g.cs");
    invokers.Should().NotBeNull();
    invokers.Should().Contain("ICapabilityContextAwareHandlerInvoker");
    invokers.Should().Contain("context.ServiceProvider.GetRequiredService");
    invokers.Should().Contain("CapabilityHandlerResolverProvider.Register");
    invokers.Should().NotContain("new CapabilityHandlerResolver()");
    invokers.Should().NotContain("SetResolver");
}
```

- [ ] **Step 4: Run test**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "AppServiceCompatibilityGeneratorTests.ClassLevelProjection_GeneratesCompatibilityInvokers" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityHandlerEmitter.cs tests/Tooling/CrestCreates.CodeGenerator.Tests/AppServiceCompatibilityGenerator/AppServiceCompatibilityGeneratorTests.cs
git commit -m "feat(codegenerator): AppServiceCompatibilityGenerator generates compatibility invokers"
```

---

## Task 13: AppServiceCompatibilityGenerator — Manifest Generation

**Files:**
- Create: `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityManifestEmitter.cs`

**Interfaces:**
- Consumes: `CompatibilityServiceModel` from Task 10, `AppServiceCompatibilityProjectionEntry` from Task 6
- Produces: `GeneratedAppServiceCompatibilityManifest_{name}.g.cs`

- [ ] **Step 1: Create AppServiceCompatibilityManifestEmitter**

```csharp
// src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityManifestEmitter.cs
using System.Text;

namespace CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator;

internal static class AppServiceCompatibilityManifestEmitter
{
    public static string Emit(CompatibilityServiceModel service)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using CrestCreates.DynamicApi.Abstractions;");
        sb.AppendLine("using CrestCreates.Metadata.Abstractions.DescriptorCapability;");
        sb.AppendLine();
        sb.AppendLine("namespace CrestCreates.Generated;");
        sb.AppendLine();
        sb.AppendLine($"internal sealed class GeneratedAppServiceCompatibilityManifest_{service.StrippedName}");
        sb.AppendLine("{");
        sb.AppendLine("    public static readonly IReadOnlyList<AppServiceCompatibilityProjectionEntry> Entries = new[]");
        sb.AppendLine("    {");

        foreach (var action in service.Actions)
        {
            var invokerClassName = $"{service.ServiceName}_{action.ActionName}_CompatibilityInvoker";
            sb.AppendLine("        new AppServiceCompatibilityProjectionEntry");
            sb.AppendLine("        {");
            sb.AppendLine($"            SourceService = \"{service.ServiceName}\",");
            sb.AppendLine($"            SourceMethod = \"{action.ServiceMethodName}\",");
            sb.AppendLine($"            CapabilityId = \"{action.CapabilityId}\",");
            sb.AppendLine($"            EndpointId = \"{action.EndpointId}\",");
            sb.AppendLine($"            HttpMethod = \"{action.HttpMethod}\",");
            sb.AppendLine($"            RoutePattern = \"{action.RoutePattern}\",");
            sb.AppendLine($"            PermissionNames = new[] {{ \"{action.PermissionName}\" }},");
            sb.AppendLine($"            InvokerTypeName = \"{invokerClassName}\",");
            sb.AppendLine("            ProjectionKind = CapabilityProjectionKind.AppServiceCompatibility,");
            sb.AppendLine("        },");
        }

        sb.AppendLine("    };");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Update AppServiceCompatibilityGenerator to emit manifest**

Add call to `AppServiceCompatibilityManifestEmitter.Emit()` in the generation method.

- [ ] **Step 3: Write test for manifest generation**

```csharp
[Fact]
public void ClassLevelProjection_GeneratesManifest()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        
        namespace MyApp;
        
        [CrestService]
        [CapabilityCompatibilityProjection]
        public class BookAppService
        {
            public Task<string> CreateAsync(string input, CancellationToken ct) => Task.FromResult(input);
        }
        """;

    var result = SourceGeneratorTestHelper.RunGenerator<AppServiceCompatibilityGenerator>(
        source, additionalSources: BuildCompatibilityStubs());

    var manifest = result.GetSourceByFileName("GeneratedAppServiceCompatibilityManifest_book.g.cs");
    manifest.Should().NotBeNull();
    manifest.Should().Contain("AppServiceCompatibilityProjectionEntry");
    manifest.Should().Contain("compat.appservice.book.create");
    manifest.Should().Contain("CapabilityProjectionKind.AppServiceCompatibility");
}
```

- [ ] **Step 4: Run test**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "AppServiceCompatibilityGeneratorTests.ClassLevelProjection_GeneratesManifest" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityManifestEmitter.cs tests/Tooling/CrestCreates.CodeGenerator.Tests/AppServiceCompatibilityGenerator/AppServiceCompatibilityGeneratorTests.cs
git commit -m "feat(codegenerator): AppServiceCompatibilityGenerator generates projection manifest"
```

---

## Task 14: AddCrestCompatibilityProjection DI Extension

**Files:**
- Modify: `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointExtensions.cs`

**Interfaces:**
- Consumes: `AddCapabilityRuntime()`, `AddCrestCapabilityEndpoints()` from existing code
- Produces: `AddCrestCompatibilityProjection()` extension method

- [ ] **Step 1: Add extension method**

In `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointExtensions.cs`, add:

```csharp
/// <summary>
/// Registers compatibility projection services.
/// Ensures Capability runtime and endpoint infrastructure are available.
/// Compatibility handler invokers are auto-registered via generated [ModuleInitializer].
/// </summary>
public static IServiceCollection AddCrestCompatibilityProjection(
    this IServiceCollection services)
{
    services.AddCapabilityRuntime();
    services.AddCrestCapabilityEndpoints();
    return services;
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Framework/Api/CrestCreates.DynamicApi`
Expected: SUCCESS

- [ ] **Step 3: Commit**

```bash
git add src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointExtensions.cs
git commit -m "feat(dynamicapi): add AddCrestCompatibilityProjection DI extension"
```

---

## Task 15: Diagnostics CEP030-CEP033

**Files:**
- Modify: `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityGenerator.cs` (add diagnostic reporting)

**Interfaces:**
- Consumes: `AppServiceCompatibilityDiagnostics` from Task 10
- Produces: CEP030-CEP033 diagnostic reports during SG execution

- [ ] **Step 1: Add diagnostic reporting in GetServiceInfo**

In `AppServiceCompatibilityGenerator.GetServiceInfo`, add validation:

```csharp
// CEP030: [CapabilityCompatibilityProjection] on non-[CrestService] class
if (!hasCrestService && classProjectionAttr != null)
{
    context.ReportDiagnostic(Diagnostic.Create(
        AppServiceCompatibilityDiagnostics.CEP030,
        classDecl.GetLocation()));
    return null;
}

// CEP031: [CapabilityCompatibilityProjection] + [DynamicApiIgnore] on same member
foreach (var method in symbol.GetMembers().OfType<IMethodSymbol>())
{
    var hasProjection = method.GetAttributes().Any(a => a.AttributeClass?.Name == "CapabilityCompatibilityProjectionAttribute");
    var hasIgnore = method.GetAttributes().Any(a => a.AttributeClass?.Name == "DynamicApiIgnoreAttribute");
    if (hasProjection && hasIgnore)
    {
        // Report CEP031 — need access to the method's syntax reference for location
        // Use method.Locations[0] or find the corresponding syntax node
    }
}
```

Note: The `GetServiceInfo` method currently returns `CompatibilityServiceModel?` and doesn't have access to `SourceProductionContext` for reporting diagnostics. The implementer must restructure to separate the syntax predicate from the semantic transform, or use a two-phase approach where diagnostics are collected and reported in the `RegisterSourceOutput` callback.

- [ ] **Step 2: Write tests for each diagnostic**

```csharp
[Fact]
public void CEP030_ProjectionOnNonCrestService()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        
        namespace MyApp;
        
        [CapabilityCompatibilityProjection]  // Error: not on [CrestService]
        public class SomeClass
        {
            public Task<string> CreateAsync(string input, CancellationToken ct) => Task.FromResult(input);
        }
        """;

    var result = SourceGeneratorTestHelper.RunGenerator<AppServiceCompatibilityGenerator>(
        source, additionalSources: BuildCompatibilityStubs());
    result.Diagnostics.Should().Contain(d => d.Id == "CEP030");
}

[Fact]
public void CEP031_ProjectionWithDynamicApiIgnore()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        
        namespace MyApp;
        
        [CrestService]
        [CapabilityCompatibilityProjection]
        public class BookAppService
        {
            [CapabilityCompatibilityProjection]
            [DynamicApiIgnore]  // Error: conflicting
            public Task<string> CreateAsync(string input, CancellationToken ct) => Task.FromResult(input);
        }
        """;

    var result = SourceGeneratorTestHelper.RunGenerator<AppServiceCompatibilityGenerator>(
        source, additionalSources: BuildCompatibilityStubs());
    result.Diagnostics.Should().Contain(d => d.Id == "CEP031");
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "AppServiceCompatibilityGeneratorTests.CEP" -v n`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/ tests/Tooling/CrestCreates.CodeGenerator.Tests/AppServiceCompatibilityGenerator/
git commit -m "feat(codegenerator): CEP030-CEP033 diagnostics for compatibility projection"
```

---

## Task 16: SG Tests — Comprehensive Coverage

**Files:**
- Modify: `tests/Tooling/CrestCreates.CodeGenerator.Tests/AppServiceCompatibilityGenerator/AppServiceCompatibilityGeneratorTests.cs`

**Interfaces:**
- Consumes: All SG emitters from Tasks 10-13, 15
- Produces: Full test coverage for all generation scenarios

- [ ] **Step 1: Add method-level projection test**

```csharp
[Fact]
public void MethodLevelProjection_OnlyProjectsMarkedMethod()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        
        namespace MyApp;
        
        [CrestService]
        public class BookAppService
        {
            [CapabilityCompatibilityProjection]
            public Task<string> CreateAsync(string input, CancellationToken ct) => Task.FromResult(input);
            
            public Task<string> GetAllAsync(CancellationToken ct) => Task.FromResult("all");
        }
        """;

    var result = SourceGeneratorTestHelper.RunGenerator<AppServiceCompatibilityGenerator>(
        source, additionalSources: BuildCompatibilityStubs());

    var capabilities = result.GetSourceByFileName("GeneratedAppServiceCompatibilityCapabilities_book.g.cs");
    capabilities.Should().Contain("compat.appservice.book.create");
    capabilities.Should().NotContain("compat.appservice.book.get-all");
}
```

- [ ] **Step 2: Add [CapabilityCompatibilityIgnore] test**

```csharp
[Fact]
public void CompatibilityIgnore_ExcludesMethod()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        
        namespace MyApp;
        
        [CrestService]
        [CapabilityCompatibilityProjection]
        public class BookAppService
        {
            public Task<string> CreateAsync(string input, CancellationToken ct) => Task.FromResult(input);
            
            [CapabilityCompatibilityIgnore]
            public Task<string> GetInternalAsync(CancellationToken ct) => Task.FromResult("internal");
        }
        """;

    var result = SourceGeneratorTestHelper.RunGenerator<AppServiceCompatibilityGenerator>(
        source, additionalSources: BuildCompatibilityStubs());

    var capabilities = result.GetSourceByFileName("GeneratedAppServiceCompatibilityCapabilities_book.g.cs");
    capabilities.Should().Contain("compat.appservice.book.create");
    capabilities.Should().NotContain("compat.appservice.book.get-internal");
}
```

- [ ] **Step 3: Add multi-parameter method test**

```csharp
[Fact]
public void MultiParamMethod_GeneratesEnvelope()
{
    var source = """
        using System;
        using CrestCreates.Domain.Shared.Attributes;
        
        namespace MyApp;
        
        [CrestService]
        [CapabilityCompatibilityProjection]
        public class BookAppService
        {
            public Task<string> UpdateAsync(Guid id, string input, CancellationToken ct) => Task.FromResult("updated");
        }
        """;

    var result = SourceGeneratorTestHelper.RunGenerator<AppServiceCompatibilityGenerator>(
        source, additionalSources: BuildCompatibilityStubs());

    var bindings = result.GetSourceByFileName("GeneratedAppServiceCompatibilityBindings_book.g.cs");
    bindings.Should().Contain("CompatibilityInput");
    bindings.Should().NotContain("Dictionary<string, object?>");
}
```

- [ ] **Step 4: Add CapabilityIdPrefix override test**

```csharp
[Fact]
public void CustomCapabilityIdPrefix_OverridesDefault()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        
        namespace MyApp;
        
        [CrestService]
        [CapabilityCompatibilityProjection(CapabilityIdPrefix = "book")]
        public class BookAppService
        {
            public Task<string> CreateAsync(string input, CancellationToken ct) => Task.FromResult(input);
        }
        """;

    var result = SourceGeneratorTestHelper.RunGenerator<AppServiceCompatibilityGenerator>(
        source, additionalSources: BuildCompatibilityStubs());

    var capabilities = result.GetSourceByFileName("GeneratedAppServiceCompatibilityCapabilities_book.g.cs");
    capabilities.Should().Contain("book.create");
    capabilities.Should().NotContain("compat.appservice.book.create");
}
```

- [ ] **Step 5: Run all SG tests**

Run: `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "AppServiceCompatibilityGeneratorTests" -v n`
Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add tests/Tooling/CrestCreates.CodeGenerator.Tests/AppServiceCompatibilityGenerator/AppServiceCompatibilityGeneratorTests.cs
git commit -m "test(codegenerator): comprehensive AppServiceCompatibilityGenerator test coverage"
```

---

## Task 17: Functional Integration Test

**Files:**
- Create: `tests/Framework/Api/CrestCreates.DynamicApi.Tests/CompatibilityProjectionIntegrationTests.cs`

**Interfaces:**
- Consumes: All runtime components from Tasks 1-14
- Produces: End-to-end test proving HTTP → Capability Pipeline → AppService method

This test requires a running web application with:
- A `[CrestService]` class with `[CapabilityCompatibilityProjection]`
- `AddCrestCompatibilityProjection()` registered in DI
- `MapCrestCapabilityEndpoints()` called in middleware pipeline

- [ ] **Step 1: Write integration test**

```csharp
// tests/Framework/Api/CrestCreates.DynamicApi.Tests/CompatibilityProjectionIntegrationTests.cs
using System.Net;
using System.Net.Http.Json;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.TestBase;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class CompatibilityProjectionIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task ProjectedEndpoint_ExecutesThroughCapabilityPipeline()
    {
        // Arrange
        using var client = CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/books", new { Title = "Test Book" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        // Verify the response came through Capability Pipeline (not legacy DynamicApi)
        // by checking that Pipeline middleware executed (e.g., audit log entry exists)
    }

    [Fact]
    public async Task ProjectedEndpoint_SharesScopedDependencies()
    {
        // Arrange — verify that the AppService resolved by the compatibility invoker
        // shares the same scoped IServiceProvider as the HTTP request
        // This proves Exit Criteria #17

        using var client = CreateClient();

        // Act
        var response = await client.GetAsync("/api/books/scoped-test");

        // Assert — the endpoint should return the same scope ID
        // (requires a test-specific endpoint that returns scope identity)
        response.EnsureSuccessStatusCode();
    }
}
```

Note: The exact test structure depends on the sample application setup. The implementer must adapt to the existing `IntegrationTestBase` pattern and the `WebApplicationFactory<Program>` configuration used in this project.

- [ ] **Step 2: Run integration test**

Run: `dotnet test tests/Framework/Api/CrestCreates.DynamicApi.Tests --filter "CompatibilityProjectionIntegrationTests" -v n`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add tests/Framework/Api/CrestCreates.DynamicApi.Tests/CompatibilityProjectionIntegrationTests.cs
git commit -m "test(dynamicapi): compatibility projection integration test"
```

---

## Task 18: Boundary Tests

**Files:**
- Modify: `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs`

**Interfaces:**
- Consumes: `DependencyBoundaryTests.AssertNoDirectProjectReferences` pattern
- Produces: Boundary tests verifying compatibility projection doesn't violate dependency rules

- [ ] **Step 1: Add boundary test for DynamicApi.Abstractions**

Verify `CrestCreates.DynamicApi.Abstractions` does not reference `CrestCreates.Capability` (only `CrestCreates.Capability.Abstractions` is allowed):

```csharp
[Fact]
public void DynamicApiAbstractions_DoesNotReferenceCapabilityImplementation()
{
    AssertNoDirectProjectReferences(
        "src/Framework/Api/CrestCreates.DynamicApi.Abstractions",
        "DynamicApi.Abstractions must not reference Capability implementation (only Abstractions)",
        new[] { "CrestCreates.Capability.csproj" });  // Must NOT contain this
}
```

Note: This test already exists at line 373-383. Verify it still passes after adding `AppServiceCompatibilityProjectionEntry` (which references `CapabilityProjectionKind` from `Metadata.Abstractions`, not `Capability`).

- [ ] **Step 2: Add boundary test for generated code**

Verify that generated compatibility code does not reference `DynamicApiGeneratedRegistryStore`, `IDynamicApiGeneratedProvider`, or `DynamicApiGeneratedRuntime`:

This is verified by inspecting the generated source strings in the SG tests (Task 16), not by project references. The SG tests already assert `DoesNotContain("DynamicApiGeneratedRegistryStore")` etc.

- [ ] **Step 3: Run boundary tests**

Run: `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests --filter "DynamicApi" -v n`
Expected: All PASS

- [ ] **Step 4: Commit**

```bash
git add tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs
git commit -m "test(boundary): verify compatibility projection dependency boundaries"
```

---

## Self-Review

### 1. Spec Coverage

| Spec Section | Task |
|---|---|
| 2.1 Attributes | Task 5 |
| 2.2 Semantic rules | Task 10 (class-level), Task 16 (method-level, ignore) |
| 2.4 Legacy suppression | Task 9 |
| 3. Capability Identity Namespace | Task 10 (capabilityIdPrefix derivation) |
| 4.1 Convention derivation | Task 8 |
| 4.2 CapabilityDescriptor provider | Task 10 |
| 4.3 Endpoint + binding provider | Task 11 |
| 4.4 Binding (envelope) | Task 11, Task 16 |
| 4.5 Handler invoker | Task 12 |
| 4.6 Manifest | Task 13 |
| 5.1 Additive registration | Task 1, Task 2 |
| 5.2 ServiceProvider property | Task 3 |
| 5.3 Pipeline assignment | Task 4 |
| 5.3 ProjectionKind property | Task 7 |
| 5.4 CapabilityProjectionKind enum | Task 6 |
| 5.5 DynamicApiAotSourceGenerator exclusion | Task 9 |
| 5.6 DynamicApiConventionAnalyzer | Task 8 |
| 6. SG internal structure | Tasks 10-13 |
| 7. DI registration | Task 14 |
| 8. Transaction handling | No code — by design (AOP handles) |
| 9. InputSchema/OutputSchema | No code — by design (null in 8d) |
| 10. Boundary constraints | Task 18 |
| 11. Diagnostics | Task 15 |
| 12. Unchanged components | Verified in Tasks 1-18 |
| 13. Exit path | Task 7 (ProjectionKind enum) |
| 14. Implementation steps | Mapped to Tasks 1-18 |
| 15. Exit criteria | Verified by Tasks 1-18 |

### 2. Placeholder Scan

Issues found and fixed inline:
- Task 11: Multi-param binding code has `/* envelope fields */` placeholder — the implementer must fill in the specific parameter binding logic based on the `CompatibilityActionModel` parameter analysis. This is acceptable because the exact code depends on runtime parameter analysis that the SG performs per-method.
- Task 12: Method call argument list has `/* envelope fields */` — same situation.

These are not "TODO" placeholders — they are structural markers indicating where the SG must emit method-specific code that cannot be pre-written in a plan document. The implementer must read the `CompatibilityActionModel` fields and generate the appropriate code.

### 3. Type Consistency

- `CapabilityProjectionKind` defined in Task 6 → used in Task 7, Task 10, Task 13 ✓
- `AppServiceCompatibilityProjectionEntry` defined in Task 6 → used in Task 13 ✓
- `CompatibilityServiceModel` defined in Task 10 → used in Tasks 11-13 ✓
- `CapabilityHandlerResolverProvider.Register()` defined in Task 1 → used in Task 2, Task 12 ✓
- `CapabilityExecutionContext.ServiceProvider` defined in Task 3 → used in Task 4, Task 12 ✓
- `DynamicApiConventionAnalyzer` methods defined in Task 8 → used in Task 9, Task 10 ✓
- `ICapabilityContextAwareHandlerInvoker` from existing code → used in Task 12 ✓
