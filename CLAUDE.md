# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CrestCreates is a .NET 10 enterprise application framework (similar to ABP Framework) focused on modular DDD architecture with multi-ORM support. The framework emphasizes compile-time code generation, AoT friendliness, and a single main chain approach.

## Build & Test Commands

```bash
dotnet build                              # Build entire solution (uses CrestCreates.slnx)
dotnet build framework/src/CrestCreates.Domain   # Build a single project
dotnet test                               # Run all tests
dotnet test framework/test/CrestCreates.Domain.Tests   # Run a single test project
dotnet test --filter "FullyQualifiedName~CrestCreates.Application.Tests.Tenants.TenantAppServiceTests"  # Run specific test class
dotnet test --filter "FullyQualifiedName~SomeTestClass.SomeTestMethod"  # Run specific test method
dotnet run --project samples/LibraryManagement/LibraryManagement.Web  # Run sample app
```

- Solution file: `CrestCreates.slnx` (new XML-based `.slnx` format, not `.sln`)
- SDK: .NET 10.0.100 (`global.json`), `rollForward: latestMinor`
- Central package management: `Directory.Packages.props` (all versions pinned)
- AoT enabled globally via `Directory.Build.Aot.props` (except `netstandard2.0` projects and `framework/tools/`)

## Architecture

### Dual Code Generation Pipeline

This is the most critical architectural pattern to understand:

**Pipeline 1 — Roslyn Source Generator** (compile-time, per-project):
- Injected globally via `Directory.Build.Aot.props` as an Analyzer reference to every project
- Lives in `framework/tools/CrestCreates.CodeGenerator/` (targets `netstandard2.0`)
- Generates code into `obj/generated/CrestCreates.CodeGenerator/`
- Generators: DynamicApi, Module, Service, Entity (DTOs, repositories, mappings, permissions, validators, query builders)

**Pipeline 2 — MSBuild Tasks** (build-time, cross-project):
- Lives in `build/CrestCreates.BuildTasks/`
- 4 tasks: `ScanModulesFromSource` → `CollectModuleManifests` → `GenerateAggregatedModuleCode` → `ScanEntityPermissions`
- Outputs: `ModuleManifest.json`, `AggregatedModuleManifest.json`, `ModuleAutoInitializer.g.cs`, `EntityPermissionsManifest.json`
- Sample projects import `build/CrestCreates.BuildTasks/CrestCreates.Modules.props` to activate

### Key Attributes Driving Code Generation

| Attribute | Effect |
|-----------|--------|
| `[CrestModule]` | Module discovery → BuildTasks + SourceGenerator |
| `[CrestService]` | Service DI registration + controller/endpoint generation |
| `[Entity]` | Repository, DTO, permissions, query builder generation |
| `[GenerateCrudService]` | CRUD service generation for entity |
| `[GenerateRepository]` | Repository generation (with ORM provider selection) |
| `[GenerateObjectMapping]` | Compile-time object mapping (replaces AutoMapper) |
| `[DynamicApiRoute]` / `[DynamicApiIgnore]` | Custom route or exclusion from Dynamic API |
| `[UnitOfWorkMo]` | AOP transaction boundary (Rougamo/Fody) |
| `[CacheMo]` | AOP caching interceptor |
| `[MapFrom]` / `[MapIgnore]` / `[MapName]` | Object mapping property-level control |

### Module Lifecycle

```csharp
[CrestModule(typeof(DependencyModule), Order = -100)]
public class XxxModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services) { /* DI */ }
    // Also: OnPreInitialize, OnInitialize, OnPostInitialize, OnApplicationInitialization
}
```

Registration uses compile-time generated `ModuleAutoInitializer.g.cs` (primary). Runtime reflection `RegisterAllModules()` is legacy fallback.

### Entity & Service Patterns

```csharp
[Entity]
public class Book : AuditedEntity<Guid>  // Hierarchy: IEntity → Entity → AuditedEntity / AggregateRoot / FullyAuditedEntity
{
    // Private setters, constructor with validation, domain methods
    // Domain events via AddDomainEvent() (MediatR INotification)
}

[CrestService]
public class LoanAppService : CrestAppServiceBase<Loan, Guid, LoanDto, CreateLoanDto, LoanDto>, ILoanAppService
// Provides: CRUD, permission checking, data permission filtering, audit property setting, UoW
```

