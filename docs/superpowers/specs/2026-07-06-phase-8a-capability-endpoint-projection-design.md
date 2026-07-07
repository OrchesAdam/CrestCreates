# Phase 8a — Capability Endpoint Projection Design

## 1. 定位

**8a 做一件事：让一个 Capability 不通过 AppService 就能暴露成 HTTP endpoint。**

建立 Capability-first 新主线，不对接旧 DynamicApiGenerator。旧 DynamicApi 保留为 legacy AppService HTTP exposure，后续通过 8c/8d 反向适配 Capability。

## 2. 链路

```text
[Compile time]
Level 1: [CapabilityEndpointSpecs] + [CapabilityEndpointSpec] + [CapabilityEndpointInput]
Level 2: [CapabilityEndpointSet] + [Post/Get/Put/Delete/Patch]
    ↓ (Level 2 normalize to Level 1 internally)
CapabilityEndpointGenerator
    ↓ 产物 1
GeneratedCapabilityEndpointDescriptorProvider
    → CapabilityEndpointDescriptor（projection metadata，无 CLR 细节）
    → DescriptorProviderRegistry.Register<CapabilityEndpointDescriptor>()
    ↓ 产物 2
GeneratedCapabilityEndpointBindingRegistration
    → CapabilityEndpointBindingContract(endpointId, version, BindInputAsync)
    → CapabilityEndpointBindingRegistry.Register(contract)

[Startup]
AddCrestCapabilityEndpoints()
    → register ICapabilityEndpointRegistry
    → register CapabilityEndpointRegistryBootstrapper

[Mapping]
MapCrestCapabilityEndpoints()
    → bootstrapper.EnsureBuilt()
    → registry.GetAll().Where(Active)
    → resolve CapabilityDescriptor from descriptor.Capability ref
    → lookup binding contract per (endpointId, version)
    → MapMethods(descriptor.RoutePattern, descriptor.HttpMethod)
    → request delegate uses binding.BindInputAsync()
    → dispatcher.DispatchAsync(capabilityDescriptor, InvocationSource.Http, input, ...)

[Request]
HTTP request
    → binding.BindInputAsync(context, ct) → exact TInput
    → ICapabilityDispatcher.DispatchAsync(capabilityDescriptor, Http, input, ...)
    → CapabilityPipeline.ExecuteAsync(capabilityDescriptor, ...)
    → Handler
    → CapabilityExecutionResult
    → CapabilityEndpointResultMapper
    → IResult
```

**Map-time capability resolution 语义：**

`CapabilityEndpointDescriptor.Capability` 在 `MapCrestCapabilityEndpoints()` 时解析。解析后的 `CapabilityDescriptor` 被请求委托闭包捕获。运行时 capability 激活/停用/版本升级对已映射端点不可见。要应用变更，宿主需重建 registry 并重新映射端点（通常通过应用重启）。运行时热投影推迟到未来 phase。

## 3. 四个关注点分离

| 关注点 | 载体 | 程序集 | 职责 |
|---|---|---|---|
| **Endpoint 是什么** | `CapabilityEndpointDescriptor` | `DynamicApi.Abstractions` | 投影 metadata：capability ref、HTTP method、route、authorization、input/output mapping、OpenAPI tags。不含 CLR 类型细节 |
| **如何绑定 HTTP → TInput** | `CapabilityEndpointBindingContract`（SG 产物） | `DynamicApi`（public + `[EditorBrowsable(Never)]`） | endpoint id/version + `BindInputAsync` delegate。CLR 类型信息和 parse 逻辑在这里，不进 descriptor |
| **如何执行** | `ICapabilityDispatcher` | `Metadata`（接口）+ `Capability`（实现） | 统一门面，用 `CapabilityDescriptor` overload 保留版本语义 |
| **如何映射结果** | `CapabilityEndpointResultMapper`（internal static） | `DynamicApi` | `CapabilityExecutionResult` → `IResult`，固定映射表 |

**关键决策：**

1. `BindingContract` / `BindingRegistry` / `JsonRuntime` 必须 public——SG 产物在业务程序集，无法访问 `DynamicApi` 的 internal 类型。用 `[EditorBrowsable(EditorBrowsableState.Never)]` 降低误用概率。

2. `ResultMapper` 保持 internal——它在 `MapCrestCapabilityEndpoints()` 内部被 `CapabilityEndpointMapper` 调用，不跨程序集。

3. `BindingRegistry` 沿用 `DescriptorProviderRegistry` 模式——static `ConcurrentDictionary` + `[ModuleInitializer]` 注册，runtime 查询。与 descriptor 注册平行，职责独立。

## 4. Authoring Assembly Rule

Capability Endpoint Specs **必须位于 HTTP/projection assembly**，该程序集引用 `CrestCreates.DynamicApi` 和 `Microsoft.AspNetCore.Http.Abstractions`。

SG 生成的 binding 代码引用 `HttpContext`、`CancellationToken`、`CapabilityEndpointJsonRuntime`、`CapabilityEndpointBindingRegistry`、`CapabilityEndpointBindingContract`——这些类型来自 `CrestCreates.DynamicApi` + ASP.NET Core abstractions。

**DTOs 可以留在 `Application.Contracts`，但 endpoint projection specs 不应放在纯 contracts 程序集**，除非该程序集有意引用 HTTP projection runtime。

推荐的项目结构：

```text
Application.Contracts/       — DTOs, 不引用 DynamicApi / ASP.NET
Application.CapabilityEndpoints/  — Endpoint specs, 引用 DynamicApi + ASP.NET
Application/                 — AppService, Handlers
```

## 5. DX 分层

### Level 0：Runtime canonical model

`CapabilityEndpointDescriptor` + `CapabilityEndpointBindingContract` + `CapabilityEndpointBindingRegistry` + `MapCrestCapabilityEndpoints()`。开发者一般不直接碰。

### Level 1：Explicit spec，适合复杂场景

