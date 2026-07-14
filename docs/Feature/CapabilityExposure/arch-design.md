# Capability Projection and Exposure — Architecture Design

> **Phase 8** overall architecture reference for Capability Endpoint Projection, AppService Compatibility, and future Agent/MCP Tool projection.
> Last updated: 2026-07-14.

---

## 1. Background

CrestCreates defines business operations as `CapabilityDescriptor` instances — self-describing units of execution that carry an identity, version, schema, permissions, and handler binding. Prior to Phase 8, the only HTTP-driven consumption path for these capabilities ran through the legacy Dynamic API pipeline: AppService interfaces were scanned, conventions were applied, and endpoints were materialized at runtime via a generated provider. That path worked but was tightly coupled to the AppService abstraction: every capability had to be expressed as an AppService interface method first.

Phase 8 establishes a direct projection model. `CapabilityDescriptor` becomes the universal contract; HTTP endpoints, legacy AppService surfaces, and eventually MCP/Agent tool surfaces all become projections — distinct representations of the same capability, each with its own binding rules, authorization mode, and result envelope. The pipeline stays singular: every projection eventually calls `ICapabilityDispatcher.DispatchAsync()`, which delegates to the shared `CapabilityPipeline` middleware chain.

## 2. Design Goals

1. **Unified runtime mainline.** Every capability execution, regardless of invocation source, goes through `ICapabilityDispatcher` → `CapabilityPipeline`. There is no second execution path.
2. **Projection separation.** HTTP binding details (route pattern, HTTP method, input binding, result envelope) live in projection descriptors and binding contracts — never in the core `CapabilityDescriptor`.
3. **Compile-time generation.** Source generators produce descriptor providers and binding delegates at build time. Runtime reflection scanning is legacy; generated code is the mainline.
4. **Compatibility as a bridge, not a second path.** Existing `[CrestService]` AppServices can opt into the capability pipeline via a one-way compatibility projection. The legacy Dynamic API output is suppressed for projected services so that capability and legacy paths never dual-serve the same method.
5. **Trimming-safe, NativeAOT-oriented.** Static registries, generated delegates, and `JsonTypeInfo<T>` overloads replace runtime reflection. The pipeline resolves handlers from compile-time registrations. Current deployment guarantee is `PublishTrimmed`; full NativeAOT is a future target.
6. **Fail-closed by default.** Missing bindings, missing capabilities, diagnostic errors, and invalid states all cause startup failure or per-service generation suppression — never a silent fallback.

## 3. Core Principles

| Principle | Enforced by |
|---|---|
| Generated path is the mainline | SG produces all providers and binding delegates; `[ModuleInitializer]` registration; no runtime reflection scanner |
| Projection never owns execution logic | `CapabilityEndpointDescriptor` has no handler, no schema, no permission — only routing and binding metadata |
| Compatibility projection is one-way | `AppService → Capability`, never reverse |
| Custom result contracts only govern success | `MapResult()` checks `!result.IsSuccess` first; pipeline failures always use unified mapper |
| BindingRegistry is process-wide | Static `ConcurrentDictionary`; no runtime unload/reload; internal `Reset()` for test isolation |
| Service-level fail-closed generation | Any `Error` diagnostic skips all codegen for that service class |
| `ProjectionKind` is governance metadata | `DefinitionOnly` in canonical hash — does not affect runtime contract |

## 4. Projection and Exposure Model

The architecture defines three projection tracks. Tracks 1 and 2 are implemented; Track 3 is specified for future evolution.

### 4.1 Track 1 — Native Capability HTTP (Implemented, Phase 8a/8b/8c)

```
[CapabilityEndpointSpec]
    │
    ▼
CapabilityEndpointGenerator (SG)
    │
    ├── DescriptorProvider<CapabilityEndpointDescriptor>  ──►  ICapabilityEndpointRegistry
    └── BindingContract (BindInputAsync delegate)          ──►  CapabilityEndpointBindingRegistry
    │
    ▼
MapCrestCapabilityEndpoints()
    │
    ├── Registry bootstrapper flushes providers
    ├── ResultContractRegistration.ApplyTo() flushes pending mappings
    ├── Iterates Active descriptors
    ├── Resolves binding contracts and capability descriptors
    └── CapabilityEndpointMapper.MapEndpoint()
            │
            ▼
        RouteHandler (HttpContext → ICapabilityDispatcher.DispatchAsync(CapabilityDescriptor, ...))
            │
            ▼
        CapabilityPipeline (middleware chain → handler invoker)
```

### 4.2 Track 2 — Legacy Compatibility (Implemented, Phase 8d)

```
[CrestService] + [CapabilityCompatibilityProjection]
    │
    ▼
AppServiceCompatibilityGenerator (SG)
    │
    ├── IDescriptorProvider<CapabilityDescriptor>          ──►  ICapabilityRegistry
    ├── IDescriptorProvider<CapabilityEndpointDescriptor>  ──►  ICapabilityEndpointRegistry
    ├── BindInputAsync delegates                           ──►  CapabilityEndpointBindingRegistry
    ├── ICapabilityContextAwareHandlerInvoker per action    ──►  CapabilityHandlerResolverProvider
    ├── Manifest                                           ──►  AppServiceCompatibilityProjectionManifestRegistry
    └── ResultContractRegistration.Register()              ──►  CapabilityEndpointResultContractRegistration
    │
    ▼
MapCrestCapabilityEndpoints() (same as Track 1)
    │
    ▼
CapabilityPipeline (same as Track 1)
```

### 4.3 Track 3 — Tool Exposure (Future)

```
CapabilityDescriptor  ──►  MCP / Agent Projection Descriptor
    │
    ├── Tool Binding Surface (tool name, description, parameter JSON Schema)
    └── │
        ▼
    ICapabilityDispatcher.DispatchAsync(CapabilityDescriptor, InvocationSource.Mcp/Agent, ...)
        │
        ▼
    CapabilityPipeline
```

This track is **not yet implemented**. No source generator, attribute, or runtime bridge exists. The `InvocationSource.Mcp` and `InvocationSource.Agent` enum values are reserved. The architecture above shows the intended shape; actual implementation will require a tool projection descriptor type, an MCP/Agent-specific binding layer, and integration with the tool protocol server.

## 5. Unified Runtime Mainline

All three tracks converge at the same single entry point:

```
ICapabilityDispatcher.DispatchAsync(CapabilityDescriptor descriptor, InvocationSource source, ...)
```

### 5.1 Dispatch Flow

```csharp
// CrestCreates.Metadata
public interface ICapabilityDispatcher
{
    Task<CapabilityExecutionResult> DispatchAsync(
        CapabilityDescriptor descriptor,
        InvocationSource source,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);

    Task<CapabilityExecutionResult> DispatchAsync(
        string capabilityId,
        InvocationSource source,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);
}
```

The `CapabilityDispatcher` (internal, scoped) sets `InvocationSource`, `TenantId`, and `UserId` from ambient context, then delegates to:

