# Phase 3: Metadata Runtime Foundation Design

> **Date:** 2026-06-09
> **Status:** Approved
> **Author:** CrestCreates Architecture Team

---

## 1. 设计背景与目标

### 1.1 Phase 2a 的遗产

Phase 2a（Event System Metadata Bridge）已经建立了一个功能完整的 Metadata Runtime 原型：

- **EventRegistry** — 基于 `FrozenDictionary` 的不可变快照
- **RegistryEventValidator** — 运行时验证（版本链、唯一性）
- **EventResolver** — 联合查询（Generated + Dynamic）
- **EventRegistryBootstrapper** — `IHostedService` 启动
- **DeadLetterMessage** — 17 字段增强 + `VersionKey`
- **IDeadLetterStore** — 统一 DLQ 抽象

### 1.2 核心洞察

> Phase 2a 的 EventRegistry 实际上已经是 Metadata Runtime v1。

EventRegistry 的设计模式（Build/Snapshot/Validation/Resolver/Bootstrapper）是所有 Registry 的通用模式。如果不提取基类，未来会出现：

```
EventRegistry      → Build/Snapshot/Validation/Resolver
CapabilityRegistry   → Build/Snapshot/Validation/Resolver
WorkflowRegistry     → Build/Snapshot/Validation/Resolver
HumanTaskRegistry    → Build/Snapshot/Validation/Resolver
```

**4 套重复代码，半年后开始痛苦。**

### 1.3 Phase 3 目标

将 EventRegistry 提炼为所有 Registry 的母体，建立 Crest Framework 的底层元数据内核。

---

## 2. 架构设计

### 2.1 核心抽象层

```csharp
/// <summary>
/// 所有描述符的底层接口。
/// 不假设 Version 存在 —— FormDescriptor、HumanTaskDescriptor 等可能无版本。
/// Namespace + Id = Global Identity
/// </summary>
public interface IDescriptor
{
    /// <summary>
    /// 命名空间。用于区分不同 Registry 中的同名描述符。
    /// 例如："event", "capability", "workflow"
    /// </summary>
    string Namespace { get; }

    /// <summary>
    /// 全局唯一标识符。格式：{Namespace}.{Name}
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 人类可读名称。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 兼容性哈希。用于判断两个描述符版本是否兼容。
    /// </summary>
    string ContractHash { get; }

    /// <summary>
    /// 实现哈希。用于判断描述符内容是否变化。
    /// </summary>
    string DefinitionHash { get; }
}

/// <summary>
/// 版本化描述符。EventDescriptor、CapabilityDescriptor 等有版本概念。
/// </summary>
public interface IVersionedDescriptor : IDescriptor
{
    int Version { get; }
}
```

### 2.2 注册表运行时

#### RegistryBase<TDescriptor>

