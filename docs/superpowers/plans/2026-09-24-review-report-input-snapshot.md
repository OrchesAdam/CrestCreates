# Captured inputs for lazy review reports

The report builder currently consumes a projected DescriptorDraftReviewResult and
captured DescriptorDraft. The former contains interface-valued descriptor inventories
and a topology object with private indexes; the latter contains an abstract payload.
Neither is the selected durable report protocol. AgentReviewResultDto loses facts
needed by the report. Preserve lazy report generation by capturing finite typed facts.

Add versioned DescriptorReviewReportInputSnapshot in ControlPlane.Abstractions with
its own source-generated JSON context. It is data, never proof of visibility or
approval. Capture requires the existing VisibilityApplied precondition. An eventual
artifact service must authorize tenant/owner/scope before loading/building this data.

Capture the following facts, retaining null/empty distinctions and collection order:

- Tenant/draft identity, activation eligibility, separate validation result and
  review diagnostics. Reuse finite DescriptorDraftDiagnostic records; copy lists.
- Owner: tenant/draft/descriptor identity and kind, operation, author kind/id, status,
  proposed/base version. Preserve review-time status; no payload or current-draft lookup.
- Materialization: presence, success flag, ordered descriptor facts (namespace, ID,
  name, kind), and diagnostic list. No IDescriptor implementations in the snapshot.
- Topology: node/edge totals and counts by descriptor/relationship kind, not indexes.
- Impact: max severity and ordered affected name/kind/severity/reason facts.
- Compatibility: ordered findings containing level and subject ID.
- Governance: max decision, first decision and full typed first transition (its
  record ToString participates in existing report text), package-finding subject IDs.
  Derive IsAllowed/RequiresReview/IsBlocked from the same max decision.
- Package preview IDs and full canonical hashes; stable hashes. Clone collections;
  immutable scalar records may be reused. Do not retain arbitrary runtime graphs.

Avoid independently stored duplicate canonical identities. Derive the projected
DescriptorDraftReviewHashInput from these captured facts (review diagnostics, valid
and eligible flags, governance max decision and impact max severity) for report hash
computation. This is the hash of the projected report view. Original activation hash
inputs remain a separate outer-artifact responsibility from PR104. Report facts must
not be mutated while a stale independent ProjectedReviewHashInput is reused.

Keep Build(DescriptorReviewReportBuildRequest) as a compatibility entry point that
captures inputs and delegates to the single Build(snapshot) core. Add the snapshot
overload to the existing builder interface and document the requirement for external
custom implementations to update. Do not introduce a second rendering implementation
or a second builder interface as fallback. Existing tool service callers stay on
the same builder path; no persistence cutover is claimed by this change.

Validate supported version, structural required values and owner/review identity
consistency before rendering. Failed reviews and missing optional analysis remain
renderable. Copy mutable collections during capture; later source-list mutation
must not change stored facts. Preserve template/catalog version, contract version,
clock and report ID semantics at build time rather than pre-rendering a report.

Verification: existing section/content/identity report tests remain valid; add rich
facts roundtrip with fixed clock and complete report equality, explicit expectations
for materialization/topology/impact/compatibility/governance/package sections, null
and failed-review cases, source-mutation isolation and version/malformed rejection.
Equality only between two new entry points is insufficient evidence by itself.
Extend the actual NativeAOT fixture with snapshot JSON roundtrip and report building,
compare full output through generated JSON with fixed time; preserve real-service
scope checks from PR105. Run full Control Plane regressions plus publish/link/run.

Root designs/reviews/verifies; GPT-6 Luna high implements. No DB, model, approval,
activation, merge, or new public test-only extension point is involved.
