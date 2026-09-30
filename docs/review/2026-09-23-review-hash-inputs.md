# Captured review hash inputs

The original review and the permission-filtered review are distinct representations.
Control Plane computes activation-binding review hashes before visibility projection,
then caches the projected result for user-facing retrieval and reports. Recomputing
the original hashes from a restored display DTO can therefore change diagnostics,
eligibility or other canonical inputs.

This slice captures the existing source-binding projection in a versioned typed
envelope. The integrity view is derived from the same captured fields and diagnostic
count, avoiding a second copy of identity/eligibility fields. The existing canonical
writers and v2 hash metadata remain the computation authority. Old result-based
calls and captured-input calls use the same path.

The input envelope has source-generated JSON metadata and rejects unsupported or
malformed input before computing hashes. It carries no executable descriptors and
confers no approval authority. A future persistent artifact must compare its hashes
against authoritative bindings and preserve owner/tenant/visibility information;
this change alone is not a durable review store or an activation-request store.

The public IDescriptorDraftReviewHashService interface gains capture and input-based
methods. External custom implementations must implement those methods; this is an
interface expansion, not a binary-compatible extension. Existing callers keep their
result-based methods, and both entry points share the existing canonical writers.
Validation now rejects missing required identity/diagnostic fields before hashing;
failed reviews and absent optional governance/impact results remain valid inputs.

Local validation so far:

- BuildTasks bootstrap passed (142 existing warnings, no errors).
- Review hash service focused tests: 18 passed, including pinned v2 values,
  source-generated roundtrip, source collection mutation, malformed input, and
  differing original/projected review identities.
- Full DescriptorDraft tests: 132 passed.
- Full Control Plane tests: 556 passed.
- NativeAOT wrapper: 1 passed; linux-x64 Release native publish/link/run completed
  in 1m36s, with reflection fallback disabled and the required new marker present.

The native fixture computes both hashes through the real hash service/computer
before and after JSON roundtrip and compares complete CanonicalHash records. It
also preserves failed-review fields and rejects an unsupported input version.
The test wrapper requires CONTROL_PLANE_REVIEW_HASH_INPUT_NATIVEAOT_OK in the
native executable's output. Local commands use serialized builds and the existing
environment-only NuGetAudit=false workaround; repository defaults are unchanged.
