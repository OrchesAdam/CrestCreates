# Phase 5h — Runtime Binding Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a read-only runtime binding status/reporting layer with per-descriptor contributors that distinguish structurally valid from runtime-executable descriptors.

**Architecture:** New `IDescriptorBindingStatusContributor` interface — each module (Capability/Form/HumanTask/Workflow/Event) implements one contributor that self-enumerates descriptors from its injected typed registry and evaluates binding status per descriptor. A `DefaultDescriptorRuntimeBindingStatusProvider` aggregates contributors via DI `IEnumerable<T>` and exposes `GetStatus(IDescriptor)` / `GetAllStatuses()`.

**Tech Stack:** .NET 10, C# records/classes, Singleton DI, xUnit + FluentAssertions + Moq, existing `ValidationSeverity` enum, existing `RegistryBase<T>` / `IVersionedDescriptorRegistry<T>` patterns.

**Spec:** `docs/superpowers/specs/2026-06-12-phase-5h-runtime-binding-status-design.md`
**Parent Issue:** [#4](https://github.com/OrchesAdam/CrestCreates/issues/4)

---

## File Structure

### New Files (14)

```
framework/src/CrestCreates.Metadata.Abstractions/
  DescriptorBindingStatus.cs          (enum)
  DescriptorBindingIssue.cs           (record)
  DescriptorBindingReport.cs          (class)
  RuntimeBindingReport.cs             (class)
  IDescriptorBindingStatusContributor.cs  (interface)
  IDescriptorRuntimeBindingStatusProvider.cs  (interface)

framework/src/CrestCreates.Metadata/
  BindingStatusSynthesizer.cs         (static synthesis method)
  DefaultDescriptorRuntimeBindingStatusProvider.cs  (implementation)
  MetadataServiceCollectionExtensions.cs  (DI: AddBindingStatusKernel)

framework/src/CrestCreates.Capability/
  CapabilityBindingStatusContributor.cs

framework/src/CrestCreates.Form/
  FormBindingStatusContributor.cs

framework/src/CrestCreates.HumanTask/
  HumanTaskBindingStatusContributor.cs

framework/src/CrestCreates.Workflow/
  WorkflowBindingStatusContributor.cs

framework/src/CrestCreates.Event/
  EventBindingStatusContributor.cs
  EventServiceCollectionExtensions.cs     (new: EventRegistry bridging + contributor DI)

framework/src/CrestCreates.Schema/
  SchemaServiceCollectionExtensions.cs    (new: SchemaRegistry DI)
```

### Modified Files (4)

```
framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs  (+registry DI, +ICapabilityHandlerResolver DI, +contributor DI)
framework/src/CrestCreates.Form/FormServiceCollectionExtensions.cs              (+contributor DI)
framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs    (+registry DI, +contributor DI)
framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs     (+registry DI, +contributor DI)
```

### Test Files (8)

```
framework/test/CrestCreates.Metadata.Tests/
  BindingStatusSynthesizerTests.cs          (6 tests)
  DefaultDescriptorRuntimeBindingStatusProviderTests.cs  (4 tests)

framework/test/CrestCreates.Capability.Tests/
  CapabilityBindingStatusContributorTests.cs  (3 tests)

framework/test/CrestCreates.Form.Tests/
  FormBindingStatusContributorTests.cs  (3 tests)

framework/test/CrestCreates.HumanTask.Tests/
  HumanTaskBindingStatusContributorTests.cs  (3 tests)

framework/test/CrestCreates.Workflow.Tests/
  WorkflowBindingStatusContributorTests.cs  (5 tests)

framework/test/CrestCreates.Event.Tests/
  EventBindingStatusContributorTests.cs  (4 tests)
```

---

### Task 1: Core Models — enum + record + classes

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorBindingStatus.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorBindingIssue.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorBindingReport.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/RuntimeBindingReport.cs`

- [ ] **Step 1: Create DescriptorBindingStatus enum**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorBindingStatus.cs
namespace CrestCreates.Metadata.Abstractions;

public enum DescriptorBindingStatus
{
    /// <summary>All bindings valid; descriptor is runtime-executable.</summary>
    RuntimeReady,

    /// <summary>Warnings only (e.g., optional schema field missing from form).</summary>
    PartiallyBound,

    /// <summary>Missing handler or binding (e.g., capability without handler).</summary>
    Unbound,

    /// <summary>Feature declared but current runtime explicitly does not support it.</summary>
    Unsupported,

    /// <summary>Unresolved references (schema missing, target missing, etc.).</summary>
    Invalid
}
```

- [ ] **Step 2: Create DescriptorBindingIssue record**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorBindingIssue.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Independent from ValidationIssue. Binding status is a different domain
/// from structural validation — different fields, different consumers.
/// Reuses ValidationSeverity to avoid creating a parallel severity enum.
/// </summary>
public sealed record DescriptorBindingIssue(
    ValidationSeverity Severity,
    string Code,          // Stable error code for tests (e.g., "REF_MISSING_SCHEMA")
    string Message,       // Human-readable description
    string? DescriptorId = null,
    DescriptorKind? DescriptorKind = null,
    string? Path = null); // Property path (e.g., "InputSchema.Id")
```

- [ ] **Step 3: Create DescriptorBindingReport class**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/DescriptorBindingReport.cs
namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorBindingReport
{
    public string DescriptorId { get; init; } = default!;
    public DescriptorKind DescriptorKind { get; init; }
    public DescriptorBindingStatus Status { get; init; }
    public IReadOnlyList<DescriptorBindingIssue> Issues { get; init; } = Array.Empty<DescriptorBindingIssue>();

    public bool IsRuntimeReady => Status == DescriptorBindingStatus.RuntimeReady;
}
```

- [ ] **Step 4: Create RuntimeBindingReport class**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/RuntimeBindingReport.cs
namespace CrestCreates.Metadata.Abstractions;

public sealed class RuntimeBindingReport
{
    public IReadOnlyList<DescriptorBindingReport> Descriptors { get; init; }
        = Array.Empty<DescriptorBindingReport>();

    public bool HasErrors => Descriptors.Any(d =>
        d.Status is DescriptorBindingStatus.Invalid
                   or DescriptorBindingStatus.Unbound
                   or DescriptorBindingStatus.Unsupported);

    public IReadOnlyList<DescriptorBindingReport> NotReady =>
        Descriptors.Where(d => !d.IsRuntimeReady).ToArray();
}
```

- [ ] **Step 5: Build verification**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorBindingStatus.cs \
        framework/src/CrestCreates.Metadata.Abstractions/DescriptorBindingIssue.cs \
        framework/src/CrestCreates.Metadata.Abstractions/DescriptorBindingReport.cs \
        framework/src/CrestCreates.Metadata.Abstractions/RuntimeBindingReport.cs
git commit -m "feat(Phase5h): add core binding status models — enum, issue, per-descriptor report, aggregate report"
```

---

### Task 2: Interfaces

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorBindingStatusContributor.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorRuntimeBindingStatusProvider.cs`

- [ ] **Step 1: Create IDescriptorBindingStatusContributor interface**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IDescriptorBindingStatusContributor.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Per-module evaluator + descriptor enumerator. Each module (Capability, Form, HumanTask,
/// Workflow, Event) implements one to enumerate and evaluate descriptors of its SupportedKind.
/// Singleton, stateless, receives typed registries via constructor DI.
/// </summary>
public interface IDescriptorBindingStatusContributor
{
    /// <summary>Which DescriptorKind this contributor handles.</summary>
    DescriptorKind SupportedKind { get; }

    /// <summary>Execution order (lower = earlier). Contributors are sorted before evaluation.</summary>
    int Order { get; }

    /// <summary>
    /// Enumerate all descriptors of this kind from the contributor's injected registry.
    /// Returns empty list if the registry has not been built (RegistryState != Built).
    /// Must not trigger registry.Build().
    /// </summary>
    IReadOnlyList<IDescriptor> GetDescriptors();

    /// <summary>Evaluate a single descriptor. Must not mutate state.</summary>
    DescriptorBindingReport Evaluate(IDescriptor descriptor);
}
```

- [ ] **Step 2: Create IDescriptorRuntimeBindingStatusProvider interface**

```csharp
// framework/src/CrestCreates.Metadata.Abstractions/IDescriptorRuntimeBindingStatusProvider.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Consumer-facing query API. Runs AFTER registries are built.
/// Does not trigger registry.Build() or mutate descriptors.
/// </summary>
public interface IDescriptorRuntimeBindingStatusProvider
{
    DescriptorBindingReport GetStatus(IDescriptor descriptor);
    RuntimeBindingReport GetAllStatuses();
}
```

- [ ] **Step 3: Build verification**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/IDescriptorBindingStatusContributor.cs \
        framework/src/CrestCreates.Metadata.Abstractions/IDescriptorRuntimeBindingStatusProvider.cs
git commit -m "feat(Phase5h): add binding status contributor and provider interfaces"
```

---

### Task 3: BindingStatusSynthesizer

**Files:**
- Create: `framework/src/CrestCreates.Metadata/BindingStatusSynthesizer.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/BindingStatusSynthesizerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// framework/test/CrestCreates.Metadata.Tests/BindingStatusSynthesizerTests.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class BindingStatusSynthesizerTests
{
    [Fact]
    public void SynthesizeStatus_EmptyIssues_ReturnsRuntimeReady()
    {
        var result = BindingStatusSynthesizer.SynthesizeStatus(Array.Empty<DescriptorBindingIssue>());
        result.Should().Be(DescriptorBindingStatus.RuntimeReady);
    }

    [Fact]
    public void SynthesizeStatus_RefError_ReturnsInvalid()
    {
        var issues = new[] { new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA", "Schema missing") };
        var result = BindingStatusSynthesizer.SynthesizeStatus(issues);
        result.Should().Be(DescriptorBindingStatus.Invalid);
    }

    [Fact]
    public void SynthesizeStatus_BindError_ReturnsUnbound()
    {
        var issues = new[] { new DescriptorBindingIssue(ValidationSeverity.Error, "BIND_NO_HANDLER", "No handler") };
        var result = BindingStatusSynthesizer.SynthesizeStatus(issues);
        result.Should().Be(DescriptorBindingStatus.Unbound);
    }

    [Fact]
    public void SynthesizeStatus_UnsupportedError_ReturnsUnsupported()
    {
        var issues = new[] { new DescriptorBindingIssue(ValidationSeverity.Error, "UNSUPPORTED_RETRY", "Retry not supported") };
        var result = BindingStatusSynthesizer.SynthesizeStatus(issues);
        result.Should().Be(DescriptorBindingStatus.Unsupported);
    }

    [Fact]
    public void SynthesizeStatus_WarningOnly_ReturnsPartiallyBound()
    {
        var issues = new[] { new DescriptorBindingIssue(ValidationSeverity.Warning, "WARN_DEPRECATED", "Deprecated") };
        var result = BindingStatusSynthesizer.SynthesizeStatus(issues);
        result.Should().Be(DescriptorBindingStatus.PartiallyBound);
    }

    [Fact]
    public void SynthesizeStatus_MixedErrors_RefTakesPriority()
    {
        var issues = new[]
        {
            new DescriptorBindingIssue(ValidationSeverity.Error, "UNSUPPORTED_RETRY", "Retry unsupported"),
            new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA", "Schema missing")
        };
        var result = BindingStatusSynthesizer.SynthesizeStatus(issues);
        result.Should().Be(DescriptorBindingStatus.Invalid);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~BindingStatusSynthesizerTests"
```

Expected: FAIL — `BindingStatusSynthesizer` not found.

- [ ] **Step 3: Implement BindingStatusSynthesizer**

```csharp
// framework/src/CrestCreates.Metadata/BindingStatusSynthesizer.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public static class BindingStatusSynthesizer
{
    public static DescriptorBindingStatus SynthesizeStatus(IReadOnlyList<DescriptorBindingIssue> issues)
    {
        if (issues.Count == 0) return DescriptorBindingStatus.RuntimeReady;

        if (issues.Any(i => i.Severity == ValidationSeverity.Error && i.Code.StartsWith("REF_")))
            return DescriptorBindingStatus.Invalid;

        if (issues.Any(i => i.Severity == ValidationSeverity.Error && i.Code.StartsWith("BIND_")))
            return DescriptorBindingStatus.Unbound;

        if (issues.Any(i => i.Severity == ValidationSeverity.Error && i.Code.StartsWith("UNSUPPORTED_")))
            return DescriptorBindingStatus.Unsupported;

        return DescriptorBindingStatus.PartiallyBound; // warnings only
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~BindingStatusSynthesizerTests"
```

Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata/BindingStatusSynthesizer.cs \
        framework/test/CrestCreates.Metadata.Tests/BindingStatusSynthesizerTests.cs
git commit -m "feat(Phase5h): add BindingStatusSynthesizer with 6 status synthesis rules"
```

---

### Task 4: DefaultDescriptorRuntimeBindingStatusProvider

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DefaultDescriptorRuntimeBindingStatusProvider.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DefaultDescriptorRuntimeBindingStatusProviderTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// framework/test/CrestCreates.Metadata.Tests/DefaultDescriptorRuntimeBindingStatusProviderTests.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DefaultDescriptorRuntimeBindingStatusProviderTests
{
    [Fact]
    public void GetAllStatuses_AggregatesFromAllContributors()
    {
        var contributor1 = new Mock<IDescriptorBindingStatusContributor>();
        contributor1.Setup(c => c.SupportedKind).Returns(DescriptorKind.Capability);
        contributor1.Setup(c => c.Order).Returns(10);
        contributor1.Setup(c => c.GetDescriptors()).Returns(new IDescriptor[] { Mock.Of<IDescriptor>(d => d.FullId == "capability.test") });
        contributor1.Setup(c => c.Evaluate(It.IsAny<IDescriptor>())).Returns(new DescriptorBindingReport
        {
            DescriptorId = "capability.test",
            DescriptorKind = DescriptorKind.Capability,
            Status = DescriptorBindingStatus.RuntimeReady
        });

        var contributor2 = new Mock<IDescriptorBindingStatusContributor>();
        contributor2.Setup(c => c.SupportedKind).Returns(DescriptorKind.Form);
        contributor2.Setup(c => c.Order).Returns(20);
        contributor2.Setup(c => c.GetDescriptors()).Returns(new IDescriptor[] { Mock.Of<IDescriptor>(d => d.FullId == "form.test") });
        contributor2.Setup(c => c.Evaluate(It.IsAny<IDescriptor>())).Returns(new DescriptorBindingReport
        {
            DescriptorId = "form.test",
            DescriptorKind = DescriptorKind.Form,
            Status = DescriptorBindingStatus.PartiallyBound
        });

        var provider = new DefaultDescriptorRuntimeBindingStatusProvider(
            new[] { contributor1.Object, contributor2.Object });

        var report = provider.GetAllStatuses();

        report.Descriptors.Should().HaveCount(2);
        report.Descriptors[0].DescriptorId.Should().Be("capability.test");
        report.Descriptors[1].DescriptorId.Should().Be("form.test");
    }

    [Fact]
    public void GetAllStatuses_EmptyContributor_Skipped()
    {
        var contributor = new Mock<IDescriptorBindingStatusContributor>();
        contributor.Setup(c => c.SupportedKind).Returns(DescriptorKind.Event);
        contributor.Setup(c => c.Order).Returns(10);
        contributor.Setup(c => c.GetDescriptors()).Returns(Array.Empty<IDescriptor>());

        var provider = new DefaultDescriptorRuntimeBindingStatusProvider(new[] { contributor.Object });

        var report = provider.GetAllStatuses();

        report.Descriptors.Should().BeEmpty();
    }

    [Fact]
    public void GetStatus_UnknownKind_ReturnsPartiallyBound()
    {
        var provider = new DefaultDescriptorRuntimeBindingStatusProvider(
            Array.Empty<IDescriptorBindingStatusContributor>());

        var descriptor = Mock.Of<IDescriptor>(d =>
            d.FullId == "schema.test" && d.Kind == DescriptorKind.Schema);

        var result = provider.GetStatus(descriptor);

        result.Status.Should().Be(DescriptorBindingStatus.PartiallyBound);
        result.Issues.Should().ContainSingle(i => i.Code == "WARN_NO_BINDING_CONTRIBUTOR");
    }

    [Fact]
    public void GetStatus_KnownKind_DelegatesToContributor()
    {
        var contributor = new Mock<IDescriptorBindingStatusContributor>();
        contributor.Setup(c => c.SupportedKind).Returns(DescriptorKind.Workflow);
        contributor.Setup(c => c.Order).Returns(10);
        contributor.Setup(c => c.Evaluate(It.IsAny<IDescriptor>())).Returns(new DescriptorBindingReport
        {
            DescriptorId = "workflow.test",
            DescriptorKind = DescriptorKind.Workflow,
            Status = DescriptorBindingStatus.RuntimeReady
        });

        var provider = new DefaultDescriptorRuntimeBindingStatusProvider(new[] { contributor.Object });
        var descriptor = Mock.Of<IDescriptor>(d =>
            d.FullId == "workflow.test" && d.Kind == DescriptorKind.Workflow);

        var result = provider.GetStatus(descriptor);

        result.Status.Should().Be(DescriptorBindingStatus.RuntimeReady);
        contributor.Verify(c => c.Evaluate(descriptor), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DefaultDescriptorRuntimeBindingStatusProviderTests"
```

Expected: FAIL — `DefaultDescriptorRuntimeBindingStatusProvider` not found.

- [ ] **Step 3: Implement DefaultDescriptorRuntimeBindingStatusProvider**

```csharp
// framework/src/CrestCreates.Metadata/DefaultDescriptorRuntimeBindingStatusProvider.cs
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DefaultDescriptorRuntimeBindingStatusProvider
    : IDescriptorRuntimeBindingStatusProvider
{
    private readonly IReadOnlyList<IDescriptorBindingStatusContributor> _contributors;

    public DefaultDescriptorRuntimeBindingStatusProvider(
        IEnumerable<IDescriptorBindingStatusContributor> contributors)
    {
        _contributors = contributors.OrderBy(c => c.Order).ToList();
    }

    public DescriptorBindingReport GetStatus(IDescriptor descriptor)
    {
        var contributor = _contributors.FirstOrDefault(c => c.SupportedKind == descriptor.Kind);
        return contributor?.Evaluate(descriptor)
            ?? new DescriptorBindingReport
            {
                DescriptorId = descriptor.FullId,
                DescriptorKind = descriptor.Kind,
                Status = DescriptorBindingStatus.PartiallyBound,
                Issues = new[]
                {
                    new DescriptorBindingIssue(
                        Severity: ValidationSeverity.Warning,
                        Code: "WARN_NO_BINDING_CONTRIBUTOR",
                        Message: $"No binding status contributor registered for {descriptor.Kind}.",
                        DescriptorId: descriptor.FullId,
                        DescriptorKind: descriptor.Kind)
                }
            };
    }

    public RuntimeBindingReport GetAllStatuses()
    {
        var reports = new List<DescriptorBindingReport>();
        foreach (var contributor in _contributors)
        {
            foreach (var descriptor in contributor.GetDescriptors())
            {
                reports.Add(contributor.Evaluate(descriptor));
            }
        }
        return new RuntimeBindingReport { Descriptors = reports };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DefaultDescriptorRuntimeBindingStatusProviderTests"
```

Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DefaultDescriptorRuntimeBindingStatusProvider.cs \
        framework/test/CrestCreates.Metadata.Tests/DefaultDescriptorRuntimeBindingStatusProviderTests.cs
git commit -m "feat(Phase5h): add DefaultDescriptorRuntimeBindingStatusProvider with contributor aggregation"
```

---

### Task 5: MetadataServiceCollectionExtensions

**Files:**
- Create: `framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs`

- [ ] **Step 1: Create DI extension**

```csharp
// framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Metadata;

public static class MetadataServiceCollectionExtensions
{
    public static IServiceCollection AddBindingStatusKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorRuntimeBindingStatusProvider,
            DefaultDescriptorRuntimeBindingStatusProvider>();
        return services;
    }
}
```

- [ ] **Step 2: Build verification**

```bash
dotnet build framework/src/CrestCreates.Metadata
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs
git commit -m "feat(Phase5h): add MetadataServiceCollectionExtensions.AddBindingStatusKernel()"
```

---

### Task 6: CapabilityBindingStatusContributor + Capability DI

**Files:**
- Create: `framework/src/CrestCreates.Capability/CapabilityBindingStatusContributor.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityBindingStatusContributorTests.cs`
- Modify: `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// framework/test/CrestCreates.Capability.Tests/CapabilityBindingStatusContributorTests.cs
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityBindingStatusContributorTests
{
    private static CapabilityDescriptor CreateDescriptor(string id = "test.cap", string? inputSchemaId = null,
        int? inputSchemaVersion = null, string? outputSchemaId = null, int? outputSchemaVersion = null)
    {
        return new CapabilityDescriptor
        {
            Id = id,
            Name = "Test Capability",
            Version = 1,
            State = DescriptorState.Active,
            InputSchema = inputSchemaId != null
                ? new VersionedDescriptorRef<SchemaDescriptor> { Id = inputSchemaId, Version = inputSchemaVersion ?? 1 }
                : null,
            OutputSchema = outputSchemaId != null
                ? new VersionedDescriptorRef<SchemaDescriptor> { Id = outputSchemaId, Version = outputSchemaVersion ?? 1 }
                : null
        };
    }

    [Fact]
    public void Evaluate_NoHandler_ReturnsUnbound()
    {
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var handlerResolver = new Mock<ICapabilityHandlerResolver>();
        handlerResolver.Setup(r => r.Resolve("test.cap")).Returns((ICapabilityHandlerInvoker?)null);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(new SchemaDescriptor { Id = "input", Name = "Input", Version = 1,
                Fields = Array.Empty<SchemaFieldDescriptor>() });

        var contributor = new CapabilityBindingStatusContributor(
            capRegistry.Object, handlerResolver.Object, schemaRegistry.Object);
        var descriptor = CreateDescriptor("test.cap", "input", 1, "output", 1);

        var result = contributor.Evaluate(descriptor);

        result.Status.Should().Be(DescriptorBindingStatus.Unbound);
        result.Issues.Should().Contain(i => i.Code == "BIND_NO_HANDLER");
    }

    [Fact]
    public void Evaluate_MissingSchemaRef_ReturnsInvalid()
    {
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var handlerResolver = new Mock<ICapabilityHandlerResolver>();
        handlerResolver.Setup(r => r.Resolve("test.cap")).Returns(Mock.Of<ICapabilityHandlerInvoker>());
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion("missing_input", 1)).Returns((SchemaDescriptor?)null);
        schemaRegistry.Setup(r => r.GetByVersion("output", 1))
            .Returns(new SchemaDescriptor { Id = "output", Name = "Output", Version = 1,
                Fields = Array.Empty<SchemaFieldDescriptor>() });

        var contributor = new CapabilityBindingStatusContributor(
            capRegistry.Object, handlerResolver.Object, schemaRegistry.Object);
        var descriptor = CreateDescriptor("test.cap", "missing_input", 1, "output", 1);

        var result = contributor.Evaluate(descriptor);

        result.Status.Should().Be(DescriptorBindingStatus.Invalid);
        result.Issues.Should().Contain(i => i.Code == "REF_MISSING_INPUT_SCHEMA");
    }

    [Fact]
    public void Evaluate_HandlerAndSchemas_ReturnsRuntimeReady()
    {
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var handlerResolver = new Mock<ICapabilityHandlerResolver>();
        handlerResolver.Setup(r => r.Resolve("test.cap")).Returns(Mock.Of<ICapabilityHandlerInvoker>());
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion("input", 1))
            .Returns(new SchemaDescriptor { Id = "input", Name = "Input", Version = 1,
                Fields = Array.Empty<SchemaFieldDescriptor>() });
        schemaRegistry.Setup(r => r.GetByVersion("output", 1))
            .Returns(new SchemaDescriptor { Id = "output", Name = "Output", Version = 1,
                Fields = Array.Empty<SchemaFieldDescriptor>() });

        var contributor = new CapabilityBindingStatusContributor(
            capRegistry.Object, handlerResolver.Object, schemaRegistry.Object);
        var descriptor = CreateDescriptor("test.cap", "input", 1, "output", 1);

        var result = contributor.Evaluate(descriptor);

        result.Status.Should().Be(DescriptorBindingStatus.RuntimeReady);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests --filter "FullyQualifiedName~CapabilityBindingStatusContributorTests"
```

Expected: FAIL — `CapabilityBindingStatusContributor` not found.

- [ ] **Step 3: Implement CapabilityBindingStatusContributor**

```csharp
// framework/src/CrestCreates.Capability/CapabilityBindingStatusContributor.cs
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Capability;

public sealed class CapabilityBindingStatusContributor : IDescriptorBindingStatusContributor
{
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly ICapabilityHandlerResolver _handlerResolver;
    private readonly ISchemaRegistry _schemaRegistry;

    public CapabilityBindingStatusContributor(
        ICapabilityRegistry capabilityRegistry,
        ICapabilityHandlerResolver handlerResolver,
        ISchemaRegistry schemaRegistry)
    {
        _capabilityRegistry = capabilityRegistry;
        _handlerResolver = handlerResolver;
        _schemaRegistry = schemaRegistry;
    }

    public DescriptorKind SupportedKind => DescriptorKind.Capability;
    public int Order => 10;

    public IReadOnlyList<IDescriptor> GetDescriptors()
    {
        // RegistryBase.GetAll() returns empty when not built
        return _capabilityRegistry.GetAll().Cast<IDescriptor>().ToList();
    }

    public DescriptorBindingReport Evaluate(IDescriptor descriptor)
    {
        var cap = (CapabilityDescriptor)descriptor;
        var issues = new List<DescriptorBindingIssue>();

        // Check registry built
        if (_capabilityRegistry.State != RegistryState.Built)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "BIND_REGISTRY_NOT_BUILT",
                $"Capability registry is not built.", cap.FullId, DescriptorKind.Capability));
        }

        // Check schema refs
        if (cap.InputSchema != null)
        {
            var schema = _schemaRegistry.GetByVersion(cap.InputSchema.Id, cap.InputSchema.Version);
            if (schema == null)
            {
                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_INPUT_SCHEMA",
                    $"Input schema '{cap.InputSchema.Id}' v{cap.InputSchema.Version} not found.",
                    cap.FullId, DescriptorKind.Capability, "InputSchema"));
            }
        }

        if (cap.OutputSchema != null)
        {
            var schema = _schemaRegistry.GetByVersion(cap.OutputSchema.Id, cap.OutputSchema.Version);
            if (schema == null)
            {
                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_OUTPUT_SCHEMA",
                    $"Output schema '{cap.OutputSchema.Id}' v{cap.OutputSchema.Version} not found.",
                    cap.FullId, DescriptorKind.Capability, "OutputSchema"));
            }
        }

        // Check handler exists
        var handler = _handlerResolver.Resolve(cap.Id);
        if (handler == null)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "BIND_NO_HANDLER",
                $"No handler registered for capability '{cap.Id}'.",
                cap.FullId, DescriptorKind.Capability));
        }

        var status = BindingStatusSynthesizer.SynthesizeStatus(issues);
        return new DescriptorBindingReport
        {
            DescriptorId = cap.FullId,
            DescriptorKind = DescriptorKind.Capability,
            Status = status,
            Issues = issues
        };
    }
}
```

- [ ] **Step 4: Add DI registrations to CapabilityServiceCollectionExtensions**

Add to `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs` inside `AddCapabilityRuntime()`:

After line 85 (`services.AddSingleton<IBootstrapValidator, CapabilitySchemaValidator>();`), append:

```csharp
        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, CapabilityBindingStatusContributor>();

        // Capability Registry (for binding status contributors)
        services.TryAddSingleton<ICapabilityRegistry, CapabilityRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<CapabilityDescriptor>,
            RegistryValidationEngine<CapabilityDescriptor>>();

        // ICapabilityHandlerResolver bridging (populated by source generator [ModuleInitializer])
        services.TryAddSingleton<ICapabilityHandlerResolver>(_ =>
            CapabilityHandlerResolverProvider.GetResolver()
            ?? throw new InvalidOperationException(
                "CapabilityHandlerResolver not initialized by source generator."));
```

Also add the `using` at top if not already present:
```csharp
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests --filter "FullyQualifiedName~CapabilityBindingStatusContributorTests"
```

Expected: 3 passed.

- [ ] **Step 6: Run existing Capability tests for regression**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests
```

Expected: All existing tests pass.

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Capability/CapabilityBindingStatusContributor.cs \
        framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs \
        framework/test/CrestCreates.Capability.Tests/CapabilityBindingStatusContributorTests.cs
git commit -m "feat(Phase5h): add CapabilityBindingStatusContributor with handler + schema binding checks"
```

---

### Task 7: FormBindingStatusContributor + Form DI

**Files:**
- Create: `framework/src/CrestCreates.Form/FormBindingStatusContributor.cs`
- Create: `framework/test/CrestCreates.Form.Tests/FormBindingStatusContributorTests.cs`
- Modify: `framework/src/CrestCreates.Form/FormServiceCollectionExtensions.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// framework/test/CrestCreates.Form.Tests/FormBindingStatusContributorTests.cs
using CrestCreates.Form;
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormBindingStatusContributorTests
{
    private static FormDescriptor CreateForm(string id = "test.form", string schemaId = "test.schema", int schemaVersion = 1,
        params FormFieldDescriptor[] fields)
    {
        return new FormDescriptor
        {
            Id = id, Name = "Test Form", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor> { Id = schemaId, Version = schemaVersion },
            Fields = fields.ToList()
        };
    }

    private static SchemaDescriptor CreateSchema(string id, int version, params SchemaFieldDescriptor[] fields)
    {
        return new SchemaDescriptor { Id = id, Name = "Test Schema", Version = version, Fields = fields.ToList() };
    }

    [Fact]
    public void Evaluate_MissingSchemaVersion_ReturnsInvalid()
    {
        var formRegistry = new Mock<IFormRegistry>();
        formRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion("missing", 1)).Returns((SchemaDescriptor?)null);

        var contributor = new FormBindingStatusContributor(formRegistry.Object, schemaRegistry.Object);
        var form = CreateForm(schemaId: "missing");

        var result = contributor.Evaluate(form);

        result.Status.Should().Be(DescriptorBindingStatus.Invalid);
        result.Issues.Should().Contain(i => i.Code == "REF_MISSING_SCHEMA_VERSION");
    }

    [Fact]
    public void Evaluate_RequiredSchemaFieldMissing_ReturnsPartiallyBound()
    {
        var formRegistry = new Mock<IFormRegistry>();
        formRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion("test.schema", 1))
            .Returns(CreateSchema("test.schema", 1,
                new SchemaFieldDescriptor("name", SchemaFieldType.String) { IsRequired = true }));

        var contributor = new FormBindingStatusContributor(formRegistry.Object, schemaRegistry.Object);
        var form = CreateForm(); // no fields

        var result = contributor.Evaluate(form);

        result.Status.Should().Be(DescriptorBindingStatus.PartiallyBound);
        result.Issues.Should().Contain(i => i.Code == "BIND_MISSING_REQUIRED_FIELD");
    }

    [Fact]
    public void Evaluate_ValidFormAndSchema_ReturnsRuntimeReady()
    {
        var formRegistry = new Mock<IFormRegistry>();
        formRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion("test.schema", 1))
            .Returns(CreateSchema("test.schema", 1,
                new SchemaFieldDescriptor("name", SchemaFieldType.String)));

        var contributor = new FormBindingStatusContributor(formRegistry.Object, schemaRegistry.Object);
        var form = CreateForm(fields: new FormFieldDescriptor { SchemaFieldName = "name" });

        var result = contributor.Evaluate(form);

        result.Status.Should().Be(DescriptorBindingStatus.RuntimeReady);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test framework/test/CrestCreates.Form.Tests --filter "FullyQualifiedName~FormBindingStatusContributorTests"
```

Expected: FAIL.

- [ ] **Step 3: Implement FormBindingStatusContributor**

```csharp
// framework/src/CrestCreates.Form/FormBindingStatusContributor.cs
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Form;

public sealed class FormBindingStatusContributor : IDescriptorBindingStatusContributor
{
    private readonly IFormRegistry _formRegistry;
    private readonly ISchemaRegistry _schemaRegistry;

    public FormBindingStatusContributor(IFormRegistry formRegistry, ISchemaRegistry schemaRegistry)
    {
        _formRegistry = formRegistry;
        _schemaRegistry = schemaRegistry;
    }

    public DescriptorKind SupportedKind => DescriptorKind.Form;
    public int Order => 20;

    public IReadOnlyList<IDescriptor> GetDescriptors()
    {
        return _formRegistry.GetAll().Cast<IDescriptor>().ToList();
    }

    public DescriptorBindingReport Evaluate(IDescriptor descriptor)
    {
        var form = (FormDescriptor)descriptor;
        var issues = new List<DescriptorBindingIssue>();

        // Check registry built
        if (_formRegistry.State != RegistryState.Built)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "BIND_REGISTRY_NOT_BUILT",
                "Form registry is not built.", form.FullId, DescriptorKind.Form));
        }

        // Check schema version exists
        var schema = _schemaRegistry.GetByVersion(form.Schema.Id, form.Schema.Version);
        if (schema == null)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA_VERSION",
                $"Schema '{form.Schema.Id}' v{form.Schema.Version} not found.",
                form.FullId, DescriptorKind.Form, "Schema"));
        }
        else
        {
            // Check all form fields exist in schema
            var schemaFieldNames = new HashSet<string>(schema.Fields.Select(f => f.Name));
            foreach (var field in form.Fields)
            {
                if (!schemaFieldNames.Contains(field.SchemaFieldName))
                {
                    issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA_FIELD",
                        $"Form field '{field.SchemaFieldName}' not found in schema '{form.Schema.Id}' v{form.Schema.Version}.",
                        form.FullId, DescriptorKind.Form, $"Fields.{field.SchemaFieldName}"));
                }
            }

            // Check required schema fields present in form (warning only)
            var formFieldNames = new HashSet<string>(form.Fields.Select(f => f.SchemaFieldName));
            foreach (var schemaField in schema.Fields.Where(f => f.IsRequired))
            {
                if (!formFieldNames.Contains(schemaField.Name))
                {
                    issues.Add(new DescriptorBindingIssue(ValidationSeverity.Warning, "BIND_MISSING_REQUIRED_FIELD",
                        $"Required schema field '{schemaField.Name}' is missing from form.",
                        form.FullId, DescriptorKind.Form, $"Fields.{schemaField.Name}"));
                }
            }
        }

        var status = BindingStatusSynthesizer.SynthesizeStatus(issues);
        return new DescriptorBindingReport
        {
            DescriptorId = form.FullId,
            DescriptorKind = DescriptorKind.Form,
            Status = status,
            Issues = issues
        };
    }
}
```

- [ ] **Step 4: Add contributor DI to FormServiceCollectionExtensions**

Append to `AddFormKernel()` in `framework/src/CrestCreates.Form/FormServiceCollectionExtensions.cs`, before `return services;`:

```csharp
        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, FormBindingStatusContributor>();
```

Add `using CrestCreates.Metadata.Abstractions;` if not present.

- [ ] **Step 5: Run tests**

```bash
dotnet test framework/test/CrestCreates.Form.Tests --filter "FullyQualifiedName~FormBindingStatusContributorTests"
```

Expected: 3 passed.

- [ ] **Step 6: Regression**

```bash
dotnet test framework/test/CrestCreates.Form.Tests
```

Expected: All 32 existing + 3 new = 35 passed.

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Form/FormBindingStatusContributor.cs \
        framework/src/CrestCreates.Form/FormServiceCollectionExtensions.cs \
        framework/test/CrestCreates.Form.Tests/FormBindingStatusContributorTests.cs
git commit -m "feat(Phase5h): add FormBindingStatusContributor with schema field binding checks"
```

---

### Task 8: HumanTaskBindingStatusContributor + HumanTask DI

**Files:**
- Create: `framework/src/CrestCreates.HumanTask/HumanTaskBindingStatusContributor.cs`
- Create: `framework/test/CrestCreates.HumanTask.Tests/HumanTaskBindingStatusContributorTests.cs`
- Modify: `framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// framework/test/CrestCreates.HumanTask.Tests/HumanTaskBindingStatusContributorTests.cs
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskBindingStatusContributorTests
{
    private static HumanTaskDescriptor CreateTask(string id = "test.task",
        AssigneeStrategy strategy = AssigneeStrategy.SingleUser,
        CompletionOutcome[]? outcomes = null)
    {
        return new HumanTaskDescriptor
        {
            Id = id, Name = "Test Task", Version = 1,
            AssigneeStrategy = strategy,
            Outcomes = outcomes?.ToList() ?? new List<CompletionOutcome>()
        };
    }

    [Fact]
    public void Evaluate_RoundRobin_ReturnsUnsupported()
    {
        var taskRegistry = new Mock<IHumanTaskRegistry>();
        taskRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var formRegistry = new Mock<IFormRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();

        var contributor = new HumanTaskBindingStatusContributor(
            taskRegistry.Object, formRegistry.Object, schemaRegistry.Object, capRegistry.Object);
        var task = CreateTask(strategy: AssigneeStrategy.RoundRobin);

        var result = contributor.Evaluate(task);

        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
        result.Issues.Should().Contain(i => i.Code == "UNSUPPORTED_ASSIGNEE_STRATEGY");
    }

    [Fact]
    public void Evaluate_LeastLoaded_ReturnsUnsupported()
    {
        var taskRegistry = new Mock<IHumanTaskRegistry>();
        taskRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var formRegistry = new Mock<IFormRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();

        var contributor = new HumanTaskBindingStatusContributor(
            taskRegistry.Object, formRegistry.Object, schemaRegistry.Object, capRegistry.Object);
        var task = CreateTask(strategy: AssigneeStrategy.LeastLoaded);

        var result = contributor.Evaluate(task);

        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
    }

    [Fact]
    public void Evaluate_SingleUser_ReturnsRuntimeReady()
    {
        var taskRegistry = new Mock<IHumanTaskRegistry>();
        taskRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var formRegistry = new Mock<IFormRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();

        var contributor = new HumanTaskBindingStatusContributor(
            taskRegistry.Object, formRegistry.Object, schemaRegistry.Object, capRegistry.Object);
        var task = CreateTask(strategy: AssigneeStrategy.SingleUser);

        var result = contributor.Evaluate(task);

        result.Status.Should().Be(DescriptorBindingStatus.RuntimeReady);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test framework/test/CrestCreates.HumanTask.Tests --filter "FullyQualifiedName~HumanTaskBindingStatusContributorTests"
```

Expected: FAIL — `HumanTaskBindingStatusContributor` not found.

- [ ] **Step 3: Implement HumanTaskBindingStatusContributor**

```csharp
// framework/src/CrestCreates.HumanTask/HumanTaskBindingStatusContributor.cs
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class HumanTaskBindingStatusContributor : IDescriptorBindingStatusContributor
{
    private readonly IHumanTaskRegistry _taskRegistry;
    private readonly IFormRegistry _formRegistry;
    private readonly ISchemaRegistry _schemaRegistry;
    private readonly ICapabilityRegistry _capabilityRegistry;

    private static readonly HashSet<AssigneeStrategy> UnsupportedStrategies = new()
    {
        AssigneeStrategy.RoundRobin,
        AssigneeStrategy.LeastLoaded
    };

    public HumanTaskBindingStatusContributor(
        IHumanTaskRegistry taskRegistry,
        IFormRegistry formRegistry,
        ISchemaRegistry schemaRegistry,
        ICapabilityRegistry capabilityRegistry)
    {
        _taskRegistry = taskRegistry;
        _formRegistry = formRegistry;
        _schemaRegistry = schemaRegistry;
        _capabilityRegistry = capabilityRegistry;
    }

    public DescriptorKind SupportedKind => DescriptorKind.HumanTask;
    public int Order => 30;

    public IReadOnlyList<IDescriptor> GetDescriptors()
    {
        return _taskRegistry.GetAll().Cast<IDescriptor>().ToList();
    }

    public DescriptorBindingReport Evaluate(IDescriptor descriptor)
    {
        var task = (HumanTaskDescriptor)descriptor;
        var issues = new List<DescriptorBindingIssue>();

        if (_taskRegistry.State != RegistryState.Built)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "BIND_REGISTRY_NOT_BUILT",
                "HumanTask registry is not built.", task.FullId, DescriptorKind.HumanTask));
        }

        // Check interaction form exists
        if (task.Interaction != null)
        {
            var form = _formRegistry.GetByVersion(task.Interaction.Id, task.Interaction.Version);
            if (form == null)
            {
                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_INTERACTION",
                    $"Interaction form '{task.Interaction.Id}' v{task.Interaction.Version} not found.",
                    task.FullId, DescriptorKind.HumanTask, "Interaction"));
            }
        }

        // Check schema refs
        if (task.InputSchema != null)
        {
            var schema = _schemaRegistry.GetByVersion(task.InputSchema.Id, task.InputSchema.Version);
            if (schema == null)
            {
                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA",
                    $"Input schema '{task.InputSchema.Id}' v{task.InputSchema.Version} not found.",
                    task.FullId, DescriptorKind.HumanTask, "InputSchema"));
            }
        }

        if (task.OutputSchema != null)
        {
            var schema = _schemaRegistry.GetByVersion(task.OutputSchema.Id, task.OutputSchema.Version);
            if (schema == null)
            {
                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA",
                    $"Output schema '{task.OutputSchema.Id}' v{task.OutputSchema.Version} not found.",
                    task.FullId, DescriptorKind.HumanTask, "OutputSchema"));
            }
        }

        // Check capability outcomes refs
        if (task.Outcomes != null)
        {
            foreach (var outcome in task.Outcomes)
            {
                if (outcome.Capability != null)
                {
                    var cap = _capabilityRegistry.GetById(outcome.Capability.Id);
                    if (cap == null)
                    {
                        issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_CAPABILITY",
                            $"Outcome capability '{outcome.Capability.Id}' not found.",
                            task.FullId, DescriptorKind.HumanTask, "Outcomes"));
                    }
                }
            }
        }

        // Check assignee strategy support
        if (UnsupportedStrategies.Contains(task.AssigneeStrategy))
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "UNSUPPORTED_ASSIGNEE_STRATEGY",
                $"Assignee strategy '{task.AssigneeStrategy}' is not supported by the current runtime.",
                task.FullId, DescriptorKind.HumanTask, "AssigneeStrategy"));
        }

        var status = BindingStatusSynthesizer.SynthesizeStatus(issues);
        return new DescriptorBindingReport
        {
            DescriptorId = task.FullId,
            DescriptorKind = DescriptorKind.HumanTask,
            Status = status,
            Issues = issues
        };
    }
}
```

- [ ] **Step 4: Add contributor + registry DI to HumanTaskServiceCollectionExtensions**

Replace `framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs` content:

```csharp
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.HumanTask;

