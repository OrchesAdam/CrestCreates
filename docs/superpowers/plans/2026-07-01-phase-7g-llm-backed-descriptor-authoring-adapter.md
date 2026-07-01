# Phase 7g LLM-backed Descriptor Authoring Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement Phase 7g by promoting descriptor authoring to framework-level contracts, adding a provider-agnostic LLM-backed authoring runtime, adding an OpenAI-compatible provider integration project, and proving the LLM fixture path still produces draft proposals only and flows through the existing governance/review mainline.

**Architecture:** `CrestCreates.Agent.Authoring.Abstractions` owns stable authoring contracts. `CrestCreates.Agent.Authoring` owns provider-agnostic prompt projection, canonical prompt hashing, model orchestration, parsing, diagnostics, fake/recorded clients, and draft materialization. `CrestCreates.Agent.Authoring.Http` owns HTTP/provider protocol and credential resolution. Authoring produces `DescriptorDraftSet`; it must not review, activate, approve, mutate runtime registries, or execute runtime handlers.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, System.Text.Json source generation, existing canonical hash runtime, existing metadata context pack contracts, existing agent memory contracts, existing descriptor draft contracts.

## Verified Codebase Facts

Preserve these facts during implementation:

- `DescriptorRef` is `DescriptorRef(string Namespace, string Id, int? Version = null)`. Do not treat the first argument as descriptor kind.
- `CanonicalHash` has required properties `Value`, `Algorithm`, `AlgorithmVersion`, `ArtifactKind`, `Scope`, `Purpose`, `ContractVersion`, and `CanonicalShapeVersion`; it has no `Version` property.
- `CanonicalHashProjectionResult` carries `CanonicalHashMetadata` plus an `Action<Utf8JsonWriter>` canonical JSON writer. Prompt hashes must be computed through `ICanonicalHashComputer.ComputeFromProjection`.
- `MetadataContextPackDescriptorEntry` exposes descriptor hashes as `Hashes`, not `StableHashes`.
- `DiagnosticCode` and `SeverityLevel` are in `CrestCreates.Core.Abstractions.Identity`; `SeverityLevel` is not an enum.
- `DescriptorDraft` requires `TenantId`, `DraftId`, `DescriptorKind`, `DescriptorId`, `Operation`, `AuthorKind`, `AuthorId`, `CreatedAt`, and `Payload`.
- `DescriptorDraftPayload` is abstract. Reuse existing concrete payload records from `CrestCreates.DescriptorDraft.Abstractions`; do not invent a second draft payload model.
- If parser/materializer constructs concrete draft payloads, the authoring runtime must reference the relevant descriptor abstraction projects, not runtime execution projects.
- `Directory.Packages.props` already contains the required Microsoft extension package versions used by this phase.

## Project Layout

Create:

```text
src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/
src/Runtime/Agent/CrestCreates.Agent.Authoring/
src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/
tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/
```

Modify:

```text
CrestCreates.slnx
samples/CrestCreates.Samples.DescriptorControlPlane/
tests/Framework/Testing/CrestCreates.Samples.Tests/
memory.md
```

Move obsolete sample-local authoring contracts to:

```text
99_RecycleBin/phase7g-sample-authoring-contracts/
```

## Task 1: Add Framework Authoring Contracts

Files:

