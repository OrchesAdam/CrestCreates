# Governed Agent Memory Tools

Phase 8c+ exposes four MCP tools for context and memory recall/expansion:

`ctx_recall`, `memory_recall`, `ctx_expand`, `memory_source_expand`.

Phase 8d+ exposes seven generated tools through the normal Agent Tool →
Capability Pipeline path:

`build-pack`, `expand-source`, `compress-history`, `extract-candidates`,
`promote-candidate`, `reject-candidate`, and `supersede-item`.

Hosts register the runtime with `AddAgentMemoryRuntime()`, select the module
with `AddMcpMemoryTools()` (8c+) or `AddAgentMemoryTools()` (8d+), and provide
the trusted tenant/user/execution context, exact-version visibility scope,
permission checker, and access authorizers. History handles are issued by the
Host through `IAgentMemoryContextHandleIssuer`; the model never supplies tenant,
actor, scope, or governance decisions.

All model-visible resource identifiers are opaque handles. Persistent Context,
Block, Candidate, and Memory identities are framework-owned. Source grants are
bound to tenant, principal, execution, scope, exact source range, and descriptor
closure; expansion returns `Unavailable` for missing, expired, revoked, or
out-of-scope resources without probing.

Read operations (ctx_recall, memory_recall) construct Handle and Grant plans
before Coordinator preparation. Requested keys, Coordinator-confirmed keys, and
caller-visible credentials must be equal. Missing, extra, duplicate, or
binding-mismatched artifacts fail with `handle-contract` / `grant-contract`
errors, revoke batch-created artifacts, and never return a partial Completed
result. Same-SourceKey deduplication ensures one Grant per unique source
regardless of how many Blocks or Memory Items reference it.

Mutating operations prepare security artifacts and every legal output branch
before the domain call. Curation is enabled only when the selected
`IAgentMemoryPromotionService` also proves `ConfirmedAtomic`; unknown commit
state is not mapped to an ordinary failure. Completed Agent Tool replay is
served by the invocation gate and does not execute the handler again.

The deterministic Memory Tool path is NativeAOT-verified on linux-x64. Durable
or distributed Memory stores, and LLM compression/extraction adapters, require
their own capability and atomicity evidence.