public static class HumanTaskServiceCollectionExtensions
{
    public static IServiceCollection AddHumanTaskRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<IHumanTaskInstanceStore, InMemoryHumanTaskInstanceStore>();
        services.TryAddScoped<IHumanTaskRuntime, DefaultHumanTaskRuntime>();
        services.TryAddScoped<IHumanTaskAssigneeResolver, DefaultHumanTaskAssigneeResolver>();

        // HumanTask Registry (for binding status contributors)
        services.TryAddSingleton<IHumanTaskRegistry, HumanTaskRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<HumanTaskDescriptor>,
            RegistryValidationEngine<HumanTaskDescriptor>>();

        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, HumanTaskBindingStatusContributor>();

        return services;
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test framework/test/CrestCreates.HumanTask.Tests --filter "FullyQualifiedName~HumanTaskBindingStatusContributorTests"
```

Expected: 3 passed.

- [ ] **Step 6: Regression**

```bash
dotnet test framework/test/CrestCreates.HumanTask.Tests
```

Expected: All 44 existing + 3 new = 47 passed.

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/HumanTaskBindingStatusContributor.cs \
        framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs \
        framework/test/CrestCreates.HumanTask.Tests/HumanTaskBindingStatusContributorTests.cs
git commit -m "feat(Phase5h): add HumanTaskBindingStatusContributor with assignee strategy + ref checks"
```

---

### Task 9: WorkflowBindingStatusContributor + Workflow DI

**Files:**
- Create: `framework/src/CrestCreates.Workflow/WorkflowBindingStatusContributor.cs`
- Create: `framework/test/CrestCreates.Workflow.Tests/WorkflowBindingStatusContributorTests.cs`
- Modify: `framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// framework/test/CrestCreates.Workflow.Tests/WorkflowBindingStatusContributorTests.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowBindingStatusContributorTests
{
    [Fact]
    public void Evaluate_MissingCapabilityTarget_ReturnsInvalid()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        wfRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.GetById(It.IsAny<string>())).Returns((CapabilityDescriptor?)null);
        var taskRegistry = new Mock<IHumanTaskRegistry>();

        var contributor = new WorkflowBindingStatusContributor(
            wfRegistry.Object, schemaRegistry.Object, capRegistry.Object, taskRegistry.Object);

        var wf = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "missing", Version = 1 } } }
            }
        };

        var result = contributor.Evaluate(wf);
        result.Status.Should().Be(DescriptorBindingStatus.Invalid);
        result.Issues.Should().Contain(i => i.Code == "REF_MISSING_TARGET");
    }

    [Fact]
    public void Evaluate_SubWorkflowTarget_ReturnsUnsupported()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        wfRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();
        var taskRegistry = new Mock<IHumanTaskRegistry>();

        var contributor = new WorkflowBindingStatusContributor(
            wfRegistry.Object, schemaRegistry.Object, capRegistry.Object, taskRegistry.Object);

        var wf = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new SubWorkflowTarget { SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor> { Id = "child", Version = 1 } } }
            }
        };

        var result = contributor.Evaluate(wf);
        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
        result.Issues.Should().Contain(i => i.Code == "UNSUPPORTED_SUBWORKFLOW");
    }

    [Fact]
    public void Evaluate_Retry_ReturnsUnsupported()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        wfRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.GetById("test.cap"))
            .Returns(new CapabilityDescriptor { Id = "test.cap", Name = "Test", Version = 1 });
        var taskRegistry = new Mock<IHumanTaskRegistry>();

        var contributor = new WorkflowBindingStatusContributor(
            wfRegistry.Object, schemaRegistry.Object, capRegistry.Object, taskRegistry.Object);

        var wf = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "test.cap", Version = 1 } },
                    OnError = StepErrorBehavior.Retry }
            }
        };

        var result = contributor.Evaluate(wf);
        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
        result.Issues.Should().Contain(i => i.Code == "UNSUPPORTED_RETRY");
    }

    [Fact]
    public void Evaluate_Compensate_ReturnsUnsupported()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        wfRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.GetById("test.cap"))
            .Returns(new CapabilityDescriptor { Id = "test.cap", Name = "Test", Version = 1 });
        var taskRegistry = new Mock<IHumanTaskRegistry>();

        var contributor = new WorkflowBindingStatusContributor(
            wfRegistry.Object, schemaRegistry.Object, capRegistry.Object, taskRegistry.Object);

        var wf = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "test.cap", Version = 1 } },
                    OnError = StepErrorBehavior.Compensate }
            }
        };

        var result = contributor.Evaluate(wf);
        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
        result.Issues.Should().Contain(i => i.Code == "UNSUPPORTED_COMPENSATE");
    }

    [Fact]
    public void Evaluate_Transitions_ReturnsUnsupported()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        wfRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.GetById("test.cap"))
            .Returns(new CapabilityDescriptor { Id = "test.cap", Name = "Test", Version = 1 });
        var taskRegistry = new Mock<IHumanTaskRegistry>();

        var contributor = new WorkflowBindingStatusContributor(
            wfRegistry.Object, schemaRegistry.Object, capRegistry.Object, taskRegistry.Object);

        var wf = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "test.cap", Version = 1 } },
                    Transitions = new List<string> { "step2" } }
            }
        };

        var result = contributor.Evaluate(wf);
        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
        result.Issues.Should().Contain(i => i.Code == "UNSUPPORTED_TRANSITIONS");
    }

    [Fact]
    public void Evaluate_SupportedSteps_ReturnsRuntimeReady()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        wfRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.GetById("test.cap"))
            .Returns(new CapabilityDescriptor { Id = "test.cap", Name = "Test", Version = 1 });
        var taskRegistry = new Mock<IHumanTaskRegistry>();

        var contributor = new WorkflowBindingStatusContributor(
            wfRegistry.Object, schemaRegistry.Object, capRegistry.Object, taskRegistry.Object);

        var wf = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "test.cap", Version = 1 } },
                    OnError = StepErrorBehavior.Fail }
            }
        };

        var result = contributor.Evaluate(wf);
        result.Status.Should().Be(DescriptorBindingStatus.RuntimeReady);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test framework/test/CrestCreates.Workflow.Tests --filter "FullyQualifiedName~WorkflowBindingStatusContributorTests"
```

Expected: FAIL — `WorkflowBindingStatusContributor` not found.

- [ ] **Step 3: Implement WorkflowBindingStatusContributor**

```csharp
// framework/src/CrestCreates.Workflow/WorkflowBindingStatusContributor.cs
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowBindingStatusContributor : IDescriptorBindingStatusContributor
{
    private readonly IWorkflowRegistry _workflowRegistry;
    private readonly ISchemaRegistry _schemaRegistry;
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly IHumanTaskRegistry _humanTaskRegistry;

    public WorkflowBindingStatusContributor(
        IWorkflowRegistry workflowRegistry,
        ISchemaRegistry schemaRegistry,
        ICapabilityRegistry capabilityRegistry,
        IHumanTaskRegistry humanTaskRegistry)
    {
        _workflowRegistry = workflowRegistry;
        _schemaRegistry = schemaRegistry;
        _capabilityRegistry = capabilityRegistry;
        _humanTaskRegistry = humanTaskRegistry;
    }

    public DescriptorKind SupportedKind => DescriptorKind.Workflow;
    public int Order => 40;

    public IReadOnlyList<IDescriptor> GetDescriptors()
    {
        return _workflowRegistry.GetAll().Cast<IDescriptor>().ToList();
    }

    public DescriptorBindingReport Evaluate(IDescriptor descriptor)
    {
        var wf = (WorkflowDescriptor)descriptor;
        var issues = new List<DescriptorBindingIssue>();

        if (_workflowRegistry.State != RegistryState.Built)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "BIND_REGISTRY_NOT_BUILT",
                "Workflow registry is not built.", wf.FullId, DescriptorKind.Workflow));
        }

        // Check VariableSchema ref
        if (wf.VariableSchema != null)
        {
            var schema = _schemaRegistry.GetByVersion(wf.VariableSchema.Id, wf.VariableSchema.Version);
            if (schema == null)
            {
                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA",
                    $"Variable schema '{wf.VariableSchema.Id}' v{wf.VariableSchema.Version} not found.",
                    wf.FullId, DescriptorKind.Workflow, "VariableSchema"));
            }
        }

        // Check step targets and unsupported features
        if (wf.Steps != null)
        {
            foreach (var step in wf.Steps)
            {
                switch (step.Target)
                {
                    case CapabilityTarget capTarget:
                        if (capTarget.Capability != null)
                        {
                            var cap = _capabilityRegistry.GetById(capTarget.Capability.Id);
                            if (cap == null)
                            {
                                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_TARGET",
                                    $"Capability target '{capTarget.Capability.Id}' not found.",
                                    wf.FullId, DescriptorKind.Workflow, $"Steps[{step.Id}].Target"));
                            }
                        }
                        break;
                    case HumanTaskTarget taskTarget:
                        if (taskTarget.HumanTask != null)
                        {
                            var task = _humanTaskRegistry.GetById(taskTarget.HumanTask.Id);
                            if (task == null)
                            {
                                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_TARGET",
                                    $"HumanTask target '{taskTarget.HumanTask.Id}' not found.",
                                    wf.FullId, DescriptorKind.Workflow, $"Steps[{step.Id}].Target"));
                            }
                        }
                        break;
                    case SubWorkflowTarget:
                        issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "UNSUPPORTED_SUBWORKFLOW",
                            $"Step '{step.Id}' uses SubWorkflowTarget which is not supported by the current runtime.",
                            wf.FullId, DescriptorKind.Workflow, $"Steps[{step.Id}].Target"));
                        break;
                }

                if (step.OnError == StepErrorBehavior.Retry)
                {
                    issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "UNSUPPORTED_RETRY",
                        $"Step '{step.Id}' uses Retry which is not supported by the current runtime.",
                        wf.FullId, DescriptorKind.Workflow, $"Steps[{step.Id}].OnError"));
                }

                if (step.OnError == StepErrorBehavior.Compensate)
                {
                    issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "UNSUPPORTED_COMPENSATE",
                        $"Step '{step.Id}' uses Compensate which is not supported by the current runtime.",
                        wf.FullId, DescriptorKind.Workflow, $"Steps[{step.Id}].OnError"));
                }

                if (step.Transitions?.Count > 0)
                {
                    issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "UNSUPPORTED_TRANSITIONS",
                        $"Step '{step.Id}' has transitions which are not supported by the current runtime.",
                        wf.FullId, DescriptorKind.Workflow, $"Steps[{step.Id}].Transitions"));
                }
            }
        }

        var status = BindingStatusSynthesizer.SynthesizeStatus(issues);
        return new DescriptorBindingReport
        {
            DescriptorId = wf.FullId,
            DescriptorKind = DescriptorKind.Workflow,
            Status = status,
            Issues = issues
        };
    }
}
```

- [ ] **Step 4: Add contributor + registry DI to WorkflowServiceCollectionExtensions**

In `AddWorkflowEngine()`, after line 17 (`services.TryAddScoped<WorkflowCompatibilityValidator>();`), add:

```csharp

        // Workflow Registry (for binding status contributors)
        services.TryAddSingleton<IWorkflowRegistry, WorkflowRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<WorkflowDescriptor>,
            RegistryValidationEngine<WorkflowDescriptor>>();

        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, WorkflowBindingStatusContributor>();
```

Add `using CrestCreates.Metadata;` and `using CrestCreates.Metadata.Abstractions;` at top.

- [ ] **Step 5: Run tests**

```bash
dotnet test framework/test/CrestCreates.Workflow.Tests --filter "FullyQualifiedName~WorkflowBindingStatusContributorTests"
```

- [ ] **Step 6: Regression**

```bash
dotnet test framework/test/CrestCreates.Workflow.Tests
```

Expected: All 57 existing pass, plus new tests.

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Workflow/WorkflowBindingStatusContributor.cs \
        framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs \
        framework/test/CrestCreates.Workflow.Tests/WorkflowBindingStatusContributorTests.cs
git commit -m "feat(Phase5h): add WorkflowBindingStatusContributor with step target + feature support checks"
```

---

### Task 10: EventBindingStatusContributor + EventServiceCollectionExtensions + EventRegistry Bridging

**Files:**
- Create: `framework/src/CrestCreates.Event/EventBindingStatusContributor.cs`
- Create: `framework/src/CrestCreates.Event/EventServiceCollectionExtensions.cs`
- Create: `framework/test/CrestCreates.Event.Tests/EventBindingStatusContributorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// framework/test/CrestCreates.Event.Tests/EventBindingStatusContributorTests.cs
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Event.Tests;

public class EventBindingStatusContributorTests
{
    [Fact]
    public void Evaluate_RegistryNotBuilt_ReturnsUnbound()
    {
        var eventRegistry = new Mock<IEventRegistry>();
        eventRegistry.Setup(r => r.State).Returns(RegistryState.Created);
        var schemaRegistry = new Mock<ISchemaRegistry>();

        var contributor = new EventBindingStatusContributor(eventRegistry.Object, schemaRegistry.Object);
        var evt = new GeneratedEventDescriptor { Id = "test.event", Name = "Test", Version = 1, State = DescriptorState.Active };

        var result = contributor.Evaluate(evt);

        result.Status.Should().Be(DescriptorBindingStatus.Unbound);
        result.Issues.Should().Contain(i => i.Code == "BIND_REGISTRY_NOT_BUILT");
    }

