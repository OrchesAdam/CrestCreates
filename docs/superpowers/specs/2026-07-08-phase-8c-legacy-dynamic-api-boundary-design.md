# Phase 8c — Legacy Dynamic API Boundary Design

**Date**: 2026-07-08
**Status**: Draft
**Parent Issue**: #21

---

## 1. 定位

8c 做一件事：**让旧 DynamicApi 路径的身份从"默认主线"降级为"compatibility-only"，同时修正 8a 遗留的小债务。**

8c 不是迁移工程。不迁移 sample，不删除 legacy runtime 组件，不做 AppService→Capability 兼容生成。

核心价值：**解决双轨身份不清，而非消灭双轨物理共存。**

```text
8c 前：
  DynamicApiAotSourceGenerator — 事实主线，无降级标注
  CapabilityEndpointGenerator — 新主线，无边界测试保护
  双轨身份模糊，后续 Agent 可能误扩展旧路径

8c 后：
  DynamicApiAotSourceGenerator — 明确标注 legacy compatibility-only
  CapabilityEndpointGenerator — 唯一主线，边界测试钉死
  旧路径被冻结，新路径债务修正
```

## 2. 交付物

| # | 交付物 | 类型 | 优先级 |
|---|--------|------|--------|
| 1 | Legacy boundary documentation | XML docs + architecture note | P1 |
| 2 | Boundary tests | 测试 | P0 |
| 3 | EndpointId / EndpointVersion 独立属性 | SG + Attribute 变更 | P0 |
| 4 | TargetProperty 分离 | SG 变更 | P0 |
| 5 | CEP013 Warning → Error + 删除 Dictionary fallback | SG 变更 | P0 |
| 6 | VersionSelectionMode.Latest / LatestActive 命名统一 | 文档/注释 | P2 |
| 7 | DynamicApiSourceGenerator 回收 | 清理 | P1 |
| 8 | Legacy test 重命名/移动 | 测试重组 | P1 |

## 3. 不做的事

- ❌ 不迁移 sample 到 CapabilityEndpointSpec
- ❌ 不删除 `DynamicApiGeneratedRuntime` / `DynamicApiGeneratedRegistryStore` / `IDynamicApiGeneratedProvider`
- ❌ 不删除 `MapCrestDynamicApi` / `AddCrestDynamicApi`
- ❌ 不给 `MapCrestDynamicApi` / `AddCrestDynamicApi` 加编译级 `[Obsolete]`
- ❌ 不做 CEP015 JsonTypeInfo AOT body binding
- ❌ 不做 AppService→Capability compatibility generator（8d）
- ❌ 不做 MCP / Agent / activation / topology projection 到旧路径
- ❌ 不合并两条 DI 注册路径

## 4. Legacy Boundary Documentation

### 4.1 XML Docs 标注

对以下公共 API 添加 legacy 标注。

**跨 assembly 引用注意**：当 `<see cref>` 指向的类型不在同一 assembly 时，使用纯文本描述而非 `<see cref>`，避免 XML doc 编译 warning。

**边界测试兼容注意**：Legacy XML docs **不得**出现边界测试扫描的 CapabilityEndpoint concrete symbol names（如 `MapCrestCapabilityEndpoints`、`CapabilityEndpointMapper`、`CapabilityEndpointBindingRegistry`、`ICapabilityDispatcher`）。使用概念描述代替，否则 Section 5.3/5.5 的源码符号测试会失败。

**`DynamicApiExtensions.cs`：**

```csharp
/// <summary>
/// Legacy AppService-oriented HTTP exposure path.
/// This API is kept for AppService compatibility.
/// New HTTP exposure should use the Capability-first endpoint projection path.
/// Do not extend this path with Capability runtime, topology, activation,
/// agent authoring, or MCP projection semantics.
/// </summary>
public static IServiceCollection AddCrestDynamicApi(...)

/// <summary>
/// Legacy AppService-oriented HTTP endpoint mapping.
/// This API is kept for AppService compatibility.
/// New HTTP exposure should use the Capability-first endpoint projection path.
/// </summary>
public static IEndpointRouteBuilder MapCrestDynamicApi(...)
```

**`AspNetCoreModuleExtensions.cs`：**

```csharp
/// <summary>
/// Legacy wiring for AppService-oriented Dynamic API.
/// New modules should use the Capability-first endpoint projection wiring.
/// </summary>
public static IServiceCollection AddCrestAspNetCoreDynamicApi(...)

/// <summary>
/// Legacy wiring for AppService-oriented Dynamic API endpoint mapping.
/// </summary>
public static IEndpointRouteBuilder MapCrestAspNetCoreDynamicApi(...)
```

**`DynamicApiGeneratedRuntime.cs`：**

