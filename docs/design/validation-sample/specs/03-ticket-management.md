# Spec: Ticket Management

## 概述

工单是系统的核心聚合根。完整的工单生命周期涵盖创建、分配、处理、回复、解决、关闭，以及 SLA 监控和历史审计。

## 实体

### Ticket

```csharp
[Entity]
public class Ticket : AuditedAggregateRoot<Guid>, IHasDomainEvents
{
    public string Title { get; private set; }          // 5-200字符
    public string Description { get; private set; }    // 无长度限制（TEXT）
    public Guid TenantId { get; private set; }         // 租户ID（DataFilter 自动过滤）
    public TicketStatus Status { get; private set; }
    public TicketPriority Priority { get; private set; }
    public TicketType Type { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid? SLAPolicyId { get; private set; }

    // SLA 时间追踪
    public DateTime? FirstResponseAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public DateTime? DueBy { get; private set; }
    public bool IsOverdue { get; private set; }

    // 导航属性
    public virtual Customer Customer { get; private set; }
    public virtual Category Category { get; private set; }
    public virtual ICollection<TicketMessage> Messages { get; private set; }
    public virtual ICollection<TicketHistory> History { get; private set; }
}
```

### 枚举

```csharp
public enum TicketStatus
{
    Open = 1,              // 新建，待分配
    InProgress = 2,        // 处理中
    WaitingOnCustomer = 3, // 等待客户回复
    WaitingOnThirdParty = 4, // 等待第三方
    Resolved = 5,          // 已解决
    Closed = 6,            // 已关闭
}

public enum TicketPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4,
}

public enum TicketType
{
    Question = 1,
    Incident = 2,
    Problem = 3,
    FeatureRequest = 4,
}
```

### 状态机

```
                        ┌─────────────────────────────────┐
                        │           Open (创建)            │
                        └──────────────┬──────────────────┘
                                       │ Assign
                                       ▼
                        ┌─────────────────────────────────┐
                        │        InProgress (处理中)       │←─────────────┐
                        └──┬──────────────┬───────────────┘              │
                           │              │                              │
              Ask Question │              │ Escalate/Need 3rd Party      │
                           ▼              ▼                              │
              ┌──────────────────┐  ┌─────────────────────────┐         │
              │ WaitingOnCustomer│  │ WaitingOnThirdParty     │         │
              └────────┬─────────┘  └───────────┬─────────────┘         │
                       │ Reply                  │ Reply                  │
                       └────────────────────────┴────────────────────────┘
                                       │
                                       ▼
                        ┌─────────────────────────────────┐
                        │         Resolved (已解决)        │←─────────────┐
                        └──┬───────────────┬──────────────┘              │
                           │               │                             │
              Auto-close   │               │ Reopen (客户回复)            │
              (N天后)       │               │                             │
                           ▼               │                             │
                        ┌──────────────────┐                             │
                        │ Closed (已关闭)   │                             │
                        └────────┬─────────┘                             │
                                 │                                       │
                                 │ Reopen (客户回复)                      │
                                 └───────────────────────────────────────┘
                                                │
                                                ▼
                        ┌─────────────────────────────────┐
                        │          InProgress (重开)       │
                        └─────────────────────────────────┘
```

### 领域方法

```csharp
// 状态变更
public void Assign(Guid agentId);                    // → InProgress, 记录 AssigneeId
public void AskCustomer(string question);             // → WaitingOnCustomer
public void EscalateToThirdParty(string reason);      // → WaitingOnThirdParty
public void CustomerReplied();                        // → InProgress (从 WaitingOnCustomer)
public void ThirdPartyReplied();                      // → InProgress (从 WaitingOnThirdParty)
public void Resolve();                                // → Resolved, 记录 ResolvedAt
public void Close();                                  // → Closed, 记录 ClosedAt
public void Reopen();                                 // → InProgress (从 Resolved/Closed)

// SLA
public void MarkFirstResponse();                      // 记录 FirstResponseAt
public void MarkOverdue();                            // IsOverdue = true, 触发 DomainEvent
public void CalculateSLA(SLAPolicy policy);           // 计算 DueBy

// 回复
public TicketMessage AddMessage(string content, MessageSenderType sender, Guid? senderId);
public TicketMessage AddInternalNote(string content, Guid agentId);
```

