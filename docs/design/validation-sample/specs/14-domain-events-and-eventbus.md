# Spec: Domain Events & EventBus

## 概述

通过领域事件实现工单系统内的松耦合通知。V1 仅使用 Local EventBus（进程内），不涉及分布式消息队列。

## 领域事件定义

```
TicketCreatedDomainEvent
├── TicketId: Guid
├── CustomerId: Guid
├── Priority: TicketPriority
└── TenantId: Guid

TicketAssignedDomainEvent
├── TicketId: Guid
├── AssigneeId: Guid
├── PreviousAssigneeId: Guid?
└── TenantId: Guid

TicketStatusChangedDomainEvent
├── TicketId: Guid
├── OldStatus: TicketStatus
├── NewStatus: TicketStatus
├── ChangedByUserId: Guid
└── TenantId: Guid

TicketResolvedDomainEvent
├── TicketId: Guid
├── AssigneeId: Guid
├── ResolutionMinutes: int
└── TenantId: Guid

TicketOverdueDomainEvent
├── TicketId: Guid
├── AssigneeId: Guid?
├── DueBy: DateTime
└── TenantId: Guid

TicketReopenedDomainEvent
├── TicketId: Guid
├── ReopenedByUserId: Guid
└── TenantId: Guid

CustomerCreatedDomainEvent
├── CustomerId: Guid
├── Name: string
└── TenantId: Guid
```

## Event Handlers

### TicketCreatedHandler

```csharp
public class TicketCreatedHandler :
    IEventHandler<TicketCreatedDomainEvent>,
    ITransientDependency
{
    private readonly ITicketRepository _ticketRepo;
    private readonly IEmailTemplateService _templateService;
    // 注意: V1 不做真实邮件发送，仅渲染模板作为占位

    public async Task HandleAsync(TicketCreatedDomainEvent eventData)
    {
        // 1. 加载邮件模板
        var template = await _templateService.GetTemplateAsync("ticket_created");

        // 2. 渲染通知内容 (V1: 仅日志记录)
        var rendered = _templateService.Render(template, new()
        {
            ["TicketNumber"] = eventData.TicketId.ToString("N")[..8],
            ["Priority"] = eventData.Priority.ToString(),
        });

        // 3. V1: 记录日志表示通知已触发
        Log.Information(
            "Ticket created notification: TicketId={TicketId}, Priority={Priority}",
            eventData.TicketId, eventData.Priority);
    }
}
```

### TicketAssignedHandler

```csharp
public class TicketAssignedHandler :
    IEventHandler<TicketAssignedDomainEvent>,
    ITransientDependency
{
    public async Task HandleAsync(TicketAssignedDomainEvent eventData)
    {
        // 记录分配动作 (V1: 占位，V2: 发送邮件通知被分配的客服)
        Log.Information(
            "Ticket assigned: TicketId={TicketId}, AssigneeId={AssigneeId}",
            eventData.TicketId, eventData.AssigneeId);
    }
}
```

### TicketOverdueHandler

```csharp
public class TicketOverdueHandler :
    IEventHandler<TicketOverdueDomainEvent>,
    ITransientDependency
{
    public async Task HandleAsync(TicketOverdueDomainEvent eventData)
    {
        // 通知主管 (V1: 占位)
        Log.Warning(
            "Ticket overdue: TicketId={TicketId}, DueBy={DueBy}",
            eventData.TicketId, eventData.DueBy);
    }
}
```

## 发布领域事件

### 在实体中定义

```csharp
public class Ticket : AuditedAggregateRoot<Guid>, IHasDomainEvents
{
    public ICollection<IDomainEvent> DomainEvents { get; } = new List<IDomainEvent>();

    public void Assign(Guid agentId)
    {
        var previousAssigneeId = AssigneeId;
        AssigneeId = agentId;
        Status = TicketStatus.InProgress;

        DomainEvents.Add(new TicketAssignedDomainEvent(
            Id, agentId, previousAssigneeId, TenantId));
    }

    // ... 其他方法
}
```

### 在 AppService 中触发发布

```csharp
public class TicketAppService : ...
{
    private readonly IDomainEventPublisher _eventPublisher;

    public async Task<TicketDto> AssignAsync(Guid ticketId, Guid agentId)
    {
        var ticket = await _ticketRepo.GetAsync(ticketId);
        ticket.Assign(agentId);
        await _unitOfWork.SaveChangesAsync();

        // 发布积攒的领域事件
        await _eventPublisher.PublishAsync(ticket.DomainEvents);

        return ticket.ToDto();
    }
}
```

## EventBus 配置

```csharp
// 使用本地事件总线
services.AddLocalEventBus(options =>
{
    options.AutoSubscribe = true;  // 自动订阅程序集中的 IEventHandler
});
```

## 验证检查点

- [ ] `TicketCreatedDomainEvent` 在创建工单后正确发布
- [ ] `TicketAssignedDomainEvent` 在分配工单后正确发布
- [ ] `TicketOverdueDomainEvent` 在 SLA 逾期 Job 检测后正确发布
- [ ] `TicketCreatedHandler` 正确接收并处理事件
- [ ] `TicketOverdueHandler` 正确记录 Warning 日志
- [ ] 事件中包含正确的 TenantId
- [ ] LocalEventBus 在同一进程内正确投递

---

## Additional: TicketHistory（工单历史）

### 实体

```csharp
public class TicketHistory : AuditedEntity<Guid>
{
    public Guid TicketId { get; private set; }
    public string FieldName { get; private set; }
    public string OldValue { get; private set; }
    public string NewValue { get; private set; }
    public HistoryChangeType ChangeType { get; private set; }
    public Guid? ChangedById { get; private set; }
    public string Summary { get; private set; }
}

public enum HistoryChangeType
{
    Created = 1,
    StatusChanged = 2,
    Assigned = 3,
    Replied = 4,
    Resolved = 5,
    Closed = 6,
    Reopened = 7,
    OverdueDetected = 8,
}
```

### 记录时机

每次工单状态变更、分配变更时，由 `Ticket` 实体的领域方法内部产生 `TicketHistory` 记录：

```csharp
public void Assign(Guid agentId)
{
    var oldAssignee = AssigneeId?.ToString();
    AssigneeId = agentId;
    Status = TicketStatus.InProgress;

    History.Add(new TicketHistory(
        Id, "AssigneeId",
        oldAssignee, agentId.ToString(),
        HistoryChangeType.Assigned, CurrentUserId,
        $"工单分配给 {agentId}"));
}
```

### 验证检查点

- [ ] 每个状态变更都有对应 History 记录
- [ ] History 记录按时间排序
- [ ] `FieldName` 准确记录变更字段
- [ ] `OldValue` / `NewValue` 准确记录变更前后值
- [ ] Soft Delete/删除工单时 History 记录保留