```csharp
ICapabilityPipeline.ExecuteAsync(CapabilityDescriptor descriptor, object? input, ...)
```

The string overload resolves the descriptor via `ICapabilityResolver`, then calls the descriptor overload. This ensures the descriptor overload is the authoritative execution path.

### 5.2 InvocationSource

```csharp
// CrestCreates.Capability.Abstractions
public enum InvocationSource
{
    Http,
    Workflow,
    HumanTask,
    Agent,
    Mcp,
    Event,
    BackgroundJob,
    Internal
}
```

HTTP endpoints set `InvocationSource.Http`. Compatibility endpoints do the same. Future MCP and Agent projections will set the corresponding values.

### 5.3 DI Registration

Two extension methods build the runtime:

**`AddCapabilityPipeline()`** registers the pipeline and its middleware:

```csharp
// Outermost → Innermost middleware order:
builder.Use<AuditMiddleware>();
builder.Use<RateLimitMiddleware>();
builder.Use<TenantMiddleware>();
builder.Use<AuthorizationMiddleware>();
builder.Use<ValidationMiddleware>();
builder.Use<IdempotencyMiddleware>();
builder.Use<MetricsMiddleware>();
builder.Use<EventPublishingMiddleware>();

// Singleton registrations:
services.TryAddSingleton<CapabilityPipelineBuilder>(builder);
services.TryAddSingleton<CapabilityHandlerResolver>(from provider);
services.TryAddSingleton<ICapabilityHandlerResolver>(from provider);

// Scoped registrations:
services.TryAddScoped<ICapabilityPipeline, CapabilityPipeline>();
services.TryAddScoped<ICapabilityAuthorizationService, PermissionCapabilityAuthorizationService>();

// Transient middleware:
services.TryAddTransient<AuditMiddleware>();
// ... (all 8 middleware types)
```

**`AddCapabilityRuntime()`** calls `AddCapabilityPipeline()`, then adds:

```csharp
services.TryAddScoped<ICapabilityDispatcher>(/* factory */);
services.TryAddSingleton<ICapabilityResolver, DefaultCapabilityResolver>();
services.TryAddSingleton<ICapabilityVersionResolver, DefaultCapabilityVersionResolver>();
services.TryAddSingleton<ICapabilityAuditStore, NullCapabilityAuditStore>();
services.AddSingleton<IBootstrapValidator, CapabilityHandlerValidator>();
services.AddSingleton<IBootstrapValidator, CapabilitySchemaValidator>();
services.TryAddSingleton<ICapabilityRegistry, CapabilityRegistry>();
services.TryAddSingleton<IRegistryValidationEngine<CapabilityDescriptor>, RegistryValidationEngine<CapabilityDescriptor>>();
services.AddSingleton<IDescriptorBindingStatusContributor, CapabilityBindingStatusContributor>();
services.AddSingleton<IDescriptorRelationshipExtractor, CapabilityRelationshipExtractor>();
```

## 6. Capability Endpoint Descriptor

`CapabilityEndpointDescriptor` is projection metadata — it describes **how** a capability is exposed, not **what** the capability does.

```csharp
// namespace CrestCreates.DynamicApi
// Implements: IDescriptor, IVersionedDescriptor
public sealed class CapabilityEndpointDescriptor : IDescriptor, IVersionedDescriptor
{
    public string Namespace => "dynamic-api-endpoint";                      // runtime constant
    public DescriptorKind Kind => DescriptorKind.DynamicApiEndpoint;        // value = 7

    // ── Identity ──
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }

    // ── Capability reference ──
    public required VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }

    // ── HTTP routing ──
    public CapabilityEndpointHttpMethod HttpMethod { get; init; }
    public string RoutePattern { get; init; } = string.Empty;

    // ── Authorization ──
    public CapabilityEndpointAuthorizationMode AuthorizationMode { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;

    // ── Input binding metadata ──
    public IReadOnlyList<CapabilityEndpointInputBinding> InputBindings { get; init; }
        = Array.Empty<CapabilityEndpointInputBinding>();

    // ── Output mapping ──
    public CapabilityEndpointOutputMapping OutputMapping { get; init; } = new();

    // ── Projection metadata (OpenAPI, documentation) ──
    public CapabilityEndpointProjectionMetadata Projection { get; init; } = new();
}
```

### 6.1 Input Binding

```csharp
public sealed record CapabilityEndpointInputBinding
{
    public string Name { get; init; } = string.Empty;
    public CapabilityEndpointParameterSource Source { get; init; }
    public string? CapabilityInputPath { get; init; }
    public bool Required { get; init; } = true;
}
```

`CapabilityInputPath` maps the HTTP-level parameter name to a path within the capability's input schema (e.g., `"query.filter.name"`). It is the **runtime descriptor field** — the SG-emitted binding delegate uses `TargetProperty` (an SG-only concept) for CLR property assignment.

### 6.2 Output Mapping

```csharp
public sealed record CapabilityEndpointOutputMapping
{
    public int SuccessStatusCode { get; init; } = 200;
    public string? ContentType { get; init; }
}
```

### 6.3 Projection Metadata

```csharp
public sealed record CapabilityEndpointProjectionMetadata
{
    public string? OperationId { get; init; }        // Contract field in canonical hash
    public string? GroupName { get; init; }          // DefinitionOnly
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();  // DefinitionOnly
    public string? Summary { get; init; }            // DefinitionOnly
    public string? Description { get; init; }        // DefinitionOnly
    public bool Deprecated { get; init; }            // DefinitionOnly
    public CapabilityEndpointVisibility Visibility { get; init; } = CapabilityEndpointVisibility.Public;  // DefinitionOnly
}
```

**Key design decision**: Only `OperationId` is a **contract** field in the canonical hash. All other projection metadata fields are `DefinitionOnly` — they carry governance/documentation information but do not affect the runtime contract hash. This reflects the architectural principle that projection metadata is surface-level decoration, not runtime contract.

### 6.4 Canonical Hash Profiles

The canonical hash for `CapabilityEndpointDescriptor` includes:

- **Contract fields**: `Id`, `Name`, `Version`, `State`, `SupersededById`, `Capability` (value profile via `VersionedDescriptorRef`), `HttpMethod`, `RoutePattern`, `AuthorizationMode`, `InputBindings` (element profile, ordinal-by-property ordering), `OutputMapping` (value profile), `Projection.OperationId`
- **Excluded**: `Namespace`, `Kind` (runtime constants)

The `ProjectionKind` field on `CapabilityDescriptor` is `DefinitionOnly` in its canonical hash (Order=100) — governance metadata, not runtime contract.

## 7. HTTP / Dynamic API Track

### 7.1 DX Layering

The native capability HTTP track provides three developer experience levels:

| Level | Mechanism | Use case |
|---|---|---|
| Level 0 | Runtime canonical model (`CapabilityEndpointDescriptor`, `BindingContract`, `Registry`) | Tooling, Control Plane, advanced scenarios |
| Level 1 | Explicit `[CapabilityEndpointSpec]` attributes with full control | Precise routing, custom binding, migration |
| Level 2 | Sugar attributes (`[CapabilityEndpointSet]` + `[Post]`/`[Get]`/`[Put]`/`[Delete]`/`[Patch]`) | Rapid development, convention-over-configuration |

