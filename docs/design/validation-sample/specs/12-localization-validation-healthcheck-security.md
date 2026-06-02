# Spec: Localization, Validation, HealthCheck & Security

## 1. Localization（本地化）

### 支持语言

| 语言 | 标识 | 默认 |
|------|------|:----:|
| 简体中文 | `zh-CN` | ✓ |
| 英语 | `en` | ✗ |

### 配置

```csharp
services.AddLocalization(options =>
{
    options.DefaultLanguage = "zh-CN";
    options.SupportedLanguages = new[] { "zh-CN", "en" };
});
```

### 本地化资源文件

```
SaaSHelpdesk.Web/
└── Localization/
    ├── zh-CN.json
    └── en.json
```

### 资源示例

```json
// zh-CN.json
{
    "Ticket.Created": "工单已创建",
    "Ticket.Assigned": "工单已分配给 {0}",
    "Ticket.Resolved": "工单已解决",
    "Ticket.Status.Open": "待处理",
    "Ticket.Status.InProgress": "处理中",
    "Ticket.Status.WaitingOnCustomer": "等待客户回复",
    "Ticket.Status.Resolved": "已解决",
    "Ticket.Status.Closed": "已关闭",
    "Ticket.Priority.Low": "低",
    "Ticket.Priority.Medium": "中",
    "Ticket.Priority.High": "高",
    "Ticket.Priority.Urgent": "紧急",
    "Validation.Required": "{0} 不能为空",
    "Validation.MaxLength": "{0} 长度不能超过 {1} 字符",
    "Validation.InvalidEmail": "邮箱格式不正确",
    "Error.TicketNotFound": "工单不存在",
    "Error.StorageQuotaExceeded": "存储空间已满，请联系管理员升级套餐",
    "Feature.MaxAgents": "最大客服数"
}

// en.json
{
    "Ticket.Created": "Ticket created",
    "Ticket.Assigned": "Ticket assigned to {0}",
    "Ticket.Resolved": "Ticket resolved",
    "Ticket.Status.Open": "Open",
    "Ticket.Status.InProgress": "In Progress",
    ...
}
```

### 语言切换

```
用户层Setting: "Helpdesk.General.Language"
API: GET /api/localization/resources?language=en
```

### 使用方式

```csharp
// AppService中
var message = await _localizationService.GetAsync("Ticket.Assigned", agent.Name);

// FluentValidation中
RuleFor(x => x.Title)
    .NotEmpty()
    .WithMessage((dto) => _localizer["Validation.Required", "标题"]);
```

### 验证检查点

- [ ] 默认语言 zh-CN，API 错误消息为中文
- [ ] 用户修改 `General.Language = en` 后消息变为英文
- [ ] `GET /api/localization/resources?language=en` 返回正确资源
- [ ] 权限名/Feature名/Setting名支持本地化显示名

---

## 2. FluentValidation（输入校验）

### 验证器清单

| 验证器 | 验证对象 | 规则数 |
|--------|---------|:------:|
| `CreateTicketDtoValidator` | 工单创建 | 4 |
| `UpdateTicketDtoValidator` | 工单更新 | 3 |
| `CreateCustomerDtoValidator` | 客户创建 | 4 |
| `UpdateCustomerDtoValidator` | 客户更新 | 3 |
| `CreateKnowledgeBaseDtoValidator` | 知识库文章创建 | 3 |
| `UploadAttachmentDtoValidator` | 附件上传 | 3 |
| `CreateSLAPolicyDtoValidator` | SLA策略创建 | 3 |
| `CreateCategoryDtoValidator` | 分类创建 | 3 |

### 规则详情

#### CreateTicketDtoValidator

```csharp
public class CreateTicketDtoValidator : AbstractValidator<CreateTicketDto>
{
    public CreateTicketDtoValidator(
        IRepository<Customer, Guid> customerRepo,
        IRepository<Category, Guid> categoryRepo,
        ISettingProvider settingProvider)
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .Length(5, 200);

        RuleFor(x => x.Description)
            .NotEmpty()
            .MinimumLength(10);

        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .MustAsync(async (id, ct) => await customerRepo.AnyAsync(c => c.Id == id))
            .WithMessage("客户不存在");

        RuleFor(x => x.CategoryId)
            .MustAsync(async (id, ct) =>
                id == null || await categoryRepo.AnyAsync(c => c.Id == id && c.IsActive))
            .WithMessage("分类不存在或已停用")
            .When(x => x.CategoryId.HasValue);
    }
}
```

#### UploadAttachmentDtoValidator

