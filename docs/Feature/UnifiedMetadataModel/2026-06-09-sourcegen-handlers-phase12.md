# Phase 12: Source-Gen Handler Invokers — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace registration-time reflection in `AddCapabilityHandler<T>` with source-generated handler invokers. The `HandlerInvokerSourceGenerator` currently emits placeholder comments — make it emit real `ICapabilityHandlerInvoker` wrapper classes and registration code.

**Architecture:** The generator discovers `ICapabilityHandler<TInput, TOutput>` implementations, identifies the capability name (from `[CapabilityName]` attribute or convention), generates a concrete `CapabilityName_Invoker` class that wraps handler → `ICapabilityHandlerInvoker`, and generates `CapabilityHandlerResolver.Register(...)` calls. All reflection is eliminated — the source gen handles registration-time type conversion once per handler.

**Tech Stack:** Roslyn IIncrementalGenerator (netstandard2.0), .NET 10, C# 13, xUnit

---

### Task 0: Replace TypedHandlerInvoker with DelegateHandlerInvoker

**Files:**
- Modify: `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`

Remove `TypedHandlerInvoker` class and replace `AddCapabilityHandler<T>` with delegate-based registration.

- [ ] **Step 1: Rewrite AddCapabilityHandler<T>**

Replace the reflection-based implementation with `DelegateHandlerInvoker`:

```csharp
public static IServiceCollection AddCapabilityHandler<THandler>(
    this IServiceCollection services,
    string capabilityName)
    where THandler : class, ICapabilityHandler
{
    services.TryAddTransient<THandler>();
    services.AddTransient<ICapabilityHandlerInvoker>(sp =>
    {
        var handler = sp.GetRequiredService<THandler>();
        return new DelegateHandlerInvoker(async (input, ct) =>
        {
            // Source-gen code replaces this with strongly-typed dispatch.
            // Phase 12: HandlerInvokerSourceGenerator emits wrappers that call
            // handler.ExecuteAsync directly — zero reflection.
            if (handler is ICapabilityHandler<object?, object?> typed)
                return await typed.ExecuteAsync(input, ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Handler '{typeof(THandler).Name}' does not implement ICapabilityHandler<object?, object?>.");
        });
    });

    return services;
}
```

Wait — that uses `ICapabilityHandler<object?, object?>` generic. The source gen replaces this entirely. Let me keep the existing reflection for now but add a clear comment that source-gen replaces it. The actual fix is in the generator.

Actually, let's keep it clean: remove `TypedHandlerInvoker` entirely. Replace `AddCapabilityHandler<T>` with the pattern that **requires** source-gen or manual `DelegateHandlerInvoker` registration. This forces the AOT-safe path:

```csharp
public static IServiceCollection AddCapabilityHandler<THandler>(
    this IServiceCollection services,
    string capabilityName,
    Func<THandler, ICapabilityHandlerInvoker> invokerFactory)
    where THandler : class, ICapabilityHandler
{
    services.TryAddTransient<THandler>();
    var resolver = new CapabilityHandlerResolver();
    // NOTE: This is registration-time, not DI auto-wired.
    // The source generator emits CapabilityHandlerResolver.Register() calls
    // using DelegateHandlerInvoker with strongly-typed delegates.
    services.AddSingleton<ICapabilityHandlerInvoker>(sp =>
    {
        var handler = sp.GetRequiredService<THandler>();
        return invokerFactory(handler);
    });
    return services;
}
```

Actually, the simplest correct fix: remove the reflection-based `AddCapabilityHandler<T>` overload, keep only the `AddCapabilityPipeline()` method, and document that handler registration is done via source-gen or manual `CapabilityHandlerResolver.Register()` + `DelegateHandlerInvoker`:

```csharp
public static IServiceCollection AddCapabilityHandlerInvoker(
    this IServiceCollection services,
    string capabilityName,
    Func<IServiceProvider, ICapabilityHandlerInvoker> factory)
{
    services.AddTransient(factory);
    return services;
}
```

And remove `TypedHandlerInvoker` class entirely. The source generator will call `CapabilityHandlerResolver.Register()` directly.

- [ ] **Step 2: Remove TypedHandlerInvoker, simplify extensions**

