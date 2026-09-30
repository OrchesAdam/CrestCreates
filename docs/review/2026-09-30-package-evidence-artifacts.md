# Package and evidence artifact cutover

This slice follows PR107. Local verification is complete; final-head CI is pending.

Package previews and linked evidence now use one configured asynchronous artifact
store instead of separate ToolService content caches and resolver hash dictionaries.
An evidence record retains its exact package preview ID. Submission checks tenant,
draft, captured visibility scope and that parent identity before creating a request.
The default resolver derives hashes from validated retained content.

The official package serializer and three existing canonical package hashes remain
unchanged. A separate versioned canonical profile covers the exact retained package
JSON, full projected DTO and immutable envelope metadata. This detects changes to
snapshot relationships and diagnostics that the original package hashes omit. It
is an integrity check, not approval or authentication against a privileged writer
who can replace both payload and digest.

The explicit memory provider detaches records before validation and insertion.
The PostgreSQL provider uses additive V015, tenant-keyed immutable rows, a parent
foreign key, atomic pair insertion, exact structured-column checks and deterministic
latest-eligible selection. Neither provider falls back to an independent hash cache.
The reuse index excludes unbounded scope/catalog strings; exact comparisons remain
query predicates. A shared projected-pair factory captures the actual policy and
catalog for trusted sample producers without copying projection logic into samples.

## Verified evidence

- Runtime compilation passed after namespace and descriptor-entry field fixes.
- Real PostgreSQL focused integration tests: 14/14 passed, including recovery,
  collision rollback, parent binding, registration and content/metadata corruption.
  Log: `/tmp/crest-package-pg-focused-r2.log`.
- Final PostgreSQL regression: 463/463 passed in5m36s, including large catalog/scope
  persistence and exact reuse matching. Log: `/tmp/crest-package-pg-final-r2.log`.
- Final Control Plane regression: 575/575 passed, including narrow policy capture,
  mutable-graph isolation, collision rollback and fail-closed submission/resolution.
  Log: `/tmp/crest-package-cp-final.log`.
- CompanyCertification sample34/34, `/tmp/crest-package-sample-r2.log`.
- Boundary168/168, `/tmp/crest-package-boundary-r2.log`; excludes the CI execution
  ledger aggregation gate, which requires the complete CI test sequence.
- Final NativeAOT publish/link/run: 1/1 passed in 1m29s. The actual executable emitted
  `CONTROL_PLANE_PACKAGE_EVIDENCE_ARTIFACT_MEMORY_NATIVEAOT_OK` with reflection
  fallback disabled. Log: `/tmp/crest-package-native-final.log`; executable output:
  `artifacts/control-plane-json-aot-8bb8b0d9d3f54b239b6edafac6fb9606/run.log`.
  This is memory-store/service native evidence, not PostgreSQL NativeAOT evidence.

## Unresolved verification

The earlier 559-pass/9-fail run is preserved in `/tmp/crest-package-cp-full-r2.log`.
The six projection fixture failures were incomplete mock packages; repairs retained
their findings and visibility assertions. Three asset tests now use explicit response
IDs instead of inferring them from audit arrays. New factory tests also pass.

Full final-head CI, including JSON ownership and aggregate evidence-ledger gates,
is still required before marking the PR ready.

Review and package artifacts do not establish a durable approval workflow. Activation
requests, human-task linkage, transition concurrency, delivery replay and runtime
installation remain separate work. The activation gate is still in memory. No new
model request was made, and the retained real proposal was not modified.
