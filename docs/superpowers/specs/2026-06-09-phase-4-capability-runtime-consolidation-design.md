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
🆕 ICapabilityDescriptorResolver（外部唯一解析入口）
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

### 3.2 枚举迁移

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
    : RegistryBase<CapabilityDescriptor>, ICapabilityRegistry, ICapabilityCatalog
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

    // ICapabilityCatalog 浏览方法（直接委托 RegistryBase）
    // GetByKind, GetByTag, GetAll 已由上述实现覆盖
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

### 4.4 ICapabilityCatalog 接口

```csharp
// Capability.Abstractions/ICapabilityCatalog.cs
public interface ICapabilityCatalog
{
    IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind);
    IReadOnlyList<CapabilityDescriptor> GetByTag(string tag);
    IReadOnlyList<CapabilityDescriptor> GetAll();
}
```

### 4.5 设计决策

| 决策 | 理由 |
|---|---|
| ICapabilityRegistry 保留在 Capability.Abstractions | 领域特化接口，Metadata.Abstractions 只知道 RegistryBase<T> |
| CapabilityRegistry 直接实现 ICapabilityCatalog | 不新增 DefaultCapabilityCatalog，避免每个领域复制 Catalog 实现 |
| GetByKind 返回 IReadOnlyList | Kind 不是唯一键，可能有多个同 Kind 的 Capability |
| IDynamicRegistry<T> 延迟 | Phase 4 统一到快照模式，运行时注册后续阶段实现 |

---

## 5. Resolver 与 Catalog 边界

### 5.1 架构约束

```text
Resolver 与 Catalog 均为 Registry 的只读适配器。
二者不得互相依赖。

CapabilityRegistry (source of truth)
      ↑                ↑
      │                │
 ICapabilityDescriptorResolver  ICapabilityCatalog
 (单个解析)                       (浏览/发现)
```

### 5.2 ICapabilityDescriptorResolver

外部唯一解析入口。所有 Runtime（Workflow, Agent, HTTP, MCP）必须通过此接口解析 Capability，禁止直接查 Registry。

```csharp
// Capability.Abstractions/ICapabilityDescriptorResolver.cs
public interface ICapabilityDescriptorResolver
{
    /// <summary>
    /// 解析能力名称，返回完整 CapabilityDescriptor。
    ///
    /// 输入格式：
    ///   "customer.create"     → 最新 Active 版本
    ///   "customer.create:3"   → 指定版本 3
    ///
    /// Latest Active 定义：State == Active
    ///
    /// 找不到 → 抛出 CapabilityNotFoundException
    /// </summary>
    CapabilityDescriptor Resolve(string capabilityNameOrAlias);
}
```

### 5.3 ICapabilityVersionResolver（内部）

版本解析组件，不暴露到 Capability.Abstractions。

```csharp
// Capability/Internal/ICapabilityVersionResolver.cs
namespace CrestCreates.Capability.Internal;

internal interface ICapabilityVersionResolver
{
    CapabilityDescriptor Resolve(string capabilityNameOrAlias);
}
```

**DefaultCapabilityDescriptorResolver** 内部使用 `ICapabilityVersionResolver`。

---

## 6. ICapabilityDispatcher

### 6.1 设计

Facade 层，位于 Pipeline 之上。为所有外部调用者提供统一入口。

```csharp
// Capability.Abstractions/ICapabilityDispatcher.cs
public interface ICapabilityDispatcher
{
    Task<CapabilityExecutionResult> DispatchAsync(
        string capabilityName,
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
    private readonly ICapabilityDescriptorResolver _descriptorResolver;
    private readonly ICapabilityPipeline _pipeline;
    private readonly ICapabilityProfileResolver _profileResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public async Task<CapabilityExecutionResult> DispatchAsync(
        string capabilityName,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        // 1. 解析 Descriptor（一步到位，含版本解析）
        var descriptor = _descriptorResolver.Resolve(capabilityName);

        // 2. 加载 Profile，自动配置
        var profile = await _profileResolver.ResolveAsync(descriptor);

        // 3. 委托 Pipeline 执行，自动注入上下文
        return await _pipeline.ExecuteAsync(descriptor.Name, input, ctx =>
        {
            ctx.CapabilityId = descriptor.Id;                  // 稳定标识
            ctx.CapabilityVersion = descriptor.Version;
            ctx.CapabilityContractHash = descriptor.ContractHash;

            // 自动注入
            ctx.TenantId = _tenantContext.TenantId;
            ctx.UserId = _currentUser.UserId;

            // Profile 配置
            if (profile?.Timeout is { } timeout)
                ctx.Items["__timeout"] = timeout;

            configureContext?.Invoke(ctx);
        }, ct);
    }
}
```

### 6.3 设计决策

