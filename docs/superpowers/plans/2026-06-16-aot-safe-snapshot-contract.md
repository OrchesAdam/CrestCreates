# AOT-safe Snapshot Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a small AOT-safe snapshot contract (`ISnapshotable<T>`) and helper extensions for framework boundary models that need defensive copies.

**Architecture:** Two source projects (Abstractions with the interface, implementation with extension helpers) plus a test project. Zero coupling to other framework projects. TDD — write tests first, then implement.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, no reflection or JSON dependencies.

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `framework/src/CrestCreates.Snapshot.Abstractions/CrestCreates.Snapshot.Abstractions.csproj` | Project file — zero dependencies |
| Create | `framework/src/CrestCreates.Snapshot.Abstractions/ISnapshotable.cs` | `ISnapshotable<T>` interface |
| Create | `framework/src/CrestCreates.Snapshot/CrestCreates.Snapshot.csproj` | Project file — references Abstractions |
| Create | `framework/src/CrestCreates.Snapshot/SnapshotExtensions.cs` | `SnapshotList`, `SnapshotDictionary`, `SnapshotStringDictionary` |
| Create | `framework/test/CrestCreates.Snapshot.Tests/CrestCreates.Snapshot.Tests.csproj` | Test project |
| Create | `framework/test/CrestCreates.Snapshot.Tests/ISnapshotableContractTests.cs` | Contract pattern tests |
| Create | `framework/test/CrestCreates.Snapshot.Tests/SnapshotListTests.cs` | `SnapshotList` tests |
| Create | `framework/test/CrestCreates.Snapshot.Tests/SnapshotDictionaryTests.cs` | `SnapshotDictionary` tests |
| Create | `framework/test/CrestCreates.Snapshot.Tests/SnapshotStringDictionaryTests.cs` | `SnapshotStringDictionary` tests |
| Modify | `CrestCreates.slnx` | Add all 3 new projects |

---

### Task 1: Create CrestCreates.Snapshot.Abstractions project

**Files:**
- Create: `framework/src/CrestCreates.Snapshot.Abstractions/CrestCreates.Snapshot.Abstractions.csproj`
- Create: `framework/src/CrestCreates.Snapshot.Abstractions/ISnapshotable.cs`

- [ ] **Step 1: Create the project directory and csproj**

```bash
mkdir -p framework/src/CrestCreates.Snapshot.Abstractions
```

Create `framework/src/CrestCreates.Snapshot.Abstractions/CrestCreates.Snapshot.Abstractions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Snapshot.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="CrestCreates.Snapshot" />
    <InternalsVisibleTo Include="CrestCreates.Snapshot.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create ISnapshotable.cs**

Create `framework/src/CrestCreates.Snapshot.Abstractions/ISnapshotable.cs`:

```csharp
namespace CrestCreates.Snapshot.Abstractions;

/// <summary>
/// AOT-safe snapshot contract for models that require defensive copies
/// at store/registry/runtime boundaries.
/// <para>
/// Snapshot means safe boundary copy — explicit, deterministic, and deep enough
/// to protect internal state from external mutation. It is NOT a generic deep clone.
/// </para>
/// <para>
/// The returned object must not share mutable reference state with this instance.
/// Immutable values may be reused. Shared references are allowed only when the
/// referenced object is immutable, stateless, or intentionally shared infrastructure
/// (e.g., ILogger, IServiceProvider, FrozenDictionary), and the model documents that choice.
/// </para>
/// </summary>
/// <typeparam name="T">The concrete type producing the snapshot.</typeparam>
public interface ISnapshotable<T>
    where T : ISnapshotable<T>
{
    /// <summary>
    /// Creates a defensive copy of this instance.
    /// </summary>
    T Snapshot();
}
```

- [ ] **Step 3: Build the Abstractions project**

Run: `dotnet build framework/src/CrestCreates.Snapshot.Abstractions`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Snapshot.Abstractions/
git commit -m "feat(snapshot): add ISnapshotable<T> contract in Abstractions project (#33)"
```

---

### Task 2: Create CrestCreates.Snapshot project

**Files:**
- Create: `framework/src/CrestCreates.Snapshot/CrestCreates.Snapshot.csproj`
- Create: `framework/src/CrestCreates.Snapshot/SnapshotExtensions.cs`

- [ ] **Step 1: Create the project directory and csproj**

```bash
mkdir -p framework/src/CrestCreates.Snapshot
```

Create `framework/src/CrestCreates.Snapshot/CrestCreates.Snapshot.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Snapshot</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="CrestCreates.Snapshot.Tests" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Snapshot.Abstractions\CrestCreates.Snapshot.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create SnapshotExtensions.cs**

Create `framework/src/CrestCreates.Snapshot/SnapshotExtensions.cs`:

```csharp
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Snapshot;