    [Fact]
    public void Evaluate_Deprecated_ReturnsPartiallyBound()
    {
        var eventRegistry = new Mock<IEventRegistry>();
        eventRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var schemaRegistry = new Mock<ISchemaRegistry>();

        var contributor = new EventBindingStatusContributor(eventRegistry.Object, schemaRegistry.Object);
        var evt = new GeneratedEventDescriptor { Id = "test.event", Name = "Test", Version = 1, State = DescriptorState.Deprecated };

        var result = contributor.Evaluate(evt);

        result.Status.Should().Be(DescriptorBindingStatus.PartiallyBound);
        result.Issues.Should().Contain(i => i.Code == "WARN_DEPRECATED");
    }

    [Fact]
    public void Evaluate_Removed_ReturnsUnsupported()
    {
        var eventRegistry = new Mock<IEventRegistry>();
        eventRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var schemaRegistry = new Mock<ISchemaRegistry>();

        var contributor = new EventBindingStatusContributor(eventRegistry.Object, schemaRegistry.Object);
        var evt = new GeneratedEventDescriptor { Id = "test.event", Name = "Test", Version = 1, State = DescriptorState.Removed };

        var result = contributor.Evaluate(evt);

        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
        result.Issues.Should().Contain(i => i.Code == "UNSUPPORTED_REMOVED");
    }

