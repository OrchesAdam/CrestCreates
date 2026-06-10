# 统一元数据模型 — 使用指南

> 本文档面向 CrestCreates 模块开发者，介绍如何使用统一元数据模型声明和执行业务能力。
> *更新于 Phase 4 (2026-06-10): 加入 CapabilityDispatcher, ICapabilityResolver, AuditMiddleware, CapabilityProfile 移至 Metadata*

---

## 1. 快速开始

### 1.1 定义一个 Schema

```csharp
using CrestCreates.Schema.Abstractions;

public class CustomerSchemaProvider : ISchemaDescriptorProvider
{
    public SchemaDescriptor GetSchemaDescriptor() => new()
    {
        Id = "schema_customer",
        Name = "CustomerInput",
        Version = 1,
        State = DescriptorState.Active,
        Fields = new List<SchemaFieldDescriptor>
        {
            new() { Name = "Name", FieldType = "string", IsRequired = true, MaxLength = 100 },
            new() { Name = "Email", FieldType = "string", IsRequired = true, Pattern = @"^[^@]+@[^@]+$" },
            new() { Name = "Age", FieldType = "int", IsNullable = true, MinValue = 0, MaxValue = 150 }
        }
    };
}
```

Schema 注册由 source generator 自动完成 — 实现 `ISchemaDescriptorProvider` 即可。

### 1.2 定义一个 Capability

> ⚠️ **Phase 4 更新:** `CapabilityDescriptor` 已从 `CrestCreates.Capability.Abstractions` 移至 `CrestCreates.Metadata`。`Permission` 改为 `Permissions` (IReadOnlyList<string>)。`InputSchema`/`OutputSchema` 现在是可空类型。

```csharp
using CrestCreates.Metadata;  // CapabilityDescriptor 现在在 Metadata 中
using CrestCreates.Metadata.Abstractions;

public class CreateCustomerCapability : ICapabilityProvider
{
    public CapabilityDescriptor GetCapabilityDescriptor() => new()
    {
        Id = "cap_create_customer",
        Name = "crm.customer.create",
        Version = 1,
        CapabilityKind = CapabilityKind.Command,
        InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_customer", 1),
        OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_customer_output", 1),
        Permissions = new[] { "Customer.Create" },  // 改为复数 + 数组
        RiskLevel = CapabilityRiskLevel.Medium,
        SemanticTags = new List<string> { "customer", "crm", "create" }
    };
}
```

### 1.3 实现一个 Handler

```csharp
using CrestCreates.Capability.Abstractions;

public class CreateCustomerHandler : ICapabilityHandler<CustomerInput, CustomerOutput>
{
    public async Task<CustomerOutput> ExecuteAsync(CustomerInput input, CancellationToken ct)
    {
        // 业务逻辑
        return new CustomerOutput { CustomerId = Guid.NewGuid(), Name = input.Name };
    }
}
```

Handler 注册同样由 source generator 自动完成 — 实现 `ICapabilityHandler<TInput, TOutput>` 即自动被发现。

### 1.4 执行一个 Capability

**推荐方式：使用 `ICapabilityDispatcher`**（Phase 4 新增统一门面 — 自动注入 InvocationSource + TenantId/UserId）：

```csharp
// 注入 ICapabilityDispatcher
var result = await dispatcher.DispatchAsync("crm.customer.create", InvocationSource.Http, input: new
{
    Name = "John Doe",
    Email = "john@example.com",
    Age = 30
});

if (result.IsSuccess)
    Console.WriteLine($"Output: {result.Output}");
else
    Console.WriteLine($"Error: {result.ErrorCode} — {result.ErrorMessage}");
```

**或直接使用 `ICapabilityPipeline`：**

```csharp
var result = await pipeline.ExecuteAsync("crm.customer.create", input: new
{
    Name = "John Doe",
    Email = "john@example.com"
});
```

---

## 2. 定义 Event

