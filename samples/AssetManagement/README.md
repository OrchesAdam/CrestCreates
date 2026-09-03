# Asset Management Business Golden Application

Phase 10a business golden application for tenant-aware asset and equipment management.

The sample composes the existing Capability, generated Minimal API, DataPermission,
Workflow, HumanTask, durable outbox, Accountability, MCP, Agent Tools, and source-generated
JSON contracts. Business data uses a durable SQLite adapter with transactional concurrency
checks; runtime workflow, HumanTask and outbox state use the PostgreSQL production provider.

Run the executable golden scenario with:

```bash
export ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING='Host=127.0.0.1;Port=5432;Database=crest_asset_management;Username=crest;Password=crest'
dotnet run --project src/CrestCreates.Sample.AssetManagement.Host -- --golden-scenario
```

The connection string must point to PostgreSQL; the host intentionally has no
in-memory Runtime persistence fallback. CI supplies the PostgreSQL service and
the NativeAOT fixture starts a temporary PostgreSQL container when this variable
is absent.

## Construction friction record (#85)

| Frozen field | Evidence in this application | Classification |
| --- | --- | --- |
| Application files/code | `src/CrestCreates.Sample.AssetManagement.{Contracts,Domain,Application,Persistence,Host}` | Application code |
| Descriptors | `Host/AssetDescriptorCatalog.cs` owns schema, capability, workflow, HumanTask and form descriptors | Application descriptor declaration |
| Capabilities/handlers | `Application/Handlers/AssetCapabilityModule.cs` and `AssetHandlers.cs` | Generated-runtime capability path |
| Manual registration | `Host/Program.cs` registers registries, generated endpoint projection, PostgreSQL Runtime provider, identity and permission grants | Framework composition glue |
| Projection-specific code | `Host/Projections/AssetCompatibilityProjection.cs`, `AgentTools/AssetAgentTools.cs` and `McpTools/AssetMcpTools.cs` | Boundary adapter |
| Permission/DataPermission wiring | `AddCrestAuthorization`, explicit `IPermissionGrantRepository` + `IPermissionGrantManager` seed, and fail-closed `ApplyAssetDataPermissionAsync` guard | Framework authority plus application invariant |
| Persistence-specific code | `Persistence/SqliteAssetStore.cs` for business data; `CrestCreates.Runtime.Persistence.PostgreSql` for Workflow/HumanTask/Outbox | Durable provider adapter |
| Serialization-specific code | `Contracts/Json/AssetJsonContext.cs`, `Host/Json/AssetHostJsonContext.cs`, combined resolver and source-generated HTTP/MCP/Agent payloads | Source-generated contract glue |
| Framework glue | `Host/Program.cs`, endpoint mapping, runtime delivery, HumanTask completion obligation, workflow starter and explicit AOT consumer factory | Required composition |
| Workarounds | Explicit NativeAOT consumer factory; the canonical Workflow abort authority atomically compensates suspended Runtime records after a business-store failure; E2E uses a CI PostgreSQL service | AOT / cross-authority boundary |
| Framework modification? | Added the canonical `IWorkflowAbortService` (`Suspended → Failed` lifecycle + HumanTask cancellation + accountability in one Runtime transaction); producer metadata is normalized to contract microsecond precision before the unchanged outbox v1 hash; Agent completion confirmation compares structured JSON semantically | Framework capability gap exposed and closed by the production business case |
| Classification | Business sample with durable production Runtime composition; sample-owned permission grant storage is an explicit testable adapter | Phase 10a golden evidence |
