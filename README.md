# CrestCreates

CrestCreates is a modular .NET framework for business applications. It brings domain-driven design, compile-time code generation, and a typed execution model to applications that need tenant isolation, permissions, workflows, and an audit trail.

The central abstraction is a **capability** — a named operation with a defined input, result, and handler. A host chooses which operations to expose over HTTP or through tools. For example, a procurement application can accept a request over HTTP, let a manager approve it in a workflow, and let a tool read the result. The application service owns the procurement rules across those entry points.

## From an entity to an HTTP API

For a conventional resource, the feature author declares the entity and route on a partial class. This is the complete service declaration in the repository's Widget example.

```csharp
[GenerateCrudService(typeof(Widget), RoutePrefix = "/api/widgets")]
public partial class WidgetAppService
{
}
```

The build produces the service implementation, DTOs, mappings, typed query adapter, HTTP bindings, and JSON serialization metadata. In the host, `builder.Services.AddGeneratedCrud()` registers the generated contributions. At startup, `app.MapCrestCapabilityEndpoints()` checks the capability registrations and required JSON types before mapping routes. [Widget declaration](tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture.Feature/WidgetAppService.cs) · [Host wiring](tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture/Program.cs)

For an operation with its own business rules, implement a typed handler. The procurement submit handler takes a request DTO and passes the current tenant and user to the application service. Its HTTP declaration binds the request body, selects `POST /api/procurement/requests`, and sets the success response to `201 Created`. [Submit handler](samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Application/Handlers/SubmitProcurementRequestHandler.cs) · [Endpoint declarations](samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/Endpoints/ProcurementEndpoints.cs)

## Run an approval from end to end

With .NET installed, run this command from the repository root. The sample starts a local HTTP server on an ephemeral port, uses in-memory stores, and exits after checking the scenario.

```sh
dotnet run --project samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host -- --golden-scenario
```

The scenario submits a purchase request over HTTP and reads it through a Model Context Protocol (MCP) tool. A manager then completes the pending approval task. The next HTTP read must return both the approved status and the manager's identity. Finally, the sample submits a second request through an agent tool, replays the invocation, and checks that only one new request was stored.

A successful run exits with code zero and prints both lines below. The assertions, including the audit checks, are in the [scenario source](samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/ProcurementGoldenScenario.cs).

```text
CRESTCREATES_PROCUREMENT_SAMPLE_OK
CRESTCREATES_ACCOUNTABILITY_OK
```

## Explore the framework

| If you are working on… | Start here |
| --- | --- |
| Domain entities, repositories, application contracts, and services | [DDD building blocks](src/Framework/Ddd) and the [helpdesk ticket service](samples/SaaSHelpdesk/SaaSHelpdesk.Application/Services/TicketAppService.cs) |
| How declarations become executable code | [Source generators](src/Tooling/CrestCreates.CodeGenerator) and [MSBuild tasks for cross-project module registration](src/Tooling/CrestCreates.BuildTasks) |
| How capabilities, schemas, and human tasks fit together | [Procurement descriptor catalog](samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/ProcurementDescriptorCatalog.cs) and [runtime implementations](src/Runtime) |
| Transactional messaging across business processes | [ERP sample](samples/ErpManagement), which pairs a transactional outbox with RabbitMQ delivery and inbox processing |
| Choosing a module or persistence provider | [Capability map](docs/Feature/CapabilityMap.md) for implementation status and [ORM support matrix](docs/Feature/OrmProviderSupportMatrix.md) for tested provider combinations |

## Build and verify

Build the main solution and run the capability runtime tests from the repository root.

```sh
dotnet build CrestCreates.slnx
dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests
```

For the full project set, use the [all-project solution](solutions/CrestCreates.All.slnx). Integration suites that exercise PostgreSQL, RabbitMQ, and other external services use the shared test environment. Its [setup guide](infra/README.md) lists the services and connection variables.

NativeAOT support is documented per execution path and target platform. On Linux x64, the generated CRUD and helpdesk Ticket HTTP fixtures publish and link native executables, then run requests against them. Consult the fixture for the operations it exercises and the ORM matrix for database-specific behavior. [Generated CRUD fixture](tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture) · [Ticket HTTP fixture](tests/Framework/Api/CrestCreates.TicketHttpProjection.AotFixture) · [ORM support matrix](docs/Feature/OrmProviderSupportMatrix.md)

## License

MIT
