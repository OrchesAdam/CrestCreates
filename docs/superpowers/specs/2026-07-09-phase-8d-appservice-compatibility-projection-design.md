# Phase 8d — AppService-to-Capability Compatibility Projection Design

**Date**: 2026-07-09
**Status**: Approved
**Issue**: #22
**Depends on**: Phase 8a (Capability Endpoint Projection), Phase 8c (Legacy Dynamic API Boundary)

---

## 1. 定位

8d 做一件事：**让现有 `[CrestService]` AppService 方法 opt-in 地跑在 Capability Pipeline 上，同时保持外部 HTTP contract 不变。**

这是单向迁移桥：AppService → Capability，不反向。

```text
Existing AppService method
    ↓ source generator compatibility projection
Generated CapabilityDescriptor (ProjectionKind = AppServiceCompatibility)
    ↓
Generated ICapabilityContextAwareHandlerInvoker (resolves AppService via DI)
    ↓
Generated CapabilityEndpointDescriptor
    ↓
Capability Endpoint Projection / generated HTTP binding
    ↓
ICapabilityDispatcher.DispatchAsync(..., InvocationSource.Http, ...)
    ↓
CapabilityPipeline (Authorization → Validation → Audit → Tenant → ...)
    ↓
service.Method(...) through generated compatibility handler
```

---

## 2. Opt-in 机制

### 2.1 Attributes

```csharp
// CrestCreates.Domain.Shared.Attributes
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class CapabilityCompatibilityProjectionAttribute : Attribute
{
    /// <summary>
    /// Override the capability ID prefix.
    /// Default: service name (stripped AppService/Service suffix) in kebab-case,
    /// prefixed with "compat.appservice.".
    /// Example: BookAppService → compat.appservice.book
    /// </summary>
    public string? CapabilityIdPrefix { get; init; }

    /// <summary>
    /// Override the route prefix.
    /// Default: derived from [DynamicApiRoute] or service name convention.
    /// </summary>
    public string? RoutePrefix { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CapabilityCompatibilityIgnoreAttribute : Attribute
{
}
```

### 2.2 语义规则

| 类级 | 方法级 | 结果 |
|---|---|---|
| `[CapabilityCompatibilityProjection]` | 无 | 所有 eligible 方法 projected |
| `[CapabilityCompatibilityProjection]` | `[CapabilityCompatibilityIgnore]` | 该方法排除 |
| 无 | `[CapabilityCompatibilityProjection]` | 仅该方法 projected |
| 无 | 无 | 不 projected（走 legacy） |

### 2.3 使用方式

```csharp
[CrestService]
[CapabilityCompatibilityProjection]  // ← 加这一行，该 AppService 的方法走 Capability Pipeline
public class BookAppService
    : CrestAppServiceBase<Book, Guid, BookDto, CreateBookDto, UpdateBookDto>,
      IBookAppService
{
    // 所有方法自动 projected

    [CapabilityCompatibilityIgnore]  // ← 排除特定方法
    public Task<List<BookDto>> GetInternalAsync(CancellationToken ct) => ...;
}
```

### 2.4 opt-in 抑制 legacy 生成

`[CapabilityCompatibilityProjection]` 出现时，`DynamicApiAotSourceGenerator` 必须跳过对应的 legacy 生成。两级抑制：

- **class-level `[CapabilityCompatibilityProjection]`** → 跳过整个 service（`IsDynamicApiImplementation` 返回 false）
- **method-level `[CapabilityCompatibilityProjection]`** → `BuildActionModels` 排除该方法（与 `[DynamicApiIgnore]` 同等逻辑）

理由：
- opt-in 的语义是"这个 service/method 走新路径"，legacy 继续生成是语义矛盾
- 符合"唯一主链"原则——同一 service/method 不应同时走两条路径
- method-level 不抑制会导致同 route + method 冲突

实现方式：
1. `DynamicApiAotSourceGenerator.IsDynamicApiImplementation` 增加 class-level `[CapabilityCompatibilityProjection]` 排除条件
2. `DynamicApiAotSourceGenerator.BuildActionModels` 增加 method-level `[CapabilityCompatibilityProjection]` 排除条件（与现有 `[DynamicApiIgnore]` 检查并列）

---

## 3. Capability Identity Namespace

使用 `compat.appservice.` 前缀隔离，避免与 native capability 命名空间冲突。

```text
CapabilityId:  compat.appservice.book.create
EndpointId:    endpoint:compat.appservice.book.create
```

默认前缀推导规则：
- `BookAppService` → 去除 `AppService` 后缀 → `Book` → kebab-case → `book` → `compat.appservice.book`
- `BookService` → 去除 `Service` 后缀 → `Book` → kebab-case → `book` → `compat.appservice.book`
- `LibraryManagementBookAppService` → 去除 `AppService` 后缀 → `LibraryManagementBook` → kebab-case → `library-management-book` → `compat.appservice.library-management-book`

