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

### Descriptor Hierarchy

Two distinct descriptor types share a common interface, reflecting their fundamentally different contracts:

```csharp
// CrestCreates.Event.Abstractions/IEventDescriptor.cs

public interface IEventDescriptor
{
    string Id { get; }
    string Name { get; }
    EventScope Scope { get; }
    EventDirection Direction { get; }
    EventImportance Importance { get; }
    bool IsAuditable { get; }
    bool IsReplayable { get; }
    bool IsPublic { get; }
    string? Description { get; }
}

// CrestCreates.Event.Abstractions/GeneratedEventDescriptor.cs

public sealed record GeneratedEventDescriptor : IEventDescriptor, IVersionedDescriptor
{
    // ── 1. Identity ──
    public string Id { get; init; }                 // SHA256(Name + ":" + Version). Stable across CLR type renames; name change = new identity.
    public string Name { get; init; }
    public int Version { get; init; }
    public DescriptorState State { get; init; }
    public string? Description { get; init; }

    // ── 2. Payload (versioned, schema-backed) ──
    public Type PayloadType { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor> PayloadSchemaRef { get; init; }

    // ── 3. Scope ──
    public EventScope Scope { get; init; }

    // ── 4. Reliability ──
    public EventReliability Reliability { get; init; }

    // ── 5. Direction ──
    public EventDirection Direction { get; init; }

    // ── 6. Ownership ──
    public VersionedDescriptorRef<CapabilityDescriptor>? CapabilityRef { get; init; }
    public string? CreatedBy { get; init; }

    // ── Classification ──
    public EventImportance Importance { get; init; }

    // ── Operational flags ──
    public bool IsAuditable { get; init; }
    public bool IsReplayable { get; init; }
    public bool IsPublic { get; init; }

    // ── Compatibility ──
    public SchemaChangeKind ChangeKind { get; init; }

    // ── Topology (reserved) ──
    public IReadOnlyList<string> Producers { get; init; } = [];
    public IReadOnlyList<string> Consumers { get; init; } = [];
}

// CrestCreates.Event.Abstractions/DynamicEventDescriptor.cs

public sealed record DynamicEventDescriptor : IEventDescriptor
{
    public string Id { get; init; }                     // SHA256(Name) — unversioned
    public string Name { get; init; }
    public EventScope Scope { get; init; }
    public EventDirection Direction { get; init; } = EventDirection.Internal;
    public EventImportance Importance { get; init; } = EventImportance.Normal;
    public bool IsAuditable { get; init; }
    public bool IsReplayable { get; init; }
    public bool IsPublic { get; init; }
    public string? Description { get; init; }
    public Type PayloadType { get; init; }               // Dev-time only, no schema

    // No: Version, State, Reliability, CapabilityRef, ChangeKind, PayloadSchemaRef
    // Dynamic events are unversioned, have no schema evolution, and no descriptor lifecycle
}
```

**Key distinction:** `GeneratedEventDescriptor` is versioned, schema-backed, and source-generated. `DynamicEventDescriptor` is tenant-defined, always Version=1, has no schema evolution, and no descriptor lifecycle (State, ChangeKind). The registry stores both as `IEventDescriptor`, but they have fundamentally different contracts. No deferred migration needed.

### New Enums

```csharp
public enum EventScope      { Local, Domain, Integration }
public enum EventDirection  { Internal, Incoming, Outgoing }
public enum EventReliability { BestEffort, AtLeastOnce, Idempotent }  // Idempotent = AtLeastOnce + dedup
public enum EventImportance { Low, Normal, High, Critical }
```

### Publish Resolution Rule

`PublishAsync(eventName, payload)` always resolves to the highest Active version. Version selection is a registry concern, not a caller concern — callers never specify event versions. There is no API to publish a specific version or a deprecated version.

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
    public int Version { get; init; } = 1;              // defaults to 1; explicit for v2+
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
    Version = 2,                                        // explicit — generator uses this
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
    IReadOnlyList<GeneratedEventDescriptor> GetDescriptors();
}
```

### What the Generator Emits (per-project, into obj/generated/)

```csharp
// GeneratedEventDescriptorProvider.g.cs

