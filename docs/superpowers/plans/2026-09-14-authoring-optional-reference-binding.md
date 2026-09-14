# Authoring optional reference binding

The complete Asset candidate comparison fails because missing InputSchema and
OutputSchema become nullable references containing an empty/default value. The
compiled target contract has null for both. The earlier partial parser test did
not check these properties; do not treat it as complete equivalence evidence.

Fix the existing JsonDescriptorAuthoringOutputParser only. Add a private typed
optional-reference helper that returns null for missing properties or explicit
JSON null and otherwise delegates to the existing reference parser. Use it for
HumanTask InputSchema and OutputSchema. Keep required Interaction parsing and
valid reference id/version behavior unchanged. Do not normalize arbitrary malformed
values to absence, add a second parser, or repair descriptors in test/host code.

Validate missing/null/valid optional references and complete Asset descriptor
equivalence. Then rerun the real control-plane handoff and human-completion Outbox
tests, followed by full related suites and final-head CI. Primary owns review and
execution; GPT-5.6 Luna high owns implementation. PR #95 remains draft until its
final scope has passed the required gates.