显式 override 仍然允许：
```csharp
[CapabilityCompatibilityProjection(CapabilityIdPrefix = "book")]
// → CapabilityId: book.create (override, 开发者自行负责命名空间冲突)
```

---

## 4. SG 产物

当 `AppServiceCompatibilityGenerator` 发现 `[CrestService]` + `[CapabilityCompatibilityProjection]` 时，为每个 AppService 方法生成 5 个文件。

### 4.1 约定推导

复用 `DynamicApiAotSourceGenerator` 的约定推导逻辑。实现方式：把 `DynamicApiAotSourceGenerator` 中的约定推导方法从 `private static` 改为 `internal static`，移到 `DynamicApiConventionAnalyzer` 类。两个 generator 在同一 `CrestCreates.CodeGenerator` 程序集内，`internal` 可见性足够。

推导方法清单（从 `DynamicApiAotSourceGenerator` 提取到 `DynamicApiConventionAnalyzer`：

| 方法 | 签名 | 用途 |
|---|---|---|
| `ResolveHttpMethod` | `static string ResolveHttpMethod(string methodName)` | 方法名 → HTTP method |
| `ResolveActionRoute` | `static string ResolveActionRoute(IMethodSymbol methodSymbol)` | 方法 → route suffix |
| `ResolvePermission` | `static string ResolvePermission(string serviceName, string methodName)` | 方法名 → 权限名 |
| `ResolveServiceRoute` | `static ServiceRouteModel ResolveServiceRoute(INamedTypeSymbol serviceType, string serviceName, INamedTypeSymbol? dynamicApiRouteAttribute)` | service → route prefix |
| `TrimServiceName` | `static string TrimServiceName(string serviceTypeName)` | 去除 I 前缀和 AppService/Service 后缀 |
| `TrimAsyncSuffix` | `static string TrimAsyncSuffix(string methodName)` | 去除 Async 后缀 |
| `ToKebabCase` | `static string ToKebabCase(string value)` | PascalCase → kebab-case |
| `ResolveParameterSource` | `static ParameterSource ResolveParameterSource(IParameterSymbol parameter, ISet<string> routeTokens, string httpMethod, ref bool bodyAssigned)` | 参数 → Route/Query/Body/Header/CancellationToken |
| `IsScalar` | `static bool IsScalar(ITypeSymbol typeSymbol)` | 判断参数是否为标量类型 |

**注意**：`BuildServiceModels` 和 `BuildActionModels` 是编排方法（非纯约定逻辑），保留在 `DynamicApiAotSourceGenerator` 中。8d generator 使用提取的 primitive 方法自建发现逻辑。

**Model 类型同步提取为 internal：**

以下 private nested 类型从 `DynamicApiAotSourceGenerator` 移到 `DynamicApiConventionAnalyzer`，改为 `internal sealed record` / `internal enum`：

| 类型 | 原可见性 | 新可见性 | 定义 |
|---|---|---|---|
| `ServiceModel` | `private sealed record` | `internal sealed record` | Service 分析结果 |
| `ActionModel` | `private sealed record` | `internal sealed record` | Action 分析结果 |
| `ParameterModel` | `private sealed record` | `internal sealed record` | 参数分析结果 |
| `QueryPropertyModel` | `private sealed record` | `internal sealed record` | Query 参数模型 |
| `ReturnModel` | `private sealed record` | `internal sealed record` | 返回值模型 |
| `ServiceRouteModel` | `private sealed record` | `internal sealed record` | Route 模板 + IsCustom |
| `ParameterSource` | `private enum` | `internal enum` | Route/Query/Body/Header/CancellationToken |
| `CrudAction` | `private enum` | `internal enum` | Get/GetList/Create/Update/Delete |

**关键保证：**
- 方法签名和体不变，只改可见性和位置
- Model 类型属性不变，只改可见性
- 生成代码完全不变（方法体原封不动）
- 推导一致性由"同一方法调用"保证

约定推导映射表：

| AppService 方法约定 | → CapabilityEndpointDescriptor |
|---|---|
| `CreateAsync(CreateBookDto input)` | `Id = "endpoint:compat.appservice.book.create"`, `HttpMethod = Post`, `RoutePattern = "/api/books"`, `InputBindings = [Body(CreateBookDto)]` |
| `UpdateAsync(Guid id, UpdateBookDto input)` | `Id = "endpoint:compat.appservice.book.update"`, `HttpMethod = Put`, `RoutePattern = "/api/books/{id}"`, `InputBindings = [Route(Guid id), Body(UpdateBookDto)]` |
| `GetByIdAsync(Guid id)` | `Id = "endpoint:compat.appservice.book.get-by-id"`, `HttpMethod = Get`, `RoutePattern = "/api/books/{id}"`, `InputBindings = [Route(Guid id)]` |
| `DeleteAsync(Guid id)` | `Id = "endpoint:compat.appservice.book.delete"`, `HttpMethod = Delete`, `RoutePattern = "/api/books/{id}"`, `InputBindings = [Route(Guid id)]` |