完整控制每个字段。适合特殊 endpoint、复杂 binding、非标准 route。

```csharp
[CapabilityEndpointSpecs]
public static class BookEndpointSpecs
{
    [CapabilityEndpointSpec("books.update", CapabilityEndpointHttpMethod.Put, "/api/books/{id}")]
    [CapabilityEndpointInput(typeof(Guid), Name = "id", Source = CapabilityEndpointParameterSource.Route)]
    [CapabilityEndpointInput(typeof(UpdateBookDto), Source = CapabilityEndpointParameterSource.Body)]
    public sealed class Update { }
}
```

### Level 2：DX sugar，适合常规业务

简写 attribute，更接近"声明 HTTP projection"。SG 内部 normalize 成 Level 1 再处理，最终仍生成 Level 0。

```csharp
[CapabilityEndpointSet(RoutePrefix = "/api/books", Tags = new[] { "Books" })]
public static partial class BookEndpoints
{
    [Post("books.create", Body = typeof(CreateBookDto), SuccessStatusCode = 201)]
    public sealed partial class Create;

    [Put("books.update", "{id}", Body = typeof(UpdateBookDto))]
    public sealed partial class Update;

    [Get("books.getById", "{id}", Input = typeof(Guid),
         Auth = CapabilityEndpointAuthorizationMode.RequireAuthenticated)]
    public sealed partial class GetById;

    [Get("books.getByIsbn", "by-isbn/{isbn}", Input = typeof(string),
         Auth = CapabilityEndpointAuthorizationMode.AllowAnonymous)]
    public sealed partial class GetByIsbn;
}
```

**Route + Body convention：** SG 自动匹配 route token → DTO 同名可写属性（PascalCase）。CEP008 仍验证属性存在且可写。

**Level 2 normalize 规则：**

| Level 2 Attribute | Normalize to Level 1 |
|---|---|
| `[Post(capabilityId, route, Body, ...)]` | `[CapabilityEndpointSpec(capabilityId, Post, RoutePrefix+route)]` + `[CapabilityEndpointInput(Body, Source=Body)]` |
| `[Get(capabilityId, route, Input, ...)]` | `[CapabilityEndpointSpec(capabilityId, Get, RoutePrefix+route)]` + `[CapabilityEndpointInput(Input, Source=Route)]` |
| `[Put(capabilityId, route, Body, ...)]` | `[CapabilityEndpointSpec(capabilityId, Put, RoutePrefix+route)]` + `[CapabilityEndpointInput(Body, Source=Body)]` + auto route→body mapping |
| `[Delete(capabilityId, route, Input, ...)]` | `[CapabilityEndpointSpec(capabilityId, Delete, RoutePrefix+route)]` + `[CapabilityEndpointInput(Input, Source=Route)]` |
| (removed) | `[RouteToBody]` removed as YAGNI — convention-over-config auto-match covers 95%+ of Route+Body scenarios |

## 6. Attribute 定义

### Level 1 Attributes（`CrestCreates.DynamicApi.Abstractions`）

```csharp
// [CapabilityEndpointSpecs] — Level 1 容器标记
[AttributeUsage(AttributeTargets.Class)]
public sealed class CapabilityEndpointSpecsAttribute : Attribute { }

// [CapabilityEndpointSpec] — Level 1 每个 endpoint 的核心声明，构造函数化
[AttributeUsage(AttributeTargets.Class)]
public sealed class CapabilityEndpointSpecAttribute : Attribute
{
    public CapabilityEndpointSpecAttribute(
        string capabilityId,
        CapabilityEndpointHttpMethod httpMethod,
        string routePattern)
    {
        CapabilityId = capabilityId;
        HttpMethod = httpMethod;
        RoutePattern = routePattern;
    }

    public string CapabilityId { get; }
    public CapabilityEndpointHttpMethod HttpMethod { get; }
    public string RoutePattern { get; }
    public int CapabilityVersion { get; init; }           // 0 = latest active
    public CapabilityEndpointAuthorizationMode AuthorizationMode { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;
    public int SuccessStatusCode { get; init; }            // 0 = auto (200/201)
    public string? OperationId { get; init; }
    public string? GroupName { get; init; }
    public string[]? Tags { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }
}

// [CapabilityEndpointInput] — Level 1 输入参数声明，构造函数化
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class CapabilityEndpointInputAttribute : Attribute
{
    public CapabilityEndpointInputAttribute(Type type)
    {
        Type = type;
    }

    public Type Type { get; }
    public string Name { get; init; } = string.Empty;
    public CapabilityEndpointParameterSource Source { get; init; }
        = CapabilityEndpointParameterSource.Body;
    public bool Required { get; init; } = true;
    public string? CapabilityInputPath { get; init; }
}

// [CapabilityEndpointOutput] — Level 1 输出映射覆盖（可选）
[AttributeUsage(AttributeTargets.Class)]
public sealed class CapabilityEndpointOutputAttribute : Attribute
{
    public int SuccessStatusCode { get; init; } = 200;
    public string? ContentType { get; init; }
}
```

### Level 2 Attributes（`CrestCreates.DynamicApi.Abstractions`）

> **命名取舍：** `[Get]`/`[Post]`/`[Put]`/`[Delete]`/`[Patch]` 命名简洁，DX 最佳。这些名字仅在 `using CrestCreates.DynamicApi;` 作用域内可见，与其他框架 attribute 冲突概率低。如果未来确实出现冲突，可在 using alias 或 namespace 限定中解决。当前选择接受这个取舍。