public sealed class GeneratedEventDescriptorProvider : IEventDescriptorProvider
{
    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() => [
        new GeneratedEventDescriptor
        {
            Id = GeneratedEventDescriptorProvider.GenerateId(
                "capability.succeeded", attribute.Version),
            // Id = SHA256("capability.succeeded:2") → stable across type renames
            Name = "capability.succeeded",
            Version = attribute.Version,           // from [CrestEvent(Version = 2)]
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

`IDynamicEventRegistry.Register(name, payloadType, scope)` adds dynamic events at runtime (tenant-custom, configuration-driven). Dynamic events are stored separately from generated descriptors. `IEventResolver` unifies query access — generated wins on name conflict. **Dynamic descriptors are unversioned, use `DynamicEventDescriptor` type, have no schema backing or descriptor lifecycle, and are restricted to `Scope.Local` only.** This prevents dynamic events from leaking into distributed contracts (Kafka topics, RabbitMQ exchanges), preserving registry governance.

Dynamic events serve internal extensibility — tenant workflows, form scripts, notification rules — where compile-time registration is impractical. They cannot become cross-service contracts.

---

## Design Section 3: EventRegistry — Build, Validate, Freeze

### Four-Interface Model: Registry + Dynamic Registry + Resolver + Metadata Provider

Generated and dynamic descriptors have separate registries. A resolver unifies query access, and a metadata provider exposes build state and diagnostic queries without coupling consumers to `EventRegistry`:

```csharp
// Generated only — frozen after Build()
public interface IEventRegistry
{
    RegistryState State { get; }
    void Build(IEnumerable<IEventDescriptorProvider> providers);
    GeneratedEventDescriptor? GetByName(string name);
    GeneratedEventDescriptor? GetByPayloadType(Type payloadType);
    // ... (name, version, category queries)
}

// Dynamic only — mutable after Build()
public interface IDynamicEventRegistry
{
    bool Register(string name, Type payloadType, EventScope scope);
    DynamicEventDescriptor? GetByName(string name);
}

// Union resolver — generated first, dynamic fallback
public interface IEventResolver
{
    IEventDescriptor? GetByName(string name);
    IEventDescriptor? GetByPayloadType(Type type);
}

// Metadata provider — build state + diagnostic-only queries
// NOT on IEventResolver because GetAllVersions/GetLatestVersion are
// GeneratedEventDescriptor-specific (DynamicEventDescriptors are unversioned)
public interface IEventMetadataProvider
{
    RegistryState State { get; }
    IReadOnlyList<GeneratedEventDescriptor> GetAllVersions(string name);
    GeneratedEventDescriptor? GetLatestVersion(string name);
}
```

`EventRegistry` implements both `IEventRegistry` and `IEventMetadataProvider`. `IEventValidator` takes `IEventResolver` + `IEventMetadataProvider` — it never casts to `EventRegistry`. Generated wins on name conflict in the resolver. `IDynamicEventRegistry` is not used during `Build()`.

### One-Shot Build

`Build()` succeeds exactly once for the generated category. After building, generated descriptors are immutable.

```csharp
// CrestCreates.Event/EventRegistry.cs (revised)

public sealed class EventRegistry : IEventRegistry, IEventMetadataProvider
{
    private readonly ConcurrentDictionary<string, GeneratedEventDescriptor> _byId = new();
    private readonly ConcurrentDictionary<Type, GeneratedEventDescriptor> _byPayloadType = new();
    public RegistryState State { get; private set; } = RegistryState.Created;

    public void Build(IEnumerable<IEventDescriptorProvider> providers)
    {
        if (State == RegistryState.Built) return;
        if (State == RegistryState.Failed)
            throw new InvalidOperationException("Build previously failed. Restart required.");
        State = RegistryState.Building;
        var descriptors = providers.SelectMany(p => p.GetDescriptors()).ToList();
        try
        {
            ValidateNoDuplicateNameVersions(descriptors);
            ValidateSingleActiveVersion(descriptors);
            ValidateVersionOrdering(descriptors);
            foreach (var d in descriptors) RegisterGenerated(d);
            State = RegistryState.Built;
        }
        catch { State = RegistryState.Failed; throw; }
    }

    public GeneratedEventDescriptor? GetByName(string name) { /* highest Active */ }
    public GeneratedEventDescriptor? GetByPayloadType(Type t) => _byPayloadType.TryGetValue(t, out var d) ? d : null;
    public IReadOnlyList<GeneratedEventDescriptor> GetAllVersions(string name) { /* all versions regardless of state */ }
    public GeneratedEventDescriptor? GetLatestVersion(string name) { /* highest version regardless of state */ }
    public GeneratedEventDescriptor? GetByNameAndVersion(string name, int version) { /* ... */ }

    private void RegisterGenerated(GeneratedEventDescriptor d)
    {
        _byId[d.Id] = d;
        _byPayloadType[d.PayloadType] = d;
    }
}

// Separate class — dynamic only; no Build, no State, no Version
public sealed class DynamicEventRegistry : IDynamicEventRegistry
{
    private readonly ConcurrentDictionary<string, DynamicEventDescriptor> _byName = new();
    private readonly IEventRegistry _generated;

    public DynamicEventRegistry(IEventRegistry generated) => _generated = generated;

    public bool Register(string name, Type payloadType, EventScope scope)
    {
        if (scope != EventScope.Local)
            throw new ArgumentException(
                $"Dynamic events are restricted to Scope.Local. " +
                $"Requested: {scope}. Use [CrestEvent] for Domain/Integration events.");
        if (_generated.State != RegistryState.Built)
            throw new InvalidOperationException("Cannot register dynamic events before Build completes.");
        if (_generated.GetByName(name) is not null) return false;  // generated wins
        _byName[name] = new DynamicEventDescriptor
        {
            Id = DynamicEventDescriptor.GenerateId(name),
            Name = name, PayloadType = payloadType, Scope = scope
        };
        return true;
    }

    public DynamicEventDescriptor? GetByName(string name) => _byName.TryGetValue(name, out var d) ? d : null;
}

// Union resolver — IEventValidator depends on this
public sealed class EventResolver : IEventResolver
{
    private readonly IEventRegistry _generated;
    private readonly IDynamicEventRegistry _dynamic;

    public EventResolver(IEventRegistry g, IDynamicEventRegistry d) { _generated = g; _dynamic = d; }
    public IEventDescriptor? GetByName(string name)
        => (IEventDescriptor?)_generated.GetByName(name) ?? _dynamic.GetByName(name);
    public IEventDescriptor? GetByPayloadType(Type type) => _generated.GetByPayloadType(type);
}
```

### Name → Version Resolution

When multiple versions of an event exist, `GetByName` returns the active version with the highest version number. `GetLatestVersion` returns the highest version regardless of state — used by the validator to distinguish "not registered" from "all versions deprecated":

```csharp
// Returns the active descriptor with the highest version, or null
public GeneratedEventDescriptor? GetByName(string name)
    => _byName.TryGetValue(name, out var versions)
        ? versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version)
        : null;

// Returns the highest-version descriptor regardless of state, or null
public GeneratedEventDescriptor? GetLatestVersion(string name)
    => _byName.TryGetValue(name, out var versions)
        ? versions.MaxBy(v => v.Version)
        : null;

// Exact version lookup
public GeneratedEventDescriptor? GetByNameAndVersion(string name, int version)
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

**Version Ordering rule:** The highest version of an event must be `Active`. This prevents a hidden lifecycle fork where `GetByName()` returns v1 (Active) but `GetLatestVersion()` returns v2 (Deprecated):

```
// Allowed
v1 Active                          (single version)
v1 Deprecated, v2 Active           (upgrade)
v1 Removed, v2 Active              (replacement)

// FORBIDDEN
v1 Active, v2 Deprecated           (fork — highest version is not Active)
v1 Removed, v2 Deprecated          (no Active at all — caught by Single Active Version)
```

Without this rule, `GetByName()` and `GetLatestVersion()` diverge, and the lifecycle becomes non-deterministic.

    private static void ValidateNoDuplicateNameVersions(List<GeneratedEventDescriptor> descriptors)
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

    private static void ValidateVersionOrdering(List<GeneratedEventDescriptor> descriptors)
    {
        var violations = descriptors
            .GroupBy(d => d.Name)
            .Select(g => new
            {
                Name = g.Key,
                Highest = g.MaxBy(d => d.Version)!,
                HasActive = g.Any(d => d.State == DescriptorState.Active)
            })
            .Where(g => g.HasActive && g.Highest.State != DescriptorState.Active)
            .ToList();

        if (violations.Count > 0)
            throw new EventRegistryBuildException(
                $"Version ordering violation: the highest version of an event must be Active. " +
                $"Violations: {string.Join(", ", violations.Select(v =>
                    $"{v.Name} (highest=v{v.Highest.Version} is {v.Highest.State})"))}. " +
                "Set the highest version to Active, or add a higher Active version.");
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
  EventRegistry (generated):                 DynamicEventRegistry (separate class):
  Created → Building → Built (frozen)         Register() anytime — no Build, no State
               ↓ Failed (restart)
```

Generated descriptors (`EventRegistry`) are frozen at startup. Dynamic descriptors (`DynamicEventRegistry`) can be added at runtime. `IEventResolver` queries both — generated wins on name conflict.

Error at build time, not at runtime.

### Registry Bootstrapper

All registries (Event, Capability, Workflow, HumanTask) share the same bootstrap pattern. The interface is defined now so the DI API is stable from day one:

```csharp
// CrestCreates.Abstractions/IRegistryBootstrapper.cs

public interface IRegistryBootstrapper
{
    Task BootstrapAsync(CancellationToken ct);
}
```

`EventRegistryHostedService` is the Phase 2a implementation — a `BackgroundService` that calls `registry.Build(providers)`. Future registries add their own bootstrappers. Phase 3 will introduce `IRegistryBootstrapper` ordering and dependency graph resolution so registries initialize in the correct sequence (Event → Capability → Workflow → HumanTask).

```csharp
// CrestCreates.Event/EventRegistryBootstrapper.cs

public sealed class EventRegistryBootstrapper : BackgroundService
{
    private readonly EventRegistry _registry;
    private readonly IEnumerable<IEventDescriptorProvider> _providers;

    public EventRegistryBootstrapper(
        EventRegistry registry,
        IEnumerable<IEventDescriptorProvider> providers)
    {
        _registry = registry;
        _providers = providers;
    }

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        _registry.Build(_providers);  // Fail-fast on collision
        return Task.CompletedTask;
    }
}
```

Registration: `services.AddHostedService<EventRegistryBootstrapper>()` in `EventModule`.

> **`BackgroundService` over `IHostedService`:** `BackgroundService` guarantees `ExecuteAsync` completes before `StartAsync` returns to the host, meaning no requests are served before `Build()` finishes. This is the correct behavior for a fail-fast registry bootstrap.

> **Transitional:** The concrete `BackgroundService` wrapping `EventRegistry` works for Phase 2a's single registry. When Phase 3 introduces multiple registries with dependency ordering, the `IRegistryBootstrapper` interface is already defined and the DI registration surface does not change — only the implementation behind it evolves.

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
    IEventDescriptor? Descriptor);

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
    private readonly IEventResolver _resolver;
    private readonly IEventMetadataProvider _metadata;

    public RegistryEventValidator(IEventResolver resolver, IEventMetadataProvider metadata)
    {
        _resolver = resolver;
        _metadata = metadata;
    }

    public void ValidateOrThrow(string eventName, object? payload)
    {
        if (_metadata.State != RegistryState.Built)
            throw new InvalidOperationException(
                "EventRegistry has not been built yet. Publish cannot occur before Build completes.");

        // Defensive: multiple Active versions indicate registry corruption
        // (should be impossible after Build validation, but double-check)
        var activeCount = _metadata.GetAllVersions(eventName)
            .Count(d => d.State == DescriptorState.Active);
        if (activeCount > 1)
            throw new InvalidOperationException(
                $"Registry corruption: event '{eventName}' has {activeCount} Active versions. " +
                "Only one Active version is permitted per event name.");

        // Publish path: highest Active version (what will actually be dispatched)
        var active = _resolver.GetByName(eventName);
        if (active is not null)
            return;  // OK — at least one Active version exists

        // Diagnostic path: determine why no Active version exists
        var latest = _metadata.GetLatestVersion(eventName);
        if (latest is null)
            throw new EventValidationException(
                $"Event '{eventName}' is not registered. " +
                "Apply [CrestEvent] to the event class or register via EventRegistryProvider.");

        if (latest.State == DescriptorState.Deprecated)
            throw new EventValidationException(
                $"Event '{eventName}' is deprecated. All versions are deprecated.");

        if (latest.State == DescriptorState.Removed)
            throw new EventValidationException(
                $"Event '{eventName}' has been removed.");

        // Schema validation deferred to Phase 3
    }
}
```

**Scope enforcement is transport-layer, not validator-layer.** Distributed bus base classes check scope independently:

```csharp
// DistributedEventBusBase
protected void ValidateScope(IEventDescriptor descriptor)
{
    if (descriptor.Scope == EventScope.Local)
        throw new EventValidationException(
            $"Event '{descriptor.Name}' has Scope.Local — cannot publish to a distributed bus.");

    if (descriptor.Scope == EventScope.Domain)
        throw new EventValidationException(
            $"Event '{descriptor.Name}' has Scope.Domain — cannot publish cross-process.");
}
```

`IEventValidator` is metadata-only (registered? active? deprecated?). Transport scope checks live where the transport decision is made. This keeps `BusKind` out of the validator and prevents coupling as transports grow (RabbitMQ, Kafka, Azure Service Bus, NATS, Redis Streams).

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

**`IEventValidator` is always present in DI.** A `PassThroughEventValidator` (no-op) is registered by default. Real validation requires `AddEventRegistry()` which replaces it with `RegistryEventValidator`. Bus code calls `_validator.ValidateOrThrow()` unconditionally — no null check. This eliminates the "validates in prod but not in dev" split.

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
    string DescriptorVersionKey,   // "capability.succeeded:v2" — monitoring aggregation key
    string? EventDescriptorId,     // "evt_6A9D8F..." (stable descriptor id)
    string? CorrelationId,         // correlation id for distributed tracing
    EventScope Scope,
    string PayloadTypeFullName,    // "MyApp.Events.CapabilitySucceeded" (not AQN — survives assembly version changes)
    byte[] Payload,
    string ErrorMessage,
    string? ExceptionType,         // typeof(TimeoutException).FullName
    DateTime OccurredAt,           // when the original event was created
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

| Add `DescriptorSnapshotJson` to `DeadLetterMessage` | Phase 3 |
| Add `PayloadSchemaRef` to `DeadLetterMessage` | Phase 3 |

> **EventTypeMap → IEventTypeResolver:** The Phase 2a `EventTypeMap` (typeof → event name) is backed by `Registry.GetByPayloadType()`. Phase 3 should extract a general `IEventTypeResolver` interface to unify Type↔Descriptor mapping across Event, Capability, Workflow, and HumanTask registries. This becomes core Metadata infrastructure, not Event-specific.

> **RegistryBase<TDescriptor>:** Phase 3 should extract a generic `RegistryBase<TDescriptor>` to unify Event, Capability, Workflow, and HumanTask registries. All share the same lifecycle (Created→Building→Built→Failed), the same Build/Register pattern, and the same name+version indexing.

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
9. **Wire `IEventValidator` + scope enforcement into buses** — EventBus.Local/Channel/RabbitMQ/Kafka. Validator handles metadata checks; scope enforcement (Local/Domain vs Distributed) lives in DistributedEventBusBase
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
| `EventRegistry.Build()` validation | Duplicate (name, version) → throws; same name + different versions → succeeds; single registration succeeds; v1 Active + v2 Deprecated → throws (highest must be Active) |
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
| Version ordering violation | `"capability.succeeded"` v1 Active + v2 Deprecated → `Build()` throws (highest version must be Active) |
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

> **Phase 2b forward reference:** `EventReliability` is metadata only in Phase 2a. Phase 2b will introduce `DeliveryStrategy` resolution that maps `EventReliability` to transport behavior — `BestEffort` → fire-and-forget, `AtLeastOnce` → Outbox + retry, `Idempotent` → Outbox + idempotency check. Note: `Idempotent` is a consumer-side guarantee (AtLeastOnce + dedup), not a true exactly-once delivery semantic. This naming may be refined in Phase 2b.

> **Phase 2a — typed publish:** The source generator also emits an `EventTypeMap` (typeof → event name). `IEventBus.PublishAsync<TEvent>(TEvent evt)` resolves the event name from the map at zero runtime cost, then delegates to the string-based path. This eliminates magic strings for all `[CrestEvent]`-annotated types immediately. The string-based `PublishAsync(string, payload)` remains as the low-level API for dynamic events, but generated events use the typed overload by default.

