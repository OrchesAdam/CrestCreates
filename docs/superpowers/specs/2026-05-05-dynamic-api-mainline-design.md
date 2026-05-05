# Dynamic API Mainline Design

## Goal

Dynamic API must have one official execution path: compile-time generated registry and compile-time generated endpoints.

The runtime path must not scan services, build endpoint descriptors, or execute app service methods through reflection. Missing generated code is a startup/configuration error, not a reason to fall back.

## Source Plan

This design implements `docs/review/feature-plans/dynamic-api-mainline.xml`.

## Decisions

| Decision | Result |
|---|---|
| Main execution path | Generated endpoints only |
| Service discovery | `DynamicApiAotSourceGenerator` only |
| Runtime fallback | Removed |
| `UseRuntimeReflectionFallback()` | Delete public API |
| `EnableRuntimeReflectionFallback` | Delete option |
| Missing generated provider | Fail fast |
| Swagger metadata | Read generated registry |
| New Dynamic API features | Implement in generator, descriptors, or generated runtime |

## Architecture

| Component | Role | Responsibility |
|---|---|---|
| `DynamicApiAotSourceGenerator` | Compile-time discovery and generation | Finds Dynamic API services and emits registry plus endpoint mapping code |
| `IDynamicApiGeneratedProvider` | Generated code bridge | Exposes `CreateRegistry(options)` and `MapEndpoints(endpoints, options)` |
| `DynamicApiGeneratedRegistryStore` | Provider store | Aggregates generated providers and fails when none exist |
| `DynamicApiExtensions.AddCrestDynamicApi` | DI entry | Registers options, generated registry, route convention, Swagger setup |
| `DynamicApiExtensions.MapCrestDynamicApi` | Endpoint mapping entry | Calls generated providers to map endpoints |
| `GeneratedDynamicApiRuntime` | Runtime helper | Handles body/query/route binding support, permission checks, validation, UoW, and result envelopes |
| `DynamicApiDescriptors` | Metadata model | Carries generated service/action/parameter/return/permission descriptors |
| `DynamicApiSwagger` / setup | API documentation | Builds Swagger from generated registry |

The runtime helper is allowed because it does not discover services or dispatch by reflection. It is shared generated-endpoint support code.

## Generated Output

| Output | Purpose |
|---|---|
| `GeneratedDynamicApiRegistry.g.cs` | Provides service/action metadata for Swagger, tests, and diagnostics |
| `GeneratedDynamicApiEndpoints.g.cs` | Registers concrete ASP.NET Core endpoints |
| Generated provider | Registers itself with `DynamicApiGeneratedRegistryStore` through module initialization |

Generated descriptors must include enough metadata to prove and document the mainline:

| Metadata | Examples |
|---|---|
| Service | service name, route prefix, service type, implementation type |
| Action | action name, operation id, relative route, HTTP method |
| Parameters | name, type, source, optional flag |
| Return | declared type, payload type, void flag |
| Security | permission names and policy semantics |

## Data Flow

| Stage | Flow | Result |
|---|---|---|
| Compile time | `DynamicApiAotSourceGenerator` analyzes service contracts and implementations | Generated registry and endpoint mapper are emitted |
| Application startup / DI | `AddCrestDynamicApi(options)` registers `DynamicApiRegistry` from generated providers | Registry exists only when generated provider exists |
| Application startup / routing | `MapCrestDynamicApi()` maps generated endpoints | ASP.NET Core endpoint routing contains generated Dynamic API endpoints |
| Request execution | Generated handler binds parameters and calls `GeneratedDynamicApiRuntime` helpers | App service method executes through generated code |
| Error handling | Generated runtime throws framework/platform exceptions outward | Global exception middleware owns response formatting |

Detailed request path:

| Step | Component | Behavior |
|---|---|---|
| 1 | Generated endpoint | Receives ASP.NET Core request |
| 2 | Generated endpoint | Reads route, query, body, and cancellation token parameters |
| 3 | `GeneratedDynamicApiRuntime` | Ensures permissions |
| 4 | `GeneratedDynamicApiRuntime` | Validates complex DTOs |
| 5 | `GeneratedDynamicApiRuntime` | Opens UoW scope when needed |
| 6 | App service | Executes real service method |
| 7 | `GeneratedDynamicApiRuntime` | Commits or rolls back UoW |
| 8 | `GeneratedDynamicApiRuntime` | Wraps successful result |
| 9 | Global exception middleware | Handles any thrown exception |

## Removed Runtime Fallback

Runtime fallback is removed from the public API.

| Item | Action |
|---|---|
| `DynamicApiOptions.EnableRuntimeReflectionFallback` | Delete |
| `DynamicApiOptions.UseRuntimeReflectionFallback()` | Delete |
| Fallback-positive tests | Delete |
| Missing-provider error message | Remove fallback suggestion |
| Runtime scanner/executor references | Do not extend; delete if actual active code is found |

