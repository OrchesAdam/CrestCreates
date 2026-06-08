# Phase 4: Capability Execution Pipeline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the unified Capability Execution Pipeline — the middleware chain that wraps every Capability invocation regardless of trigger source (HTTP, Workflow, Agent, BackgroundJob). Adds CapabilityExecutionContext, CapabilityExecutionResult, pipeline middleware pattern, and system event descriptors.

**Architecture:** Middleware delegate-chain pattern. `ICapabilityPipeline.ExecuteAsync(name, input)` resolves the CapabilityDescriptor from the registry, locates the `ICapabilityHandler<TInput, TOutput>` from DI, builds a middleware chain (Authorization → Validation → Idempotency → UoW → Handler → Events → Audit), and executes. Each middleware stage is an `ICapabilityPipelineMiddleware` resolved from DI. The pipeline emits `CapabilityExecuting`/`Succeeded`/`Failed`/`Compensated` system events during execution. The pipeline is the same regardless of trigger — HTTP, Workflow, Agent, and BackgroundJob all enter the same chain.

**Tech Stack:** .NET 10, C# 13, Microsoft.Extensions.DependencyInjection, xUnit + FluentAssertions, Moq (for middleware mocking), System.Text.Json

**Dependency Order:** Capability.Abstractions additions → Pipeline implementation → System event descriptors → Tests

---

### Task 0: CapabilityExecutionStatus + CapabilityExecutionContext + CapabilityExecutionResult

**Files:**
- Create: `framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionStatus.cs`
- Create: `framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs`
- Create: `framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionResult.cs`
- Create: `framework/src/CrestCreates.Capability.Abstractions/ICapabilityPipeline.cs`

- [ ] **Step 1: Write CapabilityExecutionStatus.cs**

```csharp
namespace CrestCreates.Capability.Abstractions;

public enum CapabilityExecutionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    TimedOut,
    Compensated
}
```

- [ ] **Step 2: Write CapabilityExecutionContext.cs**

```csharp
namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityExecutionContext
{
    public string CapabilityName { get; init; } = string.Empty;
    public int CapabilityVersion { get; init; }
    public string CapabilityContractHash { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    public string? CausationId { get; init; }
    public string? TenantId { get; init; }
    public string? UserId { get; init; }
    public string IdempotencyKey { get; init; } = Guid.NewGuid().ToString("N");
    public object? Input { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public IDictionary<string, object?> Items { get; init; } = new Dictionary<string, object?>();
    public CancellationToken CancellationToken { get; init; }
}
```

- [ ] **Step 3: Write CapabilityExecutionResult.cs**

```csharp
namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityExecutionResult
{
    public CapabilityExecutionStatus Status { get; init; }
    public object? Output { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AuditRecordId { get; init; }
    public IReadOnlyList<string> EmittedEventIds { get; init; } = Array.Empty<string>();

    public bool IsSuccess => Status == CapabilityExecutionStatus.Succeeded;

    public static CapabilityExecutionResult Success(object? output, TimeSpan duration, string? auditRecordId = null, IReadOnlyList<string>? emittedEventIds = null)
        => new()
        {
            Status = CapabilityExecutionStatus.Succeeded,
            Output = output,
            Duration = duration,
            AuditRecordId = auditRecordId,
            EmittedEventIds = emittedEventIds ?? Array.Empty<string>()
        };

    public static CapabilityExecutionResult Failure(string errorCode, string errorMessage, TimeSpan duration)
        => new()
        {
            Status = CapabilityExecutionStatus.Failed,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Duration = duration
        };

    public static CapabilityExecutionResult Timeout(TimeSpan duration)
        => new()
        {
            Status = CapabilityExecutionStatus.TimedOut,
            Duration = duration
        };
}
```

- [ ] **Step 4: Write ICapabilityPipeline.cs**

```csharp
namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityPipeline
{
    Task<CapabilityExecutionResult> ExecuteAsync(
        string capabilityName,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Capability.Abstractions/CrestCreates.Capability.Abstractions.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Capability.Abstractions/
git commit -m "feat: add CapabilityExecutionContext, CapabilityExecutionResult, ICapabilityPipeline"
```

