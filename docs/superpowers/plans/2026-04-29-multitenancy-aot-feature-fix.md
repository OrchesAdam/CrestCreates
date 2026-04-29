# Multi-Tenancy AOT & Feature Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all AOT blockers and feature gaps in the multi-tenancy module, and modernize integration test infrastructure to use TestContainers.

**Architecture:** Replace runtime reflection with compile-time Source Generators for tenant filter configuration and DbContext factory. Remove dead code (Schema enum, duplicate ITenantInfo). Async-ify ICurrentTenant.Change. Enable disabled resolvers. Introduce TestContainers for integration tests.

**Tech Stack:** .NET 10, Roslyn Source Generators (IIncrementalGenerator), Testcontainers 4.11.0, xUnit, FluentAssertions

---

## File Structure

### New Files
| File | Responsibility |
|------|---------------|
| `framework/tools/CrestCreates.CodeGenerator/TenantFilterGenerator/TenantFilterSourceGenerator.cs` | Source Generator: generates `HasQueryFilter` for each `[Entity]` + `IMultiTenant` class |
| `framework/tools/CrestCreates.CodeGenerator/TenantDbContextFactoryGenerator/TenantDbContextFactorySourceGenerator.cs` | Source Generator: generates AoT-safe `ITenantDbContextFactory` implementation |
| `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/ITenantDbContextFactory.cs` | Interface for tenant-scoped DbContext creation |
| `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/DefaultTenantDbContextFactory.cs` | Default (reflection-based) factory for non-AoT scenarios |
| `framework/test/CrestCreates.Application.Tests/Tenants/QueryStringTenantResolverTests.cs` | Unit tests for QueryString resolver |
| `framework/test/CrestCreates.Application.Tests/Tenants/CookieTenantResolverTests.cs` | Unit tests for Cookie resolver |
| `framework/test/CrestCreates.Application.Tests/Tenants/RouteTenantResolverTests.cs` | Unit tests for Route resolver |

### Modified Files
| File | Change |
|------|-------|
| `framework/src/CrestCreates.MultiTenancy.Abstract/ICurrentTenant.cs` | `Change` → `ChangeAsync` |
| `framework/src/CrestCreates.MultiTenancy/CurrentTenant.cs` | Implement `ChangeAsync`, remove `Change` |
| `framework/src/CrestCreates.MultiTenancy/Middleware/MultiTenancyMiddleware.cs` | Use `ChangeAsync` |
| `framework/src/CrestCreates.MultiTenancy/Interceptors/MultiTenantMoAttribute.cs` | Use `ChangeAsync` |
| `framework/src/CrestCreates.MultiTenancy/MultiTenancyOptions.cs` | Remove `Schema` from `TenantIsolationStrategy` |
| `framework/src/CrestCreates.MultiTenancy/MultiTenancyExtensions.cs` | Enable 3 resolvers, remove `BuildServiceProvider()` |
| `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/MultiTenancyDiscriminator.cs` | Remove reflection, call generated `TenantFilterConfiguration.ApplyAll` |
| `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/TenantConnectionStringResolver.cs` | Use `ITenantDbContextFactory` |
| `Directory.Packages.props` | Add Testcontainers packages |
| `framework/test/CrestCreates.IntegrationTests/CrestCreates.IntegrationTests.csproj` | Add Testcontainers refs |
| `framework/test/CrestCreates.IntegrationTests/WebApplicationFactory.cs` | TestContainers PostgreSQL |
| `framework/test/CrestCreates.TestBase/CrestCreates.TestBase.csproj` | Add Testcontainers refs |

### Deleted Files
| File | Reason |
|------|--------|
| `framework/src/CrestCreates.Domain.Shared/MultiTenancy/ITenantInfo.cs` | Duplicate of `MultiTenancy.Abstract` definitions |

---

### Task 1: ICurrentTenant.ChangeAsync — Remove Sync Blocking

**Files:**
- Modify: `framework/src/CrestCreates.MultiTenancy.Abstract/ICurrentTenant.cs`
- Modify: `framework/src/CrestCreates.MultiTenancy/CurrentTenant.cs`
- Modify: `framework/src/CrestCreates.MultiTenancy/Middleware/MultiTenancyMiddleware.cs`
- Modify: `framework/src/CrestCreates.MultiTenancy/Interceptors/MultiTenantMoAttribute.cs`
- Test: existing tests in `framework/test/CrestCreates.Application.Tests/Tenants/`

- [ ] **Step 1: Update ICurrentTenant interface**

Replace `IDisposable Change(string tenantId)` with `Task<IDisposable> ChangeAsync(string tenantId)` in `framework/src/CrestCreates.MultiTenancy.Abstract/ICurrentTenant.cs`:

```csharp
namespace CrestCreates.MultiTenancy.Abstract
{
    public interface ICurrentTenant
    {
        ITenantInfo? Tenant { get; }

        string? Id { get; }

        Task<IDisposable> ChangeAsync(string tenantId);

        void SetTenantId(string tenantId);
    }
}
```

- [ ] **Step 2: Update CurrentTenant implementation**

