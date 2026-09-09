# Asset two-stage review checkpoint

Latest local verification: `547fecf8` implements and verifies the bounded candidate
v2 NativeAOT profile. Final-head CI `34319577314` passed at `895feecd`; PR #92 is
ready for review and remains unmerged. Earlier checkpoints below preserve
the distinction between review findings and completed verification.

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

At `efbf1396`, the primary reviewer independently reran the built Asset E2E
assembly with `dotnet test samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.E2E.Tests --no-build --no-restore --logger 'console;verbosity=minimal'`:
10 passed, 0 failed (19 seconds), against the isolated local PostgreSQL database.
The compiled-pin test now uses the same alternate registry for both the generic
resolver and the Asset resolver. The outcome/fact test uses a real completed task
and directly invokes the consumer; its placeholder delivery context is not an
Outbox transport-integrity test.

The implementation agent's native-fixture log records one passing
`NativeAotBinary_RunsGoldenScenarioAndExits` run (3 minutes 20 seconds). The fixture
publishes, links and runs the default v1 Host through the existing script. The
primary reviewer checked that log and fixture, but did not independently repeat
that native run. Candidate v2 native behavior remains unverified.

The race test in `efbf1396` is still nondeterministic: the initial call may return
before the final task completes. Its passing result does not yet establish the
missed-transient-state regression. A deterministic completion-response barrier
is required before counting this correction as regression-verified.

Commit `f77b4995` replaces that concurrent-call test with a test-only decorator
over the real HumanTask runtime. It holds the initial completion response after
persistence, waits until the terminal review and Workflow have completed, asserts
the initial call is still blocked, then releases it. No production interface was
added. The primary reviewer independently reran this exact focused test with
`--no-build --no-restore --filter FullyQualifiedName~CandidateV2_InitialCompletion_ReturnsWhenFinalAlreadyCompleted`:
1 passed, 0 failed (3 seconds). This establishes the required ordering; an actual
run against reverted production code has not been recorded.

Manual CI run `34299002209` targets `70fa81a8`, before this final test correction.
It was still running at this checkpoint. The final branch needs its own CI result.
The remaining implementation gate for this PR is candidate v2 NativeAOT behavior
in the bounded verification profile. Default v1 remains unchanged. Do not mark
the PR ready or close #87/#88 from the results above.

On resumption, final-head CI run `34299642183` was verified successful at
`fdbcfe02b189ae7b02a4ceb476a84414811e779e`. The remaining implementation gate is
candidate v2 NativeAOT publish/link/run; the PR remains draft for that work.

## Candidate native verification — 2026-09-09

Implementation `547fecf8` keeps the candidate descriptor in a verification catalog.
Ordinary startup still selects v1. The candidate selector is rejected before Host
construction unless `--golden-scenario` is present. The terminating golden mode
binds to loopback and uses the same Host, generated HTTP endpoints, runtime,
consumer and business capability.

The default native script publishes once and runs v1, candidate v2 and the invalid
selector check. The existing AOT fixture requires both success markers, putting
candidate verification in the regular CI gate. Runs retain binaries/logs and use
separate SQLite files and PostgreSQL schemas.

The implementation agent ran `bash samples/AssetManagement/scripts/run-nativeaot-golden-scenario.sh`
and the full Asset E2E suite (10/10). The primary reviewer inspected the published
linux-x64 ELF and independently ran that binary with
`--golden-scenario --golden-scenario-profile=asset-v2-candidate`, a new SQLite file
and PostgreSQL schema: exit 0 and candidate success marker. The complete independent
log is `docs/review/2026-09-09-asset-candidate-native.log`.

Observations include initial approval remaining pending with zero terminal records
and one final task; final approval restoring Available or Assigned with the exact
prior assignment ID; initial rejection completing Workflow with its final step
Skipped; and final rejection producing one rejected record and no approved record.
Terminal assertions wait for durable Workflow completion.

Dependency AOT/trim warnings remain. This is executed candidate-host evidence, not
a blanket support claim for every dependency. It does not prove approved deployment,
process restart, arbitrary authored business correctness, real-model quality or
distributed exactly-once delivery. Final-head CI remains required for readiness.