---

### Task 1: Pipeline Middleware Delegate + Interface

**Files:**
- Create: `framework/src/CrestCreates.Capability/CapabilityPipelineMiddleware.cs`
- Create: `framework/src/CrestCreates.Capability/ICapabilityPipelineMiddleware.cs`

- [ ] **Step 1: Write ICapabilityPipelineMiddleware.cs**

```csharp
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public delegate Task<CapabilityExecutionResult> CapabilityPipelineDelegate(CapabilityExecutionContext context);

public interface ICapabilityPipelineMiddleware
{
    Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next);
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Capability/
git commit -m "feat: add CapabilityPipelineDelegate and ICapabilityPipelineMiddleware"
```

---

### Task 2: CapabilityPipeline Implementation + Builder

**Files:**
- Create: `framework/src/CrestCreates.Capability/CapabilityPipeline.cs`
- Create: `framework/src/CrestCreates.Capability/CapabilityPipelineBuilder.cs`

- [ ] **Step 1: Write CapabilityPipelineBuilder.cs**

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Capability;

public sealed class CapabilityPipelineBuilder
{
    private readonly List<Type> _middlewareTypes = new();

    public CapabilityPipelineBuilder Use<TMiddleware>() where TMiddleware : ICapabilityPipelineMiddleware
    {
        _middlewareTypes.Add(typeof(TMiddleware));
        return this;
    }

    public CapabilityPipelineBuilder Clear()
    {
        _middlewareTypes.Clear();
        return this;
    }

    internal IReadOnlyList<Type> MiddlewareTypes => _middlewareTypes.AsReadOnly();
}
```

- [ ] **Step 2: Write CapabilityPipeline.cs**

```csharp
using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Capability;

public sealed class CapabilityPipeline : ICapabilityPipeline
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICapabilityRegistry _registry;
    private readonly CapabilityPipelineBuilder _builder;

    public CapabilityPipeline(
        IServiceProvider serviceProvider,
        ICapabilityRegistry registry,
        CapabilityPipelineBuilder builder)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _builder = builder;
    }

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        string capabilityName,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        var descriptor = _registry.GetActiveVersion(capabilityName)
            ?? _registry.GetByName(capabilityName);

        if (descriptor == null)
        {
            return CapabilityExecutionResult.Failure(
                "CAPABILITY_NOT_FOUND",
                $"Capability '{capabilityName}' is not registered.",
                TimeSpan.Zero);
        }

        var context = new CapabilityExecutionContext
        {
            CapabilityName = descriptor.Name,
            CapabilityVersion = descriptor.Version,
            CapabilityContractHash = descriptor.ContractHash,
            Input = input,
            CancellationToken = ct
        };
        configureContext?.Invoke(context);

        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            // Build middleware chain
            CapabilityPipelineDelegate handler = async (ctx) =>
            {
                var handlerType = typeof(ICapabilityHandler<,>);
                // The handler is resolved by convention: DI contains ICapabilityHandler<TInput, TOutput>
                // For now, we use the Items bag to pass the resolved handler
                if (ctx.Items.TryGetValue("__handler", out var h) && h is ICapabilityHandler marker)
                {
                    var handlerInterface = marker.GetType().GetInterfaces()
                        .FirstOrDefault(i => i.IsGenericType
                            && i.GetGenericTypeDefinition() == typeof(ICapabilityHandler<,>));

                    if (handlerInterface != null)
                    {
                        var inputArg = ctx.Input;
                        var method = handlerInterface.GetMethod("ExecuteAsync")!;
                        var task = (Task)method.Invoke(marker, new[] { inputArg, ctx.CancellationToken })!;
                        await task.ConfigureAwait(false);

                        var resultProp = task.GetType().GetProperty("Result");
                        var output = resultProp?.GetValue(task);

                        return CapabilityExecutionResult.Success(
                            output,
                            DateTimeOffset.UtcNow - startedAt);
                    }
                }

                return CapabilityExecutionResult.Failure(
                    "HANDLER_NOT_FOUND",
                    $"No handler found for capability '{capabilityName}'.",
                    DateTimeOffset.UtcNow - startedAt);
            };

            // Apply middleware in reverse order (outermost first)
            var middlewareTypes = _builder.MiddlewareTypes;
            for (int i = middlewareTypes.Count - 1; i >= 0; i--)
            {
                var middlewareType = middlewareTypes[i];
                var middleware = (ICapabilityPipelineMiddleware)_serviceProvider.GetRequiredService(middlewareType);
                var next = handler;
                handler = (ctx) => middleware.InvokeAsync(ctx, next);
            }

            return await handler(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CapabilityExecutionResult.Timeout(DateTimeOffset.UtcNow - startedAt);
        }
        catch (Exception ex)
        {
            return CapabilityExecutionResult.Failure(
                "PIPELINE_ERROR",
                ex.Message,
                DateTimeOffset.UtcNow - startedAt);
        }
    }
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Capability/
git commit -m "feat: add CapabilityPipeline with middleware builder pattern"
```

---

### Task 3: Built-in Middleware — Authorization + Validation

**Files:**
- Create: `framework/src/CrestCreates.Capability/Middleware/AuthorizationMiddleware.cs`
- Create: `framework/src/CrestCreates.Capability/Middleware/ValidationMiddleware.cs`

- [ ] **Step 1: Write AuthorizationMiddleware.cs**

Create directory: `framework/src/CrestCreates.Capability/Middleware/`

```csharp
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

