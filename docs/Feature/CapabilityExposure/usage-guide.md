# Capability Projection and Exposure — Usage Guide

This guide covers how to expose Capabilities over HTTP and how to project legacy AppServices onto the Capability Pipeline. It is intended for developers who need to create new endpoints, migrate existing Dynamic API services, or extend the exposure model.

---

## 1. Choosing an Exposure Model

| Scenario | Recommended Track | Description |
|---|---|---|
| New capability, no existing AppService | **Track 1** — Native Capability HTTP | Define `[CapabilityEndpointSpec]` classes; SG generates binding + provider. |
| Existing `[CrestService]` AppService, want to run on Capability Pipeline | **Track 2** — Compatibility Projection | Add `[CapabilityCompatibilityProjection]` to the class; SG generates descriptors, bindings, invokers, and result contracts. |
| Want to expose as MCP/Agent tool | **Track 3** — Future | Not yet implemented. Reserved. |

Tracks 1 and 2 share the same endpoint infrastructure: `MapCrestCapabilityEndpoints()`, `ICapabilityEndpointRegistry`, `CapabilityEndpointMapper`, and the capability pipeline.

---

## 2. Registering the Projection Runtime

Add the following to your `Program.cs` or module `OnConfigureServices`:

```csharp
// Minimal: pipeline + dispatcher + registries
builder.Services.AddCapabilityRuntime();

// With compatibility projection support (includes endpoint infrastructure)
builder.Services.AddCapabilityRuntime();
builder.Services.AddCrestCompatibilityProjection();

// Map endpoints
app.MapCrestCapabilityEndpoints();
```

**What each call registers:**

- `AddCapabilityPipeline()` — `ICapabilityPipeline` (scoped), `ICapabilityAuthorizationService`, 8 middleware types (Audit, RateLimit, Tenant, Authorization, Validation, Idempotency, Metrics, EventPublishing), `CapabilityHandlerResolver`.
- `AddCapabilityRuntime()` — builds on `AddCapabilityPipeline()`, also registers `ICapabilityDispatcher` (scoped), `ICapabilityResolver`, `ICapabilityRegistry`, bootstrap validators (`CapabilityHandlerValidator`, `CapabilitySchemaValidator`), and binding status contributor.
- `AddCrestCompatibilityProjection()` — calls `AddCrestCapabilityEndpoints()` internally, registering `ICapabilityEndpointRegistry`, `CapabilityEndpointRegistryBootstrapper`, `ICapabilityEndpointResultContractRegistry`, and the validator/extractor for `CapabilityEndpointDescriptor`.

`MapCrestCapabilityEndpoints()` bootstraps the registry, resolves bindings, and maps every active descriptor to a Minimal API route handler.

---

## 3. Exposing a Native Capability as HTTP

### Level 1 — Explicit Specification

Define a sealed nested class inside a `[CapabilityEndpointSpecs]` container:

```csharp
[CapabilityEndpointSpecs]
public static partial class BookApiEndpoints
{
    [CapabilityEndpointSpec("book.query", CapabilityEndpointHttpMethod.Get, "api/books/{id}")]
    [CapabilityEndpointInput(typeof(Guid), "id", Source = CapabilityEndpointParameterSource.Route)]
    [CapabilityEndpointOutput]
    public sealed class BookQueryEndpointSpec { }
}
```

- `[CapabilityEndpointSpec]` — declares capability ID, HTTP method, and route pattern.
- `[CapabilityEndpointInput]` — describes how an input parameter maps to the HTTP request (body, route, query, or header).
- `[CapabilityEndpointOutput]` — reserved for future output mapping override; currently marked `[Obsolete]` with no generator consumption.

### Level 2 — Sugar Syntax

Use a `[CapabilityEndpointSet]` container with HTTP method attributes:

```csharp
[CapabilityEndpointSet(RoutePrefix = "api/books", GroupName = "Books")]
public static partial class BookEndpoints
{
    [Get("book.query", "{id}")]
    public sealed partial class GetBook { }

    [Post("book.create", Input = typeof(CreateBookRequest))]
    [CapabilityEndpointInput(typeof(CreateBookRequest))]
    public sealed partial class CreateBook { }
}
```