```csharp
/// <summary>
/// Legacy runtime helpers for AppService-oriented Dynamic API endpoints.
/// New Capability Endpoint projection uses its own endpoint JSON binding runtime.
/// </summary>
public static class DynamicApiGeneratedRuntime
```

**`DynamicApiGeneratedRegistryStore.cs`：**

```csharp
/// <summary>
/// Legacy static registry for AppService-oriented Dynamic API generated providers.
/// New Capability Endpoint projection uses its own generated binding registry.
/// </summary>
public static class DynamicApiGeneratedRegistryStore
```

**`IDynamicApiGeneratedProvider.cs`：**

```csharp
/// <summary>
/// Legacy provider interface for AppService-oriented Dynamic API.
/// New Capability Endpoint projection uses its own descriptor provider interface.
/// </summary>
public interface IDynamicApiGeneratedProvider
```

### 4.2 Architecture Note

正式架构规则写入本 spec 文档（即本文档 Section 4.2 以下内容）。`memory.md` 只在实现完成后记录结果摘要，不作为正式架构文档的唯一来源。

```text
## Dynamic API Dual-Path Status (Post-8c)

### Mainline: Capability Endpoint Projection
- SG: CapabilityEndpointGenerator
- Descriptor: CapabilityEndpointDescriptor (IDescriptor, IVersionedDescriptor)
- Registry: ICapabilityEndpointRegistry (RegistryBase<T>)
- Binding: CapabilityEndpointBindingContract + CapabilityEndpointBindingRegistry
- Mapping: MapCrestCapabilityEndpoints()
- Runtime: CapabilityEndpointJsonRuntime, CapabilityEndpointResultMapper
- Execution: ICapabilityDispatcher.DispatchAsync(CapabilityDescriptor, ...)

### Legacy Compatibility: AppService Dynamic API
- SG: DynamicApiAotSourceGenerator
- Metadata: DynamicApiEndpointDescriptor, DynamicApiServiceDescriptor, DynamicApiActionDescriptor
- Registry: DynamicApiGeneratedRegistryStore (static)
- Mapping: MapCrestDynamicApi()
- Runtime: DynamicApiGeneratedRuntime
- Execution: Direct service method invocation via generated delegate

### Boundary Rules
- Legacy path MUST NOT gain: topology, activation, agent authoring, MCP projection, capability governance
- CapabilityEndpointDescriptor MUST NOT be converted to DynamicApiEndpointDescriptor
- MapCrestCapabilityEndpoints MUST NOT wrap MapCrestDynamicApi
- MapCrestDynamicApi MUST NOT wrap CapabilityDispatcher
- Legacy path continues to run for AppService compatibility
- Legacy tests continue to prove compatibility, but are not mainline development targets
```

## 5. Boundary Tests

### 5.1 Assembly / Project Reference 边界（核心边界测试）

```csharp
// tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DynamicApiBoundaryTests.cs

[Fact]
public void DynamicApi_Abstractions_DoesNotReference_DynamicApi_Implementation()
{
    // Assembly reference boundary: Abstractions must not depend on implementation
    var refs = typeof(CapabilityEndpointDescriptor).Assembly
        .GetReferencedAssemblies()
        .Select(x => x.Name)
        .ToArray();

    refs.Should().NotContain("CrestCreates.DynamicApi",
        "DynamicApi.Abstractions must not reference the DynamicApi implementation assembly");
}

[Fact]
public void DynamicApi_Abstractions_DoesNotReference_Legacy_Runtime_Types()
{
    // Smoke test: Abstractions assembly should not define or expose legacy runtime types
    var assembly = typeof(CapabilityEndpointDescriptor).Assembly;
    var typeNames = assembly.GetTypes().Select(t => t.FullName!);

    var forbiddenTypes = new[]
    {
        "CrestCreates.DynamicApi.DynamicApiGeneratedRegistryStore",
        "CrestCreates.DynamicApi.DynamicApiGeneratedRuntime",
        "CrestCreates.DynamicApi.IDynamicApiGeneratedProvider"
    };

    foreach (var forbiddenType in forbiddenTypes)
    {
        typeNames.Should().NotContain(forbiddenType,
            $"Abstractions assembly must not define legacy type {forbiddenType}");
    }
}

[Fact]
public void DynamicApi_Abstractions_Csproj_DoesNotReference_DynamicApi_Csproj()
{
    // Project file boundary: verify .csproj has no ProjectReference to implementation
    var csprojPath = Path.Combine(FindRepoRoot(),
        "src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CrestCreates.DynamicApi.Abstractions.csproj");
    var content = File.ReadAllText(csprojPath);
    content.Should().NotContain("CrestCreates.DynamicApi.csproj",
        "Abstractions project must not reference implementation project");
}
```

