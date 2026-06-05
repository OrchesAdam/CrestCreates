# Data Provider Architecture & Multi-Tenancy Refactoring — Design Spec

**Date**: 2026-06-05
**Status**: draft
**References**: [[2026-05-02-tenant-db-lifecycle-design]] (tenant init pipeline this refactoring builds on)

> This spec defines the data provider project hierarchy, multi-tenancy interface consolidation, and auditing contract separation. It supersedes the dual-layer `CrestCreates.OrmProviders.*` experiment and the scattered tenant init interfaces.

## 1. Data Provider Hierarchy

### 1.1 Project Structure

```
CrestCreates.Data.Abstractions          (exists — ORM-agnostic abstractions)
  ├── IRepository<T>, IUnitOfWork, OrmModuleBase, OrmProvider enum
  ├── IUnitOfWorkFactory, IUnitOfWorkManager, Repository<T>
  └── Depends on: Domain, DbContextProvider.Abstract, Modularity

CrestCreates.Data.Core                  (NEW — shared data infrastructure)
  ├── Base repository helpers, data filter integrations
  ├── Common data extensions shared across ALL ORM providers
  └── Depends on: Data.Abstractions, DataFilter, MultiTenancy.Abstract

CrestCreates.Data.EFCore                (MODIFIED — base, NO DB-specific provider)
  ├── CrestCreatesDbContext, MultiTenancyInterceptor, AuditInterceptor
  ├── EfCoreTenantSchemaMigrator, EfCoreTenantInitializationStore
  ├── TenantConnectionStringResolver, TenantDbContextFactory
  ├── MultiTenancyDiscriminator, TenantFilterRegistryStore
  ├── EfCoreRepository<T>, EfCoreUnitOfWork
  └── PackageRefs: Microsoft.EntityFrameworkCore (NO SqlServer, NO Sqlite)
  └── Depends on: Data.Core, Authorization.Abstractions, Application, Infrastructure

CrestCreates.Data.EFCore.SqlServer      (NEW)
  ├── SqlServerTenantDatabaseProvisioner (moved from Data.EFCore)
  ├── SqlServerConnectionFactory (moved from Data.EFCore)
  └── PackageRefs: Microsoft.EntityFrameworkCore.SqlServer
  └── Depends on: Data.EFCore

CrestCreates.Data.EFCore.MySql          (NEW)
  ├── MySqlTenantDatabaseProvisioner
  ├── MySqlConnectionFactory
  └── PackageRefs: MySql.EntityFrameworkCore
  └── Depends on: Data.EFCore

CrestCreates.Data.EFCore.PostgreSql     (RENAMED from .PostgreSQL)
  ├── PostgreSqlTenantDatabaseProvisioner (existing, moved)
  ├── NpgsqlDbContexOptionsContributor
  └── PackageRefs: Npgsql.EntityFrameworkCore.PostgreSQL
  └── Depends on: Data.EFCore

CrestCreates.Data.SqlSugar              (MODIFIED — base, NO DB-specific provider)
  ├── SqlSugarOrmModule, SqlSugarRepository, SqlSugarUnitOfWork
  └── PackageRefs: SqlSugarCore (only)
  └── Depends on: Data.Core

CrestCreates.Data.SqlSugar.SqlServer    (NEW)
CrestCreates.Data.SqlSugar.MySql        (NEW)
CrestCreates.Data.SqlSugar.PostgreSql   (NEW)
  └── Each: DB-specific connection factory + provisioner

CrestCreates.Data.FreeSql               (RENAMED from FreeSqlProvider)
  ├── FreeSqlOrmModule, FreeSqlRepositoryBase, FreeSqlUnitOfWork
  └── PackageRefs: FreeSql, FreeSql.DbContext (only)
  └── Depends on: Data.Core

CrestCreates.Data.FreeSql.SqlServer     (NEW)
CrestCreates.Data.FreeSql.MySql         (NEW)
CrestCreates.Data.FreeSql.PostgreSql    (NEW)
  └── Each: DB-specific connection factory + provisioner
```

### 1.2 Design Rationale (ABP Pattern)

