# Multi-Tenancy Module: AOT Compatibility & Feature Completeness Fix

Date: 2026-04-29

## Problem Statement

The multi-tenancy module has two categories of issues:

1. **AOT blockers** — Two reflection-based code paths prevent AoT compilation
2. **Feature gaps** — Incomplete implementations and disabled features

## Fix List

| # | Problem | Severity | Fix |
|---|---------|----------|-----|
| 1 | `Activator.CreateInstance` in `TenantDbContextFactory` | AoT blocker | `ITenantDbContextFactory` + Source Generator |
| 2 | `MakeGenericMethod` + `Invoke` in Discriminator | AoT blocker | Source Generator for `HasQueryFilter` |
| 3 | `TenantIsolationStrategy.Schema` enum with no implementation | Feature gap | Remove enum value |
| 4 | QueryString/Cookie/Route Resolvers commented out | Feature gap | Enable DI registration + add tests |
| 5 | `ITenantInfo` duplicated across `Domain.Shared` and `MultiTenancy.Abstract` | Consistency | Unify to `MultiTenancy.Abstract` |
| 6 | `GetAwaiter().GetResult()` in `CurrentTenant.Change` | Runtime hazard | `ChangeAsync` (remove sync `Change`) |
| 7 | `BuildServiceProvider()` in `MultiTenancyExtensions` | Potential AoT risk | Replace with direct parameter / delayed resolution |
| 8 | Integration tests use hardcoded PostgreSQL connection | Test infra | Introduce TestContainers PostgreSQL |

## Design Details

### Fix 1: TenantDbContextFactory — Remove Activator.CreateInstance

**Current** (`TenantConnectionStringResolver.cs:56`):
```csharp
return (TDbContext)Activator.CreateInstance(typeof(TDbContext), optionsBuilder.Options);
```

**New**: Introduce `ITenantDbContextFactory` interface:

```csharp
public interface ITenantDbContextFactory
{
    TDbContext Create<TDbContext>(DbContextOptions<TDbContext> options) where TDbContext : DbContext;
}
```

- `DefaultTenantDbContextFactory` — keeps `Activator.CreateInstance` for non-AoT scenarios, registered by default
- `TenantDbContextFactoryGenerator` (new Source Generator) — scans all `DbContext` subclasses, generates a compile-time factory:

```csharp
// Generated: TenantDbContextFactory.g.cs
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public TDbContext Create<TDbContext>(DbContextOptions<TDbContext> options) where TDbContext : DbContext
{
    if (typeof(TDbContext) == typeof(LibraryDbContext))
        return (TDbContext)(object)new LibraryDbContext(options);
    throw new InvalidOperationException($"No factory for {typeof(TDbContext).Name}");
}
```

- `TenantDbContextFactory<TDbContext>` refactored to depend on `ITenantDbContextFactory` instead of calling `Activator.CreateInstance` directly
- AoT projects register the generated factory; non-AoT projects use the default

### Fix 2: Discriminator HasQueryFilter — Remove MakeGenericMethod

**Current** (`MultiTenancyDiscriminator.cs:33-38`):
```csharp
var method = typeof(MultiTenancyDiscriminatorExtensions)
    .GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Static)
    ?.MakeGenericMethod(entityType.ClrType);
method?.Invoke(null, new object[] { modelBuilder, currentTenant, tenantIdPropertyName });
```

**New**: Add `TenantFilterGenerator` to `CrestCreates.CodeGenerator`.

Scans all `[Entity]` classes that implement `IMultiTenant`, generates:

```csharp
// Generated: TenantFilter.g.cs
public static partial class TenantFilterConfiguration
{
    public static void ApplyAll(ModelBuilder modelBuilder, ICurrentTenant currentTenant)
    {
        ConfigureBookFilter(modelBuilder, currentTenant);
        ConfigureLoanFilter(modelBuilder, currentTenant);
    }

    private static void ConfigureBookFilter(ModelBuilder modelBuilder, ICurrentTenant currentTenant)
    {
        modelBuilder.Entity<Book>().HasQueryFilter(e =>
            currentTenant.Id == null || e.TenantId == currentTenant.Id);
        modelBuilder.Entity<Book>().HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_Book_TenantId");
    }
}
```

Runtime `ConfigureTenantDiscriminator` becomes:
```csharp
public static void ConfigureTenantDiscriminator(this ModelBuilder modelBuilder, ICurrentTenant currentTenant)
{
    TenantFilterConfiguration.ApplyAll(modelBuilder, currentTenant);
}
```

The old reflection-based method is removed entirely (no `[Obsolete]` transition — clean break per single main chain principle).

### Fix 3: Remove Schema Isolation Enum Value

Remove `Schema` from `TenantIsolationStrategy` enum. Search all references and clean up. The enum becomes:

```csharp
public enum TenantIsolationStrategy
{
    Database,
    Discriminator
}
```

### Fix 4: Enable Commented-Out Resolvers

Uncomment `MultiTenancyExtensions.cs:111-125` to enable DI registration for `QueryStringTenantResolver`, `CookieTenantResolver`, `RouteTenantResolver`.

Add unit tests:
- `QueryStringTenantResolverTests.cs` — resolve from query string, missing param, invalid tenant
- `CookieTenantResolverTests.cs` — resolve from cookie, missing cookie, invalid tenant
- `RouteTenantResolverTests.cs` — resolve from route data, missing route value, invalid tenant

### Fix 5: Unify ITenantInfo

Delete `framework/src/CrestCreates.Domain.Shared/MultiTenancy/ITenantInfo.cs` (contains duplicate `ITenantInfo`, `TenantInfo`, `ITenantProvider`).

All references switch to `CrestCreates.MultiTenancy.Abstract` definitions. `ConnectionString` unified to `string?` (nullable).

