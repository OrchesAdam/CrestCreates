# Explicit authoring Update version validation

Problem: the parser previously assigned payload version to both BaseVersion and
ProposedVersion. The real Asset v1->v2 case failed with expected base 1, actual 2.
A fixture workaround that changed the parsed draft was rejected and removed.

The existing 7g.v1 item now accepts optional string baseVersion for Update. A
positive integer selects the existing definition; payload version still determines
the proposal. Missing input preserves existing same-version semantics. Invalid
values and use on non-Update items fail with structured diagnostics. No authority
or materialization checks were bypassed.

Verification so far:

- Focused parser: 8/8; full Authoring: 64/64 (executed 2026-09-15).
- Combined two-draft approval and fresh Host business execution: 2/2 (2026-09-20).
- Full ControlPlane: 556/556 (executed 2026-09-20).
- Existing NativeAOT wrapper: 1/1 (publish/link/run, 2026-09-20).
- Native run includes AgentAuthoringUpdateBaseVersion:PASS and every previous marker.
- Binary inspected as x86-64 ELF. Artifact directory:
  artifacts/control-plane-json-aot-adeb8cb90c2c4963ad74685b846b1309.

The two-draft test also exposed a fixture binding error: DraftVersion was fixed
at 1. The production rechecker correctly classified the v2 request as Stale. The
fixture now binds actual validated ProposedVersion in both places; the production
rechecker remains unchanged. Foreign-harness and altered-content receipts are
rejected before catalog changes. The graph now reflects the current checked catalog.

Full Asset PostgreSQL E2E passed 14/14 (2026-09-20). Final-head CI is pending. InMemoryRuntimeActivationGate is a development stub; none of this
is a claim of production deployment, durable control-plane recovery or live-model
quality. Local outer builds use NuGetAudit=false for the historical audit-network
limitation; native inner publish uses normal audit and repository defaults are unchanged.

The final composition rechecks both approved packages before Host construction.
Their exact union contains baseline Workflow v1 from the first approved package
and Workflow v2 from the second. Duplicate identities require identical complete
contract/definition hashes; no static descriptor is appended after validation.
The Host registry uses the exact v2 object from the second retained package. Real
HTTP asset creation, maintenance request and two HumanTask completions finish with
asset status Available. Approval and business requests use the same fixture tenant.
This is controlled fixture handoff evidence, not a production activation adapter.
