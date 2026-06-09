# Phase 13: Rate Limit + AOT Hardening — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add RateLimitMiddleware with configurable sliding-window rate limiting per capability, wire CapabilityProfile into the pipeline for runtime timeout/profile enforcement, and eliminate AOT trimming warnings via System.Text.Json source-gen contexts.

**Architecture:** `RateLimitMiddleware` uses a sliding-window counter per capability name — rejects requests above the configured rate limit. `IRateLimitStore` abstracts the counter backend (in-memory default). `CapabilityProfileMiddleware` injects resolved EffectiveProfile (Timeout, RequireApproval) into the execution context. AOT hardening adds `JsonSerializerContext` for descriptor types to eliminate IL2026 trimming warnings.

**Tech Stack:** .NET 10, C# 13, System.Text.Json source-gen, xUnit + FluentAssertions

---

### Task 0: IRateLimitStore + InMemoryRateLimitStore

**Files:**
- Create: `framework/src/CrestCreates.Capability.Abstractions/IRateLimitStore.cs`
- Create: `framework/src/CrestCreates.Capability/InMemoryRateLimitStore.cs`

- [ ] **Step 1: Write IRateLimitStore.cs**

```csharp
namespace CrestCreates.Capability.Abstractions;

public interface IRateLimitStore
{
    Task<bool> CheckRateLimitAsync(string key, int maxRequests, TimeSpan window, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write InMemoryRateLimitStore.cs** (sliding window)

```csharp
using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class InMemoryRateLimitStore : IRateLimitStore
{
    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();

    public Task<bool> CheckRateLimitAsync(string key, int maxRequests, TimeSpan window, CancellationToken ct = default)
    {
        var w = _windows.GetOrAdd(key, _ => new SlidingWindow(window));
        var allowed = w.TryIncrement(maxRequests);
        return Task.FromResult(allowed);
    }

    private sealed class SlidingWindow
    {
        private readonly TimeSpan _window;
        private readonly ConcurrentQueue<DateTimeOffset> _timestamps = new();
        private readonly object _lock = new();

        public SlidingWindow(TimeSpan window)
        {
            _window = window;
        }

        public bool TryIncrement(int maxRequests)
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                var cutoff = now - _window;

                while (_timestamps.TryPeek(out var oldest) && oldest < cutoff)
                    _timestamps.TryDequeue(out _);

                if (_timestamps.Count >= maxRequests)
                    return false;

                _timestamps.Enqueue(now);
                return true;
            }
        }
    }
}
```

- [ ] **Step 3: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Capability.Abstractions/CrestCreates.Capability.Abstractions.csproj
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability.Abstractions/IRateLimitStore.cs framework/src/CrestCreates.Capability/InMemoryRateLimitStore.cs
git commit -m "feat: add IRateLimitStore + InMemoryRateLimitStore — sliding window rate limiter"
```

---

### Task 1: RateLimitMiddleware

**Files:**
- Create: `framework/src/CrestCreates.Capability/Middleware/RateLimitMiddleware.cs`
- Modify: `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`

- [ ] **Step 1: Write RateLimitMiddleware.cs**

```csharp
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

public sealed class RateLimitMiddleware : ICapabilityPipelineMiddleware
{
    private readonly IRateLimitStore? _store;
    private readonly int _defaultMaxRequests;
    private readonly TimeSpan _defaultWindow;

    public RateLimitMiddleware(
        IRateLimitStore? store = null,
        int defaultMaxRequests = 100,
        TimeSpan? defaultWindow = null)
    {
        _store = store;
        _defaultMaxRequests = defaultMaxRequests;
        _defaultWindow = defaultWindow ?? TimeSpan.FromMinutes(1);
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        if (_store == null)
            return await next(context).ConfigureAwait(false);

        var allowed = await _store.CheckRateLimitAsync(
            context.CapabilityName,
            _defaultMaxRequests,
            _defaultWindow,
            context.CancellationToken).ConfigureAwait(false);

        if (!allowed)
        {
            return CapabilityExecutionResult.Failure(
                "RATE_LIMIT_EXCEEDED",
                $"Rate limit exceeded for '{context.CapabilityName}'. Max {_defaultMaxRequests} per {_defaultWindow.TotalSeconds}s.",
                TimeSpan.Zero);
        }

        return await next(context).ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: Add to pipeline (after Tenant, before Auth)**

```csharp
builder.Use<RateLimitMiddleware>();
```

Register:
```csharp
services.TryAddTransient<RateLimitMiddleware>();
```

- [ ] **Step 3: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability/
git commit -m "feat: add RateLimitMiddleware — sliding window rate limiting per capability"
```

---

### Task 2: Tests — RateLimitMiddleware + InMemoryRateLimitStore

**Files:**
- Create: `framework/test/CrestCreates.Capability.Tests/RateLimitMiddlewareTests.cs`