```csharp
using CrestCreates.Event.Abstractions;

public class CustomerCreatedEventProvider : IEventDescriptorProvider
{
    public EventDescriptor GetEventDescriptor() => new()
    {
        Id = "evt_customer_created",
        Name = "crm.customer.created",
        Version = 1,
        PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_customer_output", 1),
        Category = EventCategory.Domain,
        Semantic = EventSemantic.Fact,
        Importance = EventImportance.Critical
    };
}
```

---

## 3. 定义 Workflow

```csharp
using CrestCreates.Workflow.Abstractions;

public class OnboardingWorkflowProvider : IWorkflowDescriptorProvider
{
    public WorkflowDescriptor GetWorkflowDescriptor() => new()
    {
        Id = "wf_onboarding",
        Name = "employee.onboarding",
        Version = 1,
        Steps = new List<WorkflowStep>
        {
            new()
            {
                Id = "step_create_account",
                Name = "创建账号",
                Target = new CapabilityTarget
                {
                    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_create_account", 1)
                },
                OnError = StepErrorBehavior.Compensate
            },
            new()
            {
                Id = "step_manager_approval",
                Name = "经理审批",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_manager_approval", 1)
                }
            },
            new()
            {
                Id = "step_send_notification",
                Name = "发送通知",
                Target = new CapabilityTarget
                {
                    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_send_notification", 1)
                }
            }
        }
    };
}
```

执行 Workflow：

```csharp
var instance = await engine.ExecuteAsync("employee.onboarding", new Dictionary<string, object?>
{
    ["EmployeeId"] = "emp_001",
    ["DepartmentId"] = "dept_eng"
});

if (instance.Status == WorkflowInstanceStatus.Completed)
    Console.WriteLine("入职流程完成");
else
    Console.WriteLine($"工作流失败: {instance.ErrorMessage}");
```

---

## 4. 定义 Form 和 HumanTask

```csharp
// Form = Schema + UI metadata
public class EmployeeFormProvider : IFormDescriptorProvider
{
    public FormDescriptor GetFormDescriptor() => new()
    {
        Id = "form_employee",
        Name = "EmployeeForm",
        Version = 1,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_employee", 1),
        Fields = new List<FormFieldDescriptor>
        {
            new() { SchemaFieldName = "Name", Label = "姓名", Order = 0 },
            new() { SchemaFieldName = "Email", Label = "邮箱", Order = 1 },
            new() { SchemaFieldName = "Department", Label = "部门", Order = 2, IsReadOnly = true }
        }
    };
}

// HumanTask = Form + 业务操作
public class ManagerApprovalProvider : IHumanTaskDescriptorProvider
{
    public HumanTaskDescriptor GetHumanTaskDescriptor() => new()
    {
        Id = "ht_manager_approval",
        Name = "manager.approval",
        Version = 1,
        Form = new VersionedDescriptorRef<FormDescriptor>("form_employee", 1),
        AssigneeStrategy = AssigneeStrategy.CandidateGroup,
        Timeout = TimeSpan.FromHours(24),
        Outcomes = new List<CompletionOutcome>
        {
            new()
            {
                Condition = CompletionCondition.Approve,
                Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_send_notification", 1)
            }
        }
    };
}
```

---

## 5. 注册和配置

### 5.1 DI 注册

```csharp
// Program.cs — 推荐使用 AddCapabilityRuntime 一次性注册所有运行时组件
services.AddCapabilityRuntime();  // 注册 Dispatcher, Resolver, Audit, Bootstrap Validators, Pipeline

// 或单独配置 Pipeline
services.AddCapabilityPipeline(options =>
{
    options.Use<CustomAuditMiddleware>();  // 自定义审计中间件位置
});

// AuditMiddleware 已默认在最外层，替代 NullCapabilityAuditStore：
services.AddInMemoryCapabilityAudit();  // 切换到内存审计存储

services.AddWorkflowEngine();
```

**Pipeline 中间件顺序（由内到外）：**
```
Handler → EventPublishing → Metrics → Idempotency → Validation → Authorization → Tenant → RateLimit → Audit
```

### 5.2 CapabilityProfile — 环境/Tenant 级别覆盖

> ⚠️ **Phase 4 更新:** `CapabilityProfile` 已移至 `CrestCreates.Metadata`。

