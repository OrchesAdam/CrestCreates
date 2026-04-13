# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CrestCreates is a .NET enterprise application framework (similar to ABP Framework) focused on modular DDD architecture with multi-ORM support. The framework emphasizes compile-time code generation, AoT friendliness, and a single main chain approach.

## Build & Test Commands

```bash
dotnet restore    # Restore dependencies
dotnet build      # Build entire solution
dotnet test       # Run all tests
dotnet run --project samples/LibraryManagement/LibraryManagement.Web  # Run sample application
```

Package versions are centrally managed in `Directory.Packages.props`.

## Architecture

### Solution Structure
```
CrestCreates/
├── framework/
│   ├── src/           # Framework modules (50+ projects)
│   ├── test/          # 13 test projects
│   └── tools/         # CodeGenerator, BuildTasks
├── samples/
│   └── LibraryManagement/  # Sample DDD application
├── docs/              # Chinese documentation
└── scripts/           # Setup scripts
```

### Dependency Direction (Strict)
```
Domain.Shared ← Domain ← Application.Contracts ← Application
                                ↓                      ↓
                          Infrastructure          OrmProviders.*
                                ↓                      ↓
                          Web/AspNetCore ←──────────(implements)
```

### Core Modules
| Module | Purpose |
|--------|---------|
| `CrestCreates.Modularity` | Module discovery via SourceGenerator + BuildTasks |
| `CrestCreates.MultiTenancy` | Multi-tenant (5 resolution strategies, 3 isolation modes) |
| `CrestCreates.OrmProviders.Abstract` | ORM abstraction (18 core interfaces) |
| `CrestCreates.OrmProviders.EFCore/FreeSql/SqlSugar` | ORM implementations |
| `CrestCreates.DynamicApi` | Compile-time generated API endpoints |
| `CrestCreates.Authorization` | RBAC permission system |
| `CrestCreates.EventBus.*` | Local + distributed event bus |
| `CrestCreates.AuditLogging` | Audit logging |
| `CrestCreates.Caching` | Multi-level caching (memory + Redis) |

## Key Principles (from AGENTS.md)

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

### Dependency Rules
- Contracts do not depend on Application implementations
- Domain does not depend on Web
- Infrastructure implements, never defines core business abstractions
- Keep layer dependency direction clean

### Naming Conventions
- Classes/interfaces/properties/methods: `PascalCase`
- Async methods: must end with `Async`
- Private fields: `_camelCase`
- Namespaces: avoid conflicts (e.g., `CrestCreates.OrmProviders.EFCore`, not `CrestCreates.Infrastructure.EntityFrameworkCore`)

### Change Self-Check
Before any change, verify:
1. Is this strengthening single main chain or preserving dual-track?
2. Is this reducing reflection/AoT or continuing runtime dependency?
3. Is this platform capability or business patch?
4. Does the test verify real main chain or expired path?

## Testing Requirements
- Tests must verify the "real main chain", not legacy paths
- Prefer real integration tests for: auth chain, tenant chain, Dynamic API main chain, Setting Management
- Mock tests can complement but not replace full-chain verification

## Documentation
- Documentation index: `docs/INDEX.md`
- Architecture docs: `docs/01-architecture/`
- Component docs: `docs/components/` (31 files)
- Sample project: `samples/LibraryManagement/`
