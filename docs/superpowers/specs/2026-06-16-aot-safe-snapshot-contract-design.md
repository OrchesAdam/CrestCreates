# Foundation — AOT-safe Snapshot Contract Design

**Date:** 2026-06-16
**Issue:** #33
**Labels:** type-feature, tech-debt, area-foundation, area-aot

## Goal

Introduce a small AOT-safe snapshot contract for framework boundary models that need defensive copies, snapshot-on-read behavior, registry/store isolation, or proposed-state materialization.

This is **not** a generic deep clone library. It is an explicit snapshot boundary for CrestCreates models.

## Background

Many framework models already define `Clone()` or `CreateClone()` manually — especially in descriptor, registry, workflow, human task, and organization paths. The patterns are inconsistent:

| Pattern | Method Name | Used By |
|---|---|---|
| `sealed class` + manual property copy | `Clone()` | Organization (4 models), Workflow, HumanTask |
| `record` + `with` expression | `CreateClone()` | DescriptorDraft (7 models) |
| Dedicated snapshot type | `GetSnapshot()` | IPipelineMetrics |

No shared interface exists. Helper patterns (`.ToArray()`, `new Dictionary<>()`, `.ToDictionary()`) are duplicated across models. String dictionary copies use `StringComparer.Ordinal` in some places but not standardized.

## Design Principle

> Snapshot means safe boundary copy.

A snapshot operation is explicit, deterministic, AOT-safe, and deep enough to protect internal state from external mutation. It does not attempt to clone arbitrary objects.

## Interface Contract

**File:** `CrestCreates.Snapshot.Abstractions/ISnapshotable.cs`

```csharp
namespace CrestCreates.Snapshot.Abstractions;

/// <summary>
/// AOT-safe snapshot contract for models that require defensive copies
/// at store/registry/runtime boundaries.
/// <para>
/// Snapshot means safe boundary copy — explicit, deterministic, and deep enough
/// to protect internal state from external mutation. It is NOT a generic deep clone.
/// </para>
/// </summary>
/// <typeparam name="T">The concrete type producing the snapshot.</typeparam>
public interface ISnapshotable<out T>
    where T : ISnapshotable<T>
{
    /// <summary>
    /// Creates a defensive copy of this instance.
    /// The returned object must not share mutable reference state with this instance.
    /// Immutable values may be reused.
    /// </summary>
    T Snapshot();
}
```

Key decisions:
- **CRTP constraint** (`where T : ISnapshotable<T>`) — ensures the return type is the same concrete type, preventing `ISnapshotable<Cat>.Snapshot()` from returning `Animal`.
- **Covariant `out T`** — enables variance scenarios like `IEnumerable<ISnapshotable<Derived>>`.
- **No non-generic base** — avoids `object`-returning methods that invite runtime downcasting, which violates AOT rules.
- **XML docs encode the contract** — "must not share mutable reference state with this instance."

## Extension Helpers

**File:** `CrestCreates.Snapshot/SnapshotExtensions.cs`

```csharp
namespace CrestCreates.Snapshot;

public static class SnapshotExtensions
{
    public static IReadOnlyList<T> SnapshotList<T>(this IEnumerable<T> source)
        where T : ISnapshotable<T>
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Select(item => item.Snapshot()).ToArray();
    }

    public static IReadOnlyDictionary<TKey, TValue> SnapshotDictionary<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> source)
        where TKey : notnull
        where TValue : ISnapshotable<TValue>
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Snapshot());
    }

    public static IReadOnlyDictionary<string, string> SnapshotStringDictionary(
        this IReadOnlyDictionary<string, string>? source)
    {
        if (source is null or { Count: 0 })
            return ReadOnlyDictionary<string, string>.Empty;

        return new Dictionary<string, string>(source, StringComparer.Ordinal);
    }
}
```

Key decisions:
- `SnapshotList` returns `IReadOnlyList<T>` via `.ToArray()` — array is the simplest immutable container, fully AOT-safe.
- `SnapshotDictionary` uses `ToDictionary()` without explicit comparer — keys are assumed immutable, default comparer is fine for non-string keys.
- `SnapshotStringDictionary` explicitly uses `StringComparer.Ordinal` — deterministic, culture-invariant, matches existing pattern in `DescriptorDraft.CreateClone()`.
- `SnapshotStringDictionary` accepts `null` and returns `ReadOnlyDictionary<string, string>.Empty` — the only nullable-accepting helper, because `Metadata` dictionaries are commonly null in the codebase.
- `ArgumentNullException.ThrowIfNull` — .NET 7+ pattern, no reflection.

## Project Structure

### CrestCreates.Snapshot.Abstractions
```
framework/src/CrestCreates.Snapshot.Abstractions/
├── CrestCreates.Snapshot.Abstractions.csproj
└── ISnapshotable.cs
```

- **Target:** `net10.0`
- **Dependencies:** None (zero project references)
- **RootNamespace:** `CrestCreates.Snapshot.Abstractions`
- **InternalsVisibleTo:** `CrestCreates.Snapshot`, `CrestCreates.Snapshot.Tests`

### CrestCreates.Snapshot
```
framework/src/CrestCreates.Snapshot/
├── CrestCreates.Snapshot.csproj
└── SnapshotExtensions.cs
```