public sealed class AuthorizationMiddleware : ICapabilityPipelineMiddleware
{
    private readonly ICapabilityAuthorizationService? _authService;

    public AuthorizationMiddleware(ICapabilityAuthorizationService? authService = null)
    {
        _authService = authService;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        if (_authService != null)
        {
            var authorized = await _authService.AuthorizeAsync(
                context.CapabilityName, context.UserId, context.CancellationToken)
                .ConfigureAwait(false);

            if (!authorized)
            {
                return CapabilityExecutionResult.Failure(
                    "UNAUTHORIZED",
                    $"User '{context.UserId}' is not authorized for capability '{context.CapabilityName}'.",
                    TimeSpan.Zero);
            }
        }

        return await next(context).ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: Write ICapabilityAuthorizationService.cs** (contract for auth integration)

```csharp
namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityAuthorizationService
{
    Task<bool> AuthorizeAsync(string capabilityName, string? userId, CancellationToken ct);
}
```

- [ ] **Step 3: Write ValidationMiddleware.cs**

```csharp
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

public sealed class ValidationMiddleware : ICapabilityPipelineMiddleware
{
    public Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        // Input validation is performed against the CapabilityDescriptor's InputSchema.
        // Schema validation logic belongs to the Schema infrastructure.
        // For now, this middleware passes through — schema validation is deferred
        // until the Schema validation engine is implemented.
        return next(context);
    }
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Capability/
git commit -m "feat: add AuthorizationMiddleware, ValidationMiddleware, ICapabilityAuthorizationService"
```

---

### Task 4: System Event Descriptors

**Files:**
- Create: `framework/src/CrestCreates.Capability/SystemEventDescriptors.cs`

The spec §4.7 defines 4 system events. These are regular `EventDescriptor` instances that the Capability Pipeline registers with the `EventRegistry`.

- [ ] **Step 1: Write SystemEventDescriptors.cs**

```csharp
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Capability;

public static class SystemEventDescriptors
{
    // Payload schema for capability lifecycle events:
    // { capabilityName: string, capabilityVersion: int, correlationId: string, timestamp: string }

    public static readonly EventDescriptor CapabilityExecuting = new()
    {
        Id = "evt_sys_capability_executing",
        Name = "capability.executing",
        Version = 1,
        State = DescriptorState.Active,
        Category = EventCategory.Capability,
        Semantic = EventSemantic.StateTransition,
        Importance = EventImportance.Operational,
        ChangeKind = SchemaChangeKind.Additive
    };

    public static readonly EventDescriptor CapabilitySucceeded = new()
    {
        Id = "evt_sys_capability_succeeded",
        Name = "capability.succeeded",
        Version = 1,
        State = DescriptorState.Active,
        Category = EventCategory.Capability,
        Semantic = EventSemantic.Fact,
        Importance = EventImportance.Business,
        ChangeKind = SchemaChangeKind.Additive
    };

    public static readonly EventDescriptor CapabilityFailed = new()
    {
        Id = "evt_sys_capability_failed",
        Name = "capability.failed",
        Version = 1,
        State = DescriptorState.Active,
        Category = EventCategory.Capability,
        Semantic = EventSemantic.Fact,
        Importance = EventImportance.Business,
        ChangeKind = SchemaChangeKind.Additive
    };

    public static readonly EventDescriptor CapabilityCompensated = new()
    {
        Id = "evt_sys_capability_compensated",
        Name = "capability.compensated",
        Version = 1,
        State = DescriptorState.Active,
        Category = EventCategory.Capability,
        Semantic = EventSemantic.StateTransition,
        Importance = EventImportance.Business,
        ChangeKind = SchemaChangeKind.Additive
    };

    public static void RegisterAll(EventRegistry registry)
    {
        registry.Register(CapabilityExecuting);
        registry.Register(CapabilitySucceeded);
        registry.Register(CapabilityFailed);
        registry.Register(CapabilityCompensated);
    }
}
```

- [ ] **Step 2: Add project reference — CrestCreates.Capability → CrestCreates.Event.Abstractions**

Edit `framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj`, add:
```xml
<ProjectReference Include="..\CrestCreates.Event.Abstractions\CrestCreates.Event.Abstractions.csproj" />
```
And add reference to `CrestCreates.Event` for the `EventRegistry` type:
```xml
<ProjectReference Include="..\CrestCreates.Event\CrestCreates.Event.csproj" />
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Capability/
git commit -m "feat: add SystemEventDescriptors — 4 framework-defined capability lifecycle events"
```

---

### Task 5: Pipeline Registration Extension (DI Wiring)

**Files:**
- Create: `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`

- [ ] **Step 1: Write CapabilityServiceCollectionExtensions.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Capability;

public static class CapabilityServiceCollectionExtensions
{
    public static IServiceCollection AddCapabilityPipeline(
        this IServiceCollection services,
        Action<CapabilityPipelineBuilder>? configure = null)
    {
        var builder = new CapabilityPipelineBuilder();

        // Default middleware chain: Authorization → Validation
        builder.Use<AuthorizationMiddleware>();
        builder.Use<ValidationMiddleware>();

        configure?.Invoke(builder);

        services.TryAddSingleton(builder);
        services.TryAddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        services.TryAddTransient<AuthorizationMiddleware>();
        services.TryAddTransient<ValidationMiddleware>();

        return services;
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Capability/
git commit -m "feat: add CapabilityServiceCollectionExtensions for DI registration"
```

---

### Task 6: Pipeline Tests

**Files:**
- Modify: `framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj` (add Moq reference)
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityExecutionContextTests.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityExecutionResultTests.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityPipelineTests.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityPipelineBuilderTests.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/SystemEventDescriptorsTests.cs`

- [ ] **Step 1: Add Moq PackageReference to Capability.Tests.csproj**

Read the existing csproj and add:
```xml
<PackageReference Include="Moq" />
```

- [ ] **Step 2: Write CapabilityExecutionContextTests.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityExecutionContextTests
{
    [Fact]
    public void Context_Defaults_CorrelationId_To_New_Guid()
    {
        var ctx = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap",
            CapabilityVersion = 1,
            CapabilityContractHash = "abc123"
        };

        ctx.CorrelationId.Should().NotBeNullOrEmpty();
        ctx.CorrelationId.Length.Should().Be(32);
    }

    [Fact]
    public void Context_Defaults_IdempotencyKey_To_New_Guid()
    {
        var ctx = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap",
            CapabilityVersion = 1,
            CapabilityContractHash = "abc123"
        };

        ctx.IdempotencyKey.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Context_Items_Bag_Is_Mutable()
    {
        var ctx = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap",
            CapabilityVersion = 1,
            CapabilityContractHash = "abc123"
        };

        ctx.Items["key"] = "value";
        ctx.Items["key"].Should().Be("value");
    }

    [Fact]
    public void Context_ConfigureContext_Overrides_Defaults()
    {
        var ctx = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap",
            CapabilityVersion = 1,
            CapabilityContractHash = "abc123"
        };

        Action<CapabilityExecutionContext> configure = c =>
        {
            c.UserId = "user_01";
            c.TenantId = "tenant_01";
            c.CausationId = "cause_01";
        };
        configure(ctx);

        ctx.UserId.Should().Be("user_01");
        ctx.TenantId.Should().Be("tenant_01");
        ctx.CausationId.Should().Be("cause_01");
    }

    [Fact]
    public void Context_StartedAt_Is_Set_On_Creation()
    {
        var before = DateTimeOffset.UtcNow;
        var ctx = new CapabilityExecutionContext
        {
            CapabilityName = "test.cap",
            CapabilityVersion = 1,
            CapabilityContractHash = "abc123"
        };
        var after = DateTimeOffset.UtcNow;

        ctx.StartedAt.Should().BeOnOrAfter(before);
        ctx.StartedAt.Should().BeOnOrBefore(after);
    }
}
```

- [ ] **Step 3: Write CapabilityExecutionResultTests.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityExecutionResultTests
{
    [Fact]
    public void Success_Creates_Result_With_Succeeded_Status()
    {
        var result = CapabilityExecutionResult.Success("output", TimeSpan.FromMilliseconds(100));

        result.Status.Should().Be(CapabilityExecutionStatus.Succeeded);
        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Be("output");
        result.Duration.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Failure_Creates_Result_With_Failed_Status()
    {
        var result = CapabilityExecutionResult.Failure("ERR_01", "Something broke", TimeSpan.FromSeconds(1));

        result.Status.Should().Be(CapabilityExecutionStatus.Failed);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ERR_01");
        result.ErrorMessage.Should().Be("Something broke");
    }

    [Fact]
    public void Timeout_Creates_Result_With_TimedOut_Status()
    {
        var result = CapabilityExecutionResult.Timeout(TimeSpan.FromSeconds(30));

        result.Status.Should().Be(CapabilityExecutionStatus.TimedOut);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Success_Includes_EmittedEventIds()
    {
        var eventIds = new[] { "evt_01", "evt_02" };
        var result = CapabilityExecutionResult.Success(
            "output", TimeSpan.FromMilliseconds(50),
            emittedEventIds: eventIds);

        result.EmittedEventIds.Should().HaveCount(2);
        result.EmittedEventIds.Should().Contain("evt_01");
    }
}
```

- [ ] **Step 4: Write CapabilityPipelineBuilderTests.cs**

```csharp
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityPipelineBuilderTests
{
    private sealed class TestMiddlewareA : ICapabilityPipelineMiddleware
    {
        public Task<CapabilityExecutionResult> InvokeAsync(
            CapabilityExecutionContext context,
            CapabilityPipelineDelegate next)
            => next(context);
    }

    private sealed class TestMiddlewareB : ICapabilityPipelineMiddleware
    {
        public Task<CapabilityExecutionResult> InvokeAsync(
            CapabilityExecutionContext context,
            CapabilityPipelineDelegate next)
            => next(context);
    }

    [Fact]
    public void Use_Adds_Middleware_In_Order()
    {
        var builder = new CapabilityPipelineBuilder();
        builder.Use<TestMiddlewareA>();
        builder.Use<TestMiddlewareB>();

        builder.MiddlewareTypes.Should().HaveCount(2);
        builder.MiddlewareTypes[0].Should().Be(typeof(TestMiddlewareA));
        builder.MiddlewareTypes[1].Should().Be(typeof(TestMiddlewareB));
    }

    [Fact]
    public void Clear_Removes_All_Middleware()
    {
        var builder = new CapabilityPipelineBuilder();
        builder.Use<TestMiddlewareA>();
        builder.Clear();

        builder.MiddlewareTypes.Should().BeEmpty();
    }

    [Fact]
    public void Builder_Starts_Empty()
    {
        var builder = new CapabilityPipelineBuilder();
        builder.MiddlewareTypes.Should().BeEmpty();
    }
}
```

- [ ] **Step 5: Write CapabilityPipelineTests.cs**

This test uses a real CapabilityRegistry + mock handler to test the full pipeline.

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_CapabilityNotFound_ReturnsFailure()
    {
        var services = new ServiceCollection();
        var registry = new CapabilityRegistry();
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton(new CapabilityPipelineBuilder());
        services.AddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        var sp = services.BuildServiceProvider();

        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();
        var result = await pipeline.ExecuteAsync("nonexistent.cap");

        result.Status.Should().Be(CapabilityExecutionStatus.Failed);
        result.ErrorCode.Should().Be("CAPABILITY_NOT_FOUND");
    }

