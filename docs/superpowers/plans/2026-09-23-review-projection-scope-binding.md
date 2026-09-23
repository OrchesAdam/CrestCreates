# Bind cached review projections to their visibility scope

Root review and Luna investigation found that ReviewDescriptorDraft stores a
creation-time visibility projection, while Get/List/report later return that data
after only checking the owner descriptor kind. ExecuteAsync obtains policy anew
per invocation. A narrower later policy can therefore receive nested facts retained
under an earlier broader policy. Existing tests check owner kinds, not this change.

Before making these artifacts durable, close this existing disclosure path. Capture
the existing AgentDescriptorVisibilityScope.ScopeFingerprint with each review.
Use exact scope equality, matching the package-preview pattern; do not implement a
second policy evaluator or invent subset semantics. A changed scope requires a new
review. Re-projecting from today's descriptor universe would silently change the
meaning of the stored review and is not the selected solution.

Get must preserve tenant and explicit owner checks, then fail closed on scope
mismatch without returning review contents. List must omit mismatched projections
and indicate trimming, preserving explicit-target authorization and missing-owner
failure semantics for candidate records. Report build must reject a latest review
from a different scope; it must not fall back to an older review, render cached
facts, or substitute the current draft for the captured review-time owner. A new
review under the current scope restores the normal path. Same-scope reads remain
unchanged. Caller-provided report rendering does not retrieve cached review data
and is outside this defect.

The same audit found GetPackagePreview returns a stored package after owner-kind
checks without comparing its already-captured ScopeFingerprint. Enforce exact scope
there too. SubmitActivationRequest must reject review or package references captured
under another scope through its existing reference-validation diagnostics, before
calling the activation service. Existing full hash/policy checks remain authoritative;
this does not redefine approval identity or change lifecycle recheck semantics.

Tests must use a mutable options factory to create under a broad scope then narrow
the policy while the owner kind stays visible and nested denied-kind information
was originally retained. Cover Get, List and report; scope expansion also requires
a new review under this conservative contract. Prove re-review recovery and stable
same-scope behavior. Reuse existing typed failure/audit conventions. Extend native
publish/link/run evidence for the changed path where feasible, and run full Control
Plane regressions. No database or model calls and no approvals or activation.

This is a security prerequisite for the durable artifact work, not that cutover.
Future durable artifacts must also persist scope identity plus original hash inputs,
captured owner identity and finite report facts. No duplicate draft payload protocol.