```csharp
using CrestCreates.Metadata;

var profiles = new[]
{
    new CapabilityProfile
    {
        Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_create_customer", 1),
        Scope = "Global-Prod",
        Timeout = TimeSpan.FromSeconds(5)
    }
};
```

解析优先级: `Tenant > Environment > Global > 默认值`

### 5.3 Tenant 隔离

```csharp
// Draft store 自动按 Tenant 隔离
services.AddSingleton<IDraftStore>(sp =>
    new TenantIsolatedDraftStore(
        new InMemoryDraftStore(),
        sp.GetRequiredService<ITenantContext>()));

// Descriptor registry 按 Tenant 过滤
var scopedRegistry = new TenantScopedRegistry<CapabilityDescriptor>(
    capabilityRegistry,
    tenantContext,
    descriptor => /* tenant selector */);
```

---

## 6. 自定义中间件

```csharp
public class CustomLoggingMiddleware : ICapabilityPipelineMiddleware
{
    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        Console.WriteLine($"Executing: {context.CapabilityName}");

        var result = await next(context);

        Console.WriteLine($"Result: {result.Status}, Duration: {result.Duration.TotalMilliseconds}ms");
        return result;
    }
}

// 注册
services.AddCapabilityPipeline(cfg => cfg.Use<CustomLoggingMiddleware>());
```

---

## 7. 手动注册 Handler (不依赖 source-gen)

> ⚠️ **Phase 4 更新:** `CapabilityRegistry` 现在使用 `Build(providers)` 模式，不再有 `Register()` 方法。

```csharp
// 方式 1: DelegateHandlerInvoker — AOT 安全
var resolver = new CapabilityHandlerResolver();
resolver.Register("crm.customer.create",
    new DelegateHandlerInvoker(async (input, ct) =>
    {
        var handler = new CreateCustomerHandler();
        return await handler.ExecuteAsync((CustomerInput)input!, ct);
    }));

// 方式 2: 通过 ICapabilityDispatcher + ICapabilityResolver
// (推荐 — 自动处理 Tenant/User 上下文)
var dispatcher = serviceProvider.GetRequiredService<ICapabilityDispatcher>();
var result = await dispatcher.DispatchAsync("crm.customer.create", InvocationSource.System, input);
```

---

## 8. 直接使用 Registry

### 8.1 通用 RegistryBase API

所有 Registry（Event、Capability、Workflow 等）共享 `RegistryBase<T>` 的通用 API：

```csharp
// 通用查询（所有 Registry 都支持）
var cap = capabilityRegistry.GetById("cap_create_customer");         // Id 查找（所有 Registry）
var caps = capabilityRegistry.GetByName("crm.customer.create");      // Name 查找（所有 Registry）
var all = capabilityRegistry.GetAll();                                // 全部
var specific = capabilityRegistry.GetByVersion("cap_create_customer", 2);  // Id + Version 精确查找 (Phase 4 新增)

// CapabilityRegistry 特定查询 (Phase 4 新增)
var commands = capabilityRegistry.GetByKind(CapabilityKind.Command);     // 按类型筛选
var customerCaps = capabilityRegistry.GetByTag("customer");              // 按语义标签筛选
```

### 8.2 Registry 构建

Registry 通过 `IDescriptorProvider<T>` + `Build(providers)` 模式构建（替代旧的 `Register()` 方法）：

```csharp
// 手动构建（不推荐，通常由 BootstrapCoordinator 自动完成）
var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
var registry = new CapabilityRegistry(engine);
registry.Build([new MyCapabilityProvider()]);  // IDescriptorProvider<CapabilityDescriptor>[]

// 通过 DI 注入的 Registry 已在启动时自动构建
```

### 8.3 验证管道

```csharp
// 验证器自动执行，收集所有错误
var engine = new RegistryValidationEngine<EventDescriptor>([
    new EventVersionChainValidator(),
    new DuplicateNameVersionValidator(),
    new UniquePayloadTypeValidator()
]);

// Build 时自动验证，失败抛出 RegistryValidationException
try
{
    registry.Build(providers);
}
catch (RegistryValidationException ex)
{
    foreach (var issue in ex.Issues)
        Console.WriteLine($"{issue.Severity}: {issue.Message}");
}
```

