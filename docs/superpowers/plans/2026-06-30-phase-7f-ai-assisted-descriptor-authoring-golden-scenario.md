# Phase 7f AI-assisted Descriptor Authoring Golden Scenario Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the approved Phase 7f golden scenario proving deterministic AI-assisted descriptor authoring through draft review, final evidence binding, activation, and runtime execution against an approved final inventory.

**Architecture:** Keep implementation sample-level. Add a narrow authoring adapter and orchestration around the existing Agent Memory, DescriptorDraft, Agent Control Plane, activation, and Company Certification sample. Do not redesign batch draft core, activation contracts, or runtime registry hot reload; runtime proof uses a fresh host built from the approved final inventory.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, in-memory DescriptorDraft / Agent Memory / Agent Control Plane services, existing Company Certification sample runtime.

## Global Constraints

- The approved spec is `docs/superpowers/specs/2026-06-30-phase-7f-ai-assisted-descriptor-authoring-golden-scenario-design.md`.
- This is sample-level orchestration, not core redesign.
- Draft set handling is all-or-block.
- Runtime proof must use a fresh host built from approved final inventory.
- Do not add a real LLM provider, HTTP/DynamicApi/MCP surface, UI, production prompt management, durable authoring provider, runtime registry hot reload, or framework-level batch draft review.
- Reuse existing `DescriptorDraft`, `IDescriptorDraftReviewService`, review report/fix proposal, activation request service, activation review orchestrator, and `IRuntimeActivationGate`.
- The fake authoring agent consumes only `AgentAuthoringContext`.
- Memory is non-authoritative and must not become activation evidence.
- Prefer using the workflow update draft as the activation subject while binding the complete final proposed inventory.
- New tests should validate the official generated/governed path, not legacy or bypass paths.

---

## File Structure

### Create

- `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/IDescriptorAuthoringAgent.cs`  
  Sample-level authoring boundary. Consumes `AgentAuthoringContext`, produces `DescriptorAuthoringResult`.

- `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorAuthoringPlan.cs`  
  Deterministic plan item list for authored changes.

- `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorAuthoringResult.cs`  
  Authoring result carrying plan, draft set, diagnostics, and context identity.

- `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorDraftSet.cs`  
  Ordered wrapper around existing `CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft` instances.

- `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/FakeCompanyCertificationAuthoringAgent.cs`  
  Deterministic sample authoring agent for the finance-review scenario.

- `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioRunner.cs`  
  End-to-end sample-level orchestration for intent -> context -> draft set -> final decision -> activation -> fresh runtime proof.

- `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioReport.cs`  
  Report for the Phase 7f scenario, including activated inventory/evidence hashes and runtime proof fields.

- `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationDraftSetReviewResult.cs`  
  Scenario-level result aggregating per-draft review results plus final inventory and final decision.

- `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationDescriptorCloner.cs`  
  AOT-safe descriptor clone helpers shared by change scenarios, fake authoring, and fresh runtime host construction.

- `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`  
  Phase 7f tests.

### Modify

- `samples/CrestCreates.Samples.DescriptorControlPlane/CrestCreates.Samples.DescriptorControlPlane.csproj`  
  Add project references for Agent Memory, DescriptorDraft, and Agent Control Plane packages needed by the sample orchestration.

- `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationChangeScenarios.cs`  
  Replace private clone helper usage with `CompanyCertificationDescriptorCloner`.

- `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioHost.cs`  
  Add constructor overload accepting an explicit runtime inventory and optional store. Register registries from that inventory. Register DescriptorDraft, Agent Memory, Agent Control Plane, and activation services required by the authoring runner.

- `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioRunner.cs`  
  Generalize HumanTask completion loop and populate sequence/report fields for two-review runtime proof.

- `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioReport.cs`  
  Add activated inventory/evidence and multi-HumanTask proof fields.

- `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationGoldenScenarioTests.cs`  
  Keep existing baseline tests passing after host/report changes.

---

### Task 1: Shared Descriptor Cloning and Inventory Host Input

**Files:**
- Create: `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationDescriptorCloner.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationChangeScenarios.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioHost.cs`
- Test: `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationGoldenScenarioTests.cs`

**Interfaces:**
- Produces: `CompanyCertificationDescriptorCloner.CopyAllDescriptors(): IReadOnlyList<IDescriptor>`
- Produces: `CompanyCertificationDescriptorCloner.CopyDescriptor(IDescriptor descriptor): IDescriptor`
- Produces: `CompanyCertificationGoldenScenarioHost(IReadOnlyList<IDescriptor>? runtimeInventory = null, InMemoryCompanyCertificationStore? store = null)`
- Consumes: existing descriptor model types from Company Certification sample.

- [ ] **Step 1: Write failing host inventory test**

Add this test to `CompanyCertificationGoldenScenarioTests`:

