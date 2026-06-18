# CrestCreates Structural Migration Plan

Date: 2026-06-18

## Direction

The repository should move toward an ABP-like package layout:

- Keep `Core` very small.
- Treat framework capabilities as independent packages, not as children of `Core`.
- Keep runtime execution kernels separate from framework authoring/application packages.
- Keep persistence as business data access providers only.
- Keep platform projects as composition/preset entry points.
- Keep tooling projects outside Metadata unless their primary subject is metadata itself.

This plan is about project, folder, solution, namespace, and dependency boundaries. It should not redesign runtime behavior, split the Git repository, or introduce new database storage strategies.

Existing public namespaces and assembly names should be preserved during physical moves unless a later, explicit rename cleanup is approved.

## Target Layout

```text
src/
  Core/
    CrestCreates.Core.Abstractions
    CrestCreates.Core

  Framework/
    Modularity/
      CrestCreates.Modularity

    Ddd/
      CrestCreates.Domain.Shared
      CrestCreates.Domain
      CrestCreates.Application.Contracts
      CrestCreates.Application

    Infrastructure/
      CrestCreates.Aop.Abstractions
      CrestCreates.Aop
      CrestCreates.Caching.Abstractions
      CrestCreates.Caching
      CrestCreates.Configuration
      CrestCreates.Localization
      CrestCreates.Validation
      CrestCreates.VirtualFileSystem
      CrestCreates.Logging
      CrestCreates.Security.Abstractions
      CrestCreates.Security
      CrestCreates.DataFilter
      CrestCreates.MultiTenancy.Abstract
      CrestCreates.MultiTenancy
      CrestCreates.Authorization.Abstractions
      CrestCreates.Authorization

    Api/
      CrestCreates.DynamicApi
      CrestCreates.OpenApi

    Web/
      CrestCreates.AspNetCore
      CrestCreates.AspNetCore.Authentication.OpenIddict
      CrestCreates.HealthCheck
      CrestCreates.HealthCheck.AspNetCore
      CrestCreates.HealthCheck.Mvc

    Modules/
      CrestCreates.FileManagement
      CrestCreates.Form.Abstractions
      CrestCreates.Form
      CrestCreates.Organization.Abstractions
      CrestCreates.Organization
      CrestCreates.Scheduling
      CrestCreates.Scheduling.Quartz
      CrestCreates.ModuleDiagnostics

    Testing/
      CrestCreates.Testing

  Metadata/
    CrestCreates.Metadata.Abstractions
    CrestCreates.Metadata
    CrestCreates.Metadata.ContextPack.Abstractions
    CrestCreates.Metadata.ContextPack
    CrestCreates.Schema.Abstractions
    CrestCreates.Schema
    CrestCreates.Snapshot.Abstractions
    CrestCreates.Snapshot

    Draft/
      CrestCreates.DescriptorDraft.Abstractions
      CrestCreates.DescriptorDraft

  Runtime/
    Capability/
      CrestCreates.Capability.Abstractions
      CrestCreates.Capability

    Workflow/
      CrestCreates.Workflow.Abstractions
      CrestCreates.Workflow

    HumanTask/
      CrestCreates.HumanTask.Abstractions
      CrestCreates.HumanTask

    Eventing/
      CrestCreates.Event.Abstractions
      CrestCreates.Event
      CrestCreates.EventBus.Abstractions
      CrestCreates.EventBus.Abstract
      CrestCreates.EventBus.Local
      CrestCreates.EventBus.Local.Channel
      CrestCreates.EventBus.MediatorAdapter
      CrestCreates.EventBus.EventStore
      CrestCreates.EventBus.DeadLetter.EFCore
      CrestCreates.EventBus.RabbitMQ
      CrestCreates.EventBus.Kafka

    Audit/
      CrestCreates.AuditLogging.Abstractions
      CrestCreates.AuditLogging

    DistributedTransaction/
      CrestCreates.DistributedTransaction
      CrestCreates.DistributedTransaction.CAP

  Persistence/
    CrestCreates.Data.Abstractions
    CrestCreates.Data.Core
    CrestCreates.Data.EFCore
    CrestCreates.Data.EFCore.MySql
    CrestCreates.Data.EFCore.PostgreSql
    CrestCreates.Data.EFCore.SqlServer
    CrestCreates.Data.FreeSql
    CrestCreates.Data.SqlSugar
    CrestCreates.DbContextProvider.Abstract
    CrestCreates.OrmProviders.MongoDB

  Platform/
    CrestCreates.Web
    CrestCreates.Platform
    CrestCreates.Platform.AspNetCore
    CrestCreates.Platform.All

  Tooling/
    CrestCreates.CodeGenerator
    CrestCreates.Metadata.Analyzers
    CrestCreates.BuildTasks

  Integrations/
    CrestCreates.PluginSystem
    CrestCreates.Integration.Abstractions
    CrestCreates.Integration.LegacyDatabase
    CrestCreates.Integration.ExternalApi
```