```csharp
// [CapabilityEndpointSet] — Level 2 容器，提供 RoutePrefix + 共享 metadata
[AttributeUsage(AttributeTargets.Class)]
public sealed class CapabilityEndpointSetAttribute : Attribute
{
    public string? RoutePrefix { get; init; }
    public string? GroupName { get; init; }
    public string[]? Tags { get; init; }
    public string? Summary { get; init; }
}

// [Post] — Level 2 POST endpoint 简写
[AttributeUsage(AttributeTargets.Class)]
public sealed class PostAttribute : Attribute
{
    public PostAttribute(string capabilityId, string route = "")
    {
        CapabilityId = capabilityId;
        Route = route;
    }

    public string CapabilityId { get; }
    public string Route { get; }                         // 相对 RoutePrefix
    public Type? Body { get; init; }                     // Body 参数类型
    public int CapabilityVersion { get; init; }        // 0 = latest active
    public CapabilityEndpointAuthorizationMode Auth { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;
    public int SuccessStatusCode { get; init; }        // 0 = auto (POST→201)
    public string? OperationId { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }
}

// [Get] — Level 2 GET endpoint 简写
[AttributeUsage(AttributeTargets.Class)]
public sealed class GetAttribute : Attribute
{
    public GetAttribute(string capabilityId, string route = "")
    {
        CapabilityId = capabilityId;
        Route = route;
    }

    public string CapabilityId { get; }
    public string Route { get; }
    public Type? Input { get; init; }                    // Route 参数类型（标量）
    public string? InputName { get; init; }            // Route 参数名（默认从 route pattern 推导）
    public int CapabilityVersion { get; init; }
    public CapabilityEndpointAuthorizationMode Auth { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;
    public string? OperationId { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }
}

// [Put] — Level 2 PUT endpoint 简写
[AttributeUsage(AttributeTargets.Class)]
public sealed class PutAttribute : Attribute
{
    public PutAttribute(string capabilityId, string route = "")
    {
        CapabilityId = capabilityId;
        Route = route;
    }

    public string CapabilityId { get; }
    public string Route { get; }
    public Type? Body { get; init; }
    public Type? Input { get; init; }                    // Route 参数类型（可选，标量）
    public string? InputName { get; init; }
    public int CapabilityVersion { get; init; }
    public CapabilityEndpointAuthorizationMode Auth { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;
    public int SuccessStatusCode { get; init; }
    public string? OperationId { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }
}

// [Delete] — Level 2 DELETE endpoint 简写
[AttributeUsage(AttributeTargets.Class)]
public sealed class DeleteAttribute : Attribute
{
    public DeleteAttribute(string capabilityId, string route = "")
    {
        CapabilityId = capabilityId;
        Route = route;
    }

    public string CapabilityId { get; }
    public string Route { get; }
    public Type? Input { get; init; }
    public string? InputName { get; init; }
    public int CapabilityVersion { get; init; }
    public CapabilityEndpointAuthorizationMode Auth { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;
    public string? OperationId { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }
}

// [Patch] — Level 2 PATCH endpoint 简写
[AttributeUsage(AttributeTargets.Class)]
public sealed class PatchAttribute : Attribute
{
    public PatchAttribute(string capabilityId, string route = "")
    {
        CapabilityId = capabilityId;
        Route = route;
    }

    public string CapabilityId { get; }
    public string Route { get; }
    public Type? Body { get; init; }
    public Type? Input { get; init; }
    public string? InputName { get; init; }
    public int CapabilityVersion { get; init; }
    public CapabilityEndpointAuthorizationMode Auth { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;
    public int SuccessStatusCode { get; init; }
    public string? OperationId { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }
}

// [RouteToBody] — Removed. YAGNI: convention-over-config auto-match covers 95%+ of
// Route+Body scenarios. The attribute may be reintroduced later if explicit override
// patterns prove necessary.
```

**设计决策：**

1. Level 1 构造函数化——`CapabilityId`、`HttpMethod`、`RoutePattern`、`Type` 是 required，用构造函数强制。init-only properties 是 optional。
2. Level 2 构造函数只强制 `capabilityId`，其余用 init-only。`Route` 可选——空字符串表示 RoutePrefix 本身。
3. 不加 `ErrorStatusCode`——8a 用固定映射表。
4. `SuccessStatusCode = 0` 表示 auto（POST→201，其他→200）。**SG 必须在生成时 materialize 具体值，descriptor 中不允许出现 `SuccessStatusCode = 0`。**
5. Nested sealed class 无方法、无构造函数——纯 attribute 载体，SG 只读 attribute 不读方法体。
6. Level 2 的 `[Post]`/`[Get]`/`[Put]`/`[Delete]`/`[Patch]` 命名简洁，与 HTTP method 一一对应。
7. `[RouteToBody]` removed as YAGNI — convention-over-config auto-match covers 95%+ of Route+Body scenarios.
8. `Deprecated` attribute property 映射到 `Projection.Deprecated`，不影响 `Descriptor.State`。Deprecated endpoint 的 `State` 仍为 `Active`，表示"HTTP endpoint 仍暴露，但 OpenAPI / metadata 标记为 deprecated"。
9. Level 2 的 `[Get]`/`[Post]`/`[Put]`/`[Delete]`/`[Patch]` 命名简洁但存在与其他框架 attribute 冲突的风险。接受此取舍——这些 attribute 只在 `using CrestCreates.DynamicApi;` 时进入候选，且 DX 收益大于冲突风险。

## 7. CapabilityEndpointGenerator 产物

SG 位于 `src/Tooling/CrestCreates.CodeGenerator/CapabilityEndpointGenerator/`。

**Generator discovery 同时监听 Level 1 和 Level 2 attributes：**

- `ForAttributeWithMetadataName("CrestCreates.DynamicApi.CapabilityEndpointSpecAttribute")` — Level 1
- `ForAttributeWithMetadataName("CrestCreates.DynamicApi.CapabilityEndpointSetAttribute")` — Level 2 容器
- `ForAttributeWithMetadataName("CrestCreates.DynamicApi.PostAttribute")` — Level 2 POST
- `ForAttributeWithMetadataName("CrestCreates.DynamicApi.GetAttribute")` — Level 2 GET
- `ForAttributeWithMetadataName("CrestCreates.DynamicApi.PutAttribute")` — Level 2 PUT
- `ForAttributeWithMetadataName("CrestCreates.DynamicApi.DeleteAttribute")` — Level 2 DELETE
- `ForAttributeWithMetadataName("CrestCreates.DynamicApi.PatchAttribute")` — Level 2 PATCH