Replace `AddCapabilityHandler<T>` with `AddHandlerInvoker`:

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

        builder.Use<TenantMiddleware>();
        builder.Use<AuthorizationMiddleware>();
        builder.Use<ValidationMiddleware>();
        builder.Use<IdempotencyMiddleware>();
        builder.Use<EventPublishingMiddleware>();
        builder.Use<MetricsMiddleware>();

        configure?.Invoke(builder);

        services.TryAddSingleton(builder);
        services.TryAddSingleton<CapabilityHandlerResolver>();
        services.TryAddSingleton<ICapabilityHandlerResolver>(sp => sp.GetRequiredService<CapabilityHandlerResolver>());
        services.TryAddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        services.TryAddTransient<TenantMiddleware>();
        services.TryAddTransient<AuthorizationMiddleware>();
        services.TryAddTransient<ValidationMiddleware>();
        services.TryAddTransient<IdempotencyMiddleware>();
        services.TryAddTransient<EventPublishingMiddleware>();
        services.TryAddTransient<MetricsMiddleware>();

        return services;
    }

    /// <summary>
    /// Registers a handler invoker for a capability name.
    /// Prefer using the source generator (HandlerInvokerSourceGenerator) which
    /// emits strongly-typed DelegateHandlerInvoker wrappers at compile time.
    /// </summary>
    public static IServiceCollection AddHandlerInvoker(
        this IServiceCollection services,
        string capabilityName,
        ICapabilityHandlerInvoker invoker)
    {
        var resolver = new CapabilityHandlerResolver();
        resolver.Register(capabilityName, invoker);
        services.AddSingleton<ICapabilityHandlerInvoker>(invoker);
        return services;
    }
}
```

- [ ] **Step 3: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs
git commit -m "fix: remove TypedHandlerInvoker reflection — use DelegateHandlerInvoker/SourceGen instead"
```

---

