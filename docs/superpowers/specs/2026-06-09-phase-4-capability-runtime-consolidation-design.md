# Phase 4 — Capability Runtime Consolidation Design Spec

**Date**: 2026-06-09
**Status**: Draft
**Depends On**: Phase 3 (Metadata Runtime Foundation)

---

## 1. Objective

将现有双轨 Capability Runtime **收敛为单一执行内核**。

Phase 4 不是重建。现有 Pipeline/Middleware/Context/Result/HandlerResolver 全部保持不变。本阶段重点是 **Runtime Consolidation（运行时统一化）**。

### 1.1 目标状态

```text
Capability Descriptor (统一)
        ↓
Capability Registry (统一)
        ↓
Capability Dispatcher (Facade)
        ↓
Capability Pipeline (Execution Engine)
        ↓
Capability Handler
        ↓
Capability Result + Audit + Events
```

### 1.2 非目标

Phase 4 **不实现**：

- Workflow Runtime（WorkflowExecutor, StateMachine, Suspend/Resume）
- HumanTask Runtime（HumanTaskInstance, HumanTaskStore）
- Metadata Graph / Topology Engine / Impact Analysis
- AI Runtime（LLM Planning, Agent Reasoning）
- IDynamicRegistry<T>（运行时注册，延迟到后续阶段）
- CapabilityQuery 统一查询模型（延迟到 Phase 6 Metadata Topology Engine）

---

## 2. Architecture Overview

### 2.1 当前状态（Phase 3 完成后）

```text
✅ Descriptor Model (IDescriptor, IVersionedDescriptor, IHasContractIdentity)
✅ RegistryBase<T> (FrozenDictionary snapshot)
✅ Registry Validation Engine (IRegistryValidator<T>)
✅ BootstrapCoordinator (topological sort)
✅ DescriptorResolver (unified resolution)
✅ EventRegistry (on RegistryBase)
✅ CapabilityRegistry (Metadata, on RegistryBase)
✅ CapabilityPipeline (onion middleware model, 7 middleware)
✅ ICapabilityHandlerInvoker + DelegateHandlerInvoker (AOT-safe)
✅ CapabilityExecutionContext + CapabilityExecutionResult
```

### 2.2 Phase 4 变更

```text
🆕 统一 CapabilityDescriptor（Metadata 版本吸收运行时属性）
🆕 统一 CapabilityRegistry（删除 ConcurrentDictionary 版本）
🆕 ICapabilityDispatcher（Facade 层）
🆕 ICapabilityResolver（外部唯一解析入口）
🆕 ICapabilityCatalog（浏览/发现接口）
🆕 ICapabilityAuditStore + AuditMiddleware
🆕 CapabilityHandlerValidator + CapabilitySchemaValidator
🆕 AddCapabilityRuntime() 统一 DI 注册
```

### 2.3 架构约束

必须遵守：

- Metadata First, Registry Driven, Descriptor Driven
- Source Generator Friendly, AOT Friendly
- 与 RegistryBase<T>, BootstrapCoordinator, Unified Metadata Model 保持一致风格

禁止：

- Reflection Discovery
- Assembly Scanning
- Service Locator
- Dynamic Runtime Registration（Phase 4 不支持，延迟到后续阶段）

---

## 3. Unified CapabilityDescriptor

### 3.1 设计

两个 CapabilityDescriptor 合并为 Metadata 层唯一版本。

**删除**：`Capability.Abstractions/CapabilityDescriptor.cs`

**修改**：`Metadata/CapabilityDescriptor.cs` 吸收运行时属性。

