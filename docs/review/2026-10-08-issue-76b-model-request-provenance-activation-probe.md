# Issue #112 — Model Request Provenance Activation Probe Evidence Record

**Date**: 2026-10-08
**Probe fixture**: `ModelRequestProvenanceActivationProbeFixture`
**Location**: `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/ModelRequestProvenanceActivationProbeFixture.cs`
**Test framework**: xUnit + FluentAssertions
**Total tests**: 10 acceptance tests, all passing
**Full test suite**: 76 tests pass (66 pre-existing + 10 new), zero regressions

---

## 1. Current Production Path Under Test

```
AgentAuthoringContext
    -> DefaultDescriptorAuthoringPromptInputFactory.Create(context)
        -> DescriptorAuthoringPromptInput (normalized prompt input)
            -> IAgentPromptEvidenceFactory.CreateInputEvidence(request)
                -> AgentPromptInputEvidence<TInput> (with InputHash, TemplateId, ModelProfileRef, ProviderProfileRef, etc.)
            -> LlmDescriptorAuthoringAgent.AuthorAsync(context)
                -> IDescriptorAuthoringModelClient.CompleteAsync(request)
                    -> DescriptorAuthoringModelResponse (with ProviderName, ModelName)
                -> IAgentPromptEvidenceFactory.CreateOutputEvidence(request, inputHash, providerObservation)
                    -> AgentPromptOutputEvidence<TOutput> (with InputHash link, OutputHash, ProviderObservation)
            -> DescriptorAuthoringResult (carries PromptInputEvidence + PromptOutputEvidence summaries)
```

The probe tests the full Descriptor Authoring pipeline from context construction through evidence creation, verifying that all fields needed for post-hoc provenance verification are present in the existing evidence summaries.

