# Module Runtime Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add runtime module diagnostics — lifecycle phase timing, failure capture, structured log output, and `/health` endpoint integration — via a new `CrestCreates.ModuleDiagnostics` project and code generation changes.

**Architecture:** A new `CrestCreates.ModuleDiagnostics` project provides `ModulePhaseTimer` (value-type stopwatch), `IModuleDiagnosticsStore` (in-memory diagnostics store), and `ModuleHealthCheck` (health check integration). Code generators in both BuildTasks and SourceGenerator paths are modified to wrap each module lifecycle call with timer → record instrumentation. The generated code exposes a static store instance that is also registered in DI for health check access.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions

---

## File Structure

### New Files

| File | Responsibility |
|---|---|
| `framework/src/CrestCreates.ModuleDiagnostics/CrestCreates.ModuleDiagnostics.csproj` | Project file with modular references |
| `framework/src/CrestCreates.ModuleDiagnostics/Timing/ModulePhaseTimer.cs` | Value-type timer, zero heap allocation |
| `framework/src/CrestCreates.ModuleDiagnostics/Stores/ModulePhaseDiagnostic.cs` | Data model: enum + readonly struct |
| `framework/src/CrestCreates.ModuleDiagnostics/Stores/IModuleDiagnosticsStore.cs` | Store interface |
| `framework/src/CrestCreates.ModuleDiagnostics/Stores/ModuleDiagnosticsStore.cs` | Default store implementation |
| `framework/src/CrestCreates.ModuleDiagnostics/Modules/ModuleDiagnosticsModule.cs` | CrestModule, DI registration |
| `framework/src/CrestCreates.ModuleDiagnostics/HealthChecks/ModuleHealthCheck.cs` | IHealthCheck for module status |
| `framework/test/CrestCreates.ModuleDiagnostics.Tests/CrestCreates.ModuleDiagnostics.Tests.csproj` | Test project |
| `framework/test/CrestCreates.ModuleDiagnostics.Tests/Stores/ModuleDiagnosticsStoreTests.cs` | Store unit tests |
| `framework/test/CrestCreates.ModuleDiagnostics.Tests/Timing/ModulePhaseTimerTests.cs` | Timer unit tests |
| `framework/test/CrestCreates.ModuleDiagnostics.Tests/HealthChecks/ModuleHealthCheckTests.cs` | Health check unit tests |

### Modified Files

| File | Change |
|---|---|
| `CrestCreates.slnx` | Add new projects to solution |
| `build/CrestCreates.BuildTasks/GenerateAggregatedModuleCode.cs` | Emit timer-wrapped lifecycle calls + summary log |
| `framework/tools/CrestCreates.CodeGenerator/ModuleGenerator/ModuleSourceGenerator.cs` | Emit timer-wrapped lifecycle calls + summary log |
| `framework/src/CrestCreates.HealthCheck/CrestCreates.HealthCheck.csproj` | Add ProjectReference to ModuleDiagnostics |

---

### Task 1: Create `CrestCreates.ModuleDiagnostics` project

**Files:**
- Create: `framework/src/CrestCreates.ModuleDiagnostics/CrestCreates.ModuleDiagnostics.csproj`

- [ ] **Step 1: Create project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Modularity\CrestCreates.Modularity.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add project to solution**

Add two lines to `CrestCreates.slnx` — the project entry and folder grouping with the HealthCheck projects:

After line 37 (the `CrestCreates.Modularity` project entry), add:
```xml
    <Project Path="framework/src/CrestCreates.ModuleDiagnostics/CrestCreates.ModuleDiagnostics.csproj" />
```

In the HealthCheck folder section (after line 69), add:
```xml
    <Project Path="framework/src/CrestCreates.ModuleDiagnostics/CrestCreates.ModuleDiagnostics.csproj" />
```

- [ ] **Step 3: Build to verify project creation**

Run: `dotnet build framework/src/CrestCreates.ModuleDiagnostics/CrestCreates.ModuleDiagnostics.csproj`
Expected: Build succeeds (empty project, no code yet)

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.ModuleDiagnostics/ CrestCreates.slnx
git commit -m "feat: add CrestCreates.ModuleDiagnostics project skeleton"
```

---

### Task 2: Implement data model — `ModulePhaseDiagnostic` and `ModulePhaseTimer`

**Files:**
- Create: `framework/src/CrestCreates.ModuleDiagnostics/Stores/ModulePhaseDiagnostic.cs`
- Create: `framework/src/CrestCreates.ModuleDiagnostics/Timing/ModulePhaseTimer.cs`

- [ ] **Step 1: Write the data model**

`framework/src/CrestCreates.ModuleDiagnostics/Stores/ModulePhaseDiagnostic.cs`:

```csharp
using System;

