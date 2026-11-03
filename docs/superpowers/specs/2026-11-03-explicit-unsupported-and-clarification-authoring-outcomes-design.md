# Explicit Unsupported / Needs-Clarification Authoring Outcomes

> Date: 2026-11-03
> Status: Proposed (rev.1, 待静态审查)
> Issue: #159
> Builds on: Phase 7g LLM-backed descriptor authoring adapter (`7g.v1`), the 2026-09-20 additive `baseVersion` input, #133 / PR #158 B12 evidence

## 1. Goal

Make "this intent is outside the supported authoring surface" and "this intent is missing information I must have"
**first-class, distinguishable terminal results** of descriptor authoring, so an ordinary caller can branch on the
result without guessing.

```text
AgentAuthoringContext
  -> LlmDescriptorAuthoringAgent
  -> JsonDescriptorAuthoringOutputParser
  -> DescriptorAuthoringResult
       status = Succeeded | SucceededWithDiagnostics   -> candidate drafts -> existing review/approval/activation chain
       status = UnsupportedIntent                      -> structured reasons, drafts empty, no governance entry point
       status = NeedsClarification                     -> structured questions, drafts empty, no governance entry point
       status = InvalidProviderOutput | ProviderUnavailable | Blocked | Failed   (unchanged meanings)
```

Today the last two rows do not exist. A model that refuses in prose or asks for the missing threshold produces
`InvalidProviderOutput` + `AUTHORING_INVALID_PROVIDER_OUTPUT` — the same observable shape as malformed JSON, a null
payload, or an empty `items` array.

The change is a contract change, not a heuristic: no string matching on refusal text, no "ask again" retry loop, no
runtime fallback.

## 2. Current Codebase Facts (verified on master `9909365a`)

| Fact | Location |
| --- | --- |
| Wire contract version literal `7g.v1` is the only accepted value | `src/Runtime/Agent/CrestCreates.Agent.Authoring/Parsing/JsonDescriptorAuthoringOutputParser.cs:24` |
| Status vocabulary is 6 members, none of which expresses "unsupported" or "needs input" | `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Authoring/DescriptorAuthoringStatus.cs:3-11` |
| Result carries `Status`, `Plan`, `DraftSet`, prompt evidence and diagnostics — no place for a structured reason list | `.../Authoring/DescriptorAuthoringResult.cs:6-13` |
| Envelope DTO is `contractVersion` / `promptInputHash` / `plan` / `items` only | `src/Runtime/Agent/CrestCreates.Agent.Authoring/Parsing/DescriptorAuthoringProviderOutputDto.cs:6-19` |
| Parse precedence: deserialize → contract version → prompt input hash (Blocker, `Blocked`) → `plan` present → `items` non-empty → per-item atomic failure | `.../Parsing/JsonDescriptorAuthoringOutputParser.cs:41-119` |
| `plan == null` and `items` null-or-empty both yield the identical `InvalidProviderOutput` + `AUTHORING_INVALID_PROVIDER_OUTPUT` class | same file, `:86-103` |
| Wire-vocabulary parsing is exact-string allowlisted (`TryParseDescriptorKind`, `TryParseOperation`), never `Enum.Parse` | same file, `:175-192` |
| Authoring diagnostic codes are a closed static table (12 codes) | `.../Abstractions/Authoring/DescriptorAuthoringDiagnosticCodes.cs:5-42` |
| Prompt text declares `7g.v1` in the system prompt, the closing instruction, and the wire-disclosure section; template is `descriptor-authoring-prompt-template-v2` | `.../Prompting/DefaultDescriptorAuthoringPromptBuilder.cs:15-23,89-92`、`.../Prompting/DescriptorAuthoringWireDisclosure.cs:15-36` |
| Contract version is part of the hashed prompt input payload | `.../Prompting/DefaultDescriptorAuthoringPromptInputFactory.cs:21` |
| Parser DTOs serialize through a hand-declared source-generated context (`GenerationMode = Metadata`, camelCase, ignore-null) | `src/Runtime/Agent/CrestCreates.Agent.Authoring/Json/DescriptorAuthoringParserJsonSerializerContext.cs` |
| Result/contract types serialize through `DescriptorAuthoringJsonSerializerContext` (includes `DescriptorAuthoringStatus`) | `src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/Json/DescriptorAuthoringJsonSerializerContext.cs:11-38` |
| `IDescriptorAuthoringAgent.AuthorAsync` has **no production caller**: consumers are the DescriptorControlPlane sample runner, the Asset harness and tests | grep of `IDescriptorAuthoringAgent` / `.AuthorAsync(` across `src`, `samples`, `tests` |
| Review suppresses `IsActivationEligible` on any Error/Blocker diagnostic; it is entered per draft, so zero drafts means no governance entry | `src/Metadata/Draft/CrestCreates.DescriptorDraft/DefaultDescriptorDraftReviewService.cs:211-235` |
| Native gate already round-trips the authoring wire contract: `AgentAuthoringOptionalReferences`, `AgentAuthoringUpdateBaseVersion`, `AgentAuthoringWireDisclosure` (asserts `ContractVersion == "7g.v1"`) | `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/Program.cs:341,428,538,544` |
| `7g.v1` literals on master `9909365a`: 5 `src` files, 11 test/fixture files, 2 Asset sample test files, and 10 doc files — of the docs only `docs/Feature/AgentControlPlane/usage-guide.md` is a live usage contract (2 registration samples); the other 9 are historical review/plan/spec records that must not be rewritten | grep `7g.v1` (see §9) |
| #133 locked the gap with `IntentMissingPolicyInformation_FailsClosed_AndClarificationIsNotDistinguishable`, and the oracle accepts only `ExplicitUnsupportedResult` keyed on the three intent-level codes | PR #158 head `1db29f48`: `samples/AssetManagement/tests/.../AssetUnsupportedIntentNegativeCaseTests.cs:151`、`AssetUnsupportedIntentOracle.cs` |