### 5.2 CapabilityEndpoint 映射路径不依赖 Legacy AppService 概念

```csharp
[Fact]
public void CapabilityEndpoint_Mapping_DoesNotReference_Legacy_AppService_Concepts()
{
    // Verify the CapabilityEndpoint mapping path (CapabilityEndpointExtensions,
    // CapabilityEndpointMapper, CapabilityEndpointBindingRegistry) does not
    // reference any of these legacy AppService DynamicApi types:
    // - DynamicApiGeneratedRegistryStore
    // - DynamicApiGeneratedRuntime
    // - IDynamicApiGeneratedProvider
    // - DynamicApiEndpointDescriptor
    // - DynamicApiServiceDescriptor
    // - DynamicApiActionDescriptor
    //
    // Implementation: read source files in the CapabilityEndpoint mapping path
    // and assert they do not contain these type names.
    var capabilityFiles = Directory.GetFiles(
        Path.Combine(FindRepoRoot(), "src/Framework/Api/CrestCreates.DynamicApi"),
        "CapabilityEndpoint*.cs",
        SearchOption.TopDirectoryOnly);

    var legacySymbols = new[] {
        "DynamicApiGeneratedRegistryStore",
        "DynamicApiGeneratedRuntime",
        "IDynamicApiGeneratedProvider",
        "DynamicApiEndpointDescriptor",
        "DynamicApiServiceDescriptor",
        "DynamicApiActionDescriptor"
    };

    foreach (var file in capabilityFiles)
    {
        var content = File.ReadAllText(file);
        foreach (var symbol in legacySymbols)
        {
            content.Should().NotContain(symbol,
                $"CapabilityEndpoint file {Path.GetFileName(file)} must not reference legacy symbol {symbol}");
        }
    }
}
```

### 5.3 Legacy 映射路径不依赖 Capability Runtime

```csharp
[Fact]
public void Legacy_DynamicApi_Mapping_DoesNotReference_Capability_Dispatcher()
{
    // Verify DynamicApiGeneratedRegistryStore and DynamicApiExtensions
    // do not reference ICapabilityDispatcher or CapabilityEndpointMapper
    var legacyFiles = new[] {
        "DynamicApiGeneratedRegistryStore.cs",
        "DynamicApiExtensions.cs"
    };
    var capabilitySymbols = new[] {
        "ICapabilityDispatcher",
        "CapabilityEndpointMapper",
        "CapabilityEndpointBindingRegistry"
    };

    foreach (var fileName in legacyFiles)
    {
        var path = Path.Combine(FindRepoRoot(),
            "src/Framework/Api/CrestCreates.DynamicApi", fileName);
        if (!File.Exists(path)) continue;
        var content = File.ReadAllText(path);
        foreach (var symbol in capabilitySymbols)
        {
            content.Should().NotContain(symbol,
                $"Legacy file {fileName} must not reference capability symbol {symbol}");
        }
    }
}
```

### 5.4 SG 产物不包含 Legacy 概念

```csharp
[Fact]
public void CapabilityEndpointGenerator_DoesNotEmit_ServiceType()
{
    // Verify generated Provider source does not contain "ServiceType"
    // This is a SG test — verify the generated output for a sample spec
}

[Fact]
public void CapabilityEndpointGenerator_DoesNotEmit_ActionName()
{
    // Verify generated Provider source does not contain "ActionName"
}

[Fact]
public void CapabilityEndpointGenerator_DoesNotEmit_DynamicApiEndpointDescriptor()
{
    // Verify generated source does not reference DynamicApiEndpointDescriptor
}
```

### 5.5 两条映射路径不交叉

```csharp
[Fact]
public void MapCrestCapabilityEndpoints_DoesNotCall_MapCrestDynamicApi()
{
    // Verify CapabilityEndpointExtensions source does not contain
    // "MapCrestDynamicApi" or "AddCrestDynamicApi"
    var path = Path.Combine(FindRepoRoot(),
        "src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointExtensions.cs");
    var content = File.ReadAllText(path);
    content.Should().NotContain("MapCrestDynamicApi");
    content.Should().NotContain("AddCrestDynamicApi");
}

[Fact]
public void MapCrestDynamicApi_DoesNotCall_CapabilityDispatcher()
{
    var files = new[]
    {
        "DynamicApiExtensions.cs",
        "DynamicApiGeneratedRegistryStore.cs",
        "DynamicApiGeneratedRuntime.cs"
    };

    var forbidden = new[]
    {
        "ICapabilityDispatcher",
        "CapabilityEndpointMapper",
        "MapCrestCapabilityEndpoints",
        "CapabilityEndpointBindingRegistry"
    };

    foreach (var file in files)
    {
        var path = Path.Combine(FindRepoRoot(),
            "src/Framework/Api/CrestCreates.DynamicApi", file);
        var content = File.ReadAllText(path);

        foreach (var symbol in forbidden)
        {
            content.Should().NotContain(symbol,
                $"Legacy file {file} must not reference capability symbol {symbol}");
        }
    }
}
```