```csharp
public sealed class CapabilityDescriptor
    : IDescriptor, IVersionedDescriptor, IHasContractIdentity, IRelationshipAwareDescriptor
{
    // === 基础属性 ===
    public string Namespace { get; init; } = "capability";
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorKind Kind => DescriptorKind.Capability;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }
    public int Version { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;

    // === 目录属性（已有）===
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<EventRef> Produces { get; init; } = Array.Empty<EventRef>();
    public IReadOnlyList<EventRef> Consumes { get; init; } = Array.Empty<EventRef>();
    public IReadOnlyList<string> SemanticTags { get; init; } = Array.Empty<string>();

    // === 运行时属性（从 Abstractions 合并）===
    public CapabilityKind CapabilityKind { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor>? InputSchema { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor>? OutputSchema { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public CapabilityRiskLevel RiskLevel { get; init; } = CapabilityRiskLevel.Medium;

    // === IRelationshipAwareDescriptor ===
    public IReadOnlyList<DescriptorRelationship> GetRelationships()
    {
        var relationships = new List<DescriptorRelationship>();

        if (InputSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                RelationshipKind.Consumes,
                new DescriptorRef(InputSchema.Value.Namespace, InputSchema.Value.Id, InputSchema.Value.Version)));
        }

        if (OutputSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                RelationshipKind.Produces,
                new DescriptorRef(OutputSchema.Value.Namespace, OutputSchema.Value.Id, OutputSchema.Value.Version)));
        }

        if (SupersededById is not null)
        {
            relationships.Add(new DescriptorRelationship(
                RelationshipKind.Obsoletes,
                new DescriptorRef(Namespace, SupersededById)));
        }

        foreach (var @event in Produces)
        {
            relationships.Add(new DescriptorRelationship(
                RelationshipKind.Produces,
                new DescriptorRef(@event.Namespace, @event.Id, @event.Version)));
        }

        foreach (var @event in Consumes)
        {
            relationships.Add(new DescriptorRelationship(
                RelationshipKind.Consumes,
                new DescriptorRef(@event.Namespace, @event.Id, @event.Version)));
        }

        return relationships;
    }
}
```

### 3.2 Id vs Name 语义

| 属性 | 语义 | 示例 |
|---|---|---|
| `Id` | 稳定唯一标识，程序化使用，不可变 | `customer.create` |
| `Name` | 可读显示名，UI/日志/展示用 | `Create Customer` |

- `Id` 是 Capability 的全局唯一标识，用于 Registry 查找、Audit 记录、Handler 映射
- `Name` 是人类可读的显示名，用于 Workflow UI、Agent Planner、Audit 报表
- `Id` 不可变（Rename 不影响），`Name` 可以变更
- 如果没有显示名需求，`Name` 可以等于 `Id`

### 3.3 枚举迁移

从 `Capability.Abstractions` 迁移到 `Metadata.Abstractions`：

```csharp
// Metadata.Abstractions/CapabilityKind.cs
public enum CapabilityKind
{
    Query,
    Command
}

// Metadata.Abstractions/CapabilityRiskLevel.cs
public enum CapabilityRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}
```

### 3.3 设计决策

| 决策 | 理由 |
|---|---|
| InputSchema/OutputSchema 保留 VersionedDescriptorRef | Schema 是版本化资源，需要精确版本引用 |
| InputSchema/OutputSchema 可空 | 并非所有 Capability 都有 Schema（如无参命令） |
| Permissions 使用 IReadOnlyList<string> | 为未来 AND/OR/Policy 预留空间 |
| 不新增 IsDeprecated 属性 | 依赖 DescriptorState，VersionResolver 规范中明确 Latest Active = State == Active |
| Aliases 不放在 Descriptor | 路由关注点，属于 Dispatcher 层 |

---

## 4. Unified CapabilityRegistry

### 4.1 设计

删除 Capability 层的 ConcurrentDictionary 注册表，统一使用 Metadata 层的 RegistryBase。

```text
Before:                              After:
CapabilityRegistry                   CapabilityRegistry
(ConcurrentDictionary, mutable)      (RegistryBase<T>, immutable snapshot)
         ×                                  ✓
Metadata.CapabilityRegistry          Metadata.CapabilityRegistry
(RegistryBase<T>, immutable)         (RegistryBase<T>, immutable) + ICapabilityRegistry + ICapabilityCatalog
         ✓                                  ✓ (唯一)
```

### 4.2 实现

```csharp
// Metadata/CapabilityRegistry.cs
public sealed class CapabilityRegistry
    : RegistryBase<CapabilityDescriptor>, ICapabilityRegistry
{
    public override string RegistryNamespace => "capability";

    // ICapabilityRegistry 领域特化方法
    public IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind)
    {
        return GetAll().Where(d => d.CapabilityKind == kind).ToList();
    }

    public IReadOnlyList<CapabilityDescriptor> GetByTag(string tag)
    {
        return GetAll().Where(d => d.SemanticTags.Contains(tag)).ToList();
    }
}
```

### 4.3 ICapabilityRegistry 接口

保留在 `Capability.Abstractions`（领域特化接口，不污染 Metadata.Abstractions）：

```csharp
// Capability.Abstractions/ICapabilityRegistry.cs
public interface ICapabilityRegistry
{
    IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind);
    IReadOnlyList<CapabilityDescriptor> GetByTag(string tag);
}
```

### 4.4 设计决策