Level 2 attributes 在 SG 内部 normalize 成 Level 1 后，走同一套 Provider + Bindings 生成逻辑。

**Emission de-duplication rule：** Generator may listen to both container and child attributes for diagnostics, but endpoint emission must be de-duplicated by normalized endpoint key `(EndpointId, Version)`. Child HTTP method attributes (`[Post]`, `[Get]`, `[Put]`, `[Delete]`, `[Patch]`) are the primary emission source; container attributes (`[CapabilityEndpointSet]`) are used only to provide defaults and validation context. This prevents double registration when both container and child attributes trigger the same generation pipeline.

### 产物 1：`{Container}_Provider.g.cs`

生成 `ICapabilityEndpointDescriptorProvider` + `[ModuleInitializer]` 注册。

Descriptor 不含 CLR 类型细节。`InputBinding` 保持现有 4 个字段：`Name`, `Source`, `CapabilityInputPath`, `Required`。

```csharp
internal sealed class BookEndpointSpecs_Provider : ICapabilityEndpointDescriptorProvider
{
    public IReadOnlyList<CapabilityEndpointDescriptor> GetDescriptors()
    {
        return new[]
        {
            new CapabilityEndpointDescriptor
            {
                Id = "endpoint:books.create",
                Name = "Create",
                Version = 1,
                State = DescriptorState.Active,
                Capability = new VersionedDescriptorRef<CapabilityDescriptor>(
                    "books.create", 0, VersionSelectionMode.LatestActive),
                HttpMethod = CapabilityEndpointHttpMethod.Post,
                RoutePattern = "/api/books",
                AuthorizationMode = CapabilityEndpointAuthorizationMode.InheritCapability,
                InputBindings = new[]
                {
                    new CapabilityEndpointInputBinding
                    {
                        Name = "input",
                        Source = CapabilityEndpointParameterSource.Body,
                        Required = true
                    }
                },
                OutputMapping = new CapabilityEndpointOutputMapping
                {
                    SuccessStatusCode = 201
                },
                Projection = new CapabilityEndpointProjectionMetadata
                {
                    OperationId = "books_create",
                    Tags = new[] { "Books" }
                }
            },
            // ... Update, GetById, GetByIsbn ...
        };
    }
}

internal static class BookEndpointSpecs_Registration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register()
        => DescriptorProviderRegistry.Register<CapabilityEndpointDescriptor>(
            new BookEndpointSpecs_Provider());
}
```

### 产物 2：`{Container}_Bindings.g.cs`

生成 `BindInputAsync` 函数 + `[ModuleInitializer]` 注册到 `CapabilityEndpointBindingRegistry`。

CLR 类型信息和 parse 逻辑在这里，不进 descriptor。

```csharp
internal static class BookEndpointSpecs_Bindings
{
    // POST /api/books — Body → CreateBookDto
    private static async ValueTask<object?> BindBooksCreateAsync(
        HttpContext context, CancellationToken ct)
    {
        return await CapabilityEndpointJsonRuntime
            .ReadBodyAsync<CreateBookDto>(context, optional: false, ct);
    }

    // PUT /api/books/{id} — Route(Guid id) + Body(UpdateBookDto) → UpdateBookDto
    private static async ValueTask<object?> BindBooksUpdateAsync(
        HttpContext context, CancellationToken ct)
    {
        var id = Guid.Parse(context.Request.RouteValues["id"]!.ToString()!);
        var input = await CapabilityEndpointJsonRuntime
            .ReadBodyAsync<UpdateBookDto>(context, optional: false, ct);
        input.Id = id;  // Route value materialize 进 TInput
        return input;
    }

    // GET /api/books/{id} — Route(Guid id) → Guid (scalar)
    private static ValueTask<object?> BindBooksGetByIdAsync(
        HttpContext context, CancellationToken ct)
    {
        var id = Guid.Parse(context.Request.RouteValues["id"]!.ToString()!);
        return new ValueTask<object?>(id);
    }

    // GET /api/books/by-isbn/{isbn} — Route(string isbn) → string (scalar)
    private static ValueTask<object?> BindBooksGetByIsbnAsync(
        HttpContext context, CancellationToken ct)
    {
        var isbn = context.Request.RouteValues["isbn"]!.ToString()!;
        return new ValueTask<object?>(isbn);
    }
}

internal static class BookEndpointSpecs_BindingRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void RegisterBindings()
    {
        CapabilityEndpointBindingRegistry.Register(
            new CapabilityEndpointBindingContract(
                "endpoint:books.create", 1,
                BookEndpointSpecs_Bindings.BindBooksCreateAsync));

        CapabilityEndpointBindingRegistry.Register(
            new CapabilityEndpointBindingContract(
                "endpoint:books.update", 1,
                BookEndpointSpecs_Bindings.BindBooksUpdateAsync));

        CapabilityEndpointBindingRegistry.Register(
            new CapabilityEndpointBindingContract(
                "endpoint:books.getById", 1,
                BookEndpointSpecs_Bindings.BindBooksGetByIdAsync));

        CapabilityEndpointBindingRegistry.Register(
            new CapabilityEndpointBindingContract(
                "endpoint:books.getByIsbn", 1,
                BookEndpointSpecs_Bindings.BindBooksGetByIsbnAsync));
    }
}
```

**SG 不生成 `MapAll()` 或任何直接 `MapMethods` 的代码。** Endpoint mapping 由 runtime `MapCrestCapabilityEndpoints()` 从 registry + binding contract 驱动。

## 8. Input Materialization 规则

**原则：业务参数必须 materialize 成 handler 需要的 TInput，不藏在 ctx.Items。**