- `[Get]`, `[Post]`, `[Put]`, `[Delete]` — convenience attributes that project to `CapabilityEndpointHttpMethod` values.
- Route prefix is inherited from `[CapabilityEndpointSet].RoutePrefix`.
- `Input` property on the attribute can declare an explicit body DTO type.
- `[CapabilityEndpointInput]` attributes remain available for additional scalar bindings (route, query, header).

### What the Source Generator Produces

After compilation, SG generates in `obj/{config}/{tfm}/source-generators/`:

1. **Descriptor Provider** — implements `ICapabilityEndpointDescriptorProvider`, returns `CapabilityEndpointDescriptor` objects.
2. **Binding Contract** — implements `CapabilityEndpointBindingContract`, generated bindings registered via `[ModuleInitializer]` into `CapabilityEndpointBindingRegistry`.
3. **Invoker & Result Contract** (for compatibility projections) — wraps the AppService method call and registers a result mapper.

`MapCrestCapabilityEndpoints()` discovers all `ICapabilityEndpointDescriptorProvider` implementations, builds the registry, and maps each endpoint.

---

## 4. Defining CapabilityEndpointDescriptor

Each endpoint is described at runtime by a `CapabilityEndpointDescriptor`:

| Property | Description |
|---|---|
| `Namespace` | Always `"dynamic-api-endpoint"` |
| `Kind` | `DescriptorKind.DynamicApiEndpoint` (numeric value 7) |
| `Id` | Unique endpoint identifier |
| `Name` | Human-readable name |
| `Version` | Integer version number |
| `State` | `Active`, `Deprecated`, `Superseded`, etc. |
| `Capability` | `VersionedDescriptorRef<CapabilityDescriptor>` — the capability this endpoint projects |
| `HttpMethod` | `Get`, `Post`, `Put`, `Delete`, `Patch`, `Head`, `Options` |
| `RoutePattern` | ASP.NET Core route template (e.g., `"api/books/{id}"`) |
| `AuthorizationMode` | `InheritCapability`, `RequireAuthenticated`, or `AllowAnonymous` |
| `InputBindings` | `IReadOnlyList<CapabilityEndpointInputBinding>` — how input values map to HTTP |
| `OutputMapping` | `CapabilityEndpointOutputMapping` — response metadata (status code, content type) |
| `Projection` | `CapabilityEndpointProjectionMetadata` — OpenAPI metadata (OperationId, GroupName, Tags, Summary, Description, Deprecated, Visibility) |

The descriptor is a *projection only*. It never owns capability schemas, permissions, handlers, or execution logic.

---

## 5. Generated HTTP Binding

The binding pipeline at endpoint registration time:

1. **SG generates** a `BindInputAsync(HttpContext, CancellationToken)` delegate for each endpoint.
2. The binding is **registered** via `[ModuleInitializer]` into `CapabilityEndpointBindingRegistry`.
3. `MapCrestCapabilityEndpoints()` **resolves** the binding from the registry for each active descriptor.
4. `CapabilityEndpointMapper.MapEndpoint()` **maps** the route handler: `binding → dispatch → result mapping`.

**Binding modes:**

- **Body only** — a single `[CapabilityEndpointInput]` with `Source = Body`; SG emits `CapabilityEndpointBodyReader.ReadBodyAsync<T>(context, jsonTypeInfo, emptyBodyFactory, optional, ct)` with `JsonTypeInfo<T>` resolved from the application's `JsonSerializerOptions`.
- **Body + route/query/header scalars** — body deserialized, then scalars assigned to body DTO properties via `TargetProperty` or PascalCase convention.
- **Single scalar** (one route/query param, no body) — directly passed as capability input.
- **Multiple scalars without body** — compile-time error (CEP013).

---

## 6. Migrating Legacy Dynamic API

The legacy Dynamic API has two generators. Neither is the primary path:

- `DynamicApiAotSourceGenerator` — still generates Minimal API endpoints for `[CrestService]` classes **without** `[CapabilityCompatibilityProjection]`. This is a bridge until all services migrate.
- `DynamicApiSourceGenerator` — the old Controller-based generator; moved to `99_RecycleBin/` and no longer maintained.

**Coexistence:**

| Path | Registration Method | Generator |
|---|---|---|
| Legacy Dynamic API | `MapCrestDynamicApi()` | `DynamicApiAotSourceGenerator` |
| Capability Endpoints | `MapCrestCapabilityEndpoints()` | `CapabilityEndpointGenerator` / `AppServiceCompatibilityGenerator` |

Both can coexist in the same application. When a service has `[CapabilityCompatibilityProjection]`, the legacy `DynamicApiAotSourceGenerator` skips it (the attribute acts as an opt-out). Other `[CrestService]` classes continue to use the legacy path.

**Test naming convention:** Tests that exercise the legacy code path are prefixed with `Legacy` (e.g., `LegacyDynamicApiAotSourceGeneratorTests`, `LegacyDynamicApiExtensionsTests`).

6 boundary tests in `CapabilityEndpointBoundaryTests` enforce cross-path isolation:
- Abstractions assembly does not reference implementation assembly
- Abstractions does not define legacy runtime types
- Legacy types do not leak into capability endpoint abstractions

---

## 7. Enabling AppService Compatibility Projection

### Class-level Opt-in

Add `[CapabilityCompatibilityProjection]` to the class. All public non-static methods are projected:

```csharp
[CrestService]
[CapabilityCompatibilityProjection]
public class BookAppService
{
    public Task<BookDto> GetAsync(Guid id) { ... }
    public Task<List<BookDto>> GetAllAsync() { ... }
    public Task CreateAsync(CreateBookRequest request) { ... }
    public Task DeleteAsync(Guid id) { ... }
}
```

**What is generated:**

| Artifact | Generated File |
|---|---|
| Capability Descriptors + Endpoint Descriptors | `GeneratedAppServiceCompatibilityManifest_{Service}.g.cs` |
| Input binding contracts | `GeneratedAppServiceCompatibilityBindings_{Service}.g.cs` |
| Handler invokers (DI-resolved service calls) | `GeneratedAppServiceCompatibilityInvokers_{Service}.g.cs` |
| Result contracts (DynamicApiResponse envelope) | `GeneratedAppServiceCompatibilityResultContracts_{Service}.g.cs` |
| Descriptor provider (ICapabilityEndpointDescriptorProvider) | `GeneratedAppServiceCompatibilityEndpoints_{Service}.g.cs` |

**Capability ID convention:** `compat.appservice.{service-name}.{method-name}` where `{service-name}` is the class name with `AppService`/`Service` suffix stripped, converted to kebab-case. For `BookAppService`, the GetAsync method becomes `compat.appservice.book.get`.

**Route convention:** `api/{service-name}/{method-name}` in kebab-case. For `BookAppService.GetAsync`, the route is `api/book/get`.

### Method-level Opt-in

When only specific methods should be projected (the class does not have the attribute):

```csharp
[CrestService]
public class BookAppService
{
    [CapabilityCompatibilityProjection]
    public Task<BookDto> GetAsync(Guid id) { ... }

    // This method stays on legacy Dynamic API
    public Task CreateAsync(CreateBookRequest request) { ... }
}
```

The generator scans for method-level `[CapabilityCompatibilityProjection]` attributes using contract type discovery (checking both the declaring method and its interface contract counterpart).

---

## 8. Ignoring or Restricting Methods

### Excluding a Method

Use `[CapabilityCompatibilityIgnore]` when the class has `[CapabilityCompatibilityProjection]` but a specific method should not be projected:

```csharp
[CrestService]
[CapabilityCompatibilityProjection]
public class BookAppService
{
    public Task<BookDto> GetAsync(Guid id) { ... }

    [CapabilityCompatibilityIgnore]
    public Task InternalSyncAsync() { ... }  // Excluded from projection
}
```