The source generator normalizes Level 2 to Level 1 internally; the emitted output is identical in structure. Level 2 does **not** read class-level `[CapabilityEndpointInput]` — all inputs come from HTTP method attribute parameters only.

### 7.2 Four Concern Separation

| Concern | Artifact | Location |
|---|---|---|
| Descriptor | `CapabilityEndpointDescriptor` — projection metadata (no CLR details) | `CrestCreates.DynamicApi.Abstractions` |
| Binding Contract | `CapabilityEndpointBindingContract` — SG-produced `BindInputAsync` delegate | `CrestCreates.DynamicApi`, SG output |
| Dispatcher | `ICapabilityDispatcher` — unified facade with descriptor overload | `CrestCreates.Metadata` |
| Result Mapper | `CapabilityEndpointResultMapper` — fixed error-code-to-HTTP mapping (internal) | `CrestCreates.DynamicApi` |

### 7.3 Binding Contract

```csharp
// namespace CrestCreates.DynamicApi
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record CapabilityEndpointBindingContract(
    string EndpointId,
    int EndpointVersion,
    Func<HttpContext, CancellationToken, ValueTask<object?>> BindInputAsync);
```

The binding contract is a process-wide static registry:

```csharp
// namespace CrestCreates.DynamicApi
public static class CapabilityEndpointBindingRegistry
{
    // Key: (EndpointId, Version)
    private static readonly ConcurrentDictionary<(string, int), CapabilityEndpointBindingContract> _bindings;

    public static void Register(CapabilityEndpointBindingContract contract);  // throws on duplicate
    public static CapabilityEndpointBindingContract? Find(string endpointId, int version);
    public static CapabilityEndpointBindingContract GetRequired(string endpointId, int version);
    internal static void Reset();  // test isolation only
}
```

SG-emitted `[ModuleInitializer]` code calls `Register()` at startup. `MapCrestCapabilityEndpoints()` calls `GetRequired()` — missing bindings cause `InvalidOperationException`.

### 7.4 MapCrestCapabilityEndpoints()

```csharp
// namespace CrestCreates.DynamicApi
public static IEndpointRouteBuilder MapCrestCapabilityEndpoints(this IEndpointRouteBuilder endpoints)
{
    // 1. Bootstrap registry (build-once via Interlocked)
    var bootstrapper = endpoints.ServiceProvider.GetRequiredService<CapabilityEndpointRegistryBootstrapper>();
    bootstrapper.EnsureBuilt();

    // 2. Flush pending result contract registrations from generated code
    CapabilityEndpointResultContractRegistration.ApplyTo(resultContractRegistry);

    // 3. Iterate Active descriptors, resolve bindings and capabilities, map endpoints
    foreach (var descriptor in registry.GetAll().Where(x => x.State == DescriptorState.Active))
    {
        var binding = CapabilityEndpointBindingRegistry.GetRequired(descriptor.Id, descriptor.Version);
        var capability = CapabilityEndpointCapabilityResolver.Resolve(capabilityRegistry, descriptor.Capability);
        CapabilityEndpointMapper.MapEndpoint(endpoints, descriptor, capability, binding, resultContractRegistry);
    }
}
```

### 7.5 Endpoint Mapping

`CapabilityEndpointMapper.MapEndpoint()` (internal) creates an ASP.NET Core `RouteHandlerBuilder`:

1. Registers a `MapMethods` delegate that:
   - Calls `binding.BindInputAsync(context, ct)` to materialize input
   - Resolves `ICapabilityDispatcher` from request services
   - Calls `dispatcher.DispatchAsync(capability, InvocationSource.Http, input, configureContext, ct)`
   - Maps the `CapabilityExecutionResult` to `IResult` via `MapResult()`
2. Applies endpoint metadata: `WithDisplayName`, `WithTags`, `WithName` (OperationId)
3. Applies authorization: `RequireAuthorization()` or `AllowAnonymous()` based on `AuthorizationMode`

### 7.6 Authorization Modes

| Mode | Behavior |
|---|---|
| `InheritCapability` | No endpoint-level authorization metadata. Pipeline `AuthorizationMiddleware` enforces based on `CapabilityDescriptor.Permissions`. |
| `RequireAuthenticated` | `routeHandler.RequireAuthorization()` + pipeline enforcement. |
| `AllowAnonymous` | `routeHandler.AllowAnonymous()` + pipeline enforcement. Error if capability has non-empty permissions or is marked high-risk. |

## 8. Legacy Dynamic API Boundary

The legacy `DynamicApiAotSourceGenerator` and associated runtime (scanner, executor, `MapCrestDynamicApi()`) remain but are explicitly labeled as compatibility-only. The boundary between legacy and capability-first paths is enforced through multiple mechanisms:

### 8.1 Symbol-Level Exclusion

`DynamicApiAotSourceGenerator.IsDynamicApiImplementation()` checks for class-level `[CapabilityCompatibilityProjection]`:

```csharp
if (compatibilityProjectionAttribute is not null && HasAttribute(typeSymbol, compatibilityProjectionAttribute))
{
    return false;  // entire type excluded from legacy generation
}
```

`HasCompatibilityProjectionOnMethod()` checks both interface and implementation methods:

```csharp
// If method is on the class, check corresponding interface methods
// If method is on the interface, check the implementation method via FindImplementationForInterfaceMember
```

This ensures that when a `[CrestService]` with `[CapabilityCompatibilityProjection]` is processed by the legacy generator, the entire service is skipped. No dual-serving.

### 8.2 Boundary Tests

Six boundary tests guard against cross-path contamination:

1. **Assembly reference boundary** — capability endpoints do not reference legacy DynamicApi types
2. **Project reference boundary** — project dependency graph enforces layer isolation
3. **Legacy source symbol boundary** — `DynamicApiEndpointDescriptor`, `DynamicApiServiceDescriptor`, `DynamicApiActionDescriptor`, `IDynamicApiGeneratedProvider` are not referenced from capability code
4. **CapabilityEndpoint mapping boundary** — `CapabilityEndpointExtensions`, `CapabilityEndpointDescriptorValidator`, `CapabilityEndpointCapabilityResolver`, `CapabilityEndpointMapper` are isolated in capability-only code
5. **CapabilityEndpoint emitter boundary** — binding emitters produce capability types only
6. **Abstractions type definition boundary** — `CapabilityEndpointDescriptor` lives only in `CrestCreates.DynamicApi.Abstractions`

### 8.3 Recycled Artifacts

- `DynamicApiSourceGenerator` (the older, non-incremental generator) has been moved to `99_RecycleBin/`
- Legacy test files (6 total) are renamed with `Legacy` prefix but continue to run
- `AddCrestDynamicApi()`/`MapCrestDynamicApi()` are documented as legacy in XML docs but not marked `[Obsolete]`