| 场景 | Materialization | `input` 传给 dispatcher |
|---|---|---|
| 只有 Body | `ReadBodyAsync<TInput>(context, false, ct)` | body 对象 |
| 只有 Route (标量) | `Guid.Parse(routeValues["id"])` | 标量值本身 |
| Route + Body | `ReadBodyAsync<TBody>` + route value 赋值到 body 的同名可写属性 | body 对象（route 已赋值进去） |
| Query (标量) | `context.Request.Query["name"]` → parse | 标量值本身 |
| Header (标量) | `context.Request.Headers["name"]` → parse | 标量值本身 |

**Route + Body 的关键约定：**

SG 生成的代码把 route value 直接赋值到 body DTO 的同名可写属性上。这要求 DTO 有对应的可写属性。

例如 `Update` endpoint：`RoutePattern = "/api/books/{id}"` + `Body = UpdateBookDto`：
- SG 检查 `UpdateBookDto` 是否有可写属性 `Id`（匹配 route token `id`，PascalCase）
- 如果有 → 生成 `input.Id = id;`
- 如果没有 → 报 CEP008：`UpdateBookDto does not have a settable property 'Id' for route parameter 'id'`

**ctx.Items 只放 projection metadata：**
- `HttpTraceIdentifier` — HTTP trace
- `CapabilityEndpointId` — endpoint descriptor id

**configureContext 回调：**

```csharp
ctx =>
{
    ctx.CausationId = context.TraceIdentifier;
    ctx.IdempotencyKey = ResolveIdempotencyKey(context);
    ctx.Items["HttpTraceIdentifier"] = context.TraceIdentifier;
    ctx.Items["CapabilityEndpointId"] = descriptor.Id;
}
```

- **不改 `CorrelationId`**（它是 `init`，保持自动生成）
- `CausationId` = HTTP `TraceIdentifier`（可 set）
- `IdempotencyKey` = 从 `Idempotency-Key` header 解析（可 set）

## 9. Runtime 组件

### 9.1 CapabilityEndpointBindingContract

```csharp
// CrestCreates.DynamicApi — public + [EditorBrowsable(Never)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record CapabilityEndpointBindingContract(
    string EndpointId,
    int EndpointVersion,
    Func<HttpContext, CancellationToken, ValueTask<object?>> BindInputAsync);
```

### 9.2 CapabilityEndpointBindingRegistry

```csharp
// CrestCreates.DynamicApi — public + [EditorBrowsable(Never)]
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CapabilityEndpointBindingRegistry
{
    private static readonly ConcurrentDictionary<(string EndpointId, int Version),
        CapabilityEndpointBindingContract> _bindings = new();

    public static void Register(CapabilityEndpointBindingContract contract)
    {
        if (!_bindings.TryAdd((contract.EndpointId, contract.EndpointVersion), contract))
        {
            throw new InvalidOperationException(
                $"Duplicate capability endpoint binding contract for endpoint " +
                $"'{contract.EndpointId}' version {contract.EndpointVersion}.");
        }
    }

    public static CapabilityEndpointBindingContract? Find(string endpointId, int version)
        => _bindings.TryGetValue((endpointId, version), out var contract) ? contract : null;

    public static CapabilityEndpointBindingContract GetRequired(string endpointId, int version)
        => Find(endpointId, version)
            ?? throw new InvalidOperationException(
                $"No binding contract registered for endpoint '{endpointId}' version {version}.");

    // Test-only reset hook via InternalsVisibleTo. Production code must not clear registered bindings.
    internal static void Reset()
        => _bindings.Clear();
}
```

### 9.3 CapabilityEndpointJsonRuntime

```csharp
// CrestCreates.DynamicApi — public + [EditorBrowsable(Never)]
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CapabilityEndpointJsonRuntime
{
    // Generic overload — 8a first closure
    public static async ValueTask<T?> ReadBodyAsync<T>(
        HttpContext context, bool optional, CancellationToken ct = default)
    {
        if (context.Request.ContentLength == 0 && optional)
            return default;

        var options = ResolveJsonSerializerOptions(context);
        T? result;
        try
        {
            result = await JsonSerializer.DeserializeAsync<T>(
                context.Request.Body, options, ct);
        }
        catch (JsonException ex)
        {
            throw new BadHttpRequestException(
                $"Request body could not be deserialized as {typeof(T).Name}.", ex);
        }

        if (result is null && !optional)
            throw new BadHttpRequestException(
                $"Request body could not be deserialized as {typeof(T).Name}.");

        return result;
    }

    // AOT-safe overload — SG 优先生成此路径（当 JsonSerializerContext 可用时）
    public static async ValueTask<T?> ReadBodyAsync<T>(
        HttpContext context, JsonTypeInfo<T> jsonTypeInfo,
        bool optional, CancellationToken ct = default)
    {
        if (context.Request.ContentLength == 0 && optional)
            return default;

        T? result;
        try
        {
            result = await JsonSerializer.DeserializeAsync(
                context.Request.Body, jsonTypeInfo, ct);
        }
        catch (JsonException ex)
        {
            throw new BadHttpRequestException(
                $"Request body could not be deserialized as {typeof(T).Name}.", ex);
        }

        if (result is null && !optional)
            throw new BadHttpRequestException(
                $"Request body could not be deserialized as {typeof(T).Name}.");

        return result;
    }

    private static JsonSerializerOptions ResolveJsonSerializerOptions(HttpContext context)
    {
        var jsonOptions = context.RequestServices
            .GetService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();

        return new JsonSerializerOptions(
            jsonOptions?.Value.SerializerOptions ?? new JsonSerializerOptions())
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
```

**关键决策：** 去掉 `where T : new()`。Required body 缺失时 throw `BadHttpRequestException`，optional body 缺失时返回 `default`。

### 9.4 CapabilityEndpointRegistryBootstrapper

