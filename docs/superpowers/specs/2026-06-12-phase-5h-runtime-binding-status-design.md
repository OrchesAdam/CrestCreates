# Phase 5h — Runtime Binding Status Design Spec

**Date**: 2026-06-12
**Status**: Approved
**Implementation Order**: 1. Core models → 2. Interfaces → 3. Default provider → 4. Desc-specific contributors → 5. Registry DI wiring → 6. DI extension → 7. Tests → 8. Regression
**Parent Issue**: [#4 — Phase 5h: Runtime Binding Status](https://github.com/OrchesAdam/CrestCreates/issues/4)

---

## 1. Overview

Phase 5h establishes a **read-only runtime binding status/reporting layer** that distinguishes:

```
Descriptor is structurally valid        (registry built, field-level rules passed)
```

from:

```
Descriptor is runtime-bound and executable/consumable by the current runtime
```

It lets control-plane code and LLM-generated descriptor drafts ask:

```
Is this descriptor RuntimeReady, PartiallyBound, Unbound, Unsupported, or Invalid?
```

### Design Principles

1. **Read-only** — never mutates descriptors, never throws exceptions for normal not-ready statuses
2. **Standalone system** — does not implement `IBootstrapValidator`; uses new `IDescriptorBindingStatusContributor` + `IDescriptorRuntimeBindingStatusProvider` pattern
3. **DI-injected registries** — contributors resolve typed registry interfaces (`ISchemaRegistry`, `IWorkflowRegistry`, etc.) via constructor DI
4. **AoT-friendly** — zero runtime reflection, zero script engine, all Singleton lifetime
5. **Post-build query** — runs AFTER registries are built; consumers call `GetAllStatuses()` explicitly

### Scope Boundary

Phase 5h provides the **binding query layer** — it tells you whether a descriptor is executable. It does NOT change what execution does.

---

## 2. Current State vs. Target

### Already Exists (pre-5h)

| Component | Location | Status |
|---|---|---|
| `ValidationReport` / `ValidationIssue` / `ValidationSeverity` | `CrestCreates.Metadata.Abstractions` | ✅ |
| `IBootstrapValidator` / `IBootstrapTask` | `CrestCreates.Metadata.Abstractions` | ✅ |
| `DescriptorRefValidator` | `CrestCreates.Metadata` | ✅ Static cross-ref validation |
| `FormSchemaBindingValidator` | `CrestCreates.Form` | ✅ Form→Schema field parity |
| `WorkflowCompatibilityValidator` | `CrestCreates.Workflow` | ✅ Runtime feature support check |
| `CapabilityHandlerValidator` / `CapabilitySchemaValidator` | `CrestCreates.Capability` | ✅ Bootstrap validators |
| `RegistryEventValidator` (IEventValidator) | `CrestCreates.Event` | ✅ Runtime publish validation |
| `IGlobalDescriptorRegistry` | `CrestCreates.Metadata` | ✅ Cross-kind descriptor enumeration |
| `MetadataBootstrapper.BuildAll()` | `CrestCreates.Metadata` | ✅ Sequential registry builds |

### Gap

| Missing Capability | Why Needed |
|---|---|
| Unified binding status enum | No way to ask "is this executable?" without knowing which validator to call |
| Per-descriptor binding report | Each validator returns different types (ValidationReport, exception, ValidationResult) |
| Contributor-based extensibility | No way for new descriptor kinds to plug into the binding status system |
| Runtime query API | No public `GetAllStatuses()` — each caller must manually orchestrate checks |
| DI-registered registries | Only FormRegistry is in DI; contributors need typed registry access |

---

## 3. Core Models

All new types go in `CrestCreates.Metadata.Abstractions` (interfaces/models) and `CrestCreates.Metadata` (implementations).

### 3.1 DescriptorBindingStatus (enum)

```csharp
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

### 3.2 DescriptorBindingIssue (record)

```csharp
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

### 3.3 DescriptorBindingReport (class)

```csharp
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

### 3.4 RuntimeBindingReport (class)

```csharp
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

### 3.5 Status Synthesis Rules

Given a descriptor's issues:

| Issue Code Pattern | Synthesized Status |
|---|---|
| `REF_*` error | `Invalid` |
| `BIND_*` error | `Unbound` |
| `UNSUPPORTED_*` error | `Unsupported` |
| Warning-only issues (no errors) | `PartiallyBound` |
| No issues at all | `RuntimeReady` |

Static synthesis method in `CrestCreates.Metadata`:

```csharp
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
```

---

## 4. Services & Contributor Architecture

### 4.1 IDescriptorBindingStatusContributor (interface)

```csharp
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Per-module evaluator. Each module (Capability, Form, HumanTask, Workflow, Event)
/// implements one to evaluate descriptors of its SupportedKind.
/// Singleton, stateless, receives typed registries via constructor DI.
/// </summary>
public interface IDescriptorBindingStatusContributor
{
    /// <summary>Which DescriptorKind this contributor handles.</summary>
    DescriptorKind SupportedKind { get; }

    /// <summary>Execution order (lower = earlier). Contributors are sorted before evaluation.</summary>
    int Order { get; }

    /// <summary>Evaluate a single descriptor. Must not mutate state.</summary>
    DescriptorBindingReport Evaluate(IDescriptor descriptor);
}
```

### 4.2 IDescriptorRuntimeBindingStatusProvider (interface)

```csharp
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

### 4.3 DefaultDescriptorRuntimeBindingStatusProvider (implementation)

Location: `CrestCreates.Metadata`

```csharp
public sealed class DefaultDescriptorRuntimeBindingStatusProvider
    : IDescriptorRuntimeBindingStatusProvider
{
    private readonly IReadOnlyList<IDescriptorBindingStatusContributor> _contributors;
    private readonly IGlobalDescriptorRegistry _globalRegistry;

    public DefaultDescriptorRuntimeBindingStatusProvider(
        IEnumerable<IDescriptorBindingStatusContributor> contributors,
        IGlobalDescriptorRegistry globalRegistry)
    {
        _contributors = contributors.OrderBy(c => c.Order).ToList();
        _globalRegistry = globalRegistry;
    }

    public DescriptorBindingReport GetStatus(IDescriptor descriptor)
    {
        var contributor = _contributors.FirstOrDefault(c => c.SupportedKind == descriptor.Kind);
        return contributor?.Evaluate(descriptor)
            ?? new DescriptorBindingReport
            {
                DescriptorId = descriptor.FullId,
                DescriptorKind = descriptor.Kind,
                Status = DescriptorBindingStatus.RuntimeReady,
                Issues = Array.Empty<DescriptorBindingIssue>()
            };
    }

    public RuntimeBindingReport GetAllStatuses()
    {
        var allDescriptors = _globalRegistry.GetAll();
        var reports = allDescriptors.Select(GetStatus).ToList();
        return new RuntimeBindingReport { Descriptors = reports };
    }
}
```

### 4.4 DI Registration

**Metadata module** (`CrestCreates.Metadata` — new `MetadataServiceCollectionExtensions`):

```csharp
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

**Consumer modules** (each module registers its contributor):

```csharp
// CapabilityServiceCollectionExtensions
services.AddSingleton<IDescriptorBindingStatusContributor, CapabilityBindingStatusContributor>();

// FormServiceCollectionExtensions.AddFormKernel()
services.AddSingleton<IDescriptorBindingStatusContributor, FormBindingStatusContributor>();

// HumanTaskServiceCollectionExtensions
services.AddSingleton<IDescriptorBindingStatusContributor, HumanTaskBindingStatusContributor>();

// WorkflowServiceCollectionExtensions
services.AddSingleton<IDescriptorBindingStatusContributor, WorkflowBindingStatusContributor>();

// EventServiceCollectionExtensions
services.AddSingleton<IDescriptorBindingStatusContributor, EventBindingStatusContributor>();
```

Use `AddSingleton` (not `TryAddSingleton`) to allow multiple registration of `IDescriptorBindingStatusContributor`.

### 4.5 Registry DI Prerequisite

Since contributors need typed registry access, registries must be registered in DI.
Currently only `FormRegistry` is registered. Phase 5h adds:

```csharp
// In each module's *ServiceCollectionExtensions or a new AddRegistry:
services.TryAddSingleton<ISchemaRegistry, SchemaRegistry>();
services.TryAddSingleton<IWorkflowRegistry, WorkflowRegistry>();
services.TryAddSingleton<IEventRegistry, EventRegistry>();
services.TryAddSingleton<IHumanTaskRegistry, HumanTaskRegistry>();
services.TryAddSingleton<ICapabilityRegistry, CapabilityRegistry>();

// Plus their validation engines:
services.TryAddSingleton<IRegistryValidationEngine<SchemaDescriptor>, RegistryValidationEngine<SchemaDescriptor>>();
// ... etc
```

---

## 5. Descriptor-Specific Binding Rules

### 5.1 CapabilityBindingStatusContributor

**File**: `CrestCreates.Capability/CapabilityBindingStatusContributor.cs`

Constructor DI: `ICapabilityRegistry`, `ICapabilityHandlerRegistry`, `ISchemaRegistry`

| Check | Issue Code | Severity | Status |
|---|---|---|---|
| InputSchema ref exists | `REF_MISSING_INPUT_SCHEMA` | Error | Invalid |
| OutputSchema ref exists | `REF_MISSING_OUTPUT_SCHEMA` | Error | Invalid |
| Handler mapped | `BIND_NO_HANDLER` | Error | Unbound |
| Registry built | `BIND_REGISTRY_NOT_BUILT` | Error | Unbound |
| All valid | — | — | RuntimeReady |

### 5.2 FormBindingStatusContributor

**File**: `CrestCreates.Form/FormBindingStatusContributor.cs`

Constructor DI: `IFormRegistry`, `ISchemaRegistry`

| Check | Issue Code | Severity | Status |
|---|---|---|---|
| Schema.Id + Schema.Version resolves | `REF_MISSING_SCHEMA_VERSION` | Error | Invalid |
| Every FormField.SchemaFieldName exists in SchemaDescriptor.Fields | `REF_MISSING_SCHEMA_FIELD` | Error | Invalid |
| Required schema field absent from form | `BIND_MISSING_REQUIRED_FIELD` | Warning | PartiallyBound |
| Registry built | `BIND_REGISTRY_NOT_BUILT` | Error | Unbound |
| All valid | — | — | RuntimeReady |

### 5.3 HumanTaskBindingStatusContributor

**File**: `CrestCreates.HumanTask/HumanTaskBindingStatusContributor.cs`

Constructor DI: `IHumanTaskRegistry`, `IFormRegistry`, `ISchemaRegistry`, `ICapabilityRegistry`

| Check | Issue Code | Severity | Status |
|---|---|---|---|
| Interaction resolves to FormDescriptor | `REF_MISSING_INTERACTION` | Error | Invalid |
| InputSchema/OutputSchema refs resolve | `REF_MISSING_SCHEMA` | Error | Invalid |
| Outcome capability refs resolve | `REF_MISSING_CAPABILITY` | Error | Invalid |
| RoundRobin / LeastLoaded (not implemented) | `UNSUPPORTED_ASSIGNEE_STRATEGY` | Error | Unsupported |
| Registry built | `BIND_REGISTRY_NOT_BUILT` | Error | Unbound |
| All valid | — | — | RuntimeReady |

### 5.4 WorkflowBindingStatusContributor

**File**: `CrestCreates.Workflow/WorkflowBindingStatusContributor.cs`

Constructor DI: `IWorkflowRegistry`, `ISchemaRegistry`, `ICapabilityRegistry`, `IHumanTaskRegistry`

| Check | Issue Code | Severity | Status |
|---|---|---|---|
| VariableSchema ref resolves | `REF_MISSING_SCHEMA` | Error | Invalid |
| Step targets resolve (CapabilityTarget, HumanTaskTarget) | `REF_MISSING_TARGET` | Error | Invalid |
| SubWorkflowTarget used | `UNSUPPORTED_SUBWORKFLOW` | Error | Unsupported |
| Retry configured | `UNSUPPORTED_RETRY` | Error | Unsupported |
| Compensate configured | `UNSUPPORTED_COMPENSATE` | Error | Unsupported |
| Transitions non-empty | `UNSUPPORTED_TRANSITIONS` | Error | Unsupported |
| Registry built | `BIND_REGISTRY_NOT_BUILT` | Error | Unbound |
| All valid | — | — | RuntimeReady |

### 5.5 EventBindingStatusContributor

**File**: `CrestCreates.Event/EventBindingStatusContributor.cs`

Constructor DI: `IEventRegistry`, `ISchemaRegistry`

| Check | Issue Code | Severity | Status |
|---|---|---|---|
| Registry built | `BIND_REGISTRY_NOT_BUILT` | Error | Unbound |
| PayloadSchema ref resolves | `REF_MISSING_SCHEMA` | Error | Invalid |
| State is Deprecated | `WARN_DEPRECATED` | Warning | PartiallyBound |
| State is Removed | `UNSUPPORTED_REMOVED` | Error | Unsupported |
| State is Active, schema valid | — | — | RuntimeReady |

Do NOT query subscriber registries (no first-class queryable subscriber registry exists).

---

## 6. MetadataBootstrapper Boundary

**No changes to `MetadataBootstrapper.BuildAll()`**.

- `BuildAll()` continues to handle structural validation (validators + bootstrap validators + callbacks).
- Binding status is queried separately: `provider.GetAllStatuses()` called by consumers after registries are built.
- Contributors access typed registries via DI — no new `BuildAll()` parameters needed.

---

## 7. Project Structure

### New Files (8)

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
```

### Modified Files (5+)

```
framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs  (+contributor DI, +registry DI)
framework/src/CrestCreates.Form/FormServiceCollectionExtensions.cs              (+contributor DI)
framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs    (+contributor DI, +registry DI)
framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs     (+contributor DI, +registry DI)
framework/src/CrestCreates.Event/EventServiceCollectionExtensions.cs            (+contributor DI, +registry DI)
framework/src/CrestCreates.Schema/                                              (+registry DI, new extension file)
```

### Test Files (7)

```
framework/test/CrestCreates.Metadata.Tests/
  BindingStatusSynthesizerTests.cs          (status synthesis rules)
  DefaultDescriptorRuntimeBindingStatusProviderTests.cs  (aggregation, GetStatus, GetAllStatuses)

framework/test/CrestCreates.Capability.Tests/
  CapabilityBindingStatusContributorTests.cs

framework/test/CrestCreates.Form.Tests/
  FormBindingStatusContributorTests.cs

framework/test/CrestCreates.HumanTask.Tests/
  HumanTaskBindingStatusContributorTests.cs

framework/test/CrestCreates.Workflow.Tests/
  WorkflowBindingStatusContributorTests.cs

framework/test/CrestCreates.Event.Tests/
  EventBindingStatusContributorTests.cs
```

---

## 8. Explicit Non-Goals

Phase 5h MUST NOT implement:

- Descriptor topology graph
- Impact analysis engine
- Lifecycle governance
- Package snapshot support
- Descriptor migration / version negotiation
- Runtime execution changes (no Workflow retry/compensate/branch/subworkflow)
- `WorkflowEventConsumer` recovery logic
- HumanTask claim/delegate/escalation/SLA
- HumanTask `RoundRobin` / `LeastLoaded` implementation
- Form submission runtime
- Frontend form renderer / component resolver
- Capability authorization changes
- DataPermission changes
- Event subscriber topology engine
- Kafka / RabbitMQ readiness probe
- Database persistence
- API / UI / AppService
- Runtime reflection scanning
- Service locator
- New registries or registry paths
- Modifications to `MetadataBootstrapper.BuildAll()`

---

## 9. Testing Strategy

### 9.1 Metadata Tests

| Test | Assertion |
|---|---|
| No issues → RuntimeReady | `SynthesizeStatus(empty list) == RuntimeReady` |
| REF_* error → Invalid | `SynthesizeStatus([REF_MISSING_SCHEMA error]) == Invalid` |
| BIND_* error → Unbound | `SynthesizeStatus([BIND_NO_HANDLER error]) == Unbound` |
| UNSUPPORTED_* error → Unsupported | `SynthesizeStatus([UNSUPPORTED_RETRY error]) == Unsupported` |
| Warning-only → PartiallyBound | `SynthesizeStatus([WARN_DEPRECATED warning]) == PartiallyBound` |
| Mixed errors → Invalid (worst-first synthesis) | REF_* errors take priority over UNSUPPORTED_* |
| GetAllStatuses() aggregates all descriptors | Mock IGlobalDescriptorRegistry with 3 descriptors, verify 3 reports |
| Unknown DescriptorKind → RuntimeReady | No contributor for the kind → default RuntimeReady |

### 9.2 Per-Contributor Tests (each module)

| Module | Test Cases |
|---|---|
| Capability | Missing handler → Unbound, missing schema → Invalid, handler+schema → RuntimeReady |
| Form | Missing schema field → Invalid, missing required field → PartiallyBound, valid → RuntimeReady |
| HumanTask | Missing interaction → Invalid, RoundRobin/LeastLoaded → Unsupported, valid → RuntimeReady |
| Workflow | Missing target → Invalid, SubWorkflow/Retry/Compensate/Transitions → Unsupported, valid → RuntimeReady |
| Event | Registry not built → Unbound, missing schema → Invalid, deprecated → PartiallyBound, active+schema → RuntimeReady |

### 9.3 Regression Gate

All existing test suites must pass with zero regressions:
- Form.Tests (32), Metadata.Tests (85), HumanTask.Tests (44), Workflow.Tests (57)
- Capability.Tests, Event.Tests, Organization.Tests
- Full `dotnet build` — 0 errors

---

## 10. Design Decisions

| Decision | Rationale |
|---|---|
| New `DescriptorBindingIssue` (not extend `ValidationIssue`) | Binding status is a different domain; extending `ValidationIssue` with optional fields adds noise to all existing validators |
| Standalone (not `IBootstrapValidator`) | `IBootstrapValidator.Validate()` takes zero parameters and returns `ValidationReport`; binding contributors need typed registries and return `DescriptorBindingReport` |
| DI injection for registries | Contributors need typed `ISchemaRegistry`, `IWorkflowRegistry`, etc. — DI is the established pattern (see `WorkflowCompatibilityValidator`) |
| `AddSingleton` (not `TryAddSingleton`) for contributors | Multiple contributors must co-exist for the same interface; `TryAddSingleton` would drop the second registration |
| `IGlobalDescriptorRegistry.GetAll()` for enumeration | No need for contributors to re-scan — `IGlobalDescriptorRegistry` is the established cross-kind descriptor catalog |
| No `MetadataBootstrapper` changes | Binding status is a post-build query, not a build-time validation gate. Consumers decide when to query. |
| Registry DI registration as prerequisite | Only `FormRegistry` is in DI today; contributors need all registries. One-time cleanup, backward-compatible. |

---

**Design reviewed and approved. Ready for implementation plan.**