```csharp
[Fact]
public void GoldenScenarioHost_Should_Build_RuntimeRegistries_From_ExplicitInventory()
{
    var inventory = CompanyCertificationDescriptorCloner.CopyAllDescriptors()
        .Where(d => d.Id != "ht_review_company_certification")
        .ToList();

    var original = (HumanTaskDescriptor)CompanyCertificationDescriptorCloner
        .CopyDescriptor(CompanyCertificationDescriptors.ReviewCompanyCertification);

    var financeTask = new HumanTaskDescriptor
    {
        Id = "ht_finance_review_company_certification",
        Name = "humantask.FinanceReviewCompanyCertification",
        Version = original.Version,
        State = original.State,
        SupersededById = original.SupersededById,
        Interaction = original.Interaction,
        InputSchema = original.InputSchema,
        OutputSchema = original.OutputSchema,
        AssigneeStrategy = original.AssigneeStrategy,
        Timeout = original.Timeout,
        Permissions = "CompanyCertification.FinanceReview",
        Outcomes = original.Outcomes
    };
    inventory.Add(financeTask);

    using var host = new CompanyCertificationGoldenScenarioHost(inventory);
    using var scope = host.CreateScope();

    var registry = scope.ServiceProvider.GetRequiredService<IHumanTaskRegistry>();

    registry.GetById("ht_review_company_certification").Should().BeNull();
    registry.GetById("ht_finance_review_company_certification").Should().NotBeNull();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter GoldenScenarioHost_Should_Build_RuntimeRegistries_From_ExplicitInventory
```

Expected: FAIL because `CompanyCertificationDescriptorCloner` and the host constructor overload do not exist.

- [ ] **Step 3: Add descriptor cloner**

Create `CompanyCertificationDescriptorCloner.cs` by moving the existing explicit clone logic out of `CompanyCertificationChangeScenarios`. Keep all cloning property-by-property; do not use reflection. Include:

```csharp
public static class CompanyCertificationDescriptorCloner
{
    public static IReadOnlyList<IDescriptor> CopyAllDescriptors()
        => CompanyCertificationDescriptorInventory.AllDescriptors()
            .Select(CopyDescriptor)
            .ToList()
            .AsReadOnly();

    public static IDescriptor CopyDescriptor(IDescriptor descriptor)
        => descriptor switch
        {
            SchemaDescriptor schema => CopySchema(schema),
            FormDescriptor form => CopyForm(form),
            CapabilityDescriptor capability => CopyCapability(capability),
            HumanTaskDescriptor humanTask => CopyHumanTask(humanTask),
            WorkflowDescriptor workflow => CopyWorkflow(workflow),
            EventDescriptor evt => CopyEvent(evt),
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor), descriptor.GetType(), "Unsupported descriptor type.")
        };

    public static WorkflowStep CopyWorkflowStep(WorkflowStep step) => new()
    {
        Id = step.Id,
        Name = step.Name,
        Target = step.Target switch
        {
            CapabilityTarget capability => new CapabilityTarget { Capability = capability.Capability },
            HumanTaskTarget humanTask => new HumanTaskTarget { HumanTask = humanTask.HumanTask },
            SubWorkflowTarget subWorkflow => new SubWorkflowTarget { SubWorkflow = subWorkflow.SubWorkflow },
            _ => throw new ArgumentOutOfRangeException(nameof(step), step.Target.GetType(), "Unsupported workflow target type.")
        },
        Condition = step.Condition,
        Transitions = step.Transitions.ToArray(),
        InputMapping = step.InputMapping,
        OutputMapping = step.OutputMapping,
        OnError = step.OnError
    };
}
```

Also include the existing explicit `CopySchema`, `CopyForm`, `CopyCapability`, `CopyHumanTask`, `CopyWorkflow`, `CopyEvent`, `CopySchemaField`, and related helpers from `CompanyCertificationChangeScenarios`.

- [ ] **Step 4: Modify change scenarios to consume cloner**

Replace internal `CopyAllDescriptors()` and `CopyWorkflowStep()` calls in `CompanyCertificationChangeScenarios` with:

```csharp
CompanyCertificationDescriptorCloner.CopyAllDescriptors()
CompanyCertificationDescriptorCloner.CopyWorkflowStep(...)
```

Remove duplicated private clone helpers only after all references are replaced.

- [ ] **Step 5: Add explicit inventory host constructor**

Change `CompanyCertificationGoldenScenarioHost` constructor to:

```csharp
public CompanyCertificationGoldenScenarioHost(
    IReadOnlyList<IDescriptor>? runtimeInventory = null,
    InMemoryCompanyCertificationStore? store = null)
{
    Store = store ?? new InMemoryCompanyCertificationStore();
    var inventory = runtimeInventory ?? CompanyCertificationDescriptorCloner.CopyAllDescriptors();
    var services = new ServiceCollection();
    services.AddSingleton(Store);
    RegisterRuntimeRegistries(services, inventory);
    RegisterRuntimeServices(services);
    RegisterControlPlaneServices(services);
    Provider = services.BuildServiceProvider(validateScopes: true);
}
```

Split existing constructor body into private methods:

```csharp
private static void RegisterRuntimeRegistries(IServiceCollection services, IReadOnlyList<IDescriptor> inventory)
private static void RegisterRuntimeServices(IServiceCollection services)
private static void RegisterControlPlaneServices(IServiceCollection services)
```

`RegisterRuntimeRegistries` filters the explicit inventory by concrete descriptor type and builds capability, HumanTask, and workflow registries from that inventory.

