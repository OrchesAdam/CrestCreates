# Event System — Metadata Bridge & Persistent DLQ Design (Phase 2a)

**Date**: 2026-06-09
**Status**: Design Approved

## Core Principle

> **Phase 2a establishes `EventRegistry` as the authoritative runtime metadata source for all event publication and consumption. Source generators populate the registry, validators enforce registry contracts, and dead-letter persistence records registry-defined events. No event-related runtime component may bypass the `EventRegistry` once validation is enabled.**

This is the foundation for all future runtimes (Workflow, Capability, HumanTask, AI Metadata Explorer).

## Problem Statement

The audit identifies an architectural gap: the EventDescriptor metadata layer and the EventBus infrastructure layer have no runtime connection. EventPublishingMiddleware writes `"capability.succeeded"` as a bare string with no registry validation. PayloadSchema exists but is never populated. The `IEventIdempotencyStore` interface exists but is not wired into distributed consumers. `ILocalDeadLetterStore` is in-memory only, losing all DLQ events on process restart.

Phase 1 (basic publish/subscribe) is complete. Phase 2a closes the metadata–infrastructure gap and adds persistent DLQ.

## Architecture: B+ (Registry-First, Generator Populates Registry)

Three phases of execution, all flowing through a single authoritative `EventRegistry`:

```
                    COMPILE TIME                    STARTUP                    RUNTIME

                [CrestEvent] class                                         PublishAsync(name, payload)
                      │                                                           │
                      ▼                                                           ▼
              Source Generator                                          IEventValidator.ValidateOrThrow()
                      │                                                   ├─ name registered?
                      ▼                                                   ├─ scope matches?
          GeneratedEventDescriptorProvider                                └─ state is Active?
                      │                                                           │
                      ▼                                                           ▼
         ModuleAutoInitializer.g.cs                                          EventBus.Dispatch()
          .TryAddEnumerable<IEventDescriptorProvider>()                            │
                      │                                                           ▼
                      ▼                                                   (handler fails)
              EventRegistry.Build()                                              │
              (one-shot, validates collisions, fail-fast)                        ▼
                      │                                                   IDeadLetterStore.EnqueueAsync()
                      ▼                                                   (DeadLetterMessage carries
                EventRegistry                                                     EventName, Scope, ExceptionType)
              (frozen, authoritative)
```

### Three-Layer Architecture

| Layer | Mechanism | Role |
|-------|-----------|------|
| **DSL Layer** | `[CrestEvent]` attribute | Human-friendly, declarative, module-level |
| **Compilation Layer** | Source Generator (IIncrementalGenerator) | Transforms DSL → `IEventDescriptorProvider`, resolves references |
| **Runtime Layer** | `EventRegistry` + `IEventValidator` | Authoritative, queryable, enforceable, injectable |

### Hard Boundary

```
Attribute → string (CapabilityId, event name)    ← DSL
Generator → Translator                           ← Compilation
Registry  → VersionedDescriptorRef<T>, Type      ← Canonical Model
```

No strings cross from the Attribute layer into the Registry layer.

---

## Design Section 1: Enhanced EventDescriptor Model

Six orthogonal dimensions, each answering a distinct question:

| Dimension | Question Answered |
|-----------|-------------------|
| Identity | What is it? |
| Payload | What does it carry? |
| Scope | How far can it travel? |
| Reliability | Can it be lost? |
| Direction | Who sends, who receives? |
| Ownership | Who is responsible for it? |

### The Model