## TicketMessage

```csharp
[Entity]
public class TicketMessage : AuditedEntity<Guid>
{
    public Guid TicketId { get; private set; }
    public string Content { get; private set; }
    public MessageSenderType SenderType { get; private set; }
    public Guid? SenderId { get; private set; }
    public bool IsInternal { get; private set; }      // 内部备注，客户不可见
    // 注: IsSystemMessage 由 SenderType == System 推导，不单独存储，
    //      避免 SenderType=Agent + IsSystemMessage=true 的矛盾状态

    public bool IsSystemMessage => SenderType == MessageSenderType.System;

    public virtual Ticket Ticket { get; private set; }
    public virtual ICollection<TicketAttachment> Attachments { get; private set; }
}

public enum MessageSenderType
{
    Agent = 1,
    Customer = 2,
    System = 3,
}
```

## TicketAttachment

```csharp
[Entity]
public class TicketAttachment : AuditedEntity<Guid>
{
    public Guid? TicketId { get; private set; }
    public Guid? MessageId { get; private set; }
    public string FileName { get; private set; }
    public string FilePath { get; private set; }
    public string ContentType { get; private set; }
    public long FileSize { get; private set; }
    public string FileHash { get; private set; }       // SHA256，用于去重和完整性校验

    public virtual Ticket Ticket { get; private set; }
    public virtual TicketMessage Message { get; private set; }
}
```

## API

### Ticket CRUD

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| `GET` | `/api/tickets` | Tickets | 工单列表（分页+过滤+排序） |
| `GET` | `/api/tickets/{id}` | Tickets | 工单详情（含消息+历史） |
| `POST` | `/api/tickets` | Tickets.Create | 创建工单 |
| `PUT` | `/api/tickets/{id}` | Tickets.Update | 更新工单（标题/描述/分类/优先级） |
| `DELETE` | `/api/tickets/{id}` | Tickets.Delete | 删除工单 |

### Ticket Operations

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| `POST` | `/api/tickets/{id}/assign` | Tickets.Assign | 分配工单给客服 |
| `POST` | `/api/tickets/{id}/resolve` | Tickets.Update | 解决工单 |
| `POST` | `/api/tickets/{id}/close` | Tickets.Update | 关闭工单 |
| `POST` | `/api/tickets/{id}/reopen` | Tickets.Update | 重新打开工单 |
| `POST` | `/api/tickets/{id}/ask-customer` | Tickets.Update | 请求客户补充信息 |
| `POST` | `/api/tickets/{id}/escalate` | Tickets.Update | 升级到第三方 |

### Messages

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| `GET` | `/api/tickets/{id}/messages` | Tickets | 获取工单所有消息 |
| `POST` | `/api/tickets/{id}/messages` | Tickets.Create | 添加回复 |
| `POST` | `/api/tickets/{id}/messages/internal` | Tickets.Create | 添加内部备注 |

### Attachments

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| `POST` | `/api/tickets/{id}/attachments` | Tickets.Create | 上传附件 |
| `GET` | `/api/attachments/{id}/download` | Tickets | 下载附件 |

### 查询参数

```
GET /api/tickets?page=1&pageSize=20&sort=createdAt+desc
    &filter=ticket.status.name:eq:InProgress
    &filter=ticket.priority.name:in:High,Urgent
    &filter=ticket.createdAt:gte:2026-01-01

支持的过滤操作符: eq, neq, gt, gte, lt, lte, in, contains, startsWith
可过滤字段: Status, Priority, Type, CategoryId, AssigneeId, CustomerId, CreatedAt, IsOverdue
可排序字段: CreatedAt, UpdatedAt, Priority, Status, DueBy
```

### DTO 示例