    [Fact]
    public void Evaluate_ActiveWithSchema_ReturnsRuntimeReady()
    {
        var eventRegistry = new Mock<IEventRegistry>();
        eventRegistry.Setup(r => r.State).Returns(RegistryState.Built);
        var schemaRegistry = new Mock<ISchemaRegistry>();

        var contributor = new EventBindingStatusContributor(eventRegistry.Object, schemaRegistry.Object);
        var evt = new GeneratedEventDescriptor { Id = "test.event", Name = "Test", Version = 1, State = DescriptorState.Active };

        var result = contributor.Evaluate(evt);

        result.Status.Should().Be(DescriptorBindingStatus.RuntimeReady);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test framework/test/CrestCreates.Event.Tests --filter "FullyQualifiedName~EventBindingStatusContributorTests"
```

Expected: FAIL.

- [ ] **Step 3: Implement EventBindingStatusContributor**

```csharp
// framework/src/CrestCreates.Event/EventBindingStatusContributor.cs
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Event;

public sealed class EventBindingStatusContributor : IDescriptorBindingStatusContributor
{
    private readonly IEventRegistry _eventRegistry;
    private readonly ISchemaRegistry _schemaRegistry;

    public EventBindingStatusContributor(IEventRegistry eventRegistry, ISchemaRegistry schemaRegistry)
    {
        _eventRegistry = eventRegistry;
        _schemaRegistry = schemaRegistry;
    }

    public DescriptorKind SupportedKind => DescriptorKind.Event;
    public int Order => 50;

    public IReadOnlyList<IDescriptor> GetDescriptors()
    {
        return _eventRegistry.GetAll().Cast<IDescriptor>().ToList();
    }

    public DescriptorBindingReport Evaluate(IDescriptor descriptor)
    {
        var evt = (GeneratedEventDescriptor)descriptor;
        var issues = new List<DescriptorBindingIssue>();

        // Check registry built
        if (_eventRegistry.State != RegistryState.Built)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "BIND_REGISTRY_NOT_BUILT",
                "Event registry is not built.", evt.FullId, DescriptorKind.Event));
        }