```csharp
// CrestCreates.Event.Abstractions/EventDescriptor.cs (revised)

public sealed record EventDescriptor : IVersionedDescriptor
{
    // ── 1. Identity ──
    public string Id { get; init; }                 // Deterministic: SHA256(Name + ":" + Version). Survives type renames.
    public string Name { get; init; }              // e.g. "capability.succeeded"
    public int Version { get; init; }
    public DescriptorState State { get; init; }
    public string? Description { get; init; }

    // ── 2. Payload ──
    public Type PayloadType { get; init; }          // Dev-time / generator helper
    public VersionedDescriptorRef<SchemaDescriptor> PayloadSchemaRef { get; init; }  // Authoritative at runtime

    // ── 3. Scope (transmission boundary) ──
    public EventScope Scope { get; init; }          // Local | Domain | Integration

    // ── 4. Reliability (contract; future strategy driver) ──
    public EventReliability Reliability { get; init; }  // BestEffort | AtLeastOnce | Idempotent

    // ── 5. Direction (topology metadata, not a runtime constraint) ──
    public EventDirection Direction { get; init; }      // Internal | Incoming | Outgoing

    // ── 6. Ownership ──
    public VersionedDescriptorRef<CapabilityDescriptor>? CapabilityRef { get; init; }
    public string? CreatedBy { get; init; }          // e.g. "CapabilityRuntime", "WorkflowRuntime"

    // ── Classification ──
    public EventImportance Importance { get; init; }    // Low | Normal | High | Critical

    // ── Operational flags ──
    public bool IsAuditable { get; init; }
    public bool IsReplayable { get; init; }
    public bool IsPublic { get; init; }             // Exposed via Dynamic API / metadata endpoint

    // ── Compatibility ──
    public SchemaChangeKind ChangeKind { get; init; }

    // ── Topology (reserved, Phase 3+) ──
    public IReadOnlyList<string> Producers { get; init; } = [];
    public IReadOnlyList<string> Consumers { get; init; } = [];
}
```

### New Enums

```csharp
public enum EventScope      { Local, Domain, Integration }
public enum EventDirection  { Internal, Incoming, Outgoing }
public enum EventReliability { BestEffort, AtLeastOnce, Idempotent }  // Idempotent = AtLeastOnce + dedup
public enum EventImportance { Low, Normal, High, Critical }
```

### Runtime Constraints (enforced by IEventValidator)

| Rule | Check |
|------|-------|
| `Scope.Local` | Rejected if published to RabbitMQ/Kafka |
| `Scope.Domain` | Rejected if published cross-process |
| `Scope.Integration` | PayloadType must be JSON-serializable |
| `State.Deprecated` or `State.Removed` | Rejected; error message includes `SupersededById` |
| Unregistered event name | Rejected; error message guides to `[CrestEvent]` |

`Direction` is **documentation/topology metadata only** — it does not constrain publishing. Publishing constraints come from Scope.

### Deferred from Phase 2a

- `EventCategory` and `EventSemantic` — removed from the model. If stable categories emerge from Capability Runtime usage, they can be added later without breaking the contract.
- `Producers`/`Consumers` as `DescriptorRef` — Phase 3. Strings are placeholders.
- `CapabilityRef` runtime resolution — Phase 3 (needs Capability Registry).
- `PayloadSchema` auto-generation from `PayloadType` — Phase 3 (needs Schema Framework).

---

## Design Section 2: Source Generator & Auto-Registration

### The Attribute

```csharp
// CrestCreates.Event.Abstractions

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CrestEventAttribute : Attribute
{
    public string Name { get; init; }                   // "capability.succeeded"
    public EventScope Scope { get; init; }              // required, no default
    public EventReliability Reliability { get; init; }  // AtLeastOnce (default)
    public EventDirection Direction { get; init; }      // Internal (default)
    public EventImportance Importance { get; init; }    // Normal (default)
    public string? Description { get; init; }
    public bool IsAuditable { get; init; }
    public bool IsReplayable { get; init; }
    public bool IsPublic { get; init; }
    public string? CapabilityId { get; init; }          // DSL layer: string only
}
```

### Usage Example

```csharp
[CrestEvent(
    Name = "capability.succeeded",
    Scope = EventScope.Integration,
    Reliability = EventReliability.Idempotent,
    Direction = EventDirection.Outgoing,
    Importance = EventImportance.High,
    IsAuditable = true,
    IsReplayable = true,
    CapabilityId = "capability-runtime.v1")]
public sealed record CapabilitySucceeded(
    string CapabilityName,
    int CapabilityVersion,
    string CorrelationId,
    string Status,
    int DurationMs);
```

### Provider Interface

```csharp
// CrestCreates.Event.Abstractions/IEventDescriptorProvider.cs

public interface IEventDescriptorProvider
{
    IReadOnlyList<EventDescriptor> GetDescriptors();
}
```

### What the Generator Emits (per-project, into obj/generated/)

