# Phase 10: Observability — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add observability to the Capability Execution Pipeline — execution metrics (count, duration, success rate), pipeline telemetry via IEventPublisher, and execution auditing.

**Architecture:** `MetricsMiddleware` records execution count and duration per capability name. `IPipelineMetrics` is the abstraction with an in-memory default. `PipelineTelemetry` publishes structured telemetry events to the EventBus after each execution. The existing `EventPublishingMiddleware` already publishes `capability.succeeded/failed` — we extend it with richer telemetry payloads.

**Tech Stack:** .NET 10, C# 13, System.Diagnostics.Metrics (optional), xUnit + FluentAssertions

---

### Task 0: IPipelineMetrics + InMemoryPipelineMetrics

**Files:**
- Create: `framework/src/CrestCreates.Capability.Abstractions/IPipelineMetrics.cs`
- Create: `framework/src/CrestCreates.Capability/InMemoryPipelineMetrics.cs`

- [ ] **Step 1: Write IPipelineMetrics.cs**

```csharp
namespace CrestCreates.Capability.Abstractions;

public interface IPipelineMetrics
{
    void RecordExecution(string capabilityName, bool success, TimeSpan duration);
    PipelineMetricsSnapshot GetSnapshot();
}

public sealed class PipelineMetricsSnapshot
{
    public int TotalExecutions { get; init; }
    public int SuccessfulExecutions { get; init; }
    public int FailedExecutions { get; init; }
    public double AverageDurationMs { get; init; }
    public IReadOnlyDictionary<string, PerCapabilityMetrics> ByCapability { get; init; }
        = new Dictionary<string, PerCapabilityMetrics>();
}

public sealed class PerCapabilityMetrics
{
    public int Executions { get; init; }
    public int Successes { get; init; }
    public int Failures { get; init; }
    public double AverageDurationMs { get; init; }
}
```

- [ ] **Step 2: Write InMemoryPipelineMetrics.cs**

```csharp
using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class InMemoryPipelineMetrics : IPipelineMetrics
{
    private readonly ConcurrentDictionary<string, List<ExecutionRecord>> _records = new();

    public void RecordExecution(string capabilityName, bool success, TimeSpan duration)
    {
        _records.GetOrAdd(capabilityName, _ => new()).Add(new ExecutionRecord
        {
            Success = success,
            Duration = duration
        });
    }

    public PipelineMetricsSnapshot GetSnapshot()
    {
        var byCapability = new Dictionary<string, PerCapabilityMetrics>();
        int total = 0, succeeded = 0, failed = 0;
        double totalMs = 0;

        foreach (var kv in _records)
        {
            var records = kv.Value;
            var capTotal = records.Count;
            var capSuccess = records.Count(r => r.Success);
            var capFailed = capTotal - capSuccess;
            var capAvgMs = records.Average(r => r.Duration.TotalMilliseconds);

            total += capTotal;
            succeeded += capSuccess;
            failed += capFailed;
            totalMs += records.Sum(r => r.Duration.TotalMilliseconds);

            byCapability[kv.Key] = new PerCapabilityMetrics
            {
                Executions = capTotal,
                Successes = capSuccess,
                Failures = capFailed,
                AverageDurationMs = capAvgMs
            };
        }

        return new PipelineMetricsSnapshot
        {
            TotalExecutions = total,
            SuccessfulExecutions = succeeded,
            FailedExecutions = failed,
            AverageDurationMs = total > 0 ? totalMs / total : 0,
            ByCapability = byCapability
        };
    }

    private sealed class ExecutionRecord
    {
        public bool Success { get; init; }
        public TimeSpan Duration { get; init; }
    }
}
```

- [ ] **Step 3: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Capability.Abstractions/CrestCreates.Capability.Abstractions.csproj
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability.Abstractions/IPipelineMetrics.cs framework/src/CrestCreates.Capability/InMemoryPipelineMetrics.cs
git commit -m "feat: add IPipelineMetrics + InMemoryPipelineMetrics — execution tracking per capability"
```

---

### Task 2: MetricsMiddleware

**Files:**
- Create: `framework/src/CrestCreates.Capability/Middleware/MetricsMiddleware.cs`
- Modify: `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`

- [ ] **Step 1: Write MetricsMiddleware.cs**

```csharp
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

public sealed class MetricsMiddleware : ICapabilityPipelineMiddleware
{
    private readonly IPipelineMetrics? _metrics;

    public MetricsMiddleware(IPipelineMetrics? metrics = null)
    {
        _metrics = metrics;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var result = await next(context).ConfigureAwait(false);

        _metrics?.RecordExecution(
            context.CapabilityName,
            result.IsSuccess,
            DateTimeOffset.UtcNow - startedAt);

        return result;
    }
}
```

- [ ] **Step 2: Add to pipeline (last position, after EventPublishing)**

```csharp
builder.Use<MetricsMiddleware>();
```

Register:
```csharp
services.TryAddTransient<MetricsMiddleware>();
```

- [ ] **Step 3: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability/
git commit -m "feat: add MetricsMiddleware — records execution count, duration, success rate"
```

---

### Task 3: Enrich EventPublishingMiddleware with Telemetry

**Files:**
- Modify: `framework/src/CrestCreates.Capability/Middleware/EventPublishingMiddleware.cs`

Add richer telemetry to the event payload.

- [ ] **Step 1: Update EventPublishingMiddleware**