        // Check state
        if (evt.State == DescriptorState.Deprecated)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Warning, "WARN_DEPRECATED",
                $"Event '{evt.Name}' is deprecated.", evt.FullId, DescriptorKind.Event));
        }
        else if (evt.State == DescriptorState.Removed)
        {
            issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "UNSUPPORTED_REMOVED",
                $"Event '{evt.Name}' has been removed.", evt.FullId, DescriptorKind.Event));
        }

        // Check payload schema ref (skip for Removed — already flagged)
        if (evt.State != DescriptorState.Removed && evt.PayloadSchemaRef != null)
        {
            var schema = _schemaRegistry.GetByVersion(evt.PayloadSchemaRef.Id, evt.PayloadSchemaRef.Version);
            if (schema == null)
            {
                issues.Add(new DescriptorBindingIssue(ValidationSeverity.Error, "REF_MISSING_SCHEMA",
                    $"Payload schema '{evt.PayloadSchemaRef.Id}' v{evt.PayloadSchemaRef.Version} not found.",
                    evt.FullId, DescriptorKind.Event, "PayloadSchemaRef"));
            }
        }

        var status = BindingStatusSynthesizer.SynthesizeStatus(issues);
        return new DescriptorBindingReport
        {
            DescriptorId = evt.FullId,
            DescriptorKind = DescriptorKind.Event,
            Status = status,
            Issues = issues
        };
    }
}
```

- [ ] **Step 4: Create EventServiceCollectionExtensions with same-instance bridging**

```csharp
// framework/src/CrestCreates.Event/EventServiceCollectionExtensions.cs
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Event;

