# Accountability Runtime Foundation — Usage Guide

> For host authors, producer developers, sanitizer owners, and durable sink providers.
> **Status:** Phase 9a implemented; durable providers are Phase 9b.

---

## 1. Quick Start

Register the Foundation before any AuditLogging, Capability, or Workflow
producer:

```csharp
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.InMemory;

builder.Services.AddAccountability(options =>
{
    options.WriteTimeout = TimeSpan.FromSeconds(5);
    options.RequireAtLeastOneSink = true;
});

// Explicit development/test sink.
builder.Services.AddAuditSink<InMemoryAuditSink>();
```

`InMemoryAuditSink` is single-process and volatile. It is suitable for tests,
development, and acceptance fixtures, not production durability.

The library default for `RequireAtLeastOneSink` is `false`; first-party
production hosts should set it to `true`. Configuration uses standard Options
composition, so a host may call `Configure<AccountabilityOptions>()` after a
platform preset instead of depending on first-registration-wins behavior.

### Result semantics

```csharp
AuditRecordResult result = await recorder.RecordAsync(envelope, cancellationToken);

if (result.IsAccepted)
{
    // At least one sink returned Accepted or Duplicate.
    var existingRecordId = result.AuditId;
}
```

Do not infer existence from `result.Status == AuditRecordStatus.Recorded`.
`IsAccepted` is derived exclusively from sink results.

---

## 2. Standard Web Host

`AddCrestWeb()` and `UseCrestWeb()` register and compose the Accountability HTTP
mainline. Do not also enable legacy `UseAuditLogging()`.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddCrestWeb(options => options.UseOpenIddict(true));

