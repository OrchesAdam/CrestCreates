# Explicit Unsupported / Needs-Clarification Authoring Outcomes

> Date: 2026-11-03
> Status: Proposed (rev.3, 修订设计审计 R2-01 并落地三项裁定；待 S0 放行)
> Issue: #159
> Builds on: Phase 7g LLM-backed descriptor authoring adapter (`7g.v1`), the 2026-09-20 additive `baseVersion` input, #133 / PR #158 B12 evidence (merged to master as `91d6574e` on 2026-11-03)

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

## 2. Current Codebase Facts (verified on master `91d6574e`, 2026-11-03 — production code identical to `9909365a`; #158 changed tests and docs only)

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
| `7g.v1` literals on master `91d6574e`: 5 `src` files, 11 test/fixture files, 4 Asset sample test files, and 11 doc files — of the docs only `docs/Feature/AgentControlPlane/usage-guide.md` is a live usage contract (2 prompt-registration samples); the other 10 are historical review/plan/spec records that must not be rewritten | grep `7g.v1` (see §9) |
| #133 locked the gap with `IntentMissingPolicyInformation_FailsClosed_AndClarificationIsNotDistinguishable`, and the oracle accepts only `ExplicitUnsupportedResult` keyed on the three intent-level codes | master `91d6574e`（PR #158，head `1db29f48`）：`samples/AssetManagement/tests/.../AssetUnsupportedIntentNegativeCaseTests.cs:151`、`AssetUnsupportedIntentOracle.cs`、`AssetAuthoringDiagnosticClassifier.cs`、`ReviewerLiveOracleProbeTests.cs` |

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

| `outcome` | required members | forbidden members | result |
| --- | --- | --- | --- |
| `drafts` | `plan` and `items` **present with the declared kind** (`items` ≥1, exactly as today — the parser has no upper bound on `items` and this issue does not add one) | `unsupported`, `needsClarification` **absent** | `Succeeded` / `SucceededWithDiagnostics` |
| `unsupported` | `unsupported` present, `reasons` 1..4 | `plan`, `items`, `needsClarification` absent | `UnsupportedIntent` |
| `needsClarification` | `needsClarification` present, `questions` 1..4 | `plan`, `items`, `unsupported` absent | `NeedsClarification` |

**Presence, not truthiness.** "forbidden" means the member name exists in the raw envelope with *any* value — `null`,
`{}`, `[]` or content. "Required" means the name exists **and** carries the declared kind (`object` / `array`), so
`plan: null` for `outcome=drafts` is a required-member violation, not an "empty plan". A nullable DTO property cannot
express either distinction (see §4.4 for the verified mechanics), so both checks are made on the raw envelope before
projection.

Missing `outcome`, an unknown `outcome` value, a forbidden member, or a required member that is absent / `null` / of the
wrong kind is **`InvalidProviderOutput`** with the exact reason in the diagnostic message. The parser never drops a
forbidden section to make an answer valid: "drafts must be empty on refuse/clarify" is guaranteed by the shape, not by an
assertion downstream.

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

Normalization is defined first, because every bound below is measured on the normalized form:

```text
normalize(text): control characters (U+0000-U+001F, U+007F) -> space; runs of whitespace collapse to one space;
                 leading/trailing whitespace trimmed.
```

No truncation, no rewriting of visible content, and no natural-language judgement — normalization only makes the bounds
and the equality rules decidable. A field that is empty after normalization is **absent content**, not a valid short
value: `"question": "\t\n"` and `"missingInputs": [" "]` are rejected (rev.1 allowed them, since length alone was
measured on the raw text).

| field | bound |
| --- | --- |
| `unsupported.reasons` | 1..4 entries |
| `needsClarification.questions` | 1..4 entries |
| `question.id` | 1..64 chars, `[A-Za-z0-9._-]` only (charset checked on the raw value), unique within the answer |
| `message` / `question` text | 1..400 chars **after normalization**; non-empty after normalization |
| `missingInputs` / `candidateValues` / `evidenceRefs` | 0..8 entries each; 1..120 chars each after normalization, each non-empty |
| `missingInputs` | **1..8** for a clarification question (a question with no named gap is not actionable) |

Duplicate-entry equality is ordinal-exact on normalized values: duplicate `question.id` (raw compare, since the charset
has no whitespace) ⇒ rejected; duplicate reason entries (same `code` **and** same normalized `message`) ⇒ rejected as a
duplicated obstacle; two reasons sharing one code with different messages are allowed (a model may list two distinct
obstacles of the same class).

Provider text stays bounded and is never stored into governance artifacts: no draft exists to carry it.

### 4.4 Envelope facts are read before projection

A nullable-DTO shape check cannot implement §4.1, because deserialization has already destroyed the facts. Verified on
.NET 10 with `JsonSerializerIsReflectionEnabledByDefault=false` and a source-generated context (R1 audit record, plus a
7-input × lenient/strict reproduction run during rev.3; JIT mechanical behaviour only — it is not evidence that `8a.v1`
exists or that anything is NativeAOT-verified):