| 决策 | 理由 |
|---|---|
| ICapabilityRegistry 保留在 Capability.Abstractions | 领域特化接口，Metadata.Abstractions 只知道 RegistryBase<T> |
| 不新增 ICapabilityCatalog | Registry 本身就是 Metadata Query Surface，Lookup/Browse/Discover 是 Registry 的职责。避免命名爆炸（Registry/Catalog/Locator/Provider/Manager） |
| GetByKind 返回 IReadOnlyList | Kind 不是唯一键，可能有多个同 Kind 的 Capability |
| IDynamicRegistry<T> 延迟 | Phase 4 统一到快照模式，运行时注册后续阶段实现 |

---

## 5. Resolver 设计

### 5.1 架构约束

```text
ICapabilityResolver 是外部唯一解析入口。
Resolver 是 Registry 的只读适配器。

CapabilityRegistry (source of truth)
      ↑
      │
 ICapabilityResolver
 (单个解析：name → descriptor)
```

Alias 解析不属于 Resolver，属于 Dispatcher/Gateway/HTTP/MCP 层。

### 5.2 ICapabilityResolver

外部唯一解析入口。所有 Runtime（Workflow, Agent, HTTP, MCP）必须通过此接口解析 Capability，禁止直接查 Registry。

名称不含 "Descriptor"：返回类型已在签名中体现，避免 `IWorkflowDescriptorResolver` / `ISchemaDescriptorResolver` 等命名爆炸。

```csharp
// Capability.Abstractions/ICapabilityResolver.cs
public interface ICapabilityResolver
{
    /// <summary>
    /// 解析 Logical Capability Id + 版本选择，返回完整 CapabilityDescriptor。
    ///
    /// 输入格式：
    ///   "customer.create"     → 最新 Active 版本
    ///   "customer.create:3"   → 指定版本 3
    ///
    /// Latest Active 定义：State == Active
    ///
    /// 不支持 Alias（Alias 解析属于 Dispatcher/Gateway 层）
    ///
    /// 找不到 → 抛出 CapabilityNotFoundException
    /// </summary>
    CapabilityDescriptor Resolve(string capabilityIdOrVersion);
}
```

### 5.3 ICapabilityVersionResolver（内部）

版本解析组件，不暴露到 Capability.Abstractions。

```csharp
// Capability/Internal/ICapabilityVersionResolver.cs
namespace CrestCreates.Capability.Internal;

internal interface ICapabilityVersionResolver
{
    CapabilityDescriptor Resolve(string capabilityNameOrVersion);
}
```

**DefaultCapabilityResolver** 内部使用 `ICapabilityVersionResolver`。

---

## 6. ICapabilityDispatcher

### 6.1 设计

Facade 层，位于 Pipeline 之上。为所有外部调用者提供统一入口。

```csharp
// Capability.Abstractions/ICapabilityDispatcher.cs
public interface ICapabilityDispatcher
{
    /// <summary>
    /// 主重载：直接接收 Descriptor（无重复 Resolve）。
    /// Workflow/Agent 等已持有 Descriptor 的调用者应使用此重载。
    /// </summary>
    Task<CapabilityExecutionResult> DispatchAsync(
        CapabilityDescriptor descriptor,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);

    /// <summary>
    /// 便利重载：内部 Resolve 后委托主重载。
    /// 适用于未预先持有 Descriptor 的简单调用场景。
    /// </summary>
    Task<CapabilityExecutionResult> DispatchAsync(
        string capabilityId,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default);
}
```

### 6.2 实现逻辑

```csharp
// Capability/CapabilityDispatcher.cs
internal sealed class CapabilityDispatcher : ICapabilityDispatcher
{
    private readonly ICapabilityResolver _descriptorResolver;
    private readonly ICapabilityPipeline _pipeline;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    // 主重载：直接接收 Descriptor
    public async Task<CapabilityExecutionResult> DispatchAsync(
        CapabilityDescriptor descriptor,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        return await _pipeline.ExecuteAsync(descriptor.Id, input, ctx =>
        {
            ctx.CapabilityId = descriptor.Id;
            ctx.CapabilityName = descriptor.Name;              // 可读名称，用于日志/审计
            ctx.CapabilityVersion = descriptor.Version;
            ctx.CapabilityContractHash = descriptor.ContractHash;

            // 自动注入
            ctx.TenantId = _tenantContext.TenantId;
            ctx.UserId = _currentUser.UserId;

            configureContext?.Invoke(ctx);
        }, ct);
    }

    // 便利重载：内部 Resolve 后委托主重载
    public async Task<CapabilityExecutionResult> DispatchAsync(
        string capabilityId,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        var descriptor = _descriptorResolver.Resolve(capabilityId);
        return await DispatchAsync(descriptor, input, configureContext, ct);
    }
}
```