```csharp
/// <summary>
/// 通用注册表基类。不依赖 Version，不假设 TKey。
/// 所有 Registry（Event/Capability/Workflow/HumanTask/Form）的母体。
/// </summary>
public abstract class RegistryBase<TDescriptor>
    where TDescriptor : IDescriptor
{
    protected RegistrySnapshot<TDescriptor>? _snapshot;
    protected readonly object _buildLock = new();
    public RegistryState State { get; protected set; } = RegistryState.Created;

    private readonly IRegistryValidationEngine<TDescriptor> _validationEngine;
    private readonly IEnumerable<IRegistryIndexBuilder<TDescriptor>> _indexBuilders;

    protected RegistryBase(
        IRegistryValidationEngine<TDescriptor> validationEngine,
        IEnumerable<IRegistryIndexBuilder<TDescriptor>>? indexBuilders = null)
    {
        _validationEngine = validationEngine;
        _indexBuilders = indexBuilders ?? Array.Empty<IRegistryIndexBuilder<TDescriptor>>();
    }

    /// <summary>
    /// 构建注册表。收集所有 Provider 的描述符，执行验证管道，构建快照。
    /// 验证失败时一次性报告所有错误，而不是逐个抛出。
    /// </summary>
    public void Build(IEnumerable<IDescriptorProvider<TDescriptor>> providers)
    {
        if (State == RegistryState.Built) return;
        
        lock (_buildLock)
        {
            if (State == RegistryState.Built) return;
            if (State == RegistryState.Failed)
                throw new InvalidOperationException(
                    "Registry.Build() previously failed. Restart required.");
            State = RegistryState.Building;
        }

        var descriptors = providers.SelectMany(p => p.GetDescriptors()).ToList();

        try
        {
            // 执行验证管道 —— 通过 ValidationEngine 收集所有错误，一次性报告
            var report = _validationEngine.Validate(descriptors);

            if (report.HasErrors)
            {
                throw new RegistryValidationException(report.Issues);
            }

            _snapshot = BuildSnapshot(descriptors);
            State = RegistryState.Built;
        }
        catch
        {
            State = RegistryState.Failed;
            throw;
        }
    }

    public TDescriptor? GetById(string id)
        => _snapshot?.ById.TryGetValue(id, out var d) == true ? d : null;

    public IReadOnlyList<TDescriptor> GetByName(string name)
        => _snapshot?.ByName.TryGetValue(name, out var versions) == true
            ? versions
            : Array.Empty<TDescriptor>();

    public IReadOnlyList<TDescriptor> GetAll()
        => _snapshot?.All ?? Array.Empty<TDescriptor>();

    /// <summary>
    /// 通过精确版本获取描述符。
    /// </summary>
    public TDescriptor? GetByVersion(string id, int version)
        => _snapshot?.ByVersion.TryGetValue(new DescriptorKey(id, version), out var d) == true ? d : null;

    protected abstract RegistrySnapshot<TDescriptor> BuildSnapshot(List<TDescriptor> descriptors);
}
```

#### RegistrySnapshot<TDescriptor>

```csharp
/// <summary>
/// 通用注册表快照。支持三种索引方式：
/// - ById: Canonical Descriptor（无版本=唯一实例，有版本=最新 Active）
/// - ByName: 所有版本（用于版本链分析）
/// - ByVersion: 精确版本（用于 Metadata Authoring）
/// - CustomIndexes: 额外索引（ContractHash、DefinitionHash 等）
/// </summary>
public sealed record RegistrySnapshot<TDescriptor>(
    FrozenDictionary<string, TDescriptor> ById,
    FrozenDictionary<string, ImmutableArray<TDescriptor>> ByName,
    FrozenDictionary<DescriptorKey, TDescriptor> ByVersion,
    ImmutableArray<TDescriptor> All,
    FrozenDictionary<string, IReadOnlyList<TDescriptor>> CustomIndexes)
    where TDescriptor : IDescriptor;

/// <summary>
/// 索引构建器。允许 Registry 构建额外索引（如 ContractHash、DefinitionHash）。
/// </summary>
public interface IRegistryIndexBuilder<TDescriptor>
    where TDescriptor : IDescriptor
{
    string IndexName { get; }
    FrozenDictionary<string, IReadOnlyList<TDescriptor>> BuildIndex(IReadOnlyList<TDescriptor> descriptors);
}

/// <summary>
/// 描述符的精确版本键。
/// </summary>
public readonly record struct DescriptorKey(string Id, int Version);
```

### 2.3 验证管道