```csharp
// GeneratedEventDescriptorProvider.g.cs

public sealed class GeneratedEventDescriptorProvider : IEventDescriptorProvider
{
    public IReadOnlyList<EventDescriptor> GetDescriptors() => [
        new EventDescriptor
        {
            Id = "evt_6A9D8F3C...",  // Stable deterministic ID (not derived from Name)
            Name = "capability.succeeded",
            Version = 1,
            State = DescriptorState.Active,
            PayloadType = typeof(CapabilitySucceeded),
            PayloadSchemaRef = null,  // Phase 3: SchemaRegistry.Resolve<CapabilitySucceeded>()
            Scope = EventScope.Integration,
            Reliability = EventReliability.Idempotent,
            Direction = EventDirection.Outgoing,
            Importance = EventImportance.High,
            CapabilityRef = new VersionedDescriptorRef<CapabilityDescriptor>("cap-runtime", 1),
            IsAuditable = true,
            IsReplayable = true,
            IsPublic = false,
            ChangeKind = SchemaChangeKind.None
        }
    ];
}
```

### Auto-Discovery (No Manual Register Required)

The generator also emits into `ModuleAutoInitializer.g.cs`:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<
        IEventDescriptorProvider,
        GeneratedEventDescriptorProvider>());
```

`EventRegistry.Build()` runs once at startup and calls `GetDescriptors()` on all DI-registered providers.

### Manual Escape Hatch

`EventRegistry.RegisterDynamic(EventDescriptor)` remains available for dynamically-defined events (e.g., tenant-custom events loaded from configuration at runtime). These are stored separately from generated descriptors but participate in all queries. **Phase 2a dynamic descriptors are always Version=1.** Versioned dynamic descriptors are not supported — generated events handle schema evolution. > Phase 3 should consider a distinct `DynamicEventDescriptor` type rather than reusing `EventDescriptor`, since generated (versioned) and dynamic (unversioned) events are semantically different.

---

## Design Section 3: EventRegistry — Build, Validate, Freeze

### Two-Category Model: Generated vs Dynamic

Generated descriptors (produced by source generators) and dynamically registered descriptors are distinct categories. Source-generator-produced descriptors are validated and frozen during `Build()`. Dynamically registered descriptors remain supported for tenant-defined or configuration-driven events and are stored separately from generated descriptors. Registry queries operate over the union of both sets.

### One-Shot Build

`Build()` succeeds exactly once for the generated category. After building, generated descriptors are immutable.

```csharp
// CrestCreates.Event/EventRegistry.cs (revised)

public sealed class EventRegistry : IEventRegistry
{
    private readonly ConcurrentDictionary<string, EventDescriptor> _generatedById = new();
    private readonly ConcurrentDictionary<string, EventDescriptor> _dynamicById = new();
    // ... (shared indexes by name, category, etc.)
    public RegistryState State { get; private set; } = RegistryState.Created;

    // Called once at startup for generated descriptors
    public void Build(IEnumerable<IEventDescriptorProvider> providers)
    {
        if (State == RegistryState.Built)
            return;  // Idempotent — multiple modules may trigger Build()

        State = RegistryState.Building;

        var descriptors = providers.SelectMany(p => p.GetDescriptors()).ToList();

        try
        {
            ValidateNoDuplicateNameVersions(descriptors);
            ValidateSingleActiveVersion(descriptors);
            // Future: ValidateCapabilityRefs(descriptors);  // Phase 3

            foreach (var descriptor in descriptors)
                RegisterGenerated(descriptor);

            State = RegistryState.Built;
        }
        catch
        {
            State = RegistryState.Failed;
            throw;
        }
    }

    // Runtime: dynamic/tenant-custom events (not frozen, always Version=1)
    // Only allowed after Build() completes — rejects during Building or Failed states
    public bool RegisterDynamic(string name, Type payloadType, EventScope scope = EventScope.Integration)
    {
        if (State != RegistryState.Built)
            throw new InvalidOperationException(
                $"Cannot register dynamic events while registry state is {State}. " +
                "Dynamic registration is only allowed after Build() completes.");
        var descriptor = new EventDescriptor
        {
            Id = GenerateId(name, version: 1),  // SHA256(Name + ":" + Version)
            Name = name,
            Version = 1,   // Locked — no version evolution for dynamic events
            State = DescriptorState.Active,
            PayloadType = payloadType,
            Scope = scope,
            Direction = EventDirection.Internal,
            Importance = EventImportance.Normal
        };

        // Generated wins on conflict (checked by name + version, not Id)
        if (_generatedByNameVersion.ContainsKey((descriptor.Name, descriptor.Version)))
            return false;

        _dynamicById[descriptor.Id] = descriptor;
        return true;
    }