```csharp
// CreateTicketDto
{
    "title": "无法登录系统",
    "description": "输入密码后一直提示密码错误，已尝试重置密码仍然不行",
    "priority": "High",
    "type": "Incident",
    "customerId": "guid",
    "categoryId": "guid"
}

// UpdateTicketDto
{
    "title": "无法登录系统（已重置）",
    "description": "更新后的描述...",
    "priority": "High",
    "type": "Incident",
    "categoryId": "guid"
    // 注: CustomerId 创建后不可修改
}

// TicketDto (Response)
{
    "id": "guid",
    "tenantId": "guid",
    "title": "无法登录系统",
    "status": "InProgress",
    "priority": "High",
    "type": "Incident",
    "customer": { "id": "guid", "name": "张三", ... },
    "assignee": { "id": "guid", "name": "客服小李", ... },
    "category": { "id": "guid", "name": "账号问题" },
    "createdAt": "2026-06-01T10:00:00Z",
    "dueBy": "2026-06-01T18:00:00Z",
    "isOverdue": false,
    "messageCount": 3
}

// TicketDetailDto (详情 - 含消息和历史)
{
    ...TicketDto,
    "messages": [ ... ],
    "history": [
        {
            "timestamp": "2026-06-01T10:30:00Z",
            "changeType": "Assigned",
            "fieldName": "AssigneeId",
            "oldValue": null,
            "newValue": "客服小李",
            "summary": "工单分配给客服小李",
            "changedBy": { "id": "guid", "name": "客服主管" }
        }
    ],
}

// AskCustomerRequest
{
    "question": "请提供您使用的浏览器版本和操作系统信息"
}

// EscalateRequest
{
    "reason": "需要运维团队检查服务器日志"
}
```

## Domain Events

| 事件 | 触发方法 | 携带数据 |
|------|---------|---------|
| `TicketCreatedDomainEvent` | 构造函数 | TicketId, CustomerId, Priority |
| `TicketAssignedDomainEvent` | `Assign()` | TicketId, OldAssigneeId, NewAssigneeId |
| `TicketStatusChangedDomainEvent` | 各状态变更方法 | TicketId, OldStatus, NewStatus |
| `TicketResolvedDomainEvent` | `Resolve()` | TicketId, AssigneeId, ResolutionMinutes |
| `TicketOverdueDomainEvent` | `MarkOverdue()` | TicketId, AssigneeId, DueBy |
| `TicketReopenedDomainEvent` | `Reopen()` | TicketId |

## 验证规则

| 字段 | 规则 |
|------|------|
| `Title` | 必填, 5-200 字符 |
| `Description` | 必填, 最少 10 字符 |
| `CustomerId` | 必填, 必须属于当前租户 **且 Customer.IsActive = true** |
| `CategoryId` | 可选, 必须属于当前租户且 `IsActive = true` 且 `IsDeleted = false` |
| `Priority` | 必填, 合法枚举值 |
| `Type` | 必填, 合法枚举值 |

## 验证检查点

- [ ] 创建工单后状态为 `Open`
- [ ] 分配客服后状态变为 `InProgress`
- [ ] 只有 `Tickets.Assign` 权限可分配工单
- [ ] Agent 只能看到分配给自己的工单 (没有 `Tickets.ViewAll`)
- [ ] Supervisor 可以看到所有工单
- [ ] 状态变更产生 `TicketHistory` 记录
- [ ] 状态变更产生审计日志
- [ ] 非合法状态转换抛出 `InvalidOperationException`
- [ ] 关闭后的工单无法直接修改（除重开外）
- [ ] 消息 `IsInternal = true` 时对客户 API 不可见
- [ ] `IsSystemMessage` 仅在 `SenderType = System` 时为 true（计算属性，不独立存储）
- [ ] 分页查询返回正确的 `totalCount`
- [ ] Filter `Status:Open,InProgress` 正确过滤
- [ ] Sort `CreatedAt desc` 正确排序
- [ ] Domain Events 正确发布
- [ ] `Reopen()` 可从 `Resolved` 或 `Closed` 状态触发
- [ ] Reopen 后 `DueBy` 和 `ResolvedAt` 重置为 null，SLA 重新计算
- [ ] 停用 Customer 后无法为其创建新工单（`Customer.IsActive = false` 校验拦截）
- [ ] 停用 Agent 后其未关闭工单自动重分配（参见 Spec 16）