权限推导：`{ServiceName}.Create`、`.Update`、`.Delete`、`.Get`、`.Search`——与 `DynamicApiAotSourceGenerator.ResolvePermission` 一致。

### 4.2 产物 1：`GeneratedAppServiceCompatibilityCapabilities.g.cs`

生成 `IDescriptorProvider<CapabilityDescriptor>` 实现，为每个 AppService 方法生成一个 `CapabilityDescriptor`。

```csharp
new CapabilityDescriptor
{
    Namespace = "capability",
    Id = "compat.appservice.book.create",
    Name = "Create",
    Kind = DescriptorKind.Capability,
    State = DescriptorState.Active,
    Version = 1,
    CapabilityKind = CapabilityKind.Command,  // POST/PUT/DELETE/PATCH → Command; GET → Query
    Permissions = new[] { "Books.Create" },   // 从约定推导
    RiskLevel = CapabilityRiskLevel.Medium,
    InputSchema = null,   // 8d 初始版本不填
    OutputSchema = null,  // 8d 初始版本不填
    ProjectionKind = CapabilityProjectionKind.AppServiceCompatibility,  // 新增属性
}
```

注册通过 `[ModuleInitializer]` → `DescriptorProviderRegistry.Register<CapabilityDescriptor>(provider)`。

### 4.3 产物 2：`GeneratedAppServiceCompatibilityEndpoints.g.cs`

生成 `ICapabilityEndpointDescriptorProvider` 实现，为每个 AppService 方法生成一个 `CapabilityEndpointDescriptor`。

```csharp
new CapabilityEndpointDescriptor
{
    Namespace = "dynamic-api-endpoint",
    Kind = DescriptorKind.DynamicApiEndpoint,
    Id = "endpoint:compat.appservice.book.create",
    Name = "Create",
    Version = 1,
    State = DescriptorState.Active,
    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("compat.appservice.book.create", 1),
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
    OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 201 },
    Projection = new CapabilityEndpointProjectionMetadata
    {
        OperationId = "compat_appservice_book_create",
        Tags = new[] { "Book" }
    }
}
```

注册通过 `[ModuleInitializer]` → `DescriptorProviderRegistry.Register<CapabilityEndpointDescriptor>(provider)`。

### 4.4 产物 3：`GeneratedAppServiceCompatibilityBindings.g.cs`

生成 `BindInputAsync` 委托，注册到 `CapabilityEndpointBindingRegistry`。

**单参数方法**——直接用 DTO 作为 input，无需 envelope：

```csharp
// POST /api/books — Body → CreateBookDto (AOT debt: generic ReadBodyAsync<T>)
private static async ValueTask<object?> BindCompatAppServiceBookCreateAsync(
    HttpContext context, CancellationToken ct)
{
    return await CapabilityEndpointJsonRuntime
        .ReadBodyAsync<CreateBookDto>(context, optional: false, ct);
}
```

**多参数方法**——生成 per-action input envelope：

```csharp
// PUT /api/books/{id} — Route(Guid id) + Body(UpdateBookDto) (AOT debt: generic ReadBodyAsync<T>)
internal sealed class BookAppService_Update_CompatibilityInput
{
    public Guid Id { get; init; }
    public UpdateBookDto Input { get; init; } = null!;
}

private static async ValueTask<object?> BindCompatAppServiceBookUpdateAsync(
    HttpContext context, CancellationToken ct)
{
    var id = Guid.Parse(context.Request.RouteValues["id"]!.ToString()!);
    var input = await CapabilityEndpointJsonRuntime
        .ReadBodyAsync<UpdateBookDto>(context, optional: false, ct);
    return new BookAppService_Update_CompatibilityInput { Id = id, Input = input };
}
```

注册通过 `[ModuleInitializer]` → `CapabilityEndpointBindingRegistry.Register(contract)`。

**AOT-safe body binding：** 8d 初始版本使用泛型 `ReadBodyAsync<T>(context, optional: false, ct)` overload，标注 AOT debt。理由：Roslyn SGs 无法在同一编译轮中看到彼此的 `RegisterSourceOutput` 输出，因此 `AppServiceCompatibilityGenerator` 生成的 `[JsonSerializable]`-decorated partial class 不会被 STJ generator 处理，导致 `JsonSerializerContext.Default` 和 `GetTypeInfo()` 无法生成（CS0534）。与 8a 现状一致。当 STJ 提供跨 SG 可见性机制或 `RegisterPreCompilationSourceOutput` 稳定后，可恢复 `JsonTypeInfo<T>` 路径。