```csharp
// CrestCreates.DynamicApi — internal
internal sealed class CapabilityEndpointRegistryBootstrapper
{
    private readonly ICapabilityEndpointRegistry _registry;
    private int _built;

    public CapabilityEndpointRegistryBootstrapper(
        ICapabilityEndpointRegistry registry)
    {
        _registry = registry;
    }

    public void EnsureBuilt()
    {
        if (Interlocked.Exchange(ref _built, 1) == 1)
            return;

        var providers = DescriptorProviderRegistry
            .GetProviders<CapabilityEndpointDescriptor>();
        _registry.Build(providers);
    }
}
```

### 9.5 CapabilityEndpointCapabilityResolver

```csharp
// CrestCreates.DynamicApi — internal
internal static class CapabilityEndpointCapabilityResolver
{
    internal static CapabilityDescriptor Resolve(
        ICapabilityRegistry registry,
        VersionedDescriptorRef<CapabilityDescriptor> capabilityRef)
    {
        // Exact version
        if (capabilityRef.Version > 0)
        {
            var exact = registry.GetByVersion(capabilityRef.Id, capabilityRef.Version);
            if (exact is not null) return exact;
        }

        // Latest active — 按 Id 查，不是按 Name
        var latest = registry.GetById(capabilityRef.Id);
        if (latest?.State == DescriptorState.Active) return latest;

        // Fallback: scan all versions for active
        var active = registry.GetAll()
            .Where(d => d.Id == capabilityRef.Id && d.State == DescriptorState.Active)
            .MaxBy(d => d.Version);
        if (active is not null) return active;

        // Last resort: return latest even if not active
        if (latest is not null) return latest;

        throw new InvalidOperationException(
            $"Capability '{capabilityRef.Id}' version {capabilityRef.Version} " +
            "could not be resolved.");
    }

    // ExpectedContractHash validation deferred — 8a 不做 hash 校验。
    // When implemented: if capabilityRef.ExpectedContractHash is not null,
    // the resolved CapabilityDescriptor contract hash must match it;
    // otherwise MapCrestCapabilityEndpoints() fails closed.

    // 8a supports Exact version and LatestActive semantics only.
    // Other VersionSelectionMode values are out of scope and should fail closed
    // or be normalized by SG.
}
```

### 9.6 CapabilityEndpointResultMapper

```csharp
// CrestCreates.DynamicApi — internal
internal static class CapabilityEndpointResultMapper
{
    public static IResult Map(CapabilityExecutionResult result, CapabilityEndpointOutputMapping outputMapping)
    {
        return result.Status switch
        {
            CapabilityExecutionStatus.Succeeded
                => MapSuccess(result.Output, outputMapping),
            CapabilityExecutionStatus.Failed
                => MapFailure(result),
            CapabilityExecutionStatus.TimedOut
                => Results.StatusCode(StatusCodes.Status504GatewayTimeout),
            CapabilityExecutionStatus.Compensated
                => Results.StatusCode(StatusCodes.Status409Conflict),
            _
                => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static IResult MapSuccess(object? output, CapabilityEndpointOutputMapping mapping)
    {
        if (output is null)
            return Results.StatusCode(mapping.SuccessStatusCode);

        return Results.Json(
            output,
            statusCode: mapping.SuccessStatusCode,
            contentType: mapping.ContentType);
    }

    private static IResult MapFailure(CapabilityExecutionResult result)
    {
        return result.ErrorCode switch
        {
            "UNAUTHORIZED" => Results.Forbid(),
            "CAPABILITY_NOT_FOUND" => Results.NotFound(),
            "CAPABILITY_VALIDATION_FAILED" => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["Input"] = new[] { result.ErrorMessage ?? "Validation failed." }
                }),
            "RATE_LIMIT_EXCEEDED"
                => Results.StatusCode(StatusCodes.Status429TooManyRequests),
            _
                => Results.Problem(result.ErrorMessage, statusCode: 500)
        };
    }
}
```

### 9.7 CapabilityEndpointMapper

```csharp
// CrestCreates.DynamicApi — internal
internal static class CapabilityEndpointMapper
{
    public static void MapEndpoint(
        IEndpointRouteBuilder endpoints,
        CapabilityEndpointDescriptor descriptor,
        CapabilityDescriptor capability,
        CapabilityEndpointBindingContract binding)
    {
        var httpMethod = descriptor.HttpMethod.ToString().ToUpperInvariant();

        var routeHandler = endpoints.MapMethods(
            descriptor.RoutePattern,
            new[] { httpMethod },
            async (HttpContext context) =>
            {
                var input = await binding.BindInputAsync(context, context.RequestAborted);

                var dispatcher = context.RequestServices
                    .GetRequiredService<ICapabilityDispatcher>();
                var result = await dispatcher.DispatchAsync(
                    capability, InvocationSource.Http, input,
                    ctx =>
                    {
                        ctx.CausationId = context.TraceIdentifier;
                        ctx.IdempotencyKey = ResolveIdempotencyKey(context);
                        ctx.Items["HttpTraceIdentifier"] = context.TraceIdentifier;
                        ctx.Items["CapabilityEndpointId"] = descriptor.Id;
                    },
                    context.RequestAborted);

                return CapabilityEndpointResultMapper.Map(result, descriptor.OutputMapping);
            });

        // Apply endpoint metadata
        routeHandler.WithDisplayName($"{descriptor.Capability.Id} → {descriptor.RoutePattern}");

        if (descriptor.Projection.Tags is { Count: > 0 } tags)
            routeHandler.WithTags(tags.ToArray());
        if (descriptor.Projection.OperationId is not null)
            routeHandler.WithName(descriptor.Projection.OperationId);

        // 8a only applies Tags and OperationId to Minimal API metadata.
        // GroupName/Summary/Description/Deprecated/Visibility are stored in Projection
        // metadata for future OpenAPI integration.

        // Authorization
        if (descriptor.AuthorizationMode == CapabilityEndpointAuthorizationMode.RequireAuthenticated)
            routeHandler.RequireAuthorization();
        else if (descriptor.AuthorizationMode == CapabilityEndpointAuthorizationMode.AllowAnonymous)
            routeHandler.AllowAnonymous();
    }

    private static string ResolveIdempotencyKey(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("Idempotency-Key", out var key)
            && !string.IsNullOrWhiteSpace(key))
            return key!;
        return Guid.NewGuid().ToString("N");
    }
}
```

