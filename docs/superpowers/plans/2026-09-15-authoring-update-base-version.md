# Explicit authoring Update base version

Business case: a deployed Asset Workflow v1 gains an approved first HumanTask
step, producing Workflow v2. The draft must select existing v1 as BaseVersion and
carry payload v2 as ProposedVersion. The current parser derives both from the
payload; its DTO and prompt contain no independent baseVersion input. A sample
patch that changed parsed.BaseVersion afterwards was rejected during review.

First preserve an executed failure for explicit item baseVersion="1" and payload
version=2. Then make the smallest additive change in the existing 7g.v1 output
item: optional string baseVersion for Update. Map an explicit valid value to the
domain BaseVersion, while ProposedVersion continues to derive solely from payload.
Absent baseVersion retains the current same-version Update semantics; it does not
infer a previous version or authorize any mutation. Cross-version updates must
explicitly name their base, which normal review/materialization must resolve.

Reject explicit empty/whitespace, malformed or non-positive integer base versions
with existing structured provider-output diagnostics. Reject baseVersion on Create,
which has no prior definition. Do not add operations, runtime fallback, descriptor
lookup inside the parser, or another authoring protocol. Do not alter human review,
activation authority or runtime handlers. Keep version parsing culture-invariant.

Update the existing DTO/source-generated serializer and prompt contract guidance.
Cover explicit Update 1->2, current missing-field same-version Update, invalid base
versions, and Create misuse. Complete the real two-draft acceptance without editing
parsed envelopes. Extend the existing native authoring/parser fixture if production
parser changes, and run publish/link/run plus authoring/control-plane/Asset checks.

This is a proposed narrow repair pending executed baseline and primary review, not
a claim that production activation or Issue #87 is complete.
