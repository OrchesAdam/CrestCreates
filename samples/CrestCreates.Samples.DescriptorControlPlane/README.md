# CrestCreates.Samples.DescriptorControlPlane

A **SQLite-backed Reference Golden Scenario** for the Company Certification business domain. This sample proves the descriptor-native control plane can gate and drive a real runnable workflow end-to-end, with SQLite persistence enabling host restart recovery and workflow continuation.

## What It Proves

- Descriptor authoring and control plane
- Governance and activation gating
- Capability execution
- Workflow suspension and continuation
- HumanTask completion
- SQLite persistence for business records, workflow instances, and human task instances
- Host restart recovery — state survives process restart
- Optimistic concurrency via ConcurrencyStamp

## What It Is Not

- Not a production business template
- Not a formal database Provider (no `CrestCreates.Workflow.Persistence.Sqlite` package)
- Not a complete ORM example
- Not a multi-node distributed runtime
- Not an Outbox or distributed transaction implementation

## Future Production Use

For real business applications:

1. Create an independent business project
2. Use a dedicated database with a proper provider (PostgreSQL, SQL Server, etc.)
3. Implement or reference a formal Persistence Provider
4. Configure migrations, backups, monitoring, access control, and operational policies

## Two-Plane Architecture

### Layer A: Descriptor Control Plane
```
descriptor inventory → relationship extraction → topology → impact →
compatibility → lifecycle governance → package / manifest / evidence / stable hash
```

### Layer B: Runtime Execution Plane
```
capability execution → workflow start / continuation → human task creation →
human task approval → approval capability execution → event capture
```

The control plane is the activation gate: `Allowed` executes runtime, `ReviewRequired` may execute only with explicit option, `Blocked` must not execute.

## Persistence Modes

| Mode | Storage | Use Case |
|---|---|---|
| **SQLite** (default) | Local `.db` file | Demonstration, restart recovery testing |
| InMemory | Process memory | Fast unit-style tests, no persistence |

### SQLite Configuration

```csharp
// Default demonstration mode
var host = CompanyCertificationGoldenScenarioHost.CreateSqlite(
    "artifacts/sample-data/company-certification.db");

// In-memory mode for fast tests
var host = CompanyCertificationGoldenScenarioHost.CreateInMemory();
```

### Default Database Path

```
artifacts/sample-data/company-certification.db
```

### Reset the Demo Database

```bash
rm artifacts/sample-data/company-certification.db
rm artifacts/sample-data/company-certification.db-wal
rm artifacts/sample-data/company-certification.db-shm
```

The database is re-created automatically on next startup (idempotent initialization).

### Test Databases

Tests use temporary databases under `artifacts/test-data/{unique-test-id}/` and clean up after completion.

## Project Structure

| File | Purpose |
|---|---|
| `CompanyCertificationDescriptors.cs` | 15 static descriptors (schemas, forms, capabilities, humantask, workflow, events) |
| `CompanyCertificationDescriptorInventory.cs` | Typed collections grouped by descriptor kind |
| `CompanyCertificationChangeScenarios.cs` | 6 before/after change scenarios with AOT-safe deep copy |
| `CompanyCertificationControlPlaneRunner.cs` | Synchronous control-plane analysis pipeline |
| `CompanyCertificationControlPlaneReport.cs` | Structured report with convenience pass/fail projections |
| `CompanyCertificationRuntimeModels.cs` | Domain data types for the runtime execution plane |
| `ICompanyCertificationStore.cs` | Business store abstraction |
| `InMemoryCompanyCertificationStore.cs` | Thread-safe in-memory store for certification records |
| `CompanyCertificationEvents.cs` | `ILocalEvent` implementations for submission, approval, rejection |
| `CompanyCertificationCapabilityInvokers.cs` | Three `ICapabilityContextAwareHandlerInvoker` implementations |
| `CompanyCertificationGoldenScenarioHost.cs` | DI host factory with SQLite/InMemory mode selection |
| `CompanyCertificationGoldenScenarioRunner.cs` | Two-plane runner with activation gate |
| `CompanyCertificationGoldenScenarioReport.cs` | Runtime report record |
| `Persistence/Sqlite/CompanyCertificationPersistenceOptions.cs` | Persistence mode configuration |
| `Persistence/Sqlite/SqliteConnectionFactory.cs` | SQLite connection management with WAL and foreign keys |
| `Persistence/Sqlite/SqliteDatabaseInitializer.cs` | Idempotent schema creation |
| `Persistence/Sqlite/SqliteCompanyCertificationStore.cs` | SQLite-backed `ICompanyCertificationStore` |
| `Persistence/Sqlite/SqliteWorkflowInstanceStore.cs` | SQLite-backed `IWorkflowInstanceStore` with optimistic concurrency |
| `Persistence/Sqlite/SqliteHumanTaskInstanceStore.cs` | SQLite-backed `IHumanTaskInstanceStore` with optimistic concurrency |
| `Persistence/Sqlite/SampleSqliteJsonContext.cs` | JSON serialization for workflow variables and step results |