### 9.8 Startup 扩展方法

```csharp
// Add — 注册阶段不 throw，Map 阶段 fail-closed
public static IServiceCollection AddCrestCapabilityEndpoints(
    this IServiceCollection services,
    Action<CapabilityEndpointOptions>? configure = null)
{
    services.TryAddSingleton<ICapabilityEndpointRegistry, CapabilityEndpointRegistry>();
    services.TryAddSingleton<IRegistryValidationEngine<CapabilityEndpointDescriptor>,
        RegistryValidationEngine<CapabilityEndpointDescriptor>>();
    services.TryAddSingleton<CapabilityEndpointRegistryBootstrapper>();

    var options = new CapabilityEndpointOptions();
    configure?.Invoke(options);
    services.AddSingleton(options);

    return services;
}

// Map — fail-closed 在此阶段
public static IEndpointRouteBuilder MapCrestCapabilityEndpoints(
    this IEndpointRouteBuilder endpoints)
{
    var bootstrapper = endpoints.ServiceProvider
        .GetRequiredService<CapabilityEndpointRegistryBootstrapper>();
    bootstrapper.EnsureBuilt();

    var registry = endpoints.ServiceProvider
        .GetRequiredService<ICapabilityEndpointRegistry>();
    var capabilityRegistry = endpoints.ServiceProvider
        .GetRequiredService<ICapabilityRegistry>();

    foreach (var descriptor in registry.GetAll()
        .Where(x => x.State == DescriptorState.Active))
    {
        var binding = CapabilityEndpointBindingRegistry
            .GetRequired(descriptor.Id, descriptor.Version);

        var capability = CapabilityEndpointCapabilityResolver
            .Resolve(capabilityRegistry, descriptor.Capability);

        CapabilityEndpointMapper.MapEndpoint(
            endpoints, descriptor, capability, binding);
    }

    return endpoints;
}
```

## 10. Prerequisite — ICapabilityPipeline Descriptor Overload

当前 `ICapabilityPipeline` 只有 `string capabilityIdOrName` overload。`CapabilityDispatcher.DispatchAsync(CapabilityDescriptor, ...)` 把 `descriptor.Id` 传给 pipeline，pipeline 内部重新 resolve，丢失 version 语义。

**变更：**

```csharp
public interface ICapabilityPipeline
{
    Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityDescriptor descriptor,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);

    Task<CapabilityExecutionResult> ExecuteAsync(
        string capabilityIdOrName,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);
}
```

`CapabilityPipeline` 实现：

- **Descriptor overload**：直接用传入的 descriptor，不 re-resolve。跳过 `_registry.GetById/GetActiveVersion/GetByName` 查找。
- **String overload**：保持现有 resolve 逻辑，最后委托给 descriptor overload。

`CapabilityDispatcher` 对应更新：

- `DispatchAsync(CapabilityDescriptor, ...)` 直接传 descriptor 给 `_pipeline.ExecuteAsync(descriptor, ...)`，不再只传 `descriptor.Id`。

**影响范围：** `ICapabilityPipeline`（Abstractions）、`CapabilityPipeline`（实现）、`CapabilityDispatcher`（实现）。

**这是 controlled interface expansion。** 现有 string overload 行为不变，但自定义 `ICapabilityPipeline` 实现必须实现新的 descriptor overload。框架内置 `CapabilityPipeline` 直接修改。

**关键测试要求：** `DispatchAsync(CapabilityDescriptor, ...)` 必须执行传入的 descriptor，不得 re-resolve by id。这需要集成测试验证——当 registry 中 capability version 发生变化时，descriptor overload 的执行结果应使用 map-time 捕获的 descriptor，而非 request-time 重新解析的结果。

## 11. Authorization

| Mode | HTTP 层 | Pipeline 层 |
|---|---|---|
| `InheritCapability` | 无 | `AuthorizationMiddleware` 检查 `descriptor.Permissions` |
| `RequireAuthenticated` | `.RequireAuthorization()` | `AuthorizationMiddleware` 仍检查（双重保障） |
| `AllowAnonymous` | `.AllowAnonymous()` | `AuthorizationMiddleware` 仍执行，permissions 为空 → 自然放行 |

Pipeline 的 `AuthorizationMiddleware` **始终执行**。HTTP 层只做短路优化——`RequireAuthenticated` 让 ASP.NET Core auth middleware 在 pipeline 之前就拒绝未认证请求，避免无意义的 dispatcher 调用。

`AllowAnonymous` 安全性由 `CapabilityEndpointDescriptorValidator` 保证：`AllowAnonymous` is valid only when the target capability has no permissions and is not high risk. Otherwise descriptor validation fails with error.

## 12. 新旧主线边界

**不共享的：** Descriptor 类型、执行模型、权限模型、结果映射、Registry、Provider、DI 注册、Endpoint mapping

**唯一共享：**
- `DescriptorKind.DynamicApiEndpoint`（value = 7）— 语义分类值
- `GenerateParseExpression` — CodeGenerator 内部 helper method（不是运行时依赖）

**明确不复用：**
- `DynamicApiGeneratedRuntime` — 用 `CapabilityEndpointJsonRuntime` 替代
- `DynamicApiGeneratedRegistryStore` — 用 `CapabilityEndpointBindingRegistry` 替代
- `DynamicApiEndpointDescriptor` — 8a 不生成此类型
- `IDynamicApiGeneratedProvider` — 8a 不使用此接口
- `DynamicApiServiceDescriptor` / `DynamicApiActionDescriptor` — 旧 AppService 概念

**迁移方向：** 不让 CapabilityEndpoint 适配旧 DynamicApi。旧 DynamicApi 未来适配 Capability（8c legacy 降级，8d AppService→Capability 兼容生成器）。