`[CapabilityCompatibilityIgnore]` works on both interface and implementation methods. The generator checks for the attribute on the contract type (interface method) and the implementation.

### Overriding Route Prefix

Customize the capability ID prefix and route prefix on the class-level attribute:

```csharp
[CrestService]
[CapabilityCompatibilityProjection(
    CapabilityIdPrefix = "catalog.book",
    RoutePrefix = "v2/books")]
public class BookAppService { ... }
```

This produces capability IDs like `catalog.book.get` and routes like `v2/books/get`.

**Warning:** Setting `CapabilityIdPrefix` or `RoutePrefix` on a method-level `[CapabilityCompatibilityProjection]` attribute produces diagnostic CEP036 — the values are service-level properties that only take effect on class-level attributes. They will be ignored.

**Warning:** If you have customized `DynamicApiOptions.DefaultRoutePrefix` at runtime and are migrating from legacy Dynamic API, the compatibility projection uses the hardcoded default prefix `api/`. To match your existing routes, set `RoutePrefix` on the class-level attribute. Otherwise, diagnostic CEP035 warns you to ensure contract fidelity.

---

## 9. HTTP Contract Compatibility

The core promise: the external HTTP contract is **unchanged** when migrating from legacy Dynamic API to compatibility projection. Existing clients do not need to change.

### Success Responses

| Method Return Type | HTTP Status | Response Body |
|---|---|---|
| Non-void (any non-GET) | `200 OK` | `DynamicApiResponse<T>` envelope: `Code=200`, `Message="操作成功"`, `Data=value` |
| Void | `200 OK` | `DynamicApiResponse` envelope: `Code=200`, `Message="操作成功"` (no `Data` field) |
| Non-void GET returning `null` | `404 Not Found` | `DynamicApiResponse` envelope: `Code=404`, `Message="资源不存在"` |
| Non-void GET returning non-null | `200 OK` | `DynamicApiResponse<T>` envelope: `Code=200`, `Message="操作成功"`, `Data=value` |

These are implemented by:
- `CompatibilityHttpResultMapper.WrapResult<T>()` — non-void, non-GET-null
- `CompatibilityHttpResultMapper.WrapVoidResult()` — void return
- `CompatibilityHttpResultMapper.WrapGetResult<T>()` — GET with null check
- `CapabilityEndpointBodyReader.ReadBodyAsync<T>()` — AOT-safe body reading with `JsonTypeInfo<T>` from application's `JsonSerializerOptions`. Compatibility path uses non-null `emptyBodyFactory` (empty body → default instance); native path uses null `emptyBodyFactory` (empty body → 400 BAD_REQUEST).

### Pipeline Failure Responses

Custom result contracts **only** govern the success response envelope. Pipeline failures always use the unified `CapabilityEndpointResultMapper`, regardless of whether the endpoint is native or compatibility-projected:

| Failure | HTTP Status |
|---|---|
| `UNAUTHORIZED` | `403 Forbid` |
| `CAPABILITY_VALIDATION_FAILED` | `400` Problem JSON |
| `RATE_LIMIT_EXCEEDED` | `429` |
| `HANDLER_NOT_FOUND` | `500` Problem JSON |

This guarantee is verified by the `AuthorizationFailureE2ETests` — a pipeline that always returns `RATE_LIMIT_EXCEEDED` produces `429`, not `200 OK` with a fallback envelope.

---

## 10. Exposing Capabilities as MCP Tools

Not yet implemented. Reserved for Track 3.

---

## 11. Exposing Capabilities as Agent Tools

Not yet implemented. Reserved for Track 3.

---

## 12. Diagnostics and Troubleshooting

### Compatibility Projection Diagnostics (CEP030–CEP037)

