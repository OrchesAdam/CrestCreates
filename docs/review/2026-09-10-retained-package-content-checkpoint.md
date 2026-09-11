# Retained package content acceptance

The isolated branch `codex/phase-10c-approved-asset-content-87` starts at
`cbbc18fd`, PR #93's final head. PR #93 full CI run `34442962164` passed on that
exact commit and the PR is ready for review, unmerged.

Commit `ec2aea6d` adds
`tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/RetainedPackageContentBindingAcceptanceTests.cs`.
The focused test passed 1/1; the primary reviewer independently reran it with
`dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --no-build --no-restore --filter FullyQualifiedName~RetainedPackageContentBindingAcceptanceTests --logger 'console;verbosity=normal'`.

The test observes actual package construction through the existing builder
interface, uses real materialization and canonical hash implementations behind
fixture collaborators, and checks preview/evidence artifact associations. It
verifies the actual visibility-filtered inventory, reproduces all three package
hashes, and detects a changed descriptor definition while retaining the original
claimed hashes. Draft store, catalog, auditor, and downstream review/activation
collaborators remain fixture substitutes. This is not Asset approval, host loading,
persistent artifact storage or automatic deployment evidence. No production API
is added.

The next slice must produce real review/evidence, complete the actual activation
review HumanTask through the production consumer, and check approved bindings
before handing the verified definitions to a fresh runtime. Preserve original
and altered content as separate candidates; do not rehash only stored manifest
entries or substitute a proposed-inventory report for authority. See
`docs/superpowers/plans/2026-09-09-asset-activation-handoff-acceptance.md`.