Replace `Change` with `ChangeAsync` in `framework/src/CrestCreates.MultiTenancy/CurrentTenant.cs`. Remove `GetAwaiter().GetResult()`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.MultiTenancy
{
    public class CurrentTenant : ICurrentTenant
    {
        private readonly AsyncLocal<TenantContextHolder> _currentTenant = new AsyncLocal<TenantContextHolder>();
        private readonly IServiceProvider _serviceProvider;

        public CurrentTenant(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ITenantInfo? Tenant => _currentTenant.Value?.Tenant;
        public string? Id => Tenant?.Id;

        public async Task<IDisposable> ChangeAsync(string tenantId)
        {
            var oldTenant = Tenant;
            ITenantInfo? newTenant = null;

            if (!string.IsNullOrEmpty(tenantId))
            {
                var tenantProvider = _serviceProvider.GetRequiredService<ITenantProvider>();
                newTenant = await tenantProvider.GetTenantAsync(tenantId);
            }

            _currentTenant.Value = new TenantContextHolder { Tenant = newTenant };

            return new DisposeAction(() =>
            {
                _currentTenant.Value = new TenantContextHolder { Tenant = oldTenant };
            });
        }

        public void SetTenantId(string tenantId)
        {
            if (string.IsNullOrEmpty(tenantId))
            {
                _currentTenant.Value = new TenantContextHolder { Tenant = null };
                return;
            }

            var tenantProvider = _serviceProvider.GetRequiredService<ITenantProvider>();
            var tenant = tenantProvider.GetTenantAsync(tenantId).GetAwaiter().GetResult();
            _currentTenant.Value = new TenantContextHolder { Tenant = tenant };
        }

        private class TenantContextHolder
        {
            public ITenantInfo? Tenant { get; set; }
        }

        private class DisposeAction : IDisposable
        {
            private readonly Action _action;

            public DisposeAction(Action action)
            {
                _action = action;
            }

            public void Dispose()
            {
                _action();
            }
        }
    }
}
```

Note: `SetTenantId` retains sync-over-async because it is used in non-async AOP contexts where the Rougamo interceptor cannot be async. This is an intentional tradeoff.

- [ ] **Step 3: Update MultiTenancyMiddleware**

In `framework/src/CrestCreates.MultiTenancy/Middleware/MultiTenancyMiddleware.cs`, change `currentTenant.Change(...)` to `await currentTenant.ChangeAsync(...)`:

```csharp
// Line 36: change from
using (currentTenant.Change(resolutionResult.TenantId))
// to
using (await currentTenant.ChangeAsync(resolutionResult.TenantId))
```

The full `InvokeAsync` method becomes:

```csharp
public async Task InvokeAsync(
    HttpContext context,
    ICurrentTenant currentTenant,
    ITenantResolver tenantResolver)
{
    var resolutionResult = await tenantResolver.ResolveAsync(context);

    if (resolutionResult.IsResolved && !string.IsNullOrEmpty(resolutionResult.TenantId))
    {
        _logger.LogDebug("Tenant resolved: {TenantId} by {ResolvedBy}", resolutionResult.TenantId, resolutionResult.ResolvedBy);

        using (await currentTenant.ChangeAsync(resolutionResult.TenantId))
        {
            if (currentTenant.Tenant == null)
            {
                _logger.LogWarning("Tenant is unavailable: {TenantId}", resolutionResult.TenantId);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    Code = StatusCodes.Status403Forbidden,
                    Message = "租户不存在或已停用",
                    TraceId = context.TraceIdentifier
                }));
                return;
            }

            await _next(context);
        }
    }
    else if (resolutionResult.Error != null)
    {
        _logger.LogWarning("Tenant resolution failed: {ErrorCode} - {ErrorMessage}", resolutionResult.Error.Code, resolutionResult.Error.Message);

        if (resolutionResult.Error.Code == "TENANT_INACTIVE")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                Code = StatusCodes.Status403Forbidden,
                Message = "租户已停用",
                TraceId = context.TraceIdentifier
            }));
            return;
        }

        await _next(context);
    }
    else
    {
        _logger.LogDebug("No tenant resolved, continuing without tenant context");
        await _next(context);
    }
}
```

- [ ] **Step 4: Update MultiTenantMoAttribute**

In `framework/src/CrestCreates.MultiTenancy/Interceptors/MultiTenantMoAttribute.cs`, the `OnEntryAsync` method uses `currentTenant.SetTenantId(tenantId)` which is still sync — no change needed here. The `ChangeAsync` is not used in this AOP interceptor because Rougamo's `OnEntryAsync` cannot change the method's control flow to add a `using` scope. `SetTenantId` remains the correct API for this context.

- [ ] **Step 5: Build and verify**

Run: `dotnet build framework/src/CrestCreates.MultiTenancy && dotnet build framework/src/CrestCreates.MultiTenancy.Abstract && dotnet build framework/src/CrestCreates.MultiTenancy/Middleware`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.MultiTenancy.Abstract/ICurrentTenant.cs framework/src/CrestCreates.MultiTenancy/CurrentTenant.cs framework/src/CrestCreates.MultiTenancy/Middleware/MultiTenancyMiddleware.cs
git commit -m "refactor(multitenancy): replace ICurrentTenant.Change with ChangeAsync to eliminate sync-over-async blocking"
```

---

### Task 2: Remove Schema Isolation Enum Value

**Files:**
- Modify: `framework/src/CrestCreates.MultiTenancy/MultiTenancyOptions.cs`

- [ ] **Step 1: Remove Schema from TenantIsolationStrategy**

In `framework/src/CrestCreates.MultiTenancy/MultiTenancyOptions.cs`, remove the `Schema` member from the `TenantIsolationStrategy` enum:

```csharp
public enum TenantIsolationStrategy
{
    Database,
    Discriminator
}
```

- [ ] **Step 2: Search for any references to TenantIsolationStrategy.Schema**

Run: `grep -r "TenantIsolationStrategy.Schema" framework/src/ framework/test/ samples/`
Expected: No matches (confirmed in analysis)

- [ ] **Step 3: Build and verify**

Run: `dotnet build framework/src/CrestCreates.MultiTenancy`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.MultiTenancy/MultiTenancyOptions.cs
git commit -m "refactor(multitenancy): remove TenantIsolationStrategy.Schema enum value (unimplemented, YAGNI)"
```

---

### Task 3: Unify ITenantInfo — Remove Domain.Shared Duplicate

**Files:**
- Delete: `framework/src/CrestCreates.Domain.Shared/MultiTenancy/ITenantInfo.cs`

- [ ] **Step 1: Search for all references to Domain.Shared ITenantInfo**

Run: `grep -r "CrestCreates.Domain.MultiTenancy" framework/src/ framework/test/ samples/`
Check all files that reference `CrestCreates.Domain.MultiTenancy.ITenantInfo`, `CrestCreates.Domain.MultiTenancy.TenantInfo`, or `CrestCreates.Domain.MultiTenancy.ITenantProvider`.

- [ ] **Step 2: Move the file to 99_RecycleBin**

Per project file deletion rules, move instead of delete:

```bash
mkdir -p 99_RecycleBin
mv framework/src/CrestCreates.Domain.Shared/MultiTenancy/ITenantInfo.cs 99_RecycleBin/
```

- [ ] **Step 3: Update any referencing files to use CrestCreates.MultiTenancy.Abstract**

For each file found in Step 1, replace:
- `using CrestCreates.Domain.MultiTenancy;` → `using CrestCreates.MultiTenancy.Abstract;`
- `CrestCreates.Domain.MultiTenancy.ITenantInfo` → `CrestCreates.MultiTenancy.Abstract.ITenantInfo`
- `CrestCreates.Domain.MultiTenancy.ITenantProvider` → `CrestCreates.MultiTenancy.Abstract.ITenantProvider`
- `CrestCreates.Domain.MultiTenancy.TenantInfo` → `CrestCreates.MultiTenancy.Abstract.TenantInfo`

- [ ] **Step 4: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Domain.Shared && dotnet build framework/src/CrestCreates.MultiTenancy`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(multitenancy): remove duplicate ITenantInfo from Domain.Shared, unify to MultiTenancy.Abstract"
```

---

### Task 4: Enable Commented-Out Resolvers

**Files:**
- Modify: `framework/src/CrestCreates.MultiTenancy/MultiTenancyExtensions.cs`

- [ ] **Step 1: Uncomment the three resolver registrations**

In `framework/src/CrestCreates.MultiTenancy/MultiTenancyExtensions.cs`, replace the commented block (lines 111-125) with active code:

```csharp
if (strategy.HasFlag(TenantResolutionStrategy.QueryString))
{
    resolvers.Add(typeof(QueryStringTenantResolver));
}