```csharp
public class UploadAttachmentDtoValidator : AbstractValidator<UploadAttachmentDto>
{
    public UploadAttachmentDtoValidator(ISettingProvider settingProvider)
    {
        var maxSizeMB = int.Parse(settingProvider.GetOrNull(
            "Helpdesk.Attachment.MaxFileSizeMB") ?? "10");
        var allowedTypes = (settingProvider.GetOrNull(
            "Helpdesk.Attachment.AllowedTypes") ?? "")
            .Split(',');

        RuleFor(x => x.File).NotNull();

        RuleFor(x => x.File)
            .Must(f => f.Length <= maxSizeMB * 1024 * 1024)
            .WithMessage($"文件大小不能超过 {maxSizeMB}MB");

        RuleFor(x => x.File)
            .Must(f =>
            {
                var ext = Path.GetExtension(f.FileName).TrimStart('.').ToLower();
                return allowedTypes.Contains(ext);
            })
            .WithMessage($"不支持的文件类型，允许: {string.Join(", ", allowedTypes)}");
    }
}
```

### 配置

```csharp
services.AddValidation();
services.AddValidatorsFromAssemblyContaining<CreateTicketDtoValidator>();
```

### 验证检查点

- [ ] Title 为空时返回 400 + 错误消息
- [ ] Title 超过200字符返回 400
- [ ] CustomerId 不存在时返回 400
- [ ] 停用的分类作为 CategoryId 返回 400
- [ ] 文件超出大小返回 400
- [ ] 非法文件类型返回 400
- [ ] 错误消息支持本地化（中/英）

---

## 3. HealthCheck

### 检查项

| 检查 | 说明 |
|------|------|
| Database | `SELECT 1` 测试当前租户DB连接 |
| Redis | PING 测试（Phase 4） |
| FileStorage | 检查本地存储目录可写 |
| SLA Job | 检查最近一次 SLA 检测 Job 执行时间 |

### 配置

```csharp
services.AddHealthChecks()
    .AddCheck<TenantHealthCheck>("database")
    .AddCheck<RedisHealthCheck>("redis", tags: new[] { "cache" })
    .AddCheck<FileStorageHealthCheck>("file_storage")
    .AddCheck<JobHealthCheck>("sla_job");

// 端点
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### 健康报告示例

```
GET /health

{
    "status": "Healthy",
    "checks": {
        "database": { "status": "Healthy", "duration": "00:00:00.015" },
        "redis": { "status": "Healthy", "duration": "00:00:00.002" },
        "file_storage": { "status": "Healthy", "duration": "00:00:00.001" },
        "sla_job": { "status": "Healthy", "lastRun": "2026-06-01T10:00:00Z" }
    }
}
```

### 验证检查点

- [ ] `/health` 端点可访问
- [ ] 数据库不可达时状态为 `Unhealthy`
- [ ] Redis 不可达时仅 redis 检查失败，其他检查正常
- [ ] 返回 JSON 格式健康报告

---

## 4. Security Headers

### 配置

```csharp
services.AddCrestSecurity(options =>
{
    options.UseHsts = true;
    options.UseHttpsRedirection = true;
    options.Antiforgery = true;

    options.Headers = new SecurityHeadersOptions
    {
        XContentTypeOptions = "nosniff",
        XFrameOptions = "DENY",
        XXSSProtection = "1; mode=block",
        ReferrerPolicy = "strict-origin-when-cross-origin",
        PermissionsPolicy = "camera=(), microphone=(), geolocation=()"
    };
});
```

### 验证检查点

- [ ] 响应头包含 `X-Content-Type-Options: nosniff`
- [ ] 响应头包含 `X-Frame-Options: DENY`
- [ ] 响应头包含 `X-XSS-Protection: 1; mode=block`
- [ ] 响应头包含 `Referrer-Policy: strict-origin-when-cross-origin`
- [ ] 响应头包含 `Strict-Transport-Security`
- [ ] POST 请求通过 AntiForgery 校验

---

## 5. AOP [UnitOfWorkMo]

### 使用场景

在需要保证事务边界的复杂 AppService 方法上使用：

```csharp
[CrestService]
public class TicketAppService : ...
{
    [UnitOfWorkMo]
    public async Task<TicketDto> AssignAsync(Guid ticketId, Guid agentId)
    {
        // 分配客服 + 添加系统消息 + 发送通知 = 一个事务
        var ticket = await _ticketRepo.GetAsync(ticketId);
        ticket.Assign(agentId);
        ticket.AddMessage("工单已分配", MessageSenderType.System, null);
        // ... 其他操作
        await _unitOfWork.SaveChangesAsync();
        return ticket.ToDto();
    }
}
```

### 验证检查点

- [ ] `[UnitOfWorkMo]` 方法内的多个操作在同一事务中
- [ ] 中间异常时所有操作回滚
