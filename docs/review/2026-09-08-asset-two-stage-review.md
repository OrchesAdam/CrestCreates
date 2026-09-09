# Asset two-stage review checkpoint

Base dependency: PR #91, commit `669470a9`. Its complete CI run 192 passed;
the PR is ready for review and has not been merged.

Implementation checkpoint: `a0462b2b`, candidate business behavior only. Default
production composition remains v1. This does not complete Issue #87 or prove
approved activation, model quality, runtime deployment or process restart.

The primary reviewer independently executed:

```sh
dotnet test samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.E2E.Tests --filter FullyQualifiedName~AssetTwoStageAcceptanceTests --disable-build-servers -m:1 -p:UseSharedCompilation=false --logger 'console;verbosity=minimal'
```

`ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING` selected a separate local PostgreSQL
test database. Result: 6/6 passed, including real HTTP asset operations, scoped
completion service, persisted Workflow/HumanTask state and SQLite terminal records.
This is not an all-HTTP completion claim. A pre-fix business failure has not yet
been independently recorded; do not invent red-test evidence from code inspection.

## Required review corrections before readiness

1. Share one typed Asset-owned role resolver. Resolving a runtime pin against its
   current registry and comparing only ID/version is insufficient to establish
   equality with the known compiled contract; reject changed contract content.
2. Initial completion must not wait solely for a transient pending final task:
   the final task can complete before polling sees it. Use the existing durable
   continuation acceptance proof; do not introduce a second receipt protocol.
3. Validate the consistency of the completed canonical outcome and the typed
   decision fact before using that fact to select a terminal/nonterminal action.
4. Keep compiled business-role identity distinct from governance approval in
   error messages. No enable-v2 boolean or caller-authorized package hash.

Add focused failure/race tests for these corrections, then verify the complete
Asset regression suite and the relevant native paths. Candidate NativeAOT evidence
is not yet established by the six integration tests. Preserve v1 descriptors and
the independent B01–B05, replay, tenant and authorization observations in the plan.

## Follow-up review — 2026-09-09

Commits `5073268e` and `7d656aa2` implement the shared compiled-contract resolver,
durable continuation acceptance check, and canonical outcome/fact consistency.
The resolver also maps invalid runtime pins to a consumer conflict rather than a
retry. Source inspection confirms these corrections; this does not replace their
pending failure/race verification or the complete regression and native runs.

The contract regression must use a valid alternate registry descriptor with the
same ID/version, so it tests the Asset compiled-contract boundary in addition to
the existing generic pin validator. The completion race must deterministically
reach the wait after final completion; concurrent calls alone can pass the old
implementation. Consumer fault injection is scoped evidence and must not be
reported as durable Outbox delivery verification.

PR #92 remains a draft stacked on PR #91. Its base does not match the CI workflow's
master-only pull-request trigger; run the existing workflow manually on the final
branch head and record that exact SHA. No workflow policy change is required.