## 6. EndpointId / EndpointVersion 独立属性

### 6.1 当前问题

`CapabilityEndpointSpecAttribute` 和 Level 2 attributes 没有 `EndpointId` / `EndpointVersion` 属性。SG 硬编码推导：

```csharp
// CapabilityEndpointProviderEmitter.cs:74-75
var endpointId = $"endpoint:{spec.CapabilityId}";
var version = spec.CapabilityVersion > 0 ? spec.CapabilityVersion : 1;
```

问题：
- 同一个 CapabilityId 不能暴露多个 endpoint（如 `/admin/books/{id}`、`/public/books/{id}`、`/internal/books/{id}` 需要不同 EndpointId）
- EndpointVersion 语义与 CapabilityVersion 耦合，无法独立演进

### 6.2 变更

**`CapabilityEndpointSpecAttribute` 新增 init-only 属性：**

```csharp
public sealed class CapabilityEndpointSpecAttribute : Attribute
{
    // ... existing constructor ...

    /// <summary>
    /// Override the generated endpoint identity.
    /// Default: "endpoint:" + CapabilityId.
    /// </summary>
    public string? EndpointId { get; init; }

    /// <summary>
    /// Override the endpoint version independently from CapabilityVersion.
    /// Default: CapabilityVersion if > 0, otherwise 1.
    /// </summary>
    public int EndpointVersion { get; init; }
}
```

**Level 2 attributes 同步新增：**

```csharp
// PostAttribute, GetAttribute, PutAttribute, DeleteAttribute, PatchAttribute
// 各新增：
public string? EndpointId { get; init; }
public int EndpointVersion { get; init; }
```

**SG 推导逻辑变更：**

```csharp
// CapabilityEndpointProviderEmitter.cs
var endpointId = !string.IsNullOrEmpty(spec.EndpointId)
    ? spec.EndpointId
    : $"endpoint:{spec.CapabilityId}";
var version = spec.EndpointVersion > 0
    ? spec.EndpointVersion
    : spec.CapabilityVersion > 0
        ? spec.CapabilityVersion
        : 1;
```

**BindingEmitter 同步变更** — 使用相同的推导逻辑。

**验证规则：**
- `EndpointId` 非空时：必须非空、不含空白字符、推荐使用 `endpoint:` 前缀
- `EndpointId` 为空时：SG 自动生成 `endpoint:{CapabilityId}`
- `EndpointVersion` 必须 >= 0（0 表示使用 CapabilityVersion 推导）
- `CapabilityEndpointDescriptorValidator` 新增 Id 格式验证：`Id` 必须非空且不含空白字符

**关于 `endpoint:` 前缀**：8c 不强制 `endpoint:` 前缀为 Error 级约束。SG 默认生成带前缀的 Id，但手写 `CapabilityEndpointDescriptor` 或显式 `EndpointId` 不强制前缀。理由：避免 SG 生成和手写 descriptor 的验证规则分叉。如果未来要强制前缀，应作为平台命名策略统一引入，而非 SG-only 规则。

CEP017 诊断调整为：

| Code | Severity | 规则 |
|------|----------|------|
| CEP017 | Error | `EndpointId` 非空时包含空白字符 |

## 7. TargetProperty 分离

### 7.1 当前问题

`CapabilityInputPath` 同时承担两个职责：
1. **Descriptor metadata**：描述 input binding 在 Capability input schema 中的路径（语义投影）
2. **CLR property assignment**：SG 生成的 binding 代码用它确定 body DTO 上的赋值目标属性

当 route token 名不是合法 C# 标识符时（如 `X-Request-Id`），`CapabilityInputPath` 被用来覆盖默认的 PascalCase 推导。但这把 CLR 细节泄漏进了 descriptor。

### 7.2 变更

**`CapabilityEndpointInputAttribute` 新增 `TargetProperty`：**

```csharp
public sealed class CapabilityEndpointInputAttribute : Attribute
{
    // ... existing ...

    /// <summary>
    /// Override the CLR property name on the body DTO for route/query/header value assignment.
    /// Default: PascalCase of the input Name.
    /// This property is SG-only — it does not appear in the generated descriptor.
    /// </summary>
    public string? TargetProperty { get; init; }
}
```

**`CapabilityEndpointInputRecord`（SG 内部模型）新增：**

```csharp
public string? TargetProperty { get; init; }
```

**SG 行为变更：**

