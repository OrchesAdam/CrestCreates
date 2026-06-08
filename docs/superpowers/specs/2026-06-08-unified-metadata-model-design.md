# Unified Business Metadata Model Design

## Date: 2026-06-08

## Status: Draft

---

## 1. Problem Statement

CrestCreates plans to introduce three platform capabilities: **Form**, **Workflow**, and **Agent**. Before implementing any of them, we must establish a unified metadata model that prevents the framework from fracturing into multiple independent descriptor systems.

The risk: without a unified model, each capability would develop its own metadata layer:

```
Entity → Form → FormSchema
Entity → Workflow → WorkflowSchema
ApplicationService → API → ApiSchema
ApplicationService → Agent → ToolSchema
```

Result: 4-5 parallel, incompatible descriptor systems, each with its own notion of "what data looks like" and "what the system can do."

The goal: converge on three core abstractions — **Schema**, **Capability**, **Workflow** — and make everything else a consumer or projection of these three.

---

## 2. Core Abstraction: The Three Pillars

| Pillar | Answers | Descriptor |
|--------|---------|------------|
| **Schema** | "What does the data look like?" | `SchemaDescriptor` |
| **Capability** | "What can the system do?" | `CapabilityDescriptor` |
| **Workflow** | "How are capabilities orchestrated?" | `WorkflowDescriptor` |

Everything else — Form, HumanTask, DynamicAPI, AgentTool, MCP Tool — is a **consumer** or **projection view** of these three.

---

## 3. The Dependency Chain

```text
                    SchemaDescriptor
                        ↑
        ┌───────────────┼───────────────┐
        │               │               │
CapabilityDescriptor  FormDescriptor    │
        ↑               ↑               │
        │               │               │
  ┌─────┼─────┐   HumanTaskDescriptor ──┘
  │     │     │         │
  │     │     │    ┌────┴────┐
  │     │     │    │         │
DynamicAPI Agent MCP  │    (post-completion
          Tool  Tool   │     Capability ref)
                       │
              WorkflowDescriptor
                       │
                  WorkflowStep
                       │
                       ▼
                InteractionTarget
                       │
              ┌────────┼──────────┐
              │        │          │
        Capability  HumanTask SubWorkflow
          Target     Target     Target
```

**Key dependency rules:**

- `SchemaDescriptor` depends on nothing (leaf node).
- `CapabilityDescriptor` depends on `SchemaDescriptor` (for Input/Output schema).
- `FormDescriptor` depends on `SchemaDescriptor` (Form = Schema + UI metadata).
- `HumanTaskDescriptor` depends on `FormDescriptor` (references form for UI) and `SchemaDescriptor` (task input/output schema).
- `WorkflowDescriptor` depends on `CapabilityDescriptor`, `HumanTaskDescriptor`, and itself (sub-workflows) via `InteractionTarget`.
- `DynamicApiDescriptor`, `AgentToolDescriptor`, `MCPToolDescriptor` are **projection views** of `CapabilityDescriptor` — they do not define their own metadata system.
- `WorkflowStep` owns an `InteractionTarget`. The step handles ordering/conditions/transitions; the target defines what executes.

**Entity** is intentionally absent from this chain. Entity is a domain concept that **produces** Schema (via `EntityDescriptor → SchemaDescriptor`), but sits outside the capability chain. An Entity is one source of Schema, not the only source.

---

## 4. Descriptor Responsibility Boundaries

### 4.1 SchemaDescriptor — "What does the data look like?"

| Responsibility | Detail |
|---|---|
| Field definitions | name, type, constraints (required, max, min, pattern) |
| Nested schemas | complex types, arrays of schema refs |
| Relations | references to other SchemaDescriptors |
| Validation rules | per-field and cross-field validation expressions |
| Default values | static or computed defaults |
| **NOT: UI layout** | belongs to FormDescriptor |
| **NOT: permissions** | belongs to CapabilityDescriptor or HumanTaskDescriptor |
| **NOT: field visibility** | belongs to FormDescriptor |
| **NOT: who can read/write a field** | belongs to CapabilityDescriptor |

Schema is a **pure data description**. It has no opinion about rendering, permissions, or business context.

Schema variants used across the system:
- `EntitySchema` — derived from Entity properties.
- `CapabilityInputSchema` — the input payload of a Capability.
- `CapabilityOutputSchema` — the output payload of a Capability.
- `WorkflowVariableSchema` — variables carried across Workflow steps.
- These are all the same `SchemaDescriptor` type — no separate subclasses needed.