    private void RegisterGenerated(EventDescriptor descriptor)
    {
        _generatedById[descriptor.Id] = descriptor;
        // ... (populate shared indexes)
    }
```

### Name → Version Resolution

When multiple versions of an event exist, `GetByName` returns the active version with the highest version number. `GetLatestVersion` returns the highest version regardless of state — used by the validator to distinguish "not registered" from "all versions deprecated":

```csharp
// Returns the active descriptor with the highest version, or null
public EventDescriptor? GetByName(string name)
    => _byName.TryGetValue(name, out var versions)
        ? versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version)
        : null;

// Returns the highest-version descriptor regardless of state, or null
public EventDescriptor? GetLatestVersion(string name)
    => _byName.TryGetValue(name, out var versions)
        ? versions.MaxBy(v => v.Version)
        : null;

// Exact version lookup
public EventDescriptor? GetByNameAndVersion(string name, int version)
    => _byName.TryGetValue(name, out var versions)
        ? versions.FirstOrDefault(v => v.Version == version)
        : null;
```

**Validator state resolution:**
```
GetLatestVersion(name) == null     → NotRegistered
GetLatestVersion(name).State == Deprecated → Deprecated
GetLatestVersion(name).State == Removed    → Removed
GetLatestVersion(name).State == Active     → OK (accept)
```

This ensures "v1 Deprecated + v2 Deprecated" returns `Deprecated`, not `NotRegistered`. The validator always checks against the latest version's state, while `GetByName()` returns the active version for consumers.

### Build-Time Validation

**Single Active Version rule:** At most one version of an event may be `Active` at any time. When v2 is registered as Active, v1 must be Deprecated. If v1 is Active and a new v1 is registered (same name + version), it's a duplicate and fails. If v2 is registered as Active while v1 is still Active, `Build()` throws — the module must explicitly deprecate v1 first. This guarantees the validator's `GetLatestVersion()` → Active branch always resolves to a single unambiguous descriptor.

    private static void ValidateNoDuplicateNameVersions(List<EventDescriptor> descriptors)
    {
        var duplicates = descriptors
            .GroupBy(d => (d.Name, d.Version))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Name} v{g.Key.Version}")
            .ToList();

        if (duplicates.Count > 0)
            throw new EventRegistryBuildException(
                $"Duplicate (name, version) pairs detected: {string.Join(", ", duplicates)}. " +
                "Each (name, version) pair must be declared by exactly one module. " +
                "Use a new Version to evolve an existing event name.");
    }
}
```

### RegistryState

```csharp
public enum RegistryState { Created, Building, Built, Failed }
```

Future registries (Capability, Workflow, HumanTask) follow the same state model.

### Lifecycle

```
                        ┌── dynamic Only ──► RegisterDynamic() (requires State == Built)
                        │
Created ──► Building ──► Built (Generated Frozen)
               │
               └── Failed (no recovery; process must restart)
```

Generated descriptors are frozen at startup. Dynamic descriptors can be added at runtime. Both are queryable through the same `IEventRegistry` interface.

**Precedence rule:** If a dynamic descriptor conflicts with a generated descriptor (same name + version), the generated descriptor wins — `RegisterDynamic()` returns `false` and the conflict is logged.

Error at build time, not at runtime.

### Hosted Service

`EventRegistry` is built by a hosted service at application start, ensuring fail-fast before any requests are served:

```csharp
// CrestCreates.Event/EventRegistryHostedService.cs

public sealed class EventRegistryHostedService : IHostedService
{
    private readonly EventRegistry _registry;
    private readonly IEnumerable<IEventDescriptorProvider> _providers;

    public EventRegistryHostedService(
        EventRegistry registry,
        IEnumerable<IEventDescriptorProvider> providers)
    {
        _registry = registry;
        _providers = providers;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _registry.Build(_providers);  // Fail-fast on collision
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

Registration: `services.AddHostedService<EventRegistryHostedService>()` in `EventModule`.

> **HostedService is transitional.** `IHostedService` works for Phase 2a's single registry, but does not guarantee ordering across multiple registries. Phase 3 will introduce `IRegistryBootstrapper` or `ModuleBootstrapper` with explicit dependency ordering. `EventRegistry` is the root registry — all others depend on it directly or transitively.

---

## Design Section 4: Validation Architecture

### Interface

```csharp
// CrestCreates.Event.Abstractions/IEventValidator.cs

public interface IEventValidator
{
    void ValidateOrThrow(string eventName, object? payload);
    ValidationResult Validate(string eventName, object? payload);
}