    [Fact]
    public async Task ExecuteAsync_CapabilityFound_Executes_Through_Pipeline()
    {
        var services = new ServiceCollection();
        var registry = new CapabilityRegistry();
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "test.echo",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active
        });
        services.AddSingleton<ICapabilityRegistry>(registry);

        var builder = new CapabilityPipelineBuilder();
        services.AddSingleton(builder);
        services.AddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        var sp = services.BuildServiceProvider();

        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();
        var result = await pipeline.ExecuteAsync("test.echo");

        result.Status.Should().Be(CapabilityExecutionStatus.Failed);
        result.ErrorCode.Should().Be("HANDLER_NOT_FOUND");
    }

    [Fact]
    public async Task ExecuteAsync_ConfigureContext_Overrides_Context_Values()
    {
        var services = new ServiceCollection();
        var registry = new CapabilityRegistry();
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "test.echo",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            State = DescriptorState.Active
        });
        services.AddSingleton<ICapabilityRegistry>(registry);
        services.AddSingleton(new CapabilityPipelineBuilder());
        services.AddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        var sp = services.BuildServiceProvider();

        var pipeline = sp.GetRequiredService<ICapabilityPipeline>();
        var result = await pipeline.ExecuteAsync("test.echo", configureContext: ctx =>
        {
            ctx.TenantId = "tenant_01";
            ctx.UserId = "user_01";
        });

        // Pipeline executed but handler not found (expected)
        result.ErrorCode.Should().Be("HANDLER_NOT_FOUND");
    }
}
```

- [ ] **Step 6: Write SystemEventDescriptorsTests.cs**

```csharp
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class SystemEventDescriptorsTests
{
    [Fact]
    public void All_System_Events_Have_Unique_Ids()
    {
        var ids = new[]
        {
            SystemEventDescriptors.CapabilityExecuting.Id,
            SystemEventDescriptors.CapabilitySucceeded.Id,
            SystemEventDescriptors.CapabilityFailed.Id,
            SystemEventDescriptors.CapabilityCompensated.Id
        };

        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void All_System_Events_Have_Capability_Category()
    {
        SystemEventDescriptors.CapabilityExecuting.Category.Should().Be(EventCategory.Capability);
        SystemEventDescriptors.CapabilitySucceeded.Category.Should().Be(EventCategory.Capability);
        SystemEventDescriptors.CapabilityFailed.Category.Should().Be(EventCategory.Capability);
        SystemEventDescriptors.CapabilityCompensated.Category.Should().Be(EventCategory.Capability);
    }

    [Fact]
    public void Executing_Has_StateTransition_Semantic()
    {
        SystemEventDescriptors.CapabilityExecuting.Semantic.Should().Be(EventSemantic.StateTransition);
        SystemEventDescriptors.CapabilityCompensated.Semantic.Should().Be(EventSemantic.StateTransition);
    }

    [Fact]
    public void Succeeded_And_Failed_Have_Fact_Semantic()
    {
        SystemEventDescriptors.CapabilitySucceeded.Semantic.Should().Be(EventSemantic.Fact);
        SystemEventDescriptors.CapabilityFailed.Semantic.Should().Be(EventSemantic.Fact);
    }

    [Fact]
    public void RegisterAll_Registers_All_Four_Events()
    {
        var registry = new Event.EventRegistry();
        SystemEventDescriptors.RegisterAll(registry);

        var all = registry.GetAll();
        all.Should().HaveCount(4);
    }

    [Fact]
    public void System_Events_Are_Active()
    {
        SystemEventDescriptors.CapabilityExecuting.State.Should().Be(DescriptorState.Active);
        SystemEventDescriptors.CapabilitySucceeded.State.Should().Be(DescriptorState.Active);
        SystemEventDescriptors.CapabilityFailed.State.Should().Be(DescriptorState.Active);
        SystemEventDescriptors.CapabilityCompensated.State.Should().Be(DescriptorState.Active);
    }
}
```

- [ ] **Step 7: Build and run tests**

First, add Moq to Capability.Tests.csproj if not already present. Then:
Run: `dotnet test framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj`
Expected: Build succeeded, all new + existing tests pass (~26 tests: 10 existing + 16 new).

- [ ] **Step 8: Commit**

```bash
git add framework/test/CrestCreates.Capability.Tests/
git commit -m "feat: add CapabilityPipeline tests — 16 tests for context, result, pipeline, builder, system events"
```

---

### Task 7: IDraftStore InMemory Implementation + Tests

**Files:**
- Create: `framework/src/CrestCreates.Draft/InMemoryDraftStore.cs`
- Create: `framework/test/CrestCreates.Draft.Tests/InMemoryDraftStoreTests.cs`

- [ ] **Step 1: Write InMemoryDraftStore.cs**

```csharp
using System.Collections.Concurrent;
using CrestCreates.Draft.Abstractions;

namespace CrestCreates.Draft;

public sealed class InMemoryDraftStore : IDraftStore
{
    private readonly ConcurrentDictionary<string, DraftRecord> _drafts = new();

    public Task<DraftRecord> SaveAsync(DraftRecord draft, CancellationToken ct = default)
    {
        draft = new DraftRecord
        {
            DraftId = draft.DraftId,
            DraftType = draft.DraftType,
            Schema = draft.Schema,
            TenantId = draft.TenantId,
            OwnerId = draft.OwnerId,
            PayloadJson = draft.PayloadJson,
            Status = draft.Status,
            CreatedAt = draft.CreatedAt == default ? DateTimeOffset.UtcNow : draft.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = draft.ExpiresAt
        };
        _drafts[draft.DraftId] = draft;
        return Task.FromResult(draft);
    }

    public Task<DraftRecord?> GetAsync(string draftId, CancellationToken ct = default)
    {
        _drafts.TryGetValue(draftId, out var draft);
        return Task.FromResult(draft);
    }

    public Task DeleteAsync(string draftId, CancellationToken ct = default)
    {
        _drafts.TryRemove(draftId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DraftRecord>> QueryAsync(DraftQuery query, CancellationToken ct = default)
    {
        var results = _drafts.Values.AsEnumerable();

        if (query.TenantId != null)
            results = results.Where(d => d.TenantId == query.TenantId);
        if (query.OwnerId != null)
            results = results.Where(d => d.OwnerId == query.OwnerId);
        if (query.DraftType != null)
            results = results.Where(d => d.DraftType == query.DraftType);
        if (query.Status != null)
            results = results.Where(d => d.Status == query.Status.Value);

        if (query.MaxResults.HasValue)
            results = results.Take(query.MaxResults.Value);

        return Task.FromResult<IReadOnlyList<DraftRecord>>(results.ToList().AsReadOnly());
    }
}
```

- [ ] **Step 2: Write InMemoryDraftStoreTests.cs**

```csharp
using CrestCreates.Draft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Draft.Tests;

public class InMemoryDraftStoreTests
{
    [Fact]
    public async Task SaveAsync_Persists_Draft()
    {
        var store = new InMemoryDraftStore();
        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "test.type",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            TenantId = "tenant_01",
            PayloadJson = "{\"name\":\"test\"}"
        };

        var saved = await store.SaveAsync(draft);

        saved.DraftId.Should().Be("draft_01");
        saved.UpdatedAt.Should().BeAfter(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task GetAsync_Returns_Saved_Draft()
    {
        var store = new InMemoryDraftStore();
        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "test.type",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            TenantId = "tenant_01"
        };
        await store.SaveAsync(draft);

        var retrieved = await store.GetAsync("draft_01");

        retrieved.Should().NotBeNull();
        retrieved!.DraftType.Should().Be("test.type");
    }

    [Fact]
    public async Task GetAsync_Missing_Returns_Null()
    {
        var store = new InMemoryDraftStore();
        var result = await store.GetAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_Removes_Draft()
    {
        var store = new InMemoryDraftStore();
        var draft = new DraftRecord
        {
            DraftId = "draft_01",
            DraftType = "test.type",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            TenantId = "tenant_01"
        };
        await store.SaveAsync(draft);
        await store.DeleteAsync("draft_01");

        var result = await store.GetAsync("draft_01");
        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_Filters_By_TenantId()
    {
        var store = new InMemoryDraftStore();
        await store.SaveAsync(new DraftRecord
        {
            DraftId = "d1", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_A"
        });
        await store.SaveAsync(new DraftRecord
        {
            DraftId = "d2", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_B"
        });

        var results = await store.QueryAsync(new DraftQuery { TenantId = "tenant_A" });
        results.Should().HaveCount(1);
        results[0].DraftId.Should().Be("d1");
    }
}
```

- [ ] **Step 3: Build and run tests**

Run: `dotnet test framework/test/CrestCreates.Draft.Tests/CrestCreates.Draft.Tests.csproj`
Expected: Build succeeded, 9 tests pass (4 existing + 5 new).

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Draft/InMemoryDraftStore.cs framework/test/CrestCreates.Draft.Tests/InMemoryDraftStoreTests.cs
git commit -m "feat: add InMemoryDraftStore implementation with 5 tests"
```

---

### Task 8: Full Build + All Tests + Final Commit

- [ ] **Step 1: Full solution build**

Run: `dotnet build CrestCreates.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run all unit tests**

Run: `dotnet test framework/test/CrestCreates.Schema.Tests framework/test/CrestCreates.Metadata.Tests framework/test/CrestCreates.Capability.Tests framework/test/CrestCreates.Draft.Tests framework/test/CrestCreates.Event.Tests framework/test/CrestCreates.Form.Tests framework/test/CrestCreates.HumanTask.Tests framework/test/CrestCreates.Workflow.Tests`
Expected: ~108 tests pass (87 existing + 21 new).

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: complete Phase 4 — Capability Execution Pipeline, system events, InMemoryDraftStore

- CapabilityExecutionContext, CapabilityExecutionResult, CapabilityExecutionStatus
- ICapabilityPipeline + CapabilityPipeline with middleware delegate chain
- CapabilityPipelineBuilder (fluent Use/Clear pattern)
- AuthorizationMiddleware + ValidationMiddleware (built-in)
- ICapabilityAuthorizationService (integration contract)
- SystemEventDescriptors: 4 capability lifecycle events (Executing/Succeeded/Failed/Compensated)
- CapabilityServiceCollectionExtensions for DI wiring
- InMemoryDraftStore with 5 tests
- 21 new tests (16 pipeline + 5 draft store)
- ~108 total tests passing"
```

---

## Phase 4 Summary

| Category | Count |
|----------|-------|
| New source files | 12 |
| New test files | 6 |
| New tests | ~21 |
| Key capability | Capabilities become executable through unified pipeline |
| Pipeline middleware | Authorization, Validation (extensible) |
| System events | 4 capability lifecycle events |