- [ ] **Step 6: Run tests**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "CompanyCertificationGoldenScenarioTests"
```

Expected: PASS. Existing baseline tests and the new explicit inventory test pass.

- [ ] **Step 7: Commit**

```bash
git add samples/CrestCreates.Samples.DescriptorControlPlane tests/Framework/Testing/CrestCreates.Samples.Tests
git commit -m "feat: allow company certification host inventory input"
```

### Task 2: Sample-level Authoring Contracts and Fake Agent

**Files:**
- Create: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/IDescriptorAuthoringAgent.cs`
- Create: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorAuthoringPlan.cs`
- Create: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorDraftSet.cs`
- Create: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/DescriptorAuthoringResult.cs`
- Create: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/FakeCompanyCertificationAuthoringAgent.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/CrestCreates.Samples.DescriptorControlPlane.csproj`
- Test: `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`

**Interfaces:**
- Consumes: `CrestCreates.Agent.Memory.Abstractions.AgentAuthoringContext`
- Produces: `Task<DescriptorAuthoringResult> IDescriptorAuthoringAgent.AuthorAsync(AgentAuthoringContext context, CancellationToken cancellationToken = default)`
- Produces: deterministic `DescriptorDraftSet` with one HumanTask create draft and one Workflow update draft.

- [ ] **Step 1: Add project references**

Add to sample `.csproj`:

```xml
<ProjectReference Include="../../src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj" />
<ProjectReference Include="../../src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj" />
<ProjectReference Include="../../src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/CrestCreates.Agent.ControlPlane.Abstractions.csproj" />
<ProjectReference Include="../../src/Runtime/Agent/CrestCreates.Agent.ControlPlane/CrestCreates.Agent.ControlPlane.csproj" />
<ProjectReference Include="../../src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CrestCreates.DescriptorDraft.Abstractions.csproj" />
<ProjectReference Include="../../src/Metadata/Draft/CrestCreates.DescriptorDraft/CrestCreates.DescriptorDraft.csproj" />
```

- [ ] **Step 2: Write failing fake authoring tests**

Create `CompanyCertificationAuthoringGoldenScenarioTests.cs` with:

```csharp
public sealed class CompanyCertificationAuthoringGoldenScenarioTests
{
    [Fact]
    public async Task FakeAuthoringAgent_Output_Is_Deterministic()
    {
        var agent = new FakeCompanyCertificationAuthoringAgent();
        var context = TestAuthoringContext();

        var first = await agent.AuthorAsync(context);
        var second = await agent.AuthorAsync(context);

        first.DraftSet.Drafts.Select(d => d.DraftId)
            .Should().Equal(second.DraftSet.Drafts.Select(d => d.DraftId));
        first.DraftSet.Drafts.Select(d => d.DescriptorId)
            .Should().Equal(second.DraftSet.Drafts.Select(d => d.DescriptorId));
    }

    [Fact]
    public async Task DraftSet_Creates_FinanceReview_HumanTask()
    {
        var result = await new FakeCompanyCertificationAuthoringAgent()
            .AuthorAsync(TestAuthoringContext());

        var draft = result.DraftSet.Drafts.Single(d => d.DescriptorId == "ht_finance_review_company_certification");

        draft.Operation.Should().Be(DescriptorDraftOperation.Create);
        draft.Payload.Should().BeOfType<HumanTaskDescriptorDraftPayload>();
    }

    [Fact]
    public async Task DraftSet_Updates_Workflow_With_FinanceReviewStep()
    {
        var result = await new FakeCompanyCertificationAuthoringAgent()
            .AuthorAsync(TestAuthoringContext());

        var draft = result.DraftSet.Drafts.Single(d => d.DescriptorId == "wf_company_certification");
        var payload = draft.Payload.Should().BeOfType<WorkflowDescriptorDraftPayload>().Subject;

        payload.Descriptor.Steps.Select(s => s.Id)
            .Should().Equal("step_submit", "step_review", "step_finance_review", "step_approve");
    }
}
```

Add a local `TestAuthoringContext()` helper using an empty non-authoritative
`AgentMemoryPack` and this explicit `MetadataContextPack` shape:

```csharp
private static AgentAuthoringContext TestAuthoringContext(
    bool memoryIsAuthoritative = false,
    string? memoryText = null)
{
    return new AgentAuthoringContext
    {
        Request = new AgentAuthoringRequest
        {
            TenantId = "tenant-company-certification",
            IntentText = Phase7fIntent
        },
        MetadataContextPack = new MetadataContextPack
        {
            Request = new MetadataContextPackRequest
            {
                Scope = MetadataContextPackScope.RuntimeScenario,
                TenantId = "tenant-company-certification",
                Intent = Phase7fIntent,
                FocusDescriptors = new[]
                {
                    new DescriptorRef("workflow", "wf_company_certification", 1)
                }
            },
            Descriptors = Array.Empty<MetadataContextPackDescriptorEntry>(),
            Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
            Summary = new MetadataContextPackSummary
            {
                TotalDescriptorCount = 0,
                DescriptorCountsByKind = new Dictionary<DescriptorKind, int>(),
                TotalRelationshipCount = 0,
                RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
                FocusRefs = new[]
                {
                    new DescriptorRef("workflow", "wf_company_certification", 1)
                },
                WasTruncated = false,
                TruncatedAtCount = null,
                TraversalDepthReached = 0
            },
            Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
        },
        MemoryPack = new AgentMemoryPack
        {
            TenantId = "tenant-company-certification",
            IsAuthoritative = memoryIsAuthoritative,
            Memories = string.IsNullOrWhiteSpace(memoryText)
                ? Array.Empty<AgentMemoryItem>()
                : new[]
                {
                    new AgentMemoryItem
                    {
                        TenantId = "tenant-company-certification",
                        MemoryId = "memory-conflict",
                        Content = memoryText,
                        Kind = AgentMemoryKind.Decision,
                        CanonicalContentHash = CreateTestCanonicalHash("memory-conflict-hash"),
                        Confidence = AgentMemoryConfidence.Low,
                        Status = AgentMemoryStatus.Active,
                        PromotedAt = DateTimeOffset.UnixEpoch,
                        IsAuthoritative = memoryIsAuthoritative
                    }
                }
        }
    };
}

private static CanonicalHash CreateTestCanonicalHash(string value) => new()
{
    Algorithm = "SHA-256",
    AlgorithmVersion = "sha256-canonical-json-v1",
    ArtifactKind = CanonicalHashArtifactNames.Descriptor,
    Scope = CanonicalHashScopeNames.InternalFull,
    Purpose = CanonicalHashPurposeNames.Definition,
    ContractVersion = "canonical-hash-v1",
    CanonicalShapeVersion = "phase7f-test-v1",
    Value = value
};
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "FakeAuthoringAgent_Output_Is_Deterministic|DraftSet_Creates_FinanceReview_HumanTask|DraftSet_Updates_Workflow_With_FinanceReviewStep"
```

Expected: FAIL because authoring types do not exist.

- [ ] **Step 4: Implement authoring contracts**

Create:

```csharp
public interface IDescriptorAuthoringAgent
{
    Task<DescriptorAuthoringResult> AuthorAsync(
        AgentAuthoringContext context,
        CancellationToken cancellationToken = default);
}

public sealed record DescriptorDraftSet
{
    public required string DraftSetId { get; init; }
    public required IReadOnlyList<DescriptorDraft> Drafts { get; init; }
}

public sealed record DescriptorAuthoringPlan
{
    public required string PlanId { get; init; }
    public required string IntentText { get; init; }
    public required IReadOnlyList<string> PlannedDescriptorIds { get; init; }
}

public sealed record DescriptorAuthoringResult
{
    public required DescriptorAuthoringPlan Plan { get; init; }
    public required DescriptorDraftSet DraftSet { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}
```

- [ ] **Step 5: Implement fake authoring agent**

`FakeCompanyCertificationAuthoringAgent.AuthorAsync` must:

- validate `context.Request.IntentText` equals or contains the approved intent;
- create `ht_finance_review_company_certification` by copying the existing review task and changing id, name, permissions;
- update `wf_company_certification` by copying existing workflow and inserting `step_finance_review` between `step_review` and `step_approve`;
- emit stable draft ids, for example `draft_company_certification_finance_review_humantask` and `draft_company_certification_workflow_finance_review`;
- set `AuthorKind = DescriptorDraftAuthorKind.Agent`, `AuthorId = "fake-company-certification-authoring-agent"`, `CreatedAt = DateTimeOffset.UnixEpoch`, `TenantId = context.Request.TenantId`, `Source = "Phase7fFakeAuthoringAgent"`, and stable `CorrelationId`.

- [ ] **Step 6: Run tests**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "FakeAuthoringAgent_Output_Is_Deterministic|DraftSet_Creates_FinanceReview_HumanTask|DraftSet_Updates_Workflow_With_FinanceReviewStep"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add samples/CrestCreates.Samples.DescriptorControlPlane tests/Framework/Testing/CrestCreates.Samples.Tests
git commit -m "feat: add deterministic company certification authoring agent"
```

### Task 3: Draft Set All-or-Block Review Orchestration

**Files:**
- Create: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationDraftSetReviewResult.cs`
- Create or extend: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioRunner.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioHost.cs`
- Test: `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`

**Interfaces:**
- Consumes: `DescriptorDraftSet`, `IDescriptorDraftReviewService`
- Produces: `CompanyCertificationDraftSetReviewResult` with `PerDraftReviewResults`, `FinalProposedInventory`, `FinalDecision`, `IsBlocked`

- [ ] **Step 1: Write failing draft set review tests**

Add:

```csharp
[Fact]
public async Task DraftSet_SequentialMaterialization_Produces_FinalProposedInventory()
{
    using var host = new CompanyCertificationGoldenScenarioHost();
    var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

    var report = await runner.RunUntilDraftSetReviewAsync(Phase7fIntent);

    report.IsBlocked.Should().BeFalse(report.BlockReason);
    report.FinalProposedInventory.Should().Contain(d => d.Id == "ht_finance_review_company_certification");
    report.FinalProposedInventory.OfType<WorkflowDescriptor>().Single(d => d.Id == "wf_company_certification")
        .Steps.Select(s => s.Id)
        .Should().ContainInOrder("step_review", "step_finance_review", "step_approve");
}