- [ ] **Step 1: Write RateLimitMiddlewareTests.cs (5 tests)**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class RateLimitMiddlewareTests
{
    private static CapabilityExecutionContext CreateContext()
    {
        return new CapabilityExecutionContext
        {
            CapabilityName = "test.cap", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };
    }

    [Fact]
    public async Task Passthrough_WhenNoStore()
    {
        var middleware = new RateLimitMiddleware(null);
        var result = await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AllowsRequests_WithinLimit()
    {
        var store = new InMemoryRateLimitStore();
        var middleware = new RateLimitMiddleware(store, defaultMaxRequests: 10);

        for (int i = 0; i < 10; i++)
        {
            var result = await middleware.InvokeAsync(CreateContext(), _ =>
                Task.FromResult(CapabilityExecutionResult.Success(i, TimeSpan.Zero)));
            result.IsSuccess.Should().BeTrue();
        }
    }

    [Fact]
    public async Task RejectsWhenOverLimit()
    {
        var store = new InMemoryRateLimitStore();
        var middleware = new RateLimitMiddleware(store, defaultMaxRequests: 3);

        for (int i = 0; i < 3; i++)
            await middleware.InvokeAsync(CreateContext(), _ =>
                Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        var result = await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("blocked", TimeSpan.Zero)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task DifferentCapabilities_HaveSeparateLimits()
    {
        var store = new InMemoryRateLimitStore();
        var middleware = new RateLimitMiddleware(store, defaultMaxRequests: 1);

        var ctxA = new CapabilityExecutionContext
        {
            CapabilityName = "cap.a", CapabilityVersion = 1, CapabilityContractHash = "a"
        };
        var ctxB = new CapabilityExecutionContext
        {
            CapabilityName = "cap.b", CapabilityVersion = 1, CapabilityContractHash = "b"
        };

        await middleware.InvokeAsync(ctxA, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("a1", TimeSpan.Zero)));

        var r2 = await middleware.InvokeAsync(ctxB, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("b1", TimeSpan.Zero)));

        r2.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SlidingWindow_ExpiresOldEntries()
    {
        var store = new InMemoryRateLimitStore();
        var middleware = new RateLimitMiddleware(store, defaultMaxRequests: 2,
            defaultWindow: TimeSpan.FromMilliseconds(50));

        await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("1", TimeSpan.Zero)));
        await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("2", TimeSpan.Zero)));

        // Over limit now
        var blocked = await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("3", TimeSpan.Zero)));
        blocked.ErrorCode.Should().Be("RATE_LIMIT_EXCEEDED");

        // Wait for window to expire
        await Task.Delay(100);

        var allowed = await middleware.InvokeAsync(CreateContext(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success("4", TimeSpan.Zero)));
        allowed.IsSuccess.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests + commit**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj
git add framework/test/CrestCreates.Capability.Tests/RateLimitMiddlewareTests.cs
git commit -m "feat: add RateLimitMiddlewareTests — 5 tests"
```

Expected: ~64 Capability tests.

---

### Task 3: AOT Hardening — JsonSerializerContext for Descriptors

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/CrestCreatesMetadataJsonContext.cs`

A System.Text.Json source-gen context for the descriptor types used in serialization (HashComputer, ManifestSerializer, SnapshotBuilder). This eliminates all remaining IL2026 trimming warnings.

- [ ] **Step 1: Write CrestCreatesMetadataJsonContext.cs**

```csharp
using System.Text.Json.Serialization;

namespace CrestCreates.Metadata.Abstractions;

[JsonSerializable(typeof(DescriptorManifest))]
[JsonSerializable(typeof(DescriptorSnapshot))]
[JsonSerializable(typeof(Schema.Abstractions.SchemaDescriptor))]
[JsonSerializable(typeof(Capability.Abstractions.CapabilityDescriptor))]
[JsonSerializable(typeof(Event.Abstractions.EventDescriptor))]
[JsonSerializable(typeof(Form.Abstractions.FormDescriptor))]
[JsonSerializable(typeof(HumanTask.Abstractions.HumanTaskDescriptor))]
[JsonSerializable(typeof(Workflow.Abstractions.WorkflowDescriptor))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class CrestCreatesMetadataJsonContext : JsonSerializerContext
{
}
```

- [ ] **Step 2: Update Metadata project to enable source-gen**

Add to `framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`:
```xml
<PropertyGroup>
  <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
</PropertyGroup>
```

And add package reference:
```xml
<PackageReference Include="System.Text.Json" />
```

- [ ] **Step 3: Update DescriptorManifestSerializer to use source-gen context**

```csharp
private static readonly JsonSerializerOptions Options = new()
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    TypeInfoResolver = CrestCreatesMetadataJsonContext.Default
};
```

- [ ] **Step 4: Build, verify trimming, commit**

```bash
dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj
git add framework/src/CrestCreates.Metadata.Abstractions/ framework/src/CrestCreates.Metadata/
git commit -m "feat: add JsonSerializerContext for AOT-safe descriptor serialization"
```

---

### Task 4: Full Build + All Tests + Final Commit

- [ ] **Step 1: Full solution build**

Run: `dotnet build CrestCreates.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Expected: ~196 tests pass.

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "feat: complete Phase 13 — Rate Limiting + AOT Hardening, 5 tests

- IRateLimitStore + InMemoryRateLimitStore: thread-safe sliding window rate limiter
- RateLimitMiddleware: enforces per-capability rate limits (configurable max reqs + window)
- Returns RATE_LIMIT_EXCEEDED when threshold is breached
- Per-capability isolation: different capabilities have separate counters
- Sliding window: old entries expire automatically

- CrestCreatesMetadataJsonContext: System.Text.Json source-gen context
  for all 6 descriptor types + Manifest + Snapshot
- Eliminates IL2026 trimming warnings for descriptor serialization

- 5 RateLimitMiddlewareTests: passthrough, within limit, over limit,
  per-capability isolation, sliding window expiry

Pipeline: RateLimit → Tenant → Auth → Valid → Idempotency → Handler → Events → Metrics
~196 total tests across all 13 phases"
```

---

## Phase 13 Summary

| Task | Component | Tests |
|------|-----------|-------|
| 0 | IRateLimitStore + InMemoryRateLimitStore | — |
| 1 | RateLimitMiddleware | — |
| 2 | RateLimit tests | 5 |
| 3 | AOT JsonSerializerContext | — |
| 4 | Full build + commit | — |
| **Total** | **~5 new files** | **~5 new tests** |

### Final pipeline after Phase 13:
```
RateLimit → Tenant → Auth → Validation → Idempotency → Handler → EventPublishing(telemetry) → Metrics
```