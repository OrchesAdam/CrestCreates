# Validation Sample Specs — 索引

## 文档目录

| # | 文档 | 核心框架能力 |
|---|------|------------|
| [01](./01-multi-tenancy.md) | Multi-Tenancy | 独立DB隔离、租户生命周期、Bootstrapper、DeletionManager、Diagnostics |
| [02](./02-auth-authorization.md) | Auth & Authorization | OpenIddict JWT、角色权限矩阵、Permission Sync |
| [03](./03-ticket-management.md) | Ticket Management | 核心聚合根、状态机、CRUD、FilterBuilder/SortBuilder、Domain Events |
| [04](./04-customer-management.md) | Customer Management | CRUD、Unique约束、激活/停用、组织关联 |
| [05](./05-category-management.md) | Category Management | 树形结构、循环引用检测、Soft Delete、[CacheMo] 缓存 |
| [06](./06-sla-and-background-jobs.md) | SLA Policy & Background Jobs | SLA计算、Quartz调度、跨租户Job执行 |
| [07](./07-knowledge-base.md) | Knowledge Base | [CacheMo]、Feature Checker、浏览量去重 |
| [08](./08-file-management.md) | File Management | LocalFileSystemProvider、Setting驱动校验、Feature存储配额 |
| [09](./09-setting-and-feature.md) | Setting & Feature Management | DefinitionProvider、三级作用域、Feature Checker、加密Setting |
| [10](./10-dashboard-and-reporting.md) | Dashboard & Reporting | 聚合查询、Feature控制报表可见性 |
| [11](./11-audit-logging.md) | Audit Logging | 审计中间件、脱敏、AuditLogCleanup |
| [12](./12-localization-validation-healthcheck-security.md) | Localization, Validation, HealthCheck & Security | 中英文、FluentValidation、/health端点、安全头、AOP [UnitOfWorkMo] |
| [13](./13-vfs-and-organization.md) | Virtual File System & Organization | 嵌入资源读取、邮件模板渲染、组织层级权限 |
| [14](./14-domain-events-and-eventbus.md) | Domain Events & EventBus | LocalEventBus、IEventHandler、TicketHistory |
| [15](./15-efcore-datafilter-concurrency.md) | EF Core, DataFilter & Concurrency | DbContext配置、索引定义、SoftDelete/Tenant Filter、乐观锁 |
| [16](./16-agent-management.md) | Agent Management | IdentityUser扩展属性、Feature限制、停用自动重分配、角色变更 |
| [17](./17-customer-portal.md) | Customer Portal | API Key认证、客户工单提交/回复、公开知识库、状态联动 |

## 框架能力覆盖矩阵

| 框架能力 | 覆盖 Spec | 优先级 |
|---------|----------|:------:|
| Multi-Tenancy (独立DB) | 01 | P0 |
| Dynamic API AoT | 03, 04, 05, 06, 07, 10, 16, 17 | P0 |
| OpenIddict 认证 | 02 | P0 |
| Authorization & Permission | 02, 16 | P0 |
| Setting Management | 06, 08, 09 | P0 |
| Feature Management | 07, 08, 09, 10, 16 | P0 |
| Audit Logging | 11 | P0 |
| EF Core Provider | 15 | P0 |
| File Management | 08 | P1 |
| Background Jobs (Quartz) | 06 | P1 |
| Caching (Redis + [CacheMo]) | 05, 07 | P1 |
| FluentValidation | 12 | P1 |
| Localization | 12 | P1 |
| Domain Events | 14 | P1 |
| EventBus (Local) | 14 | P1 |
| Permission Sync | 02 | P1 |
| API Key / 自定义认证 | 17 | P1 |
| HealthCheck | 12 | P2 |
| Security Headers | 12 | P2 |
| Organization Hierarchy | 13 | P2 |
| Virtual File System | 13 | P2 |
| Audit Log Cleanup | 11 | P2 |
| AOP [UnitOfWorkMo] | 12 | P2 |
| ConcurrencyStamp | 15 | P2 |
| Data Filter | 15 | P2 |
| 加密 Setting | 09 | P2 |

## 变更记录

| 日期 | 变更 |
|------|------|
| 2026-06-02 | 初始版本，15 个 Spec 文档 |
| 2026-06-02 | 审计修复：新增 Spec 16-17（Agent Management, Customer Portal），修复 Spec 03/06/09 的 18 个问题 |