| Mechanism | Observed fact | Consequence for this contract |
| --- | --- | --- |
| `JsonDocument.Parse` | a duplicate member is preserved: `{"outcome":…,"promptInputHash":"WRONG","promptInputHash":"RIGHT",…}` enumerates 5 members with `promptInputHash` twice | the raw stage can see duplicates that the DTO cannot |
| `JsonDocument.Parse` | `"items":null` enumerates as a member with `ValueKind=Null`, while an absent `items` enumerates as no member at all | presence vs explicit null is distinguishable only here |
| STJ deserialization, default options | `JsonSerializerOptions.AllowDuplicateProperties` defaults to **`true`**; the duplicate envelope projects to `promptInputHash = "RIGHT"`, `outcome = drafts` | last-value-wins is the inherited behaviour the R1 audit characterized on the shipped `7g.v1` parser; the contract must not inherit it |
| STJ deserialization, `AllowDuplicateProperties = false` | throws `JsonException` naming the duplicate property — measured at root (`promptInputHash`, `outcome`) and inside material (`id`) — through the source-generated path, no reflection | real defense in depth, **and it means removing Stage A does not restore last-wins** (see §10.6 (f1)/(f2)/(g)) |
| `payload` as `JsonElement?` | strict Stage B reports duplicates **outside** `payload` only; the raw `payload` text is handed through verbatim, including `{"id":"FIRST","id":"SECOND"}` and `customField` | only Stage A can see a duplicate *inside* descriptor content — the vocabulary check stops there, the duplication check must not |
| unknown member | `items[]` element carrying `"extra":"surprise"` deserializes cleanly in both lenient and strict mode (silently dropped) | Stage A is the only place an unknown member is rejected |
| name casing | with a naming policy configured, binding matched `"Unsupported"` (capital) to the declared `unsupported` member in both configs | case-alias rejection is a Stage A job; the source-generated path cannot be relied on to be ordinal-exact |
| STJ DTO projection | absent `items` and explicit `"items":null` both project to `null`; an empty `"unsupported":{}` projects to a non-null object with `null` reasons | a null-check-only shape test would accept `outcome=unsupported` + `"items":null` as a valid refusal |

Parsing is therefore two-stage and deterministic, with no reflection, no scanner and no new production extension point:

```text
Stage A  JsonDocument.Parse(providerOutputText)          raw facts: member names, duplicates, ValueKind
Stage B  source-generated DTO deserialization            values: bounds, code tables, per-item material
         with AllowDuplicateProperties = false
```

Rules fixed for Stage A (they apply at every **protocol envelope** object level: root, `plan`,
`plannedDescriptorRefs[]`, `items[]`, `unsupported`, each reason, `needsClarification`, each question):

1. **Name matching is ordinal-exact** against the declared wire vocabulary (the camelCase names in §4.1/§4.2 and the table
   below). No case-insensitive matching, no aliases: `PromptInputHash` and `promptinputhash` are unknown members, not the
   declared one. The measured casing row above is why this rule cannot be delegated to Stage B.
2. **Duplicate member name at the same level ⇒ `InvalidProviderOutput`** (`Envelope contains duplicate member '…'`),
   whatever the two values are, and the duplicate check **also recurses into `payload` and its sub-objects** — a
   contradictory descriptor is not admissible even though its inner field names are exempt from the vocabulary table.
   Attribution note kept deliberately: were Stage A removed, strict Stage B would still reject duplicates outside
   `payload` (as a deserialization failure), so "an envelope with two hashes is refused" is not, by itself, proof that
   Stage A ran — the discriminating facts are the ones only Stage A can see (§10.6).
3. **Member outside the declared vocabulary ⇒ `InvalidProviderOutput`** (`Envelope contains unknown member '…'`),
   so a drifted provider answer cannot silently lose a section. This is a deliberate tightening over `7g.v1`, where
   unknown properties are ignored by deserialization; it is recorded as part of the `8a.v1` migration in §9.
4. **Required member** must exist with the declared `ValueKind`; **forbidden member** must not exist at all.
5. Stage B then enforces its own kind/bounds on the projected values. A `JsonException` from Stage B is a contract
   breach and is reported as `InvalidProviderOutput` with the deserialization message, never as a clarification.
6. The `payload` exemption is exactly three things: its inner field **names** are not checked against the envelope
   vocabulary, they are not added to any descriptor-content schema here, and materialization keeps its own
   supported/ignored boundary (`MaterializeHumanTaskPayload` / `MaterializeWorkflowPayload` read only what they support —
   an unknown payload field is *not* a promise that it survives or takes effect). It is not an exemption for duplicate
   keys, for the item-level `payload` member itself (must be present and of kind `object`), or for unknown members at the
   item level.

The declared vocabulary is one table, reused by the raw check and by the DTO mapping (a name listed nowhere is an unknown
member; a name listed twice in the table is a design bug caught by the §10.1 matrix):

| object level | declared members |
| --- | --- |
| root | `contractVersion`, `promptInputHash`, `outcome`, `plan`, `items`, `unsupported`, `needsClarification` |
| `plan` | `planId`, `intentText`, `assumptions`, `plannedDescriptorRefs` |
| `plannedDescriptorRefs[]` | `namespace`, `id`, `version` |
| `items[]` | `descriptorKind`, `descriptorId`, `operation`, `baseVersion`, `rationale`, `payload`, `evidenceRefs`, `memoryRefs`, `assumptions` |
| `unsupported` | `reasons` |
| `unsupported.reasons[]` | `code`, `message`, `evidenceRefs` |
| `needsClarification` | `questions` |
| `needsClarification.questions[]` | `id`, `reasonCode`, `question`, `missingInputs`, `candidateValues` |