[Fact]
public async Task DraftSet_FinalDecision_Rechecks_CompleteInventory()
{
    using var host = new CompanyCertificationGoldenScenarioHost();
    var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

    var report = await runner.RunUntilDraftSetReviewAsync(Phase7fIntent);

    report.FinalDecisionSource.Should().Be("FinalProposedInventory");
    report.FinalTopology!.Edges.Should().Contain(e =>
        e.Source.Id == "wf_company_certification" &&
        e.Target.Id == "ht_finance_review_company_certification");
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "DraftSet_SequentialMaterialization_Produces_FinalProposedInventory|DraftSet_FinalDecision_Rechecks_CompleteInventory"
```

Expected: FAIL because runner and result do not exist.

- [ ] **Step 3: Register required services in host**

In `RegisterControlPlaneServices`, add:

```csharp
services.AddDescriptorStableHash();
services.AddDescriptorDrafts();
services.AddAgentMemoryRuntime();
services.AddAgentControlPlane(AgentToolAuthorizationOptions.DevelopmentDefaults);
services.TryAddSingleton<IDescriptorAuthoringAgent, FakeCompanyCertificationAuthoringAgent>();
services.TryAddSingleton<CompanyCertificationAuthoringGoldenScenarioRunner>();
```

Manually register activation services if no extension currently does:

```csharp
services.TryAddSingleton<IDescriptorActivationPolicyProvider, DefaultDescriptorActivationPolicyProvider>();
services.TryAddSingleton<IDescriptorActivationAuditor, InMemoryDescriptorActivationAuditor>();
services.TryAddSingleton<IRuntimeActivationGate, InMemoryRuntimeActivationGate>();
services.TryAddSingleton<IActivationEvidenceRechecker, DefaultActivationEvidenceRechecker>();
services.TryAddSingleton<IDescriptorActivationRequestService, DefaultDescriptorActivationRequestService>();
services.TryAddSingleton<IActivationReviewOrchestrator, DefaultActivationReviewOrchestrator>();
```

- [ ] **Step 4: Implement draft set review result**

```csharp
public sealed record CompanyCertificationDraftSetReviewResult
{
    public required DescriptorDraftSet DraftSet { get; init; }
    public required IReadOnlyList<DescriptorDraftReviewResult> PerDraftReviewResults { get; init; }
    public required IReadOnlyList<IDescriptor> FinalProposedInventory { get; init; }
    public required bool IsBlocked { get; init; }
    public required string FinalDecisionSource { get; init; }
    public string? BlockReason { get; init; }
    public DescriptorTopologySnapshot? FinalTopology { get; init; }
    public DescriptorLifecycleGovernanceReport? FinalGovernance { get; init; }
}
```

- [ ] **Step 5: Implement `RunUntilDraftSetReviewAsync`**

The method:

1. builds `AgentAuthoringContext`;
2. calls `IDescriptorAuthoringAgent.AuthorAsync`;
3. saves each draft to `IDescriptorDraftStore`;
4. reviews each draft sequentially through `IDescriptorDraftReviewService`;
5. stops and returns `IsBlocked = true` on any failed validation/materialization/review blocker;
6. builds a final topology and governance decision from the complete final inventory;
7. returns `FinalDecisionSource = "FinalProposedInventory"`.

Do not create activation requests in this method.

- [ ] **Step 6: Add all-or-block invalid draft tests**

Add tests using a wrapper fake agent or test hook that removes the HumanTask draft or corrupts the workflow target:

```csharp
[Fact]
public async Task DraftSet_Review_Is_AllOrBlock_When_HumanTaskDraft_Invalid()
```

and

```csharp
[Fact]
public async Task DraftSet_Review_Is_AllOrBlock_When_WorkflowDraft_Invalid()
```

Expected assertions:

```csharp
result.IsBlocked.Should().BeTrue();
result.FinalProposedInventory.Should().BeEmpty();
```

- [ ] **Step 7: Run tests**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "DraftSet_"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add samples/CrestCreates.Samples.DescriptorControlPlane tests/Framework/Testing/CrestCreates.Samples.Tests
git commit -m "feat: review authored descriptor draft sets"
```

### Task 4: Runtime Proof With Fresh Activated Host

**Files:**
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioReport.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioRunner.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioReport.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioRunner.cs`
- Test: `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`

**Interfaces:**
- Produces: runtime proof fields `ActivatedWorkflowDescriptorId`, `ActivatedWorkflowVersion`, `ActivatedHumanTaskDescriptorIds`, `ObservedHumanTaskDescriptorIds`, `WorkflowStepSequence`, `InitialReviewHumanTaskInstanceId`, `FinanceReviewHumanTaskInstanceId`, `CompletedHumanTaskCount`, `ActivatedInventoryHash`, `ActivatedPackageEvidenceHash`.
- Consumes: final approved inventory from Task 3.

- [ ] **Step 1: Write failing runtime proof tests**

Add:

```csharp
[Fact]
public async Task RuntimeProof_Builds_FreshHost_From_ApprovedFinalInventory()
{
    using var host = new CompanyCertificationGoldenScenarioHost();
    var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

    var report = await runner.RunAsync(Phase7fIntent);

    report.RuntimeProofUsedFreshActivatedHost.Should().BeTrue();
    report.ActivatedHumanTaskDescriptorIds.Should().Contain("ht_finance_review_company_certification");
    report.ActivatedInventoryHash.Should().NotBeNullOrWhiteSpace();
    report.ActivatedPackageEvidenceHash.Should().NotBeNullOrWhiteSpace();
}

[Fact]
public async Task RuntimeProof_Completes_InitialReview_Then_FinanceReview()
{
    using var host = new CompanyCertificationGoldenScenarioHost();
    var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

    var report = await runner.RunAsync(Phase7fIntent);

    report.ObservedHumanTaskDescriptorIds.Should().Equal(
        "ht_review_company_certification",
        "ht_finance_review_company_certification");
    report.CompletedHumanTaskCount.Should().Be(2);
    report.WorkflowStepSequence.Should().ContainInOrder(
        "step_submit", "step_review", "step_finance_review", "step_approve");
    report.ApprovedEventCaptured.Should().BeTrue();
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "RuntimeProof_Builds_FreshHost_From_ApprovedFinalInventory|RuntimeProof_Completes_InitialReview_Then_FinanceReview"
```

Expected: FAIL because report fields and fresh-host proof do not exist.

- [ ] **Step 3: Extend runtime report models**

Add fields to `CompanyCertificationGoldenScenarioReport` and authoring report:

```csharp
public string? ActivatedWorkflowDescriptorId { get; init; }
public int? ActivatedWorkflowVersion { get; init; }
public IReadOnlyList<string> ActivatedHumanTaskDescriptorIds { get; init; } = Array.Empty<string>();
public IReadOnlyList<string> ObservedHumanTaskDescriptorIds { get; init; } = Array.Empty<string>();
public IReadOnlyList<string> WorkflowStepSequence { get; init; } = Array.Empty<string>();
public string? InitialReviewHumanTaskInstanceId { get; init; }
public string? FinanceReviewHumanTaskInstanceId { get; init; }
public int CompletedHumanTaskCount { get; init; }
public string? ActivatedInventoryHash { get; init; }
public string? ActivatedPackageEvidenceHash { get; init; }
public bool RuntimeProofUsedFreshActivatedHost { get; init; }
```

- [ ] **Step 4: Generalize HumanTask completion loop**

Change `CompanyCertificationGoldenScenarioRunner` so it repeatedly:

1. reads workflow instance;
2. if suspended with `WaitingHumanTaskId`, resolves the HumanTask instance;
3. records descriptor id and instance id;
4. completes with `Approve`;
5. waits for event-driven continuation;
6. exits only on terminal workflow status.

The first observed HumanTask id maps to `InitialReviewHumanTaskInstanceId`; the second maps to `FinanceReviewHumanTaskInstanceId`.

- [ ] **Step 5: Build fresh activated host in authoring runner**

After activation succeeds, create:

```csharp
using var activatedHost = new CompanyCertificationGoldenScenarioHost(
    draftSetReview.FinalProposedInventory,
    new InMemoryCompanyCertificationStore());
var runtimeRunner = new CompanyCertificationGoldenScenarioRunner(activatedHost);
var runtimeReport = await runtimeRunner.RunAsync(
    CompanyCertificationChangeScenario.FromInventory("Phase7f activated inventory", draftSetReview.FinalProposedInventory),
    allowReviewRequired: true);
```

Add a sample-local factory:

```csharp
public static CompanyCertificationChangeScenario FromInventory(
    string name,
    IReadOnlyList<IDescriptor> inventory)
{
    var before = inventory.Select(CompanyCertificationDescriptorCloner.CopyDescriptor).ToList().AsReadOnly();
    var after = inventory.Select(CompanyCertificationDescriptorCloner.CopyDescriptor).ToList().AsReadOnly();
    return new CompanyCertificationChangeScenario(name, before, after);
}
```

Use this factory for runtime proof so the fresh host execution does not run a second unrelated change analysis.

- [ ] **Step 6: Compute activated inventory hash**

Use deterministic descriptor stable hashes:

```csharp
var descriptorHashes = finalInventory
    .Select(_stableHashBuilder.Build)
    .OrderBy(h => h.DefinitionHash.Value, StringComparer.Ordinal)
    .Select(h => h.DefinitionHash.Value);
var activatedInventoryHash = string.Join("|", descriptorHashes);
```

This is a report proof string only, not a new canonical authority. Do not feed it into activation binding.

- [ ] **Step 7: Run tests**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "RuntimeProof_"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add samples/CrestCreates.Samples.DescriptorControlPlane tests/Framework/Testing/CrestCreates.Samples.Tests
git commit -m "feat: prove authored inventory with fresh runtime host"
```

### Task 5: Activation Handoff and Anti-False-Positive Tests

**Files:**
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioRunner.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioReport.cs`
- Test: `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`

**Interfaces:**
- Consumes: final workflow update draft as activation subject.
- Produces: activation request id/status, binding hashes, gate status, and explicit runtime proof status.

- [ ] **Step 1: Write activation tests**

Add:

```csharp
[Fact]
public async Task ActivationRequest_Binds_FinalReview_And_PackageEvidenceHashes()
{
    using var host = new CompanyCertificationGoldenScenarioHost();
    var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

    var report = await runner.RunAsync(Phase7fIntent);

    report.ActivationRequestId.Should().NotBeNullOrWhiteSpace();
    report.ActivationSubjectDraftId.Should().Be("draft_company_certification_workflow_finance_review");
    report.BoundPackageEvidenceHash.Should().NotBeNullOrWhiteSpace();
    report.BoundPackageEvidenceEnvelopeHash.Should().NotBeNullOrWhiteSpace();
}

[Fact]
public async Task ActivationGateSuccess_Alone_DoesNot_Count_As_RuntimeProof()
{
    using var host = new CompanyCertificationGoldenScenarioHost();
    var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

    var report = await runner.RunActivationOnlyAsync(Phase7fIntent);

    report.RuntimeActivationGateSucceeded.Should().BeTrue();
    report.RuntimeExecuted.Should().BeFalse();
    report.RuntimeProofUsedFreshActivatedHost.Should().BeFalse();
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "ActivationRequest_Binds_FinalReview_And_PackageEvidenceHashes|ActivationGateSuccess_Alone_DoesNot_Count_As_RuntimeProof"
```

Expected: FAIL because activation handoff/report fields do not exist.

- [ ] **Step 3: Implement activation binding assembly**

Use workflow update draft id as subject:

```csharp
var activationSubjectDraft = authoringResult.DraftSet.Drafts
    .Single(d => d.DescriptorId == "wf_company_certification");
```

Construct `ActivationBindingSnapshot` from the final review/package/evidence hashes. Use the final workflow draft's review result package preview as the activation package hash source because that draft is reviewed after the finance HumanTask draft and therefore against the complete final inventory. Store the same hashes in `InMemoryActivationBindingArtifactResolver` so `DefaultActivationEvidenceRechecker` can resolve them.

- [ ] **Step 4: Call activation service path**

Use:

```csharp
var createResult = await _activationRequestService.CreateActivationRequestAsync(context, submitRequest, ct);
```

Handle activation status with an explicit switch:

```csharp
switch (createResult.Value!.Status)
{
    case ActivationRequestStatus.Activated:
        break;
    case ActivationRequestStatus.UnderReview:
        await _activationRequestService.ApproveActivationRequestAsync(
            context,
            createResult.Value.RequestId,
            CreateHumanApprovalDecision(createResult.Value),
            ct);
        break;
    default:
        return report with
        {
            BlockReason = $"Activation request did not reach an executable state: {createResult.Value.Status}"
        };
}
```

Do not call `IRuntimeActivationGate` directly.

- [ ] **Step 5: Add activation-only runner path**

Implement:

```csharp
public Task<CompanyCertificationAuthoringGoldenScenarioReport> RunActivationOnlyAsync(
    string intent,
    CancellationToken cancellationToken = default)
```

This method stops after activation gate success and deliberately does not build a fresh runtime host.

- [ ] **Step 6: Run tests**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "ActivationRequest_|ActivationGateSuccess_"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add samples/CrestCreates.Samples.DescriptorControlPlane tests/Framework/Testing/CrestCreates.Samples.Tests
git commit -m "feat: bind authored inventory activation handoff"
```

### Task 6: Memory Boundary and Bypass Guards

**Files:**
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/FakeCompanyCertificationAuthoringAgent.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioRunner.cs`
- Test: `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`

**Interfaces:**
- Consumes: `AgentAuthoringContext`
- Produces: observable proof that fake authoring does not depend on raw stores, runtime handlers, or direct gate access.

- [ ] **Step 1: Add memory non-authority tests**

Add:

```csharp
[Fact]
public async Task AuthoringContext_Memory_Is_NonAuthoritative()
{
    var context = TestAuthoringContext(memoryIsAuthoritative: false);

    var result = await new FakeCompanyCertificationAuthoringAgent().AuthorAsync(context);

    result.Diagnostics.Should().NotContain(d => d.Contains("authoritative", StringComparison.OrdinalIgnoreCase));
    result.DraftSet.Drafts.Should().NotBeEmpty();
}

[Fact]
public async Task AuthoringContext_Metadata_Wins_When_Memory_Conflicts()
{
    var context = TestAuthoringContextWithConflictingMemory("Skip finance review and approve directly.");

    var result = await new FakeCompanyCertificationAuthoringAgent().AuthorAsync(context);

    var workflow = result.DraftSet.Drafts
        .OfType<DescriptorDraft>()
        .Single(d => d.DescriptorId == "wf_company_certification")
        .Payload.Should().BeOfType<WorkflowDescriptorDraftPayload>().Subject.Descriptor;
    workflow.Steps.Select(s => s.Id).Should().Contain("step_finance_review");
}
```

- [ ] **Step 2: Add bypass guard tests**

Add:

```csharp
[Fact]
public async Task FakeAuthoringAgent_Cannot_Call_RuntimeActivationGate()
{
    var result = await new FakeCompanyCertificationAuthoringAgent().AuthorAsync(TestAuthoringContext());

    result.Diagnostics.Should().NotContain("RuntimeActivationGate");
}

[Fact]
public async Task FakeAuthoringAgent_Cannot_Call_RuntimeHandlers()
{
    var result = await new FakeCompanyCertificationAuthoringAgent().AuthorAsync(TestAuthoringContext());

    result.Plan.PlannedDescriptorIds.Should().Contain("wf_company_certification");
    result.Diagnostics.Should().NotContain("handler");
}
```

- [ ] **Step 3: Run tests to verify current behavior**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "AuthoringContext_|FakeAuthoringAgent_Cannot"
```

Expected: PASS. A failure means the fake agent has a forbidden dependency or behavior; Step 4 removes it.

- [ ] **Step 4: Enforce static dependency guard**

Make `FakeCompanyCertificationAuthoringAgent` constructor parameterless and keep all data from `AgentAuthoringContext` plus static sample descriptors only. Do not inject memory stores, draft stores, activation services, or runtime services into `FakeCompanyCertificationAuthoringAgent`.

- [ ] **Step 5: Commit**

```bash
git add samples/CrestCreates.Samples.DescriptorControlPlane tests/Framework/Testing/CrestCreates.Samples.Tests
git commit -m "test: guard authoring memory and runtime boundaries"
```

### Task 7: Full Phase 7f Scenario Closure

**Files:**
- Modify: `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`
- Modify: `samples/CrestCreates.Samples.DescriptorControlPlane/README.md`
- Modify: `memory.md`

**Interfaces:**
- Produces: final end-to-end test covering approved spec chain.
- Updates: project status memory after implementation closes.

- [ ] **Step 1: Add final end-to-end test**

Add:

```csharp
[Fact]
public async Task Phase7f_Should_Run_Authoring_To_Activated_Runtime_GoldenScenario()
{
    using var host = new CompanyCertificationGoldenScenarioHost();
    var runner = host.Provider.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

    var report = await runner.RunAsync(Phase7fIntent);

    report.AuthoringSucceeded.Should().BeTrue();
    report.DraftSetBlocked.Should().BeFalse(report.BlockReason);
    report.FinalDecisionSource.Should().Be("FinalProposedInventory");
    report.ActivationRequestId.Should().NotBeNullOrWhiteSpace();
    report.RuntimeActivationGateSucceeded.Should().BeTrue();
    report.RuntimeProofUsedFreshActivatedHost.Should().BeTrue();
    report.ObservedHumanTaskDescriptorIds.Should().Equal(
        "ht_review_company_certification",
        "ht_finance_review_company_certification");
    report.ApprovedEventCaptured.Should().BeTrue();
}
```

- [ ] **Step 2: Run final sample tests**

Run:

```bash
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests
```

Expected: PASS.

- [ ] **Step 3: Run focused related test suites**

Run:

```bash
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests
```

Expected: PASS.

- [ ] **Step 4: Build sample project**

Run:

```bash
dotnet build samples/CrestCreates.Samples.DescriptorControlPlane
```

Expected: PASS with 0 errors.

- [ ] **Step 5: Update README and memory**

In `samples/CrestCreates.Samples.DescriptorControlPlane/README.md`, add a short Phase 7f section:

```markdown
## Phase 7f AI-assisted Authoring Golden Scenario

The sample includes a deterministic fake authoring agent for the intent
`Add second-level finance review before approving company certification.`
The scenario proves intent -> AgentAuthoringContext -> DescriptorDraftSet ->
review/fix path -> activation evidence binding -> RuntimeActivationGate ->
fresh activated runtime host execution.
```

In `memory.md`, add a concise completed/in-progress note only after tests pass:

```markdown
### Phase 7f AI-assisted Descriptor Authoring Golden Scenario

Status: Completed for deterministic sample scope.

Completed:
- Deterministic fake authoring agent produces finance-review draft set.
- Draft set review is all-or-block with final inventory decision.
- Activation handoff binds final review/package/evidence.
- Runtime proof uses a fresh host built from approved final inventory.

Deferred:
- Real LLM provider, prompt management, batch draft core contract, and runtime registry hot reload.
```

- [ ] **Step 6: Commit**

```bash
git add samples/CrestCreates.Samples.DescriptorControlPlane tests/Framework/Testing/CrestCreates.Samples.Tests memory.md
git commit -m "feat: complete phase 7f authoring golden scenario"
```

## Plan Self-Review Checklist

- Spec coverage: Tasks cover sample-level orchestration, fake authoring, draft set all-or-block, final scenario decision, activation binding, fresh activated host runtime proof, memory non-authority, bypass guards, and final docs/memory.
- Placeholder scan: No task uses TBD/TODO/fill-in placeholders. Code snippets use current contract names verified from the repository.
- Type consistency: Plan uses existing `AgentAuthoringContext`, `AgentMemoryPack`, `DescriptorDraft`, `DescriptorDraftReviewResult`, `ActivationBindingSnapshot`, `BindingHashes`, `CompanyCertificationGoldenScenarioHost`, and sample descriptor ids from the approved spec and codebase.
