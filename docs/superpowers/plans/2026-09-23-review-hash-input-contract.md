# Review-time canonical hash input contract

This first production slice of the durable-review design preserves canonical hash
inputs independently from permission-filtered display data. The current tool hashes
the original review, but caches a visibility-projected review for Get/report. A
serialized display result is therefore not a faithful source for original hashes.

Reuse the existing ReviewResultSourceBindingProjection as the captured data. It
contains all fields needed by both current canonical views; the integrity view's
DiagnosticCount is derived from its Diagnostics.Count. Do not store a second,
independently mutable copy of tenant/draft/eligibility/count fields.

Add a versioned typed review-hash-input envelope with a defensive capture operation
and source-generated JSON context. The canonical hash service gains capture and
input-based computations; existing result-based methods delegate to the same path.
Keep existing v2 canonical writers, shape versions and hash values unchanged. Input
contract version is a serialization version and must not alter canonical hash metadata.
Reject unsupported input versions or malformed required fields before hashing. A
captured input is data to verify against authoritative hashes, never approval authority.

The envelope deliberately excludes raw descriptor definitions, report rendering,
policy authority and caller credentials. It is not the complete durable review
artifact, and no activation-request or database persistence is claimed by this slice.
The remaining artifact must bind its original hash inputs, owner and projected report
under the appropriate tenant/visibility semantics described in PR103.

Verification must show exact old/new canonical hashes, source-generated JSON
roundtrip, isolation from later mutation of the original diagnostics collection,
rejection of unsupported/malformed input, and preservation of original hashes when
the visible review has fewer diagnostics/different eligibility. Extend the existing
ControlPlane.JsonContracts NativeAOT publish/link/run fixture with a marker and
wrapper assertion; JSON-only tests do not suffice. Run relevant Draft and Control
Plane regressions, and document actual results without a durability claim.

Implementation is delegated to GPT-6 Luna high. Root reviews and executes tests.
No provider/model call, human completion, approval, activation or merge is involved.