- **BindingEmitter**：使用 `TargetProperty`（如果非空）作为 body DTO 属性赋值目标，fallback 到 PascalCase 推导
- **ProviderEmitter**：仍然只输出 `CapabilityInputPath`，不输出 `TargetProperty`
- `CapabilityInputPath` 回归纯语义职责：描述 input binding 在 Capability input schema 中的路径

**BindingEmitter 赋值逻辑变更：**

```csharp
// Before (8a): CapabilityInputPath used for both descriptor metadata and CLR property assignment
var propAssignmentName = !string.IsNullOrEmpty(input.CapabilityInputPath)
    ? input.CapabilityInputPath
    : !string.IsNullOrEmpty(sourceKey)
        ? char.ToUpperInvariant(sourceKey[0]) + sourceKey.Substring(1)
        : sourceKey;

// After (8c): TargetProperty for CLR assignment, CapabilityInputPath for descriptor metadata only
var propAssignmentName = !string.IsNullOrEmpty(input.TargetProperty)
    ? input.TargetProperty
    : !string.IsNullOrEmpty(sourceKey)
        ? char.ToUpperInvariant(sourceKey[0]) + sourceKey.Substring(1)
        : sourceKey;
```

注意：字段名是 `CapabilityInputPath`（不是 `CapabilityEndpointInputPath`）。Spec 之前版本误写为后者，已修正。

**`CapabilityEndpointInputBinding`（descriptor model）不变** — 仍只有 `CapabilityInputPath`，不加 `TargetProperty`。

### 7.3 验证

**TargetProperty 新增独立诊断**，不复用 CEP008：

| Code | Severity | 规则 |
|------|----------|------|
| CEP018 | Error | `TargetProperty` 非空时，body DTO 上不存在对应 public settable property |
| CEP019 | Error | `TargetProperty` 非空但不是合法 C# simple property access identifier |

**诊断边界**：
- CEP008：route/body convention 缺少属性，只用于默认 PascalCase(token) convention
- CEP018：显式 `TargetProperty` 指定的属性在 body DTO 上不存在
- CEP019：`TargetProperty` 本身不是合法的 simple property name

**8c 不支持 nested path**：`TargetProperty = "Address.City"` 不合法，CEP019 报错。只支持 simple property name（如 `TargetProperty = "CityId"`）。理由：nested path 需要生成 `model.Address.City = ...`，涉及 null chain 处理，会扩大 8c scope。

- `TargetProperty` 不进 descriptor，不进 `CapabilityEndpointInputBinding`

## 8. CEP013 Warning → Error + 删除 Dictionary Fallback

### 8.1 当前问题

CEP013 当前是 Warning。当 endpoint spec 有多个 route/query/header scalar input 但没有 Body 时，SG 生成 `Dictionary<string, object?>` binding：

```csharp
// CapabilityEndpointBindingEmitter.cs:258-277
var dict = new System.Collections.Generic.Dictionary<string, object?>();
dict["id"] = Guid.Parse(context.Request.RouteValues["id"]!.ToString()!);
dict["name"] = context.Request.RouteValues["name"]!.ToString()!;
return dict;
```

这破坏了 Capability 强类型主线——`ICapabilityDispatcher.DispatchAsync` 收到 `Dictionary<string, object?>` 而非强类型 TInput。

### 8.2 变更

**CEP013 severity 从 Warning 改为 Error：**

```csharp
public static readonly DiagnosticDescriptor MultipleRouteParamsWithoutBody = new(
    id: "CEP013",
    title: "Multiple scalar inputs without a body type",
    messageFormat: "Endpoint spec '{0}' declares {1} scalar inputs (Route/Query/Header) but no Body or Input type. " +
                   "Multi-scalar binding without a body type is not supported. " +
                   "Add a Body type that contains the scalar values as properties.",
    category: Category,
    defaultSeverity: DiagnosticSeverity.Error,  // Changed from Warning
    isEnabledByDefault: true);
```

**CEP013 适用范围**：

Level 1（`[CapabilityEndpointSpecs]` + class-level `[CapabilityEndpointInput]`）覆盖所有 scalar-only input source 组合：

```text
Route + Route without Body → CEP013 Error
Route + Query without Body → CEP013 Error
Query + Header without Body → CEP013 Error
Header + Header without Body → CEP013 Error
Route + Query + Header without Body → CEP013 Error
Single scalar Route/Query/Header → allowed (单标量 parse + return)
Body + multiple scalar splice → allowed (scalar 值赋值到 body DTO 属性)
```

Level 2（`[CapabilityEndpointSet]` + HTTP method attribute）只覆盖 route tokens 组合，因为 Level 2 不读取 class-level `[CapabilityEndpointInput]`：

```text
Route + Route without Body → CEP013 Error
Route + explicit Input on HTTP method attribute → CEP013 Error
Single route token → allowed
```