/// <summary>
/// AOT-safe snapshot helpers for collections of <see cref="ISnapshotable{T}"/> models.
/// All helpers are deterministic and use ordinal comparison for string keys.
/// </summary>
public static class SnapshotExtensions
{
    /// <summary>
    /// Creates a defensive copy of a list where each element snapshots itself.
    /// Returns a new <see cref="IReadOnlyList{T}"/>; mutating the source
    /// or its elements after snapshot does not affect the result.
    /// </summary>
    public static IReadOnlyList<T> SnapshotList<T>(this IEnumerable<T> source)
        where T : ISnapshotable<T>
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Select(item => item.Snapshot()).ToArray();
    }

    /// <summary>
    /// Creates a defensive copy of a dictionary where each value snapshots itself.
    /// Keys are reused (assumed immutable). Returns a new
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/>.
    /// </summary>
    public static IReadOnlyDictionary<TKey, TValue> SnapshotDictionary<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> source,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
        where TValue : ISnapshotable<TValue>
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Snapshot(), comparer);
    }

    /// <summary>
    /// Creates a defensive copy of a string-to-string dictionary
    /// using <see cref="StringComparer.Ordinal"/> for deterministic ordering.
    /// String values are immutable and reused; only the dictionary container is copied.
    /// Always returns an independent container, never a shared static empty.
    /// </summary>
    public static IReadOnlyDictionary<string, string> SnapshotStringDictionary(
        this IReadOnlyDictionary<string, string>? source)
    {
        if (source is null or { Count: 0 })
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return new Dictionary<string, string>(source, StringComparer.Ordinal);
    }
}
```

- [ ] **Step 3: Build the implementation project**

Run: `dotnet build framework/src/CrestCreates.Snapshot`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Snapshot/
git commit -m "feat(snapshot): add SnapshotExtensions helpers (#33)"
```

---

### Task 3: Create CrestCreates.Snapshot.Tests project

**Files:**
- Create: `framework/test/CrestCreates.Snapshot.Tests/CrestCreates.Snapshot.Tests.csproj`
- Create: `framework/test/CrestCreates.Snapshot.Tests/ISnapshotableContractTests.cs`
- Create: `framework/test/CrestCreates.Snapshot.Tests/SnapshotListTests.cs`
- Create: `framework/test/CrestCreates.Snapshot.Tests/SnapshotDictionaryTests.cs`
- Create: `framework/test/CrestCreates.Snapshot.Tests/SnapshotStringDictionaryTests.cs`

- [ ] **Step 1: Create the test project directory and csproj**

```bash
mkdir -p framework/test/CrestCreates.Snapshot.Tests
```

Create `framework/test/CrestCreates.Snapshot.Tests/CrestCreates.Snapshot.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Snapshot.Tests</RootNamespace>
    <AssemblyName>CrestCreates.Snapshot.Tests</AssemblyName>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Snapshot.Abstractions\CrestCreates.Snapshot.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Snapshot\CrestCreates.Snapshot.csproj" />
    <ProjectReference Include="..\CrestCreates.TestBase\CrestCreates.TestBase.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create ISnapshotableContractTests.cs**

Create `framework/test/CrestCreates.Snapshot.Tests/ISnapshotableContractTests.cs`:

```csharp
using CrestCreates.Snapshot.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Snapshot.Tests;

public class ISnapshotableContractTests
{
    /// <summary>
    /// Test model: sealed record implementing ISnapshotable via 'with' expression.
    /// </summary>
    private sealed record TestRecordModel(int Value) : ISnapshotable<TestRecordModel>
    {
        public TestRecordModel Snapshot() => this with { };
    }

    /// <summary>
    /// Test model: mutable class implementing ISnapshotable with manual copy.
    /// </summary>
    private sealed class MutableModel : ISnapshotable<MutableModel>
    {
        public int Value { get; set; }
        public MutableModel Snapshot() => new() { Value = Value };
    }

    /// <summary>
    /// Test model: class with both Clone() and Snapshot() where Clone() delegates to Snapshot().
    /// This verifies the backward-compatibility migration pattern.
    /// </summary>
    private sealed class LegacyBridgeModel : ISnapshotable<LegacyBridgeModel>
    {
        public int Value { get; set; }
        public LegacyBridgeModel Snapshot() => new() { Value = Value };
        public LegacyBridgeModel Clone() => Snapshot();
    }