If `Domain.Shared` cannot reference `MultiTenancy.Abstract` due to layering constraints, move the shared interfaces to a lower-level package that both can reference.

### Fix 6: ICurrentTenant.Change → ChangeAsync

**Breaking change**: Remove `IDisposable Change(string tenantId)` from `ICurrentTenant`. Replace with:

```csharp
public interface ICurrentTenant
{
    ITenantInfo? Tenant { get; }
    string? Id { get; }
    Task<IDisposable> ChangeAsync(string tenantId);
    void SetTenantId(string tenantId); // sync, for non-async contexts only
}
```

`CurrentTenant` implementation:
```csharp
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
```

All callers updated:
- `MultiTenancyMiddleware` → `await currentTenant.ChangeAsync(...)`
- `MultiTenantMoAttribute` → `await currentTenant.ChangeAsync(...)`
- Any other consumers

No `[Obsolete]` transition — clean removal of the sync method.

### Fix 7: Remove BuildServiceProvider()

**Current**: `AddInMemoryTenantProvider`, `AddMultiTenancyWithInMemory`, `AddMultiTenancyWithConfiguration` all call `services.BuildServiceProvider()` to read `IOptions<MultiTenancyOptions>`.

**New**: Accept `TenantResolutionStrategy` as a direct parameter:

```csharp
public static IServiceCollection AddMultiTenancyWithInMemory(
    this IServiceCollection services,
    Action<MultiTenancyOptions> configureOptions = null,
    Action<InMemoryTenantProvider> configureTenants = null)
{
    services.AddMultiTenancy(configureOptions);

    // Read strategy from options configuration directly, no BuildServiceProvider
    TenantResolutionStrategy strategy = TenantResolutionStrategy.Header;
    configureOptions?.Invoke(new MultiTenancyOptions { ResolutionStrategy = strategy });

    services.AddTenantResolvers(strategy);
    services.AddInMemoryTenantProvider(configureTenants);
    return services;
}
```

Alternatively, use `IConfigureOptions<T>` pattern to defer resolution. The key constraint: no `BuildServiceProvider()` calls during service registration.

### Fix 8: TestContainers PostgreSQL for Integration Tests

**Current**: `LibraryManagementWebApplicationFactory` hardcodes `Host=localhost;Database=librarymanagement_test;Username=crest;Password=crest123`.

**New**:

1. Add to `Directory.Packages.props`:
   - `Testcontainers` (latest stable)
   - `Testcontainers.PostgreSql` (latest stable)

2. Add package references to `CrestCreates.IntegrationTests.csproj` and `CrestCreates.TestBase.csproj`

3. Refactor `LibraryManagementWebApplicationFactory`:

```csharp
public sealed class LibraryManagementWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("crestcreates_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private string _baseConnectionString = null!;
    private readonly string _schemaName = $"itest_{Guid.NewGuid():N}";
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

    public new async Task DisposeAsync()
    {
        _sharedConnection?.Dispose();
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    // ... rest of factory logic unchanged (schema creation, seeding, etc.)
}
```

4. Update xUnit collection fixture pattern — `IAsyncLifetime` is natively supported by xUnit

5. Schema-per-test isolation strategy preserved (each factory instance gets its own schema within the container)

## Files Changed (Summary)

### New Files
- `framework/tools/CrestCreates.CodeGenerator/TenantFilterGenerator.cs` — Source Generator for HasQueryFilter
- `framework/tools/CrestCreates.CodeGenerator/TenantDbContextFactoryGenerator.cs` — Source Generator for DbContext factory
- `framework/test/CrestCreates.Application.Tests/Tenants/QueryStringTenantResolverTests.cs`
- `framework/test/CrestCreates.Application.Tests/Tenants/CookieTenantResolverTests.cs`
- `framework/test/CrestCreates.Application.Tests/Tenants/RouteTenantResolverTests.cs`

### Modified Files
- `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/TenantConnectionStringResolver.cs` — ITenantDbContextFactory
- `framework/src/CrestCreates.OrmProviders.EFCore/MultiTenancy/MultiTenancyDiscriminator.cs` — Remove reflection, call generated code
- `framework/src/CrestCreates.MultiTenancy/MultiTenancyOptions.cs` — Remove Schema enum value
- `framework/src/CrestCreates.MultiTenancy/MultiTenancyExtensions.cs` — Enable resolvers, remove BuildServiceProvider
- `framework/src/CrestCreates.MultiTenancy/CurrentTenant.cs` — ChangeAsync
- `framework/src/CrestCreates.MultiTenancy.Abstract/ICurrentTenant.cs` — ChangeAsync
- `framework/src/CrestCreates.MultiTenancy/Middleware/MultiTenancyMiddleware.cs` — ChangeAsync
- `framework/src/CrestCreates.MultiTenancy/Interceptors/MultiTenantMoAttribute.cs` — ChangeAsync
- `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantInfo.cs` — ConnectionString nullable
- `Directory.Packages.props` — Add Testcontainers packages
- `framework/test/CrestCreates.IntegrationTests/CrestCreates.IntegrationTests.csproj` — Add Testcontainers refs
- `framework/test/CrestCreates.IntegrationTests/WebApplicationFactory.cs` — TestContainers PostgreSQL
- `framework/test/CrestCreates.TestBase/CrestCreates.TestBase.csproj` — Add Testcontainers refs

### Deleted Files
- `framework/src/CrestCreates.Domain.Shared/MultiTenancy/ITenantInfo.cs` — Duplicate removed

## Out of Scope

- Adding Schema isolation implementation (removed per YAGNI)
- TenantAppService unit test expansion (separate concern)
- Discriminator mode integration tests (can be added after Source Generator is in place)
