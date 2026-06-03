# Spec: Dashboard & Reporting

## 概述

仪表盘提供工单系统运行状况的统计视图。核心验证点是复杂聚合查询、FilterBuilder 链式查询、Feature 控制报表可见性。

## 仪表盘数据项

### 实时仪表盘 API

```
GET /api/dashboard/summary
```

响应：

```json
{
    "todayNewTickets": 12,
    "todayResolvedTickets": 8,
    "openTickets": 45,
    "overdueTickets": 3,
    "averageResolutionHours": 6.5,
    "slaAchievementRate": 0.87,
    "statusDistribution": {
        "open": 10,
        "inProgress": 15,
        "waitingOnCustomer": 12,
        "waitingOnThirdParty": 3,
        "resolved": 5,
        "closed": 0
    },
    "priorityDistribution": {
        "low": 5,
        "medium": 18,
        "high": 15,
        "urgent": 7
    },
    "agentWorkload": [
        { "agentId": "guid", "agentName": "客服小李", "assignedCount": 12 },
        { "agentId": "guid", "agentName": "客服小王", "assignedCount": 8 }
    ],
    "topCategories": [
        { "categoryId": "guid", "categoryName": "账号问题", "count": 15 },
        { "categoryId": "guid", "categoryName": "支付问题", "count": 10 }
    ]
}
```

### 实现

```csharp
[CrestService]
[Authorize(HelpdeskPermissions.Dashboard_View)]
public class DashboardAppService : ApplicationService, IDashboardAppService
{
    private readonly IRepository<Ticket, Guid> _ticketRepo;

    public async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var today = DateTime.UtcNow.Date;

        return new DashboardSummaryDto
        {
            TodayNewTickets = await _ticketRepo
                .AsQueryable()
                .CountAsync(t => t.CreatedAt >= today),

            OpenTickets = await _ticketRepo
                .AsQueryable()
                .Where(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.Resolved)
                .CountAsync(),

            OverdueTickets = await _ticketRepo
                .AsQueryable()
                .CountAsync(t => t.IsOverdue && t.Status != TicketStatus.Closed),

            AverageResolutionHours = await _ticketRepo
                .AsQueryable()
                .Where(t => t.ResolvedAt != null)
                .AverageAsync(t => EF.Functions
                    .DateDiffMinute(t.CreatedAt, t.ResolvedAt.Value)) / 60.0,

            // ... 分组统计
        };
    }
}
```

## 周报查询 API

```
GET /api/dashboard/weekly-reports?page=1&pageSize=10
```

获取历史周报记录（由 `WeeklyReportJob` 生成）。

## 工单趋势 API

```
GET /api/dashboard/ticket-trend?days=30
```

返回最近N天每天的工单新建/解决数量：

```json
{
    "dataPoints": [
        { "date": "2026-05-01", "created": 5, "resolved": 3 },
        { "date": "2026-05-02", "created": 8, "resolved": 6 },
        ...
    ]
}
```

## Feature 控制

```csharp
[RequiresFeature("Helpdesk.Reports.Enabled")]
[Authorize(HelpdeskPermissions.Dashboard_View)]
public class DashboardAppService : ... { ... }
```

## 验证检查点

- [ ] 仪表盘各统计数据准确
- [ ] `AgentWorkload` 仅统计未关闭工单
- [ ] `OverdueTickets` 只计算 `IsOverdue = true` 且非 Closed 的工单
- [ ] `SLA_AchievementRate` = `按时解决数 / 总解决数` (排除无 SLA 的工单)
- [ ] Feature `Reports.Enabled = false` 时 API 返回 403
- [ ] `WeeklyReportJob` 生成的报告可查询