Level 2 的 Query/Header 输入应通过 HTTP method attribute 的 `Input` named arg 声明，不是 class-level `[CapabilityEndpointInput]`。

**SG 触发条件变更**：Level 1 从 `routeTokens.Length > 1` 改为 `allScalarInputs.Length > 1`，其中 `allScalarInputs` = route + query + header inputs 的并集。Level 2 只计数 route tokens + HTTP method attribute explicit Input。

**删除 `EmitScalarOnlyBinding` 中的 Dictionary fallback 代码：**

当 `bodyType is null && inputType is null && allInputs.Length > 1` 时，SG 不再生成 Dictionary binding 代码。multi-scalar 无 body 路径生成 fail-closed throw binding。CEP013 Error 阻止编译通过。

**保留单 scalar binding**：`allInputs.Length == 1` 且类型是 scalar 或 enum 时仍生成标量 parse + return，这是合法的（如 `GetById(Guid id)`）。Dictionary fallback 只在 `allInputs.Length > 1` 或单输入非 scalar/enum 时触发，8c 删除此路径。

**注意**：`EmitScalarOnlyBinding` 方法本身不删除，只删除 multi-scalar dictionary 分支。单 scalar/enum 分支保留。

**Emitter 硬约束**：即使 CEP013 diagnostic 被 build configuration suppression 关闭，emitter 也不能生成 `Dictionary<string, object?>` fallback。8c 后 emitter 中不得存在任何 `Dictionary<string, object?>` 分支。multi-scalar 无 body 路径的 emitter 行为改为：

```csharp
throw new InvalidOperationException(
    "CEP013: Multiple scalar inputs without a body/input DTO are not supported.");
```

这确保 fail-closed：即使 diagnostic 被绕过，runtime 也不会静默产生弱类型 Dictionary。

### 8.3 影响范围

- `CapabilityEndpointBindingEmitter.cs` — 删除 `EmitScalarOnlyBinding` 中 multi-scalar dictionary 路径
- `CapabilityEndpointDiagnostics.cs` — CEP013 severity 改 Error
- SG 测试 — 更新 CEP013 测试期望 Warning → Error，删除 dictionary binding 生成测试

## 9. VersionSelectionMode.Latest / LatestActive 命名统一

### 9.1 当前状态

`VersionSelectionMode` 枚举有 `Latest`（值 1），没有 `LatestActive`。

8a spec 和 runtime resolver 使用 "LatestActive" 语义描述，但枚举值是 `Latest`。Runtime `CapabilityEndpointCapabilityResolver` 在 `version <= 0` 时执行 "resolve latest active" 逻辑，不检查 `SelectionMode`。

### 9.2 变更

**不改枚举值**（`Latest` 保持不变，避免破坏已有生成代码）。

**文档/注释统一**：

- `VersionSelectionMode.Latest` 的 XML doc 更新为：`"Resolve the latest active version. Inactive/deprecated versions are skipped."`
- `CapabilityEndpointCapabilityResolver` 注释明确：`"Latest mode resolves to the latest active version at map-time."`
- 8a spec 中 "LatestActive" 统一为 "Latest (resolves to latest active version)"

这是纯文档级变更，不影响生成代码或 runtime 行为。

## 10. BindingRegistry 生命周期边界声明

### 9.5.1 当前状态

`CapabilityEndpointBindingRegistry` 是静态 process-wide generated registry，通过 `ModuleInitializer` 和 descriptor provider 注册。与 legacy `DynamicApiGeneratedRegistryStore` 风格一致，适合 AoT。

但全局状态问题未在 spec 中声明：重复注册、测试 reset、多 host 场景、插件加载等。

### 9.5.2 声明

```text
8a/8c binding registry 是 process-wide generated registry。
不支持 runtime unload / reload / hot projection。
动态重建推迟到后续 phase。

具体约束：
- 注册发生在 ModuleInitializer / generated provider 构造期间，process 生命周期只执行一次
- 测试场景使用独立 schema / test server，不依赖 registry reset
- 多 host 场景（如 SaaS 多租户独立 host）共享同一 process-wide registry
- 插件加载场景不支持运行时新增 endpoint projection
```

这是纯文档级声明，不改变 runtime 行为。Issue #21 Comment 2 第 5 项，定级 P2。

## 11. DynamicApiSourceGenerator 回收

### 11.1 当前状态

`DynamicApiSourceGenerator`（`src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiSourceGenerator.cs`，306 行）：
- 标记 `[Obsolete]`
- 零消费者（grep 确认无其他源文件引用）
- 零测试依赖
- 零构建配置引用

### 11.2 变更

**回收到 `99_RecycleBin/`，不是删除**（遵循 AGENTS.md 规则：不直接删除文件）。