### 4.5 产物 4：`GeneratedAppServiceCompatibilityInvokers.g.cs`

生成 `ICapabilityContextAwareHandlerInvoker` 实现，内部通过 DI 解析原始 AppService 并调用方法。

**单参数方法：**

```csharp
internal sealed class BookAppService_Create_CompatibilityInvoker
    : ICapabilityContextAwareHandlerInvoker
{
    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        var service = context.ServiceProvider.GetRequiredService<IBookAppService>();
        var typedInput = (CreateBookDto)context.Input!;
        var result = await service.CreateAsync(typedInput, ct).ConfigureAwait(false);
        return result;
    }

    public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        => throw new InvalidOperationException(
            "Compatibility invoker requires CapabilityExecutionContext. " +
            "Use the ICapabilityContextAwareHandlerInvoker overload.");
}
```

**多参数方法（使用 envelope）：**

```csharp
internal sealed class BookAppService_Update_CompatibilityInvoker
    : ICapabilityContextAwareHandlerInvoker
{
    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        var service = context.ServiceProvider.GetRequiredService<IBookAppService>();
        var envelope = (BookAppService_Update_CompatibilityInput)context.Input!;
        var result = await service.UpdateAsync(envelope.Id, envelope.Input, ct).ConfigureAwait(false);
        return result;
    }

    public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        => throw new InvalidOperationException(
            "Compatibility invoker requires CapabilityExecutionContext. " +
            "Use the ICapabilityContextAwareHandlerInvoker overload.");
}
```

注册通过 `[ModuleInitializer]` → `CapabilityHandlerResolverProvider.Register("compat.appservice.book.create", new BookAppService_Create_CompatibilityInvoker())`。

**关键设计决策：为什么用 `ICapabilityContextAwareHandlerInvoker`？**

代码库已有 `ICapabilityContextAwareHandlerInvoker` 接口（`CrestCreates.Capability.Abstractions`），Pipeline 已有 `is ICapabilityContextAwareHandlerInvoker` 分支（`CapabilityPipeline.cs:85`）。AppService 是 scoped service，必须通过 DI 解析。`CapabilityExecutionContext.ServiceProvider`（新增属性）提供 scoped `IServiceProvider`。

不新增 `IServiceProviderAwareInvoker` 接口——复用已有接口，避免重复发明。

### 4.6 产物 5：`GeneratedAppServiceCompatibilityManifest.g.cs`

Projection manifest，记录每个 projected action 的完整映射关系。

```csharp
internal sealed class GeneratedAppServiceCompatibilityManifest
{
    public static readonly IReadOnlyList<AppServiceCompatibilityProjectionEntry> Entries = new[]
    {
        new AppServiceCompatibilityProjectionEntry
        {
            SourceService = "BookAppService",
            SourceMethod = "CreateAsync",
            CapabilityId = "compat.appservice.book.create",
            EndpointId = "endpoint:compat.appservice.book.create",
            HttpMethod = "POST",
            RoutePattern = "/api/books",
            PermissionNames = new[] { "Books.Create" },
            InvokerTypeName = "BookAppService_Create_CompatibilityInvoker",
            ProjectionKind = CapabilityProjectionKind.AppServiceCompatibility,
        },
        // ... per action
    };
}
```

`AppServiceCompatibilityProjectionEntry` 定义在 `CrestCreates.DynamicApi.Abstractions`：

```csharp
public sealed record AppServiceCompatibilityProjectionEntry
{
    public string SourceService { get; init; } = string.Empty;
    public string SourceMethod { get; init; } = string.Empty;
    public string CapabilityId { get; init; } = string.Empty;
    public string EndpointId { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = string.Empty;
    public string RoutePattern { get; init; } = string.Empty;
    public IReadOnlyList<string> PermissionNames { get; init; } = Array.Empty<string>();
    public string InvokerTypeName { get; init; } = string.Empty;
    public CapabilityProjectionKind ProjectionKind { get; init; }
}
```

---

## 5. 现有组件变更

### 5.1 `CapabilityHandlerResolverProvider` — 改为 additive registration

当前 `CapabilityHandlerResolverProvider` 使用 `SetResolver`/`GetResolver` 替换式 API。`HandlerInvokerSourceGenerator` 生成 `new CapabilityHandlerResolver()` → 注册 → `SetResolver(resolver)`，8d 如果也生成新 resolver 再 `SetResolver`，后执行的 `ModuleInitializer` 会覆盖前者。

**修正为 additive registration：**