public static class EventServiceCollectionExtensions
{
    public static IServiceCollection AddEventKernel(this IServiceCollection services)
    {
        // Same-instance bridging: concrete first, then interface resolves to same instance.
        // EventRegistryBootstrapper constructor takes EventRegistry (concrete).
        services.TryAddSingleton<EventRegistry>();
        services.TryAddSingleton<IEventRegistry>(sp => sp.GetRequiredService<EventRegistry>());
        services.TryAddSingleton<IEventMetadataProvider>(sp => sp.GetRequiredService<EventRegistry>());

        // Validation engine
        services.TryAddSingleton<IRegistryValidationEngine<GeneratedEventDescriptor>,
            RegistryValidationEngine<GeneratedEventDescriptor>>();

        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, EventBindingStatusContributor>();

        return services;
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test framework/test/CrestCreates.Event.Tests --filter "FullyQualifiedName~EventBindingStatusContributorTests"
```

Expected: 4 passed.

- [ ] **Step 6: Regression**

```bash
dotnet test framework/test/CrestCreates.Event.Tests
```

Expected: All existing tests pass.

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.Event/EventBindingStatusContributor.cs \
        framework/src/CrestCreates.Event/EventServiceCollectionExtensions.cs \
        framework/test/CrestCreates.Event.Tests/EventBindingStatusContributorTests.cs
git commit -m "feat(Phase5h): add EventBindingStatusContributor + EventRegistry same-instance bridging DI"
```

---

### Task 10b: Integration Test — Real Registries Round-Trip

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/RuntimeBindingStatusIntegrationTests.cs`

- [ ] **Step 1: Write integration test**

```csharp
// framework/test/CrestCreates.Metadata.Tests/RuntimeBindingStatusIntegrationTests.cs
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class RuntimeBindingStatusIntegrationTests
{
    [Fact]
    public void GetAllStatuses_BuiltRegistries_ReturnsNonEmptyReport()
    {
        // Arrange: register all registries and contributors in DI
        var services = new ServiceCollection();

        // Registries
        services.AddSchemaKernel();
        services.AddFormKernel();
        services.AddHumanTaskRuntime();
        services.AddWorkflowEngine();
        services.AddCapabilityRuntime();
        services.AddEventKernel();

        // Binding status kernel
        services.AddBindingStatusKernel();

        // Mock handler resolver (needed by CapabilityBindingStatusContributor)
        services.AddSingleton<ICapabilityHandlerResolver>(_ =>
        {
            var mock = new Mock<ICapabilityHandlerResolver>();
            mock.Setup(r => r.Resolve(It.IsAny<string>())).Returns(Mock.Of<ICapabilityHandlerInvoker>());
            return mock.Object;
        });

        var sp = services.BuildServiceProvider();

        // Build all registries via MetadataBootstrapper using DI-resolved instances
        var schemaRegistry = sp.GetRequiredService<ISchemaRegistry>();
        var formRegistry = sp.GetRequiredService<IFormRegistry>();
        var humanTaskRegistry = sp.GetRequiredService<IHumanTaskRegistry>();
        var workflowRegistry = sp.GetRequiredService<IWorkflowRegistry>();
        var eventRegistry = sp.GetRequiredService<IEventRegistry>();

        // Register providers with minimal descriptors
        var schemaDescriptor = new SchemaDescriptor
        {
            Id = "test.schema", Name = "Test Schema", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new("name", SchemaFieldType.String)
            }
        };
        DescriptorProviderRegistry.Register<SchemaDescriptor>(
            new SingleDescriptorProvider<SchemaDescriptor>(schemaDescriptor));

        var formDescriptor = new FormDescriptor
        {
            Id = "test.form", Name = "Test Form", Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "test.schema", Version = 1 },
            Fields = new List<FormFieldDescriptor>
            {
                new() { SchemaFieldName = "name" }
            }
        };
        DescriptorProviderRegistry.Register<FormDescriptor>(
            new SingleDescriptorProvider<FormDescriptor>(formDescriptor));

        var capDescriptor = new CapabilityDescriptor
        {
            Id = "test.cap", Name = "Test Capability", Version = 1
        };
        DescriptorProviderRegistry.Register<CapabilityDescriptor>(
            new SingleDescriptorProvider<CapabilityDescriptor>(capDescriptor));

        var taskDescriptor = new HumanTaskDescriptor
        {
            Id = "test.task", Name = "Test Task", Version = 1,
            AssigneeStrategy = AssigneeStrategy.SingleUser
        };
        DescriptorProviderRegistry.Register<HumanTaskDescriptor>(
            new SingleDescriptorProvider<HumanTaskDescriptor>(taskDescriptor));

        var wfDescriptor = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test Workflow", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "test.cap", Version = 1 } },
                    OnError = StepErrorBehavior.Fail }
            }
        };
        DescriptorProviderRegistry.Register<WorkflowDescriptor>(
            new SingleDescriptorProvider<WorkflowDescriptor>(wfDescriptor));

        var eventDescriptor = new GeneratedEventDescriptor
        {
            Id = "test.event", Name = "Test Event", Version = 1, State = DescriptorState.Active,
            PayloadType = typeof(string)
        };
        DescriptorProviderRegistry.Register<GeneratedEventDescriptor>(
            new SingleDescriptorProvider<GeneratedEventDescriptor>(eventDescriptor));

        // Build all registries (same DI instances)
        MetadataBootstrapper.BuildAll(schemaRegistry, formRegistry, humanTaskRegistry,
            workflowRegistry, eventRegistry);

        // Act
        var provider = sp.GetRequiredService<IDescriptorRuntimeBindingStatusProvider>();
        var report = provider.GetAllStatuses();

        // Assert
        report.Descriptors.Should().NotBeEmpty("built registries should produce descriptor reports");
        report.Descriptors.Should().HaveCountGreaterThanOrEqualTo(5,
            "at least one descriptor per kind (Schema excluded — no contributor)");
        report.Descriptors.Should().Contain(d => d.DescriptorKind == DescriptorKind.Form
            && d.Status == DescriptorBindingStatus.RuntimeReady);
        report.Descriptors.Should().Contain(d => d.DescriptorKind == DescriptorKind.Capability
            && d.Status == DescriptorBindingStatus.RuntimeReady);
    }

    [Fact]
    public void GetAllStatuses_RegistriesNotBuilt_ReturnsEmptyReport()
    {
        var services = new ServiceCollection();
        services.AddSchemaKernel();
        services.AddFormKernel();
        services.AddHumanTaskRuntime();
        services.AddWorkflowEngine();
        services.AddCapabilityRuntime();
        services.AddEventKernel();
        services.AddBindingStatusKernel();

        // Mock handler resolver (Capability contributor requires it)
        services.AddSingleton<ICapabilityHandlerResolver>(_ =>
        {
            var mock = new Mock<ICapabilityHandlerResolver>();
            mock.Setup(r => r.Resolve(It.IsAny<string>())).Returns(Mock.Of<ICapabilityHandlerInvoker>());
            return mock.Object;
        });

        var sp = services.BuildServiceProvider();

        // Do NOT call BuildAll() — registries are Created

        var provider = sp.GetRequiredService<IDescriptorRuntimeBindingStatusProvider>();
        var report = provider.GetAllStatuses();

        // Registries not built → GetDescriptors() returns empty from each contributor → empty report
        report.Descriptors.Should().BeEmpty();
    }

    /// <summary>Minimal IDescriptorProvider that returns a single descriptor.</summary>
    private sealed class SingleDescriptorProvider<T> : IDescriptorProvider<T> where T : class, IDescriptor
    {
        private readonly T _descriptor;
        public SingleDescriptorProvider(T descriptor) => _descriptor = descriptor;
        public IEnumerable<T> GetDescriptors() { yield return _descriptor; }
    }
}
```

- [ ] **Step 2: Run integration tests**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~RuntimeBindingStatusIntegrationTests"
```

