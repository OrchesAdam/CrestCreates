# Phase 8c — Legacy Dynamic API Boundary Architecture Note

**Date**: 2026-07-08
**Phase**: 8c
**Parent Spec**: 2026-07-08-phase-8c-legacy-dynamic-api-boundary-design.md

---

## 1. DynamicApi Legacy Path Definition

The following types and APIs constitute the legacy AppService-oriented Dynamic API path:

- `DynamicApiAotSourceGenerator` — Source generator for `[CrestService]` AppService HTTP exposure
- `DynamicApiGeneratedRegistryStore` — Static registry for generated providers
- `DynamicApiGeneratedRuntime` — Runtime helpers (ReadBodyAsync, EnsurePermissionAsync, etc.)
- `IDynamicApiGeneratedProvider` — Provider interface for generated endpoint data
- `DynamicApiEndpointDescriptor` — Endpoint metadata record
- `DynamicApiServiceDescriptor` — Service metadata record
- `DynamicApiActionDescriptor` — Action metadata record
- `AddCrestDynamicApi()` / `MapCrestDynamicApi()` — DI and endpoint mapping extensions

These APIs are kept for AppService compatibility. They are NOT marked `[Obsolete]` in 8c.

## 2. CapabilityEndpoint Mainline Definition

The following types and APIs constitute the Capability-first endpoint projection mainline:

- `CapabilityEndpointGenerator` — Source generator for `[CapabilityEndpointSpec]` / `[Get]`/`[Post]`/etc.
- `CapabilityEndpointDescriptor` — Canonical endpoint descriptor
- `CapabilityEndpointInputBinding` — Input binding descriptor
- `CapabilityEndpointBindingRegistry` — Generated binding registry
- `CapabilityEndpointMapper` — Endpoint mapping logic
- `ICapabilityEndpointDescriptorProvider` — Provider interface
- `AddCrestCapabilityEndpoints()` / `MapCrestCapabilityEndpoints()` — DI and endpoint mapping extensions

## 3. Forbidden Extensions to Legacy Path

The legacy DynamicApi path MUST NOT be extended with:

- Topology / activation / agent authoring / MCP projection semantics
- Capability runtime types (ICapabilityDispatcher, CapabilityEndpointMapper, etc.)
- Conversion from CapabilityEndpointDescriptor to DynamicApiEndpointDescriptor
- MapCrestCapabilityEndpoints wrapping MapCrestDynamicApi
- MapCrestDynamicApi wrapping CapabilityDispatcher

## 4. Allowed Coexistence Rules

- Legacy AppService path continues to run for existing `[CrestService]` consumers
- Legacy tests continue to prove compatibility
- Both mapping paths coexist without shared execution semantics
- New HTTP exposure should use the Capability-first endpoint projection path

## 5. Identity Independence

- CapabilityEndpointDescriptor.Id defaults to `endpoint:{CapabilityId}` but can be overridden via `EndpointId` attribute property
- CapabilityEndpointDescriptor.Version defaults to CapabilityVersion but can be overridden via `EndpointVersion` attribute property
- EndpointId and EndpointVersion are independent from CapabilityId and CapabilityVersion
- This allows the same capability to expose multiple HTTP endpoints with different identities

## 6. TargetProperty Separation

- `TargetProperty` on `[CapabilityEndpointInput]` is a source-generator-only property
- BindingEmitter uses TargetProperty for CLR property assignment (highest priority)
- ProviderEmitter only emits `CapabilityInputPath` in the descriptor (TargetProperty is NOT in the descriptor)
- This separates CLR assignment concerns from descriptor metadata concerns

## 7. CEP013 Hardening

- CEP013 is Error severity (not Warning) after 8c
- Level 1: covers all scalar-only input combinations (Route+Route, Route+Query, Query+Header, Header+Header)
- Level 2: covers route tokens + explicit Input on HTTP method attribute only (Level 2 does not read class-level `[CapabilityEndpointInput]`)
- Dictionary<string, object?> fallback is deleted from BindingEmitter
- Multi-scalar-no-body path generates fail-closed `throw new InvalidOperationException`
- Even if CEP013 is suppressed, generated code must fail-closed

## 8. VersionSelectionMode.Latest Semantics

- `VersionSelectionMode.Latest` resolves to the latest **active** version at runtime
- Inactive versions are excluded from resolution
- This is the runtime behavior, not a compile-time guarantee
- The enum name `Latest` (not `LatestActive`) is kept for brevity; the XML doc clarifies the active-only behavior

## 9. Migration Path

- 8d will provide AppService→Capability compatibility generator
- After 8d, samples can migrate from `[CrestService]` to Capability Endpoint projection
- After sample migration, legacy DynamicApi components can be considered for deprecation
- `[Obsolete]` on `AddCrestDynamicApi`/`MapCrestDynamicApi` is deferred to post-8d

## 10. BindingRegistry Lifecycle Boundary

CapabilityEndpointBindingRegistry is a process-wide generated registry.
It is populated by ModuleInitializer calls at assembly load time.
It does not support runtime unload, reload, or hot projection.
Dynamic rebuilding of the binding registry is deferred to a future phase.