```csharp
// src/Runtime/Capability/CrestCreates.Capability/CapabilityHandlerResolverProvider.cs
public static class CapabilityHandlerResolverProvider
{
    private static readonly CapabilityHandlerResolver Resolver = new();

    public static void Register(string capabilityId, ICapabilityHandlerInvoker invoker)
        => Resolver.Register(capabilityId, invoker);

    public static ICapabilityHandlerResolver GetResolver() => Resolver;

    [Obsolete("Use Register() for additive registration.")]
    public static void SetResolver(ICapabilityHandlerResolver resolver)
    {
        // Compatibility no-op.
        // Old generated code will be replaced in the same phase.
    }
}
```

**`HandlerInvokerSourceGenerator` 修正：** 改为 additive registration：

```csharp
// 旧代码：
// var resolver = new CapabilityHandlerResolver();
// resolver.Register("xxx", new XxxInvoker());
// CapabilityHandlerResolverProvider.SetResolver(resolver);

// 新代码：
CapabilityHandlerResolverProvider.Register("xxx", new XxxInvoker());
```

**8d `AppServiceCompatibilityGenerator` 同样使用 additive registration：**

```csharp
CapabilityHandlerResolverProvider.Register("compat.appservice.book.create", new BookAppService_Create_CompatibilityInvoker());
```

### 5.2 `CapabilityExecutionContext` — 新增 `ServiceProvider` 属性

```csharp
// src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs
public required IServiceProvider ServiceProvider { get; init; };
```

`ServiceProvider` must be the current DI scope's provider. For HTTP dispatch, this is `HttpContext.RequestServices` through scoped `ICapabilityDispatcher`/`ICapabilityPipeline` resolution. It must not be root provider.

### 5.3 `CapabilityPipeline` — 构建 context 时赋值

```csharp
// src/Runtime/Capability/CrestCreates.Capability/CapabilityPipeline.cs
// 在构建 CapabilityExecutionContext 时增加：
ServiceProvider = _serviceProvider,
```

Pipeline 构造函数已有 `IServiceProvider serviceProvider` 参数（存储为 `_serviceProvider` 字段），无需修改构造函数。Pipeline 是 scoped 注册，`_serviceProvider` 即当前请求 scope。

### 5.3 `CapabilityDescriptor` — 新增 `ProjectionKind` 属性

```csharp
// src/Runtime/Capability/CrestCreates.Capability.Abstractions/CapabilityDescriptor.cs
public CapabilityProjectionKind ProjectionKind { get; init; } = CapabilityProjectionKind.Native;
```

**Canonical hash profile 同步：**

`ProjectionKind` 是来源/治理元数据，不是 capability runtime contract。标为 `DefinitionOnly`，不进 ContractHash：

```csharp
// src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityDescriptorCanonicalHashProfile.cs
// 新增字段声明：
[CanonicalHashField(
    nameof(CapabilityDescriptor.ProjectionKind),
    CanonicalHashFieldClassification.DefinitionOnly,
    Order = 100)]
```

理由：进 ContractHash 会让"兼容投影 → native 替代"的治理状态变化看起来像业务 contract breaking change。

### 5.4 `CapabilityProjectionKind` 枚举

```csharp
// src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorCapability/CapabilityProjectionKind.cs
namespace CrestCreates.Metadata.Abstractions.DescriptorCapability;

/// <summary>
/// Marks the origin of a CapabilityDescriptor.
/// Compatibility projections are migration artifacts with an exit path to native capabilities.
/// </summary>
public enum CapabilityProjectionKind
{
    Native = 0,                    // 手写/设计的原生 capability
    AppServiceCompatibility = 1,   // 从 AppService 自动投影
}
```

### 5.5 `DynamicApiAotSourceGenerator` — 两处变更

1. **增加 `[CapabilityCompatibilityProjection]` 排除**：`IsDynamicApiImplementation` 检查类是否有此 attribute，有则返回 false。
2. **改为调用 `DynamicApiConventionAnalyzer`**：把 `private static` 约定推导方法移到 `DynamicApiConventionAnalyzer`，generator 改为调用 `DynamicApiConventionAnalyzer.ResolveHttpMethod(...)` 等。

### 5.6 `DynamicApiConventionAnalyzer` — 新类