### 4.2 CapabilityDescriptor — "What can the system do?"

Capability is an **atomic business action**. It answers exactly three questions:

1. **What?** — `Name` (e.g., `employee.create`)
2. **Input?** — `InputSchema` (ref to SchemaDescriptor)
3. **Output?** — `OutputSchema` (ref to SchemaDescriptor)

Plus a minimal set of execution metadata:

| Field | Detail |
|---|---|
| `Name` | stable business capability name (kebab-case convention) |
| `Kind` | `Query` / `Draft` / `Command` |
| `InputSchema` | ref to SchemaDescriptor |
| `OutputSchema` | ref to SchemaDescriptor |
| `Permission` | required permission to invoke |
| `RiskLevel` | `Low` / `Medium` / `High` / `Critical` |

**Explicitly NOT in CapabilityDescriptor:**

| Field | Why not? |
|---|---|
| Workflow definition | Workflow is an orchestrator, not a Capability kind |
| HumanTask definition | HumanTask is human interaction, not system execution |
| Form reference | Capability has no UI |
| Audit policy detail | Audit is a cross-cutting concern applied by the pipeline |
| Outbox/Retry/Cache config | Infrastructure concerns, applied by the execution pipeline |
| Agent metadata | Agent is a consumer of Capability, not a property of it |
| DynamicAPI route/HTTP method | DynamicAPI is a consumer of Capability |

**CapabilityKind rationale:**

```csharp
public enum CapabilityKind
{
    Query,    // Read-only, no side effects
    Draft,    // Write with no side effects (save draft, validate only)
    Command   // Write with side effects
}
```

Draft belongs in Capability because it still follows the Input → Output pattern — it just carries the semantic guarantee of "no side effects beyond the draft store." This is not the same as HumanTask or Workflow, which are fundamentally different interaction models.

**What about `IdempotencyMode`, `TransactionMode`, `Timeout`, `Compensation`?**

These are **execution pipeline concerns**, not Capability definition concerns. They belong to:
- The **Capability Execution Pipeline** (which resolves a CapabilityDescriptor and wraps the handler with idempotency, transaction, timeout, audit).
- The **Capability Profile** (tenant-level or environment-level overrides for timeout, retry policy, etc.).

Keeping these out of the descriptor itself prevents Capability from becoming a God Object.

### 4.3 FormDescriptor — "What does the user input?"

Form is **pure UI metadata**. It is NOT a business action.

```text
Form = Schema + UI Metadata
```

| Responsibility | Detail |
|---|---|
| `Schema` | ref to SchemaDescriptor (data shape) |
| Field ordering | display order of fields |
| Field grouping | tabs, sections, fieldsets |
| Field metadata | label, placeholder, help text, format hint |
| Visibility conditions | per-field show/hide expressions |
| Editability | per-field read-only/editable based on context |
| Layout | grid columns, responsive breakpoints |
| **NOT: business logic** | belongs to Capability |
| **NOT: who can submit** | belongs to HumanTask |
| **NOT: what happens on submit** | belongs to HumanTask |
| **NOT: permissions** | belongs to HumanTask |

Form does NOT know:
- Who will fill it out.
- What happens after submission.
- Whether it requires approval.
- What permissions are needed.

Form only knows: "here is the data shape, and here is how to display it."

### 4.4 HumanTaskDescriptor — "Who inputs, when, and what happens next?"

HumanTask is the **business action** of human interaction. Form is its UI delegate.

| Responsibility | Detail |
|---|---|
| `Form` | ref to FormDescriptor (what UI to show) |
| `InputSchema` | what data the human provides |
| `OutputSchema` | what data the task produces when complete |
| Assignee strategy | single user, candidate group, round-robin, least-loaded |
| Timeout / SLA | due duration, escalation on overdue |
| Transfer / delegation | allowed roles, restricted transfers |
| Completion condition | approve/reject/any-input/custom expression |
| Completion outcomes | what Capability to invoke on each outcome (optional) |
| Permissions | who can claim/view/act on this task |
| **NOT: UI rendering** | delegates to FormDescriptor |
| **NOT: business execution** | delegates to CapabilityDescriptor (for post-completion actions) |

HumanTask is NOT a CapabilityKind. A Capability is system-executed; a HumanTask is human-executed. Merging them would mean:

```csharp
// ANTI-PATTERN — do NOT do this:
CapabilityKind.HumanTask
```

Instead:

```csharp
// Correct:
WorkflowStep.Target = new HumanTaskTarget
{
    HumanTaskName = "manager.approval",
    // ...
};
```

### 4.5 WorkflowDescriptor — "How to orchestrate"

| Responsibility | Detail |
|---|---|
| Steps | ordered list of WorkflowSteps |
| Variables | `WorkflowVariableSchema` — data carried between steps |
| Gateways | exclusive, parallel, inclusive branching |
| Error handling | per-step retry, compensation, fallback |
| Version | workflow definition version |
| **NOT: business execution** | delegates to Capability (via InteractionTarget) |
| **NOT: human interaction** | delegates to HumanTask (via InteractionTarget) |
| **NOT: UI rendering** | delegates to HumanTask → Form |

#### WorkflowStep

A WorkflowStep is a container for:

| Field | Detail |
|---|---|
| `Id` | step identifier within the workflow |
| `Target` | `InteractionTarget` — what executes |
| `Condition` | expression for conditional execution |
| `Transition` | which step(s) follow |
| `InputMapping` | how workflow variables map to target input |
| `OutputMapping` | how target output maps back to workflow variables |
| `OnError` | retry, compensate, fail, or skip |

#### InteractionTarget (Abstract)

InteractionTarget is the polymorphic binding for a WorkflowStep. Three concrete types:

| Type | Executes |
|---|---|
| `CapabilityTarget` | Invokes a `CapabilityDescriptor` by name |
| `HumanTaskTarget` | Creates and awaits a `HumanTaskDescriptor` |
| `SubWorkflowTarget` | Invokes a child `WorkflowDescriptor` |

A WorkflowStep does NOT bind to:
- An `ApplicationService` method directly ❌
- A `FormDescriptor` directly ❌ (goes through HumanTask)
- A Controller or HTTP endpoint ❌

### 4.6 EntityDescriptor

EntityDescriptor lives **outside** the Capability chain. It describes domain data — aggregates, entities, value objects — and **produces** Schema:

```text
EntityDescriptor → SchemaDescriptor
```

| Responsibility | Detail |
|---|---|
| Properties | name, type, constraints |
| Relations | navigation properties, foreign keys |
| Indexes | database indexes |
| Domain behavior | domain methods and invariants |
| Auditing mode | CreationAudited, ModificationAudited, FullyAudited, None |
| Multi-tenancy | TenantId, organization isolation |
| **NOT: Form definition** | Entity doesn't know about UI |
| **NOT: Permissions** | entity-level permissions are Capability concern |
| **NOT: API exposure** | DynamicAPI exposes Capabilities, not Entities |

---

## 5. Dependency Rules (Definitive)

### ALLOWED

| From → To | Reason |
|---|---|
| `CapabilityDescriptor` → `SchemaDescriptor` | Input/Output schema |
| `FormDescriptor` → `SchemaDescriptor` | Form = Schema + UI metadata |
| `HumanTaskDescriptor` → `FormDescriptor` | HumanTask delegates UI to Form |
| `HumanTaskDescriptor` → `SchemaDescriptor` | HumanTask defines task input/output schema |
| `HumanTaskDescriptor` → `CapabilityDescriptor` | Post-completion actions (approve triggers capability, reject triggers capability) |
| `WorkflowDescriptor` → `SchemaDescriptor` | Workflow variable schema |
| `WorkflowStep.Target` → `CapabilityTarget` | Invoke capability |
| `WorkflowStep.Target` → `HumanTaskTarget` | Create human task |
| `WorkflowStep.Target` → `SubWorkflowTarget` | Invoke child workflow |
| `DynamicApiDescriptor` → `CapabilityDescriptor` | Exposes capability as HTTP (projection view) |
| `AgentToolDescriptor` → `CapabilityDescriptor` | Exposes capability to LLM (projection view) |
| `MCPToolDescriptor` → `CapabilityDescriptor` | Exposes capability as MCP tool (projection view) |
| `EntityDescriptor` → `SchemaDescriptor` | Entity produces Schema |

### FORBIDDEN