```bash
mkdir -p 99_RecycleBin/Tooling/DynamicApiGenerator
git mv src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiSourceGenerator.cs \
       99_RecycleBin/Tooling/DynamicApiGenerator/DynamicApiSourceGenerator.cs
```

回收后验证：`dotnet build src/Tooling/CrestCreates.CodeGenerator` + `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests`。

## 12. Legacy Test 重命名/移动

### 12.1 当前状态

Web.Tests 下 9 个 DynamicApi 测试文件，其中部分测试 legacy path，部分测试 capability endpoint path。需要区分。

### 12.2 分类

**Legacy path 测试（重命名加 `Legacy` 前缀）：**

| 原文件 | 新文件 | 理由 |
|--------|--------|------|
| `DynamicApiExtensionsTests.cs` | `LegacyDynamicApiExtensionsTests.cs` | 测试 `AddCrestDynamicApi` / `MapCrestDynamicApi` |
| `GeneratedDynamicApiRuntimeTests.cs` | `LegacyGeneratedDynamicApiRuntimeTests.cs` | 测试 `DynamicApiGeneratedRuntime` |
| `DynamicApiEndpointConventionTests.cs` | `LegacyDynamicApiEndpointConventionTests.cs` | 测试 `IDynamicApiEndpointConvention` |
| `GeneratedApiControllerAbstractionsTests.cs` | `LegacyGeneratedApiControllerAbstractionsTests.cs` | 测试 `GeneratedApiControllerAttribute`、`ApiOverrideAttribute`、`CrestApiController` — legacy controller pipeline 类型 |

注意：`CrestApiController` 是抽象辅助基类（`Ok<T>()`、`NotFound()`、`NoContent()`），理论上可被 Minimal API handler 复用，但当前只有 legacy controller pipeline 使用它。8c 标注为 legacy，8d 后视情况决定是否保留。

**Capability endpoint 测试（保持不变）：**

| 文件 | 理由 |
|------|------|
| `CapabilityEndpointRegistryTests.cs` | 新主线 |
| `CapabilityEndpointDynamicApiModuleTests.cs` | 新主线 |
| `CapabilityEndpointDescriptorValidatorTests.cs` | 新主线 |
| `CapabilityEndpointDescriptorTests.cs` | 新主线 |
| `CapabilityEndpointRelationshipExtractorTests.cs` | 新主线 |

**CodeGenerator 测试目录：**

只移动 legacy generator 测试，不移动 CapabilityEndpointGenerator 测试：

| 移动 | 原路径 | 新路径 |
|------|--------|--------|
| ✅ | `DynamicApiGenerator/DynamicApiAotSourceGeneratorTests.cs` | `DynamicApiGenerator/Legacy/DynamicApiAotSourceGeneratorTests.cs` |
| ✅ | `DynamicApiGenerator/DynamicApiCrudMainlineTests.cs` | `DynamicApiGenerator/Legacy/DynamicApiCrudMainlineTests.cs` |
| ✅ | `DynamicApiGenerator/` 下的 legacy fixture/helper 文件 | `DynamicApiGenerator/Legacy/` |

| 不移动 | 路径 | 理由 |
|--------|------|------|
| ❌ | `CapabilityEndpointGenerator/` 下所有测试 | 新主线 |
| ❌ | `CapabilityEndpointDiagnosticTests.cs` | 新主线 |
| ❌ | `CapabilityEndpointSpecNormalizer` 相关测试 | 新主线 |

注意：`DynamicApiAotSourceGeneratorTests.cs` 和 `DynamicApiCrudMainlineTests.cs` 仍测试当前活跃的 `DynamicApiAotSourceGenerator`，但该 generator 在 8c 后被标注为 legacy compatibility-only。测试文件移到 `Legacy/` 子目录表示它们不是主线发展目标。

### 12.3 测试类内部标注

每个 legacy 测试类加 file-level 注释：

```csharp
// Legacy compatibility test — proves the AppService-oriented Dynamic API path still works.
// Do not extend these tests with Capability Endpoint, topology, activation, or governance semantics.
// New endpoint projection tests belong in CapabilityEndpoint* test classes.
```

## 13. 新增诊断汇总

| Code | Severity | 规则 | 来源 |
|------|----------|------|------|
| CEP013 | **Error** (was Warning) | Level 1: 多 scalar input（Route/Query/Header 任意组合）无 Body；Level 2: 多 route token 无 Body | 8c 升级 |
| CEP017 | Error | `EndpointId` 非空时包含空白字符 | 8c 新增 |
| CEP018 | Error | `TargetProperty` 非空时，body DTO 上不存在对应 public settable property | 8c 新增 |
| CEP019 | Error | `TargetProperty` 非空但不是合法 C# simple property name（不支持 nested path） | 8c 新增 |
| CEP020 | Error | `EndpointVersion` < 0 不合法 | 8c 新增 |

