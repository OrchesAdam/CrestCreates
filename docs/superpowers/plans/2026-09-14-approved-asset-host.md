# Two approved Asset drafts to a fresh Host

Parent PR #96 head 9a6f9c0be08f356026f9377192595d2b33e2f7fa has local CP
556/556 and Asset E2E 12/12; CI is pending. Do not edit its worktree while CI runs.

This successor assembles the previously separate evidence. Keep the governance
harness in the Asset E2E assembly with production project references only. Use
real authoring parsing, draft persistence, Review/Preview/Evidence/Submit, human
CompleteAsync and hosted Outbox. All changes require a distinct human approval.

Start from a precisely checked deployed baseline containing Workflow v1 and its
compiled dependencies. Create Initial HumanTask using the complete parser entry;
its full contract must equal the compiled task resolver's target. After authoritative
Activated status and complete package hash checks, allow this definition into the
catalog used to review Workflow v2. Repeat the actual approval for Workflow v2.

The final Host inventory must be the exact union of verified deployed baseline
versions and both verified approved definitions. Retain v1 for old pinned instances;
replacement package materialization alone removes v1. No arbitrary descriptor may
be appended after validation. Instantiate the explicit inventory factory with these
actual objects, verify loaded hashes and execute the two-stage business flow.

Add focused rejection of absent/rejected required approvals and substituted
definitions before Host construction. Keep control-plane lifetime continuous in
this slice; no durable control-plane recovery or live model claim is permitted.
Do not add a production deployment API merely to simplify a test.

Primary designs/reviews and runs serialized tests; GPT-5.6 Luna high codes.

## Live model follow-up facts

DEEPSEEK_API_KEY was confirmed present without revealing its value. Existing
OpenAICompatibleDescriptorAuthoringModelClient and
AddOpenAICompatibleAuthoringProvider are reusable; configure credentialReference
explicitly as DEEPSEEK_API_KEY and ModelProfile.ModelName as deepseek-v4-flash.
Official docs verified 2026-09-14 list https://api.deepseek.com as the OpenAI-format
base URL and this model name: https://api-docs.deepseek.com/quick_start/pricing/.
No live call has been made. The repository has no Asset live-eval entry yet.
Reuse bounded ContextPack + LlmDescriptorAuthoringAgent; do not create another
agent loop. Keep recorded contract fixtures distinct from real model evidence.

## Explicit production activation gap

Read-only inspection found only InMemoryRuntimeActivationGate, registered by
AddAgentControlPlaneInMemoryStubs. It validates binding-hash shape and returns a
receipt; its documented scope excludes runtime state mutation. There is no existing
production IRuntimeActivationGate implementation that installs an inventory.
Therefore authoritative request status Activated proves the control-plane stub
accepted the approved request, not that production deployment happened.

The bounded two-draft Host handoff remains useful to specify the exact content
and business behavior required of a later activation owner. It cannot alone close
Issue #87's production activation requirement. Record this in the eventual #88
decision with an executable business case before designing any production adapter.

## 2026-09-20 composition refinement

The first approved package already contains the complete deployed baseline,
Workflow v1 and the newly approved initial HumanTask. The second approved package
contains the same dependencies and Workflow v2 (materialization replaces v1).
Therefore the acceptance can retain v1 without appending unchecked static data:
recheck both authoritative approved package inventories immediately before Host
construction and form their exact union keyed by namespace/id/version/kind. For
duplicate keys, require complete contract and definition hashes to match; reject
conflicts. No additional descriptor is allowed. Assert that both Workflow versions
and the parsed initial HumanTask are present, then pass that union into the
existing explicit-inventory factory and execute the two-stage business flow.

This remains a fixture-controlled handoff, not a production deployment adapter.
It is stronger evidence than separately testing approval and loading, while keeping
the missing production activation owner explicit.
