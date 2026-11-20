# CrestCreates

CrestCreates is a .NET 10 framework for modular business applications. It combines domain-driven design with compile-time generation so that an application can expose a business operation over HTTP, offer it as a tool, or invoke it from a workflow while keeping the handler focused on the operation itself.

A capability describes an operation and its contract. A host selects which capabilities to publish and configures the services they need. At runtime, the capability pipeline uses the execution context to apply policies such as input validation, authorization, tenant isolation, and auditing.

## Run an approval from start to finish

From the repository root, use .NET SDK **10.0.114**, the version pinned in CI.

```bash
dotnet --version
dotnet run --project samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host -- --golden-scenario
```

The sample starts a loopback HTTP server and uses in-memory storage. It submits a purchase request over HTTP, reads the same request through an MCP tool, and completes the resulting human approval task. It also exercises Agent Tool replay and checks the audit records. A successful run prints `CRESTCREATES_PROCUREMENT_SAMPLE_OK` and `CRESTCREATES_ACCOUNTABILITY_OK`, then shuts down.

## Follow the request through the code

1. The [procurement catalog](samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/ProcurementDescriptorCatalog.cs) describes the submit operation, its input and output schemas, and the approval workflow.
2. The [endpoint declarations](samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/Endpoints/ProcurementEndpoints.cs) supply the route and binding metadata for the HTTP source generator. The [host entry point](samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/Program.cs) maps the generated endpoints.
3. The [typed submit handler](samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Application/Handlers/SubmitProcurementRequestHandler.cs) calls the application service with the tenant and user from its execution context. The workflow and human-task runtimes manage the approval steps.

This split also applies to generated AppService and CRUD endpoints. Roslyn generators and MSBuild tasks prepare endpoint bindings, JSON metadata, and module registration at build time. The host composes the resulting contributions. See the [capability exposure guide](docs/Feature/CapabilityExposure/usage-guide.md) and the [metadata model guide](docs/Feature/UnifiedMetadataModel/usage-guide.md).

## Explore other applications

| Application | What to look for |
| --- | --- |
| [SaaS Helpdesk](samples/SaaSHelpdesk/) | Tenant-aware ticketing, SLA policies, and the platform's identity and administration endpoints. |
| [Asset Management](samples/AssetManagement/README.md) | Equipment maintenance and approval with SQLite business data and PostgreSQL workflow, human-task, and outbox state. |
| [ERP Management](samples/ErpManagement/deploy/README.md) | Procurement, inventory, and manufacturing, with a transactional outbox and RabbitMQ delivery. |
| [Descriptor Control Plane](samples/CrestCreates.Samples.DescriptorControlPlane/README.md) | Agent-assisted drafting and review of metadata. The [Control Plane guide](docs/Feature/AgentControlPlane/usage-guide.md) explains the evidence and activation contracts. |

## Build and test

```bash
dotnet build solutions/CrestCreates.All.slnx
dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests --no-build
```

For database and broker tests, start the services required by the suite and set its connection environment variables. See the [integration test setup](infra/README.md).

The repository defaults to trimmed publishing. For NativeAOT, use `-p:CrestCreatesPublishMode=aot` when publishing a supported fixture. The following test performs the publish, links the native executable, and runs HTTP assertions against it on Linux x64.

```bash
dotnet test tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture.Tests --filter "Category=NativeAotGate"
```

Support is recorded per execution path, tested scenario, and runtime identifier. Check the [capability map](docs/Feature/CapabilityMap.md), [ORM provider support matrix](docs/Feature/OrmProviderSupportMatrix.md), and [native execution evidence](docs/review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md) before selecting a host or provider for deployment.

GitHub Actions runs [CI](https://github.com/OrchesAdam/CrestCreates/actions/workflows/ci.yml) for pull requests to `master` and [Full Validation](https://github.com/OrchesAdam/CrestCreates/actions/workflows/full-validation.yml) after pushes to `master`. Both workflows also support manual runs.

## License

MIT