```csharp
/// <summary>
/// 验证问题。
/// </summary>
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Message);

/// <summary>
/// 验证报告。收集所有验证器的问题，一次性报告。
/// </summary>
public sealed record ValidationReport(
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(i => i.Severity == ValidationSeverity.Error);
    public bool HasWarnings => Issues.Any(i => i.Severity == ValidationSeverity.Warning);
    public static ValidationReport Empty => new(Array.Empty<ValidationIssue>());
    public static ValidationReport FromIssues(params ValidationIssue[] issues) => new(issues);
}

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// 验证引擎。负责协调所有验证器，收集并汇总验证结果。
/// 独立于 RegistryBase，可被 CLI、AI Explorer、Registry 等复用。
/// </summary>
public interface IRegistryValidationEngine<TDescriptor>
    where TDescriptor : IDescriptor
{
    /// <summary>
    /// 执行验证。收集所有验证器的问题，一次性报告。
    /// </summary>
    ValidationReport Validate(IReadOnlyList<TDescriptor> descriptors);
}

/// <summary>
/// 注册表验证器。可插拔，每个 Registry 可以挂载不同的验证器组合。
/// </summary>
public interface IRegistryValidator<TDescriptor>
{
    /// <summary>
    /// 验证器执行顺序。数字越小越早执行。
    /// </summary>
    int Order { get; }

    /// <summary>
    /// 验证描述符集合。
    /// </summary>
    ValidationReport Validate(IReadOnlyList<TDescriptor> descriptors);
}
```

#### EventRegistry 专用验证器

```csharp
/// <summary>
/// 版本链验证器：确保每个 Name 有且仅有一个 Active 版本，且 Active 必须是最高版本。
/// </summary>
public sealed class VersionChainValidator : IRegistryValidator<IVersionedDescriptor>
{
    public int Order => 100;

    public ValidationReport Validate(IReadOnlyList<IVersionedDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();
        // ... 实现版本链验证逻辑
        return new ValidationReport(issues);
    }
}

/// <summary>
/// 唯一性验证器：确保 (Name, Version) 不重复。
/// </summary>
public sealed class DuplicateNameVersionValidator : IRegistryValidator<IVersionedDescriptor>
{
    public int Order => 200;

    public ValidationReport Validate(IReadOnlyList<IVersionedDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();
        // ... 实现唯一性验证逻辑
        return new ValidationReport(issues);
    }
}
```

### 2.4 引用类型

```csharp
/// <summary>
/// 描述符引用接口。所有强类型引用（EventRef/CapabilityRef/WorkflowRef）都实现此接口。
/// 使 Resolver 可以统一处理所有引用类型。
/// </summary>
public interface IDescriptorRef
{
    string Id { get; }
    int? Version { get; }
}

/// <summary>
/// 底层引用抽象。Version = null 表示引用最新稳定版本。
/// </summary>
public readonly record struct DescriptorRef(string Id, int? Version = null) : IDescriptorRef;

/// <summary>
/// 事件引用。业务层强类型，防止误用。
/// </summary>
public readonly record struct EventRef(string Id, int? Version = null) : IDescriptorRef;

/// <summary>
/// 能力引用。
/// </summary>
public readonly record struct CapabilityRef(string Id, int? Version = null) : IDescriptorRef;

/// <summary>
/// 工作流引用。
/// </summary>
public readonly record struct WorkflowRef(string Id, int? Version = null) : IDescriptorRef;
```

### 2.5 解析器运行时

```csharp
/// <summary>
/// 统一描述符解析器。
/// 所有跨 Registry 引用都通过此接口解析，避免直接注入多个 Registry。
/// </summary>
public interface IDescriptorResolver
{
    /// <summary>
    /// Runtime Query — 通过 ID 获取最新版本。
    /// 使用频率最高的场景。
    /// </summary>
    TDescriptor? Resolve<TDescriptor>(string id)
        where TDescriptor : IDescriptor;

    /// <summary>
    /// Metadata Authoring — 通过精确引用获取指定版本。
    /// 需要明确版本时使用。
    /// </summary>
    TDescriptor? Resolve<TDescriptor>(IDescriptorRef reference)
        where TDescriptor : IDescriptor;
}
```

### 2.6 Bootstrap 运行时