| Code | Severity | Title | Fix |
|---|---|---|---|
| CEP030 | Error | Invalid projection target | Ensure `[CapabilityCompatibilityProjection]` is on a `[CrestService]` class or method declared by one. |
| CEP031 | Error | Attribute conflict | Remove either `[CapabilityCompatibilityProjection]` or `[DynamicApiIgnore]` from the method; projection and suppression cannot coexist. |
| CEP034 | Error | Method overload collision | Compatibility projection does not support method overloads. Rename one overload or exclude it with `[CapabilityCompatibilityIgnore]`. |
| CEP035 | Warning | Default route prefix | The method uses the hardcoded `api/` prefix. If `DynamicApiOptions.DefaultRoutePrefix` is customized at runtime, set `RoutePrefix` on the class-level `[CapabilityCompatibilityProjection]`. |
| CEP036 | Warning | Method-level prefix ignored | `CapabilityIdPrefix` and `RoutePrefix` on a method-level attribute are service-level properties; move them to the class-level attribute. |
| CEP037 | Error | Body type new() constraint | The body parameter type must have a public parameterless constructor. Not allowed: abstract, interface, array, open generic. Add a parameterless constructor or exclude the method with `[CapabilityCompatibilityIgnore]`. |

### Native Endpoint Specification Diagnostics (CEP001–CEP021)

| Code | Severity | Title | Fix |
|---|---|---|---|
| CEP001 | Error | Spec must be sealed and nested | Make the spec class `sealed` and nest it inside a container. |
| CEP002 | Error | Container must have marker | Add `[CapabilityEndpointSpecs]` to the outer container class. |
| CEP003 | Error | Spec has methods or ctor params | Remove methods and constructors with parameters from the spec class. |
| CEP004 | Error | Spec inside `[CrestService]` | Move the spec class outside the `[CrestService]` type. |
| CEP005 | Error | Spec with `[DynamicApiRoute]` | Remove `[DynamicApiRoute]` from the spec class. |
| CEP008 | Error | Route body DTO missing property | Add a settable property to the body DTO matching each route token name. |
| CEP009 | Error | Set must be static partial | Add `static` and `partial` to the `[CapabilityEndpointSet]` class. |
| CEP010 | Error | HTTP method attr not sealed/partial/nested | Make the class `sealed partial` and nest it inside a `[CapabilityEndpointSet]`. |
| CEP011 | Warning | POST/PUT/PATCH missing body | Add an explicit `Input` or `Body` type to the HTTP method attribute. |
| CEP012 | Error | Unsupported route param type | Route parameter type must be a known scalar or an enum. |
| CEP013 | Error | Multiple scalars without body | Add a body DTO or explicit input type when binding multiple scalar route/query/header parameters. |
| CEP014 | Error | Invalid scalar property name | The input `Name` is not a valid C# identifier; rename it or use `TargetProperty`. |
| CEP016 | Error | HTTP method attr outside set | Nest the HTTP method attribute inside a `[CapabilityEndpointSet]` container. |
| CEP017 | Error | EndpointId contains whitespace | Remove spaces from `EndpointId`. |
| CEP018 | Error | TargetProperty missing on body | The body DTO type does not have a public settable property with that name. |
| CEP019 | Error | TargetProperty invalid identifier | `TargetProperty` must be a valid simple C# property identifier. |
| CEP020 | Error | EndpointVersion negative | Set `EndpointVersion` to a non-negative integer. |
| CEP021 | Error | Input without route token | An explicit `Input` on a Level 2 HTTP method attribute requires at least one route token to bind to. |

---

## 13. Testing Generated Exposures

### Test Coverage by Layer

