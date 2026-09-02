# Asset Management Business Golden Application

Phase 10a business golden application for tenant-aware asset and equipment management.

The sample composes the existing Capability, generated Minimal API, DataPermission,
Workflow, HumanTask, durable outbox, Accountability, MCP, Agent Tools, and source-generated
JSON contracts. Business data uses a durable SQLite adapter with transactional concurrency
checks; runtime workflow and outbox state use the existing runtime persistence composition.

Run the executable golden scenario with:

```bash
dotnet run --project src/CrestCreates.Sample.AssetManagement.Host -- --golden-scenario
```

## Construction friction record

| Requirement | Application evidence | Classification |
| --- | --- | --- |
| Asset aggregate and lifecycle | `src/CrestCreates.Sample.AssetManagement.Domain/Entities/Asset.cs` | Application/domain-owned invariant |
| Tenant and organization visibility | `AssetApplicationService` plus `IDataPermissionFilter` | Existing platform composition |
| Durable business persistence | `SqliteAssetStore` with transactional updates and concurrency stamps | Application adapter; no framework gap |
| Capability and generated HTTP mainline | `AssetCapabilityModule`, `AssetEndpoints`, generated registries | Compile-time generated/runtime registry |
| Human approval | `AssetMaintenanceWorkflowService` and `AssetMaintenanceDecisionConsumer` | Existing Workflow + HumanTask + durable outbox |
| Accountability | `AuditedMo` and host accountability middleware | Existing platform composition |
| MCP/Agent reuse | `AssetMcpTools` and `AssetAgentTools` target the Get capability | Same capability mainline |
| NativeAOT JSON | Contract and host `JsonSerializerContext` types | Source-generated serialization |

No framework modification was required. The only sample-owned workaround is the explicit
SQLite application adapter and explicit descriptor registration required to keep the sample
production composition durable and reflection-free.