This follows the ABP Framework pattern:
- `Volo.Abp.EntityFrameworkCore` — base package, no DB provider
- `Volo.Abp.EntityFrameworkCore.SqlServer` — SQL Server only
- `Volo.Abp.EntityFrameworkCore.PostgreSql` — PostgreSQL only
- `Volo.Abp.EntityFrameworkCore.MySQL` — MySQL only

Each provider package is a thin layer over the base, adding only the provider-specific NuGet package and provider-specific implementations (e.g., `TenantDatabaseProvisioner`).

### 1.3 What Moves

| From (Current) | To (Target) | Items |
|---|---|---|
| `Data.EFCore` (references SqlServer) | `Data.EFCore` (no DB provider) | Remove SqlServer/Sqlite PackageRefs |
| `Data.EFCore` | `Data.EFCore.SqlServer` | SqlServerTenantDatabaseProvisioner, SqlServer connection factory |
| `Data.EFCore.PostgreSQL` | `Data.EFCore.PostgreSql` | Rename project, move PostgreSqlTenantDatabaseProvisioner |
| `Data.FreeSqlProvider` | `Data.FreeSql` | Rename project |
| `OrmProviders.*` (empty stubs) | RecycleBin | Remove empty directories |

### 1.4 What Stays in Data.EFCore (Base)

These are ORM-implementation concerns that don't depend on a specific database provider:
- `CrestCreatesDbContext` — depends on `Microsoft.EntityFrameworkCore` (not a specific provider)
- `MultiTenancyInterceptor` — EF Core SaveChangesInterceptor
- `AuditInterceptor` — EF Core SaveChangesInterceptor
- `EfCoreTenantSchemaMigrator` — uses `DbContext.Database.MigrateAsync()` (EF Core API, not provider-specific)
- `EfCoreTenantInitializationStore` — uses raw SQL, but the SQL is standard (UPDATE/SELECT)
- `TenantConnectionStringResolver` — ORM-level connection string resolution
- `TenantDbContextFactory` — EF Core DbContext factory
- Repository implementations — depend on EF Core abstractions

## 2. Multi-Tenancy Interface Consolidation

### 2.1 Target Structure

```
CrestCreates.MultiTenancy.Abstract (MODIFIED)
  ├── ICurrentTenant, ITenantInfo, ITenantProvider, ITenantResolver (existing)
  ├── ITenantStore (NEW — tenant configuration retrieval, following ABP)
  ├── ITenantInitializationOrchestrator (NEW)
  ├── ITenantDatabaseProvisioner (MOVED from Application.Contracts)
  ├── ITenantSchemaMigrator (MOVED from Application.Contracts)
  ├── ITenantDataSeedContributor (MOVED from Application.Contracts)
  ├── ITenantSettingDefaultsSeeder (MOVED from Application.Contracts)
  ├── ITenantFeatureDefaultsSeeder (MOVED from Application.Contracts)
  ├── ITenantInitializationEventSink (MOVED from Application.Contracts)
  ├── IPhaseResult (MOVED from Application.Contracts)
  ├── TenantInitializationContext (MOVED from Application.Contracts.DTOs)
  ├── TenantDatabaseInitializeResult (MOVED from Application.Contracts)
  ├── TenantMigrationResult (MOVED from Application.Contracts)
  └── TenantSeedResult (MOVED from Application.Contracts)

CrestCreates.MultiTenancy (MODIFIED)
  ├── TenantInitializationOrchestrator (MOVED from Application)
  ├── TenantLifecycleService (NEW — create/delete/activate/deactivate)
  ├── DefaultTenantStore (NEW — implements ITenantStore)
  ├── CurrentTenant, TenantManager, TenantResolver (existing)
  └── MultiTenancyMiddleware, TenantProviders, Resolvers (existing)
```

### 2.2 New Interfaces

**ITenantStore** (in `MultiTenancy.Abstract`):
```csharp
public interface ITenantStore
{
    Task<TenantConfiguration?> FindAsync(string tenantIdOrName, CancellationToken cancellationToken = default);
    Task<TenantConfiguration?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantConfiguration>> GetListAsync(CancellationToken cancellationToken = default);
}
```

**ITenantInitializationOrchestrator** (in `MultiTenancy.Abstract`):
```csharp
public interface ITenantInitializationOrchestrator
{
    Task<TenantInitializationResult> InitializeAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}
```

