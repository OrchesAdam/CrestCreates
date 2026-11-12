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

## 审计保留治理

#132 已归档旧 Cleanup 服务、接口和日期清理 DTO；旧仓储裸删除方法已移除。
正式入口是 `IAuditRetentionAppService`，HTTP 通过 `AddCrestAuditRetentionHttp()` 显式装配，
在 `/api/audit-retention` 下发布 `POST preview`、`POST execute`、`GET run`、
`POST batches/export`、`POST holds` 和 `POST holds/release`。
不再提供旧生成路由 `/api/audit-log-cleanup/process-cleanup` 或更早的
`/api/auditlogcleanupapp/processcleanup` Controller 默认入口；常见的旧路径拼写
`/api/auditlogcleanup` 也不发布。

执行默认关闭（Global Setting `AuditLogging.Retention.Enabled=false`），必须显式启用且通过
`AuditLogging.Retention.*` 权限和当前租户校验。期限来自 Setting Management 的
`AuditLogging.RetentionDays`（默认 90），请求不能指定任意日期绕过策略。

V1 仅覆盖正式 PostgreSQL 固定库 AuditEnvelope 存储：发生时间与首次接收时间都严格早于
cutoff 才符合清理条件，边界相等、active hold、受保护或隔离记录保留，批次生成持久收据。
旧 AuditLog 查询模型不因此获得清理能力。typed job 与 HTTP 复用相同授权入口；自动调度尚未交付，
不能声称已经注册每日清理任务。设计和实施证据见
[保留治理设计](../../../superpowers/specs/2026-10-27-issue-132-audit-retention-governance-design.md)
及仓库 `memory.md` 的 #132 交付记录。

## 验证检查点

- [ ] 创建工单后审计日志表有对应记录
- [ ] 状态变更记录包含 OldStatus 和 NewStatus
- [ ] 分配变更记录包含 OldAssignee 和 NewAssignee
- [ ] 客户邮件在审计日志中被脱敏
- [ ] 匿名请求不产生审计记录
- [ ] 审计日志可通过 `/api/audit-logs` 查询
- [ ] 旧清理服务无法解析，旧清理路由返回 404
- [ ] 正式 retention 默认关闭；匿名、无权限或越权请求零 purge
- [ ] PostgreSQL retention 保留 cutoff 边界与 hold，并可按 receipt 对账
- [ ] 删除工单后审计日志仍然保留（不级联删除）
