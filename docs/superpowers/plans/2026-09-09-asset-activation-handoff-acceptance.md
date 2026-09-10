# Asset activation handoff acceptance — next slice

Design owner: primary reviewer. This is planned acceptance work after PR #92,
not evidence of a new framework gap or completed deployment.

Keep the control-plane process alive while starting and restarting a separate
runtime host. Do not conflate runtime restart with control-plane durable restart.

## Existing owners to exercise

Use the existing authoring context and draft authoring contracts to produce a
bounded candidate, then the draft store and review service to materialize the
complete change set. Package/evidence tooling owns artifact identities and hashes.
The activation request service, HumanTask review orchestrator, evidence rechecker
and activation gate own approval and activation decisions.

The Company Certification authoring runner is a wiring reference, not an oracle
for authorized deployment. Its final proposed inventory must not be accepted as
deployment authority simply because its report also says activation succeeded.

Source inspection confirms `IActivationBindingArtifactResolver` resolves hash
snapshots, and `DefaultAgentControlPlaneToolService.GetPackagePreviewAsync` returns
`DescriptorPackagePreview` with descriptor IDs and package hashes. These findings
do not prove that the existing APIs cannot express an authority-bound content
selection. Inspect the remaining owning APIs and test that association before
proposing an additional platform contract.

## First executable acceptance boundary

1. A deterministic candidate enters actual review and package/evidence tooling.
   All changed drafts must pass the required stages before deployment selection.
2. Submit a tenant-bound request requiring human review. Observe its pending state
   and real review task. An agent's request cannot approve itself.
3. Complete review through the existing HumanTask path. Observe the authoritative
   request and evidence recheck results; do not synthesize a success flag or copy
   a manually assembled approval decision from the golden runner.
4. Select runtime descriptor content by the approved artifact/version identity
   using the owning APIs. Demonstrate that replacing the proposed content, changing
   evidence, substituting another tenant's artifact or approving only part of the
   set cannot produce a v2 host.
5. Start a fresh Asset host only after that association is established. Run the
   independent business cases, including baseline retained pins and new v2 pins.

If step 4 cannot be implemented with the existing boundaries, record the concrete
failed attempt, available APIs and missing guarantee for #88. A missing interface
name or an in-memory implementation alone is insufficient evidence. Do not create
an always-failing test solely to justify a predetermined architecture.

Once fresh-host handoff is proven, separately interrupt/restart the runtime using
the same authorized descriptors and durable state. Keep live-model quality as a
separate experiment; deterministic authoring and structural review do not prove
that DeepSeek understood the requested business semantics.


## Existing-boundary candidate retention (reviewed 2026-09-10)

Read-only review found a viable experiment without a new artifact-store API.
Install an acceptance-only decorator for the existing IDescriptorPackageBuilder
before authoring/review/preview. Delegate to DefaultDescriptorPackageBuilder and
retain its actual request and returned package. Control-plane previews use draft
identity/version/CreatedAt metadata and a visibility-filtered proposed inventory;
retain that actual input rather than guessing from the current catalog.

The retained data is an observation of real package construction, not approval
or production persistence. Obtain authoritative activation status under the same
tenant and bind the retained package to that request's artifact/hash snapshot.
Recompute all three package hashes through the existing canonical hash computer;
do not trust the package's stored Hashes property. Also rebuild manifest entries
from the exact retained descriptor definitions using the real stable hash builder.
Rehashing only a stored manifest cannot detect mutation of the retained inventory.
Reject additional/missing refs, different versions, changed definitions, cross-
tenant/request substitutions, incomplete required approvals and changed evidence
before creating the new host. Revalidate the same content before runtime restart.

The acceptance runner must pass only the verified inventory to the host. A gate
success flag, proposed-inventory report, or matching package hash with unverified
runtime definitions is insufficient. Establish the checked-to-loaded association
explicitly; do not claim arbitrary concurrent mutation is prevented by a sequential
fixture. Full package snapshots contain refs/hashes, not executable definitions,
so retaining only their JSON cannot reconstruct the runtime inventory.

This experiment keeps the control plane alive and does not claim durable artifact
lookup, control-plane restart, automatic production deployment, or protection for
snapshot relationship payload outside the canonical package hash coverage.
First prove the bounded handoff before proposing a platform API. Production API
changes remain contingent on an executable unmet requirement.