Expected: 2 tests PASS — non-empty report for built registries, empty for unbuilt.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/RuntimeBindingStatusIntegrationTests.cs
git commit -m "test(Phase5h): add integration test — real registries GetAllStatuses round-trip"
```

---

### Task 11: SchemaRegistry DI Registration

**Files:**
- Create: `framework/src/CrestCreates.Schema/SchemaServiceCollectionExtensions.cs`

- [ ] **Step 1: Create SchemaServiceCollectionExtensions**

```csharp
// framework/src/CrestCreates.Schema/SchemaServiceCollectionExtensions.cs
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Schema;

public static class SchemaServiceCollectionExtensions
{
    public static IServiceCollection AddSchemaKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<ISchemaRegistry, SchemaRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<SchemaDescriptor>,
            RegistryValidationEngine<SchemaDescriptor>>();
        return services;
    }
}
```

- [ ] **Step 2: Build verification**

```bash
dotnet build framework/src/CrestCreates.Schema
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Schema/SchemaServiceCollectionExtensions.cs
git commit -m "feat(Phase5h): add SchemaRegistry DI registration"
```

---

### Task 12: Full Regression + Build

- [ ] **Step 1: Full build**

```bash
dotnet build
```

Expected: 0 errors.

- [ ] **Step 2: Run all affected test suites**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests
dotnet test framework/test/CrestCreates.Form.Tests
dotnet test framework/test/CrestCreates.HumanTask.Tests
dotnet test framework/test/CrestCreates.Workflow.Tests
dotnet test framework/test/CrestCreates.Capability.Tests
dotnet test framework/test/CrestCreates.Event.Tests
```