namespace CrestCreates.ModuleDiagnostics.Stores;

public enum ModulePhaseStatus
{
    Success,
    Failed
}

public readonly struct ModulePhaseDiagnostic
{
    public string ModuleName { get; }
    public string Phase { get; }
    public ModulePhaseStatus Status { get; }
    public TimeSpan Elapsed { get; }
    public string? ErrorMessage { get; }

    public ModulePhaseDiagnostic(string moduleName, string phase, ModulePhaseStatus status, TimeSpan elapsed, string? errorMessage)
    {
        ModuleName = moduleName;
        Phase = phase;
        Status = status;
        Elapsed = elapsed;
        ErrorMessage = errorMessage;
    }
}
```

- [ ] **Step 2: Write the timer**

`framework/src/CrestCreates.ModuleDiagnostics/Timing/ModulePhaseTimer.cs`:

```csharp
using System;
using System.Diagnostics;
using CrestCreates.ModuleDiagnostics.Stores;

namespace CrestCreates.ModuleDiagnostics.Timing;

public readonly struct ModulePhaseTimer
{
    private readonly string _moduleName;
    private readonly string _phase;
    private readonly long _startTimestamp;

    private ModulePhaseTimer(string moduleName, string phase, long startTimestamp)
    {
        _moduleName = moduleName;
        _phase = phase;
        _startTimestamp = startTimestamp;
    }

    public static ModulePhaseTimer StartNew(string moduleName, string phase)
    {
        return new ModulePhaseTimer(moduleName, phase, Stopwatch.GetTimestamp());
    }

    public ModulePhaseDiagnostic Stop(ModulePhaseStatus status)
    {
        var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        return new ModulePhaseDiagnostic(_moduleName, _phase, status, elapsed, null);
    }

    public ModulePhaseDiagnostic StopFailed(Exception ex)
    {
        var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        return new ModulePhaseDiagnostic(_moduleName, _phase, ModulePhaseStatus.Failed, elapsed, ex.Message);
    }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.ModuleDiagnostics/CrestCreates.ModuleDiagnostics.csproj`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.ModuleDiagnostics/Stores/ModulePhaseDiagnostic.cs framework/src/CrestCreates.ModuleDiagnostics/Timing/ModulePhaseTimer.cs
git commit -m "feat: add ModulePhaseDiagnostic and ModulePhaseTimer types"
```

---

### Task 3: Implement `IModuleDiagnosticsStore` and `ModuleDiagnosticsStore`

**Files:**
- Create: `framework/src/CrestCreates.ModuleDiagnostics/Stores/IModuleDiagnosticsStore.cs`
- Create: `framework/src/CrestCreates.ModuleDiagnostics/Stores/ModuleDiagnosticsStore.cs`

- [ ] **Step 1: Write the interface**

`framework/src/CrestCreates.ModuleDiagnostics/Stores/IModuleDiagnosticsStore.cs`:

```csharp
using System.Collections.Generic;

namespace CrestCreates.ModuleDiagnostics.Stores;

public interface IModuleDiagnosticsStore
{
    void Record(ModulePhaseDiagnostic diagnostic);
    IReadOnlyList<ModulePhaseDiagnostic> GetAll();
    IReadOnlyList<ModulePhaseDiagnostic> GetByModule(string moduleName);
    IReadOnlyList<ModulePhaseDiagnostic> GetFailed();
    bool HasFailures { get; }
    int TotalCount { get; }
}
```

- [ ] **Step 2: Write the implementation**

`framework/src/CrestCreates.ModuleDiagnostics/Stores/ModuleDiagnosticsStore.cs`:

```csharp
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace CrestCreates.ModuleDiagnostics.Stores;

public class ModuleDiagnosticsStore : IModuleDiagnosticsStore
{
    private readonly ConcurrentDictionary<string, List<ModulePhaseDiagnostic>> _records = new();
    private volatile int _totalCount;
    private volatile int _failureCount;

    public bool HasFailures => _failureCount > 0;
    public int TotalCount => _totalCount;

    public void Record(ModulePhaseDiagnostic diagnostic)
    {
        _records.AddOrUpdate(
            diagnostic.ModuleName,
            _ => new List<ModulePhaseDiagnostic> { diagnostic },
            (_, list) =>
            {
                list.Add(diagnostic);
                return list;
            });

        System.Threading.Interlocked.Increment(ref _totalCount);

        if (diagnostic.Status == ModulePhaseStatus.Failed)
        {
            System.Threading.Interlocked.Increment(ref _failureCount);
        }
    }

    public IReadOnlyList<ModulePhaseDiagnostic> GetAll()
    {
        return _records.Values.SelectMany(v => v).ToList();
    }

    public IReadOnlyList<ModulePhaseDiagnostic> GetByModule(string moduleName)
    {
        if (_records.TryGetValue(moduleName, out var list))
        {
            return list.ToList();
        }
        return System.Array.Empty<ModulePhaseDiagnostic>();
    }

    public IReadOnlyList<ModulePhaseDiagnostic> GetFailed()
    {
        return GetAll().Where(d => d.Status == ModulePhaseStatus.Failed).ToList();
    }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.ModuleDiagnostics/CrestCreates.ModuleDiagnostics.csproj`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.ModuleDiagnostics/Stores/IModuleDiagnosticsStore.cs framework/src/CrestCreates.ModuleDiagnostics/Stores/ModuleDiagnosticsStore.cs
git commit -m "feat: add IModuleDiagnosticsStore and ModuleDiagnosticsStore"
```

---

### Task 4: Implement `ModuleDiagnosticsModule`

**Files:**
- Create: `framework/src/CrestCreates.ModuleDiagnostics/Modules/ModuleDiagnosticsModule.cs`

- [ ] **Step 1: Write the module class with the shared static store**

The `ModuleDiagnosticsModule` creates the authoritative `ModuleDiagnosticsStore` instance as a public static property. Generated code and DI both access this same instance.

`framework/src/CrestCreates.ModuleDiagnostics/Modules/ModuleDiagnosticsModule.cs`:

```csharp
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using CrestCreates.ModuleDiagnostics.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.ModuleDiagnostics.Modules;

[CrestModule]
public class ModuleDiagnosticsModule : ModuleBase
{
    /// <summary>
    /// The shared diagnostics store instance. Set during ConfigureServices,
    /// read by generated ModuleAutoInitializer code.
    /// </summary>
    public static ModuleDiagnosticsStore Store { get; } = new();

    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModuleDiagnosticsStore>(Store);
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.ModuleDiagnostics/CrestCreates.ModuleDiagnostics.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.ModuleDiagnostics/Modules/ModuleDiagnosticsModule.cs
git commit -m "feat: add ModuleDiagnosticsModule with shared static store"
```

---

### Task 5: Implement `ModuleHealthCheck`

**Files:**
- Create: `framework/src/CrestCreates.ModuleDiagnostics/HealthChecks/ModuleHealthCheck.cs`

- [ ] **Step 1: Write the health check**

`framework/src/CrestCreates.ModuleDiagnostics/HealthChecks/ModuleHealthCheck.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.ModuleDiagnostics.Stores;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrestCreates.ModuleDiagnostics.HealthChecks;

[HealthCheck(Name = "Modules", Tags = new[] { "modules" }, Description = "Check module initialization status")]
public class ModuleHealthCheck : IHealthCheck
{
    private readonly IModuleDiagnosticsStore _store;

    public ModuleHealthCheck(IModuleDiagnosticsStore store)
    {
        _store = store;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var allEntries = _store.GetAll();
        var failedEntries = _store.GetFailed();

        var data = new Dictionary<string, object>
        {
            { "totalPhases", allEntries.Count },
            { "failedPhases", failedEntries.Count }
        };

        if (failedEntries.Count > 0)
        {
            var failedDetails = new List<Dictionary<string, string>>();
            foreach (var entry in failedEntries)
            {
                failedDetails.Add(new Dictionary<string, string>
                {
                    { "module", entry.ModuleName },
                    { "phase", entry.Phase },
                    { "error", entry.ErrorMessage ?? "Unknown error" }
                });
            }
            data["failedDetails"] = failedDetails;

            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Modules: {failedEntries.Count} phase(s) failed",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Modules: all {allEntries.Count} phases healthy",
            data));
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.ModuleDiagnostics/CrestCreates.ModuleDiagnostics.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.ModuleDiagnostics/HealthChecks/ModuleHealthCheck.cs
git commit -m "feat: add ModuleHealthCheck"
```

---

### Task 6: Create test project and write unit tests — Timer and Diagnostic

**Files:**
- Create: `framework/test/CrestCreates.ModuleDiagnostics.Tests/CrestCreates.ModuleDiagnostics.Tests.csproj`
- Create: `framework/test/CrestCreates.ModuleDiagnostics.Tests/Timing/ModulePhaseTimerTests.cs`
- Create: `framework/test/CrestCreates.ModuleDiagnostics.Tests/Stores/ModulePhaseDiagnosticTests.cs`

- [ ] **Step 1: Create test project file**

`framework/test/CrestCreates.ModuleDiagnostics.Tests/CrestCreates.ModuleDiagnostics.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.ModuleDiagnostics.Tests</RootNamespace>
    <AssemblyName>CrestCreates.ModuleDiagnostics.Tests</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.ModuleDiagnostics\CrestCreates.ModuleDiagnostics.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
  </ItemGroup>

</Project>
```

Add to `CrestCreates.slnx` after the existing test project entries:
```xml
    <Project Path="framework/test/CrestCreates.ModuleDiagnostics.Tests/CrestCreates.ModuleDiagnostics.Tests.csproj" />
```

- [ ] **Step 2: Write timer tests**

`framework/test/CrestCreates.ModuleDiagnostics.Tests/Timing/ModulePhaseTimerTests.cs`:

```csharp
using System;
using CrestCreates.ModuleDiagnostics.Stores;
using CrestCreates.ModuleDiagnostics.Timing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.ModuleDiagnostics.Tests.Timing;

public class ModulePhaseTimerTests
{
    [Fact]
    public void StartNew_Stop_ShouldRecordNonZeroElapsedTime()
    {
        var timer = ModulePhaseTimer.StartNew("TestModule", "PreInit");

        var result = timer.Stop(ModulePhaseStatus.Success);

        result.Elapsed.Should().BePositive();
        result.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Stop_WithSuccess_ShouldSetStatusToSuccess()
    {
        var timer = ModulePhaseTimer.StartNew("TestModule", "Init");

        var result = timer.Stop(ModulePhaseStatus.Success);

        result.Status.Should().Be(ModulePhaseStatus.Success);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void StopFailed_ShouldCaptureErrorMessage()
    {
        var timer = ModulePhaseTimer.StartNew("TestModule", "ConfigureServices");
        var exception = new InvalidOperationException("Connection string missing");

        var result = timer.StopFailed(exception);

        result.Status.Should().Be(ModulePhaseStatus.Failed);
        result.ErrorMessage.Should().Be("Connection string missing");
    }

    [Fact]
    public void Stop_ShouldPreserveModuleNameAndPhase()
    {
        var timer = ModulePhaseTimer.StartNew("SecurityModule", "PostInit");

        var result = timer.Stop(ModulePhaseStatus.Success);

        result.ModuleName.Should().Be("SecurityModule");
        result.Phase.Should().Be("PostInit");
    }

    [Fact]
    public void StopFailed_ShouldPreserveModuleNameAndPhase()
    {
        var timer = ModulePhaseTimer.StartNew("DataCoreModule", "AppInit");
        var exception = new Exception("Failed");

        var result = timer.StopFailed(exception);

        result.ModuleName.Should().Be("DataCoreModule");
        result.Phase.Should().Be("AppInit");
    }
}
```

- [ ] **Step 3: Write diagnostic struct tests (validation)**

`framework/test/CrestCreates.ModuleDiagnostics.Tests/Stores/ModulePhaseDiagnosticTests.cs`:

```csharp
using System;
using CrestCreates.ModuleDiagnostics.Stores;
using FluentAssertions;
using Xunit;

namespace CrestCreates.ModuleDiagnostics.Tests.Stores;

public class ModulePhaseDiagnosticTests
{
    [Fact]
    public void Constructor_ShouldSetAllProperties()
    {
        var elapsed = TimeSpan.FromMilliseconds(42);

        var diagnostic = new ModulePhaseDiagnostic(
            "TestModule",
            "Init",
            ModulePhaseStatus.Success,
            elapsed,
            null);

        diagnostic.ModuleName.Should().Be("TestModule");
        diagnostic.Phase.Should().Be("Init");
        diagnostic.Status.Should().Be(ModulePhaseStatus.Success);
        diagnostic.Elapsed.Should().Be(elapsed);
        diagnostic.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithError_ShouldSetErrorMessage()
    {
        var diagnostic = new ModulePhaseDiagnostic(
            "FailingModule",
            "ConfigureServices",
            ModulePhaseStatus.Failed,
            TimeSpan.FromMilliseconds(5),
            "DI resolution failed");

        diagnostic.Status.Should().Be(ModulePhaseStatus.Failed);
        diagnostic.ErrorMessage.Should().Be("DI resolution failed");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.ModuleDiagnostics.Tests/CrestCreates.ModuleDiagnostics.Tests.csproj`
Expected: All 7 tests pass

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.ModuleDiagnostics.Tests/ CrestCreates.slnx
git commit -m "test: add timer and diagnostic struct unit tests"
```

---

### Task 7: Write unit tests — ModuleDiagnosticsStore

**Files:**
- Create: `framework/test/CrestCreates.ModuleDiagnostics.Tests/Stores/ModuleDiagnosticsStoreTests.cs`

- [ ] **Step 1: Write store tests**

`framework/test/CrestCreates.ModuleDiagnostics.Tests/Stores/ModuleDiagnosticsStoreTests.cs`:

```csharp
using System;
using System.Linq;
using CrestCreates.ModuleDiagnostics.Stores;
using FluentAssertions;
using Xunit;

namespace CrestCreates.ModuleDiagnostics.Tests.Stores;

public class ModuleDiagnosticsStoreTests
{
    private readonly ModuleDiagnosticsStore _store = new();

    private ModulePhaseDiagnostic CreateSuccess(string moduleName, string phase)
    {
        return new ModulePhaseDiagnostic(moduleName, phase, ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null);
    }

    private ModulePhaseDiagnostic CreateFailure(string moduleName, string phase, string error)
    {
        return new ModulePhaseDiagnostic(moduleName, phase, ModulePhaseStatus.Failed, TimeSpan.FromMilliseconds(1), error);
    }

    [Fact]
    public void Record_ShouldStoreDiagnostic()
    {
        var diagnostic = CreateSuccess("TestModule", "Init");

        _store.Record(diagnostic);

        _store.GetAll().Should().ContainSingle()
            .Which.Should().Be(diagnostic);
    }

    [Fact]
    public void Record_MultiplePhases_ShouldIncreaseTotalCount()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateSuccess("M1", "Init"));
        _store.Record(CreateSuccess("M2", "PreInit"));

        _store.TotalCount.Should().Be(3);
    }

    [Fact]
    public void GetByModule_ShouldReturnAllPhasesForModule()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateSuccess("M1", "Init"));
        _store.Record(CreateSuccess("M2", "PreInit"));

        var m1Phases = _store.GetByModule("M1");

        m1Phases.Should().HaveCount(2);
        m1Phases.Select(p => p.Phase).Should().Contain(new[] { "PreInit", "Init" });
    }

    [Fact]
    public void GetByModule_UnknownModule_ShouldReturnEmpty()
    {
        _store.GetByModule("NonExistent").Should().BeEmpty();
    }

    [Fact]
    public void GetFailed_ShouldReturnOnlyFailures()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateFailure("M1", "Init", "error 1"));
        _store.Record(CreateSuccess("M2", "PreInit"));
        _store.Record(CreateFailure("M2", "ConfigureServices", "error 2"));

        var failed = _store.GetFailed();

        failed.Should().HaveCount(2);
        failed.All(f => f.Status == ModulePhaseStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public void Record_FailedPhase_ShouldSetHasFailures()
    {
        _store.HasFailures.Should().BeFalse();

        _store.Record(CreateFailure("M1", "Init", "error"));

        _store.HasFailures.Should().BeTrue();
    }

    [Fact]
    public void Record_OnlySuccess_ShouldKeepHasFailuresFalse()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateSuccess("M2", "Init"));

        _store.HasFailures.Should().BeFalse();
    }

    [Fact]
    public void GetByModule_WithPartialFailure_ShouldReturnAllPhases()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateFailure("M1", "Init", "failed"));

        var m1Phases = _store.GetByModule("M1");

        m1Phases.Should().HaveCount(2);
        m1Phases.Select(p => p.Status).Should().Contain(new[] { ModulePhaseStatus.Success, ModulePhaseStatus.Failed });
    }

    [Fact]
    public void GetAll_AfterMixedResults_ShouldContainBothSuccessAndFailure()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateFailure("M2", "Init", "failed"));

        var all = _store.GetAll();

        all.Should().HaveCount(2);
        all.Select(r => r.Status).Should().Contain(new[] { ModulePhaseStatus.Success, ModulePhaseStatus.Failed });
    }

    [Fact]
    public void EmptyStore_TotalCountShouldBeZero()
    {
        _store.TotalCount.Should().Be(0);
        _store.HasFailures.Should().BeFalse();
        _store.GetAll().Should().BeEmpty();
        _store.GetFailed().Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.ModuleDiagnostics.Tests/CrestCreates.ModuleDiagnostics.Tests.csproj --filter "FullyQualifiedName~ModuleDiagnosticsStoreTests"`
Expected: All 9 tests pass

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.ModuleDiagnostics.Tests/Stores/ModuleDiagnosticsStoreTests.cs
git commit -m "test: add ModuleDiagnosticsStore unit tests"
```

---

### Task 8: Write unit tests — ModuleHealthCheck

**Files:**
- Create: `framework/test/CrestCreates.ModuleDiagnostics.Tests/HealthChecks/ModuleHealthCheckTests.cs`

- [ ] **Step 1: Write health check tests**

`framework/test/CrestCreates.ModuleDiagnostics.Tests/HealthChecks/ModuleHealthCheckTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.ModuleDiagnostics.HealthChecks;
using CrestCreates.ModuleDiagnostics.Stores;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace CrestCreates.ModuleDiagnostics.Tests.HealthChecks;

public class ModuleHealthCheckTests
{
    [Fact]
    public async Task AllSuccess_ShouldReturnHealthy()
    {
        var store = new ModuleDiagnosticsStore();
        store.Record(new ModulePhaseDiagnostic("M1", "PreInit", ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null));
        store.Record(new ModulePhaseDiagnostic("M1", "Init", ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(2), null));
        var healthCheck = new ModuleHealthCheck(store);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task EmptyStore_ShouldReturnHealthy()
    {
        var store = new ModuleDiagnosticsStore();
        var healthCheck = new ModuleHealthCheck(store);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task AnyFailure_ShouldReturnUnhealthy()
    {
        var store = new ModuleDiagnosticsStore();
        store.Record(new ModulePhaseDiagnostic("M1", "PreInit", ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null));
        store.Record(new ModulePhaseDiagnostic("M1", "Init", ModulePhaseStatus.Failed, TimeSpan.FromMilliseconds(2), "error"));
        var healthCheck = new ModuleHealthCheck(store);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task AnyFailure_ShouldIncludeFailureDetailsInData()
    {
        var store = new ModuleDiagnosticsStore();
        store.Record(new ModulePhaseDiagnostic("SecurityModule", "Init", ModulePhaseStatus.Failed, TimeSpan.FromMilliseconds(5), "Unable to resolve IPasswordHasher"));
        var healthCheck = new ModuleHealthCheck(store);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Data.Should().ContainKey("failedPhases");
        result.Data["failedPhases"].Should().Be(1);
        result.Data.Should().ContainKey("failedDetails");
        var details = result.Data["failedDetails"] as List<Dictionary<string, string>>;
        details.Should().NotBeNull();
        details!.Should().HaveCount(1);
        details![0]["module"].Should().Be("SecurityModule");
        details![0]["phase"].Should().Be("Init");
        details![0]["error"].Should().Be("Unable to resolve IPasswordHasher");
    }

    [Fact]
    public async Task MultipleModulesFailed_ShouldCountAll()
    {
        var store = new ModuleDiagnosticsStore();
        store.Record(new ModulePhaseDiagnostic("M1", "Init", ModulePhaseStatus.Failed, TimeSpan.FromMilliseconds(1), "err1"));
        store.Record(new ModulePhaseDiagnostic("M2", "ConfigureServices", ModulePhaseStatus.Failed, TimeSpan.FromMilliseconds(1), "err2"));
        var healthCheck = new ModuleHealthCheck(store);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Data["failedPhases"].Should().Be(2);
        var details = result.Data["failedDetails"] as List<Dictionary<string, string>>;
        details.Should().HaveCount(2);
    }

    [Fact]
    public async Task AllSuccess_ShouldIncludeTotalPhasesInData()
    {
        var store = new ModuleDiagnosticsStore();
        store.Record(new ModulePhaseDiagnostic("M1", "PreInit", ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null));
        store.Record(new ModulePhaseDiagnostic("M1", "Init", ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null));
        store.Record(new ModulePhaseDiagnostic("M2", "PreInit", ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null));
        var healthCheck = new ModuleHealthCheck(store);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Data["totalPhases"].Should().Be(3);
        result.Data["failedPhases"].Should().Be(0);
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test framework/test/CrestCreates.ModuleDiagnostics.Tests/CrestCreates.ModuleDiagnostics.Tests.csproj --filter "FullyQualifiedName~ModuleHealthCheckTests"`
Expected: All 6 tests pass

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.ModuleDiagnostics.Tests/HealthChecks/ModuleHealthCheckTests.cs
git commit -m "test: add ModuleHealthCheck unit tests"
```

---

### Task 9: Modify BuildTasks — `GenerateAggregatedModuleCode.cs` to emit diagnostic code

**Files:**
- Modify: `build/CrestCreates.BuildTasks/GenerateAggregatedModuleCode.cs`

This is the core change. The `GenerateCode()` method must be updated to emit timer-wrapped code for each lifecycle phase call, with a static store, per-module-per-phase comments, and a summary log loop.

- [ ] **Step 1: Read current file to understand exact structure**

The current file is at `build/CrestCreates.BuildTasks/GenerateAggregatedModuleCode.cs`. Review the `GenerateCode` method (line 106 onwards) to understand the current template.

- [ ] **Step 2: Update GenerateCode to emit diagnostic infrastructure**

Modify the `GenerateCode` method to:

1. Add `using CrestCreates.ModuleDiagnostics.Timing;` and `using CrestCreates.ModuleDiagnostics.Stores;` to the generated usings block
2. Add a `private static readonly CrestCreates.ModuleDiagnostics.Stores.ModuleDiagnosticsStore _diagnostics = CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsModule.Store;` field — references the shared static store from `ModuleDiagnosticsModule`
3. Wrap each `OnConfigureServices` call block (lines 156-159) with timer + comment
4. Wrap `OnPreInitializeAsync` calls with timer + comment
5. Wrap `OnInitializeAsync` calls with timer + comment
6. Wrap `OnPostInitializeAsync` calls with timer + comment
7. Wrap `OnApplicationInitializationAsync` calls with timer + comment
8. After all initialization, emit the summary log foreach loop

All generated code uses fully-qualified type names (`CrestCreates.ModuleDiagnostics.Timing.ModulePhaseTimer`, `CrestCreates.ModuleDiagnostics.Stores.ModulePhaseStatus`) to avoid requiring a `using` in the generated file. The consuming project must reference `CrestCreates.ModuleDiagnostics` (which it will, since `ModuleDiagnosticsModule` is a `[CrestModule]` that gets discovered by the build tasks).

The key change pattern for each phase call. **Current** (ConfigureServices, lines 156-159):
```csharp
                try {
                    new {module.FullName}().OnConfigureServices(services);
                } catch (System.Exception ex) { System.Console.Error.WriteLine($"[ConfigureServices] {module.FullName}: {{ex}}"); throw; }
```

**Target** (each on its own line):
```csharp
                // ModuleDiagnostics: {module.FullName} → ConfigureServices
                var timer_cs_{i} = CrestCreates.ModuleDiagnostics.Timing.ModulePhaseTimer.StartNew("{module.FullName}", "ConfigureServices");
                try
                {{
                    new {module.FullName}().OnConfigureServices(services);
                    _diagnostics.Record(timer_cs_{i}.Stop(CrestCreates.ModuleDiagnostics.Stores.ModulePhaseStatus.Success));
                }}
                catch (System.Exception ex)
                {{
                    _diagnostics.Record(timer_cs_{i}.StopFailed(ex));
                    throw;
                }}
```

Similarly, wrap the PreInit/Init/PostInit calls in `InitializeAllModulesAsync` (lines ~187-212) and AppInit calls in `InitializeModulesAsync` (lines ~214-226).

Note: The code uses `{{` and `}}` in string formatting because `$"..."` strings escape `{` as `{{`. When generating C# code, single braces are what we want in the output, so inside a `$"..."` or `sb.AppendLine($"...")` call, braces need to be doubled.

After all initialization, add the summary log output at the end of `InitializeModulesAsync`:
```csharp
        var summary = _diagnostics.GetAll();
        foreach (var d in summary)
        {{
            if (d.Status == CrestCreates.ModuleDiagnostics.Stores.ModulePhaseStatus.Failed)
            {{
                logger?.LogError("[ModuleDiagnostics] {ModuleName}: Failed ({Phase} {Elapsed}ms → {ErrorMessage})", d.ModuleName, d.Phase, d.Elapsed.TotalMilliseconds, d.ErrorMessage);
            }}
            else
            {{
                logger?.LogInformation("[ModuleDiagnostics] {ModuleName}: Success ({Phase} {Elapsed}ms)", d.ModuleName, d.Phase, d.Elapsed.TotalMilliseconds);
            }}
        }}
```

- [ ] **Step 3: Build BuildTasks to verify**

Run: `dotnet build build/CrestCreates.BuildTasks/CrestCreates.BuildTasks.csproj`
Expected: Build succeeds

- [ ] **Step 4: Run existing BuildTasks tests**

Run: `dotnet test framework/test/CrestCreates.BuildTasks.Tests/CrestCreates.BuildTasks.Tests.csproj`
Expected: Existing tests pass

- [ ] **Step 5: Commit**

```bash
git add build/CrestCreates.BuildTasks/GenerateAggregatedModuleCode.cs
git commit -m "feat: emit module diagnostics code from BuildTasks generator"
```

---

### Task 10: Modify SourceGenerator — `ModuleSourceGenerator.cs` to emit diagnostic code

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/ModuleGenerator/ModuleSourceGenerator.cs`

Apply the same timing + record pattern to the `GenerateAutoModuleRegistration` method. All generated type references use fully-qualified names, same as Task 9.

- [ ] **Step 1: Update `GenerateAutoModuleRegistration` to emit diagnostic infrastructure**

1. Add `using CrestCreates.ModuleDiagnostics.Timing;` and `using CrestCreates.ModuleDiagnostics.Stores;` to the generated usings block
2. Add a `private static readonly CrestCreates.ModuleDiagnostics.Stores.ModuleDiagnosticsStore _diagnostics = CrestCreates.ModuleDiagnostics.Modules.ModuleDiagnosticsModule.Store;` field
3. For the `RegisterModules` method (ConfigureServices phase block, line ~167-190):
   - Add the comment `// ModuleDiagnostics: {module.FullName} → ConfigureServices` before each module block
   - Wrap each `OnConfigureServices` call with timer → Record using fully-qualified `ModulePhaseTimer.StartNew` and `ModulePhaseStatus.Success`
   - In the catch block, add `_diagnostics.Record(timer.StopFailed(ex));` before `throw`
4. For the `InitializeModulesAsync` method (lines ~197-222):
   - Add the comment + timer wrapping for PreInit, Init, PostInit, and AppInit phases
   - Each lifecycle call gets its own timer variable with a unique name
5. After all initialization, add the summary log foreach loop at end of `InitializeModulesAsync` (same pattern as Task 9)

Pattern for each phase in the generated output (one call per line):
```csharp
            // ModuleDiagnostics: WebModule → PreInit
            var timer_pi_0 = CrestCreates.ModuleDiagnostics.Timing.ModulePhaseTimer.StartNew("WebModule", "PreInit");
            try
            {
                await ((IModule)host.Services.GetRequiredService(descriptor.ModuleType)).OnPreInitializeAsync();
                _diagnostics.Record(timer_pi_0.Stop(CrestCreates.ModuleDiagnostics.Stores.ModulePhaseStatus.Success));
            }
            catch (Exception ex)
            {
                _diagnostics.Record(timer_pi_0.StopFailed(ex));
                throw;
            }
```

---

### Task 11: Update HealthCheck project reference

**Files:**
- Modify: `framework/src/CrestCreates.HealthCheck/CrestCreates.HealthCheck.csproj`

- [ ] **Step 1: Add ModuleDiagnostics reference**

Add this line to the existing `CrestCreates.HealthCheck.csproj` `ItemGroup` that contains the other `ProjectReference` entries:

```xml
    <ProjectReference Include="..\CrestCreates.ModuleDiagnostics\CrestCreates.ModuleDiagnostics.csproj" />
```

- [ ] **Step 2: Build HealthCheck project to verify**

Run: `dotnet build framework/src/CrestCreates.HealthCheck/CrestCreates.HealthCheck.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HealthCheck/CrestCreates.HealthCheck.csproj
git commit -m "feat: add ModuleDiagnostics reference to HealthCheck project"
```

---

### Task 12: Integration verification — full solution build

**Files:**
- (verification only, no code changes)

- [ ] **Step 1: Build the entire solution**

Run: `dotnet build`
Expected: Build succeeds with zero errors and zero new warnings

- [ ] **Step 2: Run all tests**

Run: `dotnet test`
Expected: All tests pass (pre-existing failures from other projects are unchanged; no new failures from our changes)

- [ ] **Step 3: Run module diagnostics tests specifically**

Run: `dotnet test framework/test/CrestCreates.ModuleDiagnostics.Tests/CrestCreates.ModuleDiagnostics.Tests.csproj -v n`
Expected: All 22 tests pass

- [ ] **Step 4: Commit (if any build fixes were needed)**

```bash
git add -A
git commit -m "chore: ensure full solution builds with module diagnostics"
```

---

### Task 13: Verify a sample project runs with diagnostic output

**Files:**
- Modify: (one sample project to add `ModuleDiagnosticsModule` dependency, if not already picked up)

- [ ] **Step 1: Add ModuleDiagnostics reference to a sample project**

Pick `samples/LibraryManagement/LibraryManagement.Web/LibraryManagement.Web.csproj` and add:
```xml
    <ProjectReference Include="..\..\..\framework\src\CrestCreates.ModuleDiagnostics\CrestCreates.ModuleDiagnostics.csproj" />
```

- [ ] **Step 2: Register ModuleDiagnosticsModule in the sample's WebModule**

Ensure the sample's WebModule `[CrestModule]` attribute has `typeof(ModuleDiagnosticsModule)` in its `DependsOn` (or in the constructor args).

Actually, check if the generated `ModuleAutoInitializer` already picks it up automatically (since the BuildTasks scan all referenced assemblies for `[CrestModule]`). The `CrestCreates.ModuleDiagnostics` project has `[CrestModule]` on `ModuleDiagnosticsModule`, so adding the project reference should be sufficient.

- [ ] **Step 3: Run the sample**

Run: `dotnet run --project samples/LibraryManagement/LibraryManagement.Web/LibraryManagement.Web.csproj`
Expected: Startup log output includes `[ModuleDiagnostics]` lines showing each module's phase timings

- [ ] **Step 4: Hit the health endpoint**

After the sample starts, in another terminal:
Run: `curl -s http://localhost:5000/health | jq .`
Expected: Response includes a `modules` check entry with `totalPhases` and `failedPhases`

- [ ] **Step 5: Revert sample changes (don't commit — sample changes are for verification only)**

```bash
git checkout -- samples/
```

- [ ] **Step 6: Commit final status (if code changes were needed)**

```bash
git status
# Only commit if there are verified code changes; otherwise, verification-only
```