if (strategy.HasFlag(TenantResolutionStrategy.Cookie))
{
    resolvers.Add(typeof(CookieTenantResolver));
}

if (strategy.HasFlag(TenantResolutionStrategy.Route))
{
    resolvers.Add(typeof(RouteTenantResolver));
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build framework/src/CrestCreates.MultiTenancy`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.MultiTenancy/MultiTenancyExtensions.cs
git commit -m "feat(multitenancy): enable QueryString, Cookie, and Route tenant resolvers in DI registration"
```

---

### Task 5: Remove BuildServiceProvider() from MultiTenancyExtensions

**Files:**
- Modify: `framework/src/CrestCreates.MultiTenancy/MultiTenancyExtensions.cs`

- [ ] **Step 1: Refactor AddInMemoryTenantProvider to avoid BuildServiceProvider**

Replace the `AddInMemoryTenantProvider` method. Instead of calling `services.BuildServiceProvider()`, register the provider lazily:

```csharp
public static IServiceCollection AddInMemoryTenantProvider(
    this IServiceCollection services,
    Action<InMemoryTenantProvider> configure = null)
{
    services.TryAddSingleton<ITenantProvider>(sp =>
    {
        var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryTenantProvider>>();
        var provider = new InMemoryTenantProvider(logger);
        configure?.Invoke(provider);
        return provider;
    });
    return services;
}
```

- [ ] **Step 2: Refactor AddMultiTenancyWithInMemory to avoid BuildServiceProvider**

Replace the method. Accept `TenantResolutionStrategy` as an optional parameter with default, and read from `IOptions` post-build:

```csharp
public static IServiceCollection AddMultiTenancyWithInMemory(
    this IServiceCollection services,
    Action<MultiTenancyOptions> configureOptions = null,
    Action<InMemoryTenantProvider> configureTenants = null)
{
    services.AddMultiTenancy(configureOptions);
    services.AddTenantResolversFromOptions();
    services.AddInMemoryTenantProvider(configureTenants);
    return services;
}
```

Add a helper method:

```csharp
private static IServiceCollection AddTenantResolversFromOptions(
    this IServiceCollection services)
{
    services.TryAddScoped<ITenantResolver>(sp =>
    {
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MultiTenancyOptions>>();
        var strategy = options.Value.ResolutionStrategy;
        var resolvers = BuildResolverList(strategy);
        var resolverInstances = resolvers
            .Select(t => (ITenantResolver)sp.GetRequiredService(t))
            .ToArray();
        return new CompositeTenantResolver(
            resolverInstances,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CompositeTenantResolver>>());
    });

    // Also register individual resolvers so they can be resolved
    services.TryAddScoped<HeaderTenantResolver>();
    services.TryAddScoped<SubdomainTenantResolver>();
    services.TryAddScoped<QueryStringTenantResolver>();
    services.TryAddScoped<CookieTenantResolver>();
    services.TryAddScoped<RouteTenantResolver>();

    return services;
}

private static List<Type> BuildResolverList(TenantResolutionStrategy strategy)
{
    var resolvers = new List<Type>();
    if (strategy.HasFlag(TenantResolutionStrategy.Header))
        resolvers.Add(typeof(HeaderTenantResolver));
    if (strategy.HasFlag(TenantResolutionStrategy.Subdomain))
        resolvers.Add(typeof(SubdomainTenantResolver));
    if (strategy.HasFlag(TenantResolutionStrategy.QueryString))
        resolvers.Add(typeof(QueryStringTenantResolver));
    if (strategy.HasFlag(TenantResolutionStrategy.Cookie))
        resolvers.Add(typeof(CookieTenantResolver));
    if (strategy.HasFlag(TenantResolutionStrategy.Route))
        resolvers.Add(typeof(RouteTenantResolver));
    return resolvers;
}
```

- [ ] **Step 3: Refactor AddMultiTenancyWithConfiguration similarly**

```csharp
public static IServiceCollection AddMultiTenancyWithConfiguration(
    this IServiceCollection services,
    Action<MultiTenancyOptions> configureOptions = null)
{
    services.AddMultiTenancy(configureOptions);
    services.AddTenantResolversFromOptions();
    services.AddConfigurationTenantProvider();
    return services;
}
```

- [ ] **Step 4: Refactor AddTenantResolvers to use BuildResolverList**

Update the existing `AddTenantResolvers` method to use the shared `BuildResolverList` helper and register all resolver types:

```csharp
public static IServiceCollection AddTenantResolvers(
    this IServiceCollection services,
    TenantResolutionStrategy strategy)
{
    var resolvers = BuildResolverList(strategy);

    foreach (var resolverType in resolvers)
    {
        services.TryAddScoped(resolverType);
    }

    services.TryAddScoped<ITenantResolver>(sp =>
    {
        var resolverInstances = resolvers
            .Select(t => (ITenantResolver)sp.GetRequiredService(t))
            .ToArray();
        return new CompositeTenantResolver(
            resolverInstances,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CompositeTenantResolver>>());
    });

    return services;
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build framework/src/CrestCreates.MultiTenancy`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.MultiTenancy/MultiTenancyExtensions.cs
git commit -m "refactor(multitenancy): remove BuildServiceProvider() calls from DI registration, use IOptions delayed resolution"
```

---

### Task 6: ITenantDbContextFactory — Remove Activator.CreateInstance

**Files:**
- Create: `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/ITenantDbContextFactory.cs`
- Create: `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/DefaultTenantDbContextFactory.cs`
- Modify: `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/TenantConnectionStringResolver.cs`

- [ ] **Step 1: Create ITenantDbContextFactory interface**

Create `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/ITenantDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
{
    public interface ITenantDbContextFactory
    {
        TDbContext Create<TDbContext>(DbContextOptions<TDbContext> options) where TDbContext : DbContext;
    }
}
```

- [ ] **Step 2: Create DefaultTenantDbContextFactory**

Create `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/DefaultTenantDbContextFactory.cs`:

```csharp
using System;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
{
    public class DefaultTenantDbContextFactory : ITenantDbContextFactory
    {
        public TDbContext Create<TDbContext>(DbContextOptions<TDbContext> options) where TDbContext : DbContext
        {
            return (TDbContext)Activator.CreateInstance(typeof(TDbContext), options)!;
        }
    }
}
```

- [ ] **Step 3: Refactor TenantDbContextFactory to use ITenantDbContextFactory**

Replace `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/TenantConnectionStringResolver.cs` entirely:

```csharp
using System;
using Microsoft.EntityFrameworkCore;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
{
    public class TenantConnectionStringResolver : ITenantConnectionStringResolver
    {
        private readonly ICurrentTenant _currentTenant;

        public TenantConnectionStringResolver(ICurrentTenant currentTenant)
        {
            _currentTenant = currentTenant;
        }

        public string Resolve()
        {
            if (_currentTenant?.Tenant == null)
            {
                throw new InvalidOperationException("No tenant is available in the current context.");
            }

            return _currentTenant.Tenant.ConnectionString
                ?? throw new InvalidOperationException($"Tenant '{_currentTenant.Tenant.Name}' has no connection string configured.");
        }
    }

    public interface ITenantConnectionStringResolver
    {
        string Resolve();
    }

    public class TenantDbContextFactory<TDbContext> : IDbContextFactory<TDbContext>
        where TDbContext : DbContext
    {
        private readonly ITenantConnectionStringResolver _connectionStringResolver;
        private readonly ITenantDbContextFactory _dbContextFactory;

        public TenantDbContextFactory(
            ITenantConnectionStringResolver connectionStringResolver,
            ITenantDbContextFactory dbContextFactory)
        {
            _connectionStringResolver = connectionStringResolver;
            _dbContextFactory = dbContextFactory;
        }

        public TDbContext CreateDbContext()
        {
            var connectionString = _connectionStringResolver.Resolve();

            var optionsBuilder = new DbContextOptionsBuilder<TDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            return _dbContextFactory.Create<TDbContext>(optionsBuilder.Options);
        }
    }
}
```

- [ ] **Step 4: Register DefaultTenantDbContextFactory in DI**

Add registration in the EFCore module's DI extension (find the appropriate service collection extension in `framework/src/CrestCreates.OrmProviders.EFCore/` and add):

```csharp
services.TryAddSingleton<ITenantDbContextFactory, DefaultTenantDbContextFactory>();
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build framework/src/CrestCreates.OrmProviders.EFCore`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/ITenantDbContextFactory.cs framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/DefaultTenantDbContextFactory.cs framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/TenantConnectionStringResolver.cs
git commit -m "refactor(multitenancy): introduce ITenantDbContextFactory to decouple from Activator.CreateInstance for AoT compatibility"
```

---

### Task 7: TenantFilter Source Generator — Remove MakeGenericMethod

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/TenantFilterGenerator/TenantFilterSourceGenerator.cs`
- Modify: `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/MultiTenancyDiscriminator.cs`

- [ ] **Step 1: Create TenantFilterSourceGenerator**

Create `framework/tools/CrestCreates.CodeGenerator/TenantFilterGenerator/TenantFilterSourceGenerator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.TenantFilterGenerator
{
    [Generator]
    public class TenantFilterSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var multiTenantEntities = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsMultiTenantCandidate(node),
                    transform: static (ctx, _) => GetMultiTenantClass(ctx))
                .Where(static x => x is not null)
                .Collect();

            context.RegisterSourceOutput(multiTenantEntities, ExecuteGeneration);
        }

        private static bool IsMultiTenantCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDeclaration && classDeclaration.AttributeLists.Count > 0;
        }

        private static INamedTypeSymbol? GetMultiTenantClass(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol == null) return null;

            // Must have [Entity] attribute
            bool hasEntityAttr = symbol.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == "EntityAttribute" || attr.AttributeClass?.Name == "Entity");

            if (!hasEntityAttr) return null;

            // Must implement IMultiTenant
            bool implementsMultiTenant = ImplementsInterface(symbol, "IMultiTenant");

            if (!implementsMultiTenant) return null;

            return symbol;
        }

        private static bool ImplementsInterface(INamedTypeSymbol symbol, string interfaceName)
        {
            foreach (var iface in symbol.AllInterfaces)
            {
                if (iface.Name == interfaceName)
                    return true;
            }
            return false;
        }

        private void ExecuteGeneration(SourceProductionContext context, ImmutableArray<INamedTypeSymbol?> entities)
        {
            if (entities.IsDefaultOrEmpty) return;

            var processed = new HashSet<string>();
            var entityList = new List<INamedTypeSymbol>();

            foreach (var entity in entities)
            {
                if (entity == null) continue;
                var fullName = entity.ToDisplayString();
                if (processed.Contains(fullName)) continue;
                processed.Add(fullName);
                entityList.Add(entity);
            }

            if (entityList.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using Microsoft.EntityFrameworkCore;");
            sb.AppendLine("using CrestCreates.MultiTenancy.Abstract;");
            sb.AppendLine();
            sb.AppendLine("namespace CrestCreates.OrmProviders.EFCore.MultiTenancy");
            sb.AppendLine("{");
            sb.AppendLine("    public static partial class TenantFilterConfiguration");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void ApplyAll(ModelBuilder modelBuilder, ICurrentTenant currentTenant)");
            sb.AppendLine("        {");

            foreach (var entity in entityList)
            {
                var entityName = entity.Name;
                var entityNamespace = entity.ContainingNamespace.ToDisplayString();
                sb.AppendLine($"            Configure{entityName}Filter(modelBuilder, currentTenant);");
            }

            sb.AppendLine("        }");
            sb.AppendLine();

            foreach (var entity in entityList)
            {
                var entityName = entity.Name;
                var entityFullName = entity.ToDisplayString();
                sb.AppendLine($"        private static void Configure{entityName}Filter(ModelBuilder modelBuilder, ICurrentTenant currentTenant)");
                sb.AppendLine("        {");
                sb.AppendLine($"            modelBuilder.Entity<{entityFullName}>().HasQueryFilter(e =>");
                sb.AppendLine("                currentTenant.Id == null || e.TenantId == currentTenant.Id);");
                sb.AppendLine($"            modelBuilder.Entity<{entityFullName}>().HasIndex(e => e.TenantId)");
                sb.AppendLine($"                .HasDatabaseName(\"IX_{entityName}_TenantId\");");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            context.AddSource("TenantFilter.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }
}
```

- [ ] **Step 2: Refactor MultiTenancyDiscriminatorExtensions to use generated code**

Replace `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/MultiTenancyDiscriminator.cs` entirely:

```csharp
using System;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
{
    public static class MultiTenancyDiscriminatorExtensions
    {
        public static void ConfigureTenantDiscriminator(
            this ModelBuilder modelBuilder,
            ICurrentTenant currentTenant,
            string tenantIdPropertyName = "TenantId")
        {
            if (modelBuilder == null) throw new ArgumentNullException(nameof(modelBuilder));
            if (currentTenant == null) throw new ArgumentNullException(nameof(currentTenant));

            TenantFilterConfiguration.ApplyAll(modelBuilder, currentTenant);
        }

        public static void SetTenantId<TEntity>(
            this TEntity entity,
            ICurrentTenant currentTenant)
            where TEntity : class, IMultiTenant
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (currentTenant == null) throw new ArgumentNullException(nameof(currentTenant));

            if (string.IsNullOrEmpty(entity.TenantId))
            {
                entity.TenantId = currentTenant.Id;
            }
        }
    }

    public interface IMultiTenant
    {
        string TenantId { get; set; }
    }

    public abstract class MultiTenantEntity : IMultiTenant
    {
        public virtual string TenantId { get; set; } = string.Empty;
    }

    public abstract class MultiTenantEntity<TKey> : MultiTenantEntity
    {
        public virtual TKey Id { get; set; }
    }
}
```

Note: `TenantFilterConfiguration` is a `partial class` — the Source Generator provides the other part. If no `[Entity]` + `IMultiTenant` classes exist in the project, the generator produces nothing and compilation will fail with "TenantFilterConfiguration not found". To handle this, add a fallback partial class in the same file:

```csharp
// Fallback when no IMultiTenant entities exist — Source Generator overrides this
public static partial class TenantFilterConfiguration
{
    public static void ApplyAll(ModelBuilder modelBuilder, ICurrentTenant currentTenant)
    {
        // No IMultiTenant entities found — nothing to configure
    }
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator && dotnet build framework/src/CrestCreates.OrmProviders.EFCore`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/TenantFilterGenerator/TenantFilterSourceGenerator.cs framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/MultiTenancyDiscriminator.cs
git commit -m "feat(multitenancy): add TenantFilterSourceGenerator to replace runtime reflection with compile-time HasQueryFilter generation"
```

---

### Task 8: TenantDbContextFactory Source Generator

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/TenantDbContextFactoryGenerator/TenantDbContextFactorySourceGenerator.cs`

- [ ] **Step 1: Create TenantDbContextFactorySourceGenerator**

Create `framework/tools/CrestCreates.CodeGenerator/TenantDbContextFactoryGenerator/TenantDbContextFactorySourceGenerator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.TenantDbContextFactoryGenerator
{
    [Generator]
    public class TenantDbContextFactorySourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var dbContextClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsDbContextCandidate(node),
                    transform: static (ctx, _) => GetDbContextClass(ctx))
                .Where(static x => x is not null)
                .Collect();

            context.RegisterSourceOutput(dbContextClasses, ExecuteGeneration);
        }

        private static bool IsDbContextCandidate(SyntaxNode node)
        {
            if (node is ClassDeclarationSyntax classDecl)
            {
                foreach (var baseList in classDecl.BaseList?.Types ?? default)
                {
                    var type = baseList.Type.ToString();
                    if (type.Contains("DbContext"))
                        return true;
                }
            }
            return false;
        }

        private static INamedTypeSymbol? GetDbContextClass(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol == null) return null;

            // Must inherit from DbContext (directly or indirectly)
            var baseType = symbol.BaseType;
            while (baseType != null)
            {
                if (baseType.Name == "DbContext" && baseType.ContainingNamespace.ToDisplayString().StartsWith("Microsoft.EntityFrameworkCore"))
                    return symbol;
                baseType = baseType.BaseType;
            }

            return null;
        }

        private void ExecuteGeneration(SourceProductionContext context, ImmutableArray<INamedTypeSymbol?> dbContexts)
        {
            if (dbContexts.IsDefaultOrEmpty) return;

            var processed = new HashSet<string>();
            var dbContextList = new List<INamedTypeSymbol>();

            foreach (var dbContext in dbContexts)
            {
                if (dbContext == null) continue;
                var fullName = dbContext.ToDisplayString();
                if (processed.Contains(fullName)) continue;
                processed.Add(fullName);
                dbContextList.Add(dbContext);
            }

            if (dbContextList.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using System;");
            sb.AppendLine("using Microsoft.EntityFrameworkCore;");
            sb.AppendLine("using CrestCreates.OrmProviders.EFCore.MultiTenancy;");
            sb.AppendLine();
            sb.AppendLine("namespace CrestCreates.OrmProviders.EFCore.MultiTenancy");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// AoT-friendly tenant DbContext factory. Generated by TenantDbContextFactorySourceGenerator.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public sealed class GeneratedTenantDbContextFactory : ITenantDbContextFactory");
            sb.AppendLine("    {");

            foreach (var dbContext in dbContextList)
            {
                var fullName = dbContext.ToDisplayString();
                sb.AppendLine($"        private static TDbContext Create{dbContext.Name}(DbContextOptions<TDbContext> options)");
                sb.AppendLine($"            => (TDbContext)(object)new {fullName}(options);");
                sb.AppendLine();
            }

            sb.AppendLine("        public TDbContext Create<TDbContext>(DbContextOptions<TDbContext> options) where TDbContext : DbContext");
            sb.AppendLine("        {");

            for (int i = 0; i < dbContextList.Count; i++)
            {
                var dbContext = dbContextList[i];
                var fullName = dbContext.ToDisplayString();
                var prefix = i == 0 ? "if" : "else if";
                sb.AppendLine($"            {prefix} (typeof(TDbContext) == typeof({fullName}))");
                sb.AppendLine($"                return Create{dbContext.Name}(options);");
            }

            sb.AppendLine("            throw new InvalidOperationException($\"No factory registered for {{typeof(TDbContext).Name}}. Register an ITenantDbContextFactory or ensure the DbContext is discoverable by the source generator.\");");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            context.AddSource("TenantDbContextFactory.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/TenantDbContextFactoryGenerator/TenantDbContextFactorySourceGenerator.cs
git commit -m "feat(codegen): add TenantDbContextFactorySourceGenerator for AoT-friendly DbContext creation"
```

---

### Task 9: Resolver Unit Tests

**Files:**
- Create: `framework/test/CrestCreates.Application.Tests/Tenants/QueryStringTenantResolverTests.cs`
- Create: `framework/test/CrestCreates.Application.Tests/Tenants/CookieTenantResolverTests.cs`
- Create: `framework/test/CrestCreates.Application.Tests/Tenants/RouteTenantResolverTests.cs`

- [ ] **Step 1: Create QueryStringTenantResolverTests**

Create `framework/test/CrestCreates.Application.Tests/Tenants/QueryStringTenantResolverTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class QueryStringTenantResolverTests
{
    private readonly Mock<ITenantRepository> _tenantRepository;
    private readonly Mock<ILogger<QueryStringTenantResolver>> _logger;
    private readonly TenantIdentifierNormalizer _normalizer;
    private readonly IOptions<MultiTenancyOptions> _options;

    public QueryStringTenantResolverTests()
    {
        _tenantRepository = new Mock<ITenantRepository>();
        _logger = new Mock<ILogger<QueryStringTenantResolver>>();
        _normalizer = new TenantIdentifierNormalizer();
        _options = Options.Create(new MultiTenancyOptions());
    }

    [Fact]
    public async Task ResolveAsync_WithValidTenantInQueryString_ReturnsSuccess()
    {
        var tenant = new Tenant(Guid.NewGuid(), "ACME") { IsActive = true };
        _tenantRepository.Setup(r => r.FindByNameAsync("ACME", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var resolver = new QueryStringTenantResolver(_options, _tenantRepository.Object, _normalizer, _logger.Object);
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tenantId=ACME");

        var result = await resolver.ResolveAsync(context);

        Assert.True(result.IsResolved);
        Assert.Equal("ACME", result.TenantName);
    }

    [Fact]
    public async Task ResolveAsync_WithoutTenantInQueryString_ReturnsNotResolved()
    {
        var resolver = new QueryStringTenantResolver(_options, _tenantRepository.Object, _normalizer, _logger.Object);
        var context = new DefaultHttpContext();

        var result = await resolver.ResolveAsync(context);

        Assert.False(result.IsResolved);
    }

    [Fact]
    public async Task ResolveAsync_WithUnknownTenant_ReturnsNotFound()
    {
        _tenantRepository.Setup(r => r.FindByNameAsync("UNKNOWN", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var resolver = new QueryStringTenantResolver(_options, _tenantRepository.Object, _normalizer, _logger.Object);
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tenantId=UNKNOWN");

        var result = await resolver.ResolveAsync(context);

        Assert.False(result.IsResolved);
        Assert.Equal("TENANT_NOT_FOUND", result.Error?.Code);
    }
}
```

- [ ] **Step 2: Create CookieTenantResolverTests**

Create `framework/test/CrestCreates.Application.Tests/Tenants/CookieTenantResolverTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class CookieTenantResolverTests
{
    private readonly Mock<ITenantRepository> _tenantRepository;
    private readonly Mock<ILogger<CookieTenantResolver>> _logger;
    private readonly TenantIdentifierNormalizer _normalizer;
    private readonly IOptions<MultiTenancyOptions> _options;

    public CookieTenantResolverTests()
    {
        _tenantRepository = new Mock<ITenantRepository>();
        _logger = new Mock<ILogger<CookieTenantResolver>>();
        _normalizer = new TenantIdentifierNormalizer();
        _options = Options.Create(new MultiTenancyOptions());
    }

    [Fact]
    public async Task ResolveAsync_WithValidTenantInCookie_ReturnsSuccess()
    {
        var tenant = new Tenant(Guid.NewGuid(), "ACME") { IsActive = true };
        _tenantRepository.Setup(r => r.FindByNameAsync("ACME", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var resolver = new CookieTenantResolver(_options, _tenantRepository.Object, _normalizer, _logger.Object);
        var context = new DefaultHttpContext();
        context.Request.Headers["Cookie"] = "TenantId=ACME";

        var result = await resolver.ResolveAsync(context);

        Assert.True(result.IsResolved);
        Assert.Equal("ACME", result.TenantName);
    }

    [Fact]
    public async Task ResolveAsync_WithoutTenantCookie_ReturnsNotResolved()
    {
        var resolver = new CookieTenantResolver(_options, _tenantRepository.Object, _normalizer, _logger.Object);
        var context = new DefaultHttpContext();

        var result = await resolver.ResolveAsync(context);

        Assert.False(result.IsResolved);
    }
}
```

- [ ] **Step 3: Create RouteTenantResolverTests**

Create `framework/test/CrestCreates.Application.Tests/Tenants/RouteTenantResolverTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class RouteTenantResolverTests
{
    private readonly Mock<ITenantRepository> _tenantRepository;
    private readonly Mock<ILogger<RouteTenantResolver>> _logger;
    private readonly TenantIdentifierNormalizer _normalizer;
    private readonly IOptions<MultiTenancyOptions> _options;

    public RouteTenantResolverTests()
    {
        _tenantRepository = new Mock<ITenantRepository>();
        _logger = new Mock<ILogger<RouteTenantResolver>>();
        _normalizer = new TenantIdentifierNormalizer();
        _options = Options.Create(new MultiTenancyOptions());
    }

    [Fact]
    public async Task ResolveAsync_WithValidTenantInRoute_ReturnsSuccess()
    {
        var tenant = new Tenant(Guid.NewGuid(), "ACME") { IsActive = true };
        _tenantRepository.Setup(r => r.FindByNameAsync("ACME", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var resolver = new RouteTenantResolver(_options, _tenantRepository.Object, _normalizer, _logger.Object);
        var context = new DefaultHttpContext();
        context.SetEndpoint(new RoutingEndpoint(new RouteValueDictionary { ["tenantId"] = "ACME" }));

        var result = await resolver.ResolveAsync(context);

        Assert.True(result.IsResolved);
        Assert.Equal("ACME", result.TenantName);
    }

    [Fact]
    public async Task ResolveAsync_WithoutTenantRouteValue_ReturnsNotResolved()
    {
        var resolver = new RouteTenantResolver(_options, _tenantRepository.Object, _normalizer, _logger.Object);
        var context = new DefaultHttpContext();

        var result = await resolver.ResolveAsync(context);

        Assert.False(result.IsResolved);
    }
}
```

Note: The `RouteTenantResolverTests` uses `RoutingEndpoint` helper to set route data on `HttpContext`. If `RoutingEndpoint` is not available, use `httpContext.Features.Set<IRoutingFeature>(new RoutingFeature { RouteData = ... })`.

- [ ] **Step 4: Build and verify tests compile**

Run: `dotnet build framework/test/CrestCreates.Application.Tests`
Expected: PASS (may need adjustments for cookie/route test setup)

- [ ] **Step 5: Run tests**

Run: `dotnet test framework/test/CrestCreates.Application.Tests --filter "FullyQualifiedName~QueryStringTenantResolverTests|FullyQualifiedName~CookieTenantResolverTests|FullyQualifiedName~RouteTenantResolverTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add framework/test/CrestCreates.Application.Tests/Tenants/QueryStringTenantResolverTests.cs framework/test/CrestCreates.Application.Tests/Tenants/CookieTenantResolverTests.cs framework/test/CrestCreates.Application.Tests/Tenants/RouteTenantResolverTests.cs
git commit -m "test(multitenancy): add unit tests for QueryString, Cookie, and Route tenant resolvers"
```

---

### Task 10: TestContainers PostgreSQL for Integration Tests

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `framework/test/CrestCreates.IntegrationTests/CrestCreates.IntegrationTests.csproj`
- Modify: `framework/test/CrestCreates.TestBase/CrestCreates.TestBase.csproj`
- Modify: `framework/test/CrestCreates.IntegrationTests/WebApplicationFactory.cs`

- [ ] **Step 1: Add TestContainers packages to Directory.Packages.props**

Add to the `<ItemGroup>` in `Directory.Packages.props`:

```xml
<PackageVersion Include="Testcontainers" Version="4.11.0" />
<PackageVersion Include="Testcontainers.PostgreSql" Version="4.11.0" />
```

- [ ] **Step 2: Add package references to integration test project**

Add to `framework/test/CrestCreates.IntegrationTests/CrestCreates.IntegrationTests.csproj`:

```xml
<PackageReference Include="Testcontainers" />
<PackageReference Include="Testcontainers.PostgreSql" />
```

- [ ] **Step 3: Add package references to test base project**

Add to `framework/test/CrestCreates.TestBase/CrestCreates.TestBase.csproj`:

```xml
<PackageReference Include="Testcontainers" />
<PackageReference Include="Testcontainers.PostgreSql" />
```

- [ ] **Step 4: Refactor WebApplicationFactory to use TestContainers**

Replace `framework/test/CrestCreates.IntegrationTests/WebApplicationFactory.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Services;
using CrestCreates.AuditLogging.Options;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Repositories;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Repositories;
using Microsoft.EntityFrameworkCore;
using LibraryManagement.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CrestCreates.IntegrationTests;

public sealed class LibraryManagementWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("crestcreates_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly string _schemaName = $"itest_{Guid.NewGuid():N}";
    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private bool _seedCompleted;

    private string _baseConnectionString = null!;
    private NpgsqlConnection? _sharedConnection;

    public string ConnectionString => $"{_baseConnectionString};Search Path={_schemaName}";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _baseConnectionString = _postgres.GetConnectionString();
        EnsureSchemaCreated();
        _sharedConnection = new NpgsqlConnection(ConnectionString);
        await _sharedConnection.OpenAsync();
    }

    private void EnsureSchemaCreated()
    {
        using var connection = new NpgsqlConnection(_baseConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{_schemaName}";""";
        command.ExecuteNonQuery();
    }

    private static async Task EnsureOpenIddictSchemaAsync(OpenIddictDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE IF EXISTS "OpenIddictTokens" CASCADE;
            DROP TABLE IF EXISTS "OpenIddictAuthorizations" CASCADE;
            DROP TABLE IF EXISTS "OpenIddictScopes" CASCADE;
            DROP TABLE IF EXISTS "OpenIddictApplications" CASCADE;
            """;
        await command.ExecuteNonQueryAsync();

        var databaseCreator = dbContext.Database.GetService<IRelationalDatabaseCreator>();
        await databaseCreator.CreateTablesAsync();
    }

    public async Task EnsureSeedCompleteAsync()
    {
        if (_seedCompleted)
        {
            return;
        }

        await _seedLock.WaitAsync();
        try
        {
            if (_seedCompleted)
            {
                return;
            }

            var scopeFactory = Services.GetRequiredService<IServiceScopeFactory>();
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var openIddictDbContext = scope.ServiceProvider.GetRequiredService<OpenIddictDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            await dbContext.Database.EnsureCreatedAsync();
            await EnsureOpenIddictSchemaAsync(openIddictDbContext);

            await EnsureOpenIddictClientAsync(applicationManager);

            var tenantA = dbContext.Tenants.FirstOrDefault(t => t.Name == "tenant-a");
            if (tenantA == null)
            {
                tenantA = new Tenant(Guid.NewGuid(), "tenant-a")
                {
                    DisplayName = "Tenant A",
                    IsActive = true,
                    LifecycleState = TenantLifecycleState.Active,
                    CreationTime = DateTime.UtcNow
                };
                dbContext.Tenants.Add(tenantA);
                await dbContext.SaveChangesAsync();
            }

            var tenantARole = dbContext.Roles.FirstOrDefault(r => r.Name == "Administrators" && r.TenantId == tenantA.Id.ToString());
            if (tenantARole == null)
            {
                tenantARole = new Role(Guid.NewGuid(), "Administrators", tenantA.Id.ToString())
                {
                    DisplayName = "Administrators",
                    IsActive = true,
                    CreationTime = DateTime.UtcNow
                };
                dbContext.Roles.Add(tenantARole);
                await dbContext.SaveChangesAsync();
            }

            var tenantAUser = dbContext.Users.FirstOrDefault(u => u.UserName == "admin" && u.TenantId == tenantA.Id.ToString());
            if (tenantAUser == null)
            {
                tenantAUser = new User(Guid.NewGuid(), "admin", "admin@tenant-a.local", tenantA.Id.ToString())
                {
                    PasswordHash = passwordHasher.HashPassword("Admin123!"),
                    IsActive = true,
                    IsSuperAdmin = true,
                    LockoutEnabled = true,
                    CreationTime = DateTime.UtcNow,
                    LastPasswordChangeTime = DateTime.UtcNow
                };
                dbContext.Users.Add(tenantAUser);
                await dbContext.SaveChangesAsync();
            }

            await dbContext.SaveChangesAsync();

            if (!await dbContext.AuditLogs.AnyAsync())
            {
                var now = DateTime.UtcNow;
                using var insertCmd = _sharedConnection!.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO ""AuditLogs"" (""Id"", ""Duration"", ""UserId"", ""UserName"", ""TenantId"", ""ClientIpAddress"", ""HttpMethod"", ""Url"", ""ServiceName"", ""MethodName"", ""Parameters"", ""ReturnValue"", ""ExceptionMessage"", ""ExceptionStackTrace"", ""Status"", ""ExecutionTime"", ""CreationTime"", ""TraceId"", ""ExtraProperties"")
                    VALUES
                    (@id1, 150, 'host-user-1', 'host-alice', 'host', '192.168.1.1', 'POST', 'https://localhost/api/books', 'BookAppService', 'CreateAsync', NULL, NULL, NULL, NULL, 0, @time1, @now, NULL, '{}'),
                    (@id2, 80, 'host-user-2', 'host-bob', 'host', '192.168.1.2', 'GET', 'https://localhost/api/books', 'BookAppService', 'GetListAsync', NULL, NULL, NULL, NULL, 0, @time2, @now, NULL, '{}'),
                    (@id3, 200, 'tenant-a-user-1', 'tenant-a-charlie', 'tenant-a', '10.0.0.1', 'POST', 'https://localhost/api/authors', 'AuthorAppService', 'CreateAsync', NULL, NULL, NULL, NULL, 0, @time3, @now, NULL, '{}'),
                    (@id4, 60, 'tenant-a-user-2', 'tenant-a-david', 'tenant-a', '10.0.0.2', 'GET', 'https://localhost/api/authors', 'AuthorAppService', 'GetListAsync', NULL, NULL, NULL, NULL, 1, @time4, @now, NULL, '{}')";

                insertCmd.Parameters.AddWithValue("@id1", Guid.NewGuid());
                insertCmd.Parameters.AddWithValue("@id2", Guid.NewGuid());
                insertCmd.Parameters.AddWithValue("@id3", Guid.NewGuid());
                insertCmd.Parameters.AddWithValue("@id4", Guid.NewGuid());
                insertCmd.Parameters.AddWithValue("@time1", now.AddMinutes(-30));
                insertCmd.Parameters.AddWithValue("@time2", now.AddMinutes(-20));
                insertCmd.Parameters.AddWithValue("@time3", now.AddMinutes(-25));
                insertCmd.Parameters.AddWithValue("@time4", now.AddMinutes(-15));
                insertCmd.Parameters.AddWithValue("@now", now);

                await insertCmd.ExecuteNonQueryAsync();
            }

            _seedCompleted = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    private static async Task EnsureOpenIddictClientAsync(IOpenIddictApplicationManager applicationManager)
    {
        if (await applicationManager.FindByClientIdAsync("test-client") is not null)
        {
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = "test-client",
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Test Client"
        };

        descriptor.Permissions.UnionWith(new[]
        {
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.Password,
            Permissions.GrantTypes.RefreshToken,
            Permissions.Prefixes.Scope + Scopes.OpenId,
            Permissions.Prefixes.Scope + Scopes.Profile,
            Permissions.Prefixes.Scope + Scopes.Email,
            Permissions.Prefixes.Scope + Scopes.OfflineAccess
        });

        await applicationManager.CreateAsync(descriptor);
    }

    public new HttpClient CreateClient()
    {
        EnsureSeedCompleteAsync().GetAwaiter().GetResult();
        return base.CreateClient();
    }

    public new HttpClient CreateClient(WebApplicationFactoryClientOptions options)
    {
        EnsureSeedCompleteAsync().GetAwaiter().GetResult();
        return base.CreateClient(options);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["SeedIdentity:TenantId"] = "host",
                ["SeedIdentity:RoleName"] = "Administrators",
                ["SeedIdentity:UserName"] = "admin",
                ["SeedIdentity:Email"] = "admin@library.local",
                ["SeedIdentity:Password"] = "Admin123!",
                ["CrestLogging:EnableFile"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LibraryDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<LibraryDbContext>>();
            services.RemoveAll<LibraryDbContext>();
            services.RemoveAll<DbContextOptions<CrestCreatesDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<CrestCreatesDbContext>>();
            services.RemoveAll<CrestCreatesDbContext>();
            services.RemoveAll<CrestCreatesDbContextFactory>();
            services.RemoveAll<DbContextOptions<OpenIddictDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<OpenIddictDbContext>>();
            services.RemoveAll<OpenIddictDbContext>();
            services.RemoveAll<DbContext>();
            services.RemoveAll<AuditLoggingMiddleware>();
            services.RemoveAll<IAuditLogService>();

            services.AddDbContext<LibraryDbContext>(options =>
            {
                options.UseNpgsql(_sharedConnection!);
            });

            services.AddScoped<DbContext>(sp => sp.GetRequiredService<LibraryDbContext>());

            services.RemoveAll<IEntityFrameworkCoreDbContext>();
            services.RemoveAll<IDataBaseContext>();
            services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
                new EfCoreDbContextAdapter(sp.GetRequiredService<LibraryDbContext>()));
            services.AddScoped<IDataBaseContext>(sp =>
                sp.GetRequiredService<IEntityFrameworkCoreDbContext>());

            services.RemoveAll<IAuditLogRepository>();
            services.AddScoped<IAuditLogRepository>(sp =>
                new AuditLogRepository(
                    sp.GetRequiredService<IEntityFrameworkCoreDbContext>(),
                    sp.GetRequiredService<ICurrentTenant>()));

            services.AddScoped<AuditLoggingMiddleware>();
            services.AddScoped<IAuditLogRedactor, AuditLogRedactor>();
            services.AddScoped<IAuditLogWriter, AuditLogWriter>();
            services.AddScoped<IAuditLogService, AuditLogService>();
            services.AddScoped<IAuditLogAppService, AuditLogAppService>();
            services.AddScoped<IAuditLogCleanupAppService, AuditLogCleanupAppService>();

            services.AddDbContext<OpenIddictDbContext>(options =>
            {
                options.UseNpgsql(_sharedConnection!);
            });
        });
    }

    public new async Task DisposeAsync()
    {
        _sharedConnection?.Dispose();
        _seedLock.Dispose();
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build framework/test/CrestCreates.IntegrationTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props framework/test/CrestCreates.IntegrationTests/CrestCreates.IntegrationTests.csproj framework/test/CrestCreates.TestBase/CrestCreates.TestBase.csproj framework/test/CrestCreates.IntegrationTests/WebApplicationFactory.cs
git commit -m "refactor(testing): replace hardcoded PostgreSQL with TestContainers for integration tests"
```

---

### Task 11: Full Build Verification

- [ ] **Step 1: Build entire solution**

Run: `dotnet build CrestCreates.slnx`
Expected: PASS (0 errors)

- [ ] **Step 2: Run unit tests**

Run: `dotnet test framework/test/CrestCreates.Application.Tests --filter "FullyQualifiedName~Tenants"`
Expected: PASS

- [ ] **Step 3: Run integration tests (requires Docker)**

Run: `dotnet test framework/test/CrestCreates.IntegrationTests`
Expected: PASS

- [ ] **Step 4: Commit any remaining fixes**

```bash
git add -A
git commit -m "fix(multitenancy): final build fixes for multi-tenancy AOT and feature completeness"
```