Expected minimum counts:
- Metadata: ≥95 (85 existing + 10 new)
- Form: ≥35 (32 existing + 3 new)
- HumanTask: ≥47 (44 existing + 3 new)
- Workflow: ≥59 (57 existing + 2+ new)
- Capability: All existing + 3 new
- Event: All existing + 4 new

- [ ] **Step 3: Fix any regressions, then commit**

```bash
git commit -am "chore(Phase5h): full regression — all suites pass, 0 build errors"
```

---

### Task 13: Update memory.md + GitHub Issue

- [ ] **Step 1: Update memory.md**

Add to `Platform Status` section:

```markdown
### Runtime Binding Status (Phase 5h, 2026-06-12)

- `DescriptorBindingStatus` enum: RuntimeReady, PartiallyBound, Unbound, Unsupported, Invalid
- `IDescriptorBindingStatusContributor` per-module evaluator + enumerator pattern
- `IDescriptorRuntimeBindingStatusProvider.GetStatus()` / `GetAllStatuses()`
- Contributors for Capability, Form, HumanTask, Workflow, Event
- All registries now registered in DI (Schema, Workflow, Event, HumanTask, Capability)
- EventRegistry same-instance bridging for EventRegistryBootstrapper
- `ICapabilityHandlerResolver` DI bridge from source generator provider
- 0 runtime execution changes, 0 MetadataBootstrapper changes
```

- [ ] **Step 2: Post completion comment to GitHub issue #4**

```bash
gh issue comment 4 --body "Phase 5h delivered. Summary..." 
```

- [ ] **Step 3: Commit**

```bash
git add memory.md
git commit -m "docs: update memory.md with Phase 5h Runtime Binding Status completion"
```

---

## Task Dependency Order

```
Task 1 (Core models) → Task 2 (Interfaces) → Task 3 (Synthesizer) → Task 4 (Provider) → Task 5 (Metadata DI)
                                                                                           ↓
Task 6 (Capability contributor) ─┐
Task 7 (Form contributor) ───────┤
Task 8 (HumanTask contributor) ──┼── all depend on Tasks 1-5
Task 9 (Workflow contributor) ──┤
Task 10 (Event contributor) ────┘
Task 10b (Integration test) ────┘
Task 11 (Schema DI) ← independent

Task 12 (Regression) ← depends on all above
Task 13 (Docs + Issue) ← depends on Task 12 passing
```

Tasks 6-10b can be parallelized (all depend only on Tasks 1-5 being complete).

---

**Plan complete.** Each task includes: exact file paths, full code, test code, run commands with expected output, and commit instructions.