```csharp
/// <summary>
/// Bootstrap 任务。不仅限于 Registry，未来 Schema/Projection/Cache/AI Index 都可以接入。
/// </summary>
public interface IBootstrapTask
{
    /// <summary>
    /// 任务类型。用于日志和诊断。
    /// </summary>
    Type ServiceType { get; }

    /// <summary>
    /// 依赖的其他 BootstrapTask 类型。
    /// </summary>
    IReadOnlyList<Type> Dependencies { get; }

    /// <summary>
    /// 是否必需。如果为 true，失败时终止启动；如果为 false，失败时记录警告继续。
    /// </summary>
    bool IsRequired { get; }

    /// <summary>
    /// 执行 Bootstrap。
    /// </summary>
    Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken ct);
}

/// <summary>
/// Bootstrap 协调器。
/// 使用拓扑排序确定启动顺序，支持循环依赖检测。
/// </summary>
public sealed class BootstrapCoordinator : IHostedService
{
    private readonly IEnumerable<IBootstrapTask> _tasks;
    private readonly ILogger<BootstrapCoordinator> _logger;

    public BootstrapCoordinator(
        IEnumerable<IBootstrapTask> tasks,
        ILogger<BootstrapCoordinator> logger)
    {
        _tasks = tasks;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // 拓扑排序
        var sorted = TopologicalSort(_tasks);

        foreach (var task in sorted)
        {
            _logger.LogInformation("Bootstrapping {TaskType}...", task.ServiceType.Name);
            await task.ExecuteAsync(/* ... */, ct);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// 拓扑排序 + 循环依赖检测。
    /// 检测到循环时抛出 BootstrapDependencyException，包含完整链路。
    /// </summary>
    private static IReadOnlyList<IBootstrapTask> TopologicalSort(IEnumerable<IBootstrapTask> tasks)
    {
        // 实现拓扑排序
        // 使用 DFS 检测循环，维护 Visited/Visiting 状态
        // 循环时抛出包含完整链路的 BootstrapDependencyException
        throw new NotImplementedException();
    }
}

/// <summary>
/// Bootstrap 依赖异常。包含完整的循环依赖链路。
/// </summary>
public sealed class BootstrapDependencyException : Exception
{
    public IReadOnlyList<Type> Cycle { get; }

    public BootstrapDependencyException(IReadOnlyList<Type> cycle)
        : base($"Bootstrap dependency cycle detected: {string.Join(" -> ", cycle.Select(t => t.Name))}")
    {
        Cycle = cycle;
    }
}
```

### 2.7 关系感知描述符

```csharp
/// <summary>
/// 关系感知描述符。描述符自身提供关系信息，供 Topology Engine 消费。
/// 避免 Descriptor 和 RelationshipProvider 两套信息源。
/// </summary>
public interface IRelationshipAwareDescriptor
{
    IEnumerable<DescriptorRelationship> GetRelationships();
}
```

### 2.8 关系提供者（Future Hook）

```csharp
/// <summary>
/// 描述符关系提供者。
/// 为未来的 Topology Engine 提供数据。
/// 如果描述符实现了 IRelationshipAwareDescriptor，优先从描述符自身获取关系。
/// </summary>
public interface IDescriptorRelationshipProvider
{
    IEnumerable<DescriptorRelationship> GetRelationships();
}

public sealed record DescriptorRelationship(
    DescriptorRef From,
    DescriptorRef To,
    RelationshipKind Kind);

public enum RelationshipKind
{
    Produces,
    Consumes,
    DependsOn,
    References
}
```

---

## 3. EventRegistry 迁移

### 3.1 迁移策略

**外部接口完全不变，内部实现迁移到 RegistryBase。**