| From → To | Reason |
|---|---|
| `EntityDescriptor` → `FormDescriptor` | Both consume Schema independently |
| `EntityDescriptor` → `WorkflowDescriptor` | No direct dependency |
| `EntityDescriptor` → `CapabilityDescriptor` | Entity doesn't know callers |
| `CapabilityDescriptor` → `WorkflowDescriptor` | Capability doesn't own orchestration |
| `CapabilityDescriptor` → `FormDescriptor` | Capability has no UI |
| `CapabilityDescriptor` → `HumanTaskDescriptor` | Separate concerns |
| `CapabilityDescriptor` → `AgentToolDescriptor` | Capability doesn't know consumers |
| `CapabilityDescriptor` → `DynamicApiDescriptor` | Capability doesn't know consumers |
| `WorkflowStep` → `ApplicationService` (direct) | Must go through Capability |
| `WorkflowStep` → `FormDescriptor` (direct) | Must go through HumanTask |
| `FormDescriptor` → `CapabilityDescriptor` | Form is pure UI metadata |
| `FormDescriptor` → `HumanTaskDescriptor` | Form doesn't know who uses it |
| `HumanTaskDescriptor` → `WorkflowDescriptor` | HumanTask doesn't own orchestration |
| `AgentToolDescriptor` → `ApplicationService` (direct) | Must go through Capability |
| `DynamicApiDescriptor` → `ApplicationService` (direct) | Must go through Capability |

---

## 6. Capability Execution Pipeline (The Event Semantics Layer)

Every Capability invocation — regardless of trigger source (HTTP, Workflow, Agent, BackgroundJob) — enters a unified pipeline:

```text
Trigger (DynamicAPI | Workflow | Agent | BackgroundJob | MCP)
    │
    ▼
Resolve CapabilityDescriptor by name
    │
    ▼
Build ExecutionContext:
    ├── TenantId
    ├── UserId / SystemPrincipal
    ├── CorrelationId
    ├── CausationId
    ├── IdempotencyKey
    ├── CapabilityName
    ├── CapabilityDefinitionHash
    ├── Input payload
    └── StartedAt
    │
    ▼
Capability Pipeline:
    1. Authorization (permission check)
    2. Input validation (against InputSchema)
    3. Idempotency check (duplicate detection)
    4. Unit of Work begin
    5. Handler invocation
    6. Unit of Work commit/rollback
    7. Audit emission (who, what, result, duration, error)
    8. Metrics emission
    │
    ▼
Return ExecutionResult:
    ├── Status (Success/Failure/Timeout)
    ├── Output payload
    ├── Duration
    ├── ErrorCode (if failed)
    └── AuditRecordId
```

**Key principle**: The pipeline is the same regardless of the trigger. DynamicAPI, Workflow, Agent, and BackgroundJob all enter the same pipeline — they only differ in how they receive the initial request and how they deliver the result.

---

## 7. Exposure Layer

### 7.1 DynamicApiDescriptor (Capability → HTTP)

DynamicApiDescriptor is a **projection view** of CapabilityDescriptor. It adds:

| Field | Detail |
|---|---|
| `CapabilityName` | ref to CapabilityDescriptor |
| `HttpMethod` | GET (Query), POST (Draft/Command) |
| `RoutePattern` | derived from capability name |
| `ResponseEnvelope` | standard CrestCreates response wrapper |

DynamicAPI does NOT define its own Input/Output schema — it inherits from Capability.

### 7.2 AgentToolDescriptor (Capability → LLM Tool)

AgentToolDescriptor is a **projection view** of CapabilityDescriptor. It adds:

| Field | Detail |
|---|---|
| `CapabilityName` | ref to CapabilityDescriptor |
| `Description` | LLM-facing description of what the tool does |
| `ToolCallMode` | Auto / RequiresApproval / Disabled |
| `BudgetLimit` | max invocations per agent execution |

AgentToolDescriptor does NOT define its own Input/Output schema — it inherits from Capability.

### 7.3 MCPToolDescriptor (Capability → MCP Tool)

Same pattern as AgentToolDescriptor, for MCP (Model Context Protocol) exposure.

---

## 8. What This Model Prevents

| Anti-Pattern | How this model prevents it |
|---|---|
| `CapabilityKind.Workflow` | Workflow is a separate descriptor; CapabilityKind is only Query/Draft/Command |
| `CapabilityKind.HumanTask` | HumanTask is a separate descriptor delegating to Form |
| `ApproveTaskCapability` | Approval is a HumanTask, not a Capability |
| `StartWorkflowCapability` | Sub-workflows use `SubWorkflowTarget`, not a special Capability |
| `FormTarget` in WorkflowStep | WorkflowStep binds to HumanTask, not Form directly |
| `AgentToolKind.Form` | Agent tools are always Capability projections; human interaction goes through HumanTask |
| Direct `ApplicationService` call from Workflow | Must go through Capability |
| 4-5 parallel schema systems | SchemaDescriptor is the single source of truth for data shape |

---

