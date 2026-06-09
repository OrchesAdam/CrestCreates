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
    public string Id { get; init; }
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
    public EventReliability Reliability { get; init; }  // AtMostOnce | AtLeastOnce | ExactlyOnce

    // ── 5. Direction (topology metadata, not a runtime constraint) ──
    public EventDirection Direction { get; init; }      // Internal | Incoming | Outgoing

    // ── 6. Ownership ──
    public VersionedDescriptorRef<CapabilityDescriptor>? CapabilityRef { get; init; }

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
public enum EventReliability { AtMostOnce, AtLeastOnce, ExactlyOnce }
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
    Reliability = EventReliability.AtLeastOnce,
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
            Id = "evt_capability.succeeded_v1",
            Name = "capability.succeeded",
            Version = 1,
            State = DescriptorState.Active,
            PayloadType = typeof(CapabilitySucceeded),
            PayloadSchemaRef = null,  // Phase 3: SchemaRegistry.Resolve<CapabilitySucceeded>()
            Scope = EventScope.Integration,
            Reliability = EventReliability.AtLeastOnce,
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

`EventRegistryProvider.Register(EventDescriptor)` remains available for dynamically-defined events (e.g., tenant-custom events loaded from configuration at runtime).

---

## Design Section 3: EventRegistry — Build, Validate, Freeze

### One-Shot Build

`Build()` succeeds exactly once. After building, the registry is immutable.

```csharp
// CrestCreates.Event/EventRegistry.cs (revised)

public sealed class EventRegistry : IEventRegistry
{
    public bool IsBuilt { get; private set; }

    public void Build(IEnumerable<IEventDescriptorProvider> providers)
    {
        if (IsBuilt)
            return;  // Idempotent, not exceptional — multiple modules may trigger Build()

        var descriptors = providers.SelectMany(p => p.GetDescriptors()).ToList();

        ValidateNoDuplicateNames(descriptors);
        ValidateNoDuplicateVersions(descriptors);
        // Future: ValidateCapabilityRefs(descriptors);  // Phase 3

        foreach (var descriptor in descriptors)
            Register(descriptor);

        IsBuilt = true;
    }

    private static void ValidateNoDuplicateNames(List<EventDescriptor> descriptors)
    {
        var duplicates = descriptors
            .GroupBy(d => d.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw new EventRegistryBuildException(
                $"Duplicate event names detected: {string.Join(", ", duplicates)}. " +
                "Each event name must be declared by exactly one module.");
    }

    private static void ValidateNoDuplicateVersions(List<EventDescriptor> descriptors)
    {
        var duplicates = descriptors
            .GroupBy(d => (d.Name, d.Version))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Name} v{g.Key.Version}")
            .ToList();

        if (duplicates.Count > 0)
            throw new EventRegistryBuildException(
                $"Duplicate event versions detected: {string.Join(", ", duplicates)}.");
    }
}
```

### Lifecycle

```
Created ──► Build ──► Frozen (immutable)
               │
               └── Fail-Fast on collision
```

Error at build time, not at runtime.

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

public sealed record ValidationResult(bool IsValid, string? Error, EventDescriptor? Descriptor);
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
        var descriptor = _registry.GetByName(eventName)
            ?? throw new EventValidationException(
                $"Event '{eventName}' is not registered. " +
                "Apply [CrestEvent] to the event class or register via EventRegistryProvider.");

        if (descriptor.State is DescriptorState.Deprecated or DescriptorState.Removed)
            throw new EventValidationException(
                $"Event '{eventName}' is {descriptor.State}. " +
                $"Use '{descriptor.SupersededById}' instead.");

        // Scope boundary enforcement
        if (descriptor.Scope == EventScope.Local && _busKind == BusKind.Distributed)
            throw new EventValidationException(
                $"Event '{eventName}' has Scope.Local — cannot publish to a distributed bus.");

        if (descriptor.Scope == EventScope.Domain && _busKind == BusKind.Distributed)
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
    string EventName,           // ← added: "capability.succeeded" (registry-defined name)
    EventScope Scope,           // ← added
    string EventType,           // Assembly-qualified type name
    byte[] Payload,
    string ErrorMessage,
    string? ExceptionType,      // ← added: typeof(TimeoutException).FullName
    DateTime FailedAt,
    int RetryCount,
    int MaxRetries,
    DeadLetterStatus Status     // Pending | Retrying | Retried | Archived
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
| `EventRegistry` | Register, GetByName, GetActiveVersion, IsBuilt flag, duplicate Build is idempotent |
| `EventRegistry.Build()` validation | Duplicate event name → throws; duplicate (name, version) → throws; single registration succeeds |
| `RegistryEventValidator` | Registered event → valid; unregistered → throws; Deprecated → throws with SupersededById message; Local scope on distributed bus → throws |
| `InMemoryDeadLetterStore` | Full lifecycle: Enqueue → GetPending → MarkRetrying → MarkRetried; MarkArchived after max retries |
| `EfCoreDeadLetterStore` | Same lifecycle + query by EventName, Scope, Status |
| `GeneratedEventDescriptorProvider` | Produces correct descriptors from `[CrestEvent]` attribute inputs |

### Integration Tests

| Test | Verifies |
|------|----------|
| Full compilation chain | `[CrestEvent]` → generator → provider → registry.Build() → validator.ValidateOrThrow() → pass |
| Duplicate event detection | Two modules declare `"capability.succeeded"` → `Build()` throws `EventRegistryBuildException` |
| Scope enforcement | Publish `Scope.Local` event via RabbitMQ → throws |
| DLQ metadata | Handler throws → `IDeadLetterStore` receives `DeadLetterMessage` with EventName, Scope, ExceptionType populated |

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
| `EventReliability` driving bus behavior (AtLeastOnce → Outbox) | Phase 2b (Outbox) |
| `PayloadSchemaRef` in `DeadLetterMessage` | Phase 3 |
| AoT optimization (`FrozenDictionary`, `switch`-based lookup) | Phase 4 |