`payload` stays an open `JsonElement` object: its inner keys are descriptor content, not envelope members, so the
**vocabulary** rule stops at the `payload` boundary while the **duplicate-key** rule keeps recursing through it and its
sub-objects (rule 2, rule 6). The disclosure examples must contain no member outside this table.

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
- JSON closure, stated precisely (rev.2, D13): the **authoritative roots** are `DescriptorAuthoringProviderOutputDto` in
  the parser context and `DescriptorAuthoringResult` in the contract context. The new nested DTOs (`Unsupported`,
  `Clarification`, reason/question types) are reachable from those roots, so the generator emits their metadata from
  reachability alone — declaring them additionally is documentation of intent, **not** a runtime prerequisite, and no test
  may treat a redundant annotation as the lock. A type must be registered explicitly only if some consumer serializes it
  **detached** from any registered root; none does today, and none is added by this issue. No `JsonSerializerOptions`
  path that relies on reflection, in either direction.
- Stage A reads the envelope through `JsonDocument` before any DTO exists, so — it depends on no reflection and no scanner, and its **native** execution is only claimed after the S3 scenario runs it (this design does not pre-issue a NativeAOT statement)
  the raw facts (§4.4) survive the projection that would otherwise merge them.
- **Decision point D1**: adding two status members vs. keeping `Blocked` and carrying a sub-code. This spec takes the new
  members: `Blocked` already means "structural/governance rejection of produced artifacts" (see the R1 oracle:
  `BlockedWithoutUnsupportedIntentDiagnoses`), and reusing it would force every consumer to re-read diagnostic codes to
  learn what the caller could not decide from the status — which is exactly the gap #159 exists to close.

## 6. Parser algorithm and precedence

Fixed order; the first failing check returns, with `Plan`/`DraftSet` empty and zero drafts:

```text
Stage A (raw envelope, §4.4)
1 JsonDocument.Parse            -> AUTHORING_INVALID_PROVIDER_OUTPUT ("Failed to deserialize provider output as JSON.")
                                   also: non-object root
2 member names: duplicate       -> AUTHORING_INVALID_PROVIDER_OUTPUT ("... duplicate member '…'")
   at any envelope level, and   (checked before any value, so contradictory envelopes never reach a branch; the recursion
   recursing through payload)    continues into `payload`, where Stage B cannot see)
3 member names: unknown/alias   -> AUTHORING_INVALID_PROVIDER_OUTPUT ("... unknown member '…'")
4 contractVersion member        -> absent/null/non-string/non-8a.v1 -> "Unsupported contract version …"
                                   (message prefix kept: the Asset classifier maps it to ContractVersionMismatch)
5 promptInputHash member        -> absent/null/non-string -> AUTHORING_INVALID_PROVIDER_OUTPUT
6 promptInputHash value         -> != expected echo -> AUTHORING_PROMPT_HASH_MISMATCH (Blocker), status Blocked
7 outcome member                -> absent/null/unknown value -> AUTHORING_INVALID_PROVIDER_OUTPUT
8 shape per outcome             -> required member absent/null/wrong kind, or forbidden member present
                                   (null, {}, [] or content) -> AUTHORING_INVALID_PROVIDER_OUTPUT

Stage B (source-generated DTO, AllowDuplicateProperties = false)
9 deserialize                   -> JsonException -> AUTHORING_INVALID_PROVIDER_OUTPUT (kind/type mismatch)
10 bounds + normalization       -> over-bound or empty-after-normalization -> AUTHORING_INVALID_PROVIDER_OUTPUT
11 closed code tables          -> unknown reason code / reasonCode -> AUTHORING_INVALID_PROVIDER_OUTPUT
12 id charset + uniqueness,    -> AUTHORING_INVALID_PROVIDER_OUTPUT
   duplicate-entry rules (§4.3)

Stage C (outcome build)
13 drafts              -> existing per-item atomic validation (unchanged semantics)
   unsupported         -> UnsupportedIntent  + one Warning diagnostic per reason
   needsClarification  -> NeedsClarification + one Warning diagnostic per question
```

