# Governed Agent Memory Tools

Phase 8d+ exposes seven generated tools through the normal Agent Tool →
Capability Pipeline path:

`build-pack`, `expand-source`, `compress-history`, `extract-candidates`,
`promote-candidate`, `reject-candidate`, and `supersede-item`.

Hosts register the runtime with `AddAgentMemoryRuntime()`, select the module
with `AddAgentMemoryTools()`, and provide the trusted tenant/user/execution
context, exact-version visibility scope, permission checker, and access
authorizers. History handles are issued by the Host through
`IAgentMemoryHistoryResourceHandleIssuer`; the model never supplies tenant,
actor, scope, or governance decisions.

All model-visible resource identifiers are opaque handles. Persistent Context,
Block, Candidate, and Memory identities are framework-owned. Source grants are
bound to tenant, principal, execution, scope, exact source range, and descriptor
closure; expansion returns `Unavailable` for missing, expired, revoked, or
out-of-scope resources without probing.

Mutating operations prepare security artifacts and every legal output branch
before the domain call. Curation is enabled only when the selected
`IAgentMemoryPromotionService` also proves `ConfirmedAtomic`; unknown commit
state is not mapped to an ordinary failure. Completed Agent Tool replay is
served by the invocation gate and does not execute the handler again.

The deterministic Memory Tool path is NativeAOT-verified on linux-x64. Durable
or distributed Memory stores, and LLM compression/extraction adapters, require
their own capability and atomicity evidence.