This is an intentional breaking change. Existing consumers calling `UseRuntimeReflectionFallback()` must remove that call and make sure generated providers are produced.

## Error Handling

| Scenario | Behavior |
|---|---|
| No generated provider during registry resolution | Throw `InvalidOperationException` |
| No generated endpoint provider during mapping | Throw `InvalidOperationException` |
| Old code calls `UseRuntimeReflectionFallback()` | Compile error after API deletion |
| Parameter binding fails | Throw standard ASP.NET Core or framework exception |
| Unauthenticated permission check | Throw `UnauthorizedAccessException`; global middleware maps to 401 |
| Authenticated but forbidden permission check | Throw `CrestPermissionException`; global middleware maps to 403 |
| DTO validation fails | Keep current validation exception behavior for this feature; validation exception unification is separate work |
| UoW failure | Roll back and rethrow original exception |

Missing-provider diagnostics should mention only generated-mainline causes:

| Diagnostic | Content |
|---|---|
| Problem | Dynamic API did not find compile-time generated provider |
| Common causes | generator did not run, analyzer reference missing, app service assembly not referenced, service not marked or discoverable, generated files not compiled |
| Files to inspect | `GeneratedDynamicApiRegistry.g.cs`, `GeneratedDynamicApiEndpoints.g.cs` |
| Do not mention | runtime fallback |

## Testing Strategy

Tests must express that generated Dynamic API is the only maintained path.

| Layer | Tests | Purpose |
|---|---|---|
| CodeGenerator.Tests | `DynamicApiAotSourceGeneratorTests` | Verify generated registry and endpoint code |
| Web.Tests | `GeneratedDynamicApiRuntimeTests` | Verify generated runtime helpers |
| Web.Tests | `DynamicApiExtensionsTests` | Verify fail-fast behavior without generated provider |
| IntegrationTests | Dynamic API HTTP tests | Verify real host uses generated endpoints |
| Swagger tests | Generated registry based assertions | Verify documentation comes from generated metadata |

Required test changes:

| Test | Action |
|---|---|
| `AddCrestDynamicApi_WithRuntimeFallbackOptIn_CanResolveEmptyRegistryForLegacyDiagnostics` | Delete |
| Missing-provider registry test | Keep or strengthen |
| Missing-provider endpoint mapping test | Keep or strengthen |
| Generator registry/endpoint output test | Keep and extend when metadata gaps are found |
| Integration CRUD test | Keep as generated-mainline acceptance |
| Runtime scanner/executor tests | Do not add |

Recommended acceptance tests:

| Test | Verifies |
|---|---|
| `GeneratedDynamicApi_ShouldRegisterExpectedEndpoints` | Real host has expected generated endpoint routes |
| `GeneratedDynamicApi_ShouldBindRouteQueryAndBodyParameters` | Generated handlers bind supported parameter sources |
| `GeneratedDynamicApi_ShouldUseUnitOfWorkForWriteEndpoints` | Write endpoints enter UoW and commit/rollback correctly |
| `GeneratedDynamicApi_ShouldPropagateExceptionsToGlobalMiddleware` | Generated endpoints do not swallow platform exceptions |
| `DynamicApiOptions_ShouldNotExposeRuntimeFallback` | Fallback API is gone |

## Acceptance Criteria

| Criterion | Passing condition |
|---|---|
| Generated endpoints are the only official path | `MapCrestDynamicApi()` maps only generated providers |
| Registry is generated | `DynamicApiRegistry` resolves only from generated providers |
| Runtime fallback is removed | `DynamicApiOptions` has no fallback option or method |
| Missing generated provider fails fast | Registry resolution and endpoint mapping throw clear errors |
| Swagger uses generated metadata | Swagger setup reads `DynamicApiRegistry` |
| Tests align with mainline | No new positive runtime fallback tests exist |
| Sample validates the path | Sample Dynamic API endpoints are produced by generated code |

## Out Of Scope

| Item | Reason |
|---|---|
| Validation exception unification | Separate platform exception work |
| New Dynamic API features | This feature is mainline cleanup |
| Reintroducing scanner/executor | Conflicts with generated mainline |
| Long compatibility period for fallback | User explicitly selected direct deletion |
| Controller source generator retirement | Related but separate if it is not active endpoint mainline |

## Review Checklist

| Check | Expected answer |
|---|---|
| Does any new code scan app services at runtime? | No |
| Does any new test prove fallback works? | No |
| Does the error message recommend fallback? | No |
| Are permissions, UoW, validation, and errors handled in generated runtime? | Yes |
| Can missing generated code be diagnosed quickly? | Yes |