## 9. AppService Compatibility Projection

The compatibility projection provides a one-way migration bridge: existing `[CrestService]` AppServices can opt into the capability pipeline while preserving their external HTTP contract. This is Phase 8d.

### 9.1 Opt-In Attributes

```csharp
// namespace CrestCreates.Domain.Shared.Attributes
[CapabilityCompatibilityProjection]          // class-level: project all methods
[CapabilityCompatibilityProjection]          // method-level: project specific method
[CapabilityCompatibilityIgnore]              // method-level: exclude from class-level projection
```

`[DynamicApiIgnore]` (existing attribute) also excludes methods from both legacy and compatibility generation. CEP031 fires when `[CapabilityCompatibilityProjection]` and `[DynamicApiIgnore]` conflict on the same method.

### 9.2 Capability ID Namespace

Compatibility-generated capabilities use the prefix `compat.appservice.{kebab-case-stripped-service-name}` to isolate from native capabilities. The prefix is derived from the service name (e.g., `IBookAppService` → `book`). An explicit `CapabilityIdPrefix` on the attribute can override.

### 9.3 Source Generator Output (6 Files per Service)

`AppServiceCompatibilityGenerator` emits six source files per projected service:

| File | Content |
|---|---|
| `GeneratedAppServiceCompatibilityCapabilities_{Name}.g.cs` | `IDescriptorProvider<CapabilityDescriptor>` with one descriptor per action, `ProjectionKind = AppServiceCompatibility` |
| `GeneratedAppServiceCompatibilityEndpoints_{Name}.g.cs` | `IDescriptorProvider<CapabilityEndpointDescriptor>` with endpoint descriptors |
| `GeneratedAppServiceCompatibilityBindings_{Name}.g.cs` | `BindInputAsync` delegates, `[ModuleInitializer]`-registered into `CapabilityEndpointBindingRegistry` |
| `GeneratedAppServiceCompatibilityInvokers_{Name}.g.cs` | `ICapabilityContextAwareHandlerInvoker` per action, resolving service from DI via `context.ServiceProvider.GetRequiredService<T>()` |
| `GeneratedAppServiceCompatibilityManifest_{Name}.g.cs` | `IAppServiceCompatibilityProjectionManifestProvider` listing all projections |
| `GeneratedAppServiceCompatibilityResultContracts_{Name}.g.cs` | `[ModuleInitializer]`-registered `CapabilityEndpointResultContractRegistration.Register()` calls per endpoint |

### 9.4 HTTP Contract Preservation

Compatibility endpoints must produce the same HTTP response envelope as legacy Dynamic API. The envelope is the `DynamicApiResponse` / `DynamicApiResponse<T>` wrapper:

```json
{
    "code": 200,
    "message": "操作成功",
    "data": { ... }
}
```

This is achieved through the **Result Contract Registry**:

```csharp
// namespace CrestCreates.DynamicApi
public interface ICapabilityEndpointResultContractRegistry
{
    void Register(string endpointId, int version, Func<EndpointExecutionContext, object> mapResult);
    Func<EndpointExecutionContext, object>? TryGetResultMapper(string endpointId, int version);
}
```

The registry uses a deferred registration pattern matching `CapabilityEndpointBindingRegistry`:

```csharp
public static class CapabilityEndpointResultContractRegistration
{
    public static void Register(string endpointId, int version, Func<EndpointExecutionContext, object> mapResult);
    internal static void ApplyTo(ICapabilityEndpointResultContractRegistry registry);
    internal static void Reset();
}
```

Generated `[ModuleInitializer]` code calls `Register()`; `MapCrestCapabilityEndpoints()` calls `ApplyTo()` before iterating descriptors.

### 9.5 Safety: Failures Never Use Custom Result Contracts

`CapabilityEndpointMapper.MapResult()` enforces the critical safety rule:

```csharp
private static IResult MapResult(CapabilityExecutionResult result, CapabilityEndpointOutputMapping outputMapping, Func<EndpointExecutionContext, object>? resultMapper)
{
    // Pipeline failures MUST use the unified mapper — never swallow as 200 OK.
    if (!result.IsSuccess)
        return CapabilityEndpointResultMapper.Map(result, outputMapping);

    if (resultMapper is not null)
    {
        var ctx = new EndpointExecutionContext { Output = result.Output, Succeeded = true, ... };
        var mapped = resultMapper(ctx);
        return (IResult)mapped;
    }

    return CapabilityEndpointResultMapper.Map(result, outputMapping);
}
```

Custom result contracts only govern **success** response envelopes. Authorization failures (403), rate limiting (429), validation errors (400), and handler-not-found (500) always use the unified mapper.

### 9.6 CompatibilityHttpResultMapper

The neutral response envelope helper decouples compatibility-generated code from the legacy `DynamicApiGeneratedRuntime`:

```csharp
public static class CompatibilityHttpResultMapper
{
    public static IResult WrapResult<T>(T? value);         // 200 + DynamicApiResponse<T>
    public static IResult WrapVoidResult();                 // 200 + DynamicApiResponse
    public static IResult WrapGetResult<T>(T? value);       // 404 + DynamicApiResponse if null, else WrapResult
}
```

Wrapper selection in generated result contracts: `WrapVoidResult()` for void-return methods, `WrapGetResult(ctx.Output)` for GET non-void, `WrapResult(ctx.Output)` for other non-void.

### 9.7 CompatibilityBodyReader

Legacy Dynamic API had specific body-reading semantics that differ from ASP.NET Core's default behavior. `CompatibilityBodyReader` reproduces these:

```csharp
public static class CompatibilityBodyReader
{
    public static async Task<T?> ReadBodyAsync<T>(HttpContext context, bool optional) where T : new()
    {
        // ContentLength == 0 → optional ? default : new T()
        // Empty/whitespace body → optional ? default : new T()
        // Invalid JSON + optional → default (no exception)
        // Invalid JSON + required → throws JsonException
    }
}
```

The `where T : new()` constraint is enforced by CEP037 at generation time. Types that do not satisfy this constraint (abstract, interface, array, open generic) trigger an Error diagnostic.

### 9.8 Fail-Closed Generation

Error-level diagnostics cause service-level code generation to be completely suppressed:

- **CEP030**: `[CapabilityCompatibilityProjection]` on non-`[CrestService]` class — Error
- **CEP031**: Projection + Ignore attribute conflict — Error
- **CEP034**: Method overload collision (same action name) — Error
- **CEP037**: Body type does not satisfy `new()` constraint — Error

Warning-level diagnostics emit warnings but allow generation:

- **CEP035**: Default route prefix `api/` used (may mismatch runtime configuration) — Warning
- **CEP036**: Method-level `CapabilityIdPrefix`/`RoutePrefix` set (only class-level takes effect) — Warning

If any Error diagnostic is present for a service, `GenerateAll()` skips all six output files for that service. This is tested and frozen by the `ServiceLevelFailClosed_ErrorDiagnosticSkipsEntireService` test.