## 9. Existing Code Impact

### 9.1 What already exists

- `DynamicApiDescriptors.cs` — `DynamicApiServiceDescriptor`, `DynamicApiActionDescriptor`, `DynamicApiParameterDescriptor`, `DynamicApiReturnDescriptor`, `DynamicApiPermissionMetadata`, `DynamicApiRegistry`.
- `DynamicApiEndpointDescriptor` (record) — describes a single endpoint.
- `[Entity]` attribute and entity base class hierarchy.
- `[CrestService]` attribute for Application Services.
- Permission system: `IPermissionChecker`, `PermissionDefinition`, `IEntityPermissions`.
- Build-time MSBuild tasks for module scanning and code generation.

### 9.2 What changes

- **New projects to add:**
  - `CrestCreates.Schema.Abstractions` / `CrestCreates.Schema` — SchemaDescriptor and schema registry.
  - `CrestCreates.Capability.Abstractions` / `CrestCreates.Capability` — CapabilityDescriptor, Capability Pipeline, ExecutionContext.
  - `CrestCreates.Form.Abstractions` / `CrestCreates.Form` — FormDescriptor.
  - `CrestCreates.HumanTask.Abstractions` / `CrestCreates.HumanTask` — HumanTaskDescriptor, task lifecycle.
  - `CrestCreates.Workflow.Abstractions` / `CrestCreates.Workflow` — WorkflowDescriptor, WorkflowStep, InteractionTarget.
  - `CrestCreates.AgentRuntime` (already planned) — AgentToolDescriptor as Capability projection.

- **Existing projects to evolve:**
  - `CrestCreates.DynamicApi` — refactor `DynamicApiDescriptors` to reference `CapabilityDescriptor` rather than duplicating metadata. The existing descriptor classes become Capability projections.
  - `CrestCreates.CodeGenerator` / `CrestCreates.BuildTasks` — add source generators for SchemaDescriptor (from Entity), CapabilityDescriptor (from `[CrestService]` methods), and FormDescriptor.

### 9.3 Migration path

Phase 1: Introduce SchemaDescriptor and CapabilityDescriptor as new abstractions. Existing DynamicApiDescriptors continue to work — they are internally mapped to CapabilityDescriptors.

Phase 2: Refactor DynamicApi to consume CapabilityDescriptor directly. The existing descriptor types become projection views.

Phase 3: Introduce FormDescriptor, HumanTaskDescriptor, WorkflowDescriptor.

Phase 4: Introduce AgentToolDescriptor and MCPToolDescriptor as additional Capability projections.

---

## 10. Design Decisions Summary

| # | Decision |
|---|---|
| 1 | SchemaDescriptor is the single source of truth for data shape — no FormSchema, WorkflowSchema, ApiSchema, ToolSchema |
| 2 | CapabilityDescriptor answers only three core questions: What? Input? Output? — plus Kind, Permission, RiskLevel |
| 3 | CapabilityKind is limited to Query, Draft, Command — Workflow and HumanTask are NOT CapabilityKinds |
| 4 | FormDescriptor = Schema + UI metadata — pure presentation concern, not a business action |
| 5 | HumanTaskDescriptor is the business action for human interaction — Form is its UI delegate |
| 6 | WorkflowStep binds to InteractionTarget (Capability | HumanTask | SubWorkflow), never to ApplicationService or Form directly |
| 7 | DynamicApiDescriptor, AgentToolDescriptor, MCPToolDescriptor are projection views of CapabilityDescriptor |
| 8 | Every Capability invocation enters the unified Capability Execution Pipeline regardless of trigger source |
| 9 | Entity is a Schema source, not a participant in the Capability/Workflow chain |
| 10 | Entity → Form, Entity → Workflow, Entity → Capability are all forbidden dependencies |

---

## 11. Future Considerations

- **Low-code form builder**: Because Form depends only on Schema, a low-code form builder only needs SchemaDescriptor + FormDescriptor — it does not need to know about Capability, Workflow, or Entity.
- **Workflow engine integration (e.g., Elsa)**: The WorkflowDescriptor and InteractionTarget abstractions serve as the CrestCreates-native workflow model. External engines can be adapted behind these abstractions.
- **Approval flow complexity**: HumanTaskDescriptor can evolve to support multi-level approval, co-sign, countersign, and delegation patterns without affecting Capability or Workflow.
- **Observability**: The unified Capability Pipeline provides a single point for metrics, tracing, and auditing — every business action, regardless of trigger, is observable through the same mechanism.