## SQLite Tables

### company_certifications
| Column | Type | Description |
|---|---|---|
| id | TEXT (GUID) | Primary key |
| company_name | TEXT | Company name |
| unified_social_credit_code | TEXT | Business registration code |
| certification_type | TEXT | Type of certification |
| application_date | TEXT | Application date |
| notes | TEXT | Application notes |
| status | INTEGER | CertificationStatus enum value |
| reviewer_notes | TEXT | Review notes |
| reviewer_decision | TEXT | Approve/Reject decision |
| reviewed_by | TEXT | Reviewer user ID |

### workflow_instances
| Column | Type | Description |
|---|---|---|
| instance_id | TEXT | Primary key |
| workflow_descriptor_id | TEXT | Workflow descriptor reference |
| workflow_descriptor_version | INTEGER | Descriptor version |
| workflow_selection_mode | INTEGER | VersionSelectionMode enum |
| workflow_expected_contract_hash | TEXT | Contract hash (nullable) |
| status | INTEGER | WorkflowInstanceStatus enum |
| current_step_id | TEXT | Current step (nullable) |
| step_index | INTEGER | Current step index |
| waiting_human_task_id | TEXT | Waiting HumanTask instance ID (nullable) |
| started_at | TEXT | ISO 8601 timestamp |
| updated_at | TEXT | ISO 8601 timestamp (nullable) |
| completed_at | TEXT | ISO 8601 timestamp (nullable) |
| variables | TEXT (JSON) | Workflow variables dictionary |
| step_variables | TEXT (JSON) | Step-local variables dictionary |
| step_results | TEXT (JSON) | List of WorkflowStepResult |
| error_message | TEXT | Error message (nullable) |
| concurrency_stamp | TEXT | Optimistic concurrency token |

### human_task_instances
| Column | Type | Description |
|---|---|---|
| id | TEXT | Primary key |
| human_task_id | TEXT | HumanTask descriptor reference |
| human_task_version | INTEGER | Descriptor version |
| status | INTEGER | HumanTaskInstanceStatus enum |
| tenant_id | TEXT | Tenant ID (nullable) |
| assignee_user_id | TEXT | Assigned user (nullable) |
| assignee_role_id | TEXT | Assigned role (nullable) |
| workflow_instance_id | TEXT | Correlated workflow (nullable) |
| workflow_step_id | TEXT | Correlated step (nullable) |
| input | TEXT (JSON) | Task input data |
| output | TEXT (JSON) | Task output/result |
| outcome | TEXT | Completion outcome (nullable) |
| created_at | TEXT | ISO 8601 timestamp |
| updated_at | TEXT | ISO 8601 timestamp (nullable) |
| completed_at | TEXT | ISO 8601 timestamp (nullable) |
| cancelled_at | TEXT | ISO 8601 timestamp (nullable) |
| cancellation_reason | TEXT | Cancellation reason (nullable) |
| candidate_user_ids | TEXT (JSON) | List of candidate user IDs |
| candidate_role_ids | TEXT (JSON) | List of candidate role IDs |
| organization_unit_id | TEXT | Organization unit (nullable) |
| position_id | TEXT | Position (nullable) |
| assignee_resolution_reason | TEXT | Resolution reason (nullable) |
| concurrency_stamp | TEXT | Optimistic concurrency token |