public sealed record ValidationResult(
    bool IsValid,
    EventValidationError ErrorCode,
    EventDescriptor? Descriptor);

public enum EventValidationError
{
    None,
    NotRegistered,
    Deprecated,
    Removed,
    InvalidScope,
    InvalidPayload       // Phase 3
}
```

### Single Implementation

`RegistryEventValidator` in `CrestCreates.Event`:

```csharp
public sealed class RegistryEventValidator : IEventValidator
{
    private readonly IEventRegistry _registry;
    private readonly BusKind _busKind;  // injected: Local | Distributed

    public RegistryEventValidator(IEventRegistry registry, BusKind busKind)
    {
        _registry = registry;
        _busKind = busKind;
    }

    public void ValidateOrThrow(string eventName, object? payload)
    {
        if (_registry.State != RegistryState.Built)
            throw new InvalidOperationException(
                "EventRegistry has not been built yet. Publish cannot occur before Build completes.");

        // Defensive: multiple Active versions indicate registry corruption
        var activeCount = _registry.GetAllByName(eventName)
            .Count(d => d.State == DescriptorState.Active);
        if (activeCount > 1)
            throw new InvalidOperationException(
                $"Registry corruption: event '{eventName}' has {activeCount} Active versions. " +
                "Only one Active version is permitted per event name.");

        var latest = _registry.GetLatestVersion(eventName);
        if (latest is null)
            throw new EventValidationException(
                $"Event '{eventName}' is not registered. " +
                "Apply [CrestEvent] to the event class or register via EventRegistryProvider.");

        if (latest.State == DescriptorState.Deprecated)
            throw new EventValidationException(
                $"Event '{eventName}' is deprecated. " +
                $"Use '{latest.SupersededById}' instead.");

        if (latest.State == DescriptorState.Removed)
            throw new EventValidationException(
                $"Event '{eventName}' has been removed.");

        // Scope boundary enforcement (uses latest version)
        if (latest.Scope == EventScope.Local && _busKind == BusKind.Distributed)
            throw new EventValidationException(
                $"Event '{eventName}' has Scope.Local — cannot publish to a distributed bus.");

        if (latest.Scope == EventScope.Domain && _busKind == BusKind.Distributed)
            throw new EventValidationException(
                $"Event '{eventName}' has Scope.Domain — cannot publish cross-process.");

        // Schema validation deferred to Phase 3
    }
}

public enum BusKind { Local, Distributed }
```

### Wired Into Bus Base Classes

```csharp
// DistributedEventBusBase (revised)
public abstract class DistributedEventBusBase : IEventBus
{
    protected readonly IEventValidator? _validator;

    protected DistributedEventBusBase(IEventValidator? validator = null)
        => _validator = validator;

    protected void ValidateOrThrow(string eventName, object? payload)
        => _validator?.ValidateOrThrow(eventName, payload);
}
```

Local bus implementations inject `IEventValidator` directly.

**IEventValidator is optional in DI.** If not registered, validation degrades gracefully for minimal projects. Documentation notes that validation-disabled mode is for development/single-node only — production clusters must enable it.

---

## Design Section 5: Dead Letter Store — Unified Abstraction

### Interface (replaces ILocalDeadLetterStore)

```csharp
// CrestCreates.EventBus.Abstractions/IDeadLetterStore.cs

public interface IDeadLetterStore
{
    Task EnqueueAsync(DeadLetterMessage message, CancellationToken ct);
    Task<IReadOnlyList<DeadLetterMessage>> GetPendingAsync(int skip, int take, CancellationToken ct);
    Task<DeadLetterMessage?> GetByIdAsync(string messageId, CancellationToken ct);
    Task MarkRetryingAsync(string messageId, CancellationToken ct);
    Task MarkRetriedAsync(string messageId, CancellationToken ct);
    Task MarkArchivedAsync(string messageId, CancellationToken ct);
    Task<int> CountAsync(DeadLetterStatus? status, CancellationToken ct);
    Task<IReadOnlyList<DeadLetterMessage>> GetByEventNameAsync(string eventName, int skip, int take, CancellationToken ct);
}
```

### Enhanced DeadLetterMessage

```csharp
public sealed record DeadLetterMessage(
    string MessageId,
    string EventName,              // "capability.succeeded" (registry-defined name)
    int EventVersion,              // ← added: event version number
    string? EventDescriptorId,     // "evt_6A9D8F..." (stable descriptor id)
    string? CorrelationId,         // correlation id for distributed tracing
    EventScope Scope,
    string PayloadTypeFullName,    // "MyApp.Events.CapabilitySucceeded" (not AQN — survives assembly version changes)
    byte[] Payload,
    string ErrorMessage,
    string? ExceptionType,         // typeof(TimeoutException).FullName
    DateTime OccurredAt,           // ← added: when the original event was created
    DateTime FailedAt,             // when the handler failed
    int RetryCount,
    int MaxRetries,
    DeadLetterStatus Status        // Pending | Retrying | Retried | Archived
);
```

### Status Lifecycle

```
Pending ──► Retrying ──► Retried
  │                        │
  └────► Archived ◄────────┘  (max retries exhausted or manually archived)