**Key source files**:
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/Authoring/LlmDescriptorAuthoringAgent.cs` — agent implementation
- `src/Runtime/Agent/CrestCreates.Agent.Prompting/DefaultAgentPromptEvidenceFactory.cs` — evidence factory
- `src/Runtime/Agent/CrestCreates.Agent.Authoring/Prompting/DefaultDescriptorAuthoringPromptInputFactory.cs` — prompt input factory
- `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Prompting/DescriptorAuthoringPromptInput.cs` — normalized prompt input
- `src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/AgentPromptEvidenceContracts.cs` — evidence contracts (summaries, observation)

---

## 2. Acceptance Test Results

| # | Test | Classification | Result | Duration |
|---|------|---------------|--------|----------|
| 1 | `DescriptorAuthoring_Request_Should_Be_Explainable_From_Existing_PromptEvidence` | A | Passed | 4 ms |
| 2 | `CandidateNormalizedInput_Should_Verify_Against_RecordedHash` | C | Passed | 1 ms |
| 3 | `MetadataProjection_Should_Be_Identifiable_From_Effective_PromptInput` | A/C | Passed | 1 ms |
| 4 | `MemoryProjection_Should_Be_Identifiable_From_Effective_PromptInput` | A/C | Passed | 116 ms |
| 5 | `PromptTemplate_ModelProfile_And_ProviderProfile_Should_Be_Explainable` | A | Passed | 1 ms |
| 6 | `ProviderObservation_Should_Remain_Distinct_From_Configured_Profile` | A | Passed | 1 ms |
| 7 | `EmptyMemoryProjection_Should_Remain_Explainable` | A/C | Passed | 1 ms |
| 8 | `SensitiveProviderCredentials_Should_Not_Appear_In_Evidence` | A | Passed | 43 ms |
| 9 | `OutputEvidence_Should_Link_Back_To_The_Same_InputHash` | A | Passed | 1 ms |
| 10 | `Missing_Optional_ProviderLocal_Identifiers_Should_Not_Automatically_Activate_Provenance` | D | Passed | < 1 ms |

---

## 3. Classification Summary

### A — Directly Present (directly observable in existing evidence)

The following fields are directly present in `AgentPromptInputEvidenceSummary` and `AgentPromptOutputEvidenceSummary`:

- `TemplateId` — identifies the prompt template
- `TemplateVersion` — identifies the template version
- `ContractVersion` — identifies the prompt contract version
- `Purpose` — always `AgentPromptPurpose.DescriptorAuthoring`
- `ModelProfileRef` — configured model profile reference
- `ProviderProfileRef` — configured provider profile reference
- `InputHash` — canonical hash of the normalized prompt input
- `OutputHash` — canonical hash of the output evidence (when available)
- `ProviderObservation.ProviderName` — observed provider name from model response
- `ProviderObservation.ModelName` — observed model name from model response

### C — Hash-Verifiable (candidate can be verified against recorded hash)

- **Candidate normalized input verification**: Given a candidate `DescriptorAuthoringPromptInput` (supplied by the post-hoc reviewer), `IAgentPromptHashService.ComputeInputHash()` produces a hash that can be compared against the recorded `InputHash`. Test #2 confirms that a candidate input verifies against the recorded historical hash.
- **Metadata projection content**: The metadata context pack contribution is covered by the `InputHash`. Test #3 confirms that metadata presence/absence changes the hash, and a candidate input with metadata verifies against the recorded hash.
- **Memory projection content**: The memory pack contribution is covered by the `InputHash`. Test #4 confirms that memory presence/absence changes the hash, and a candidate input with memory verifies against the recorded hash. Empty memory is also verifiable (test #7).

**Important limitation**: The `AgentPromptInputEvidenceSummary` carries only the `InputHash`, NOT the normalized input payload itself. A post-hoc reviewer cannot reconstruct the historical normalized input from the summary alone. They can only verify a candidate input they supply against the recorded hash. This is C-level verification, not B-level reconstruction.

### D — Adapter-Local (not required by current audit semantics)

- `ProviderObservation.ResponseId` — optional, not populated by `FakeDescriptorAuthoringModelClient`
- `ProviderObservation.FinishReason` — optional, not populated by `FakeDescriptorAuthoringModelClient`
- These fields are provider-adapter-local data. Their absence does not prevent provenance verification.

### E — Diagnostic-Only / Retention Gap

- `AgentPromptOutputEvidence.Diagnostics` — carries `OutputHashUnavailable` warning when no canonical payload projector is registered. Diagnostic, not provenance-critical.
- **Retention gap**: The normalized input payload (`DescriptorAuthoringPromptInput`) is not retained in the evidence summary. Only the `InputHash` is retained. This means post-hoc reviewers cannot reconstruct historical inputs from summaries alone; they can only verify candidate inputs against the recorded hash.

### F — Missing Framework Semantic

**None identified.** All fields required for post-hoc provenance verification of a Descriptor Authoring model request are present in the existing evidence contracts. No missing framework semantic was found.

---

## 4. What the Probe Confirms

1. **Full identity chain is present**: Template id/version, contract version, purpose, model profile ref, provider profile ref, and input hash are all directly present in `AgentPromptInputEvidenceSummary`.

2. **Input-Output linkage is verifiable**: `AgentPromptOutputEvidenceSummary.InputHash` matches `AgentPromptInputEvidenceSummary.InputHash`, creating a verifiable chain from input to output.

3. **Configured vs. observed provider distinction is preserved**: The configured `ProviderProfileRef` (from options) remains distinct from the observed `ProviderObservation.ProviderName` (from the model response). Both are independently accessible.

4. **Hash integrity is confirmed**: The `InputHash` covers the full `DescriptorAuthoringPromptInput` including Metadata, Memory, VisibleDescriptorRefs, SupportedDescriptorKinds, TenantId, IntentText, and ContractVersion. Any change to these inputs produces a different hash.

5. **Candidate verification is feasible**: A post-hoc reviewer can supply a candidate normalized input and verify it against the recorded hash. This is C-level verification, not B-level reconstruction.

6. **Empty memory is representable**: An empty `AgentMemoryPack` produces a valid `DescriptorAuthoringPromptInput` with an empty `DescriptorAuthoringMemoryProjection.Memories` collection and a valid `InputHash`.

7. **No credential leakage**: Serialized evidence JSON does not contain provider API key prefixes (`sk-`), bearer tokens, passwords, or secret values.

8. **Optional provider-local fields do not block provenance**: Missing `ResponseId` and `FinishReason` do not prevent core provenance verification.

---

## 5. What the Probe Does NOT Test

- **Cross-request provenance**: The probe tests single-request provenance. It does not test whether provenance can be verified across multiple requests or sessions.
- **Provider adapter production implementations**: The probe uses `HashCapturingModelClient`. Real provider adapters may populate additional fields (e.g., `ResponseId`, `FinishReason`, `LatencyMs`).
- **Hash collision resistance**: The probe confirms hash integrity but does not test collision resistance of the underlying canonical hash algorithm.
- **Canonical payload projector coverage**: The probe does not test whether all input types have registered canonical payload projectors.
- **Historical input reconstruction**: The probe does not test whether historical normalized inputs can be reconstructed from evidence summaries alone. The summaries carry only the `InputHash`, not the payload. Reconstruction is not possible; only candidate verification is possible.

---

## 6. Post-Hoc Verification Feasibility

Given a `DescriptorAuthoringResult` with non-null `PromptInputEvidence` and `PromptOutputEvidence`, a post-hoc reviewer can verify:

| Verification Target | Source | Classification |
|--------------------|--------|---------------|
| Which prompt template was used | `InputEvidence.TemplateId` + `TemplateVersion` | A |
| Which prompt contract version | `InputEvidence.ContractVersion` | A |
| Which model profile was configured | `InputEvidence.ModelProfileRef` | A |
| Which provider profile was configured | `InputEvidence.ProviderProfileRef` | A |
| What was the effective input hash | `InputEvidence.InputHash` | A |
| What provider/model actually responded | `OutputEvidence.ProviderObservation.ProviderName/ModelName` | A |
| Was the output linked to the input | `OutputEvidence.InputHash == InputEvidence.InputHash` | A |
| Does a candidate normalized input match the recorded hash | Supply candidate, compute hash, compare | C |
| Does a candidate metadata projection match the recorded hash | Supply candidate with metadata, compute hash, compare | C |
| Does a candidate memory projection match the recorded hash | Supply candidate with memory, compute hash, compare | C |
| Was memory empty | Verify candidate with empty memory against recorded hash | C |

**What cannot be verified from summaries alone**:

| Verification Target | Limitation | Classification |
|--------------------|------------|---------------|
| What was the historical normalized input | Summary carries only `InputHash`, not payload | E (retention gap) |
| What was the historical metadata content | Summary carries only `InputHash`, not payload | E (retention gap) |
| What was the historical memory content | Summary carries only `InputHash`, not payload | E (retention gap) |

**Conclusion**: Post-hoc provenance verification is feasible using existing evidence contracts for identity (A) and integrity (C). Historical payload reconstruction is not feasible from summaries alone (E retention gap). No additional framework-level provenance contract is required for the current audit semantics.

---

## 7. Final Disposition

### **Keep #76 Candidate B Deferred**

**Rationale:**
1. Existing Prompt Evidence provides direct request/model/provider identity (A-class).
2. Existing Prompt Evidence provides deterministic integrity linkage via `InputHash` (A-class).
3. Existing Prompt Evidence enables verification of a supplied historical normalized-input candidate against the recorded hash (C-class).
4. The summaries do not by themselves reconstruct historical Metadata/Memory content (E-class retention gap).
5. No missing framework semantic (F-classification) was identified.
6. Optional adapter-local fields (D-classification) do not block provenance verification.
7. The existing `IAgentPromptEvidenceFactory` + `DefaultDescriptorAuthoringPromptInputFactory` pipeline already provides complete provenance coverage for Descriptor Authoring model requests at the A/C level.

**Activation criteria assessment (for a dedicated Provenance contract):**
1. Existing evidence is insufficient for post-hoc reconstruction — **Partially met**: summaries do not carry payload, only hash (E retention gap). However, for the current audit semantics (identity + integrity + candidate verification), existing evidence is sufficient.
2. A framework-level provenance contract would add information not currently available — **Not met for current semantics**: no F-classification gaps found. Would be met only if full payload reconstruction is required.
3. Multiple consumers need a standardized provenance shape — **Not yet demonstrated**: only Descriptor Authoring is probed.
4. Cross-agent provenance correlation is required — **Not yet demonstrated**.

Criteria 1 is partially met (E retention gap exists), but criterion 2 is not met for current audit semantics. Candidate B remains Deferred. If future requirements demand full payload reconstruction (not just candidate verification), the retention gap should be revisited.

---

## Appendix: Test Execution Evidence

```
Test Run: 2026-10-08
Framework: xUnit 3.1.5+1b188a7b0a (.NET 10.0.12)
Total probe tests: 10
Passed: 10
Failed: 0
Duration: 0.9776 seconds