## DI Registration and Store Replacement

SQLite stores are registered **before** `AddWorkflowEngine()` and `AddHumanTaskRuntime()`. Both methods use `TryAddSingleton` for their default `InMemoryWorkflowInstanceStore` and `InMemoryHumanTaskInstanceStore`, so the pre-registered SQLite stores take precedence.

```
services.AddSingleton<IWorkflowInstanceStore, SqliteWorkflowInstanceStore>();  // registered first
services.AddWorkflowEngine();  // TryAddSingleton<InMemoryWorkflowInstanceStore> → no-op
```

## JSON/AOT Serialization Strategy

- Strongly-typed collections (`List<WorkflowStepResult>`, `List<string>`) use reflection-based `JsonSerializerOptions` (acceptable for a sample project)
- `Dictionary<string, object?>` (workflow variables) uses reflection-based serialization with `JsonElement` → CLR type conversion on deserialization
- `object?` fields (HumanTask input/output) use type-preserving serialization
- `Guid` values in workflow variables are correctly round-tripped (detected via `TryGetGuid` during `JsonElement` conversion)
- No arbitrary `$type` discriminators or runtime type name usage
- This is a sample project — framework core maintains stricter AOT requirements

## One-Command Regression

```bash
dotnet test --filter "FullyQualifiedName~GoldenScenario"
```

This filter covers both InMemory and SQLite Golden Scenario test classes.

Expected output:
```
ControlPlane: Passed
Governance: ReviewRequired (baseline) or Blocked (breaking scenarios)
Workflow: Completed
HumanTask: Approved
Event: CompanyCertificationApproved captured
```

## Test Coverage

### Control-Plane Tests (7 tests)
- Baseline healthy topology
- Optional field addition → compatible
- Required field removal → breaking, review-required
- Permission change → security-sensitive
- Missing workflow target → blocked
- Unsupported subworkflow → warning
- Package manifest/evidence/stable hash

### Golden Scenario Tests (5 tests)
- Baseline control-plane + runtime
- Happy-path workflow completion
- Approval event publication
- Breaking change blocks runtime activation
- Missing workflow target blocks runtime activation

### SQLite Persistence Tests (8 tests)
- SQLite happy path — full workflow completion with persistence
- Host restart — business record recovery
- Host restart — workflow instance recovery
- Suspend → Restart → Continue — workflow completes after host restart
- Workflow concurrency conflict — `RuntimeConcurrencyException` on stale stamp
- HumanTask concurrency conflict — `RuntimeConcurrencyException` on stale stamp
- Test isolation — different databases do not pollute each other
- Governance blocked — no runtime data created

### Authoring Golden Scenario Tests (13 tests)
- Fake agent output is deterministic
- Draft set creates finance review HumanTask
- Draft set updates workflow with finance review step
- Draft set sequential materialization produces final proposed inventory
- Draft set final decision rechecks complete inventory
- Runtime proof builds fresh host from approved final inventory
- Runtime proof completes initial review then finance review
- Activation request binds final review and package evidence hashes
- Activation gate success alone does not count as runtime proof
- Authoring context memory is non-authoritative
- Authoring context metadata wins when memory conflicts
- Fake authoring agent cannot call RuntimeActivationGate
- Fake authoring agent cannot call runtime handlers
- Phase 7f end-to-end: authoring to activated runtime golden scenario

## Descriptor Inventory

| Kind | Count | IDs |
|---|---|---|
| Schema | 5 | SubmitInput, ReviewInput, Result, ApprovedPayload, RejectedPayload |
| Form | 2 | SubmitForm, ReviewForm |
| Capability | 3 | Submit, Approve, Reject |
| HumanTask | 1 | ReviewCompanyCertification (both Approve + Reject outcomes) |
| Workflow | 1 | 3-step: submit → review (human task) → finalize approval |
| Event | 3 | Submitted, Approved, Rejected |