builder.Services.Configure<AccountabilityOptions>(accountability =>
{
    accountability.RequireAtLeastOneSink = true;
    accountability.WriteTimeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddAuditSink<MyDurableAuditSink>();

var app = builder.Build();
app.UseCrestWeb();
app.MapCrestWeb();
```

`AddCrestWeb()` registers the Foundation. The Host composes
`AccountabilityOptions` through standard Options after the preset; repeated
`AddAccountability()` calls are not used as an ordering-based override.

### Manual HTTP composition

Hosts that do not use `UseCrestWeb()` must preserve this ordering:

```csharp
app.UseCrestRequestLogging();
app.UseAccountabilityHttpTerminalObserver();
app.UseExceptionHandling();
app.UseRouting();
app.UseMultiTenancy();
app.UseAuthentication();
app.UseAccountabilityHttpOperationScope();
app.UseTenantBoundary();
app.UseAuthorization();
```

Why two Accountability middleware?

- Terminal Observer must be outside global exception handling so it sees the
  converted final response.
- Operation Scope must be after tenant resolution and authentication so child
  facts receive trusted Actor/Tenant context.

Moving one middleware to satisfy both requirements breaks one of the two
contracts.

---

## 3. Capability and Workflow Hosts

Capability and Workflow are Accountability producers. Registering their runtime
without the Foundation is a startup composition error:

```csharp
builder.Services.AddAccountability(options =>
    options.RequireAtLeastOneSink = true);
builder.Services.AddAuditSink<MyDurableAuditSink>();

builder.Services.AddCapabilityRuntime();
builder.Services.AddWorkflowEngine();
```

`AddCapabilityPipeline()` also installs the producer-owned startup validator.
Using the lower-level pipeline registration does not bypass the Foundation
requirement.

For resolved executions:

```csharp
CapabilityExecutionResult result =
    await dispatcher.DispatchAsync(capabilityId, request, cancellationToken);

string? auditRecordId = result.AuditRecordId;
```

`AuditRecordId` is null when no sink Accepted or returned Duplicate. A generated
candidate AuditId is not exposed as a persisted record.

Workflow facts are emitted only after the corresponding state save succeeds.
Cancelling the original business token after commit does not suppress the
bounded post-commit notification attempt.

---

## 4. Method Accountability with AuditedMo

Add `[AuditedMo]` to the real method in the assembly that is woven by
Rougamo/Fody:

```csharp
using CrestCreates.AuditLogging.Interceptors;

public sealed class OrderApplicationService
{
    [AuditedMo("orders.submit")]
    public async Task<OrderResult> SubmitAsync(
        SubmitOrderRequest request,
        CancellationToken cancellationToken)
    {
        // Business implementation.
    }
}
```

The assembly containing `SubmitAsync` must contain the Rougamo weaver
configuration. Adding `Rougamo.Fody` only to the final Host does not weave a
method located in a referenced Application assembly.

`[AuditedMo]` does not capture parameters, results, or Exception objects. Its
legacy `includeParameters` and `includeResult` constructor parameters are
retained for source compatibility but do not enable unsafe capture.

Method recording is post-fact:

- recording failure never replaces a successful method result;
- recording failure never replaces the original method exception;
- HTTP and method invocation remain separate facts.

---

## 5. Creating a Custom Producer

Use stable semantic Kinds and trusted identities. Do not copy Actor/Tenant data
from an untrusted request payload.

```csharp
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Recording;

var envelope = new AuditEnvelope
{
    AuditId = auditIdentity.CreateAuditId(),
    OccurredAt = timeProvider.GetUtcNow(),
    TenantId = trustedTenantId,
    CorrelationId = correlationId,
    CausationId = causingOperationId,
    ParentAuditId = enclosingAuditId,
    Actor = new AuditActor
    {
        Kind = "integration",
        Id = trustedIntegrationId
    },
    Action = new AuditAction
    {
        Kind = "integration.sync",
        Name = "catalog.refresh"
    },
    Target = new AuditTarget
    {
        Kind = "catalog",
        Id = catalogId
    },
    Outcome = new AuditOutcome
    {
        Status = "succeeded"
    },
    Runtime = new AuditRuntimeContext
    {
        InvocationSource = "integration",
        ExecutionId = executionId,
        References = []
    },
    Descriptors = AuditDescriptorContext.Empty,
    Evidence = [],
    Tags = AuditTagMap.Empty
};

AuditRecordResult result =
    await recorder.RecordAsync(envelope, cancellationToken);
```

### Relationship checklist

- Use `CausationId` only for the direct causing operation/event/decision.
- Use `ParentAuditId` only for an enclosing Accountability fact.
- Use `PreviousAuditId` only for the previous fact in the same lifecycle.
- Put entity identities such as Workflow/HumanTask instances in Runtime
  References, not in `CausationId`.
- Keep trace/span/request IDs in Runtime observation fields.

### Time semantics

For operations with duration:

```text
OccurredAt = terminal outcome observation time
Runtime.Duration = elapsed execution duration
```

Do not choose start time in one adapter and completion time in another.

---

## 6. Payload and Data Sanitization Rules

Payload and DataSnapshot are optional and default-deny. Prefer leaving them null
unless investigation value justifies a stable typed contract.

Register one owner for each stable Kind:

```csharp
public sealed class CatalogPayloadSanitizationRule
    : IAuditPayloadSanitizationRule
{
    public string Kind => "catalog.summary";
    public int RuleVersion => 1;

    public AuditPayload Sanitize(AuditPayload payload)
    {
        // Return the same Kind and Version with minimized safe Data.
        return payload with { Data = Minimize(payload.Data) };
    }
}

builder.Services.AddSingleton<
    IAuditPayloadSanitizationRule,
    CatalogPayloadSanitizationRule>();
```

Rules must:

- be deterministic and side-effect free;
- preserve Payload Kind and Version;
- preserve artifact Kind;
- return non-null output;
- stay within candidate and safe-output size limits.

Duplicate Kind ownership fails startup. Unknown Kinds and rule failures reject
the candidate before any sink is called.

Sanitizers may minimize presentation/data fields but cannot rewrite Actor,
Action, Target, Outcome status/code, relationships, Runtime identities,
descriptor references, or evidence references.

---

## 7. Implementing a Durable Sink

```csharp
public sealed class DatabaseAuditSink : IAuditSink
{
    public string Id => "database";

    public ValueTask<AuditSinkWriteResult> WriteAsync(
        AuditEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        // 1. Conditional insert by envelope.AuditId.
        // 2. If it exists, load the existing structured Integrity.
        // 3. Return Accepted, Duplicate, or Conflict exactly.
        // 4. Never mutate the safe Envelope.
        throw new NotImplementedException();
    }
}
```

Required behavior:

```text
new AuditId
    → Accepted

same AuditId + same CanonicalHash
    → Duplicate

same AuditId + different CanonicalHash
    → Conflict + ExistingIntegrity
```

`ExistingIntegrity` must be null for Accepted and Duplicate. The returned
`SinkId`, `AuditId`, and current `Integrity` must match the invocation. Recorder
contract validation turns mismatches into provider failures.

The method must return its `ValueTask` promptly. Do not use `.Wait()`, `.Result`,
`Thread.Sleep`, or `GetAwaiter().GetResult()` before returning the awaitable.

### Reuse the shared contract cases

Provider test projects reference:

```text
tests/Shared/CrestCreates.Accountability.Testing
```

Implement `IAuditSinkContractDriver`, then expose the runner-free
`AuditSinkContractCases` through the provider test framework's own `[Fact]` or
`[Theory]` methods.

Do not reference:

- `CrestCreates.Accountability.Tests`;
- the concrete `InMemoryAuditSink`;
- xUnit from the shared Testing library.

---

## 8. Cancellation and Post-commit Recording

`IAuditRecorder.RecordAsync(envelope, cancellationToken)` treats the supplied
token as caller cancellation for that recorder attempt.

Use the real caller token when the caller still owns an uncommitted operation:

```csharp
await recorder.RecordAsync(envelope, cancellationToken);
```

After a business result or state transition is committed, do not pass an already
cancelled business token:

```csharp
await recorder.RecordAsync(envelope, CancellationToken.None);
```

The recorder still applies the configured total `WriteTimeout`. Caller
cancellation is propagated; internal write timeout is returned as stable sink
timeout failures.

---

## 9. Reading AuditRecordResult

| Collection/property | Meaning |
|---|---|
| `SinkResults` | Accepted, Duplicate, and Conflict provider results |
| `SinkFailures` | Provider throw, timeout, unavailable, or contract mismatch |
| `Issues` | Candidate validation, sanitizer rejection, or safe-snapshot issue |
| `RecordHash` | Structured integrity of the safe fact |
| `ProcessedAt` | Completion time of this recorder attempt |
| `IsAccepted` | At least one Accepted or Duplicate sink result |

Conflict is an integrity signal, not a network/provider failure. Rejection is a
contract/policy outcome, not a sink failure.

---

## 10. Common Mistakes

### Enabling a producer without the Foundation

```text
Symptom: startup composition failure
Fix: call AddAccountability() and register/configure sinks before Host start
```

### Required sink but no provider

```text
Symptom: ACCOUNTABILITY_SINK_REQUIRED
Fix: register at least one IAuditSink or disable the requirement explicitly for
     a library/test scenario
```

### Invalid timeout

```text
Symptom: ACCOUNTABILITY_WRITE_TIMEOUT_INVALID
Fix: use a finite positive timeout supported by CancellationTokenSource
```

### HTTP Actor is anonymous for authenticated requests

```text
Cause: Operation Scope runs before Authentication
Fix: preserve the documented two-middleware ordering
```

### Tenant/Auth exception escapes the global error contract

```text
Cause: ExceptionHandling was moved inside Routing/MultiTenancy/Authentication
Fix: keep Terminal Observer outside ExceptionHandling and Operation Scope after
     Authentication
```

### Duplicate replay is not accepted

```text
Cause: sink returned ExistingIntegrity for Duplicate or returned mismatched IDs
Fix: ExistingIntegrity is Conflict-only; reuse the shared sink contract cases
```

### Payload is rejected

```text
Cause: no typed sanitization rule owns the Payload/Artifact Kind
Fix: register one deterministic rule, or remove unnecessary data capture
```

---

## 11. Accountability vs Agent Governance Audit

`IAgentToolGovernanceAuditor` is a required pre-dispatch/finalization control
protocol. It may block dispatch, fence a logical invocation, and participate in
reconciliation.

`IAuditRecorder` records post-fact responsibility claims and must not replace
that governance protocol. An Agent-triggered Capability can therefore produce:

1. required governance decision/finalization records; and
2. a separate post-fact `capability.execute` Accountability fact.

Do not use a best-effort Accountability sink to satisfy a Required Agent Tool
governance checkpoint.

Architecture details:
`docs/Feature/Accountability/arch-design.md`.