    [Fact]
    public void Snapshot_Returns_Different_Instance()
    {
        var original = new MutableModel { Value = 42 };
        var snapshot = original.Snapshot();

        snapshot.Should().NotBeSameAs(original);
    }

    [Fact]
    public void Snapshot_Returns_Equivalent_Values()
    {
        var original = new MutableModel { Value = 42 };
        var snapshot = original.Snapshot();

        snapshot.Value.Should().Be(original.Value);
    }

    [Fact]
    public void Snapshot_Isolation_Mutation_Does_Not_Affect_Original()
    {
        var original = new MutableModel { Value = 42 };
        var snapshot = original.Snapshot();

        original.Value = 99;

        snapshot.Value.Should().Be(42);
    }

    [Fact]
    public void Snapshot_Isolation_Snapshot_Mutation_Does_Not_Affect_Original()
    {
        var original = new MutableModel { Value = 42 };
        var snapshot = original.Snapshot();

        snapshot.Value = 99;

        original.Value.Should().Be(42);
    }

    [Fact]
    public void Record_Snapshot_Uses_With_Expression()
    {
        var original = new TestRecordModel(10);
        var snapshot = original.Snapshot();

        snapshot.Should().NotBeSameAs(original);
        snapshot.Value.Should().Be(10);
    }

    [Fact]
    public void Clone_Delegates_To_Snapshot()
    {
        var original = new LegacyBridgeModel { Value = 7 };
        var clone = original.Clone();
        var snapshot = original.Snapshot();

        // Both should produce equivalent but independent copies
        clone.Value.Should().Be(snapshot.Value).And.Be(7);
        clone.Should().NotBeSameAs(snapshot);
        clone.Should().NotBeSameAs(original);
        snapshot.Should().NotBeSameAs(original);
    }

    [Fact]
    public void Clone_Delegates_To_Snapshot_Mutation_Isolation()
    {
        var original = new LegacyBridgeModel { Value = 7 };
        var clone = original.Clone();

        original.Value = 100;

        clone.Value.Should().Be(7);
    }
}
```

- [ ] **Step 3: Create SnapshotListTests.cs**

Create `framework/test/CrestCreates.Snapshot.Tests/SnapshotListTests.cs`:

```csharp
using CrestCreates.Snapshot.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Snapshot.Tests;

public class SnapshotListTests
{
    private sealed class MutableModel : ISnapshotable<MutableModel>
    {
        public int Value { get; set; }
        public MutableModel Snapshot() => new() { Value = Value };
    }

    [Fact]
    public void Returns_New_Item_Snapshots_Not_Original_References()
    {
        var items = new List<MutableModel>
        {
            new() { Value = 1 },
            new() { Value = 2 },
            new() { Value = 3 },
        };

        var snapshot = items.SnapshotList();

        snapshot.Should().HaveCount(3);
        snapshot[0].Should().NotBeSameAs(items[0]);
        snapshot[1].Should().NotBeSameAs(items[1]);
        snapshot[2].Should().NotBeSameAs(items[2]);
        snapshot[0].Value.Should().Be(1);
        snapshot[1].Value.Should().Be(2);
        snapshot[2].Value.Should().Be(3);
    }

    [Fact]
    public void Source_Mutation_Does_Not_Affect_Snapshot()
    {
        var items = new List<MutableModel>
        {
            new() { Value = 10 },
        };

        var snapshot = items.SnapshotList();

        items[0].Value = 99;
        items.Add(new() { Value = 50 });

        snapshot.Should().HaveCount(1);
        snapshot[0].Value.Should().Be(10);
    }

    [Fact]
    public void Rejects_Null_Source()
    {
        IEnumerable<MutableModel> source = null!;

        var act = () => source.SnapshotList();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Empty_Source_Returns_Empty_Result()
    {
        var items = new List<MutableModel>();

        var snapshot = items.SnapshotList();

        snapshot.Should().BeEmpty();
    }

    [Fact]
    public void Returns_IReadOnlyList()
    {
        var items = new List<MutableModel> { new() { Value = 1 } };

        IReadOnlyList<MutableModel> snapshot = items.SnapshotList();

        snapshot.Should().HaveCount(1);
    }
}
```

- [ ] **Step 4: Create SnapshotDictionaryTests.cs**

Create `framework/test/CrestCreates.Snapshot.Tests/SnapshotDictionaryTests.cs`:

```csharp
using CrestCreates.Snapshot.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Snapshot.Tests;

public class SnapshotDictionaryTests
{
    private sealed class MutableModel : ISnapshotable<MutableModel>
    {
        public int Value { get; set; }
        public MutableModel Snapshot() => new() { Value = Value };
    }

