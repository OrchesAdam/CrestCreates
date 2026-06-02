# Spec: Multi-Tenancy

## 概述

SaaS Helpdesk 采用 **独立数据库** 隔离策略，每个租户拥有独立的 PostgreSQL 数据库。与 LibraryManagement 的共享表（Discriminator）模式不同，本项目着重验证独立DB的租户生命周期。

## 租户数据结构

使用框架 `Tenant` 实体，无自定义扩展。

| 字段 | 值 |
|------|-----|
| `Id` | Guid, 框架生成 |
| `Name` | 公司唯一标识，如 `acme-corp` |
| `DisplayName` | 展示名，如 `Acme Corporation` |
| `ConnectionString` | 独立数据库连接字符串，由 `TenantBootstrapper` 创建DB后写入 |
| `IsActive` | 是否启用 |

## 租户解析

| 策略 | 配置 |
|------|------|
| 解析方式 | `Header` → `X-Tenant-Id` |
| 隔离策略 | `Database` |
| 后备行为 | 无租户Header时返回401 |

## 租户创建流程

```
POST /api/tenants
    │
    ▼
TenantAppService.Create(input)
    │
    ├── 1. 验证 TenantName 唯一性
    ├── 2. 创建 Tenant 实体（ConnectionString 暂为空）
    ├── 3. TenantBootstrapper.Bootstrap(tenantId)
    │       ├── 3.1 生成连接字符串（基于模板: Host=localhost;Database=helpdesk_{tenantId};...）
    │       ├── 3.2 创建数据库（CREATE DATABASE）
    │       ├── 3.3 更新 Tenant.ConnectionString
    │       └── 3.4 返回成功
    ├── 4. TenantInitializationOrchestrator.Initialize(tenantId)
    │       ├── 4.1 打开租户DB连接
    │       ├── 4.2 执行 EF Core Migration
    │       ├── 4.3 种子化框架基础数据（Admin Role, Permissions）
    │       ├── 4.4 种子化应用默认数据（默认分类 Default/General）
    │       ├── 4.5 种子化 Feature 默认值
    │       └── 4.6 种子化 Setting 默认值
    └── 5. 返回 TenantDto
```

### API

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| `POST` | `/api/tenants` | 平台管理员 | 创建租户 + 触发初始化 |
| `GET` | `/api/tenants/{id}` | 平台管理员 | 获取租户信息 |
| `GET` | `/api/tenants` | 平台管理员 | 租户列表 |
| `PUT` | `/api/tenants/{id}` | 平台管理员 | 更新租户（仅 Name/DisplayName） |
| `DELETE` | `/api/tenants/{id}` | 平台管理员 | 删除租户 + 清理数据库 |
| `GET` | `/api/tenants/{id}/diagnostics` | 平台管理员 | 租户诊断信息 |

## 租户删除流程

```
DELETE /api/tenants/{id}
    │
    ▼
TenantAppService.Delete(id)
    │
    ├── 1. TenantDeletionGuard.CheckCanDelete(tenantId)
    │       └── 检查是否有未结工单、活跃订阅等业务约束
    ├── 2. TenantDeletionManager.Delete(tenantId)
    │       ├── 2.1 断开数据库连接
    │       ├── 2.2 DROP DATABASE
    │       └── 2.3 标记 Tenant.IsActive = false
    └── 3. 返回成功
```

## 租户诊断

```
GET /api/tenants/{id}/diagnostics
    │
    ▼
TenantDiagnosticsAppService.GetDiagnostics(tenantId)
    │
    └── 返回 TenantDiagnosticsDto:
        ├── TenantId
        ├── DatabaseExists: bool
        ├── CanConnect: bool
        ├── MigrationVersion
        ├── EntityCounts:
        │   ├── TicketCount
        │   ├── CustomerCount
        │   ├── AgentCount
        │   └── CategoryCount
        ├── StorageUsageMB
        └── LastActivityAt: DateTime?
```

## 租户上下文在后台任务中的传递

后台 Job 执行时需要正确的租户上下文：

```
SLAOverdueCheckJob.Execute()
    │
    ├── 1. 获取所有活跃租户列表
    └── 2. 对每个租户:
            ├── using (CurrentTenant.Change(tenantId, tenantName))
            └── 在该租户DB中执行SLA检查查询
```

## 验证检查点

- [ ] 租户A创建的数据在租户B的查询中不可见
- [ ] Header `X-Tenant-Id: nonexistent` 返回 401
- [ ] 创建租户后数据库自动创建并初始化
- [ ] TenantDiagnostics 返回正确的实体计数
- [ ] 删除租户后数据库被 DROP
- [ ] 后台 Job 中的租户上下文切换正确
- [ ] TenantFeatureDefaultsSeeder 正确种子化默认 Feature 值
- [ ] TenantSettingDefaultsSeeder 正确种子化默认 Setting 值