```csharp
// src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiConventionAnalyzer.cs
namespace CrestCreates.CodeGenerator.DynamicApiGenerator;

/// <summary>
/// Shared convention derivation logic for DynamicApi and AppServiceCompatibility generators.
/// Methods extracted from DynamicApiAotSourceGenerator — signatures and bodies unchanged,
/// only visibility changed from private static to internal static.
/// </summary>
internal static class DynamicApiConventionAnalyzer
{
    internal static string ResolveHttpMethod(string methodName) { ... }
    internal static string ResolveActionRoute(IMethodSymbol methodSymbol) { ... }
    internal static string ResolvePermission(string serviceName, string methodName) { ... }
    internal static ServiceRouteModel ResolveServiceRoute(INamedTypeSymbol serviceType, string serviceName, INamedTypeSymbol? dynamicApiRouteAttribute) { ... }
    internal static string TrimServiceName(string serviceTypeName) { ... }
    internal static string TrimAsyncSuffix(string methodName) { ... }
    internal static string ToKebabCase(string value) { ... }
    internal static ParameterSource ResolveParameterSource(IParameterSymbol parameter, ISet<string> routeTokens, string httpMethod, ref bool bodyAssigned) { ... }
    internal static bool IsScalar(ITypeSymbol typeSymbol) { ... }
    // BuildServiceModels / BuildActionModels 保留在 DynamicApiAotSourceGenerator（编排方法，非纯约定逻辑）
}
```

---

## 6. SG 内部结构

```text
src/Tooling/CrestCreates.CodeGenerator/
├── DynamicApiGenerator/
│   ├── DynamicApiAotSourceGenerator.cs        // 修改：调用 DynamicApiConventionAnalyzer + 排除 [CapabilityCompatibilityProjection]
│   └── DynamicApiConventionAnalyzer.cs        // 新增：从 DynamicApiAotSourceGenerator 提取的约定推导方法
└── AppServiceCompatibilityGenerator/          // 新增目录
    ├── AppServiceCompatibilityGenerator.cs    // IIncrementalGenerator 入口
    ├── AppServiceCompatibilityModels.cs       // 内部 model（CompatibilityServiceModel、CompatibilityActionModel、CompatibilityInputEnvelope）
    ├── AppServiceCompatibilityCapabilityEmitter.cs   // CapabilityDescriptor provider 生成
    ├── AppServiceCompatibilityEndpointEmitter.cs     // CapabilityEndpointDescriptor provider + bindings 生成
    ├── AppServiceCompatibilityHandlerEmitter.cs      // ICapabilityContextAwareHandlerInvoker 生成
    ├── AppServiceCompatibilityManifestEmitter.cs     // Manifest 生成
    └── AppServiceCompatibilityDiagnostics.cs         // CEP030-CEP033
```

---

## 7. DI 注册

```csharp
// CrestCreates.DynamicApi
public static IServiceCollection AddCrestCompatibilityProjection(
    this IServiceCollection services)
{
    // 注册 Capability Endpoint 基础设施。
    // 注意：调用者需自行注册 Capability Runtime（services.AddCapabilityRuntime()），
    // 因为 DynamicApi 不能引用 Capability 实现程序集（boundary constraint）。
    // Compatibility handler invokers 通过 generated [ModuleInitializer] 自动注册。
    services.AddCrestCapabilityEndpoints();
    return services;
}
```

Compatibility handler invokers 通过 generated `[ModuleInitializer]` 自动注册到 `CapabilityHandlerResolver`，无需手动注册。

---

## 8. 事务处理

8d 初始方案：compatibility invoker 不引入额外事务包装。

理由：
- AppService 通过 DI 解析为 scoped 实例
- 如果 AppService 使用 `[UnitOfWorkMo]` AOP 拦截器，拦截器在 proxy 上自动生效
- 如果 AppService 没有使用 AOP 事务，legacy 路径的 `DynamicApiGeneratedRuntime.ExecuteAsync` 也只在 `requiresTransaction=true` 时包装事务
- 8d 不引入 `TransactionMiddleware`——这是 pipeline 层的扩展，scope 更大，应作为独立 phase

`AppServiceCompatibilityProjectionEntry` 不记录 `RequiresTransaction` 字段。后续 `TransactionMiddleware` 加入时再扩展 manifest。

---

## 9. CapabilityDescriptor 的 InputSchema/OutputSchema

8d 初始版本不填（null）。

理由：
- Schema 生成是 `SchemaCapabilitySourceGenerator` 的职责
- 8d 的 CapabilityDescriptor 是 compatibility 产物，不是正式 schema-first 设计
- Pipeline 的 `ValidationMiddleware` 在 schema 为 null 时跳过 input validation（与现有行为一致）
- 后续可以通过 `[CapabilityInputSchema]` / `[CapabilityOutputSchema]` attribute 补充

---

## 10. 边界约束

| 约束 | 说明 |
|---|---|
| 单向 | AppService → Capability，不反向 |
| 不引用 legacy registry | 生成的代码不引用 `DynamicApiGeneratedRegistryStore`、`IDynamicApiGeneratedProvider` |
| 不引用 legacy runtime | 生成的代码不引用 `DynamicApiGeneratedRuntime`（事务由 AOP 或后续 middleware 处理） |
| CapabilityEndpoint 映射路径不变 | `MapCrestCapabilityEndpoints()` 不感知 compatibility vs native，统一处理 |
| Legacy 路径不受影响 | `MapCrestDynamicApi()` 继续工作，但 opt-in service 不再被 legacy generator 生成 |
| 不交叉 | Compatibility endpoint 不走 `MapCrestDynamicApi()`，legacy endpoint 不走 `MapCrestCapabilityEndpoints()` |
| 推导一致 | `DynamicApiConventionAnalyzer` 保证 compatibility 与 legacy 推导出相同的 route/method/permissions |

