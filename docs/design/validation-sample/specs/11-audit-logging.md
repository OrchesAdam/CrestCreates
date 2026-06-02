# Spec: Audit Logging

## 概述

工单系统的所有关键操作需要审计记录。核心验证点是审计中间件集成、敏感字段脱敏、以及审计日志清理。

## 审计事件清单

### 工单操作

| 事件类型 | 触发操作 | 审计信息 |
|---------|---------|---------|
| `Ticket.Created` | 创建工单 | TicketId, 创建者 |
| `Ticket.Updated` | 更新工单属性 | TicketId, 变更字段 |
| `Ticket.Assigned` | 分配客服 | TicketId, OldAssignee → NewAssignee |
| `Ticket.StatusChanged` | 状态变更 | TicketId, OldStatus → NewStatus |
| `Ticket.Resolved` | 解决工单 | TicketId, Resolver |
| `Ticket.Closed` | 关闭工单 | TicketId, Closer |
| `Ticket.Deleted` | 删除工单 | TicketId, 删除者 |
| `Ticket.MessageAdded` | 添加回复 | TicketId, MessageId |

### 管理操作

| 事件类型 | 触发操作 |
|---------|---------|
| `Customer.Created` / `.Updated` / `.Deleted` | 客户管理 |
| `Agent.Created` / `.Deactivated` | 客服管理 |
| `SLAPolicy.Created` / `.Updated` / `.Deleted` | SLA策略管理 |
| `Category.Created` / `.Updated` / `.Deleted` | 分类管理 |
| `KnowledgeBase.Created` / `.Updated` / `.Published` | 知识库管理 |

### 系统事件

| 事件类型 | 触发操作 |
|---------|---------|
| `SLA.OverdueDetected` | SLA逾期Job检测到逾期 |
| `Ticket.AutoClosed` | 自动关闭Job关闭工单 |

## 配置

```csharp
// WebModule 中注册审计
services.AddAuditLogging(options =>
{
    options.IsEnabled = true;
    options.IsEnabledForAnonymousUsers = false;

    // 脱敏规则
    options.RedactionRules.Add(new AuditLogRedactionRule
    {
        Field = "Email",
        Pattern = @"(?<=^.{2})[^@]+(?=@)",
        Replacement = "***"
    });
});
```

## 敏感字段脱敏

`IAuditLogRedactor` 对以下字段在写入审计日志前脱敏：

| 实体/字段 | 脱敏规则 | 示例 |
|-----------|---------|------|
| `Customer.Email` | 保留前2字符+域名 | `zh***@example.com` |
| `Customer.Phone` | 保留后4位 | `*******8000` |
| `TicketMessage.Content` | 不脱敏（非敏感） | - |
| `TicketAttachment.FileName` | 不脱敏 | - |

## AuditLog AppService (框架内置)

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/api/audit-logs` | 审计日志列表（分页+过滤） |
| `GET` | `/api/audit-logs/{id}` | 审计日志详情 |

### 查询示例

```
GET /api/audit-logs?page=1&pageSize=20&sort=executionTime+desc
    &startTime=2026-06-01&endTime=2026-06-30
    &userName=admin
    &httpStatusCode=200
```

## Audit Log Cleanup

### 配置

```csharp
services.AddAuditLogCleanup(options =>
{
    options.RetentionPeriodDays = 90; // 保留90天
    options.CleanupBatchSize = 1000;
    options.IsEnabled = true;
});
```

### Cleanup Job

已在 `AuditLogCleanupJob` 中注册（Phase 4），执行频率：每天凌晨3:00。

## 验证检查点

- [ ] 创建工单后审计日志表有对应记录
- [ ] 状态变更记录包含 OldStatus 和 NewStatus
- [ ] 分配变更记录包含 OldAssignee 和 NewAssignee
- [ ] 客户邮件在审计日志中被脱敏
- [ ] 匿名请求不产生审计记录
- [ ] 审计日志可通过 `/api/audit-logs` 查询
- [ ] `AuditLogCleanupJob` 正确清理超过保留期的记录
- [ ] 删除工单后审计日志仍然保留（不级联删除）