| 决策 | 理由 |
|---|---|
| 无 DispatchManyAsync | 批量执行是 Workflow Runtime 的责任 |
| InvocationSource 为强类型字段 | 避免 Context.Items["__callerSource"] 魔法字符串 |
| 使用 ICapabilityDescriptorResolver | 一步到位解析，避免二次查询 Registry |
| 自动注入 TenantId/UserId | Dispatcher 是上下文注入的正确位置 |

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
    Agent,
    Workflow,
    HumanTask,
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

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        var executionId = Guid.NewGuid().ToString("N");
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await next(context);
            sw.Stop();

            await _auditStore.RecordAsync(new CapabilityExecutionRecord
            {
                ExecutionId = executionId,
                CapabilityId = context.CapabilityId,     // 由 Dispatcher 设置为 descriptor.Id
                CapabilityName = context.CapabilityName,
                CapabilityVersion = context.CapabilityVersion,
                TenantId = context.TenantId,
                UserId = context.UserId,
                CorrelationId = context.CorrelationId,
                Source = context.InvocationSource,
                IsSuccess = result.IsSuccess,
                ErrorCode = result.ErrorCode,
                Duration = sw.Elapsed,
                Timestamp = DateTimeOffset.UtcNow
            });

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();

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
                IsSuccess = false,
                ErrorCode = "UNHANDLED_EXCEPTION",
                Duration = sw.Elapsed,
                Timestamp = DateTimeOffset.UtcNow
            });

            throw;
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

---

## 10. Bootstrap Validators

### 10.1 CapabilityHandlerValidator

Source Generator 生成一个静态注册表 `GeneratedCapabilityHandlerRegistry`，实现 `ICapabilityHandlerRegistry` 接口。Validator 通过此接口检查映射，不依赖运行时 DI。

```csharp
// Metadata.Abstractions/ICapabilityHandlerRegistry.cs
// Source Generator 实现此接口，提供 capability name → handler type 的静态映射
public interface ICapabilityHandlerRegistry
{
    IReadOnlyDictionary<string, Type> GetHandlerMappings();
}

// Metadata/CapabilityHandlerValidator.cs
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

        // 1. 检查每个 Descriptor 在 Source Generator 注册表中有对应 Handler
        foreach (var descriptor in descriptors)
        {
            if (!mappings.ContainsKey(descriptor.Name))
            {
                issues.Add(ValidationIssue.Error(
                    $"Capability '{descriptor.Name}' has no registered handler. " +
                    $"Add [GenerateCapabilityHandler] or register manually."));
            }
        }

        // 2. 检查没有重复映射（1 Descriptor ↔ 1 Handler）
        var duplicates = mappings
            .GroupBy(kv => kv.Key)
            .Where(g => g.Count() > 1);

        foreach (var dup in duplicates)
        {
            issues.Add(ValidationIssue.Error(
                $"Capability '{dup.Key}' maps to multiple handlers: " +
                $"{string.Join(", ", dup.Select(d => d.Value.Name))}. " +
                $"Each capability must have exactly one handler."));
        }

        return ValidationReport.FromIssues(issues);
    }
}
```

### 10.2 CapabilitySchemaValidator

```csharp
// Metadata/CapabilitySchemaValidator.cs
public sealed class CapabilitySchemaValidator : IRegistryValidator<CapabilityDescriptor>
{
    public int Order => 200;

    public ValidationReport Validate(IReadOnlyList<CapabilityDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        // 验证 InputSchema/OutputSchema 引用的 SchemaDescriptor 存在
        // 通过 DescriptorResolver.Resolve(ref) 检查

        return ValidationReport.FromIssues(issues);
    }
}
```

### 10.3 设计决策

| 决策 | 理由 |
|---|---|
| 验证 Source Generator 输出 | 不依赖运行时 DI，Bootstrap 阶段安全 |
| 1:1 映射约束 | 防止"哪个 Handler 才是真的？"歧义 |
| SchemaValidator 检查引用 | 防止运行时 Schema 解析失败 |

---

## 11. DI Registration

### 11.1 AddCapabilityRuntime

```csharp
// Capability/CapabilityServiceCollectionExtensions.cs
public static IServiceCollection AddCapabilityRuntime(
    this IServiceCollection services,
    Action<CapabilityRuntimeOptions>? configure = null)
{
    // 现有
    services.AddCapabilityPipeline();
    services.AddCapabilityHandlerInvoker();

    // 新增
    services.TryAddSingleton<ICapabilityDispatcher, CapabilityDispatcher>();
    services.TryAddSingleton<ICapabilityAuditStore, InMemoryCapabilityAuditStore>();
    services.TryAddSingleton<ICapabilityDescriptorResolver, DefaultCapabilityDescriptorResolver>();
    services.TryAddSingleton<ICapabilityProfileResolver, DefaultCapabilityProfileResolver>();

    // 内部组件
    services.TryAddSingleton<ICapabilityVersionResolver, DefaultCapabilityVersionResolver>();

    // ICapabilityHandlerRegistry 由 Source Generator 注册（GeneratedCapabilityHandlerRegistry）
    // 无需在此手动注册

    // Validators
    services.AddSingleton<IRegistryValidator<CapabilityDescriptor>, CapabilityHandlerValidator>();
    services.AddSingleton<IRegistryValidator<CapabilityDescriptor>, CapabilitySchemaValidator>();

    // AuditMiddleware（最外层）
    services.AddTransient<AuditMiddleware>();

    // Options
    if (configure is not null)
        services.Configure(configure);

    return services;
}
```