| Layer | Test Project | Approximate Test Count | What It Covers |
|---|---|---|---|
| Source Generator unit tests | `CrestCreates.CodeGenerator.Tests` | ~248 | `AppServiceCompatibilityGeneratorTests`, `CapabilityEndpointGeneratorTests`, `CapabilityEndpointDiagnosticTests`, `CapabilityEndpointLevel2GeneratorTests`, `LegacyDynamicApiAotSourceGeneratorTests`, `LegacyDynamicApiCrudMainlineTests` |
| Dynamic API integration tests | `CrestCreates.DynamicApi.Tests` | 45 | `CapabilityEndpointIntegrationTests`, `CapabilityEndpointBindingRegistryTests`, `CapabilityEndpointCapabilityResolverTests`, `CapabilityEndpointJsonRuntimeTests`, `CapabilityEndpointResultMapperTests` |
| Compatibility projection E2E | `CrestCreates.CompatibilityProjection.E2E.Tests` | 9 | Full pipeline: `AddCapabilityRuntime()` + `AddCrestCompatibilityProjection()` + `MapCrestCapabilityEndpoints()` with a real `GreetingAppService`, verifying 200 OK envelopes, 404 for null GET, 404 for unknown routes, void returns, query/route binding, middleware execution, and pipeline failure mapping |
| Web-layer tests | `CrestCreates.Web.Tests/DynamicApi/` | ~50 | `CapabilityEndpointDescriptorValidatorTests`, `CapabilityEndpointRegistryTests`, `CapabilityEndpointBoundaryTests`, `CapabilityEndpointRelationshipExtractorTests`, legacy runtime tests |
| Boundary tests | `CrestCreates.DependencyBoundaries.Tests` | 34 | Enforces assembly-level dependency rules, prevents abstractions from referencing implementations, guards against legacy types leaking into capability abstractions |

### Writing Custom E2E Tests

Use `WebApplicationFactory<T>` with a source-generator-backed test app:

1. Create a minimal `Program.cs` that registers `AddCapabilityRuntime()`, `AddCrestCompatibilityProjection()`, and `MapCrestCapabilityEndpoints()`.
2. Define a test `[CrestService]` with `[CapabilityCompatibilityProjection]`.
3. Use `DescriptorProviderRegistry.GetProviders()` to bootstrap capability providers before endpoint mapping.
4. Assert HTTP status codes, response envelopes (`code`, `message`, `data` fields), and pipeline behavior.

Refer to `tests/Framework/Api/CrestCreates.CompatibilityProjection.E2E.Tests/` as a template.

---

## 14. Migration Checklist

Follow these steps to migrate an existing `[CrestService]` AppService from legacy Dynamic API to Compatibility Projection:

1. **Add capability runtime** to DI:
   ```csharp
   builder.Services.AddCapabilityRuntime();
   builder.Services.AddCrestCompatibilityProjection();
   ```

2. **Add endpoint mapping** to the middleware pipeline:
   ```csharp
   app.MapCrestCapabilityEndpoints();
   ```

3. **Add `[CapabilityCompatibilityProjection]`** to the target AppService class:
   ```csharp
   [CrestService]
   [CapabilityCompatibilityProjection]
   public class BookAppService { ... }
   ```

4. **Build and verify generated files** in `obj/{config}/{tfm}/source-generators/`. Confirm that manifest, binding, invoker, result contract, and endpoint descriptor files are produced.

5. **Verify HTTP contract:** Run existing integration tests against the new compatibility endpoints. They should pass unchanged because the `DynamicApiResponse` envelope, status codes, and routes are preserved.

6. **Match route prefix if customized:** If `DynamicApiOptions.DefaultRoutePrefix` was changed from the default `api/`, set `RoutePrefix` on `[CapabilityCompatibilityProjection]` or add `[DynamicApiRoute]` to the service. Diagnostic CEP035 warns if this is needed.

7. **Exclude internal methods** with `[CapabilityCompatibilityIgnore]`:
   ```csharp
   [CapabilityCompatibilityIgnore]
   public Task InternalSyncAsync() { ... }
   ```

8. **Check diagnostics:** Rebuild and verify no CEP030–CEP037 errors or warnings remain. Address any that appear.

9. **Verify `DynamicApiResponse` envelope** in E2E tests: Confirm that responses have `code: 200`, `message: "操作成功"`, and `data` wrapping the actual return value. GET-returning-null should produce `code: 404`, `message: "资源不存在"`.

10. **Remove legacy endpoint mapping** after all services have migrated:
    ```csharp
    // app.MapCrestDynamicApi();  ← remove when no longer needed
    ```
    The legacy mapping can be removed once no `[CrestService]` without `[CapabilityCompatibilityProjection]` remains in the application.