## Key Corrections

### Core

`Core` must remain minimal. It should not contain projects that depend on `Framework`, `Runtime`, `Persistence`, `Platform`, or ASP.NET-specific packages.

Move these out of `Core`:

- `CrestCreates.Aop.Abstractions`
- `CrestCreates.Aop`
- `CrestCreates.Caching.Abstractions`
- `CrestCreates.Caching`
- `CrestCreates.Configuration`
- `CrestCreates.Logging`
- `CrestCreates.Security.Abstractions`
- `CrestCreates.Security`
- `CrestCreates.VirtualFileSystem`

These are framework infrastructure capabilities rather than root core primitives. If later a truly dependency-free primitive is needed, split it back into `Core`.

### Caching

Do not create `CrestCreates.Caching.Framework`.

Use:

```text
Framework/Infrastructure/CrestCreates.Caching.Abstractions
Framework/Infrastructure/CrestCreates.Caching
```

Caching package contents should be generic caching capability and its module registration. Feature, setting, tenant, or AOP-specific cache integration should live with the owning capability:

- Feature cache invalidation/key contributors belong with Feature Management.
- Setting cache invalidation/key contributors belong with Setting Management.
- Tenant cache key contributors belong with MultiTenancy.
- `CacheMoAttribute` belongs with AOP or a caching interceptor extension, not Core.

### Descriptor Draft

`CrestCreates.DescriptorDraft.Abstractions` and `CrestCreates.DescriptorDraft` are Metadata packages. They model descriptor draft / pre-publish state, not runtime execution.

They should live under:

```text
src/Metadata/Draft/
  CrestCreates.DescriptorDraft.Abstractions
  CrestCreates.DescriptorDraft
```

Do not place them under `Runtime/Draft`.

### Audit

Runtime audit needs an abstractions layer.

Target:

```text
src/Runtime/Audit/
  CrestCreates.AuditLogging.Abstractions
  CrestCreates.AuditLogging
```

If `CrestCreates.AuditLogging.Abstractions` does not exist yet, create a shell project first or move only pure contract types into it. Do not move application/query DTOs or framework-specific API contracts into runtime audit abstractions.

### EventBus.Abstract

Keep the current `CrestCreates.EventBus.Abstract` project name for this migration.

Only move folders. Do not rename the public API or assembly to `EventBus.Core` in this pass. Any naming cleanup should be a separate PR after the structural migration is stable.

### Platform Web

`CrestCreates.Web` may live in `src/Platform` only if it is a composition/preset project, for example `AddCrestCreatesWeb` style bootstrapping that combines Framework, Runtime, Persistence, and hosting.

If it contains low-level ASP.NET building blocks, split those into:

```text
src/Framework/Web/CrestCreates.AspNetCore
```

Then keep only composition/preset registration in:

```text
src/Platform/CrestCreates.Web
```

### Metadata ContextPack

`Metadata.ContextPack` can stay under Metadata, but its boundary must be explicit:

- It may produce metadata projections or packages for tooling and agent consumption.
- It must not contain Agent execution logic.
- It must not contain Agent runtime/control-plane orchestration.
- Agent control plane projects should live outside Metadata, most likely under Platform or Runtime/Agent depending on their actual role.

### Persistence

Persistence is for business data access providers only.

Do not move runtime store contracts into Persistence. In particular, do not put Workflow, HumanTask, Agent, or Audit runtime store contracts in `Persistence`.

Runtime store contracts should remain in the owning runtime abstraction package, for example:

- `Runtime/Workflow/CrestCreates.Workflow.Abstractions`
- `Runtime/HumanTask/CrestCreates.HumanTask.Abstractions`
- `Runtime/Audit/CrestCreates.AuditLogging.Abstractions`
- `Runtime/Agent/CrestCreates.Agent.Abstractions`