## 3. Non-Goals

- **No clarification round-trip in the framework.** Where the answer comes from (UI form, chat, ticket) and how it re-enters
  authoring stay application-side. This issue adds no session, no store, no state machine, no new tool.
- **No persisted evidence artifact** for an unsupported/clarification outcome. The result travels in-band; no new table,
  migration or canonical-hash profile.
- No change to review, package, activation-request, gate or approval semantics; no new permission.
- No provider SDK / transport change, no second prompt source of truth.
- No general evaluation platform, no unconditional live CI gate (the #133 boundary carries over).
- No support claim for other samples, models or RIDs beyond the executed evidence.

## 4. Contract: wire `8a.v1`

### 4.1 Envelope

`outcome` is a required discriminator. One of three shapes, mutually exclusive:

```json
{ "contractVersion": "8a.v1", "promptInputHash": "<echo of the request hash>",
  "outcome": "drafts", "plan": { "planId": "...", "intentText": "...", "assumptions": [], "plannedDescriptorRefs": [] },
  "items": [ { "descriptorKind": "HumanTask", "descriptorId": "...", "operation": "Create", "payload": { } } ] }

{ "contractVersion": "8a.v1", "promptInputHash": "<echo>",
  "outcome": "unsupported",
  "unsupported": { "reasons": [ { "code": "CAPABILITY_UNSUPPORTED",
    "message": "Condition-scoring that skips human review needs a new domain algorithm and a governance change.",
    "evidenceRefs": ["visible:wf_asset_maintenance_review:1"] } ] } }

{ "contractVersion": "8a.v1", "promptInputHash": "<echo>",
  "outcome": "needsClarification",
  "needsClarification": { "questions": [ { "id": "risk-threshold", "reasonCode": "MISSING_POLICY_INPUT",
    "question": "Which risk threshold decides the automatic maintenance outcome?",
    "missingInputs": ["riskThreshold"], "candidateValues": [] } ] } }
```

| `outcome` | required | forbidden | result |
| --- | --- | --- | --- |
| `drafts` | `plan` (non-null), `items` (≥1, exactly as today — the parser has no upper bound on `items` and this issue does not add one) | `unsupported`, `needsClarification` | `Succeeded` / `SucceededWithDiagnostics` |
| `unsupported` | `unsupported.reasons` (1..4) | `plan`, `items`, `needsClarification` | `UnsupportedIntent` |
| `needsClarification` | `needsClarification.questions` (1..4) | `plan`, `items`, `unsupported` | `NeedsClarification` |

Missing `outcome`, an unknown value, or a forbidden sibling present is **`InvalidProviderOutput`** with the exact reason in
the diagnostic message. The parser never drops a forbidden section to make an answer valid: "drafts must be empty on
refuse/clarify" is guaranteed by the shape, not by an assertion downstream.

For `unsupported` / `needsClarification` the result still populates the required `Plan` and `DraftSet` with the
established placeholder ids (`plan_intent_unsupported` / `plan_needs_clarification`, `draftset_intent_unsupported` /
`draftset_needs_clarification`) and **zero drafts**, mirroring the existing provider-failure path.

### 4.2 Closed code tables

`unsupported.reasons[].code`:

| code | meaning |
| --- | --- |
| `CAPABILITY_UNSUPPORTED` | needs a domain algorithm or descriptor kind outside the allowlisted surface |
| `OPERATION_UNSUPPORTED` | needs a lifecycle operation outside `Create` / `Update` |
| `GOVERNANCE_BOUNDARY` | would require self-approval, authority claims, or runtime mutation from authoring |

`needsClarification.questions[].reasonCode`:

| code | meaning |
| --- | --- |
| `MISSING_POLICY_INPUT` | a policy value the intent depends on is absent from the request |
| `AMBIGUOUS_TARGET` | more than one visible descriptor fits, or the target is unnamed |
| `CONFLICTING_REQUIREMENTS` | the stated requirements cannot both hold |
| `MISSING_REFERENCE_SELECTION` | a required reference exists but is not selected |

Unknown code ⇒ `InvalidProviderOutput`. Callers branch on `code`; prose in `message` / `question` is display material only
and is never a decision input. This keeps the R1 attribution rule intact: no verdict from natural language.

### 4.3 Bounds (deterministic, over-bound ⇒ `InvalidProviderOutput`)

| field | bound |
| --- | --- |
| `unsupported.reasons` | 1..4 entries |
| `needsClarification.questions` | 1..4 entries |
| `question.id` | 1..64 chars, `[A-Za-z0-9._-]` only, unique within the answer |
| `message` / `question` text | 1..400 chars after control-character normalization; no truncation |
| `missingInputs` / `candidateValues` / `evidenceRefs` | 0..8 entries each, 1..120 chars each |
| `missingInputs` | **1..8** for a clarification question (a question with no named gap is not actionable) |

Provider text stays bounded and is never stored into governance artifacts: no draft exists to carry it.

## 5. Contract: .NET surface

```text
DescriptorAuthoringStatus            + UnsupportedIntent, + NeedsClarification   (append; existing values keep their numbers)
DescriptorAuthoringResult            + DescriptorAuthoringUnsupportedOutcome? Unsupported
                                     + DescriptorAuthoringClarificationOutcome? Clarification
new (Authoring.Abstractions/Authoring) DescriptorAuthoringUnsupportedOutcome   { Reasons: IReadOnlyList<...UnsupportedReason> }
                                     DescriptorAuthoringUnsupportedReason     { Code, Message, EvidenceRefs }
                                     DescriptorAuthoringClarificationOutcome  { Questions: IReadOnlyList<...Question> }
                                     DescriptorAuthoringClarificationQuestion { Id, ReasonCode, Question, MissingInputs, CandidateValues }
DescriptorAuthoringDiagnosticCodes   + IntentUnsupportedValue        = "AUTHORING_INTENT_UNSUPPORTED"
                                     + IntentNeedsClarificationValue = "AUTHORING_INTENT_NEEDS_CLARIFICATION"
```

- Wire vocabulary (`outcome`, codes) is parsed with exact-string allowlists, following `TryParseDescriptorKind`; the .NET
  enum names stay independent of the wire words (`DescriptorDraftAuthorKind`/`DescriptorKind` precedent).
- Both new diagnostics carry one entry per reason/question with `Severity = Warning`. Warning (not Error) because the
  answer is a legitimate, contract-shaped outcome that needs human input; it is not a provider failure. Nothing in the
  review/activation path sees it — zero drafts means the review service is never invoked.
- `Snapshot()` deep-copies the new lists (`IReadOnlyList` → arrays) like the existing `Diagnostics` handling.
- `JsonSerializable` additions in **both** contexts: parser side (`DescriptorAuthoringProviderOutputDto` stays the only
  wire root; the new nested DTOs are reached through it) and contract side (`UnsupportedOutcome`, `ClarificationOutcome`,
  reason/question types, and the unchanged `DescriptorAuthoringStatus`). No `JsonSerializerOptions`-with-reflection path.
- **Decision point D1**: adding two status members vs. keeping `Blocked` and carrying a sub-code. This spec takes the new
  members: `Blocked` already means "structural/governance rejection of produced artifacts" (see the R1 oracle:
  `BlockedWithoutUnsupportedIntentDiagnoses`), and reusing it would force every consumer to re-read diagnostic codes to
  learn what the caller could not decide from the status — which is exactly the gap #159 exists to close.

## 6. Parser algorithm and precedence

Fixed order; the first failing check returns, with `Plan`/`DraftSet` empty and zero drafts:

```text
1 deserialize            -> AUTHORING_INVALID_PROVIDER_OUTPUT      ("Failed to deserialize ...")
2 contractVersion == 8a.v1 -> AUTHORING_INVALID_PROVIDER_OUTPUT    ("Unsupported contract version ...")
3 promptInputHash echo   -> AUTHORING_PROMPT_HASH_MISMATCH (Blocker), status Blocked
4 outcome discriminator present and known -> else AUTHORING_INVALID_PROVIDER_OUTPUT
5 outcome-specific shape (required present, forbidden absent) -> else AUTHORING_INVALID_PROVIDER_OUTPUT
6 bounds + code tables + id charset/uniqueness -> else AUTHORING_INVALID_PROVIDER_OUTPUT
7 per-outcome build:
    drafts               -> existing per-item atomic validation (unchanged semantics)
    unsupported          -> UnsupportedIntent  + one Warning diagnostic per reason
    needsClarification   -> NeedsClarification + one Warning diagnostic per question
```

Integrity before semantics is non-negotiable: a wrong-hash answer must never be interpretable as a clarification
(#133 R1-01). Version boundary is single-valued: `7g.v1` answers now fail as `Unsupported contract version`, the same
observable class the Asset classifier already records as `ContractVersionMismatch`. **Decision point D2**: no transitional
dual acceptance — a second accepted version is the dual-track the repo forbids, and every producer of that envelope is
inside this repository.

`LlmDescriptorAuthoringAgent` passes the parser result through unchanged (it already attaches prompt input/output
evidence before parsing); the provider-failure branch (§ "empty response", `LlmDescriptorAuthoringAgent.cs:113-172`)
keeps `ProviderUnavailable`.

## 7. Prompt disclosure

The prompt must teach exactly the contract the parser enforces — one source of truth, no extra prose channel:

- `LlmDescriptorAuthoringAgentOptions.DefaultPromptContractVersion` → `8a.v1`; `DefaultDescriptorAuthoringPromptInputFactory.ContractVersion` → `8a.v1`.
- `PromptTemplateVersion` → `descriptor-authoring-prompt-template-v3` (template text changes; the version constant is part
  of the evidence request, so the change is visible in prompt evidence rather than hidden).
- System prompt adds: when the intent needs a capability/operation outside the declared surface, answer
  `outcome=unsupported` with one reason per distinct obstacle from the allowed codes; when required information is
  missing, answer `outcome=needsClarification` with the gap named in `missingInputs`; in both cases emit **no** `plan`
  and **no** `items`, and do not describe the refusal in prose instead of using these outcomes.
- `DescriptorAuthoringWireDisclosure.BuildPromptSection()` adds the `8a.v1` envelope rules, both non-draft examples
  (serialized through the same parser context as every other example, so examples cannot drift from the DTO), and the
  closed code lists.

Because `contractVersion` is inside the hashed prompt input, **every recorded hash-bearing fixture must be re-derived**;
`PromptInputHashTests` asserts determinism, and any literal hash pinned in a fixture is a migration item for S1 (flagged
as the main mechanical risk of the version bump).

## 8. Governance invariants (what stays impossible)

| Invariant | Enforcement | Locked by |
| --- | --- | --- |
| An `unsupported` or `needsClarification` answer creates no draft | shape forbids `items`; parser builds an empty `DescriptorDraftSet` | unit matrix + Asset real-chain case (draft store, review-artifact store, control-plane audit, gate counters, inventory identity) |
| Such an answer cannot enter review / approval / activation | review runs per draft; zero drafts ⇒ nothing to review; `DescriptorDraftReviewService` untouched | Asset case asserts review-passed count 0 and gate calls 0 |
| Authoring text from these answers cannot become an artifact or claim | no draft ⇒ no review artifact ⇒ no package/evidence artifact | same as above |
| A provider failure is never re-labelled as clarification | separate statuses + precedence in §6 | unit decision table; the Asset oracle keeps its reject list |
| No new approval path | this issue adds no tool, permission, or gate branch | diff review: `ControlPlane` and `DescriptorDraft` untouched |

## 9. Files that must change

| Area | Files |
| --- | --- |
| Contract | `DescriptorAuthoringStatus.cs`, `DescriptorAuthoringResult.cs`, `DescriptorAuthoringDiagnosticCodes.cs`, new outcome/type files, `DescriptorAuthoringJsonSerializerContext.cs` (all under `CrestCreates.Agent.Authoring.Abstractions`) |
| Parser + DTO | `JsonDescriptorAuthoringOutputParser.cs`, `DescriptorAuthoringProviderOutputDto.cs`, `DescriptorAuthoringParserJsonSerializerContext.cs` |
| Prompt | `DefaultDescriptorAuthoringPromptBuilder.cs`, `DescriptorAuthoringWireDisclosure.cs`, `DefaultDescriptorAuthoringPromptInputFactory.cs`, `LlmDescriptorAuthoringAgentOptions.cs` |
| Unit tests | `OutputParserTests.cs`, `AuthoringContractTests.cs`, `WireDisclosureTests.cs`, `PromptInputHashTests.cs`, `LlmDescriptorAuthoringAgentTests.cs`, `UpdateBaseVersionParserTests.cs`, `ModelRequestProvenanceActivationProbeFixture.cs`, `Fixtures/CompanyCertificationLlmFixture.cs`, `CrestCreates.Agent.Prompting.Tests/PromptingRegistryTests.cs` (registered template pins `7g.v1`), `CrestCreates.Agent.ControlPlane.Tests/AssetHumanApprovalActivationHandoffAcceptanceTests.cs` — every `7g.v1` literal → `8a.v1` |
| Sample consumers | `CompanyCertificationAuthoringGoldenScenarioRunner.cs`, `FakeCompanyCertificationAuthoringAgent.cs`, `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs` |
| Real-chain evidence | Asset E2E: `AssetUnsupportedIntentOracle.cs` (accept the two new statuses; keep the reject list), `AssetUnsupportedIntentNegativeCaseTests.cs` (the gap-lock case flips to a positive distinguishability case + a retained "prose refusal is not a clarification" negative), `AssetAuthoringDiagnosticClassifier.cs`, `AssetDeepSeekLiveAuthoringEvaluationTests.cs` (B12 live entry verdict), probe fixtures — lands after #158 merges |
| Native gate | `CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/Program.cs` + its `.Tests` harness expectations |
| Docs | `docs/Feature/AgentControlPlane/usage-guide.md` (live usage contract: two prompt-registration samples pin `7g.v1`), `docs/Feature/CapabilityMap.md` (authoring capability row, four dimensions), `memory.md`, `AGENTS.md` 当前平台状态 (+ AOT tier line for the new native scenarios), `docs/review/2026-10-10-framework-architecture-review-follow-up.md` §5.1 B12 verdict, `docs/review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md` (new scenario row), 7g spec addendum. The historical `docs/review/2026-09-*` records and the 7h/base-version/wire-disclosure plans keep their `7g.v1` text as history |

## 10. Testing strategy

1. **Parser decision table (unit)** — every rejection class gets its own case with an exact diagnostic code and message
   prefix: missing/unknown `outcome`, `unsupported` + `items`, `needsClarification` + `plan`, unknown reason code, empty
   `missingInputs`, over-bound text, non-unique `question.id`, duplicate entries, `7g.v1` answer, hash mismatch with an
   otherwise valid `needsClarification` body (must stay `Blocked`/`PromptHashMismatch`).
2. **Positive round-trips (unit + source-generated JSON)** — all three outcomes deserialize to the exact status, carry the
   expected structured fields, and `Snapshot()` preserves them with no reflection (`JsonSerializerContext` types asserted,
   and the existing `ReflectionFallback_IsDisabled` style check in the native fixture).
3. **Real-chain Asset cases** — recorded `unsupported` and `needsClarification` answers on the Asset chain: status and
   codes asserted, and every governance counter measured through the existing test-side decorators (draft store,
   review-passed count, saved count, gate calls/successes, inventory identity). Green control stays: a supported intent
   still produces a reviewable draft and deploys only after human approval.
4. **Retained negative** — a prose refusal, and any JSON answer that omits `outcome` or emits `items: []` without an
   `unsupported` / `needsClarification` section, stays `InvalidProviderOutput`. The contract makes clarification
   *expressible*; it does not turn every refusal into a clarification.
5. **Native publish/link/run** — new scenarios with per-scenario `name:PASS` sentinels: `AgentAuthoringClarificationOutcome`,
   `AgentAuthoringUnsupportedOutcome`, `AgentAuthoringOutcomeShapeConflict`, plus the extended `AgentAuthoringWireDisclosure`
   (now asserting `8a.v1`). Negative controls exit non-zero and print no total sentinel.
6. **Mutation audit (each must redden only its target)** — (a) treat any `outcome` as `drafts` when `items` is absent;
   (b) drop the `forbidden sibling` checks; (c) move the hash check after the outcome check; (d) replace the closed code
   tables with permissive `Enum`/string parsing; (e) remove a new type from the JSON contexts.
7. **Live evaluation** — one credentialed run of the B12 case with the new prompt. If the model returns a structured
   outcome, the verdict moves to covered for that model/scenario; if it returns prose, the case records
   `ProviderOutputRejected_PendingManualAdjudication` honestly and the capability stays "candidate gap / unverified".
   Never converted to a pass by relaxing the oracle.

## 11. Implementation slices

| Slice | Content | Exit condition |
| --- | --- | --- |
| S1 | Contract + DTO + parser + code tables + bounds + both JSON contexts + prompt disclosure/template version + unit matrix + migration of every `7g.v1` literal | Authoring unit suite green with the new matrix; mutations (a)(c)(d)(e) each redden their target |
| S2 | Consumers (sample runner/fake agent), Asset real-chain cases incl. the #133 gap-lock flip, oracle/classifier rev, B12 live entry accepts the new statuses | Asset E2E green with PG; oracle rejects every non-intent outcome as before; mutation (b) reddens the shape-conflict case |
| S3 | Native AOT fixture scenarios + sentinel/negative-control wiring | `PublishAot` + native run green on linux-x64 for the executed scenarios |
| S4 | Docs and status sync (CapabilityMap, `memory.md`, `AGENTS.md`, §5.1 B12 verdict, #123 evidence matrix, 7g addendum) + optional credentialed live run recorded | Claims written to match executed evidence only |

Slices land in order on one branch after this design is approved; each slice is verified locally before push (CI stays
paused), and merging stays with the maintainer.

## 12. Acceptance criteria

- [ ] `DescriptorAuthoringResult` distinguishes `UnsupportedIntent` and `NeedsClarification` from `InvalidProviderOutput`
      without reading diagnostic text.
- [ ] Both new outcomes are structurally incapable of producing a draft (shape + parser + measured counters).
- [ ] Wire version is single-valued (`8a.v1`); no dual acceptance, no reflection JSON path.
- [ ] Full unit matrix, Asset real-chain cases, and native publish/link/run scenarios green; five named mutations each
      redden their target and revert.
- [ ] `7g.v1` migration complete with no stale literal outside historical docs.
- [ ] Support statements updated to match executed evidence; live verdict recorded either way without reclassification.
- [ ] No new framework abstraction, no evaluation platform, no persisted artifact, no review/approval/activation change.

## 13. Open decisions for the static audit

| ID | Question | This spec's position |
| --- | --- | --- |
| D1 | New status members vs. reuse `Blocked` + sub-code | New members (see §5) |
| D2 | Single accepted wire version vs. transitional dual read | Single, fail-closed |
| D3 | `Warning` vs `Info` severity for the new diagnostics | `Warning` (needs human action, not a failure) |
| D4 | Allow `plan` alongside unsupported/clarification | Forbidden — co-existence is a shape conflict |
| D5 | Bound numbers (1..4 reasons/questions, 400-char text, 8 entries/120 chars) | As specified; adjustable, must stay deterministic |
| D6 | Persist a clarification/unsupported evidence artifact | Out of scope (§3) |
| D7 | Whether `unsupported.reasons[].code` should also allow `OUT_OF_SCOPE_DOMAIN` | Not in v1; `CAPABILITY_UNSUPPORTED` covers it, and a closed table that grows by guess is worse |
| D8 | Caller loop for answers (re-entering authoring with补充输入) | Application-side; this issue ships the contract, not the conversation |
| D9 | Where the "no drafts with a non-draft outcome" invariant is enforced | Parser shape + tests; no public validation hook, no new extension point |
| D10 | Does `AgentAuthoringRequest` need an explicit "answered questions" field so a second call is attributable | Deferred; needs its own evidence question, and adding a field without a consumer is speculative |