```csharp
await _publisher.PublishAsync(eventName, new
{
    capabilityName = context.CapabilityName,
    capabilityVersion = context.CapabilityVersion,
    correlationId = context.CorrelationId,
    tenantId = context.TenantId,
    userId = context.UserId,
    status = result.Status.ToString(),
    errorCode = result.ErrorCode,
    durationMs = result.Duration.TotalMilliseconds,
    timestamp = DateTimeOffset.UtcNow
}, context.CancellationToken).ConfigureAwait(false);
```

- [ ] **Step 2: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability/Middleware/EventPublishingMiddleware.cs
git commit -m "feat: enrich EventPublishingMiddleware with tenant, user, timestamp telemetry"
```

---

### Task 4: Tests — MetricsMiddleware + InMemoryPipelineMetrics

**Files:**
- Create: `framework/test/CrestCreates.Capability.Tests/MetricsMiddlewareTests.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/InMemoryPipelineMetricsTests.cs`

- [ ] **Step 1: Write InMemoryPipelineMetricsTests.cs (4 tests)**

```csharp
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class InMemoryPipelineMetricsTests
{
    [Fact]
    public void GetSnapshot_Empty_ReturnsZeros()
    {
        var metrics = new InMemoryPipelineMetrics();
        var snapshot = metrics.GetSnapshot();

        snapshot.TotalExecutions.Should().Be(0);
        snapshot.SuccessfulExecutions.Should().Be(0);
        snapshot.ByCapability.Should().BeEmpty();
    }

    [Fact]
    public void RecordExecution_TracksCounts()
    {
        var metrics = new InMemoryPipelineMetrics();
        metrics.RecordExecution("test.cap", true, TimeSpan.FromMilliseconds(10));
        metrics.RecordExecution("test.cap", false, TimeSpan.FromMilliseconds(20));
        metrics.RecordExecution("other.cap", true, TimeSpan.FromMilliseconds(5));

        var snapshot = metrics.GetSnapshot();
        snapshot.TotalExecutions.Should().Be(3);
        snapshot.SuccessfulExecutions.Should().Be(2);
        snapshot.FailedExecutions.Should().Be(1);
    }

    [Fact]
    public void GetSnapshot_PerCapabilityMetrics()
    {
        var metrics = new InMemoryPipelineMetrics();
        metrics.RecordExecution("cap.a", true, TimeSpan.FromMilliseconds(100));
        metrics.RecordExecution("cap.a", true, TimeSpan.FromMilliseconds(200));
        metrics.RecordExecution("cap.b", false, TimeSpan.FromMilliseconds(50));

        var snapshot = metrics.GetSnapshot();
        snapshot.ByCapability["cap.a"].Executions.Should().Be(2);
        snapshot.ByCapability["cap.a"].Successes.Should().Be(2);
        snapshot.ByCapability["cap.a"].AverageDurationMs.Should().Be(150);
        snapshot.ByCapability["cap.b"].Failures.Should().Be(1);
    }

    [Fact]
    public void GetSnapshot_AverageDuration_CalculatedCorrectly()
    {
        var metrics = new InMemoryPipelineMetrics();
        metrics.RecordExecution("test", true, TimeSpan.FromMilliseconds(100));
        metrics.RecordExecution("test", true, TimeSpan.FromMilliseconds(200));

        var snapshot = metrics.GetSnapshot();
        snapshot.AverageDurationMs.Should().Be(150);
    }
}
```

- [ ] **Step 2: Write MetricsMiddlewareTests.cs (3 tests)**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class MetricsMiddlewareTests
{
    [Fact]
    public async Task Passthrough_WhenNoMetrics()
    {
        var middleware = new MetricsMiddleware(null);
        var context = new CapabilityExecutionContext
        {
            CapabilityName = "test", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RecordsSuccess()
    {
        var metrics = new InMemoryPipelineMetrics();
        var middleware = new MetricsMiddleware(metrics);
        var context = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.FromMilliseconds(10))));

        var snapshot = metrics.GetSnapshot();
        snapshot.TotalExecutions.Should().Be(1);
        snapshot.SuccessfulExecutions.Should().Be(1);
    }

    [Fact]
    public async Task RecordsFailure()
    {
        var metrics = new InMemoryPipelineMetrics();
        var middleware = new MetricsMiddleware(metrics);
        var context = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Failure("ERR", "bad", TimeSpan.Zero)));

        var snapshot = metrics.GetSnapshot();
        snapshot.FailedExecutions.Should().Be(1);
    }
}
```

- [ ] **Step 3: Build, run tests, commit**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj
git add framework/test/CrestCreates.Capability.Tests/
git commit -m "feat: add MetricsMiddleware + InMemoryPipelineMetrics tests — 7 tests"
```

Expected: ~50 Capability tests (43 existing + 7 new).

---

### Task 5: Full Build + All Tests + Final Commit

- [ ] **Step 1: Full solution build**

Run: `dotnet build CrestCreates.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Expected: ~176 tests pass.

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "feat: complete Phase 10 — Observability, 7 tests

- IPipelineMetrics + InMemoryPipelineMetrics: per-capability execution tracking
  (count, success/failure, average duration, snapshot)
- MetricsMiddleware: records execution metrics for every pipeline invocation
- Enriched EventPublishingMiddleware: tenant, user, timestamp in telemetry payloads
- 7 new tests: 4 InMemoryPipelineMetrics + 3 MetricsMiddleware
- ~176 total tests across all 10 phases"
```

---

## Phase 10 Summary

| Task | Component | Tests |
|------|-----------|-------|
| 0 | IPipelineMetrics + InMemoryPipelineMetrics | 4 |
| 1 | MetricsMiddleware | 3 |
| 2 | Enriched EventPublishingMiddleware | — |
| 3 | Test files | — |
| 4 | Full build + commit | — |
| **Total** | **~4 new files** | **~7 new tests** |