```csharp
// 外部接口保持不变
public interface IEventRegistry
{
    RegistryState State { get; }
    void Build(IEnumerable<IEventDescriptorProvider> providers);
    GeneratedEventDescriptor? GetByName(string name);
    GeneratedEventDescriptor? GetByPayloadType(Type payloadType);
    GeneratedEventDescriptor? GetByNameAndVersion(string name, int version);
}

// 内部实现迁移到 RegistryBase
public sealed class EventRegistry : RegistryBase<GeneratedEventDescriptor>,
    IEventRegistry, IEventMetadataProvider
{
    public EventRegistry(IEnumerable<IRegistryValidator<GeneratedEventDescriptor>> validators)
        : base(validators)
    {
    }

    public GeneratedEventDescriptor? GetByPayloadType(Type payloadType)
        => _snapshot?.ByPayloadType.TryGetValue(payloadType, out var d) == true ? d : null;

    public GeneratedEventDescriptor? GetByNameAndVersion(string name, int version)
        => GetByVersion(name, version) as GeneratedEventDescriptor;

    protected override RegistrySnapshot<GeneratedEventDescriptor> BuildSnapshot(
        List<GeneratedEventDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Id, d.Version), d => d);

        var all = descriptors.ToImmutableArray();

        return new RegistrySnapshot<GeneratedEventDescriptor>(byId, byName, byVersion, all);
    }
}
```

### 3.2 验证器迁移

```csharp
// 旧验证器（内嵌在 EventRegistry 中）
// private static void ValidateVersionChain(List<GeneratedEventDescriptor> descriptors)

// 新验证器（可插拔，独立类）
public sealed class EventVersionChainValidator : IRegistryValidator<GeneratedEventDescriptor>
{
    public int Order => 100;

    public ValidationReport Validate(IReadOnlyList<GeneratedEventDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();
        // ... 从 EventRegistry 提取验证逻辑
        return new ValidationReport(issues);
    }
}
```

---

## 4. CapabilityRegistry

### 4.1 设计原则

**Phase 3 只做 Metadata，不做 Runtime。**

- ✅ `CapabilityDescriptor` — 描述符定义
- ✅ `CapabilityRegistry` — 注册表（基于 RegistryBase）
- ✅ `CapabilityRef` — 引用类型
- ❌ `CapabilityExecutor` — defer 到 Phase 4
- ❌ `CapabilityDispatcher` — defer 到 Phase 4
- ❌ `CapabilityAuthorization` — defer 到 Phase 4

### 4.2 CapabilityDescriptor

```csharp
public sealed class CapabilityDescriptor : IVersionedDescriptor
{
    // IDescriptor 通用字段
    public DescriptorKind Kind => DescriptorKind.Capability;
    public string Namespace { get; init; } = "capability";
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public string? SupersededById { get; init; }

    // Capability 特定 Metadata 字段
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<EventRef> Produces { get; init; } = Array.Empty<EventRef>();
    public IReadOnlyList<EventRef> Consumes { get; init; } = Array.Empty<EventRef>();
    public IReadOnlyList<string> SemanticTags { get; init; } = Array.Empty<string>();
}

public enum CapabilityKind
{
    HumanTask,
    Workflow,
    Integration,
    Notification
}
```

### 4.3 CapabilityRegistry

```csharp
public sealed class CapabilityRegistry : RegistryBase<CapabilityDescriptor>
{
    public CapabilityRegistry(
        IRegistryValidationEngine<CapabilityDescriptor> validationEngine,
        IEnumerable<IRegistryIndexBuilder<CapabilityDescriptor>>? indexBuilders = null)
        : base(validationEngine, indexBuilders)
    {
    }

    protected override RegistrySnapshot<CapabilityDescriptor> BuildSnapshot(
        List<CapabilityDescriptor> descriptors)
    {
        // ... 类似 EventRegistry 的 BuildSnapshot 实现
        throw new NotImplementedException();
    }
}
```

---

## 5. 项目结构

### 5.1 新增项目

| 项目 | 说明 |
|------|------|
| `CrestCreates.Metadata.Runtime` | RegistryBase、RegistrySnapshot、Validation、Bootstrap、Resolver |
| `CrestCreates.Capability` | CapabilityDescriptor、CapabilityRegistry、CapabilityRef |

### 5.2 修改项目