### Task 1: Emit Real Invoker Code from HandlerInvokerSourceGenerator

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/HandlerInvokerSourceGenerator.cs`

Replace placeholder comments with actual invoker wrapper class + registration code.

- [ ] **Step 1: Rewrite GenerateHandlerRegistrations**

Replace the placeholder loop with real code generation:

```csharp
private static void GenerateHandlerRegistrations(
    SourceProductionContext spc,
    ImmutableArray<HandlerInvokerInfo?> handlers,
    Compilation compilation)
{
    var hasCapability = compilation.ReferencedAssemblyNames
        .Any(a => a.Name == "CrestCreates.Capability.Abstractions");
    if (!hasCapability) return;

    var validHandlers = handlers.Where(h => h != null && !string.IsNullOrEmpty(h!.CapabilityName)).ToList();
    if (validHandlers.Count == 0) return;

    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("using CrestCreates.Capability;");
    sb.AppendLine("using CrestCreates.Capability.Abstractions;");
    sb.AppendLine("using System.Runtime.CompilerServices;");
    sb.AppendLine();
    sb.AppendLine("namespace CrestCreates.Generated;");
    sb.AppendLine();
    sb.AppendLine("internal static class GeneratedHandlerRegistry");
    sb.AppendLine("{");
    sb.AppendLine("    [ModuleInitializer]");
    sb.AppendLine("    internal static void Register()");
    sb.AppendLine("    {");
    sb.AppendLine("        var resolver = new CapabilityHandlerResolver();");

    foreach (var handler in validHandlers)
    {
        if (handler == null) continue;
        var fullName = string.IsNullOrEmpty(handler.HandlerNamespace)
            ? handler.HandlerTypeName
            : $"{handler.HandlerNamespace}.{handler.HandlerTypeName}";

        var invokerClassName = $"{handler.HandlerTypeName}_Invoker";

        sb.AppendLine();
        sb.AppendLine($"        resolver.Register(\"{handler.CapabilityName}\",");
        sb.AppendLine($"            new {invokerClassName}());");

        // Emit invoker class at end
        sb.AppendLine();
    }

    sb.AppendLine("        CapabilityHandlerResolverProvider.SetResolver(resolver);");
    sb.AppendLine("    }");

    // Emit invoker wrapper classes
    foreach (var handler in validHandlers)
    {
        if (handler == null) continue;
        var fullName = string.IsNullOrEmpty(handler.HandlerNamespace)
            ? handler.HandlerTypeName
            : $"{handler.HandlerNamespace}.{handler.HandlerTypeName}";
        var invokerClassName = $"{handler.HandlerTypeName}_Invoker";

        sb.AppendLine();
        sb.AppendLine($"    internal sealed class {invokerClassName} : ICapabilityHandlerInvoker");
        sb.AppendLine("    {");
        sb.AppendLine($"        private {fullName}? _handler;");
        sb.AppendLine();
        sb.AppendLine($"        public void SetHandler({fullName} handler)");
        sb.AppendLine("        {");
        sb.AppendLine("            _handler = handler;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public async Task<object?> InvokeAsync(object? input, CancellationToken ct)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var typedInput = ({handler.InputTypeName})input!;");
        sb.AppendLine("            var result = await _handler!.ExecuteAsync(typedInput, ct).ConfigureAwait(false);");
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    sb.AppendLine("}");

    spc.AddSource("GeneratedHandlerRegistry.g.cs", sb.ToString());
}
```

- [ ] **Step 2: Add CapabilityHandlerResolverProvider back-reference**

Since the generated code calls `CapabilityHandlerResolverProvider`, we need a static hook in the Capability project:

Create: `framework/src/CrestCreates.Capability/CapabilityHandlerResolverProvider.cs`

```csharp
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public static class CapabilityHandlerResolverProvider
{
    private static ICapabilityHandlerResolver? _resolver;

    public static void SetResolver(ICapabilityHandlerResolver resolver)
    {
        _resolver = resolver;
    }

    public static ICapabilityHandlerResolver? GetResolver()
    {
        return _resolver;
    }
}
```

- [ ] **Step 3: Build generator + Capability project**

```bash
dotnet build framework/tools/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
```

- [ ] **Step 4: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/ framework/src/CrestCreates.Capability/
git commit -m "feat: source-gen handler invokers — emit real wrapper classes + registration code"
```

---

### Task 2: Tests — Source-Gen Handler End-to-End

**Files:**
- Create: `framework/test/CrestCreates.Capability.Tests/DelegateHandlerInvokerTests.cs`

- [ ] **Step 1: Write DelegateHandlerInvokerTests.cs (3 tests)**

```csharp
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class DelegateHandlerInvokerTests
{
    [Fact]
    public async Task InvokeAsync_PassesInputAndReturnsOutput()
    {
        var invoker = new DelegateHandlerInvoker((input, ct) =>
            Task.FromResult<object?>($"ECHO: {input}"));

        var result = await invoker.InvokeAsync("hello", CancellationToken.None);

        result.Should().Be("ECHO: hello");
    }

    [Fact]
    public async Task InvokeAsync_NullInput_PassesThrough()
    {
        var invoker = new DelegateHandlerInvoker((input, ct) =>
            Task.FromResult<object?>(input));

        var result = await invoker.InvokeAsync(null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_PropagatesCancellation()
    {
        var invoker = new DelegateHandlerInvoker(async (input, ct) =>
        {
            await Task.Delay(100, ct);
            return "done";
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await invoker.Invoking(i => i.InvokeAsync("test", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 2: Run tests + commit**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj
git add framework/test/CrestCreates.Capability.Tests/DelegateHandlerInvokerTests.cs
git commit -m "feat: add DelegateHandlerInvokerTests — 3 tests"
```

Expected: ~53 Capability tests (50 existing + 3 new).

---

### Task 3: Full Build + All Tests + Final Commit

- [ ] **Step 1: Full solution build**

Run: `dotnet build CrestCreates.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Expected: ~184 tests pass.

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "feat: complete Phase 12 — AOT-safe handler registration, zero reflection pipeline

- Removed TypedHandlerInvoker (reflection-based) from CapabilityServiceCollectionExtensions
- AddHandlerInvoker uses DelegateHandlerInvoker — AOT-safe, delegate-based
- HandlerInvokerSourceGenerator now emits real ICapabilityHandlerInvoker wrappers
  with strongly-typed ExecuteAsync dispatch + CapabilityHandlerResolverProvider
- CapabilityHandlerResolverProvider: static hook for source-gen registration
- 3 DelegateHandlerInvokerTests: input/output, null pass-through, cancellation

Pipeline handler resolution: fully AOT-safe
  CapabilityPipeline → resolver.Resolve(name) → DelegateHandlerInvoker.InvokeAsync
  Zero reflection at registration time, zero reflection at execution time

~184 total tests across all 12 phases"
```

---

## Phase 12 Summary

| Task | What it does |
|------|-------------|
| 0 | Remove `TypedHandlerInvoker` reflection, add `AddHandlerInvoker` with `DelegateHandlerInvoker` |
| 1 | Handler generator emits real invoker wrapper classes + registration code |
| 2 | Tests for DelegateHandlerInvoker (3 tests) |
| 3 | Full build + commit |