Full suite regression:
Total tests: 76
Passed: 76
Failed: 0
Duration: 0.9240 seconds
```

### Invariant Compliance

| # | Invariant | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Evidence carries full identity chain | Verified | Test #1, #5: TemplateId, TemplateVersion, ContractVersion, Purpose, ModelProfileRef, ProviderProfileRef all present |
| 2 | Input-Output hash linkage | Verified | Test #9: OutputEvidence.InputHash == InputEvidence.InputHash |
| 3 | Hash integrity | Verified | Test #2: candidate input verifies against recorded hash |
| 4 | Configured != observed provider | Verified | Test #6: ProviderProfileRef.Value != ProviderObservation.ProviderName |
| 5 | No credential leakage | Verified | Test #8: no sk-, Bearer, password, or secret in serialized evidence |
| 6 | Empty memory is representable | Verified | Test #7: EmptyMemoryProjection produces valid evidence |
| 7 | Optional fields do not block provenance | Verified | Test #10: missing ResponseId/FinishReason, core evidence complete |
| 8 | Metadata projection is traceable | Verified | Test #3: Metadata not null, Descriptors identifiable, hash covers metadata |
| 9 | Memory projection is traceable | Verified | Test #4: Memory not null, Memories non-empty when items provided |
| 10 | No General Agent Runtime change | Verified | No production code modified |