```

### Implementations

| Store | Project | Use Case |
|-------|---------|----------|
| `InMemoryDeadLetterStore` | `CrestCreates.EventBus.Local` | Development / single-node |
| `EfCoreDeadLetterStore` | `CrestCreates.EventBus.DeadLetter.EFCore` (new) | Production, persistent, queryable |

### Future Enhancement (Phase 3)

Add `PayloadSchemaRef` to `DeadLetterMessage` so the DLQ can reconstruct and display payloads using the Schema Registry. `byte[] Payload` is sufficient for Phase 2a.

---

## Design Section 6: Project Changes Summary

### New Projects

| Project | Purpose |
|---------|---------|
| `CrestCreates.EventBus.DeadLetter.EFCore` | `EfCoreDeadLetterStore` — persistent DLQ via EF Core |

### Modified Projects

| Project | Changes |
|---------|---------|
| `CrestCreates.Event.Abstractions` | Enhanced `EventDescriptor`, `[CrestEvent]`, new enums, `IEventDescriptorProvider`, `IEventValidator` |
| `CrestCreates.Event` | `EventRegistry.Build()`, `RegistryEventValidator`, collision detection |
| `CrestCreates.EventBus.Abstractions` | `IDeadLetterStore` (new), enhanced `DeadLetterMessage`, `ILocalDeadLetterStore` → `[Obsolete]` |
| `CrestCreates.EventBus.Abstract` | `DistributedEventBusBase` takes `IEventValidator` |
| `CrestCreates.EventBus.Local` | `InMemoryDeadLetterStore` implements `IDeadLetterStore`; bus takes `IEventValidator` |
| `CrestCreates.EventBus.Local.Channel` | `BackgroundChannelLocalEventBus` takes `IEventValidator` |
| `CrestCreates.EventBus.RabbitMQ` | Bus takes `IEventValidator` |
| `CrestCreates.EventBus.Kafka` | Bus takes `IEventValidator` |
| `CrestCreates.CodeGenerator` | New `EventDescriptorSourceGenerator` (IIncrementalGenerator) |

---

## Design Section 7: Implementation Order

### Steps

1. **EventDescriptor model + enums** — Event.Abstractions
2. **`[CrestEvent]` attribute** — Event.Abstractions
3. **`IEventValidator` + `RegistryEventValidator`** — Event.Abstractions + Event
4. **`IEventDescriptorProvider`** — Event.Abstractions
5. **`EventRegistry.Build()` with collision detection** — Event
6. **`IDeadLetterStore` + enhanced `DeadLetterMessage`** — EventBus.Abstractions
7. **`InMemoryDeadLetterStore` → `IDeadLetterStore` refactor** — EventBus.Local
8. **Source generator for `[CrestEvent]`** — CodeGenerator
9. **Wire `IEventValidator` into bus base classes** — EventBus.Local/Channel/RabbitMQ/Kafka
10. **`EfCoreDeadLetterStore`** — EventBus.DeadLetter.EFCore (new project)
11. **Integration tests** — validate the full chain

Steps 1–5 (metadata foundation) are independent of 6–11 (DLQ). Steps 6–7 must complete before 10–11.

---

## Design Section 8: Testing

### Unit Tests

| Area | Tests |
|------|-------|
| `EventRegistry` | Register, GetByName, GetActiveVersion, State lifecycle (Created→Building→Built), duplicate Build is idempotent |
| `RegistryState` guard | Publish before Build → throws InvalidOperationException; Publish after Build → succeeds |
| `EventRegistry.Build()` validation | Duplicate (name, version) → throws; same name + different versions → succeeds; single registration succeeds |
| `EventRegistry` version resolution | v1 Deprecated + v2 Active → GetByName returns v2; GetByNameAndVersion("name", 1) returns v1 |
| `RegistryEventValidator` | Registered event → valid; unregistered → throws; Deprecated → throws with SupersededById message; Local scope on distributed bus → throws |
| `InMemoryDeadLetterStore` | Full lifecycle: Enqueue → GetPending → MarkRetrying → MarkRetried; MarkArchived after max retries |
| `EfCoreDeadLetterStore` | Same lifecycle + query by EventName, Scope, Status |
| `GeneratedEventDescriptorProvider` | Produces correct descriptors from `[CrestEvent]` attribute inputs |

### Integration Tests

| Test | Verifies |
|------|----------|
| Full compilation chain | `[CrestEvent]` → generator → provider → registry.Build() → validator.ValidateOrThrow() → pass |
| Publish before Build | Publish before `Build()` completes → throws; ensures registry is ready before any events flow |
| Duplicate (name, version) detection | Two modules declare `"capability.succeeded"` v1 → `Build()` throws `EventRegistryBuildException` |
| Version upgrade resolution | v1 Deprecated + v2 Active → `GetByName()` returns v2; validator accepts v2, rejects v1 |
| Scope enforcement | Publish `Scope.Local` event via RabbitMQ → throws |
| DLQ metadata | Handler throws → `IDeadLetterStore` receives `DeadLetterMessage` with EventName, EventVersion, CorrelationId, OccurredAt, Scope, ExceptionType populated |

### Test Project Changes

Extend existing test projects — no new test projects needed:
- `CrestCreates.Event.Tests` — EventRegistry + Validator
- `CrestCreates.EventBus.Tests` — DLQ lifecycle
- `CrestCreates.EventBus.RabbitMQ.Tests.Integration` — scope enforcement

---

## Design Section 9: Migration Path

| What | Old | New | Strategy |
|------|-----|-----|----------|
| DLQ store interface | `ILocalDeadLetterStore` | `IDeadLetterStore` | `[Obsolete]` thin wrapper delegates to new interface; remove after 1 release cycle |
| DLQ store impl | `InMemoryDeadLetterStore` (implements `ILocalDeadLetterStore`) | Same class, implements `IDeadLetterStore` directly | Drop old interface |
| DLQ message record | `DeadLetterMessage` (5 fields) | Same record, 3 new optional fields | Additive — existing code compiles unchanged |
| Bus base class | `DistributedEventBusBase` (no validator) | Takes optional `IEventValidator` | Optional parameter — no break |
| EventPublishingMiddleware | `"capability.succeeded"` bare string | Unchanged initially | Validator catches unregistered event names at runtime |

---

## Explicitly Deferred

| Item | Target Phase |
|------|-------------|
| Runtime payload schema validation | Phase 3 (needs Schema Framework maturity) |
| `Producers`/`Consumers` as `DescriptorRef` | Phase 3 |
| `CapabilityRef` runtime resolution via Capability Registry | Phase 3 |
| `PayloadSchema` auto-generation from `PayloadType` | Phase 3 |
| `EventReliability` driving bus behavior (AtLeastOnce → Outbox) | Phase 2b (see note below) |
| `PayloadSchemaRef` in `DeadLetterMessage` | Phase 3 |
| AoT optimization (`FrozenDictionary`, `switch`-based lookup) | Phase 4 |

> **Phase 2b forward reference:** `EventReliability` is metadata only in Phase 2a. Phase 2b will introduce `DeliveryStrategy` resolution that maps `EventReliability` to transport behavior — `BestEffort` → fire-and-forget, `AtLeastOnce` → Outbox + retry, `Idempotent` → Outbox + idempotency check. The `EventReliability` enum is designed to accommodate this without schema changes.

> **Phase 3 forward reference — typed publish:** `EventPublishingMiddleware` currently uses bare strings (`"capability.succeeded"`). The validator catches unregistered names at runtime, but Phase 3 should introduce `Publish<TEvent>(TEvent evt)` with compile-time resolution from the EventRegistry. Bare-string `Publish(string, payload)` becomes `[Obsolete]` at that point. The `IEventBus` interface already reserves the generic overload: `Task PublishAsync<TEvent>(TEvent evt)` where `TEvent : IDomainEvent`. Phase 2a implementations throw `NotSupportedException`; Phase 3 will resolve `TEvent` → `EventDescriptor` via the registry.