### 9.9 Symbol Unification

C# does not propagate interface method attributes to implementing class methods. The compatibility generator uses `FindImplementationForInterfaceMember` for exact symbol matching when checking `[CapabilityCompatibilityProjection]`, `[CapabilityCompatibilityIgnore]`, and `[DynamicApiIgnore]` on both contract interface methods and implementation methods. The `EnumerateContractTypes` helper yields the class type first, then all public inherited interfaces.

### 9.10 Shared Convention Analyzer

`DynamicApiConventionAnalyzer` (internal static in `CrestCreates.CodeGenerator.DynamicApiGenerator`) provides the shared convention derivation layer between the legacy `DynamicApiAotSourceGenerator` and `AppServiceCompatibilityGenerator`. It exposes:

```
ResolveServiceRoute, ResolveActionRoute, ResolvePermission, TrimServiceName,
TrimAsyncSuffix, ToKebabCase, ResolveParameterSource, IsScalar, ResolveHttpMethod,
EnumerateContractTypes, CreateMethodKey, BuildQueryProperties, IsNullableType
```

## 10. MCP Tool Projection

**Not yet implemented.** The intended architecture (Track 3 in Section 4) projects `CapabilityDescriptor` instances into MCP tool surfaces:

1. A tool projection descriptor maps a capability to an MCP tool definition (name, description, JSON Schema for parameters derived from the capability's input schema).
2. An MCP server bridge receives tool invocation requests and translates them into `ICapabilityDispatcher.DispatchAsync(descriptor, InvocationSource.Mcp, input, ...)`.
3. The result is mapped back to the MCP tool response format.

No source generator, attribute (`[McpTool]` or similar), or runtime bridge currently exists. The `InvocationSource.Mcp` value is reserved.

## 11. Agent Tool Projection

**Not yet implemented.** The intended architecture follows the same pattern as MCP but for Agent invocation:

1. An agent tool projection descriptor maps a capability to an agent tool specification.
2. The agent runtime bridge calls `ICapabilityDispatcher.DispatchAsync(descriptor, InvocationSource.Agent, input, ...)`.
3. The result feeds back into the agent's decision loop.

The `InvocationSource.Agent` value is reserved. No implementation exists.

## 12. Projection Registry and Generated Bindings

The projection system relies on a layered registry architecture:

### 12.1 DescriptorProviderRegistry

```csharp
// namespace CrestCreates.Metadata
public static class DescriptorProviderRegistry
{
    private static readonly ConcurrentBag<object> _providers;

    public static void Register<T>(IDescriptorProvider<T> provider) where T : class, IDescriptor;
    public static IReadOnlyList<IDescriptorProvider<T>> GetProviders<T>() where T : class, IDescriptor;
}
```

Both Phase 8a (native endpoints) and Phase 8d (compatibility) generate `IDescriptorProvider<CapabilityEndpointDescriptor>` and `IDescriptorProvider<CapabilityDescriptor>` implementations. These are auto-registered via `[ModuleInitializer]` at startup.

### 12.2 CapabilityEndpointRegistryBootstrapper

```csharp
internal sealed class CapabilityEndpointRegistryBootstrapper
{
    private readonly ICapabilityEndpointRegistry _registry;
    private int _built;

    public void EnsureBuilt()
    {
        if (Interlocked.Exchange(ref _built, 1) != 0) return;
        var providers = DescriptorProviderRegistry.GetProviders<CapabilityEndpointDescriptor>();
        _registry.Build(providers);
    }
}
```

`EnsureBuilt()` is called once by `MapCrestCapabilityEndpoints()`. The `Interlocked` guard ensures build-once semantics even if called multiple times.

### 12.3 CapabilityEndpointCapabilityResolver

```csharp
internal static class CapabilityEndpointCapabilityResolver
{
    internal static CapabilityDescriptor Resolve(ICapabilityRegistry registry, VersionedDescriptorRef<CapabilityDescriptor> capabilityRef)
    {
        // 1. Exact version (Version > 0) — fail-closed, throws on miss
        // 2. Latest active by Id
        // 3. Fallback: scan all active, take max version
        // 4. Throw InvalidOperationException
    }
}
```

Exact version resolution (`Version > 0`) is fail-closed: throws `InvalidOperationException` on miss with no fallback to latest active.

### 12.4 CapabilityEndpointRegistry

```csharp
services.TryAddSingleton<ICapabilityEndpointRegistry, CapabilityEndpointRegistry>();
services.TryAddSingleton<IRegistryValidationEngine<CapabilityEndpointDescriptor>, RegistryValidationEngine<CapabilityEndpointDescriptor>>();
```

### 12.5 Handler Resolver

```csharp
// Handlers are registered additively:
CapabilityHandlerResolverProvider.Register("capability-id", handlerInvoker);

// Both concrete and interface types resolve the same instance:
var concrete = CapabilityHandlerResolverProvider.GetConcreteResolver();
var interface_ = CapabilityHandlerResolverProvider.GetResolver();
```

The pipeline resolves handlers at dispatch time:

```csharp
var invoker = _handlerResolver.Resolve(descriptor.Id);
```

## 13. Dispatcher and Pipeline Integration

### 13.1 CapabilityDispatcher

```csharp
internal sealed class CapabilityDispatcher : ICapabilityDispatcher
{
    private readonly ICapabilityResolver _resolver;
    private readonly ICapabilityPipeline _pipeline;
    private readonly ITenantContext? _tenantContext;
    private readonly ICurrentUser? _currentUser;

    public async Task<CapabilityExecutionResult> DispatchAsync(
        CapabilityDescriptor descriptor, InvocationSource source, object? input, ...)
    {
        return await _pipeline.ExecuteAsync(descriptor, input, ctx =>
        {
            ctx.InvocationSource = source;
            ctx.TenantId = _tenantContext?.CurrentTenantId;
            ctx.UserId = _currentUser?.Id;
            configureContext?.Invoke(ctx);
        }, ct);
    }
}
```

The dispatcher is scoped — one instance per HTTP request. This fixes the pre-existing captive dependency problem where scoped services (`ITenantContext`, `ICurrentUser`) could not be resolved in a singleton chain.

### 13.2 CapabilityPipeline

```csharp
public sealed class CapabilityPipeline : ICapabilityPipeline
{
    public async Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityDescriptor descriptor, object? input, ...)
    {
        // 1. Build context with CapabilityId, Name, Version, ContractHash
        // 2. Set RequiredPermissions from descriptor (after configureContext, bypass-proof)
        // 3. Build middleware chain (innermost = handler, outermost = middleware[0])
        // 4. Execute chain
    }

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        string capabilityIdOrName, object? input, ...)
    {
        // Resolves from registry (by Id → active version → by Name)
        // Delegates to descriptor overload
    }
}
```

Middleware chain construction (outermost → innermost):

```
Audit → RateLimit → Tenant → Authorization → Validation → Idempotency → Metrics → EventPublishing → Handler
```

The chain is built at pipeline instantiation; middleware types are resolved from DI at execution time. The builder stores middleware types, not instances:

```csharp
public sealed class CapabilityPipelineBuilder
{
    public CapabilityPipelineBuilder Use<TMiddleware>() where TMiddleware : ICapabilityPipelineMiddleware;
    public IReadOnlyList<Type> MiddlewareTypes { get; }
}
```

### 13.3 Endpoint-to-Pipeline Context Bridging

`CapabilityEndpointMapper.MapEndpoint()` wires the ASP.NET Core `HttpContext` into the pipeline context:

```csharp
ctx =>
{
    ctx.CausationId = context.TraceIdentifier;
    ctx.IdempotencyKey = ResolveIdempotencyKey(context);
    ctx.Items["HttpTraceIdentifier"] = context.TraceIdentifier;
    ctx.Items["CapabilityEndpointId"] = descriptor.Id;
}
```

The idempotency key is resolved from the `Idempotency-Key` header or a new GUID.

## 14. Contract Hash and Versioning

The canonical hash system produces two hashes per descriptor:

- **ContractHash**: Fields that define the runtime contract (identity, routing, authorization, input/output shape). Versioned: `dynamic-api-endpoint-contract-hash-v1`.
- **DefinitionHash**: ContractHash + DefinitionOnly fields (governance metadata). Versioned: `dynamic-api-endpoint-definition-hash-v1`.

### 14.1 CapabilityEndpointDescriptor Hash Fields

**Contract fields** (sorted by order):
- `Id` (0), `Name` (1), `Version` (2), `State` (3), `SupersededById` (4)
- `Capability` (10, value profile via `VersionedDescriptorRefCapabilityCanonicalHashProfile`)
- `HttpMethod` (20), `RoutePattern` (21), `AuthorizationMode` (22)
- `InputBindings` (30, element profile, ordinal-by-property ordered by `Source,Name,CapabilityInputPath`)
- `OutputMapping` (40, value profile)
- `Projection` (50, value profile — only `OperationId` is Contract within projection)

**DefinitionOnly fields** (within `Projection`):
- `GroupName` (10), `Tags` (20), `Summary` (30), `Description` (40), `Deprecated` (50), `Visibility` (60)

**Excluded fields**:
- `Namespace`, `Kind` — runtime constants, not part of hash

### 14.2 CapabilityDescriptor ProjectionKind

The `ProjectionKind` property on `CapabilityDescriptor` is `DefinitionOnly` (Order=100) in its canonical hash. This means:

- `Native` (0) vs `AppServiceCompatibility` (1) does not affect the runtime contract hash.
- It is governance metadata: origin tracking, migration status, audit trail.
- `DefinitionShapeVersion` was bumped to `v2` when `ProjectionKind` was added.

### 14.3 Versioning Rules

- Endpoint `Id` + `Version` is the unique key for binding contracts and result contracts.
- `CapabilityDescriptor.Version` is the capability's own version; `CapabilityEndpointDescriptor.Version` is the endpoint's version — they can differ.
- `SupersededById` marks an endpoint as superseded without affecting active routing.
- `DescriptorState` transitions (`Active`, `Deprecated`, `Removed`) are honored by `MapCrestCapabilityEndpoints()` — only `Active` endpoints are mapped.

## 15. Fail-Closed Diagnostics

### 15.1 Capability Endpoint Spec Diagnostics (Level 1)

| Code | Severity | Description |
|---|---|---|
| CEP001 | Error | Structural: missing required attribute parameter |
| CEP002 | Error | Structural: invalid CapabilityId |
| CEP003 | Error | Structural: implicit constructor (must be explicit for SG parameter extraction) |
| CEP004 | Error | Structural: duplicate endpoint spec |
| CEP005 | Error | Structural: invalid HTTP method |
| CEP008 | Error | Route binding: DTO writable property mismatch |
| CEP009 | Error | Level 2 misuse: Level 2 attribute on non-sugar context |
| CEP010 | Error | Level 2 misuse: conflicting HTTP method attributes |
| CEP011 | Error | Level 2 misuse: multiple body specifications |
| CEP012 | Error | Non-enum, non-scalar type in route binding |
| CEP013 | Error | Multi-scalar without body (upgraded from Warning in 8c) |
| CEP014 | Error | Non-C#-identifier `Name` without `CapabilityInputPath` |
| CEP016 | Error | Level 2 without `[CapabilityEndpointSet]` container |
| CEP017 | Error | Whitespace in explicit `EndpointId` |
| CEP018 | Error | Missing `TargetProperty` on body parameter (Level 1) |
| CEP019 | Error | Invalid `TargetProperty` identifier (Level 1) |
| CEP020 | Error | Negative `EndpointVersion` |
| CEP021 | Error | Level 2 `Input` without route token to bind to |

### 15.2 Compatibility Projection Diagnostics

| Code | Severity | Description |
|---|---|---|
| CEP030 | Error | `[CapabilityCompatibilityProjection]` on non-`[CrestService]` class |
| CEP031 | Error | `[CapabilityCompatibilityProjection]` and `[DynamicApiIgnore]` conflict |
| CEP034 | Error | Method overload collision (same action name) |
| CEP035 | Warning | Default route prefix `api/` may mismatch runtime configuration |
| CEP036 | Warning | Method-level `CapabilityIdPrefix`/`RoutePrefix` ignored (class-level only) |
| CEP037 | Error | Body type does not satisfy `new()` constraint (compatibility path only) |

### 15.3 Runtime Fail-Closed

- Missing binding in `CapabilityEndpointBindingRegistry.GetRequired()` → `InvalidOperationException`
- Missing capability in `CapabilityEndpointCapabilityResolver.Resolve()` → `InvalidOperationException`
- Duplicate binding registration → `InvalidOperationException`
- `ValidationMiddleware` → `CapabilityExecutionResult.Failure("VALIDATION_ERROR", ...)`
- `AuthorizationMiddleware` → `CapabilityExecutionResult.Failure("UNAUTHORIZED", ...)`
- `RateLimitMiddleware` → `CapabilityExecutionResult.Failure("RATE_LIMITED", ...)`
- Handler not found → `CapabilityExecutionResult.Failure("HANDLER_NOT_FOUND", ...)`
- Unhandled exception → `CapabilityExecutionResult.Failure("PIPELINE_ERROR", ...)`

## 16. Trimming and NativeAOT

### 16.1 Deployment Target

```text
Phase 8 deployment guarantee:
- JIT runtime
- Trimming-safe by construction (no runtime reflection in new mainline input binding)
- PublishTrimmed E2E validation pending (blocked by CodeGenerator netstandard2.0 target)
- NativeAOT-ready architecture where practical
- NativeAOT publish is future target, not current acceptance gate
```

Full NativeAOT publish-and-run is a **future target**, not a current acceptance gate. EF Core NativeAOT is still experimental, and response serialization has not been migrated. The architecture is designed to not block future NativeAOT adoption.

### 16.2 Current State

- All descriptor providers and binding delegates are produced by source generators at compile time.
- `CapabilityEndpointBindingRegistry` and `CapabilityEndpointResultContractRegistration` use static `ConcurrentDictionary`/`List` + `[ModuleInitializer]` — zero dynamic assembly loading.
- `CapabilityEndpointJsonContractRegistry` stores body types at startup via `[ModuleInitializer]` `RegisterBodyType(typeof(T))` calls.
- `CapabilityEndpointJsonTypeInfoResolver` resolves `JsonTypeInfo<T>` from the application's `IOptions<JsonOptions>` at runtime — fail-closed, no fallback to reflection-based options.
- `CapabilityEndpointBodyReader` provides two entry points:
  - `ReadNativeBodyAsync<T>` — for native capability endpoints (8a). Empty body → 400 BAD_REQUEST. No `new()` constraint.
  - `ReadCompatibilityBodyAsync<T>` — for compatibility projection endpoints (8d). Preserves legacy `CompatibilityBodyReader` empty/whitespace/null/optional semantics. One intentional difference: required invalid JSON throws `BadHttpRequestException` (HTTP 400) instead of raw `JsonException`, which is more appropriate for HTTP projection endpoints.
- `CapabilityEndpointJsonContractValidator` validates at startup that all registered body types have `JsonTypeInfo` available (fail-closed).
- `CapabilityHandlerResolverProvider` uses static `ConcurrentDictionary` with additive `Register()` API.
- `DescriptorProviderRegistry` uses static `ConcurrentBag<object>`.

### 16.3 Trimming-Safe Input Binding Scope

**Trimming-safe input binding is complete for 8a and 8d only.**

| Generator | Input Binding | Status |
|---|---|---|
| CapabilityEndpoint (8a) | `ReadNativeBodyAsync<T>` + `JsonTypeInfo<T>` from application options | ✅ Trimming-safe |
| AppServiceCompatibility (8d) | `ReadCompatibilityBodyAsync<T>` + `JsonTypeInfo<T>` from application options | ✅ Trimming-safe |
| CrudService | `DynamicApiGeneratedRuntime.ReadBodyAsync<T>` (reflection-based) | ❌ Unresolved |

The CrudService generator is NOT trimming-safe — its generated DTO types (`CreateBookDto`, `UpdateBookDto`, `BookListRequestDto`) are produced by the same source generator in the same compilation round, making them invisible to the application's `[JsonSerializable]`-decorated `JsonSerializerContext`. Roslyn source generators cannot see each other's `RegisterSourceOutput` output. CRUD endpoints continue using the legacy `DynamicApiGeneratedRuntime.ReadBodyAsync<T>` path (reflection-based). A separate design (BuildTask pre-generation, upstream DTO project, or CrestCreates-owned TypeInfo generation) is required. This is tracked as future work — do not claim CRUD as trimming-safe.

**Response serialization is NOT yet trimming-safe.** The pipeline currently uses `Results.Json(object?)` for response bodies, which relies on runtime reflection. Full trimming safety requires migrating response serialization to `JsonTypeInfo<T>`-based writes. This is tracked as future work.

**Key architectural constraint:** Roslyn Source Generators cannot see each other's `RegisterSourceOutput` output in the same compilation round. Therefore, CrestCreates generators must NOT emit `[JsonSerializable]` partial classes expecting the STJ source generator to process them. The application owns the `JsonSerializerContext` as a regular source file, and CrestCreates accesses `JsonTypeInfo<T>` from it at runtime.

### 16.4 Trimming Fixture

The `CrestCreates.CapabilityEndpoint.TrimmingFixture` project is split into two parts:

- **Host project** (`CrestCreates.CapabilityEndpoint.TrimmingFixture`): A publishable ASP.NET Core web application with `WarningsAsErrors` for IL2026/IL2070/IL2072/IL2075/IL3050/SYSLIB1034. This is the project intended for `dotnet publish -p:PublishTrimmed=true` validation.
- **Test project** (`CrestCreates.CapabilityEndpoint.TrimmingFixture.Tests`): xUnit tests using `WebApplicationFactory<Program>` to exercise the host.

The fixture validates real STJ Source Generator integration with the CrestCreates CodeGenerator in the same compilation round:

1. Build succeeds with both generators active
2. STJ produces `JsonTypeInfo` for body types declared in the application's `JsonSerializerContext`
3. Real POST request body binding exercises the full chain: `JsonTypeInfoResolver → ReadCompatibilityBodyAsync → JsonSerializer.Deserialize(JsonTypeInfo<T>)`
4. HTTP endpoints return correct responses

**PublishTrimmed E2E validation is currently blocked** by a pre-existing framework issue: the `CrestCreates.CodeGenerator` project targets `netstandard2.0` (required for Roslyn analyzer hosting), and `dotnet publish -p:PublishTrimmed=true` fails with `NETSDK1124` because the CodeGenerator project is in the dependency graph via `Directory.Build.Aot.props` global `ProjectReference`. This affects all projects in the solution, not just the fixture. Resolution requires either multi-targeting the CodeGenerator or restructuring the global analyzer reference.

### 16.5 Known Trimming Debt

- **Response serialization**: Uses `Results.Json(object?)` — not `JsonTypeInfo<T>`-based. Requires migration to trimming-safe response writing.
- **CRUD body binding**: Uses `DynamicApiGeneratedRuntime.ReadBodyAsync<T>` — reflection-based. Requires separate design for generated DTO type visibility.
- **`CompatibilityBodyReader`** and **`CapabilityEndpointJsonRuntime`**: Marked `[Obsolete]` — replaced by `CapabilityEndpointBodyReader`. Still present for backward compatibility.
- **`DynamicApiGeneratedRuntime.ReadBodyAsync`**: Marked `[Obsolete]` — replaced by `CapabilityEndpointBodyReader`. Legacy `DynamicApiAotSourceGenerator` still uses it (out of CEP015 scope).

### 16.6 Source Generator Pipeline

1. `AppServiceCompatibilityGenerator` — `IIncrementalGenerator`, detects `[CapabilityCompatibilityProjection]` attributes
2. `CapabilityEndpointGenerator` — `IIncrementalGenerator`, detects `[CapabilityEndpointSpec]` attributes
3. `DynamicApiAotSourceGenerator` — `IIncrementalGenerator`, legacy AppService exposure; skips types with `[CapabilityCompatibilityProjection]`

All three run against the same original input compilation. They cannot observe source emitted by another generator in the same compilation round. No execution-order dependency is permitted.

## 17. Security and Governance Boundaries

### 17.1 Authorization

- Permissions are defined on `CapabilityDescriptor.Permissions` and copied to `CapabilityExecutionContext.RequiredPermissions` **after** `configureContext` — the caller cannot bypass permission checks.
- `PermissionCapabilityAuthorizationService` delegates to the existing `IPermissionChecker` with `AllGranted` semantics.
- Empty permission list → allowed.
- `ICapabilityAuthorizationService` is scoped, registered in `AddCapabilityPipeline()`.

### 17.2 Tenant Isolation

- `TenantMiddleware` enforces tenant context propagation.
- `CapabilityDispatcher` sets `TenantId` from `ITenantContext?.CurrentTenantId` before pipeline execution.
- `ICapabilityPipeline`/`ICapabilityDispatcher` are scoped (not singleton) so that `ITenantContext` can be resolved per-request.

### 17.3 Agent Control Plane Separation

The Agent Control Plane is a **governance** surface, not a runtime execution surface. It can:
- Read descriptors, preview projections, inspect relationships
- Submit activation proposals for review
- Query binding status

It **cannot**:
- Bypass authorization
- Directly invoke runtime handlers
- Mutate the runtime registry
- Approve its own changes

`InvocationSource.Agent` is reserved for future agent-triggered capability execution through the pipeline — not for governance operations.

### 17.4 Result Contract Security

Custom result contracts from the `ICapabilityEndpointResultContractRegistry` only apply to **success** responses. The `!result.IsSuccess` guard in `MapResult()` ensures that:

- Authorization failures always return 403
- Rate limiting always returns 429
- Validation failures always return 400
- Pipeline errors always return 500

Compatibility projections can never swallow a failure as a 200 OK with a legacy JSON envelope.

## 18. Migration Strategy

The migration from legacy Dynamic API to capability-first HTTP follows a clear sequence:

| Step | Phase | Action |
|---|---|---|
| 1 | 8a | `[CapabilityEndpointSpec]` for new native capabilities. Runs alongside legacy. |
| 2 | 8c | Legacy path labeled as compatibility-only. Boundary tests established. |
| 3 | 8d | `[CapabilityCompatibilityProjection]` on existing `[CrestService]` classes. Legacy generator automatically skips projected services. |
| 4 | Future | Remove unused legacy endpoints. Convert remaining AppServices to native capabilities. |

Key migration properties:
- `compat.appservice.` prefix isolates compatibility capabilities — no collision with native capability IDs.
- `CapabilityProjectionKind.AppServiceCompatibility` marks the origin for governance tracking.
- `ResultContractRegistry` preserves the legacy HTTP response envelope — external clients see no change.
- Service-level fail-closed generation prevents broken compatibility code from being emitted — developers fix diagnostics, then generation resumes.

## 19. Testing Strategy

### 19.1 Test Layers

| Layer | Location | Focus |
|---|---|---|
| Source Generator Tests | `tests/Tooling/CrestCreates.CodeGenerator.Tests/` | SG output correctness, diagnostics, fail-closed behavior. Uses `CSharpGeneratorDriver` with in-memory compilations. |
| Unit Tests | `tests/Framework/Api/CrestCreates.DynamicApi.Tests/` | Registry, resolver, mapper, validator, binding contract registration. |
| Boundary Tests | `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/` | Cross-path isolation, legacy symbol boundary, assembly reference boundary. |
| E2E Tests | `tests/Framework/Api/CrestCreates.CompatibilityProjection.E2E.Tests/` | Source-generator-backed `WebApplicationFactory<T>` with real pipeline execution. |
| Integration Tests | `tests/Runtime/Capability/CrestCreates.Capability.Tests/` | Pipeline execution, middleware chain, authorization, tenant context. |

### 19.2 Key Test Counts (as of Phase 8d completion)

- 248 AppServiceCompatibilityGenerator tests
- 45 DynamicApi (CapabilityEndpoint) tests
- 34 Boundary tests
- 9 Compatibility Projection E2E tests
- 117 Capability pipeline tests

### 19.3 Test Isolation

- `CapabilityEndpointBindingRegistry.Reset()` (internal) clears the static registry between tests.
- `CapabilityEndpointResultContractRegistration.Reset()` (internal) clears pending registrations.
- `CapabilityHandlerResolverProvider` does not have a `Reset()` — tests use unique capability IDs per test to avoid collisions.

## 20. Future Evolution

### 20.1 MCP Tool Projection (Track 3)

Implementation requires:
1. A tool projection descriptor type (`McpToolDescriptor` or similar) that maps a `CapabilityDescriptor` to an MCP tool definition.
2. An MCP server bridge that translates tool invocation requests into `ICapabilityDispatcher.DispatchAsync(descriptor, InvocationSource.Mcp, ...)`.
3. A source generator that produces tool projection descriptors from attributes (e.g., `[McpTool(capabilityId)]`).
4. Integration with the MCP protocol (JSON-RPC, tool listing, tool invocation).

### 20.2 Agent Tool Projection

Analogous to MCP but for Agent-invoked capabilities. Requires agent tool specification format and agent runtime bridge.

### 20.3 Trimming-Safe Response Serialization

Input body binding is now trimming-safe via `CapabilityEndpointBodyReader` + application-owned `JsonSerializerContext`. Response serialization still uses `Results.Json(object?)` (runtime reflection). Full trimming safety requires migrating response serialization to `JsonTypeInfo<T>`-based writes, which requires the pipeline to carry type information through to the response mapper.

**CRUD generator body binding is NOT trimming-safe.** Generated CRUD DTO types (`CreateBookDto`, `UpdateBookDto`, `BookListRequestDto`) are produced by the same source generator in the same compilation round, making them invisible to the application's `[JsonSerializable]`-decorated `JsonSerializerContext`. Roslyn source generators cannot see each other's `RegisterSourceOutput` output. CRUD endpoints continue using the legacy `DynamicApiGeneratedRuntime.ReadBodyAsync<T>` path (reflection-based). A separate design (BuildTask pre-generation, upstream DTO project, or CrestCreates-owned TypeInfo generation) is required. This is tracked as future work — do not claim CRUD as trimming-safe.

### 20.4 Compatibility Projection Sunset

As AppServices are migrated to native capabilities, the compatibility projection generator and its runtime components can be phased out:
- Remove `[CapabilityCompatibilityProjection]` attributes
- Convert remaining AppServices to `[CapabilityEndpointSpec]`-decorated native capabilities
- Remove `AddCrestCompatibilityProjection()` DI extension
- Remove `AppServiceCompatibilityGenerator` from the SG pipeline
- Archive `CompatibilityHttpResultMapper` and `CompatibilityBodyReader`

The `compat.appservice.` prefix serves as a permanent marker of migration origin even after sunset.

### 20.5 OpenAPI Integration

`CapabilityEndpointProjectionMetadata` carries documentation fields (`Summary`, `Description`, `Deprecated`, `Tags`, `GroupName`) that are currently stored in projection metadata for future OpenAPI integration. These are `DefinitionOnly` in canonical hash — they do not affect runtime contract but are available for OpenAPI document generation.

### 20.6 Multi-Source Binding Composition

Currently, HTTP endpoint binding is `HttpContext`-to-`object?`. Future evolutions could support composed binding from multiple sources (HTTP + gRPC + message queue) within a single endpoint registration, with source-specific input normalizers that feed into the same capability.

---

*This document reflects the architecture as of Phase 8d completion (2026-07-14). For detailed developer experience guides, see the corresponding Phase specs in `docs/superpowers/specs/`.*