### 6.3 设计决策

| 决策 | 理由 |
|---|---|
| 主重载接收 Descriptor | 避免重复 Resolve（Caller 已持有 Descriptor 时） |
| 便利重载接收 capabilityId | 简单场景无需预先 Resolve |
| DispatchAsync(string) 内部委托 DispatchAsync(descriptor) | 单一实现路径 |
| 无 DispatchManyAsync | 批量执行是 Workflow Runtime 的责任 |
| InvocationSource 为强类型字段 | 避免 Context.Items["__callerSource"] 魔法字符串 |
| 自动注入 TenantId/UserId/CapabilityName | Dispatcher 是上下文注入的正确位置 |
| 不引入 ICapabilityProfileResolver | Profile 容易变成万能配置垃圾桶，等 Phase 7+ Execution Policy 再做 |

---

## 7. InvocationSource

### 7.1 设计

```csharp
// Capability.Abstractions/Execution/InvocationSource.cs
namespace CrestCreates.Capability.Abstractions;

public enum InvocationSource
{
    Unknown,

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

### 7.2 CapabilityExecutionContext 变更

新增强类型字段：

```csharp
// 在现有 CapabilityExecutionContext 中新增
public string CapabilityId { get; init; } = string.Empty;       // 稳定标识（= IDescriptor.Id）
public InvocationSource InvocationSource { get; init; } = InvocationSource.Unknown;
```

- `CapabilityId` 由 Dispatcher 从 `descriptor.Id` 注入
- `CapabilityName` 已有，用于可读展示
- AuditMiddleware 读取 `context.CapabilityId` 写入 `CapabilityExecutionRecord.CapabilityId`

---

## 8. ICapabilityAuditStore

### 8.1 设计

```csharp
// Capability.Abstractions/ICapabilityAuditStore.cs
public interface ICapabilityAuditStore
{
    Task RecordAsync(CapabilityExecutionRecord record, CancellationToken ct = default);
}
```

### 8.2 CapabilityExecutionRecord

```csharp
// Capability.Abstractions/CapabilityExecutionRecord.cs
public sealed record CapabilityExecutionRecord
{
    public string ExecutionId { get; init; } = string.Empty;
    public string CapabilityId { get; init; } = string.Empty;
    public string CapabilityName { get; init; } = string.Empty;
    public int CapabilityVersion { get; init; }
    public string? TenantId { get; init; }
    public string? UserId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public InvocationSource Source { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorCode { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
```

### 8.3 字段语义

| 字段 | 用途 |
|---|---|
| ExecutionId | 单次 Capability 执行的唯一标识 |
| CapabilityId | 稳定标识，不受 Rename 影响（= IDescriptor.Id） |
| CapabilityName | 可读名称，用于日志/展示 |
| CorrelationId | 跨流程链（Workflow → 多个 Capability 共享） |

### 8.4 设计决策

| 决策 | 理由 |
|---|---|
| 无 QueryAsync | 查询属于未来 Audit Repository 阶段（SQL/Mongo/Elastic/ClickHouse） |
| InMemoryCapabilityAuditStore | ConcurrentQueue 存储，开发/测试用 |
| ExecutionId 区分同 CorrelationId 的多次执行 | Workflow A → Capability X → Capability Y 共享 CorrelationId |

---

## 9. AuditMiddleware

### 9.1 Middleware 顺序

```text
AuditMiddleware              ← try/finally，最外层，记录所有结果（含失败/异常/取消）
    ↓
RateLimitMiddleware
    ↓
TenantMiddleware
    ↓
AuthorizationMiddleware
    ↓
ValidationMiddleware
    ↓
IdempotencyMiddleware
    ↓
MetricsMiddleware            ← 包裹 Handler，统计真实执行时间
    ↓
EventPublishingMiddleware
    ↓
Handler
```

### 9.2 实现

```csharp
// Capability/Middleware/AuditMiddleware.cs
internal sealed class AuditMiddleware : ICapabilityPipelineMiddleware
{
    private readonly ICapabilityAuditStore _auditStore;
    private readonly ILogger<AuditMiddleware> _logger;

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        var executionId = Guid.NewGuid().ToString("N");
        var sw = Stopwatch.StartNew();
        CapabilityExecutionResult? result = null;
        Exception? unhandledException = null;
        bool cancelled = false;

        try
        {
            result = await next(context);
            return result;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            throw;
        }
        catch (Exception ex)
        {
            unhandledException = ex;
            throw;
        }
        finally
        {
            sw.Stop();

            // Audit 失败不影响业务执行
            try
            {
                var errorCode = cancelled
                    ? "CANCELLED"
                    : result?.ErrorCode
                      ?? (unhandledException is not null ? "UNHANDLED_EXCEPTION" : null);

                await _auditStore.RecordAsync(new CapabilityExecutionRecord
                {
                    ExecutionId = executionId,
                    CapabilityId = context.CapabilityId,
                    CapabilityName = context.CapabilityName,
                    CapabilityVersion = context.CapabilityVersion,
                    TenantId = context.TenantId,
                    UserId = context.UserId,
                    CorrelationId = context.CorrelationId,
                    Source = context.InvocationSource,
                    IsSuccess = result?.IsSuccess ?? false,
                    ErrorCode = errorCode,
                    Duration = sw.Elapsed,
                    Timestamp = DateTimeOffset.UtcNow
                });
            }
            catch (Exception auditEx)
            {
                _logger.LogError(auditEx, "Failed to record audit for capability '{CapabilityId}'", context.CapabilityId);
            }
        }
    }
}
```

### 9.3 设计决策

| 决策 | 理由 |
|---|---|
| Audit 在最外层 | 确保所有结果（含 Authorization/Validation 失败）都被记录 |
| Metrics 在 Handler 之上 | 统计包含 Handler 真实执行时间 |
| try/finally 模式 | 异常/取消也能记录 |
| Audit 失败隔离 | AuditStore 异常不影响业务执行，仅记录日志 |

---

## 10. Bootstrap Validators

### 10.1 CapabilityHandlerValidator

Source Generator 生成一个静态注册表 `GeneratedCapabilityHandlerRegistry`，实现 `ICapabilityHandlerRegistry` 接口。Validator 通过此接口检查映射，不依赖运行时 DI。

**重复映射检测**：不在 Runtime Validator 中做。Source Generator 在编译阶段直接报错（Compile Error），不留到运行时。

```csharp
// Metadata.Abstractions/ICapabilityHandlerRegistry.cs
// Source Generator 实现此接口，提供 capability id → handler type 的静态映射
// key = CapabilityId（稳定标识），不是 Name（可读名称）
public interface ICapabilityHandlerRegistry
{
    IReadOnlyDictionary<string, Type> GetHandlerMappings();
}

// Capability/Bootstrap/CapabilityHandlerValidator.cs
public sealed class CapabilityHandlerValidator : IRegistryValidator<CapabilityDescriptor>
{
    private readonly ICapabilityHandlerRegistry _handlerRegistry;

    public CapabilityHandlerValidator(ICapabilityHandlerRegistry handlerRegistry)
    {
        _handlerRegistry = handlerRegistry;
    }

    public int Order => 100;

    public ValidationReport Validate(IReadOnlyList<CapabilityDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();
        var mappings = _handlerRegistry.GetHandlerMappings();

        // 检查每个 Descriptor 在 Source Generator 注册表中有对应 Handler
        // 映射 key = CapabilityId（稳定标识），不使用 Name（可读名称）
        foreach (var descriptor in descriptors)
        {
            if (!mappings.ContainsKey(descriptor.Id))
            {
                issues.Add(ValidationIssue.Error(
                    $"Capability '{descriptor.Id}' (Name: '{descriptor.Name}') has no registered handler. " +
                    $"Add [GenerateCapabilityHandler] or register manually."));
            }
        }

        return ValidationReport.FromIssues(issues);
    }
}
```

### 10.2 CapabilitySchemaValidator

依赖抽象 `IDescriptorLookup`（只读接口），不依赖具体 Registry 类型。避免 Capability Validator → SchemaRegistry 的直接耦合，为 Phase 6 Graph Engine 铺路。

```csharp
// Metadata.Abstractions/IDescriptorLookup.cs
// Bootstrap 阶段可用的只读查询接口
// 实现可由各 Registry 提供，或由 BootstrapCoordinator 构建统一 Lookup
public interface IDescriptorLookup
{
    bool Exists(DescriptorRef descriptorRef);
}

// Metadata/CapabilitySchemaValidator.cs
public sealed class CapabilitySchemaValidator : IRegistryValidator<CapabilityDescriptor>
{
    private readonly IDescriptorLookup _descriptorLookup;

    public CapabilitySchemaValidator(IDescriptorLookup descriptorLookup)
    {
        _descriptorLookup = descriptorLookup;
    }

    public int Order => 200;

    public ValidationReport Validate(IReadOnlyList<CapabilityDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        foreach (var descriptor in descriptors)
        {
            if (descriptor.InputSchema.HasValue)
            {
                var schemaRef = descriptor.InputSchema.Value;
                var refObj = new DescriptorRef(schemaRef.Namespace, schemaRef.Id, schemaRef.Version);
                if (!_descriptorLookup.Exists(refObj))
                {
                    issues.Add(ValidationIssue.Error(
                        $"Capability '{descriptor.Id}' references InputSchema '{schemaRef.Id}' (v{schemaRef.Version}) which does not exist."));
                }
            }

            if (descriptor.OutputSchema.HasValue)
            {
                var schemaRef = descriptor.OutputSchema.Value;
                var refObj = new DescriptorRef(schemaRef.Namespace, schemaRef.Id, schemaRef.Version);
                if (!_descriptorLookup.Exists(refObj))
                {
                    issues.Add(ValidationIssue.Error(
                        $"Capability '{descriptor.Id}' references OutputSchema '{schemaRef.Id}' (v{schemaRef.Version}) which does not exist."));
                }
            }
        }

        return ValidationReport.FromIssues(issues);
    }
}
```

### 10.3 设计决策

| 决策 | 理由 |
|---|---|
| 验证 Source Generator 输出 | 不依赖运行时 DI，Bootstrap 阶段安全 |
| 只检查 Handler 存在性 | 重复映射由 Source Generator 编译时检测，不留到 Runtime |
| SchemaValidator 检查引用 | 防止运行时 Schema 解析失败 |

---

## 11. DI Registration

### 11.1 AddCapabilityRuntime

```csharp
// Capability/CapabilityServiceCollectionExtensions.cs
public static IServiceCollection AddCapabilityRuntime(
    this IServiceCollection services)
{
    // 现有
    services.AddCapabilityPipeline();
    services.AddCapabilityHandlerInvoker();

    // 新增
    services.TryAddSingleton<ICapabilityDispatcher, CapabilityDispatcher>();
    services.TryAddSingleton<ICapabilityResolver, DefaultCapabilityResolver>();

    // 内部组件
    services.TryAddSingleton<ICapabilityVersionResolver, DefaultCapabilityVersionResolver>();

    // ICapabilityHandlerRegistry 由 Source Generator 注册（GeneratedCapabilityHandlerRegistry）
    // 无需在此手动注册

    // 默认 NoOp AuditStore（不记录审计）
    // 开发环境：services.AddInMemoryCapabilityAudit() 显式开启
    // 生产环境：替换为真实实现（SQL/Mongo/Elastic）
    services.TryAddSingleton<ICapabilityAuditStore, NullCapabilityAuditStore>();

    // Validators
    services.AddSingleton<IRegistryValidator<CapabilityDescriptor>, CapabilityHandlerValidator>();
    services.AddSingleton<IRegistryValidator<CapabilityDescriptor>, CapabilitySchemaValidator>();

    // AuditMiddleware（最外层）
    services.AddTransient<AuditMiddleware>();

    return services;
}

// 开发环境显式开启 InMemory 审计
// 使用 Replace 确保覆盖 NullCapabilityAuditStore，避免 IEnumerable 出现多个实现
public static IServiceCollection AddInMemoryCapabilityAudit(this IServiceCollection services)
{
    services.Replace(ServiceDescriptor.Singleton<ICapabilityAuditStore, InMemoryCapabilityAuditStore>());
    return services;
}
```

---

## 12. Execution Flow

### 12.1 调用路径

**Path A**：Caller 已持有 Descriptor（Workflow/Agent 等推荐路径）

```text
Caller
  ↓
ICapabilityResolver.Resolve(id)        ← 可选，Caller 自行决定何时 Resolve
  ↓
CapabilityDescriptor
  ↓
ICapabilityDispatcher.DispatchAsync(descriptor, input, configureContext)
  ↓
Pipeline
```

**Path B**：Caller 只有 Id（简单场景）

```text
Caller
  ↓
ICapabilityDispatcher.DispatchAsync(id, input, configureContext)
  ↓
ICapabilityResolver.Resolve(id)        ← Dispatcher 内部自动 Resolve
  ↓
Pipeline
```

### 12.2 Pipeline 内部

```text
┌──────────────────────────────────────────────────────┐
│  AuditMiddleware             ← try/finally，最外层    │
│    ↓                                                  │
│  RateLimitMiddleware                                 │
│    ↓                                                  │
│  TenantMiddleware                                    │
│    ↓                                                  │
│  AuthorizationMiddleware    ← Permissions            │
│    ↓                                                  │
│  ValidationMiddleware       ← InputSchema            │
│    ↓                                                  │
│  IdempotencyMiddleware                               │
│    ↓                                                  │
│  MetricsMiddleware            ← 包裹 Handler         │
│    ↓                                                  │
│  EventPublishingMiddleware                           │
│    ↓                                                  │
│  Handler (ICapabilityHandlerInvoker)                 │
└──────────────────────────────────────────────────────┘
         ↓
CapabilityExecutionResult
         ↓
ICapabilityAuditStore.RecordAsync(record)
```

---

## 13. File Change Summary

### 13.1 New Files

| File | Description |
|---|---|
| `Capability.Abstractions/Execution/InvocationSource.cs` | 枚举 |
| `Capability.Abstractions/ICapabilityDispatcher.cs` | Dispatcher 接口 |
| `Capability.Abstractions/ICapabilityAuditStore.cs` | Audit 接口 |
| `Capability.Abstractions/CapabilityExecutionRecord.cs` | Audit 记录 |
| `Capability.Abstractions/ICapabilityResolver.cs` | 外部唯一解析入口（原 ICapabilityDescriptorResolver） |
| `Capability.Abstractions/CapabilityNotFoundException.cs` | 解析失败异常 |
| `Metadata.Abstractions/ICapabilityHandlerRegistry.cs` | Source Generator handler 注册表接口 |
| `Metadata.Abstractions/IDescriptorLookup.cs` | Bootstrap 阶段只读查询接口 |
| `Capability/Internal/ICapabilityVersionResolver.cs` | 内部版本解析 |
| `Capability/Internal/DefaultCapabilityVersionResolver.cs` | 版本解析实现 |
| `Capability/CapabilityDispatcher.cs` | Dispatcher 实现 |
| `Capability/InMemoryCapabilityAuditStore.cs` | InMemory Audit（开发环境） |
| `Capability/NullCapabilityAuditStore.cs` | NoOp 默认实现 |
| `Capability/DefaultCapabilityResolver.cs` | 解析实现 |
| `Capability/Middleware/AuditMiddleware.cs` | 最外层 Audit |
| `Metadata.Abstractions/CapabilityKind.cs` | 枚举迁移 |
| `Metadata.Abstractions/CapabilityRiskLevel.cs` | 枚举迁移 |
| `Capability/Bootstrap/CapabilityHandlerValidator.cs` | Bootstrap 验证（Runtime 层） |
| `Metadata/CapabilitySchemaValidator.cs` | Bootstrap 验证 |

### 13.2 Modified Files

| File | Change |
|---|---|
| `Metadata/CapabilityDescriptor.cs` | 合并运行时属性 |
| `Metadata/CapabilityRegistry.cs` | 实现 ICapabilityRegistry |
| `Capability.Abstractions/CapabilityExecutionContext.cs` | 新增 InvocationSource 字段 |
| `Capability/CapabilityServiceCollectionExtensions.cs` | 新增 AddCapabilityRuntime() |
| `Capability/CapabilityPipeline.cs` | middleware 顺序调整 |

### 13.3 Deleted Files

| File | Reason |
|---|---|
| `Capability.Abstractions/CapabilityDescriptor.cs` | 合并到 Metadata |
| `Capability/CapabilityRegistry.cs` | 统一到 RegistryBase |

---

## 14. Testing Strategy

### 14.1 Unit Tests

| Test Class | Coverage |
|---|---|
| CapabilityDescriptorTests | 统一 Descriptor 属性、合并后完整性 |
| CapabilityRegistryTests | RegistryBase + ICapabilityRegistry |
| CapabilityDispatcherTests | 双重载（Descriptor/Id）、Context 注入（Id+Name）、避免重复 Resolve |
| CapabilityResolverTests | Resolve by id、Resolve by id:version |
| CapabilityHandlerValidatorTests | Handler 存在性验证（by Id） |
| CapabilitySchemaValidatorTests | Schema 引用验证（IDescriptorLookup） |
| InMemoryCapabilityAuditStoreTests | RecordAsync 存储 |
| NullCapabilityAuditStoreTests | NoOp 行为 |
| AuditMiddlewareTests | try/finally 记录、异常记录、Cancellation 区分 |
| InvocationSourceTests | 枚举值、Context 字段 |

### 14.2 Integration Tests

| Test | Coverage |
|---|---|
| CapabilityRuntimeEndToEnd | Dispatcher → Pipeline → Handler → Audit 完整链路 |
| CapabilityRuntimeFailurePath | Handler 异常 → Audit 记录 → EventPublishing 失败事件 |
| CapabilityRuntimeAuthorizationFailure | Authorization 拒绝 → Audit 记录 |
| CapabilityRuntimeValidationFailure | Validation 失败 → Audit 记录 |

---

## 15. Design Decisions

| # | Decision | Rationale |
|---|---|---|
| 44 | 统一到 Metadata CapabilityDescriptor | 单一来源，消除双轨 |
| 45 | InputSchema/OutputSchema 保留 VersionedDescriptorRef | Schema 是版本化资源 |
| 46 | Permissions 使用 IReadOnlyList<string> | 为 AND/OR/Policy 预留空间 |
| 47 | Aliases 不放在 Descriptor | 路由关注点，属于 Dispatcher/Gateway/HTTP/MCP 层 |
| 48 | IsDeprecated 依赖 DescriptorState | 不新增属性，VersionResolver 规范明确 State == Active |
| 49 | ICapabilityRegistry 保留在 Capability.Abstractions | 领域特化接口，不污染 Metadata.Abstractions |
| 50 | 不新增 ICapabilityCatalog | Registry 本身就是 Metadata Query Surface，避免命名爆炸 |
| 51 | Resolver 是 Registry 的只读适配器 | 不解析 Alias，Alias 属于 Dispatcher/Gateway 层 |
| 52 | ICapabilityVersionResolver 内部化 | 外部唯一入口是 ICapabilityResolver（原 ICapabilityDescriptorResolver） |
| 53 | 无 DispatchManyAsync | 批量执行是 Workflow Runtime 的责任 |
| 54 | InvocationSource 为强类型字段 | 避免魔法字符串 |
| 55 | AuditRecord 含 ExecutionId | 区分同 CorrelationId 的多次执行 |
| 56 | AuditRecord 含 CapabilityId | 稳定标识，不受 Rename 影响 |
| 57 | AuditStore 无 QueryAsync | 查询属于未来 Audit Repository 阶段 |
| 58 | AuditMiddleware 最外层 | 确保所有结果（含失败）都被记录 |
| 59 | MetricsMiddleware 包裹 Handler | 统计真实执行时间 |
| 60 | CapabilityHandlerValidator 只检查 Handler 存在性 | 重复映射由 Source Generator 编译时检测 |
| 61 | Validator 验证 Source Generator 输出 | 不依赖运行时 DI |
| 62 | 不引入 ICapabilityProfileResolver | Profile 容易变成万能配置垃圾桶，等 Phase 7+ Execution Policy |
| 63 | Id = 稳定唯一标识，Name = 可读显示名 | 明确语义，避免 Workflow UI/Agent Planner/Audit 混乱 |
| 64 | Handler Mapping key = CapabilityId | 不使用 Name，Rename 不影响 Handler 映射 |
| 65 | Pipeline Execute 使用 Id | 执行链基于稳定标识，不基于可读名称 |
| 66 | SchemaValidator 依赖 IDescriptorLookup | 避免 Validator → 具体 Registry 耦合，为 Phase 6 Graph Engine 铺路 |
| 67 | Audit 失败隔离 | AuditStore 异常不影响业务执行 |
| 68 | 默认 NoOpAuditStore | 防止生产环境误以为有审计。开发环境显式 AddInMemoryCapabilityAudit() |
| 69 | Dispatcher 主重载接收 Descriptor | 避免 Caller 和 Dispatcher 重复 Resolve |
| 70 | Dispatcher 注入 CapabilityName | Context 同时有 Id 和 Name，审计/UI 各取所需 |
| 71 | 删除 EnableAudit | AuditMiddleware 始终存在，AuditStore 决定是否落盘 |
| 72 | InvocationSource 预留 Event/Mcp/BackgroundJob | 一次到位，避免后续阶段改枚举 |
| 73 | ICapabilityDescriptorResolver → ICapabilityResolver | 返回类型已在签名中体现，避免 IWorkflowDescriptorResolver 等命名爆炸 |
| 74 | CapabilityHandlerValidator 归属 Capability/Bootstrap | Metadata 不反向依赖 Runtime 语义，边界更干净 |
| 75 | AuditMiddleware 区分 Cancellation | OperationCanceledException → "CANCELLED"，非 "UNHANDLED_EXCEPTION"。Workflow Suspend/Resume 依赖此区别 |
| 76 | AddInMemoryCapabilityAudit 使用 Replace | 避免 IEnumerable 出现多个 ICapabilityAuditStore 实现 |