    [Fact]
    public void Returns_New_Value_Snapshots_Not_Original_References()
    {
        var source = new Dictionary<string, MutableModel>
        {
            ["a"] = new() { Value = 1 },
            ["b"] = new() { Value = 2 },
        };

        var snapshot = source.SnapshotDictionary();

        snapshot.Should().HaveCount(2);
        snapshot["a"].Should().NotBeSameAs(source["a"]);
        snapshot["b"].Should().NotBeSameAs(source["b"]);
        snapshot["a"].Value.Should().Be(1);
        snapshot["b"].Value.Should().Be(2);
    }

    [Fact]
    public void Source_Mutation_Does_Not_Affect_Snapshot()
    {
        var source = new Dictionary<string, MutableModel>
        {
            ["key"] = new() { Value = 10 },
        };

        var snapshot = source.SnapshotDictionary();

        source["key"].Value = 99;
        source.Add("new", new() { Value = 50 });

        snapshot["key"].Value.Should().Be(10);
        snapshot.Should().HaveCount(1);
    }

    [Fact]
    public void Rejects_Null_Source()
    {
        IReadOnlyDictionary<string, MutableModel> source = null!;

        var act = () => source.SnapshotDictionary();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Custom_Comparer_Is_Used()
    {
        var source = new Dictionary<string, MutableModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["Key"] = new() { Value = 1 },
        };

        var snapshot = source.SnapshotDictionary(StringComparer.OrdinalIgnoreCase);

        // The snapshot should use the custom comparer, so lookup is case-insensitive
        snapshot["key"].Value.Should().Be(1);
        snapshot["KEY"].Value.Should().Be(1);
    }

    [Fact]
    public void Default_Comparer_Is_Used_When_None_Specified()
    {
        var source = new Dictionary<string, MutableModel>
        {
            ["Key"] = new() { Value = 1 },
        };

        var snapshot = source.SnapshotDictionary();

        // Default comparer is case-sensitive
        snapshot["Key"].Value.Should().Be(1);
        snapshot.ContainsKey("key").Should().BeFalse();
    }

    [Fact]
    public void Empty_Source_Returns_Empty_Result()
    {
        var source = new Dictionary<string, MutableModel>();

        var snapshot = source.SnapshotDictionary();

        snapshot.Should().BeEmpty();
    }

    [Fact]
    public void Int_Key_Works_With_Default_Comparer()
    {
        var source = new Dictionary<int, MutableModel>
        {
            [1] = new() { Value = 10 },
            [2] = new() { Value = 20 },
        };

        var snapshot = source.SnapshotDictionary();

        snapshot[1].Value.Should().Be(10);
        snapshot[2].Value.Should().Be(20);
        snapshot[1].Should().NotBeSameAs(source[1]);
    }
}
```

- [ ] **Step 5: Create SnapshotStringDictionaryTests.cs**

Create `framework/test/CrestCreates.Snapshot.Tests/SnapshotStringDictionaryTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace CrestCreates.Snapshot.Tests;

public class SnapshotStringDictionaryTests
{
    [Fact]
    public void Returns_New_Dictionary_Instance()
    {
        var source = new Dictionary<string, string> { ["key"] = "value" };

        var snapshot = source.SnapshotStringDictionary();

        snapshot.Should().NotBeSameAs(source);
    }

    [Fact]
    public void Source_Mutation_Does_Not_Affect_Snapshot()
    {
        var source = new Dictionary<string, string> { ["key"] = "value" };

        var snapshot = source.SnapshotStringDictionary();

        source["key"] = "changed";
        source.Add("new", "entry");

        snapshot["key"].Should().Be("value");
        snapshot.Should().HaveCount(1);
    }

    [Fact]
    public void Null_Source_Returns_Empty_Dictionary()
    {
        IReadOnlyDictionary<string, string>? source = null;

        var snapshot = source.SnapshotStringDictionary();

        snapshot.Should().NotBeNull();
        snapshot.Should().BeEmpty();
    }

    [Fact]
    public void Null_Source_Returns_Independent_Instance_Not_Shared_Static()
    {
        IReadOnlyDictionary<string, string>? source1 = null;
        IReadOnlyDictionary<string, string>? source2 = null;

        var snapshot1 = source1.SnapshotStringDictionary();
        var snapshot2 = source2.SnapshotStringDictionary();

        // Each call should return a new independent instance
        snapshot1.Should().NotBeSameAs(snapshot2);
    }

    [Fact]
    public void Empty_Source_Returns_Empty_Dictionary()
    {
        var source = new Dictionary<string, string>();

        var snapshot = source.SnapshotStringDictionary();

        snapshot.Should().NotBeNull();
        snapshot.Should().BeEmpty();
    }

