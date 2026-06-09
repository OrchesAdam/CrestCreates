# 统一元数据模型 — 使用指南

> 本文档面向 CrestCreates 模块开发者，介绍如何使用统一元数据模型声明和执行业务能力。

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

```csharp
using CrestCreates.Capability.Abstractions;

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
        Permission = "Customer.Create",
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

```csharp
// 注入 ICapabilityPipeline
var result = await pipeline.ExecuteAsync("crm.customer.create", input: new
{
    Name = "John Doe",
    Email = "john@example.com",
    Age = 30
});

if (result.IsSuccess)
{
    var output = result.Output;  // CustomerOutput
}
else
{
    Console.WriteLine($"Error: {result.ErrorCode} — {result.ErrorMessage}");
}
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
// Program.cs
services.AddCapabilityPipeline(options =>
{
    // 生产环境自定义配置
    options.Use<CustomAuditMiddleware>();
});

services.AddWorkflowEngine();
```

### 5.2 CapabilityProfile — 环境/Tenant 级别覆盖

```csharp
var profiles = new[]
{
    new CapabilityProfile
    {
        Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_create_customer", 1),
        Scope = "Global-Prod",
        Timeout = TimeSpan.FromSeconds(5)
    },
    new CapabilityProfile
    {
        Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_create_customer", 1),
        Scope = "Tenant:VIP",
        Timeout = TimeSpan.FromSeconds(3)
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

```csharp
// 使用 DelegateHandlerInvoker — AOT 安全
var resolver = new CapabilityHandlerResolver();
resolver.Register("crm.customer.create",
    new DelegateHandlerInvoker(async (input, ct) =>
    {
        var handler = new CreateCustomerHandler();
        return await handler.ExecuteAsync((CustomerInput)input!, ct);
    }));
```

---

## 8. 直接使用 Registry

### 8.1 通用 RegistryBase API

所有 Registry（Event、Capability、Workflow 等）共享 `RegistryBase<T>` 的通用 API：

```csharp
// 通用查询（所有 Registry 都支持）
var cap = capabilityRegistry.GetById("cap_create_customer");
var caps = capabilityRegistry.GetByName("crm.customer.create");
var all = capabilityRegistry.GetAll();
var specific = capabilityRegistry.GetByVersion("cap_create_customer", 2);
```

### 8.2 Registry 构建

Registry 通过 `IBootstrapTask` + `BootstrapCoordinator` 在启动时自动构建：

```csharp
// EventRegistryBootstrapper 已实现 IBootstrapTask
// BootstrapCoordinator 自动拓扑排序启动

// 手动构建（不推荐，通常由 BootstrapCoordinator 自动完成）
eventRegistry.Build(providers);
capabilityRegistry.Build(providers);
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

```csharp
// Agent Tool
var agentTool = new AgentToolDescriptor
{
    Id = "tool_create_customer",
    Name = "create_customer",
    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_create_customer", 1),
    Description = "创建新客户记录",
    ToolCallMode = ToolCallMode.Auto,
    Tags = new List<string> { "customer", "crm" }
};

// HTTP Endpoint
var endpoint = new CapabilityEndpointDescriptor
{
    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_create_customer", 1),
    RoutePattern = "/api/customers",
    HttpMethod = CapabilityEndpointDescriptor.DeriveHttpMethod(CapabilityKind.Command),  // POST
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
| Phase 3 实现计划 | `docs/superpowers/plans/2026-06-09-phase-3-metadata-runtime-foundation.md` |
| 架构总结 | `docs/Feature/UnifiedMetadataModel/2026-06-09-unified-metadata-model-architecture-summary.md` |
| Phase 1-13 计划 | `docs/superpowers/plans/2026-06-08-*` / `2026-06-09-*` |

### 关键接口一览

| 接口 | 用途 |
|------|------|
| `IDescriptor` | 所有描述符基础接口 (Namespace, Id, FullId, Name) |
| `IVersionedDescriptor` | 版本化描述符 (+ Version) |
| `IHasContractIdentity` | 兼容性身份 (ContractHash, DefinitionHash) |
| `IRelationshipAwareDescriptor` | 自描述关系 |
| `RegistryBase<T>` | 通用注册表基类 |
| `RegistrySnapshot<T>` | 不可变快照 (ById, ByName, ByVersion) |
| `IRegistryValidator<T>` | 可插拔验证器 |
| `IRegistryValidationEngine<T>` | 验证引擎 |
| `IDescriptorProvider<T>` | 描述符提供者 |
| `IDescriptorResolver` | 统一解析器 |
| `IBootstrapTask` | 启动任务接口 |
| `BootstrapCoordinator` | 拓扑排序启动协调器 |
| `IDynamicRegistry<T>` | 动态注册表 |
| `ISchemaDescriptorProvider` | 声明 Schema |
| `ICapabilityProvider` | 声明 Capability |
| `ICapabilityHandler<TIn,TOut>` | 实现业务逻辑 |
| `IEventDescriptorProvider` | 声明 Event |
| `IFormDescriptorProvider` | 声明 Form |
| `IHumanTaskDescriptorProvider` | 声明 HumanTask |
| `IWorkflowDescriptorProvider` | 声明 Workflow |
| `ICapabilityPipeline` | 执行 Capability |
| `IWorkflowEngine` | 执行 Workflow |
| `IDraftStore` | Draft CRUD |

所有 Provider 接口由 source generator 自动发现并注册，无需手动调用 Registry。
