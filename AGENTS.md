# CrestCreates Agent Guide

## Source Of Truth

- This is a .NET 10 repository. `global.json` pins SDK `10.0.100` with `latestMinor` roll-forward; package versions are centralized in `Directory.Packages.props`.
- Prefer executable configuration and CI (`Directory.Build*.props/targets`, `.github/workflows/`, `.github/scripts/`) over stale prose. Read `memory.md` and the relevant `docs/superpowers/specs/` before architectural changes; update `memory.md` when a durable platform decision changes.
- `CLAUDE.md` only points here. There is no repo-local OpenCode configuration.

## Build And Test

- The canonical solution is `CrestCreates.slnx`; layered solutions are under `solutions/`.
- After a fresh restore, build BuildTasks before the solution. Several projects import `src/Tooling/CrestCreates.BuildTasks/CrestCreates.Modules.props`, whose local development import resolves the task assembly from `bin/Debug/net10.0`.

```bash
dotnet restore CrestCreates.slnx
dotnet restore tests/Runtime/Agent/CrestCreates.Agent.Memory.TypeForwardLegacyConsumer/CrestCreates.Agent.Memory.TypeForwardLegacyConsumer.csproj
dotnet build src/Tooling/CrestCreates.BuildTasks/CrestCreates.BuildTasks.csproj --no-restore
dotnet build CrestCreates.slnx --no-restore
dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests --no-build
```

- Run a focused test with `dotnet test <project-or-directory> --no-build --filter "FullyQualifiedName~Namespace.Type.Method"`.
- CI is intentionally staged: `.github/workflows/ci.yml` is PR validation and `.github/workflows/full-validation.yml` is the push-to-master inventory. Both include checks that a single root `dotnet test` can miss, including JSON-contract and dependency-boundary gates.
- PostgreSQL persistence and web integration tests start `postgres:16-alpine` through Testcontainers and require Docker. Event-bus integration tests require the services in `infra/docker-compose.eventbus.yml`.
- Test projects disable Trim/NativeAOT because Moq/Castle.DynamicProxy needs runtime code generation; do not infer production AOT support from ordinary test success.

## Generation And Publishing

- Source generation is the runtime mainline: generated Dynamic API Minimal API endpoints, registries, bindings, and module initialization are preferred over runtime scanning/reflection fallbacks.
- `Directory.Build.Aot.props` imports `CrestCreates.CodeGenerator` globally unless `-p:CrestCreatesCodeGeneration=false`; BuildTasks aggregates module manifests before `CoreCompile`. Change declarations, generators, or BuildTasks rather than generated output under `obj/`.
- Default publish mode is Trim. NativeAOT requires explicit `-p:CrestCreatesPublishMode=aot`, a self-contained RID, native linking, and execution of the original binary. A `NativeAOT-verified` claim is fixture-specific, not repository-wide.
- `.github/workflows/aot-validation.yml` is disabled (`on: []`); use the active AOT gates in `ci.yml` and `.github/scripts/`.

```bash
dotnet test tests/Integrations/CrestCreates.Mcp.AotFixture.Tests/CrestCreates.Mcp.AotFixture.Tests.csproj --no-restore --no-build
bash .github/scripts/run-json-contract-aot-gates.sh
```

- Test-owned AOT gates publish, link, and run the original binary; they are pinned to `linux-x64`. These checks can be expensive and need a native toolchain (and Docker for PostgreSQL fixtures).

## Architecture Boundaries

- The framework is metadata-first and compile-time-generation-first: runtime execution consumes typed registries, immutable snapshots, and the governed Capability/Workflow pipelines.
- Preserve the one-mainline design. Do not add new runtime reflection scanners, dictionary/string protocol fallbacks, service-locator lookups, or entry-point-specific copies of authorization, tenant, audit, settings, or feature logic.
- Keep dependency direction enforced by `tests/Boundary/CrestCreates.DependencyBoundaries.Tests`; Core and Metadata abstractions must not acquire dependencies on higher Framework, Runtime, Persistence, or Platform layers.
- Keep control-plane governance separate from runtime handlers; handlers perform business actions and should not approve or mutate their own governance state.

## Repository Hygiene

- Do not permanently delete files or directories. Move removals to `99_RecycleBin/` and preserve their original hierarchy for human review; `.gitignore` intentionally excludes that directory.
- Do not edit generated files or build artifacts. Generated source is emitted under `obj/.../source-generators`; fix the source declaration or generator instead.
- Preserve unrelated worktree changes. When docs and code disagree, verify the executable path first and correct the documentation only when the change is part of the task.

## Useful References

- User and feature documentation: `docs/Feature/`
- Architecture specifications and implementation plans: `docs/superpowers/specs/` and `docs/superpowers/plans/`
- Samples: `samples/LibraryManagement/`, `samples/SaaSHelpdesk/`, and `samples/ProcurementApproval/`
- Module BuildTasks props: `src/Tooling/CrestCreates.BuildTasks/CrestCreates.Modules.props`