Provider implementations may be added later as explicit runtime provider packages, but not in this migration unless they already exist.

## Dependency Rules

- `Core/*` must not reference `Framework/*`, `Runtime/*`, `Persistence/*`, `Platform/*`, or ASP.NET-specific packages.
- `Metadata.Abstractions` must not reference `Runtime/*`, `Framework/*`, `Persistence/*`, or `Platform/*`.
- `Framework/*` may reference `Core`, `Metadata.Abstractions`, and framework peer packages.
- `Framework/Ddd/Domain` must not reference runtime event bus implementations. Domain event contracts should be separated from event bus runtime concerns.
- `Runtime/*` must not reference `Framework/Api`, `Framework/Web`, or `Platform/*`.
- `Runtime/*` must not reference `CrestCreates.Data.FreeSql` or `CrestCreates.Data.SqlSugar`.
- `Persistence/*` may reference domain and persistence abstractions, but must not reference `Application` as a long-term direction.
- `Persistence/*` must not reference `Runtime/Workflow`, `Runtime/Agent`, or `Runtime/HumanTask`.
- `Tooling/*` may reference Metadata/Framework/Runtime abstraction projects, but must not reference concrete Runtime implementation projects.
- `Platform/*` may compose Framework, Runtime, and Persistence.
- `Tooling/CrestCreates.CodeGenerator` may generate code for multiple layers but should not be a runtime dependency.
- `Metadata.ContextPack` must remain a metadata projection/package layer only.

At minimum, keep a dependency boundary test that scans `.csproj` `ProjectReference` entries for these rules. The test should be intentionally simple and build-time friendly; it does not need full MSBuild evaluation for the first pass.

## Migration Phases

### Phase 1: Physical Layout Cleanup

Move projects to the target folders while preserving namespaces, assembly names, and project names.

Recommended first moves:

- Move `CrestCreates.CodeGenerator` to `src/Tooling/CrestCreates.CodeGenerator`.
- Move `CrestCreates.Metadata.Analyzers` to `src/Tooling/CrestCreates.Metadata.Analyzers`.
- Move `CrestCreates.DescriptorDraft*` to `src/Metadata/Draft`.
- Move framework infrastructure packages out of `src/Core` into `src/Framework/Infrastructure`.
- Move runtime packages into their capability subfolders under `src/Runtime`.
- Move `CrestCreates.Web` to `src/Platform` only after confirming it is composition/preset code.

After each group:

```bash
dotnet build solutions/CrestCreates.All.slnx
```

### Phase 2: Abstractions and Boundary Hardening

Make the smallest dependency-boundary fixes that do not change behavior:

- Remove unused upper-layer references from abstraction projects.
- Add or populate `CrestCreates.AuditLogging.Abstractions`.
- Add boundary tests for `Core/*`.
- Add boundary tests for Runtime not referencing DynamicApi/Application/business persistence providers.
- Keep `EventBus.Abstract` name unchanged.

### Phase 3: Real Boundary Debt

Handle actual architectural debts separately from folder movement:

- Split Domain event contracts away from EventBus runtime abstractions.
- Remove `Persistence -> Application` references, especially in EF Core provider setup/seeding.
- Split ASP.NET security headers/antiforgery/HSTS out of generic security services.
- Move Feature/Setting/Tenant cache integration back to the owning capability packages.
- Decide whether `CrestCreates.Configuration` is a real framework capability or legacy compatibility wrapper around `Microsoft.Extensions.Configuration`.

## Test Layout

Tests should mirror the capability layout, not the old flat project tree.

```text
tests/
  Core/
  Framework/
    Ddd/
    Infrastructure/
    Web/
    Modules/
    Testing/
  Metadata/
    Draft/
  Runtime/
    Capability/
    Workflow/
    HumanTask/
    Eventing/
    Audit/
    DistributedTransaction/
  Persistence/
  Platform/
  Tooling/
  Boundary/
```

Do not delete tests during migration. If a disk-only test project is newly added to `CrestCreates.All.slnx` and exposes pre-existing failures, track it separately from structural migration failures.

## Current Known Caveats

- Some current tests fail for reasons unrelated to physical folder movement. These should be handled as separate test/runtime cleanup work.
- Existing shell projects should be reviewed after migration. Keep only shells that represent near-term composition or boundary targets.
- Root `CrestCreates.slnx` may remain temporarily, but `solutions/CrestCreates.All.slnx` should become canonical once the layout stabilizes.