## 14. 实现步骤

| Step | 内容 | 依赖 | 涉及项目 |
|------|------|------|----------|
| 1 | Legacy boundary XML docs | 无 | DynamicApi, AspNetCore |
| 2 | Boundary tests | 无 | DependencyBoundaries.Tests, CodeGenerator.Tests |
| 3 | EndpointId / EndpointVersion 属性 + CEP017 | 无 | DynamicApi.Abstractions, CodeGenerator |
| 4 | TargetProperty 分离 + CEP018/CEP019 | 无 | DynamicApi.Abstractions, CodeGenerator |
| 5 | CEP013 Error + 删除 Dictionary fallback（Level 1 覆盖 Route/Query/Header；Level 2 覆盖 route tokens） | 无 | CodeGenerator |
| 6 | VersionSelectionMode 文档统一 | 无 | DynamicApi.Abstractions, DynamicApi |
| 7 | DynamicApiSourceGenerator 回收到 99_RecycleBin | 无 | CodeGenerator |
| 8 | Legacy test 重命名/移动（只移动 legacy generator tests） | 无 | Web.Tests, CodeGenerator.Tests |
| 9 | Architecture note 更新（docs，非 memory.md） | Step 1-8 | docs |

Step 1-6 无依赖，可并行。Step 7-8 可并行。Step 9 依赖全部完成。

## 15. Acceptance Criteria

8c 完成当：

1. Legacy path 公共 API 有 XML docs 标注 compatibility-only；XML docs 使用概念描述，不出现边界测试禁止的 CapabilityEndpoint concrete symbols（如 `MapCrestCapabilityEndpoints`、`CapabilityEndpointMapper`、`CapabilityEndpointBindingRegistry`、`ICapabilityDispatcher`）；无跨 assembly `<see cref>` warning
2. Boundary tests 证明 DynamicApi.Abstractions 不引用 DynamicApi 实现（assembly reference + .csproj 双重验证）
3. Boundary tests 证明 CapabilityEndpoint 映射路径不依赖 legacy AppService 概念（源码级符号检查）
4. Boundary tests 证明 legacy 映射路径不依赖 Capability runtime（源码级符号检查）
5. Boundary tests 证明 SG 产物不包含 ServiceType/ActionName/DynamicApiEndpointDescriptor
6. Boundary tests 证明两条映射路径不交叉（MapCrestCapabilityEndpoints 不调用 MapCrestDynamicApi，反之亦然）
7. `CapabilityEndpointSpecAttribute` 有独立 `EndpointId` / `EndpointVersion` 属性
8. Level 2 attributes 有独立 `EndpointId` / `EndpointVersion` 属性
9. SG 使用独立 endpoint identity，fallback 到 CapabilityId/CapabilityVersion 推导
10. CEP017 诊断验证 EndpointId 不含空白字符
11. `CapabilityEndpointDescriptorValidator` 验证 Id 非空且不含空白字符
12. `CapabilityEndpointInputAttribute` 有 `TargetProperty` 属性
13. BindingEmitter 使用 `TargetProperty` 做 CLR 属性赋值，不使用 `CapabilityInputPath`
14. ProviderEmitter 只输出 `CapabilityInputPath`，不输出 `TargetProperty`
15. CEP018 诊断验证 TargetProperty 对应的 body DTO 属性存在且 public settable
16. CEP019 诊断验证 TargetProperty 是合法 simple property name（不含 `.`）
17. CEP013 是 Error，不是 Warning
18. CEP013 Level 1 覆盖 Route/Query/Header 任意组合；Level 2 覆盖 route tokens + explicit Input（Level 2 不读取 class-level `[CapabilityEndpointInput]`）
19. 多 scalar input 无 Body 时 SG 不生成 Dictionary binding
20. `DynamicApiSourceGenerator` 已回收到 `99_RecycleBin/`（不是删除）
21. Legacy test 文件已重命名/移动，有 compatibility-only 注释
22. CodeGenerator legacy test 只移动 legacy generator tests，不移动 CapabilityEndpointGenerator tests
23. `AddCrestDynamicApi` / `MapCrestDynamicApi` 有 XML docs 标注 legacy 但**不加** `[Obsolete]`
24. `dotnet build` + `dotnet test` 全部通过
25. Architecture note 写入 docs，memory.md 只记录结果摘要
26. BindingRegistry 生命周期边界已声明：process-wide generated registry，不支持 runtime unload/reload/hot projection
27. CEP020 诊断验证 EndpointVersion 不能为负数
28. Emitter 中不存在任何 `Dictionary<string, object?>` fallback 分支；multi-scalar 无 body 路径生成 `throw new InvalidOperationException`