- **Target:** `net10.0`
- **Dependencies:** `CrestCreates.Snapshot.Abstractions`
- **RootNamespace:** `CrestCreates.Snapshot`
- **InternalsVisibleTo:** `CrestCreates.Snapshot.Tests`

### CrestCreates.Snapshot.Tests
```
framework/test/CrestCreates.Snapshot.Tests/
├── CrestCreates.Snapshot.Tests.csproj
├── ISnapshotableContractTests.cs
├── SnapshotListTests.cs
├── SnapshotDictionaryTests.cs
└── SnapshotStringDictionaryTests.cs
```

- **Target:** `net10.0`
- **Dependencies:** `CrestCreates.Snapshot`, `CrestCreates.TestBase`
- **Framework:** xUnit + FluentAssertions

### Dependency Graph

```
Snapshot.Abstractions ← (no deps, stands alone)
        ↑
    Snapshot ← (only references Abstractions)
        ↑
    Snapshot.Tests ← (references Snapshot + TestBase)
```

Zero coupling to `Domain.Shared`, `Metadata.Abstractions`, or any other framework project. Any module can adopt `ISnapshotable<T>` without pulling in unrelated transitive dependencies.

All three projects added to `CrestCreates.slnx`.

## Test Coverage

| # | Test | File | Issue Test # |
|---|------|------|-------------|
| 1 | `Clone_Delegates_To_Snapshot` — model with both methods, `Clone()` delegates to `Snapshot()`, returns equivalent but independent copy | `ISnapshotableContractTests.cs` | #7 |
| 2 | `Returns_New_Item_Snapshots` — `SnapshotList` returns snapshots, not original references | `SnapshotListTests.cs` | #3 |
| 3 | `Rejects_Null_Source` — `SnapshotList` throws `ArgumentNullException` | `SnapshotListTests.cs` | #5 |
| 4 | `Empty_Source_Returns_Empty_Result` — empty input yields empty output | `SnapshotListTests.cs` | — |
| 5 | `Returns_New_Value_Snapshots` — `SnapshotDictionary` returns value snapshots, not original references | `SnapshotDictionaryTests.cs` | #4 |
| 6 | `Rejects_Null_Source` — `SnapshotDictionary` throws `ArgumentNullException` | `SnapshotDictionaryTests.cs` | #5 |
| 7 | `Returns_New_Dictionary_Instance` — `SnapshotStringDictionary` returns different reference | `SnapshotStringDictionaryTests.cs` | #1 |
| 8 | `Source_Mutation_Does_Not_Affect_Snapshot` — adding/removing from source doesn't change snapshot | `SnapshotStringDictionaryTests.cs` | #2 |
| 9 | `Null_Source_Returns_Empty_Dictionary` — null input returns empty, not null | `SnapshotStringDictionaryTests.cs` | #5 |
| 10 | `Deterministic_Ordinal_Comparer` — string keys use `StringComparer.Ordinal` | `SnapshotStringDictionaryTests.cs` | #6 |
| 11 | No helper uses reflection or JSON — design constraint, verified by code review | (static guarantee) | #8 |

Test model doubles:

```csharp
private sealed record TestModel(int Value) : ISnapshotable<TestModel>
{
    public TestModel Snapshot() => this with { };
}

private sealed class MutableModel : ISnapshotable<MutableModel>
{
    public int Value { get; set; }
    public MutableModel Snapshot() => new() { Value = Value };
}
```

## Migration Path (Future Work)

When existing models adopt `ISnapshotable<T>`:

**Record models (DescriptorDraft pattern):**
```csharp
// Before
public sealed record DescriptorDraft
{
    public DescriptorDraft CreateClone() => this with { ... };
}

// After
public sealed record DescriptorDraft : ISnapshotable<DescriptorDraft>
{
    public DescriptorDraft Snapshot() => this with { ... };
    public DescriptorDraft CreateClone() => Snapshot(); // backward compat
}
```

**Class models (Organization pattern):**
```csharp
// Before
public sealed class OrganizationUnit { public OrganizationUnit Clone() => new() { ... }; }

// After
public sealed class OrganizationUnit : ISnapshotable<OrganizationUnit>
{
    public OrganizationUnit Snapshot() => new() { ... };
    public OrganizationUnit Clone() => Snapshot();
}
```

`Clone()` / `CreateClone()` become one-line delegates for backward compatibility. They can be marked `[Obsolete]` in a later pass.

Migration is tracked separately. This issue does not modify any existing models.

## Explicit Non-goals

- No generic deep clone library (`object DeepClone(object value)`)
- No reflection-based graph traversal
- No JSON serialize/deserialize clone
- No `Expression.Compile` or IL emit
- No runtime type discovery
- No arbitrary cyclic graph clone support
- No automatic clone of unknown third-party objects
- No shallow/deep mode matrix
- No non-generic `ISnapshotable` base interface

## AOT / Determinism Rules

- No runtime reflection.
- No dynamic code generation.
- No JSON-based generic clone.
- No object graph walker.
- Snapshot is implemented explicitly by each participating model.
- Immutable values may be reused.
- Mutable collections must be copied.
- Mutable child objects must call `Snapshot()` or equivalent explicit clone.
- Dictionary helpers use deterministic comparers where relevant (`StringComparer.Ordinal`).

## Exit Criteria

The framework has a small AOT-safe snapshot contract and helper module that can standardize future defensive-copy semantics without introducing a generic deep clone library or violating AOT-first constraints.