**TenantLifecycleService** (in `MultiTenancy`):
```csharp
public class TenantLifecycleService
{
    // Orchestrates tenant create/delete/activate/deactivate
    // Coordinates TenantManager + TenantInitializationOrchestrator
    Task<TenantDto> CreateTenantAsync(CreateTenantInput input);
    Task DeleteTenantAsync(Guid tenantId);
    Task ActivateTenantAsync(Guid tenantId);
    Task DeactivateTenantAsync(Guid tenantId);
}
```

### 2.3 Removed (Deprecated → Delete)

| Interface | File | Replacement |
|---|---|---|
| `ITenantDatabaseInitializer` | Application.Contracts | `ITenantDatabaseProvisioner` |
| `ITenantMigrationRunner` | Application.Contracts | `ITenantSchemaMigrator` |
| `ITenantDataSeeder` | Application.Contracts | `ITenantDataSeedContributor` |

| Implementation | File | Replacement |
|---|---|---|
| `EfCoreTenantDatabaseInitializer` | Data.EFCore | `SqlServerTenantDatabaseProvisioner` |
| `EfCoreTenantMigrationRunner` | Data.EFCore | `EfCoreTenantSchemaMigrator` |

### 2.4 Namespace Changes

| Before | After |
|---|---|
| `CrestCreates.Application.Contracts.Interfaces.ITenantDatabaseProvisioner` | `CrestCreates.MultiTenancy.Abstract.ITenantDatabaseProvisioner` |
| `CrestCreates.Application.Contracts.Interfaces.ITenantSchemaMigrator` | `CrestCreates.MultiTenancy.Abstract.ITenantSchemaMigrator` |
| `CrestCreates.Application.Contracts.Interfaces.ITenantDataSeedContributor` | `CrestCreates.MultiTenancy.Abstract.ITenantDataSeedContributor` |
| `CrestCreates.Application.Contracts.DTOs.Tenants.TenantInitializationContext` | `CrestCreates.MultiTenancy.Abstract.TenantInitializationContext` |

## 3. Auditing Contract Separation

### 3.1 Target Structure

```
CrestCreates.AuditLogging.Abstractions  (NEW)
  ├── IAuditLogStore (NEW)
  ├── IAuditedObject, IHasCreationTime, IHasModificationTime (moved from Domain?)
  ├── AuditedAttribute, DisableAuditingAttribute (moved from AuditLogging)
  └── Depends on: Domain.Shared (minimal, like ABP's Auditing.Contracts)

CrestCreates.AuditLogging (MODIFIED)
  ├── AuditLoggingMiddleware, AuditLogService, AuditLogWriter
  ├── AuditLogRedactor, AuditedMoAttribute
  ├── AuditInterceptor (MOVED from Data.EFCore)
  └── Depends on: AuditLogging.Abstractions, Domain, Authorization.Abstractions
```

### 3.2 What Moves

