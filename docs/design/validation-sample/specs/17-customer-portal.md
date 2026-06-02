# Spec: Customer Portal

## 概述

客户端 API 允许客户（无需登录为 IdentityUser）创建工单、查看工单、回复工单。客户认证通过 API Key 或 Token 方式，与内部 Agent 的 JWT 认证分离。

## 认证方案

客户不是 `IdentityUser`，采用 **API Key** 认证：

```
客户标识: 每个 Customer 实体创建时自动生成一个 API Key（Guid）
认证方式: 请求头 X-Customer-Key: {apiKey}
认证流程: CustomerApiKeyAuthenticationHandler 查询 Customer 实体验证 Key有效性
```

### API Key 管理

| 操作 | 说明 |
|------|------|
| 生成 | Customer 创建时自动生成，存储为 `Customer.ApiKey` |
| 刷新 | `POST /api/portal/refresh-key` — 需旧 Key 验证后生成新 Key |
| 失效 | Customer 停用时 `ApiKey` 自动失效 |

## API

所有端点 Base Path: `/api/portal`

| 方法 | 路径 | 认证 | 说明 |
|------|------|:--:|------|
| `POST` | `/api/portal/tickets` | Key | 创建工单 |
| `GET` | `/api/portal/tickets` | Key | 我的工单列表 |
| `GET` | `/api/portal/tickets/{id}` | Key | 工单详情（含消息，不含内部备注） |
| `POST` | `/api/portal/tickets/{id}/reply` | Key | 回复工单 |
| `POST` | `/api/portal/refresh-key` | Key | 刷新 API Key |
| `GET` | `/api/portal/knowledge-base` | None | 公开知识库（仅已发布文章） |
| `GET` | `/api/portal/knowledge-base/{id}` | None | 知识库文章详情 |

### 创建工单

```
POST /api/portal/tickets
Header: X-Customer-Key: abc-123

{
    "title": "无法登录系统",
    "description": "输入密码后提示密码错误...",
    "priority": "High",
    "type": "Incident",
    "categoryId": "guid"
}
```

**行为**：
1. 通过 `X-Customer-Key` 识别 Customer
2. 检查 `Customer.IsActive` → 停用客户返回 403
3. 自动绑定 `CustomerId`（忽略请求体中的 customerId）
4. 新建状态为 `Open`
5. 根据 Priority 自动匹配 SLA Policy 并计算 DueBy
6. 返回 `TicketDto`

### 我的工单列表

```
GET /api/portal/tickets?page=1&pageSize=20&sort=createdAt+desc
    &filter=ticket.status.name:neq:Closed

自动过滤: 仅返回当前 Customer 的工单
不支持按 CustomerId 过滤（强制绑定）
支持按 Status 过滤
```

### 回复工单

```
POST /api/portal/tickets/{id}/reply

{
    "content": "我试过了，还是不行"
}
```

**行为**：
1. 验证工单属于当前 Customer
2. 验证工单状态允许客户回复（非 Closed）
3. 添加消息 `SenderType = Customer`
4. 若工单状态为 `WaitingOnCustomer` → 自动调用 `ticket.CustomerReplied()` → `InProgress`
5. 若工单状态为 `Resolved` → 自动调用 `ticket.Reopen()` → `InProgress`

### 查看工单详情

```
GET /api/portal/tickets/{id}

Response 包含:
- 工单基本信息
- 所有消息（不含 IsInternal = true 的内部备注）
- 不含内部消息链中的附件
```

## Customer Portal AppService