    [Fact]
    public void Empty_Source_Returns_Independent_Instance()
    {
        var source1 = new Dictionary<string, string>();
        var source2 = new Dictionary<string, string>();

        var snapshot1 = source1.SnapshotStringDictionary();
        var snapshot2 = source2.SnapshotStringDictionary();

        snapshot1.Should().NotBeSameAs(snapshot2);
    }

    [Fact]
    public void Deterministic_Ordinal_Comparer()
    {
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Key"] = "value",
        };

        var snapshot = source.SnapshotStringDictionary();

        // Snapshot uses Ordinal comparer, so case-sensitive lookup should work
        snapshot["Key"].Should().Be("value");
        // Case-insensitive lookup should NOT work (Ordinal is case-sensitive)
        snapshot.ContainsKey("key").Should().BeFalse();
    }

    [Fact]
    public void Preserves_All_Entries()
    {
        var source = new Dictionary<string, string>
        {
            ["a"] = "1",
            ["b"] = "2",
            ["c"] = "3",
        };

        var snapshot = source.SnapshotStringDictionary();

        snapshot.Should().HaveCount(3);
        snapshot["a"].Should().Be("1");
        snapshot["b"].Should().Be("2");
        snapshot["c"].Should().Be("3");
    }

    [Fact]
    public void Returns_IReadOnlyDictionary()
    {
        var source = new Dictionary<string, string> { ["key"] = "value" };

        IReadOnlyDictionary<string, string> snapshot = source.SnapshotStringDictionary();

        snapshot["key"].Should().Be("value");
    }
}
```

- [ ] **Step 6: Build the test project**

Run: `dotnet build framework/test/CrestCreates.Snapshot.Tests`
Expected: Build succeeds with no errors.

- [ ] **Step 7: Run all tests**

Run: `dotnet test framework/test/CrestCreates.Snapshot.Tests`
Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git add framework/test/CrestCreates.Snapshot.Tests/
git commit -m "test(snapshot): add contract and extension method tests (#33)"
```

---

### Task 4: Add projects to solution

**Files:**
- Modify: `CrestCreates.slnx`

- [ ] **Step 1: Add projects to slnx**

Add the following two `<Project>` entries inside the `/src/core/` folder in `CrestCreates.slnx`, placed alphabetically near the existing `CrestCreates.Security` entries:

```xml
    <Project Path="framework/src/CrestCreates.Snapshot.Abstractions/CrestCreates.Snapshot.Abstractions.csproj" />
    <Project Path="framework/src/CrestCreates.Snapshot/CrestCreates.Snapshot.csproj" />
```

Add the following `<Project>` entry inside the `/src/test/` folder, placed alphabetically near the existing `CrestCreates.Scheduling.Tests` entries:

```xml
    <Project Path="framework/test/CrestCreates.Snapshot.Tests/CrestCreates.Snapshot.Tests.csproj" />
```

- [ ] **Step 2: Verify solution builds**

Run: `dotnet build CrestCreates.slnx`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Run the snapshot tests from solution**

Run: `dotnet test CrestCreates.slnx --filter "FullyQualifiedName~CrestCreates.Snapshot.Tests"`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add CrestCreates.slnx
git commit -m "feat(snapshot): add Snapshot projects to solution (#33)"
```

---

### Task 5: Final verification

- [ ] **Step 1: Run full solution build**

Run: `dotnet build CrestCreates.slnx`
Expected: Build succeeds with no errors and no new warnings.

- [ ] **Step 2: Run snapshot tests one more time**

Run: `dotnet test framework/test/CrestCreates.Snapshot.Tests -v normal`
Expected: All tests pass. Verify test count matches spec (approximately 20 test methods across 4 test files).

- [ ] **Step 3: Verify no reflection or JSON dependencies**

Run: `grep -r "System.Reflection\|System.Text.Json\|Newtonsoft\|Expression.Compile\|IL Emit" framework/src/CrestCreates.Snapshot/ framework/src/CrestCreates.Snapshot.Abstractions/`
Expected: No matches found.

- [ ] **Step 4: Verify project dependency graph is clean**

Run: `dotnet list framework/src/CrestCreates.Snapshot.Abstractions package` and `dotnet list framework/src/CrestCreates.Snapshot.Abstractions reference`
Expected: Abstractions has zero package dependencies and zero project references.

Run: `dotnet list framework/src/CrestCreates.Snapshot reference`
Expected: Implementation references only `CrestCreates.Snapshot.Abstractions`.