Integrity before semantics stays non-negotiable: a wrong-hash answer must never be interpretable as a clarification
(#133 R1-01) — and, after step 2, neither can an answer that carries a wrong hash *and* a right hash in the same
envelope. **The order is itself part of the contract**: steps 2–3 decide before steps 4/6/7, so an envelope that both
repeats a member and carries a wrong hash is reported as a duplicate member, and an envelope with one wrong hash and an
unknown `outcome` is reported as a hash mismatch. If Stage A were absent, strict Stage B would still refuse duplicate
*envelope* keys — but only as a deserialization failure, never for duplicates inside `payload`, and with no precedence
guarantee. Those three are the facts only Stage A supplies, and they are what its mutations have to prove (§10.6). Version boundary is single-valued: `7g.v1` answers now fail as `Unsupported contract
version`, the same observable class the Asset classifier already records as `ContractVersionMismatch`. **D2**: no
transitional dual acceptance — a second accepted version is the dual-track the repo forbids, and every producer of that
envelope is inside this repository. **D9 (rev.2)**: the "non-draft outcome cannot carry drafts" invariant is enforced by
Stage A shape rules plus the source-generated projection, inside the parser; no public validation hook, no new extension
point.

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
- The disclosure must state the strictness the parser will enforce, because it is not inferable from a permissive JSON
  habit: send **only** the declared member names, **never** repeat a member, and when refusing or asking, **omit**
  `plan`/`items` entirely — writing `"items": null` or `"plan": {}` is a contract violation, not an empty section.
  The generated examples satisfy this by construction (`DefaultIgnoreCondition = WhenWritingNull` drops unset members),
  so the examples and the rules cannot disagree.

Because `contractVersion` is inside the hashed prompt input, **every recorded hash-bearing fixture must be re-derived**;
`PromptInputHashTests` asserts determinism, and any literal hash pinned in a fixture is a migration item for S1 (flagged
as the main mechanical risk of the version bump).

## 8. Governance invariants (what stays impossible)

| Invariant | Enforcement | Locked by |
| --- | --- | --- |
| An `unsupported` or `needsClarification` answer creates no draft | Stage A shape rules: a forbidden member is rejected **whatever its value** (`null`, `{}`, `[]`, content), so a refusal carrying `items` never reaches an outcome branch; the parser then builds an empty `DescriptorDraftSet` | unit matrix (§10.1, incl. the null/empty/duplicate forms) + Asset real-chain case (draft store, review-artifact store, control-plane audit, gate counters, inventory identity) |
| Such an answer cannot enter review / approval / activation | review runs per draft; zero drafts ⇒ nothing to review; `DescriptorDraftReviewService` untouched | Asset case asserts review-passed count 0 and gate calls 0 |
| Authoring text from these answers cannot become an artifact or claim | no draft ⇒ no review artifact ⇒ no package/evidence artifact | same as above |
| A provider failure is never re-labelled as clarification | separate statuses + precedence in §6 | unit decision table; the Asset oracle keeps its reject list |
| No new approval path | this issue adds no tool, permission, or gate branch | diff review: `ControlPlane` and `DescriptorDraft` untouched |

## 9. Files that must change

| Area | Files |
| --- | --- |
| Contract | `DescriptorAuthoringStatus.cs`, `DescriptorAuthoringResult.cs`, `DescriptorAuthoringDiagnosticCodes.cs`, new outcome/type files, `DescriptorAuthoringJsonSerializerContext.cs` (all under `CrestCreates.Agent.Authoring.Abstractions`) |
| Parser + DTO | `JsonDescriptorAuthoringOutputParser.cs` (two-stage: Stage A raw facts, Stage B strict DTO), new internal `DescriptorAuthoringEnvelopeFacts.cs` (member inventory over the declared vocabulary: duplicate / unknown / presence / kind rules; internal to the Authoring assembly, no public hook), `DescriptorAuthoringProviderOutputDto.cs` (+ `unsupported` / `needsClarification` DTOs), `DescriptorAuthoringParserJsonSerializerContext.cs`; the parser's deserialization options gain `AllowDuplicateProperties = false` |
| Prompt | `DefaultDescriptorAuthoringPromptBuilder.cs`, `DescriptorAuthoringWireDisclosure.cs`, `DefaultDescriptorAuthoringPromptInputFactory.cs`, `LlmDescriptorAuthoringAgentOptions.cs` |
| Unit tests | `OutputParserTests.cs`, `AuthoringContractTests.cs`, `WireDisclosureTests.cs`, `PromptInputHashTests.cs`, `LlmDescriptorAuthoringAgentTests.cs`, `UpdateBaseVersionParserTests.cs`, `ModelRequestProvenanceActivationProbeFixture.cs`, `Fixtures/CompanyCertificationLlmFixture.cs`, `CrestCreates.Agent.Prompting.Tests/PromptingRegistryTests.cs` (registered template pins `7g.v1`), `CrestCreates.Agent.ControlPlane.Tests/AssetHumanApprovalActivationHandoffAcceptanceTests.cs` — every `7g.v1` literal → `8a.v1` |
| Sample consumers | `CompanyCertificationAuthoringGoldenScenarioRunner.cs`, `FakeCompanyCertificationAuthoringAgent.cs`, `tests/Framework/Testing/CrestCreates.Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs` |
| Real-chain evidence | Asset E2E: `AssetUnsupportedIntentOracle.cs` (accept the two new statuses; keep the reject list), `AssetUnsupportedIntentNegativeCaseTests.cs` (the gap-lock case flips to a positive distinguishability case + a retained "prose refusal is not a clarification" negative), `AssetAuthoringDiagnosticClassifier.cs`, `AssetDeepSeekLiveAuthoringEvaluationTests.cs` (B12 live entry verdict), `ReviewerLiveOracleProbeTests.cs` — all present on master `91d6574e` after #158 merged |
| Native gate | `CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/Program.cs` (new scenarios, §10.5) **and** `CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.Tests`: today the harness publishes and runs one green artifact and requires exit 0 + the total sentinel, so S3 must extend it to name-check per-scenario sentinels and to execute the failure controls as separate runs. Without that harness change, a missing code-table scenario could still let the slice end green |
| Docs | `docs/Feature/AgentControlPlane/usage-guide.md` (live usage contract: two prompt-registration samples pin `7g.v1`), `docs/Feature/CapabilityMap.md` (authoring capability row, four dimensions), `memory.md`, `AGENTS.md` 当前平台状态 (+ AOT tier line for the new native scenarios), `docs/review/2026-10-10-framework-architecture-review-follow-up.md` §5.1 B12 verdict, `docs/review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md` (new scenario row), 7g spec addendum. The historical `docs/review/2026-09-*` records and the 7h/base-version/wire-disclosure plans keep their `7g.v1` text as history |

## 10. Testing strategy

1. **Parser decision table (unit)** — every rejection class gets its own case with an exact diagnostic code and message
   prefix. The envelope forms the R1 audit characterized become expected-red fixtures rather than prose promises:

   | raw envelope form | decided at | expected |
   | --- | --- | --- |
   | `outcome=unsupported`, `items` absent | — | `UnsupportedIntent` (control) |
   | `outcome=unsupported`, `"items": null` | §6 step 8 (forbidden member present) | `InvalidProviderOutput` |
   | `outcome=unsupported`, `"plan": {}` | §6 step 8 | `InvalidProviderOutput` |
   | `outcome=drafts`, `"unsupported": {}` | §6 step 8 | `InvalidProviderOutput` |
   | `outcome=drafts`, `"items": []` | §6 step 10 (required member present, under the ≥1 bound) | `InvalidProviderOutput` |
   | `promptInputHash` wrong first, right second | §6 step 2 (duplicate member) | `InvalidProviderOutput` — never `Succeeded` |
   | `outcome=needsClarification` then `outcome=drafts` with `"needsClarification": null` | §6 step 2 | `InvalidProviderOutput` — never enters the drafts branch |
   | `items` present twice, both non-empty | §6 step 2 | `InvalidProviderOutput` |
   | `contractVersion` `7g.v1` | §6 step 4 | `InvalidProviderOutput` (`Unsupported contract version …`) |
   | `contractVersion` wrong first, right second | §6 step 2 | `InvalidProviderOutput` |
   | single `promptInputHash` mismatch with an otherwise valid `needsClarification` body | §6 step 6 | `Blocked` + `AUTHORING_PROMPT_HASH_MISMATCH` |
   | unknown member, or alias case (`PromptInputHash`) | §6 step 3 | `InvalidProviderOutput` (`unknown member …`) |
   | `items[]` element with `"extra": …` (unknown member at the item level) | §6 step 3 | `InvalidProviderOutput` — measured: without Stage A this deserializes cleanly, so the row is a Stage-A-only fact |
   | `"Unsupported": {…}` alongside a valid `needsClarification` body | §6 step 3 | `InvalidProviderOutput` — measured: the source-generated binding matched the capitalized alias, so casing strictness exists only at Stage A |
   | `"payload": {"id":"FIRST","id":"SECOND"}` (duplicate inside descriptor content) | §6 step 2, recursing through `payload` | `InvalidProviderOutput` — Stage B hands the raw element through untouched, so only Stage A sees it |
   | duplicate nested `"target":{"id":…,"id":…}` inside `payload` | §6 step 2 | `InvalidProviderOutput` |
   | `"payload": null` at an item | §6 step 8 (item required member) | `InvalidProviderOutput` (`missing or invalid payload`) — the exemption covers inner names, not the member itself |
   | duplicate `outcome` **and** one wrong `promptInputHash` | §6 step 2 before step 6 | `InvalidProviderOutput` (`duplicate member 'outcome'`), **not** `Blocked`/hash mismatch |
   | one wrong `promptInputHash` **and** an unknown `outcome` | §6 step 6 before step 7 | `Blocked` + `AUTHORING_PROMPT_HASH_MISMATCH`, **not** the unknown-outcome diagnostic |
   | unknown `unsupported.reasons[].code` / unknown `questions[].reasonCode` | §6 step 11 | `InvalidProviderOutput` |
   | `"question": "\t\n"`, `"missingInputs": [" "]` | §6 step 10 (empty after normalization) | `InvalidProviderOutput` |
   | duplicate `question.id`; duplicated identical reason entry | §6 step 12 | `InvalidProviderOutput` |
   | 401-char text, 5 reasons/questions, 9 collection entries | §6 step 10 | `InvalidProviderOutput` |

2. **Positive round-trips (unit + source-generated JSON)** — all three outcomes deserialize to the exact status, carry the
   expected structured fields, and `Snapshot()` preserves them. The JSON evidence asserts the **required contract**, not
   the presence of annotations: metadata must resolve for every root the production path actually serializes
   (`DescriptorAuthoringProviderOutputDto` on the parser side, `DescriptorAuthoringResult` and the standalone-serialized
   types on the contract side) with reflection disabled, and the round-trip must preserve each declared wire name.
3. **Real-chain Asset cases** — recorded `unsupported` and `needsClarification` answers on the Asset chain: status and
   codes asserted, and every governance counter measured through the existing test-side decorators (draft store,
   review-passed count, saved count, gate calls/successes, inventory identity). Green control stays: a supported intent
   still produces a reviewable draft and deploys only after human approval.
4. **Retained negatives** — a prose refusal, and any JSON answer that omits `outcome` or emits `items: []` without an
   `unsupported` / `needsClarification` section, stays `InvalidProviderOutput`; and a `drafts` answer to the B12
   unsupported intent stays a rejection (`FabricatedCapabilityClaim` class in the Asset oracle). The contract makes
   clarification *expressible*; it does not turn every refusal into a clarification and does not make a structurally
   valid candidate draft business-correct.
5. **Native publish/link/run** — scenarios with per-scenario `name:PASS` sentinels:
   - `AgentAuthoringClarificationOutcome` and `AgentAuthoringUnsupportedOutcome` — structured outcome round-trip, status and
     fields read back inside the native process;
   - `AgentAuthoringOutcomeShapeConflict` — the **appearing** forbidden members (`null`, `{}`, `[]`, content) are rejected
     with zero drafts; the *absent* form is the green control of the two outcome scenarios above, so "absent" must not be
     listed as a rejection case;
   - one native execution of the Stage A vocabulary/duplication rules (an item-level unknown member, and a duplicate
     envelope key) so the raw-envelope machinery is not proven by JIT behaviour alone;
   - **`AgentAuthoringCodeTableRejection`** — one unknown `unsupported.reasons[].code` and one unknown
     `needsClarification.questions[].reasonCode`, each asserted to yield `InvalidProviderOutput` with zero drafts. This is
     the native execution of the closed-code requirement that Issue acceptance item 8 asks for; a unit row does not
     substitute for it;
   - `AgentAuthoringWireDisclosure` extended to assert `8a.v1` and the new disclosure rules;
   - existing `ReflectionFallback_IsDisabled` gate kept, so an incomplete JSON closure fails the native run rather than
     falling back.
   Two execution layers stay distinct, and the harness runs them separately: a **correct-rejection scenario** is a green
   run (the native process asserts the expected `InvalidProviderOutput` and exits 0 with its sentinel); a **gate negative
   control** (wrong expected value, or a fixture-side decoration that skips the code-table/shape check) must exit
   non-zero and print no total sentinel.
6. **Mutation audit (each must redden only its target, and it must redden *for the mechanism claimed*)** —
   - (a) bypass the `outcome` discriminator and treat a missing `items` as `drafts`;
   - (b) weaken the forbidden-member rule to a null/empty check — this must redden the `"items": null` and `"plan": {}`
     rows of §10.1, which is exactly the case a null-only implementation cannot distinguish;
   - (c) move the hash check after the outcome check — the discriminating input is **one wrong `promptInputHash` plus an
     unknown `outcome`** (§10.1 last row). The plain "single wrong hash, otherwise valid clarification" row cannot tell the
     order, because both orders still report the hash mismatch;
   - (d) replace the closed code tables with permissive `Enum`/string parsing;
   - (e) **cut a real closure edge**: remove the wire-root `JsonSerializable` registration from the parser context, or
     drift a `[JsonPropertyName]` on `outcome` / `unsupported` / `code` — the round-trip or the native run must fail;
   - (f1) remove **only** the Stage A unknown-member / casing rules, keeping presence, duplication and Stage B: the
     item-level `"extra"` row and the `"Unsupported"` alias row of §10.1 must flip from `InvalidProviderOutput` to an
     accepted terminal state — a functional change, not a message-routing change (measured: both forms deserialize
     cleanly once the raw check is gone);
   - (f2) remove **only** the raw duplicate-member rule: the "duplicate `outcome` + one wrong hash" row of §10.1 must flip
     from `InvalidProviderOutput` (duplicate member) to `Blocked` + `AUTHORING_PROMPT_HASH_MISMATCH`, which is the
     precedence Stage A owns;
   - (g) **joint negative control, explicitly labelled**: remove Stage A *and* relax Stage B to
     `AllowDuplicateProperties = true`. Only this combination restores last-wins acceptance on duplicate envelopes. It is
     recorded as a joint control and must never be reported as independent proof that Stage A exists or works.
     Retired on purpose, two of them: *removing a subtype's `[JsonSerializable]`* is not a lock (reachability already
     generates that metadata — verified with a root-only context under reflection-disabled), and *"delete Stage A, keep
     strict Stage B ⇒ duplicates fall back to last-wins"* is also not a lock: strict Stage B throws `JsonException` for
     duplicate envelope keys (measured at root for `promptInputHash` and `outcome`, and for `id` outside `payload`), so the
     outcome class stays `InvalidProviderOutput` and only the attribution moves — a change of diagnostic source is not a
     functional discriminator. Redundant annotations must not become runtime prerequisites, and no production test hook may
     be added to make one observable.