```csharp
[CrestService]
public class CustomerPortalAppService : ApplicationService, ICustomerPortalAppService
{
    private readonly IRepository<Ticket, Guid> _ticketRepo;
    private readonly IRepository<Customer, Guid> _customerRepo;
    private readonly ICustomerKeyAuthenticationService _keyAuth;
    private readonly IFeatureChecker _featureChecker;

    public async Task<TicketDto> CreateTicketAsync(CreateTicketDto input)
    {
        // 1. 从 X-Customer-Key 获取 Customer
        var customer = await _keyAuth.GetCurrentCustomerAsync();

        // 2. 检查 Customer.IsActive
        if (!customer.IsActive)
            throw new CustomerDeactivatedException(customer.Id);

        // 3. 检查月工单上限 (Feature)
        var maxTickets = await _featureChecker.GetAsync<int>("Helpdesk.MaxTicketsPerMonth");
        var currentMonthCount = await GetCustomerMonthTicketCount(customer.Id);
        if (currentMonthCount >= maxTickets)
            throw new FeatureLimitExceededException(
                "Helpdesk.MaxTicketsPerMonth", maxTickets, currentMonthCount);

        // 4. 绑定 CustomerId
        var ticket = new Ticket(
            input.Title, input.Description,
            input.Priority, input.Type,
            customer.Id, input.CategoryId);

        // 5. 匹配 SLA
        var slaPolicy = await _slaPolicyRepo
            .FindByPriorityAsync(input.Priority);
        if (slaPolicy != null)
            ticket.CalculateSLA(slaPolicy);

        // 6. 保存 + 发布 TicketCreatedDomainEvent
        await _ticketRepo.InsertAsync(ticket);
        await _uow.SaveChangesAsync();

        return ticket.ToDto();
    }
}
```

## 认证中间件配置

```csharp
// Program.cs
services.AddAuthentication()
    .AddScheme<CustomerApiKeyOptions, CustomerApiKeyAuthenticationHandler>(
        "CustomerApiKey", options => { });

// Customer Portal 使用 CustomerApiKey 认证
app.MapGroup("/api/portal")
   .RequireAuthorization(new AuthorizeAttribute
   {
       AuthenticationSchemes = "CustomerApiKey"
   })
   .MapCrestAspNetCoreDynamicApi();
```

## CustomerApiKeyAuthenticationHandler

```csharp
public class CustomerApiKeyAuthenticationHandler : AuthenticationHandler<CustomerApiKeyOptions>
{
    private readonly ICustomerRepository _customerRepo;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Customer-Key", out var key))
            return AuthenticateResult.NoResult();

        var customer = await _customerRepo.FindByApiKeyAsync(key.ToString());
        if (customer == null || !customer.IsActive)
            return AuthenticateResult.Fail("Invalid or inactive API key");

        var claims = new[]
        {
            new Claim("CustomerId", customer.Id.ToString()),
            new Claim("CustomerName", customer.Name),
            new Claim("TenantId", customer.TenantId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name));
    }
}
```

## 安全考量

| 风险 | 缓解措施 |
|------|---------|
| API Key 泄露 | 支持 Key 刷新；停用 Customer 自动失效 |
| 客户查看他人工单 | AppService 强制绑定 CustomerId，忽略请求参数 |
| 客户看到内部备注 | Portal 查询自动过滤 `IsInternal = true` 的消息 |
| 暴力破解 API Key | API Key 为 Guid 长度，加入失败计数和临时锁定 |
| 已停用客户仍可访问 | 认证 Handler 检查 `IsActive` |

## 验证检查点

- [ ] 客户通过 `X-Customer-Key` 创建工单成功
- [ ] 无效/停用 Customer 的 Key 返回 401
- [ ] 已停用 Customer 无法创建工单（返回 403）
- [ ] 创建工单后自动绑定该 Customer 的 ID
- [ ] 客户只能看到自己的工单
- [ ] 客户看不到内部备注（`IsInternal = true`）
- [ ] 客户回复后 `WaitingOnCustomer` 工单自动转为 `InProgress`
- [ ] 客户回复 `Resolved` 工单自动转为 `InProgress`（重开）
- [ ] 客户无法回复 `Closed` 工单
- [ ] Feature `MaxTicketsPerMonth` 超限时返回错误
- [ ] 刷新 API Key 后旧 Key 立即失效
- [ ] 知识库端点无需认证即可访问
- [ ] 知识库仅返回 `IsPublished = true` 的文章