| From | To | Items |
|---|---|---|
| `Data.EFCore` | `AuditLogging` | `AuditInterceptor` (it's an auditing concern, not an EF Core concern) |
| `AuditLogging` | `AuditLogging.Abstractions` | `IAuditLogStore` interface, audit marker interfaces |

## 4. Cleanup

### 4.1 Remove Empty Stubs → RecycleBin

```
framework/src/CrestCreates.OrmProviders.Abstract/
framework/src/CrestCreates.OrmProviders.EFCore/
framework/src/CrestCreates.OrmProviders.FreeSqlProvider/
framework/src/CrestCreates.OrmProviders.SqlSugar/
```

### 4.2 Rename Projects

| Old Name | New Name |
|---|---|
| `CrestCreates.Data.EFCore.PostgreSQL` | `CrestCreates.Data.EFCore.PostgreSql` |
| `CrestCreates.Data.FreeSqlProvider` | `CrestCreates.Data.FreeSql` |

### 4.3 Remove Deprecated Code

- `ITenantDatabaseInitializer` + `TenantDatabaseInitializeResult` (in Application.Contracts)
- `ITenantMigrationRunner` + `TenantMigrationResult` (in Application.Contracts)
- `ITenantDataSeeder` + `TenantSeedResult` (in Application.Contracts)
- `EfCoreTenantDatabaseInitializer` (in Data.EFCore)
- `EfCoreTenantMigrationRunner` (in Data.EFCore)
- Deprecated constructor in `TenantInitializationOrchestrator` that accepts old interfaces

## 5. Dependency Graph

```
                    Domain.Shared
                         │
          ┌──────────────┼──────────────┐
          │              │              │
     MultiTenancy    Domain       DbContextProvider
       .Abstract        │           .Abstract
          │              │              │
          │         Data.Abstractions ──┘
          │              │
          │         Data.Core
          │              │
     MultiTenancy   ┌────┴────┬──────────┐
          │         │         │          │
          │    Data.EFCore  Data.SqlSugar  Data.FreeSql
          │      │    │         │    │        │    │
          │   .SqlServer .SqlServer .SqlServer .SqlServer
          │   .MySql     .MySql     .MySql     .MySql
          │   .PostgreSql .PostgreSql .PostgreSql .PostgreSql
          │
     AuditLogging.Abstractions
          │
     AuditLogging
```

## 6. Sample Updates

### 6.1 LibraryManagement.EntityFrameworkCore

```xml
<!-- Before -->
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
<ProjectReference Include="CrestCreates.Data.EFCore" />

<!-- After -->
<ProjectReference Include="CrestCreates.Data.EFCore" />
<ProjectReference Include="CrestCreates.Data.EFCore.PostgreSql" />
<!-- Npgsql package comes transitively from PostgreSql project -->
```

### 6.2 LibraryManagement.Web

```xml
<!-- Add reference to MultiTenancy for orchestrator -->
<ProjectReference Include="CrestCreates.MultiTenancy" />
<!-- Already present, verify it's used -->
```

## 7. Implementation Order

1. **Create `CrestCreates.Data.Core`** — new project with shared data infrastructure
2. **Rename `CrestCreates.Data.FreeSqlProvider` → `CrestCreates.Data.FreeSql`**
3. **Rename `CrestCreates.Data.EFCore.PostgreSQL` → `CrestCreates.Data.EFCore.PostgreSql`**
4. **Modify `CrestCreates.Data.EFCore`** — remove SqlServer/Sqlite PackageRefs, extract SqlServer-specific code
5. **Create `CrestCreates.Data.EFCore.SqlServer`** — move SqlServer provisioner and connection factory
6. **Create `CrestCreates.Data.EFCore.MySql`** — new project with MySql provisioner
7. **Create `CrestCreates.Data.SqlSugar.SqlServer/MySql/PostgreSql`** — stub projects
8. **Create `CrestCreates.Data.FreeSql.SqlServer/MySql/PostgreSql`** — stub projects
9. **Move tenant init interfaces** to `MultiTenancy.Abstract` from `Application.Contracts`
10. **Create `ITenantInitializationOrchestrator`** + `ITenantStore` in `MultiTenancy.Abstract`
11. **Move `TenantInitializationOrchestrator`** to `MultiTenancy` from `Application`
12. **Create `TenantLifecycleService`** and `DefaultTenantStore` in `MultiTenancy`
13. **Remove deprecated** interfaces and implementations
14. **Create `AuditLogging.Abstractions`** — extract audit contracts
15. **Move `AuditInterceptor`** to `AuditLogging` from `Data.EFCore`
16. **Remove empty `OrmProviders.*`** directories → RecycleBin
17. **Update `CrestCreates.slnx`** — add new projects, remove old
18. **Update `Directory.Packages.props`** — add MySql package versions
19. **Update samples** — fix references
20. **Build & fix** — resolve compilation errors
21. **Run tests** — ensure all pass

## 8. Testing Strategy

- **Build verification**: `dotnet build` entire solution after each phase
- **Unit tests**: Existing tests in `framework/test/` must continue to pass
- **Integration tests**: Tenant initialization pipeline must still work
- **Sample app**: `LibraryManagement.Web` must start and serve requests
- **AoT compilation**: `dotnet publish` with `CrestCreatesPublishMode=aot` must succeed

## 9. Rollback Plan

Each phase is independently reversible:
- Renamed projects keep git history (git mv)
- Deleted code goes to RecycleBin (not permanent deletion)
- New projects are additive until old ones are removed
- Samples can be reverted to reference old projects if needed