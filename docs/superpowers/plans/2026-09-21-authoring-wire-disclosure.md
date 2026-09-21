# Bounded authoring wire disclosure

Issue #87 live baseline reached HTTP 200 with content, then the real parser
rejected the output. The retained diagnostic does not prove a particular malformed
field. Independent inspection establishes that the current prompt asks for 7g.v1
without disclosing the envelope or supported payload grammar.

This slice addresses protocol discoverability before broader context changes.
Keep JsonDescriptorAuthoringOutputParser and the existing source-generated provider
DTO context authoritative. Do not add a second parser, runtime reflection, provider
specific production options, or a new Agent Runtime. Do not change parser semantics
or promote optional fields to parser-enforced requirements in documentation.

The default prompt should include a compact generic wire reference for its supported
HumanTask and Workflow payload subset, plus valid generic examples. Serialize the
envelope from the existing DTO and its source-generated context. Any example payload
must be explicitly checked by the real parser in tests, including intended values
rather than merely successful parsing (silent defaults must not hide drift).
Examples describe syntax, not Asset target answers. Example IDs must be clearly
placeholders; existing refs must come from visible context, and refs to newly
proposed descriptors must identify drafts declared in the same response. State which runtime fields
are unsupported rather than suggesting that ignored fields will survive parsing.

Keep examples/internal helpers in the existing Authoring assembly; no public
abstraction-package split is justified. Public prompt input remains unchanged in
this first slice. Bump the default prompt template version consistently in the
builder and evidence options; preserve wire contract 7g.v1 and configurable options.
Verify the prompt and example serialization in the real NativeAOT fixture with
reflection disabled, retaining all previous markers.

Required validation: executable examples for both supported kinds and all supported
Workflow target shapes; default prompt template/evidence agreement; full Authoring
regressions; native publish/link/run. Do not claim live semantic success from these
tests. The existing bounded-context limitation (only Workflow and HumanTask refs,
no Form or bodies) remains explicit for a separate follow-up. No new live call is
required until this protocol slice is reviewed and verified.

Local verification on 2026-09-21 passed: Authoring66/66, ControlPlane556/556,
NativeAOT publish/link/run1/1 (AgentAuthoringWireDisclosure and all prior markers).
Final-head CI remains required before readiness. No live model success is claimed.