### 8.4 EventRegistry 特定查询

```csharp
// EventRegistry 保留了 IEventMetadataProvider 的特定查询
var activeVersion = eventRegistry.GetByName("crm.customer.created");  // Active only
var latest = eventRegistry.GetLatestVersion("crm.customer.created");
var allVersions = eventRegistry.GetAllVersions("crm.customer.created");
var byPayload = eventRegistry.GetByPayloadType(typeof(CustomerCreated));
```

### 8.5 统一解析器

```csharp
// 通过 IDescriptorResolver 统一解析，避免注入多个 Registry
var resolver = serviceProvider.GetRequiredService<IDescriptorResolver>();

var event = resolver.Resolve<GeneratedEventDescriptor>("user.created");
var cap = resolver.Resolve<CapabilityDescriptor>(new DescriptorRef("capability", "approval"));

// Phase 5~7: 高级查询
var results = resolver.Query<CapabilityDescriptor>(new DescriptorQuery
{
    Categories = ["HumanTask"],
    SemanticTags = ["approval"]
});
```

### 8.6 依赖分析

```csharp
// 依赖分析（CrestCreates.Metadata）
var dependents = catalog.FindDependents("schema_customer");
var impact = catalog.AnalyzeImpact("schema_customer", fromVersion: 1, toVersion: 2);
```

---

## 9. Draft 操作

```csharp
var draft = new DraftRecord
{
    DraftId = "draft_001",
    DraftType = "customer.create",
    Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_customer", 1),
    TenantId = "tenant_01",
    PayloadJson = """{"Name":"John","Email":"john@example.com"}"""
};

await draftStore.SaveAsync(draft);

// 查询
var activeDrafts = await draftStore.QueryAsync(new DraftQuery
{
    TenantId = "tenant_01",
    Status = DraftStatus.Active,
    MaxResults = 10
});
```

---

## 10. 暴露为 HTTP/Agent/MCP

> ⚠️ **Phase 4 更新:** `VersionedDescriptorRef<CapabilityDescriptor>` 改为 `VersionedDescriptorRef<IVersionedDescriptor>`（避免循环依赖）。

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Exposure.Abstractions;

// Agent Tool
var agentTool = new AgentToolDescriptor
{
    Id = "tool_create_customer",
    Name = "create_customer",
    Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_create_customer", 1),
    Description = "创建新客户记录",
    ToolCallMode = ToolCallMode.Auto,
    Tags = new List<string> { "customer", "crm" }
};

// HTTP Endpoint
var endpoint = new CapabilityEndpointDescriptor
{
    Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_create_customer", 1),
    RoutePattern = "/api/customers",
    RequireAuthorization = true
};
```

---

## 11. 创建自定义 Registry

使用 `RegistryBase<T>` 创建新的 Registry：

```csharp
// 1. 定义 Descriptor
public sealed class MyDescriptor : IDescriptor, IVersionedDescriptor
{
    public string Namespace { get; init; } = "my";
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    // 自定义字段...
}

// 2. 创建 Registry
public sealed class MyRegistry : RegistryBase<MyDescriptor>
{
    protected override string RegistryNamespace => "my";

    public MyRegistry(IRegistryValidationEngine<MyDescriptor> validationEngine)
        : base(validationEngine) { }

    protected override RegistrySnapshot<MyDescriptor> BuildSnapshot(List<MyDescriptor> descriptors)
    {
        var byId = descriptors.ToFrozenDictionary(d => d.Id, d => d);
        var byName = descriptors.GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());
        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        return new RegistrySnapshot<MyDescriptor>(
            byId, byName, byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }
}

// 3. 创建验证器（可选）
public sealed class MyValidator : IRegistryValidator<MyDescriptor>
{
    public int Order => 100;
    public ValidationReport Validate(IReadOnlyList<MyDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();
        // 验证逻辑...
        return new ValidationReport(issues);
    }
}

