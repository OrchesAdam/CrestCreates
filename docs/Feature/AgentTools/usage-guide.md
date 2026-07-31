# Agent Tool Projection — Usage Guide

Phase 8f exposes explicitly authored Capabilities to trusted Agent runtimes. It
does not provide a planner or provider SDK adapter. Discovery and invocation are
provider-neutral and always execute through the captured Capability Dispatcher
mainline.

## 1. Author a generated Tool

Define the Capability and its exact input/output DTOs as usual, then add one
top-level static partial Tool container:

```csharp
[AgentToolSpecs]
public static partial class OrderAgentTools
{
    [AgentToolSpec(
        "orders.lookup",
        CapabilityVersion = 1,
        InputType = typeof(LookupOrderInput),
        OutputType = typeof(LookupOrderOutput),
        ToolName = "orders.lookup",
        Title = "Look up order",
        Description = "Returns one order visible to the current user.",
        SelectionPolicy = AgentToolSelectionPolicy.AutomaticAllowed,
        BudgetCategory = "order-read",
        CostUnits = 1,
        MaxCallsPerExecution = 10,
        ApprovalMode = AgentToolApprovalMode.None,
        AuditMode = AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { "order-agent" })]
    public sealed class Lookup;
}
```

The Source Generator emits the descriptor provider, exact input binder, exact
output serializer, and JSON contract registrations. Runtime scanning,
reflection serialization, dictionary payload fallback, and direct Handler
invocation are not supported.

`Title`, description, selection policy, roles, approval, budget, audit, risk,
and side-effect classification affect the Tool contract hash. Changing them is
a governed contract change.

## 2. Register the runtime

The application owns its source-generated JSON context:

```csharp
[JsonSerializable(typeof(LookupOrderInput))]
[JsonSerializable(typeof(LookupOrderOutput))]
internal partial class AgentToolJsonContext : JsonSerializerContext;

services.AddCrestAgentTools(options =>
    options.SerializerOptions.TypeInfoResolver = AgentToolJsonContext.Default);
```

For every Active Tool, the Host must also register:

```csharp
IAgentExecutionContextAccessor
IAgentToolInvocationGate
IAgentToolInvocationLeaseAbandoner
IAgentToolBudgetGate
IAgentToolGovernanceAuditor
```

`IAgentToolInvocationGate` and `IAgentToolInvocationLeaseAbandoner` must resolve
to the same durable backing store. For the development in-memory gate, register
one instance under both interfaces:

```csharp
services.AddSingleton<DevelopmentInMemoryAgentToolInvocationGate>();
services.AddSingleton<IAgentToolInvocationGate>(sp =>
    sp.GetRequiredService<DevelopmentInMemoryAgentToolInvocationGate>());
services.AddSingleton<IAgentToolInvocationLeaseAbandoner>(sp =>
    sp.GetRequiredService<DevelopmentInMemoryAgentToolInvocationGate>());
```

The development gate retains terminal receipts for the process lifetime and
loses them on restart. Production adapters must retain per-Attempt receipts
for at least the Audit reconciliation window, with an explicit TTL or archive
policy.

`AddCrestAgentTools()` installs the fail-closed approval gate. A Host verifier
must be registered when required or policy-driven calls can be approved. The
runtime intentionally installs no permissive invocation, budget, or audit
default; missing governance infrastructure fails Host startup.

The existing scoped `ICurrentUser` and `ITenantContext` remain authoritative
for user and tenant identity. Never copy these values from model-generated Tool
arguments.

### 2.1 Governance Audit and Accountability are different contracts

An Agent Tool Host that dispatches through the Capability Runtime also registers
the Phase 9a Accountability Foundation:

```csharp
services.AddAccountability(options =>
    options.RequireAtLeastOneSink = true);
services.AddAuditSink<MyAuditSink>();
services.AddCapabilityRuntime();
services.AddCrestAgentTools(...);
```