---

## 11. 诊断

| Code | Severity | 规则 |
|---|---|---|
| CEP030 | Error | `[CapabilityCompatibilityProjection]` 只能用于 `[CrestService]` 类，或用于 `[CrestService]` 类声明的方法 |
| CEP031 | Error | `[CapabilityCompatibilityProjection]` 与 `[DynamicApiIgnore]` 冲突 |
| CEP032 | Warning | AppService 方法无法推导 HTTP method（方法名不匹配约定） |
| CEP033 | Warning | AppService 方法无法推导权限名 |

---

## 12. 不修改的组件

CapabilityEndpoint 映射层：
- `CapabilityEndpointDescriptor` — 不变（`ProjectionKind` 加在 CapabilityDescriptor 上，不是 EndpointDescriptor`）
- `CapabilityEndpointBindingContract/Registry` — 不变
- `MapCrestCapabilityEndpoints()` — 不变
- `ICapabilityDispatcher` — 不变
- `CapabilityEndpointResultMapper` — 不变

Legacy 层：
- `DynamicApiAotSourceGenerator` — 生成代码不变（只改调用路径、排除条件、model 可见性）
- `MapCrestDynamicApi()` — 不变（legacy fallback）
- `DynamicApiGeneratedRuntime` — 不变
- `DynamicApiGeneratedRegistryStore` — 不变

需要修改的现有 SG：
- `HandlerInvokerSourceGenerator` — 改为 `CapabilityHandlerResolverProvider.Register()` additive registration（不再 `new CapabilityHandlerResolver()` + `SetResolver()`）

---

## 13. 退出路径

`CapabilityProjectionKind.AppServiceCompatibility` 标记 projected descriptor 为迁移产物。当开发者手写 native Capability 替代 compatibility projection 时：

1. 新建 native `CapabilityDescriptor`（`ProjectionKind = Native`）
2. Compatibility `CapabilityDescriptor` 的 `SupersededById` 指向 native descriptor
3. Compatibility descriptor `State` 变为 `Deprecated`
4. 未来 tooling 可以从 manifest 读取 `ProjectionKind = AppServiceCompatibility` 的条目，建议或自动生成 native replacement

基础设施复用：`SupersededById` + `DescriptorState.Deprecated` + `CapabilityRelationshipExtractor` 已完备。

---

## 14. 实现步骤

| Step | 内容 | 依赖 | 涉及项目 |
|---|---|---|---|
| 1 | `CapabilityHandlerResolverProvider` 重构为 additive registration（`Register` + 保留 `SetResolver` 为 obsolete） | 无 | Capability |
| 2 | `HandlerInvokerSourceGenerator` 改为 `CapabilityHandlerResolverProvider.Register()` additive registration | Step 1 | CodeGenerator |
| 3 | `CapabilityExecutionContext.ServiceProvider` 属性 | 无 | Capability.Abstractions |
| 4 | `CapabilityPipeline` 构建 context 时赋值 `ServiceProvider` | Step 3 | Capability |
| 5 | `[CapabilityCompatibilityProjection]` + `[CapabilityCompatibilityIgnore]` attribute 定义 | 无 | DynamicApi.Abstractions |
| 6 | `AppServiceCompatibilityProjectionEntry` record + `CapabilityProjectionKind` enum 定义 | 无 | DynamicApi.Abstractions + Metadata.Abstractions |
| 7 | `CapabilityDescriptor.ProjectionKind` 属性 + canonical hash profile 同步（DefinitionOnly） | Step 6 | Capability.Abstractions + Metadata |
| 8 | `DynamicApiConventionAnalyzer` — 从 DynamicApiAotSourceGenerator 提取约定推导方法 + model 类型为 `internal` | 无 | CodeGenerator |
| 9 | `DynamicApiAotSourceGenerator` — class-level `[CapabilityCompatibilityProjection]` 排除 + method-level 排除 + 改为调用 `DynamicApiConventionAnalyzer` | Step 5, 8 | CodeGenerator |
| 10 | `AppServiceCompatibilityGenerator` — CapabilityDescriptor provider 生成 | Step 5, 6, 7, 8 | CodeGenerator |
| 11 | `AppServiceCompatibilityGenerator` — CapabilityEndpointDescriptor provider + bindings 生成（含 per-action envelope） | Step 5, 6, 7, 8 | CodeGenerator |
| 12 | `AppServiceCompatibilityGenerator` — `ICapabilityContextAwareHandlerInvoker` 生成 + additive 注册 | Step 1, 3, 5, 6, 7, 8 | CodeGenerator |
| 13 | `AppServiceCompatibilityGenerator` — Manifest 生成 | Step 5, 6 | CodeGenerator |
| 14 | `AddCrestCompatibilityProjection()` DI 扩展方法 | Step 1-7 | DynamicApi |
| 15 | 诊断 CEP030-CEP033 | Step 5 | CodeGenerator |
| 16 | SG 测试 | Step 10-13, 15 | CodeGenerator.Tests |
| 17 | 功能测试（HTTP → binding → dispatcher → pipeline → compatibility handler → service method） | Step 14 | DynamicApi.Tests, Web.Tests |
| 18 | 边界测试 | Step 10-14 | DependencyBoundaries.Tests |

Step 1, 3, 5, 6, 7, 8 无依赖，可并行。Step 2 依赖 Step 1。Step 9 依赖 Step 5, 8。Step 10-13 依赖 Step 1, 3, 5, 6, 7, 8。Step 14 依赖 Step 1-7。

---

## 15. Exit Criteria

8d 完成当：

1. 至少一个 `[CrestService]` AppService 端点通过 Capability-first 路径暴露
2. 完整链路：HTTP request → binding → ICapabilityDispatcher → CapabilityPipeline → compatibility handler → service method → result
3. 外部 HTTP contract 不变（route、method、request/response DTO）
4. 内部运行时路径走 Capability Pipeline（authorization、validation、audit、tenant middleware 均执行）
5. 权限通过 `CapabilityDescriptor.Permissions` → Pipeline `AuthorizationMiddleware` 执行
6. Legacy `MapCrestDynamicApi()` 仍可用，但 opt-in service 不再被 legacy generator 生成
7. 生成的代码不引用 `DynamicApiGeneratedRegistryStore` / `IDynamicApiGeneratedProvider` / `DynamicApiGeneratedRuntime`
8. Boundary test 验证 compatibility 路径不交叉 legacy 映射路径
9. `ICapabilityContextAwareHandlerInvoker` 分支正确执行，`context.ServiceProvider` 可用
10. 现有 `ICapabilityHandlerInvoker` 实现（`DelegateHandlerInvoker`、SG 生成的 invoker）无需修改
11. 多参数方法使用 per-action input envelope，不假设 DTO 有可写属性
12. Compatibility capability ID 使用 `compat.appservice.` 前缀，与 native capability 隔离
13. Projection manifest 生成，记录完整映射关系
14. Method-level `[CapabilityCompatibilityIgnore]` 正确排除方法
15. `CapabilityProjectionKind.AppServiceCompatibility` 标记 projected descriptor
16. `DynamicApiConventionAnalyzer` 与 `DynamicApiAotSourceGenerator` 使用同一组推导方法，生成代码不变
17. 集成测试证明 compatibility invoker 从当前请求 scope 解析 AppService，AppService 的 scoped 依赖（tenant context、current user、UoW）与 HTTP 请求共享同一 scope。不允许从 root provider 解析或 `new` 实例化
18. `dotnet build` + `dotnet test` 全部通过
19. CEP030-CEP033 诊断正确触发
20. Native generated handlers 与 appservice compatibility handlers 可同时存在，互不覆盖（`CapabilityHandlerResolverProvider` additive registration）
21. Method-level `[CapabilityCompatibilityProjection]` 正确抑制 legacy generator 对该方法的生成，不产生重复 endpoint
22. `CapabilityDescriptor.ProjectionKind` canonical hash profile 标为 `DefinitionOnly`，不进 ContractHash

---

## 16. 不做的事

- ❌ 不删除 legacy DynamicApi generator
- ❌ 不强制所有 AppService 迁移
- ❌ 不引入 `TransactionMiddleware`（后续 phase）
- ❌ 不生成 `DynamicApiEndpointDescriptor`
- ❌ 不让 `CapabilityEndpointDescriptor` 依赖 `DynamicApiEndpointDescriptor`
- ❌ 不合并 `MapCrestDynamicApi()` 和 `MapCrestCapabilityEndpoints()`
- ❌ 不做 MCP / Agent tool projection
- ❌ 不做 CapabilityDescriptor InputSchema/OutputSchema 生成
- ❌ 不修改 `ICapabilityHandlerInvoker` 现有签名（复用 `ICapabilityContextAwareHandlerInvoker`）
- ❌ 不提取共享 `AppServiceAnalysisModel`（8d 用 `internal static` 方法共享，后续 phase 再考虑）
- ❌ 不引入 Migration Modes（ReportOnly/GenerateOnly/RuntimeEnabled）——opt-in 本身就是最简 migration control
