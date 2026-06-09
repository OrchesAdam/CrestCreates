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

```csharp
// 查询 Capability
var cap = capabilityRegistry.GetByName("crm.customer.create");
var activeVersion = capabilityRegistry.GetActiveVersion("crm.customer.create");
var commandCaps = capabilityRegistry.GetByKind(CapabilityKind.Command);
var customerCaps = capabilityRegistry.GetByTag("customer");

// 查询 Event
var domainEvents = eventRegistry.GetByCategory(EventCategory.Domain);
var criticalEvents = eventRegistry.GetByImportance(EventImportance.Critical);

// 全局查询
var everything = globalRegistry.GetAll();
var allSchemas = globalRegistry.GetByKind(DescriptorKind.Schema);

// 依赖分析
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

## 11. 参考

| 文档 | 位置 |
|------|------|
| 设计规格书 | `docs/superpowers/specs/2026-06-08-unified-metadata-model-design.md` |
| 架构总结 | `docs/Feature/UnifiedMetadataModel/2026-06-09-unified-metadata-model-architecture-summary.md` |
| Phase 1-13 计划 | `docs/superpowers/plans/2026-06-08-*` / `2026-06-09-*` |

### 关键接口一览

| 接口 | 用途 |
|------|------|
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