### 11.2 CapabilityRuntimeOptions

```csharp
public sealed class CapabilityRuntimeOptions
{
    public bool EnableAudit { get; init; } = true;
    public bool EnableMetrics { get; init; } = true;
}
```

---

## 12. Execution Flow

### 12.1 完整调用链

```text
External Caller (HTTP / Agent / Workflow / HumanTask)
         ↓
    ICapabilityDescriptorResolver.Resolve(name)
         ↓
    CapabilityDescriptor (完整，含版本/Schema/Permissions)
         ↓
    ICapabilityDispatcher.DispatchAsync(name, input, configureContext)
         ↓
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
| `Capability.Abstractions/ICapabilityDescriptorResolver.cs` | 外部唯一解析入口 |
| `Capability.Abstractions/CapabilityNotFoundException.cs` | 解析失败异常 |
| `Capability.Abstractions/ICapabilityCatalog.cs` | 浏览/发现接口 |
| `Metadata.Abstractions/ICapabilityHandlerRegistry.cs` | Source Generator handler 注册表接口 |
| `Capability/Internal/ICapabilityVersionResolver.cs` | 内部版本解析 |
| `Capability/Internal/DefaultCapabilityVersionResolver.cs` | 版本解析实现 |
| `Capability/CapabilityDispatcher.cs` | Dispatcher 实现 |
| `Capability/InMemoryCapabilityAuditStore.cs` | InMemory Audit |
| `Capability/DefaultCapabilityDescriptorResolver.cs` | 解析实现 |
| `Capability/Middleware/AuditMiddleware.cs` | 最外层 Audit |
| `Metadata.Abstractions/CapabilityKind.cs` | 枚举迁移 |
| `Metadata.Abstractions/CapabilityRiskLevel.cs` | 枚举迁移 |
| `Metadata/CapabilityHandlerValidator.cs` | Bootstrap 验证 |
| `Metadata/CapabilitySchemaValidator.cs` | Bootstrap 验证 |

### 13.2 Modified Files

| File | Change |
|---|---|
| `Metadata/CapabilityDescriptor.cs` | 合并运行时属性 |
| `Metadata/CapabilityRegistry.cs` | 实现 ICapabilityRegistry + ICapabilityCatalog |
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
| CapabilityRegistryTests | RegistryBase + ICapabilityRegistry + ICapabilityCatalog |
| CapabilityDispatcherTests | 版本解析、Profile 配置、Context 注入 |
| CapabilityDescriptorResolverTests | Resolve by name、Resolve by name:version |
| CapabilityHandlerValidatorTests | 1:1 映射验证、缺失/重复检测 |
| CapabilitySchemaValidatorTests | Schema 引用验证 |
| InMemoryCapabilityAuditStoreTests | RecordAsync 存储 |
| AuditMiddlewareTests | try/finally 记录、异常记录 |
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
| 47 | Aliases 不放在 Descriptor | 路由关注点，属于 Dispatcher 层 |
| 48 | IsDeprecated 依赖 DescriptorState | 不新增属性，VersionResolver 规范明确 State == Active |
| 49 | ICapabilityRegistry 保留在 Capability.Abstractions | 领域特化接口，不污染 Metadata.Abstractions |
| 50 | CapabilityRegistry 直接实现 ICapabilityCatalog | 不新增 DefaultCapabilityCatalog |
| 51 | Resolver 与 Catalog 均为 Registry 只读适配器 | 二者不得互相依赖 |
| 52 | ICapabilityVersionResolver 内部化 | 外部唯一入口是 ICapabilityDescriptorResolver |
| 53 | 无 DispatchManyAsync | 批量执行是 Workflow Runtime 的责任 |
| 54 | InvocationSource 为强类型字段 | 避免魔法字符串 |
| 55 | AuditRecord 含 ExecutionId | 区分同 CorrelationId 的多次执行 |
| 56 | AuditRecord 含 CapabilityId | 稳定标识，不受 Rename 影响 |
| 57 | AuditStore 无 QueryAsync | 查询属于未来 Audit Repository 阶段 |
| 58 | AuditMiddleware 最外层 | 确保所有结果（含失败）都被记录 |
| 59 | MetricsMiddleware 包裹 Handler | 统计真实执行时间 |
| 60 | CapabilityHandlerValidator 验证 1:1 映射 | 防止 Handler 歧义 |
| 61 | Validator 验证 Source Generator 输出 | 不依赖运行时 DI |
| 62 | CapabilityQuery 延迟到 Phase 6 | Phase 4 是 Execution Runtime，不是 Discovery Runtime |