`IAgentToolGovernanceAuditor` remains the required pre-dispatch/finalization
control protocol. It may block execution and fence reconciliation state.
`IAuditRecorder` is a separate post-fact responsibility recorder used by the
Capability execution. A best-effort Accountability sink must never be used to
satisfy `AgentToolAuditMode.Required`.

See `docs/Feature/Accountability/arch-design.md`.

## 3. Establish trusted Agent context

The Host or future Agent orchestrator creates the scoped context:

```csharp
new AgentExecutionContext
{
    ExecutionId = executionId,
    InvocationId = invocationId,
    AgentId = agentId,
    AgentRoles = roles,
    CallOrigin = AgentToolCallOrigin.AutomaticSelection,
    CausationId = causationId
};
```

`SelectionPolicy` describes whether a Tool may be selected automatically;
`CallOrigin` records how this call was actually selected. Unknown values fail
closed. A role or selection denial is returned as UnknownTool to avoid an
existence oracle.

Provider adapters use `IAgentToolCatalog` for discovery and `IAgentToolInvoker`
for calls. They may translate provider request/response shapes, but must not
bypass these services or invoke Capability Handlers directly.

## 4. Logical calls, retries, and reconciliation

`ExecutionId + InvocationId` identifies one logical call within the trusted
tenant/user/Agent scope. Its first accepted canonical fingerprint permanently
binds Tool, Capability, Schemas, arguments, roles, and CallOrigin.

- an identical Completed retry returns the stored safe outcome without another
  approval, reservation, dispatch, or audit;
- a changed fingerprint is `InvocationConflict`;
- an active attempt is `InProgress`;
- a `CompletionPending` attempt remains `InProgress` until Required governance
  finalization is accepted and Completed replay is explicitly published;
- an uncertain post-dispatch result is `InvocationIndeterminate` and must not be
  retried automatically;
- a pre-dispatch release remains fenced as `ReleasePending` until its terminal
  audit is confirmed and `PublishRelease` succeeds; only then may the same
  fingerprint acquire a new lease and reservation.

Budget and invocation terminal state are independent. For example, a business
call may have consumed its budget while a required post-dispatch audit failure
leaves the logical invocation Indeterminate. Hosts must route Indeterminate
records to reconciliation instead of treating them as failed-before-execution.
If Required Audit finalization loses its response, the runtime queries the
AuditId. A matching Completed record continues to publication; an unconfirmed
record leaves CompletionPending fenced rather than guessing a terminal state.
Published Completed is immutable and cannot be downgraded to Indeterminate.
BestEffort tolerates an unavailable or unconfirmed audit checkpoint, but a
confirmed contradictory Indeterminate finalization still fences the invocation.
Audit confirmation uses `OutcomeHash`, a data-minimizing integrity digest (not
a confidentiality mechanism); adapters do not need to persist the full
structured output merely to confirm a response-loss retry.
Budget reservation/finalization responses that are uncertain are represented as
`Unknown` in the governance checkpoint and keep the logical invocation fenced.
Role, selection, schema, approval, and known budget denials are recorded through
the governance decision-audit contract without inventing an Approval or Budget
reservation. A Required audit policy also covers these decision records; if the
record cannot be accepted, the call returns a stable audit-failure outcome and
does not dispatch. Role/Selection denials retain the external `UnknownTool`
mask even when their Required Decision Audit is unavailable. Malformed budget
responses retain any observed reservation for reconciliation.

## 5. Development adapters

`DevelopmentInMemoryAgentToolInvocationGate`,
`DevelopmentInMemoryAgentToolBudgetGate`,
`DevelopmentInMemoryAgentToolGovernanceAuditor`, and the development approval
evidence verifier are volatile single-process adapters for tests and explicit
single-node development use.

They do not survive restart, coordinate across nodes, or provide distributed
exactly-once guarantees. Production Hosts must supply durable adapters with
atomic evidence claims, compare-and-swap fencing, persistent budget settlement,
governance audit durability, and Indeterminate reconciliation.

Design details: `docs/superpowers/specs/2026-07-16-phase-8f-agent-tool-projection-design.md`.
