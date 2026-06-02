# Spec: SLA Policy & Background Jobs

## 概述

SLA（Service Level Agreement）定义了工单响应和解决的时限要求。通过 Quartz 后台任务定时检测 SLA 是否逾期，并触发通知和升级。

## SLA Policy 实体

```csharp
[Entity]
public class SLAPolicy : AuditedAggregateRoot<Guid>
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }              // 策略名称, 如"标准SLA"
    public TicketPriority Priority { get; private set; }   // 适用的优先级
    public int FirstResponseMinutes { get; private set; }  // 首次响应时限（分钟）
    public int ResolutionMinutes { get; private set; }     // 解决时限（分钟）
    public bool IsActive { get; private set; }
    public bool BusinessHoursOnly { get; private set; }    // V1保留字段但忽略，始终按24x7计算。
                                                             // 若 V2 启用，需修改 CalculateDueBy() 逻辑。
                                                             // 当前禁止设为 true（Validator中拦截）：

    // 领域方法
    public void Activate();
    public void Deactivate();
    public DateTime CalculateDueBy(DateTime createdAt);    // 计算最终期限
}
```

### SLA 计算规则

```
DueBy = createdAt + resolutionMinutes

示例：
  工单创建: 2026-06-01 10:00
  SLA ResolutionMinutes: 480 (8小时)
  DueBy: 2026-06-01 18:00
```

## API

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| `GET` | `/api/sla-policies` | SLAPolicies | SLA策略列表 |
| `GET` | `/api/sla-policies/{id}` | SLAPolicies | SLA策略详情 |
| `POST` | `/api/sla-policies` | SLAPolicies.Manage | 创建SLA策略 |
| `PUT` | `/api/sla-policies/{id}` | SLAPolicies.Manage | 更新SLA策略 |
| `DELETE` | `/api/sla-policies/{id}` | SLAPolicies.Manage | 删除SLA策略 |

### 默认 SLA

种子数据提供四个默认策略（每个优先级一个，符合 UNIQUE(TenantId, Priority) 约束）：

| 名称 | 优先级 | 首次响应 | 解决时限 |
|------|--------|---------|---------|
| 低优先级SLA | Low | 240分钟 (4h) | 1440分钟 (24h) |
| 标准SLA | Medium | 120分钟 (2h) | 480分钟 (8h) |
| 高优先级SLA | High | 60分钟 | 240分钟 (4h) |
| 紧急SLA | Urgent | 30分钟 | 120分钟 (2h) |

## Background Job: SLA 逾期检测

```
SLAOverdueCheckJob
├── 调度频率: 每5分钟
├── 执行逻辑:
│   ├── 1. 获取所有活跃租户
│   └── 2. 对每个租户:
│       ├── using (CurrentTenant.Change(tenantId))
│       ├── 查询: Status IN (Open, InProgress, WaitingOnCustomer, WaitingOnThirdParty)
│       │          AND DueBy IS NOT NULL
│       │          AND DueBy < NOW()
│       │          AND IsOverdue = false
│       └── 对每个逾期工单:
│           ├── ticket.MarkOverdue()
│           ├── 保存变更
│           └── 发布 TicketOverdueDomainEvent
└── 日志: 每个租户检测到的逾期工单数
```

## Background Job: 自动关闭

```
AutoCloseResolvedTicketsJob
├── 调度频率: 每天凌晨2:00
├── 执行逻辑:
│   ├── 1. 读取 Setting: "Helpdesk.Ticket.AutoCloseDays" (默认14天)
│   ├── 2. 获取所有活跃租户
│   └── 3. 对每个租户:
│       ├── using (CurrentTenant.Change(tenantId))
│       ├── 查询: Status = Resolved
│       │          AND ResolvedAt < NOW() - AutoCloseDays
│       └── 对每个到期工单:
│           ├── if (ticket.Status != Resolved)
│           │     跳过 (幂等守卫: 可能已被手动关闭)
│           ├── ticket.Close()
│           └── 保存变更
└── 日志: 关闭的工单数量
```

## Background Job: 周报

```
WeeklyReportJob
├── 调度频率: 每周一上午8:00
├── 执行逻辑:
│   ├── 1. 获取所有活跃租户
│   └── 2. 对每个租户:
│       ├── using (CurrentTenant.Change(tenantId))
│       ├── 统计上周数据:
│       │   ├── 新建工单数
│       │   ├── 解决工单数
│       │   ├── SLA 达标率 (%) = 在DueBy前解决的工单 / 总解决工单
│       │   ├── 平均解决时间
│       │   └── 逾期工单数
│       └── 生成报告 → (V1: 仅存数据库; V2: 发送邮件通知)
└── 报告实体: WeeklyReport (AuditedEntity)
    ├── TenantId
    ├── WeekStart (周一日期)
    ├── NewTickets
    ├── ResolvedTickets
    ├── SLAAchievementRate
    ├── AverageResolutionMinutes
    └── OverdueTickets
```

## Quartz 配置

```csharp
// 注册 Quartz
services.AddQuartzScheduler();

// Job 定义
[ScheduledJob(CronExpression = "0 */5 * * * ?")]  // 每5分钟
public class SLAOverdueCheckJob : IJob
{
    public async Task Execute(IJobExecutionContext context) { ... }
}

[ScheduledJob(CronExpression = "0 0 2 * * ?")]    // 每天2:00
public class AutoCloseResolvedTicketsJob : IJob { ... }

[ScheduledJob(CronExpression = "0 0 8 ? * MON")]  // 每周一8:00
public class WeeklyReportJob : IJob { ... }
```

## 工单创建时的 SLA 绑定

```csharp
// TicketAppService.CreateAsync()
public override async Task<TicketDto> CreateAsync(CreateTicketDto input)
{
    var customer = await customerRepository.GetAsync(input.CustomerId);
    var policy = await slaPolicyRepository.FindByPriorityAsync(input.Priority);

    var ticket = new Ticket(
        input.Title,
        input.Description,
        input.Priority,
        input.Type,
        customer.Id,
        input.CategoryId
    );

    if (policy != null)
    {
        ticket.CalculateSLA(policy);  // 设置 DueBy
        ticket.SLAPolicyId = policy.Id;
    }

    await ticketRepository.InsertAsync(ticket);
    await unitOfWork.SaveChangesAsync();

    return ticket.ToDto();
}
```

## 验证检查点

- [ ] 创建工单时根据优先级自动绑定SLA并计算DueBy
- [ ] SLA 逾期检测 Job 每5分钟执行一次
- [ ] 逾期工单 `IsOverdue = true` 并发布 `TicketOverdueDomainEvent`
- [ ] 自动关闭 Job 仅关闭超过N天的Resolved工单
- [ ] `AutoCloseDays` Setting 修改后下次Job执行时生效
- [ ] 后台 Job 中租户上下文正确（不同租户数据隔离）
- [ ] Job 执行日志记录（日志包含 tenantId, 处理数量）
- [ ] 无SLA策略匹配时工单 DueBy = null（不参与逾期检测）
- [ ] SLA策略更新不影响已创建的工单（关联是快照）