| 项目 | 修改 |
|------|------|
| `CrestCreates.Event` | EventRegistry 内部迁移到 RegistryBase |
| `CrestCreates.Event.Abstractions` | 保持 IEventRegistry 不变 |

---

## 6. 验收标准

### 6.1 功能验收

| 验收项 | 标准 |
|--------|------|
| EventRegistry 迁移 | 内部继承 RegistryBase，外部接口不变 |
| CapabilityRegistry | 基于 RegistryBase 实现，能正确 Build/Query |
| 验证管道 | 支持多验证器，一次性报告所有错误 |
| Bootstrap 协调器 | 支持拓扑排序，检测循环依赖 |
| DescriptorResolver | 支持 ID 查询和精确版本查询 |
| 向后兼容 | 所有现有测试通过，无需修改调用方 |

### 6.2 架构验收

| 验收项 | 标准 |
|--------|------|
| 无 Event 特定代码泄漏到 Metadata.Runtime | RegistryBase 不依赖 Event 类型 |
| 无 Version 假设 | RegistryBase 接受 IDescriptor 和 IVersionedDescriptor |
| 可扩展性 | 新增 Registry 只需实现 BuildSnapshot |
| 可测试性 | 每个组件可独立单元测试 |

---

## 7. 未来路线图

### Phase 4 — Capability Runtime

- CapabilityExecutor
- CapabilityDispatcher
- CapabilityAuthorization
- CapabilityLifecycle

### Phase 5 — Workflow Runtime

- WorkflowRegistry
- WorkflowExecutor
- WorkflowStateMachine

### Phase 6 — Metadata Topology Engine

- 跨 Registry 关系图
- Workflow → Capability → Event → HumanTask 可视化

### Phase 7 — AI Runtime

- Metadata 驱动的 AI 决策
- 自动事件路由
- 智能重试策略

---

## 8. 附录

### 8.1 术语表

| 术语 | 定义 |
|------|------|
| Descriptor | 元数据描述符，描述系统中某个实体的元信息 |
| Registry | 描述符的注册表，负责收集、验证、索引描述符 |
| Snapshot | 注册表的不可变快照，基于 FrozenDictionary |
| Validator | 验证器，检查描述符集合的合法性 |
| Bootstrap | 启动时初始化注册表的过程 |
| Resolver | 解析器，通过引用获取描述符实例 |
| Ref | 描述符引用，包含 Id 和可选 Version |

### 8.2 设计决策记录

#### ADR-001: RegistryBase 不依赖 Version

**状态：** 已接受

**上下文：** EventRegistry 假设所有描述符都有 Version。但 FormDescriptor、HumanTaskDescriptor 等可能无版本。

**决策：** RegistryBase<TDescriptor> 的约束为 IDescriptor，不是 IVersionedDescriptor。版本验证作为可插拔验证器。

**后果：**
- ✅ 更通用，支持无版本描述符
- ✅ 验证逻辑可复用
- ❌ 需要额外检查 Version 相关操作

#### ADR-002: DescriptorRef.Version 为 Optional

**状态：** 已接受

**上下文：** Runtime 查询通常只需要最新版本，Metadata Authoring 需要精确版本。

**决策：** DescriptorRef.Version 为 int?，null 表示 Latest Stable。

**后果：**
- ✅ Runtime 查询更简洁
- ✅ 避免 "999999" workaround
- ❌ 需要处理 null 语义

#### ADR-003: Bootstrap 与 Registry 解耦

**状态：** 已接受

**上下文：** Bootstrap 不仅用于 Registry，未来还用于 Schema/Projection/Cache/AI Index。

**决策：** IBootstrapTask 不依赖 IRegistry，Registry 只是其中一种 BootstrapTask。

**后果：**
- ✅ 更通用，支持非 Registry 启动任务
- ✅ 统一启动协调
- ❌ 需要额外抽象层

#### ADR-004: Registry Snapshot Immutability

**状态：** 已接受