7. **Live evaluation (required acceptance step, not optional)** — at least one credentialed run of the B12 unsupported
   intent against the new prompt and `8a.v1`, with the recorded artifact retained outside the repository. B12 counts as
   covered **only** when that run yields `UnsupportedIntent` or `NeedsClarification` with a legal code-table entry,
   non-empty reason/question content, and zero drafts / zero review-passed / zero saved / zero gate calls / unchanged
   deployed inventory. Prose refusal (`InvalidProviderOutput`), a `drafts` answer to the unsupported intent, provider
   failures and budget exhaustion keep their own labels and the gap stays open. If no credential is available when this
   step is reached, the item stays **pending** — it is not auto-checked and the issue is not closed on the unit/native
   evidence alone. This does not make live evaluation a CI gate and does not require the model to be green: it requires
   the execution to have happened and its outcome to be recorded.

## 11. Implementation slices

| Slice | Content | Exit condition |
| --- | --- | --- |
| S1 | Contract + DTO + Stage A raw-envelope facts (duplicate / unknown / presence / kind) + Stage B strict DTO (`AllowDuplicateProperties = false`) + code tables + normalization and bounds + both JSON contexts + prompt disclosure and template version + §10.1 matrix + migration of every `7g.v1` literal | Authoring unit suite green with all 23 rows of the §10.1 matrix; mutations (a)(c)(d)(e)(f1)(f2) each redden their target **by the mechanism claimed**; (g) recorded as the joint control; both retired mutations in §10.6 stay retired |
| S2 | Consumers (sample runner/fake agent), Asset real-chain cases incl. the #133 gap-lock flip, oracle/classifier rev, B12 live entry accepts the new statuses **only** under the §10.4/§10.7 criteria | Asset E2E green with PG; oracle still rejects every non-intent outcome, and a `drafts` answer to the unsupported intent stays a rejection; mutation (b) reddens the null/empty forbidden-member rows |
| S3 | Native fixture scenarios §10.5 (`ClarificationOutcome`, `UnsupportedOutcome`, `OutcomeShapeConflict`, **`CodeTableRejection`**, one native run of the Stage A vocabulary/duplication rules, extended `WireDisclosure`) **plus the harness change** to name-check per-scenario sentinels and run the failure controls separately | `PublishAot` + native run green on linux-x64 for the executed scenarios; each named sentinel present in the green run; each negative control non-zero with no total sentinel |
| S4 | Docs and status sync (CapabilityMap, `memory.md`, `AGENTS.md`, §5.1 B12 verdict, #123 evidence matrix, 7g addendum) | Claims written to match executed evidence only; the live item is reported as pending if S5 has not run |
| S5 | Required live step (§10.7): one credentialed run of the B12 unsupported intent on the new prompt/`8a.v1`, artifact recorded outside the repo, verdict written per §10.4 | Run executed and recorded — covered, or honestly kept as unmet/pending. Absence of a credential keeps this item pending; it cannot be checked off, and the issue is not closed on unit/native evidence alone |

Slices land in order on one branch after this design is approved; each slice is verified locally before push (CI stays
paused), and merging stays with the maintainer.

## 12. Acceptance criteria

- [ ] `DescriptorAuthoringResult` distinguishes `UnsupportedIntent` and `NeedsClarification` from `InvalidProviderOutput`
      without reading diagnostic text.
- [ ] Both new outcomes are structurally incapable of producing a draft: forbidden members are rejected **as members**,
      covering `null`, `{}`, `[]` and content, decided on the raw envelope before projection (§4.1/§4.4/§6).
- [ ] Duplicate or unknown members are rejected before any value-level check, so a contradictory envelope (two hashes,
      two outcomes) cannot reach an outcome branch (§10.1).
- [ ] Wire version is single-valued (`8a.v1`); no dual acceptance, no reflection JSON path.
- [ ] Full unit matrix (23 rows), Asset real-chain cases, and native publish/link/run scenarios green — including the
      `AgentAuthoringCodeTableRejection` native scenario, a native run of the Stage A vocabulary/duplication rules, and the
      harness that names each sentinel and runs the failure controls separately; mutations (a)(c)(d)(e)(f1)(f2) each redden
      their target **for the claimed mechanism** and revert, and (g) is reported as the joint control it is.
- [ ] No mutation is credited on a change of diagnostic source alone: a case that stays in the same outcome class because a
      lower layer happens to reject the same input proves routing, not the layer (R2-01).
- [ ] JSON-closure evidence asserts the required contract (roots resolve, names round-trip, reflection disabled), not the
      existence of redundant annotations; the retired subtype-registration mutation is not used as a lock.
- [ ] `7g.v1` migration complete: no stale `7g.v1` literal in `src` or in production prompt/contract text, **except** the
      old-version rejection fixtures, which must keep the literal to prove the boundary — they are exempt by name in the
      grep check, and the check must not be satisfied by string concatenation or by deleting the negative case.
      Historical review/plan/spec records keep their text.
- [ ] One credentialed live run executed and recorded (S5). Covered only under the §10.4/§10.7 criteria; if credentials
      were unavailable the item stays pending and the issue is not closed on unit/native evidence alone.
- [ ] Support statements updated to match executed evidence; live verdict recorded either way without reclassification.
- [ ] No new framework abstraction, no evaluation platform, no persisted artifact, no review/approval/activation change,
      no production test hook.

## 13. Decisions and audit mapping

| ID | Question | Position / rev.2 landing |
| --- | --- | --- |
| D1 | New status members vs. reuse `Blocked` + sub-code | New members, existing numbers kept (§5). Audit agrees: callers must not infer the next action from diagnostic text |
| D2 | Single accepted wire version vs. transitional dual read | Single, fail-closed (§6 step 4); the old-version rejection negative stays as a fixture and is exempt from the literal sweep (§12) |
| D3 | `Warning` vs `Info` severity | `Warning` accepted, on the condition that the terminal state comes from the explicit status/structure and Warning is never reused as an approval input (§5) |
| D4 | Allow `plan` alongside unsupported/clarification | Still forbidden; rev.2 defines *forbidden = member present*, whatever its value, checked on the raw envelope (§4.1, §4.4, §6 step 8) — closing R1-01 |
| D5 | Bound numbers and their measurement | Numbers kept; rev.2 adds the normalization definition, non-empty-after-normalization, and duplicate-entry equality (§4.3) — closing the audit's whitespace-only hole |
| D6 | Persist a clarification/unsupported evidence artifact | Out of scope (§3); no new table or approval fact |
| D7 | Extend the reason-code vocabulary (e.g. `OUT_OF_SCOPE_DOMAIN`) | Not in v1; closed tables, unknown values rejected, and the rejection is executed natively in S3 (§4.2, §10.5) |
| D8 | Caller loop for answers | Application-side; no session engine here (§3) |
| D9 | Where the no-drafts-with-non-draft-outcome invariant is enforced | Inside the parser: Stage A raw facts + source-generated Stage B; no public validation hook, no production extension point (§4.4, §6) |
| D10 | `AgentAuthoringRequest` "answered questions" field | Still deferred; no consumer evidence yet |
| **D11 (new, rev.2)** | Two-stage raw-envelope parsing vs. presence flags on the DTO | Two-stage: `JsonDocument` facts then the strict source-generated DTO. Flags on the DTO would need an extra parallel structure that can drift from the wire names; `AllowDuplicateProperties = false` alone cannot separate absent from `null` (both verified) |
| **D12** | Unknown members: ignore (STJ default) or reject | **Ruled (R2): accepted** as an explicit `8a.v1` tightening, limited to the declared envelope levels, ordinal-exact, no aliases (§4.4 rules 1/3). Condition attached by the ruling and carried into plan S1: **recorded-answer field inventory first** — a field with real meaning enters the formal design, a descriptive extra leaves the recorded envelope, and no temporary allowlist may be introduced to keep old recordings green |
| **D13** | What counts as JSON-closure evidence | Required roots resolve + names round-trip + reflection disabled + native run; redundant subtype annotations are explicitly not a lock, and the mutation targets were retargeted to real closure edges (§10.2, §10.6) — closing R1-02 |
| **D14 (R2 ruling landed)** | Scope of the `payload` exemption | Accepted with boundaries: inner **names** are exempt from the envelope vocabulary; **duplicate keys are not exempt** and Stage A recurses through `payload`; the item-level `payload` member itself is still required with kind `object`; and no claim is made that unknown payload fields survive or take effect (materialization keeps its supported/ignored boundary) (§4.4 rule 6) |
| **D15 (R2 ruling landed)** | What may be cited as proof that Stage A is load-bearing | Only its exclusive facts: unknown member / casing alias, presence vs `null`, duplicates *inside* `payload`, and the raw-before-hash precedence — mutations (f1)/(f2) plus the §10.1 rows they target. Deleting Stage A while keeping strict Stage B is **not** such proof (measured: it still throws on envelope duplicates), and the (g) combination is labelled a joint control (§10.6) — closing R2-01 |

## Changelog

- **rev.3 (2026-11-03, 修订设计审计 R2-01 并落地三项裁定)** — the (f) mutation expectation was wrong as written: strict Stage B keeps rejecting
  envelope duplicates, so removing Stage A cannot restore last-wins. §10.6 now carries (f1) unknown-member/casing, (f2) raw-duplicate
  precedence and (g) as an explicitly labelled joint control; §4.4 gained the measured rows (payload duplicates pass through Stage B
  untouched, item-level unknown members deserialize cleanly, the naming policy makes binding case-insensitive) and the payload recursion
  rule; §10.1 grew to 23 rows with the two precedence combinations; §10.5 fixed the absent-vs-appearing wording and added a native run of
  the Stage A rules; §12 forbids crediting a change of diagnostic source as a mutation result; D12/D13 were restated as ruled items and
  D14/D15 record the payload scope and the evidence scope.
- **rev.2 (2026-11-03, 修订设计审计 R1 四项 P2)** — R1-01: presence/duplicate semantics moved to a raw-envelope stage with
  a verified mechanics table, strict DTO options, ordinal-exact name policy and an expanded decision matrix; R1-02: the
  JSON-closure mutation retargeted to real closure edges and the redundant subtype-registration mutation retired with the
  reason recorded; R1-03: `AgentAuthoringCodeTableRejection` native scenario plus the required AotFixture harness change,
  with correct-rejection and gate-negative-control layers separated; R1-04: live execution promoted from optional to a
  required acceptance step (S5) with tightened B12 success criteria. Also landed the audit's two wording items
  (normalization/whitespace, `7g.v1` literal sweep exception) and recorded D11–D13.
- **rev.1 (2026-11-03)** — initial proposal; baseline re-pointed to master `91d6574e` after #158 merged.