**Phase 路线图：**
- **8a**（本 spec）：Capability Endpoint Projection 新主线闭合
- **8c**：Legacy DynamicApi 降级，明确标注不再加新能力
- **8d**：AppService → Capability compatibility generator，旧体验保留但运行时主链统一

## 13. Analyzer 诊断

8a minimum diagnostics for Level 1 and Level 2 authoring：

| Code | Severity | 规则 |
|---|---|---|
| CEP001 | Error | `[CapabilityEndpointSpec]` 必须在 sealed nested class 上 |
| CEP002 | Error | 容器必须有 `[CapabilityEndpointSpecs]` |
| CEP003 | Error | Spec 类不能有方法或构造函数 |
| CEP004 | Error | 不能在 `[CrestService]` 类型内 |
| CEP005 | Error | 不能与 `[DynamicApiRoute]` 同时出现 |
| CEP008 | Error | Route+Body 时 DTO 缺对应可写属性 |
| CEP009 | Error | Level 2 容器必须有 `[CapabilityEndpointSet]` |
| CEP010 | Error | `[Post]`/`[Get]`/`[Put]`/`[Delete]`/`[Patch]` 必须在 `[CapabilityEndpointSet]` 容器内 |
| CEP011 | Error | Level 1 和 Level 2 attribute 不能混用 |

CEP008 进 8a 的原因：SG 生成 `input.Id = id;` 前必须验证属性存在且可写，否则用户看到的是 generated code 编译错误，不是清晰的 analyzer diagnostic。

CEP006/CEP007 编号保留给后续诊断，以保持编号稳定性。

后续（不在 8a）：

| Code | Severity | 规则 |
|---|---|---|
| CEP006 | Warning | Body 参数缺对应 TInput 类型 |
| CEP007 | Error | `AllowAnonymous` + high-risk capability |

## 14. 8a 不做的事

- ❌ 不改 `MetadataBootstrapper.BuildAll()`
- ❌ 不改 `CapabilityExecutionContext.CorrelationId`（保持 `init`）
- ❌ 不改 `CapabilityEndpointInputBinding`（不加 `ClrTypeName`）
- ❌ 不改 `CapabilityEndpointOutputMapping`（不加 `ErrorStatusCode`）
- ❌ 不把 route/body 业务参数塞 `ctx.Items`
- ❌ 不生成 `DynamicApiEndpointDescriptor`
- ❌ 不伪造 `ServiceType/ActionName/RequiresTransaction`
- ❌ 不复用 `DynamicApiGeneratedRegistryStore`
- ❌ 不合并 `MapCrestDynamicApi()` 和 `MapCrestCapabilityEndpoints()`
- ❌ 不让 SG 直接 `MapMethods`（runtime mapper 做，registry 过滤 Active）
- ❌ 不用 `DispatchAsync(string capabilityId, ...)`（用 descriptor overload 保留 version）
- ❌ 不长期依赖 `DynamicApiGeneratedRuntime.ReadBodyAsync<T>()`（用 `CapabilityEndpointJsonRuntime` 替代）
- ❌ 不做 CEP006/CEP007（后续 phase）
- ❌ 不做 AppService → Capability 兼容生成器（8d 的事）
- ❌ 不做 Legacy DynamicApi 降级标注（8c 的事）

**0 个已有 descriptor model 变更。** 不改 `CapabilityEndpointDescriptor`、`CapabilityEndpointInputBinding`、`CapabilityEndpointOutputMapping`、`CapabilityExecutionContext`。`ICapabilityPipeline` 新增 overload 是 controlled interface expansion。

## 15. 实现步骤

| Step | 内容 | 依赖 | 涉及项目 |
|---|---|---|---|
| 1 | `ICapabilityPipeline` 新增 `CapabilityDescriptor` overload + `CapabilityPipeline` 实现 + `CapabilityDispatcher` 更新 | 无 | Capability.Abstractions, Capability, Metadata |
| 2 | `CapabilityEndpointJsonRuntime`（public + `[EditorBrowsable(Never)]`） | 无 | DynamicApi |
| 3 | `CapabilityEndpointResultMapper`（internal static） | 无 | DynamicApi |
| 4 | `CapabilityEndpointBindingContract` + `CapabilityEndpointBindingRegistry`（public + `[EditorBrowsable(Never)]`） | 无 | DynamicApi |
| 5 | Level 1 Attribute 定义（4 个） | 无 | DynamicApi.Abstractions |
| 6 | Level 2 Attribute 定义（6 个：Set, Post, Get, Put, Delete, Patch） | 无 | DynamicApi.Abstractions |
| 7 | `CapabilityEndpointRegistryBootstrapper`（internal） | 无 | DynamicApi |
| 8 | `CapabilityEndpointCapabilityResolver`（internal） | 无 | DynamicApi |
| 9 | `AddCrestCapabilityEndpoints()` + `MapCrestCapabilityEndpoints()` + `CapabilityEndpointMapper` | Step 1-8 | DynamicApi |
| 10 | `CapabilityEndpointGenerator` — Level 1 path（Provider + Bindings） | Step 2, 4, 5 | CodeGenerator |
| 11 | `CapabilityEndpointGenerator` — Level 2 normalize → Level 1 + Provider + Bindings | Step 6, 10 | CodeGenerator |
| 12 | Analyzer（CEP001-CEP005 + CEP008-CEP011） | Step 5, 6 | CodeGenerator |
| 13 | 功能测试 | Step 9, 11 | DynamicApi.Tests, Web.Tests |
| 14 | SG 测试（Level 1 and Level 2） | Step 10, 11 | CodeGenerator.Tests |
| 15 | 边界测试 | Step 9, 11 | DependencyBoundaries.Tests |

Step 1 是 prerequisite，与 Step 2-8 无依赖，可并行。
Step 10 和 Step 11 顺序执行——Level 2 normalize 依赖 Level 1 生成逻辑。