**上下文：** RegistryBase 构建完成后，Snapshot 应该是不可变的。但未来可能出现 Hot Reload、Plugin Unload 等需求。

**决策：**
- RegistryBase.Build() 构建完成后，Snapshot 不可变
- 动态注册走 DynamicRegistry，不走 RegistryBase.Upsert()
- RegistryBase 不提供运行时更新 API

**后果：**
- ✅ 保证线程安全
- ✅ 简化并发模型
- ✅ 明确区分静态注册表和动态注册表
- ❌ 需要额外设计 DynamicRegistry

#### ADR-005: ById = Canonical Descriptor

**状态：** 已接受

**上下文：** RegistrySnapshot.ById 的语义不明确。无版本 Registry 中 ById 是唯一实例，有版本 Registry 中 ById 是最新 Active 版本。

**决策：**
- ById 永远表示 Canonical Descriptor
- 无版本 Registry：ById 指向唯一实例
- 有版本 Registry：ById 指向最新 Active 版本
- 精确版本查询使用 ByVersion 索引

**后果：**
- ✅ Resolver 语义统一
- ✅ 避免歧义
- ❌ 需要文档明确

#### ADR-006: DescriptorRef vs DescriptorKey

**状态：** 已接受

**上下文：** DescriptorRef 允许 Version 为 null（逻辑引用），DescriptorKey 要求 Version 必须存在（物理定位）。

**决策：**
- DescriptorRef：逻辑引用，Version = null 表示 Latest Stable
- DescriptorKey：物理定位，必须提供精确 Version
- 不允许 new DescriptorKey(id, null)

**后果：**
- ✅ 区分逻辑引用和物理定位
- ✅ 防止误用
- ❌ 需要额外类型

#### ADR-007: DynamicRegistry Future Hook

**状态：** 已接受

**上下文：** ADR-004 明确 RegistryBase 不可变，但 Phase 2a 已有 DynamicEventRegistry。

**决策：**
- RegistryBase：静态注册表，Build 后不可变
- IDynamicRegistry<TDescriptor>：动态注册表，支持运行时注册/注销
- 两者共存，不合并

```csharp
public interface IDynamicRegistry<TDescriptor>
    where TDescriptor : IDescriptor
{
    bool TryRegister(TDescriptor descriptor);
    bool TryUnregister(string id);
}
```

**后果：**
- ✅ 明确区分静态和动态注册表
- ✅ 为 Hot Reload 预留扩展点
- ❌ 需要维护两套 API

#### ADR-008: Descriptor Identity Model

**状态：** 已接受

**上下文：** 随着 Registry 类型增多（Event/Capability/Workflow/HumanTask/Form/Entity），不同 Registry 可能出现同名描述符。需要统一的身份模型。

**决策：**
- Namespace + Id = Global Identity
- ContractHash = Compatibility Identity（用于判断兼容性）
- DefinitionHash = Implementation Identity（用于判断内容变化）

```
Identity Model:
  Namespace + Id           → Global Identity
  ContractHash             → Compatibility Identity
  DefinitionHash           → Implementation Identity
```

**后果：**
- ✅ 避免跨 Registry 命名冲突
- ✅ 支持兼容性判断和版本迁移
- ✅ 为 AI Runtime 提供身份基础
- ❌ 需要所有描述符实现 Namespace

#### ADR-009: RegistryBase 与 ValidationEngine 绑定

**状态：** 已接受

**上下文：** RegistryBase 需要验证描述符，但验证器是强类型的（IRegistryValidator<TDescriptor>）。

**决策：**
- RegistryBase 构造函数接收 IRegistryValidationEngine<TDescriptor>
- ValidationEngine 内部持有 IRegistryValidator<TDescriptor> 集合
- 避免 RegistryBase 直接依赖验证器集合

**后果：**
- ✅ 验证逻辑可复用（CLI、AI Explorer、Registry）
- ✅ RegistryBase 只依赖抽象接口
- ❌ 需要额外实现 ValidationEngine