### Dynamic API Route Convention

- Method name → HTTP: `Create/Add/Insert` → POST, `Update/Put` → PUT, `Delete/Remove` → DELETE, `Get` → GET, `Query/Search` → POST
- Route: `{prefix}/{kebab-case-service-name}/{action-route}`, `Async` suffix stripped
- Permission mapping: `Create` → `{Service}.Create`, etc.

### Dependency Direction (Strict)

```
Domain.Shared ← Domain ← Application.Contracts ← Application
                                ↓                      ↓
                          Infrastructure          OrmProviders.*
                                ↓                      ↓
                          Web/AspNetCore ←──────────(implements)
```

- Contracts never depend on Application implementations
- Domain never depends on Web
- Infrastructure implements, never defines core business abstractions
- Do not put domain abstractions in Web, application orchestration in repositories, or platform capabilities as sample-specific code

### Project Layout

All framework source projects are **flattened** under `framework/src/` — no nested subdirectories per module. Each folder matches the project name exactly (e.g., `framework/src/CrestCreates.DynamicApi/`).

```
framework/src/     → 46 framework projects (flattened)
framework/test/    → 22 test projects
framework/tools/   → CodeGenerator (Roslyn source generator)
build/             → BuildTasks (MSBuild tasks)
samples/LibraryManagement/  → DDD sample (6 projects following strict layering)
```

## Key Principles

### Single Main Chain
Once a main implementation is chosen, do not maintain alternatives. This applies especially to:
- Dynamic API (compile-time generated endpoints, not runtime reflection)
- Module initialization (SourceGenerator + BuildTasks, not runtime scanning)
- Authentication/authorization (existing platform chains)
- Tenant creation/initialization

### Code Generation Priority
- Prefer compile-time generation over runtime reflection
- Prefer strong typing over string concatenation for cache keys, routes, providers
- AoT-friendly design is a first-class requirement
- If a capability can be compile-time generated OR runtime-scanned, prefer generation

### Change Self-Check
Before any change, verify:
1. Is this strengthening single main chain or preserving dual-track?
2. Is this reducing reflection/AoT or continuing runtime dependency?
3. Is this platform capability or business patch?
4. Does the test verify real main chain or expired path?
5. Will this mislead future maintainers into maintaining a legacy path?

If 1, 2, 4, or 5 are unfavorable, stop and redesign.

### Priority Order
1. Close the framework main chain
2. AoT / generation chain
3. Platform capability completeness
4. Horizontal module expansion (last)

## Testing

- Framework: xUnit 2.9.3, FluentAssertions, Moq, AutoFixture
- Test base classes in `framework/test/CrestCreates.TestBase/`: `TestBase` → `DomainTestBase` → `IntegrationTestBase` → `ApiTestBase<TStartup>`
- Integration tests use `WebApplicationFactory<Program>` with per-test PostgreSQL schema isolation (`itest_{guid}`)
- Tests must verify the "real main chain", not legacy paths
- Prefer real integration tests for: auth chain, tenant chain, Dynamic API main chain, Setting Management
- Mock tests can complement but not replace full-chain verification
- If a test verifies a downgraded legacy path, ask: is this still the official main chain? Will this mislead future maintainers?

## Naming Conventions

- Classes/interfaces/properties/methods: `PascalCase`
- Class names: must be nouns or noun phrases
- Async methods: must end with `Async`
- Private fields: `_camelCase`
- Namespaces: avoid conflicts with third-party libraries (e.g., `CrestCreates.OrmProviders.EFCore`, not `CrestCreates.Infrastructure.EntityFrameworkCore`)
- Comments: only explain complex logic or design intent, no obvious comments

## File Deletion Rule

Never directly delete files or folders. Move them to `./99_RecycleBin/` at the workspace root. Once moved, do not rename, compress, split, merge, or reorganize. Only manual deletion by the user in the GUI counts as controlled deletion.