- `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj`
- `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/**`
- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/**`
- `CrestCreates.slnx`

Required project references:

- `CrestCreates.Agent.Memory.Abstractions`
- `CrestCreates.Core.Abstractions`
- `CrestCreates.Metadata.Abstractions`
- `CrestCreates.Metadata.ContextPack.Abstractions`
- `CrestCreates.DescriptorDraft.Abstractions`
- `CrestCreates.Snapshot.Abstractions`

Do not add provider or HTTP concerns here.

- [ ] Add failing contract tests for namespace, JSON source-generation coverage, provider profile secret safety, and `SucceededWithDiagnostics`.
- [ ] Add boundary tests proving abstractions do not reference Control Plane, provider projects, HTTP SDKs, or provider SDKs.
- [ ] Add `IDescriptorAuthoringAgent`.
- [ ] Add `DescriptorAuthoringStatus` with `Succeeded`, `SucceededWithDiagnostics`, `Blocked`, `InvalidProviderOutput`, `ProviderUnavailable`, and `Failed`.
- [ ] Add `DescriptorAuthoringDiagnostic`, `DescriptorAuthoringDiagnosticCodes`, `DescriptorAuthoringPlan`, `DescriptorDraftSet`, and `DescriptorAuthoringResult`.
- [ ] Use `DiagnosticCode` and `SeverityLevel` from `CrestCreates.Core.Abstractions.Identity`.
- [ ] Use `DescriptorRef` for planned descriptor refs.
- [ ] Use existing `DescriptorDraft` in `DescriptorDraftSet`.
- [ ] Add prompt/model DTOs and `IDescriptorAuthoringModelClient`.
- [ ] Ensure `DescriptorAuthoringPromptInput` contains normalized authoring projections, not raw `MetadataContextPack` or raw `AgentMemoryPack`.
- [ ] Ensure provider profile DTOs carry only credential references or setting names, never raw secret values.
- [ ] Add `DescriptorAuthoringJsonSerializerContext` entries for public authoring DTOs.
- [ ] Run `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj`.
- [ ] Run `dotnet build src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj`.
- [ ] Commit with `feat: add descriptor authoring contracts`.

## Task 2: Move Sample Authoring To Framework Contracts

Files:

- `samples/CrestCreates.Samples.DescriptorControlPlane/CrestCreates.Samples.DescriptorControlPlane.csproj`
- `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/**`
- `samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioHost.cs`
- `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`
- `99_RecycleBin/phase7g-sample-authoring-contracts/`

- [ ] Add a failing sample test proving `FakeCompanyCertificationAuthoringAgent` implements framework `IDescriptorAuthoringAgent`.
- [ ] Add the sample project reference to `CrestCreates.Agent.Authoring.Abstractions`.
- [ ] Update sample fake authoring code to return framework `DescriptorAuthoringResult`, `DescriptorAuthoringPlan`, and `DescriptorDraftSet`.
- [ ] Convert old string diagnostics to structured `DescriptorAuthoringDiagnostic` values or an empty diagnostic list.
- [ ] Build planned refs with the real `DescriptorRef` semantics: namespace/id/version, not kind/id/version.
- [ ] Move sample-local authoring contract files to `99_RecycleBin/phase7g-sample-authoring-contracts/`.
- [ ] Keep sample review and activation behavior unchanged.
- [ ] Run `dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "FullyQualifiedName~CompanyCertificationAuthoringGoldenScenarioTests"`.
- [ ] Commit with `refactor: move sample authoring to framework contracts`.

## Task 3: Add Provider-Agnostic Authoring Runtime And Prompt Hash

Files:

- `src/Runtime/Agent/CrestCreates.Agent.Authoring/CrestCreates.Agent.Authoring.csproj`
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/**`
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/AgentAuthoringServiceCollectionExtensions.cs`
- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/PromptInputHashTests.cs`
- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/AuthoringBoundaryTests.cs`
- `CrestCreates.slnx`

Required project references:

- `CrestCreates.Agent.Authoring.Abstractions`
- `CrestCreates.Agent.Memory.Abstractions`
- `CrestCreates.Metadata.Abstractions`
- `CrestCreates.Metadata.ContextPack.Abstractions`
- `CrestCreates.DescriptorDraft.Abstractions`

Forbidden project references:

- `CrestCreates.Agent.ControlPlane`
- `CrestCreates.Agent.ControlPlane.Abstractions`
- `CrestCreates.Agent.Authoring.Http`
- `CrestCreates.Agent.DraftContracts`
- runtime execution projects such as `CrestCreates.Capability`, `CrestCreates.Workflow`, and `CrestCreates.HumanTask`

- [ ] Add boundary tests for forbidden references.
- [ ] Add prompt hash tests for deterministic same-input hash, changed-projection hash change, projection-only input, and fully populated `CanonicalHash` test helpers.
- [ ] Implement `IDescriptorAuthoringPromptInputFactory`.
- [ ] Project `AgentAuthoringContext`, `MetadataContextPack`, and `AgentMemoryPack` into authoring-specific DTOs.
- [ ] Read metadata descriptor hashes through `MetadataContextPackDescriptorEntry.Hashes`.
- [ ] Implement `IDescriptorAuthoringPromptInputHashService` using `CanonicalHashProjectionResult.Create(metadata, writer => ...)` and `ICanonicalHashComputer.ComputeFromProjection`.
- [ ] Do not use string builder digests, pipe-delimited payloads, ad-hoc SHA256 helpers, or arbitrary upstream object serialization for prompt hashes.
- [ ] Implement `IDescriptorAuthoringPromptBuilder`.
- [ ] Ensure prompt builder states memory is non-authoritative and metadata wins on conflict.
- [ ] Register provider-agnostic services through DI.
- [ ] Do not use `AgentDraftPayloadProjection` or generated DraftContracts DTOs in the authoring runtime.
- [ ] Run `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj --filter "FullyQualifiedName~PromptInputHashTests|FullyQualifiedName~AuthoringBoundaryTests"`.
- [ ] Run `dotnet build src/Runtime/Agent/CrestCreates.Agent.Authoring/CrestCreates.Agent.Authoring.csproj`.
- [ ] Commit with `feat: add provider agnostic authoring runtime`.

## Task 4: Add Parser, Draft Materialization, Recorded Client, And LLM Agent

Files:

- `src/Runtime/Agent/CrestCreates.Agent.Authoring/Parsing/**`
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/Model/**`
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/LlmDescriptorAuthoringAgent.cs`
- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/OutputParserTests.cs`
- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/LlmDescriptorAuthoringAgentTests.cs`

Additional allowed project references when materializing concrete draft payloads:

- `CrestCreates.Capability.Abstractions`
- `CrestCreates.Workflow.Abstractions`
- `CrestCreates.HumanTask.Abstractions`
- `CrestCreates.Event.Abstractions`
- `CrestCreates.Form.Abstractions`
- `CrestCreates.Schema.Abstractions`

These are descriptor abstraction dependencies. Do not reference matching runtime execution projects.

- [ ] Add parser tests for invalid JSON, prompt hash mismatch, unknown descriptor kind, unsupported operation, memory authority claims, and atomic failure on one invalid draft.
- [ ] Add parser tests proving valid fixture output creates `DescriptorDraft` objects with all required fields populated.
- [ ] Add `DescriptorAuthoringParseContext` carrying tenant id, author id, created-at timestamp, intent text, and expected prompt input hash.
- [ ] Ensure parser does not hard-code tenant id, descriptor id, author id, or timestamp.
- [ ] Define provider-output DTOs with plan items including descriptor kind, descriptor id, operation, payload, rationale, evidence refs, memory refs, and assumptions.
- [ ] Implement output parsing with governance checks before returning any draft set.
- [ ] Implement atomic semantics: if any item is invalid, unsupported, or blocked, return no partially successful draft set.
- [ ] Implement payload materialization by reusing existing `DescriptorDraftPayload` concrete records.
- [ ] For Phase 7g closure, support the HumanTask and Workflow fixture path first; unsupported kinds must return structured diagnostics.
- [ ] Implement `FakeDescriptorAuthoringModelClient`.
- [ ] Implement `RecordedDescriptorAuthoringModelClient`; missing fixture must surface as `ProviderUnavailable`, not as an empty successful plan.
- [ ] Implement `LlmDescriptorAuthoringAgent` orchestration from prompt input to model response to parsed result.
- [ ] Map provider unavailable, invalid provider output, blocked governance, diagnostics, and success-with-diagnostics consistently.
- [ ] Run `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj --filter "FullyQualifiedName~OutputParserTests|FullyQualifiedName~LlmDescriptorAuthoringAgentTests"`.
- [ ] Commit with `feat: add deterministic llm authoring adapter`.

## Task 5: Add OpenAI-Compatible Provider Integration

Files:

- `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/CrestCreates.Agent.Authoring.Http.csproj`
- `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/OpenAICompatible/**`
- `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/Credentials/**`
- `src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/AgentAuthoringHttpServiceCollectionExtensions.cs`
- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/ProviderBoundaryTests.cs`
- `CrestCreates.slnx`

Project boundary:

- Provider project references `CrestCreates.Agent.Authoring.Abstractions`.
- Credential provider abstractions live in this provider integration project, not in `CrestCreates.Agent.Authoring.Abstractions`.
- Core authoring runtime must not reference this project.

- [ ] Add boundary tests proving abstractions do not contain `IDescriptorAuthoringCredentialProvider`.
- [ ] Add boundary tests proving authoring core does not reference the provider integration project.
- [ ] Add tests for credential unavailable, credential rejected, provider unauthorized, timeout, and rate limit diagnostics.
- [ ] Add `OpenAICompatibleDescriptorAuthoringModelClient`.
- [ ] Name protocol DTOs `OpenAICompatible...`, not generic `Http...`.
- [ ] Resolve secrets through credential references only; do not persist, serialize, log, or record raw secret values.
- [ ] Set authorization on each `HttpRequestMessage`; do not mutate `_httpClient.DefaultRequestHeaders.Authorization`.
- [ ] Keep provider request/response DTOs out of abstractions and authoring core.
- [ ] Run `dotnet build src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/CrestCreates.Agent.Authoring.Http.csproj`.
- [ ] Run `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj --filter "FullyQualifiedName~ProviderBoundaryTests"`.
- [ ] Commit with `feat: add openai compatible authoring provider`.

## Task 6: Add LLM Fixture Golden Scenario

Files:

- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/Fixtures/**`
- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/GoldenScenarioLlmFixtureTests.cs`
- `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/CompanyCertificationAuthoringGoldenScenarioRunner.cs`
- `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs`

- [ ] Add a recorded provider-output fixture for the company certification HumanTask plus Workflow scenario.
- [ ] Add a test that constructs the real `LlmDescriptorAuthoringAgent` with recorded model output.
- [ ] Use the real prompt projection path; only the fixture hash may be pinned/wrapped to select deterministic recorded output.
- [ ] Add a runner overload that accepts framework `IDescriptorAuthoringAgent`.
- [ ] Prove the LLM fixture path produces a draft set reviewed by the existing sample governance path.
- [ ] Assert no activation gate, runtime registry mutation, or handler execution is performed by the authoring adapter itself.
- [ ] Run `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj --filter "FullyQualifiedName~GoldenScenarioLlmFixtureTests"`.
- [ ] Run `dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "FullyQualifiedName~CompanyCertificationAuthoringGoldenScenarioTests"`.
- [ ] Commit with `test: add llm fixture authoring golden scenario`.

## Task 7: Final Verification And Memory Update

Files:

- `memory.md`

- [ ] Run `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/CrestCreates.Agent.Authoring.Tests.csproj`.
- [ ] Run `dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests --filter "FullyQualifiedName~CompanyCertificationAuthoringGoldenScenarioTests"`.
- [ ] Run `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/CrestCreates.Agent.Memory.Tests.csproj`.
- [ ] Run `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/CrestCreates.Agent.ControlPlane.Tests.csproj`.
- [ ] Run `dotnet build`.
- [ ] Update `memory.md` with Phase 7g status, boundaries, and remaining follow-ups.
- [ ] Commit with `docs: record phase 7g authoring adapter closure`.

## Self-Review Checklist

- [ ] The plan uses real `DescriptorRef`, `CanonicalHash`, `SeverityLevel`, `MetadataContextPackDescriptorEntry`, and `DescriptorDraft` shapes.
- [ ] No task asks for `CanonicalHash.Version`, `StableHashes`, or descriptor kind as the first `DescriptorRef` constructor argument.
- [ ] Prompt input hashing uses canonical hash infrastructure, not custom digest helpers.
- [ ] Parser/materializer receives tenant, author, timestamp, and expected hash through context.
- [ ] Provider credentials stay out of abstractions and recorded fixtures.
- [ ] Authoring core is provider-agnostic.
- [ ] Provider integration is protocol-specific and named OpenAI-compatible.
- [ ] LLM output produces atomic draft proposals only.
- [ ] Existing Control Plane, review, package evidence, activation handoff, and runtime mutation boundaries remain intact.