// 4. DI 注册
services.AddSingleton<IRegistryValidator<MyDescriptor>, MyValidator>();
services.AddSingleton<IRegistryValidationEngine<MyDescriptor>, RegistryValidationEngine<MyDescriptor>>();
services.AddSingleton<MyRegistry>();
```

---

## 12. 参考

| 文档 | 位置 |
|------|------|
| 设计规格书 | `docs/superpowers/specs/2026-06-08-unified-metadata-model-design.md` |
| Phase 3 设计规格书 | `docs/superpowers/specs/2026-06-09-phase-3-metadata-runtime-foundation-design.md` |
| Phase 4 设计规格书 | `docs/superpowers/specs/2026-06-09-phase-4-capability-runtime-consolidation-design.md` |
| Phase 3 实现计划 | `docs/superpowers/plans/2026-06-09-phase-3-metadata-runtime-foundation.md` |
| Phase 4 实现计划 | `docs/superpowers/plans/2026-06-10-phase-4-capability-runtime-consolidation.md` |
| 架构总结 | `docs/Feature/UnifiedMetadataModel/2026-06-09-unified-metadata-model-architecture-summary.md` |

### 关键接口一览

| 接口 | 用途 | 位置 |
|------|------|------|
| `IDescriptor` | 所有描述符基础接口 | Metadata.Abstractions |
| `IVersionedDescriptor` | 版本化描述符 | Metadata.Abstractions |
| `IHasContractIdentity` | 兼容性身份 | Metadata.Abstractions |
| `IRelationshipAwareDescriptor` | 自描述关系 | Metadata.Abstractions |
| `RegistryBase<T>` | 通用注册表基类 | Metadata |
| `RegistrySnapshot<T>` | 不可变快照 | Metadata |
| `IRegistryValidator<T>` | 可插拔验证器 | Metadata.Abstractions |
| `IRegistryValidationEngine<T>` | 验证引擎 | Metadata.Abstractions |
| `IDescriptorProvider<T>` | 描述符提供者 | Metadata.Abstractions |
| `IDescriptorResolver` | 统一解析器 | Metadata.Abstractions |
| `IBootstrapTask` | 启动任务接口 | Metadata.Abstractions |
| `BootstrapCoordinator` | 拓扑排序协调器 | Metadata |
| `ICapabilityRegistry` | Capability 注册表 (Id/Name/Tag/Kind 查询) | Metadata |
| `ICapabilityResolver` | Capability 统一解析入口 (Id-first) | Metadata |
| `ICapabilityDispatcher` | Capability 统一执行门面 (注入 Tenant/User 上下文) | Metadata |
| `ICapabilityAuditStore` | 审计存储契约 (InMemory/Null) | Capability.Abstractions |
| `ICapabilityPipeline` | Capability 执行流水线 | Capability.Abstractions |
| `ICapabilityHandlerInvoker` | 零反射 Handler 调用器 | Capability.Abstractions |
| `IBootstrapValidator` | 启动阶段验证器 | Metadata.Abstractions |
| `IDescriptorLookup` | 跨 Registry 描述符查找 | Metadata.Abstractions |
| `ICapabilityHandlerRegistry` | Handler 注册表 | Metadata.Abstractions |
| `ISchemaDescriptorProvider` | 声明 Schema | Schema.Abstractions |
| `ICapabilityProvider` | 声明 Capability | Capability.Abstractions |
| `ICapabilityHandler<TIn,TOut>` | 实现业务逻辑 | Capability.Abstractions |
| `IEventDescriptorProvider` | 声明 Event | Event.Abstractions |
| `IFormDescriptorProvider` | 声明 Form | Form.Abstractions |
| `IHumanTaskDescriptorProvider` | 声明 HumanTask | HumanTask.Abstractions |
| `IWorkflowDescriptorProvider` | 声明 Workflow | Workflow.Abstractions |
| `IWorkflowEngine` | 执行 Workflow | Workflow.Abstractions |
| `IDraftStore` | Draft CRUD | Draft.Abstractions |

所有 Provider 接口由 source generator 自动发现并注册，无需手动调用 Registry。
