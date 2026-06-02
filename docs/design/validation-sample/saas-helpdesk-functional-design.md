# SaaS Helpdesk — 功能设计（粗粒度）

## 1. 背景与目标

CrestCreates 框架已具备 49 个模块的平台能力，但现有 LibraryManagement 示例仅覆盖了核心主链的约 40%。本项目的目标是构建一个足够复杂的 **SaaS 工单系统**，作为框架全功能验证的正式示例项目。

选择工单系统作为验证场景的原因：

- **天然多租户**：每个租户有独立的客服团队、客户、工单数据
- **业务层次丰富**：从简单 CRUD 到 SLA 调度、文件管理、组织层级权限，覆盖多层次业务场景
- **真实需求驱动**：不是为验证而验证，每个框架能力的引入都有明确的业务理由

### 验证范围

本项目需验证以下框架能力（按覆盖度排序）：

| 优先级 | 框架能力 | 业务映射 |
|--------|---------|---------|
| P0 | Multi-Tenancy（独立DB） | 每租户独立数据库，完整租户生命周期 |
| P0 | Dynamic API AoT | 全部 CRUD 接口通过编译期生成 |
| P0 | OpenIddict 认证 | 客服登录、角色权限、RefreshToken |
| P0 | Setting Management | SLA 时间窗口、通知策略、自动关闭天数 |
| P0 | Feature Management | 套餐分级（最大客服数、工单量、存储量） |
| P0 | Authorization & Permission | 工单 CRUD 权限、管理权限、角色划分 |
| P0 | Audit Logging | 工单状态变更、分配变更、SLA 违约全量审计 |
| P0 | EF Core Provider | 仓储层、UnitOfWork、数据过滤 |
| P1 | File Management | 工单附件上传/下载（本地 + S3） |
| P1 | Background Jobs (Quartz) | SLA 违约检测、自动关闭过期工单、周报 |
| P1 | Caching (Redis + [CacheMo]) | 知识库缓存、分类树缓存 |
| P1 | FluentValidation | 工单创建校验、附件类型/大小校验 |
| P1 | Localization | 中英文界面、错误消息 |
| P1 | Domain Events | 工单创建/分配/解决事件 |
| P1 | Permission Sync | 编译期权限清单同步到数据库 |
| P2 | HealthCheck | DB/Redis/文件存储连通性检查 |
| P2 | Security Headers | HSTS、CSRF、X-Frame-Options |
| P2 | Organization Hierarchy | 客服团队层级、客户公司组织 |
| P2 | Virtual File System | 嵌入邮件通知模板 |
| P2 | Audit Log Cleanup | 定期清理过期审计日志 |
| P2 | Event Bus (Local) | 工单事件驱动通知 |
| P2 | AOP [UnitOfWorkMo] | 复杂工单操作的事务边界 |

### 非目标

本项目不做以下内容：

- 真实邮件/短信发送（仅做事件占位）
- 实时聊天/WebSocket
- 第三方系统集成（CRM、电话系统等）
- 支付/计费系统
- AI/智能路由
- 移动端 API
- RabbitMQ/Kafka/CAP 分布式验证（另建验证项目）

---

## 2. 设计原则

1. **每项框架能力必须有业务理由**：不引入"仅用于演示"的代码，每个 Setting、Feature、Event 都对应真实工单业务需求
2. **优先走编译期生成主链**：所有 Dynamic API、权限清单、模块注册都走 SourceGenerator
3. **不维护双轨实现**：不写 runtime reflection fallback，不保留"也能跑"的备用路径
4. **分层严格遵守**：Domain.Shared → Domain → Application.Contracts → Application → Infrastructure(OrmProviders.EFCore) → Web
5. **与 LibraryManagement 不重复**：已有示例验证过的路径不再重复验证，如共享DB多租户、Swagger基础配置

---

## 3. 业务领域模型

### 3.1 核心聚合

```
┌─────────────┐     ┌──────────────┐     ┌──────────────────┐
│   Tenant    │────→│   Customer   │     │   KnowledgeBase  │
│  (框架实体)  │     │  (客户)       │     │   Article (文章)  │
└─────────────┘     └──────────────┘     └──────────────────┘
       │                                         │
       ▼                                         ▼
┌─────────────┐     ┌──────────────┐     ┌──────────────────┐
│ IdentityUser│────→│    Agent     │     │    Category      │
│  (框架实体)  │     │  (客服人员)   │     │   (工单分类)      │
└─────────────┘     └──────┬───────┘     └────────┬─────────┘
                           │                      │
                           ▼                      ▼
                    ┌──────────────────────────────────┐
                    │            Ticket (工单)          │
                    │  ┌────────────────────────────┐  │
                    │  │     TicketMessage (回复)     │  │
                    │  │  ┌──────────────────────┐  │  │
                    │  │  │ TicketAttachment(附件) │  │  │
                    │  │  └──────────────────────┘  │  │
                    │  └────────────────────────────┘  │
                    └──────────────────────────────────┘
                                    │
                           ┌────────┴─────────┐
                           ▼                  ▼
                    ┌──────────┐      ┌──────────────┐
                    │ SLAPolicy│      │ TicketHistory │
                    │ (SLA策略) │      │  (状态历史)    │
                    └──────────┘      └──────────────┘
```

### 3.2 核心实体定义

#### Ticket（工单）

```
Ticket : AuditedAggregateRoot<Guid>
├── Title           string
├── Description     string
├── Status          TicketStatus (Open/InProgress/WaitingOnCustomer/
│                                   WaitingOnThirdParty/Resolved/Closed)
├── Priority        TicketPriority (Low/Medium/High/Urgent)
├── Type            TicketType (Question/Incident/Problem/FeatureRequest)
├── CustomerId      Guid
├── AssigneeId      Guid? (客服人员)
├── CategoryId      Guid?
├── SLAPolicyId     Guid?
├── FirstResponseAt DateTime? (首次响应时间)
├── ResolvedAt      DateTime?
├── ClosedAt        DateTime?
├── DueBy           DateTime? (SLA截止时间)
├── IsOverdue       bool
├── Messages        List<TicketMessage>
│
├── Assign(agentId)
├── UpdateStatus(newStatus)
├── AddReply(message)
├── MarkOverdue()
├── Resolve()
├── Close()
└── CheckSLA()
```

#### TicketMessage（工单回复）

```
TicketMessage : AuditedEntity<Guid>
├── TicketId        Guid
├── Content         string
├── SenderType      MessageSenderType (Agent/Customer/System)
├── SenderId        Guid?
├── IsInternal      bool (内部备注，客户不可见)
├── Attachments     List<TicketAttachment>
└── IsSystemMessage bool (系统自动消息，如状态变更通知)
```

#### TicketAttachment（工单附件）

```
TicketAttachment : AuditedEntity<Guid>
├── TicketId        Guid?
├── MessageId       Guid?
├── FileName        string
├── FilePath        string (文件管理模块存储路径)
├── ContentType     string
├── FileSize        long
└── FileHash        string (sha256, 去重和完整性校验)
```

#### Customer（客户）

```
Customer : AuditedAggregateRoot<Guid>
├── TenantId        Guid
├── Name            string
├── Email           string
├── Phone           string?
├── Company         string?
├── OrganizationId  Guid? (组织层级)
├── IsActive        bool
├── Tickets         List<Ticket>
│
├── Deactivate()
├── Reactivate()
└── GetOpenTickets()
```

#### Agent（客服人员）

Agent 基于框架的 `IdentityUser`，通过角色区分：
- `Helpdesk.Agent`：普通客服
- `Helpdesk.Supervisor`：客服主管
- `Helpdesk.Admin`：租户管理员

Agent 扩展属性通过 `IdentityUser` 的 ExtraProperties 存储（技能组、最大并发工单数等），不新建实体表。

#### Category（工单分类）

```
Category : AuditedEntity<Guid>
├── TenantId        Guid
├── Name            string
├── Description     string?
├── ParentId        Guid? (自引用层级)
├── SortOrder       int
├── IsActive        bool
├── Parent          Category?
└── Children        List<Category>
```

#### KnowledgeBaseArticle（知识库文章）

```
KnowledgeBaseArticle : AuditedAggregateRoot<Guid>
├── TenantId        Guid
├── Title           string
├── Content         string
├── CategoryId      Guid?
├── IsPublished     bool
├── ViewCount       int
├── Tags            string? (逗号分隔标签)
│
├── Publish()
├── Unpublish()
├── IncrementViewCount()
└── UpdateContent(title, content)
```

#### Agent（客服人员 — 基于 IdentityUser）

Agent 基于框架 `IdentityUser` + 角色实现，不新建实体表。扩展属性通过 `ExtraProperties` 存储。

```
Agent 基于 IdentityUser 扩展：
├── UserName          (IdentityUser)
├── Email             (IdentityUser)
├── Name              (IdentityUser)
├── IsActive          (IdentityUser)
├── Roles             ["Helpdesk.Agent" / "Helpdesk.Supervisor" / "Helpdesk.Admin"]
├── SkillGroup        ExtraProperties["SkillGroup"]
├── MaxConcurrentTickets ExtraProperties["MaxConcurrentTickets"] (默认10)
│
└── 管理 API: 创建、更新、停用（自动重分配工单）、角色变更
```

#### Customer Portal（客户门户）

客户不登录为 IdentityUser，通过 API Key 认证。详见 [Spec 17](specs/17-customer-portal.md)。

```
Customer Portal 能力：
├── API Key 认证 (X-Customer-Key Header)
├── 创建工单（自动绑定 CustomerId）
├── 查看我的工单（自动过滤 IsInternal 消息）
├── 回复工单（触发 WaitingOnCustomer → InProgress 或 Resolved → Reopen）
├── 公开知识库浏览（无需认证）
└── IsActive 状态控制访问
```

#### SLAPolicy（SLA策略）

```
SLAPolicy : AuditedAggregateRoot<Guid>
├── TenantId        Guid
├── Name            string
├── Priority        TicketPriority
├── FirstResponseMinutes  int (首次响应时限，分钟)
├── ResolutionMinutes     int (解决时限，分钟)
├── IsActive        bool
├── BusinessHoursOnly     bool
│
├── Activate()
├── Deactivate()
└── CalculateDueBy(createdAt) → DateTime
```

#### TicketHistory（工单历史 - 审计用）

```
TicketHistory : AuditedEntity<Guid>
├── TicketId        Guid
├── FieldName       string
├── OldValue        string?
├── NewValue        string?
├── ChangeType      HistoryChangeType (Created/StatusChanged/
│                                       Assigned/Replied/Resolved/Closed/
│                                       Reopened/OverdueDetected)
├── ChangedById     Guid?
└── Summary         string
```

### 3.3 领域事件

| 事件 | 触发时机 | 消费者 |
|------|---------|--------|
| `TicketCreatedDomainEvent` | 工单创建 | SLA计时启动、通知主管 |
| `TicketAssignedDomainEvent` | 分配客服 | 通知被分配的客服 |
| `TicketStatusChangedDomainEvent` | 状态变更 | 通知客户、更新SLA状态 |
| `TicketResolvedDomainEvent` | 工单解决 | 满意度调查触发、SLA完成标记 |
| `TicketOverdueDomainEvent` | 工单逾期 | 通知主管、记录违约 |
| `CustomerCreatedDomainEvent` | 客户注册 | 欢迎通知 |

---

## 4. 模块与分层设计

### 4.1 项目结构

```
samples/SaaSHelpdesk/
├── SaaSHelpdesk.Domain.Shared/          # 共享常量、枚举、DTO标记
│   ├── HelpdeskEnums.cs                 # TicketStatus, Priority, Type 等枚举
│   ├── HelpdeskConstants.cs             # 常量（SLA默认值、文件名长度限制等）
│   └── HelpdeskPermissions.cs           # 权限常量
├── SaaSHelpdesk.Domain/                 # 领域层
│   ├── Entities/                        # Ticket, TicketMessage, Customer, Category,
│   │   │                                 KnowledgeBaseArticle, SLAPolicy, TicketHistory
│   │   └── TicketAttachment.cs
│   ├── DomainEvents/                    # 领域事件定义
│   ├── DomainServices/                  # ISLACalculator, ITicketAssignmentService
│   └── Repositories/                    # ITicketRepository, ICustomerRepository 等
├── SaaSHelpdesk.Application.Contracts/  # 应用层契约
│   ├── Dtos/                            # 各实体的 CRUD DTO
│   ├── ITicketAppService.cs
│   ├── ICustomerAppService.cs
│   ├── ICategoryAppService.cs
│   ├── IKnowledgeBaseAppService.cs
│   ├── ISLAPolicyAppService.cs
│   └── IDashboardAppService.cs          # 工单统计/仪表盘
├── SaaSHelpdesk.Application/            # 应用层实现
│   ├── TicketAppService.cs              # 继承 CrestAppServiceBase
│   ├── CustomerAppService.cs
│   ├── CategoryAppService.cs
│   ├── KnowledgeBaseAppService.cs
│   ├── SLAPolicyAppService.cs
│   ├── DashboardAppService.cs
│   ├── TicketAssignmentService.cs       # 领域服务实现
│   ├── SLACalculator.cs                 # SLA 时效计算
│   └── EventHandlers/                   # 领域事件处理器
├── SaaSHelpdesk.EntityFrameworkCore/    # EF Core 仓储实现
│   ├── EntityFrameworkCore/
│   │   ├── HelpdeskDbContext.cs
│   │   └── Repositories/
│   └── Migrations/
└── SaaSHelpdesk.Web/                    # Web 层/启动项目
    ├── HelpdeskWebModule.cs             # CrestModule 入口
    ├── appsettings.json
    └── Program.cs
```

### 4.2 各层职责

| 层 | 职责 | 禁止 |
|----|------|------|
| Domain.Shared | 枚举、常量、权限名称、Setting定义名称、Feature定义名称 | 不包含实体逻辑 |
| Domain | 实体、值对象、领域事件、领域服务接口、仓储接口、Setting/Feature DefinitionProvider | 不引用 Application/Web |
| Application.Contracts | AppService 接口、DTO、IQueryRequest 标记 | 不引用 Domain 实现 |
| Application | AppService 实现、领域服务实现、事件处理器、Setting/Feature Manager调用 | 不处理 HTTP 层关注点 |
| OrmProviders.EFCore | DbContext、仓储实现、Migration | 不包含业务逻辑 |
| Web | 模块注册、中间件配置、租户初始化、种子数据 | 不包含应用编排逻辑 |

---

## 5. 框架能力 → 业务功能映射

### 5.1 Multi-Tenancy（独立数据库）

**业务场景**：每个企业客户（租户）拥有完全隔离的数据库。

**验证点**：
- `TenantBootstrapper` 创建新租户时自动创建独立数据库
- `TenantInitializationOrchestrator` 运行 Migration 并种子化基础数据
- Header-based 租户解析（`X-Tenant-Id`）
- 数据库隔离策略（`Database` 模式，非 Discriminator）
- 租户删除（`TenantDeletionManager` + `TenantDeletionGuard`）
- 租户诊断（`TenantDiagnosticsAppService` → 验证 DB 连接、Migration 状态、实体计数）

### 5.2 Dynamic API AoT

**业务场景**：所有 CRUD 和查询接口通过编译期生成 Minimal API Endpoint。

**验证点**：
- `[CrestService]` 标记的 AppService 自动生成 Endpoint
- Generated Registry 是唯一入口，不存在 Runtime Scanner
- `FilterBuilder` / `SortBuilder` / `QueryRequest` 链式查询
- 分页查询（`PagedResultDto<T>`）
- `[IgnoreDynamicApi]` 排除不应暴露的方法
- 接口继承 Contract（如 `ICrudAppService<T>`）场景

### 5.3 OpenIddict 认证 & 授权

**业务场景**：客服人员通过 JWT 登录，按角色分配权限。

**角色划分**：
| 角色 | 权限范围 |
|------|---------|
| `Helpdesk.Agent` | 查看/处理分配的工单、回复客户、查看知识库 |
| `Helpdesk.Supervisor` | Agent所有权限 + 分配工单、查看所有工单、管理SLA、查看报表 |
| `Helpdesk.Admin` | Supervisor所有权限 + 管理客服人员、配置分类/SLA、租户设置 |

**验证点**：
- Password + Refresh Token 流程
- `IPermissionChecker` 拦截未授权操作
- `ICurrentUser` 获取当前客服信息
- 编译期生成的权限清单

### 5.4 Setting Management

**业务场景**：租户可在管理后台配置系统行为。

| Setting | 作用域 | 默认值 | 说明 |
|---------|--------|--------|------|
| `Helpdesk.Ticket.AutoCloseDays` | Tenant | 14 | 已解决工单自动关闭天数 |
| `Helpdesk.SLA.DefaultFirstResponseMinutes` | Tenant | 60 | 默认首次响应时限 |
| `Helpdesk.SLA.DefaultResolutionMinutes` | Tenant | 480 | 默认解决时限（8小时） |
| `Helpdesk.Notification.Enabled` | Tenant | true | 是否启用通知 |
| `Helpdesk.Notification.EmailTemplate.Customer` | Tenant | (内置模板) | 客户通知邮件模板 |
| `Helpdesk.Attachment.MaxFileSizeMB` | Tenant | 10 | 附件最大大小 |
| `Helpdesk.Attachment.AllowedTypes` | Tenant | jpg,jpeg,png,gif,pdf,doc,docx,xls,xlsx,txt,csv,zip | 允许的附件类型 |
| `Helpdesk.General.Timezone` | Tenant | Asia/Shanghai | 租户时区（IANA格式） |
| `Helpdesk.General.Language` | User | zh-CN | 用户语言偏好 |

**验证点**：
- `ISettingDefinitionProvider` 注册定义
- Global / Tenant / User 三级作用域
- 加密 Setting（如邮件服务器密码）
- Setting 缓存命中

### 5.5 Feature Management

**业务场景**：不同套餐（免费/专业/企业）有不同的功能限制。

| Feature | 默认值 | 说明 |
|---------|--------|------|
| `Helpdesk.MaxAgents` | 3 | 最大客服数 |
| `Helpdesk.MaxTicketsPerMonth` | 100 | 月工单上限 |
| `Helpdesk.StorageLimitMB` | 500 | 附件存储上限 |
| `Helpdesk.KnowledgeBase.Enabled` | true | 是否启用知识库 |
| `Helpdesk.SLACustomization` | false | 是否允许自定义SLA策略 |
| `Helpdesk.CustomDomain` | false | 是否支持自定义域名 |
| `Helpdesk.Reports.Enabled` | true | 是否启用报表 |
| `Helpdesk.API.Access` | false | 是否启用API访问 |

**验证点**：
- `IFeatureDefinitionProvider` 注册定义
- `IFeatureChecker` 在应用层做功能开关检查
- 租户创建时 `TenantFeatureDefaultsSeeder` 种子化默认值
- Feature 值变更后缓存失效

### 5.6 Audit Logging

**业务场景**：工单全生命周期操作审计。

**审计事件**：
| 事件类型 | 触发操作 |
|---------|---------|
| `Ticket.Created` | 创建工单 |
| `Ticket.Assigned` | 分配工单给客服 |
| `Ticket.StatusChanged` | 状态变更（含旧→新） |
| `Ticket.Resolved` | 解决工单 |
| `Ticket.Closed` | 关闭工单 |
| `Ticket.Replied` | 添加回复 |
| `SLA.Breached` | SLA 违约 |
| `Customer.Created/Updated/Deactivated` | 客户管理操作 |
| `Agent.Created/Deactivated` | 客服管理操作 |
| `Category.Created/Updated/Deleted` | 分类管理操作 |

**验证点**：
- `AuditLoggingMiddleware` 正常采集
- `IAuditLogRedactor` 脱敏（如客户邮箱部分隐藏）
- `IAuditLogWriter` 持久化
- `AuditLogCleanupAppService` 定期清理策略

### 5.7 File Management

**业务场景**：工单附件上传、预览、下载。

**验证点**：
- `IFileManagementService` 上传/下载
- `LocalFileSystemProvider` 本地存储
- `S3StorageProvider`（可选）S3 存储切换
- `IFileUrlService` 生成访问URL
- 文件大小/类型校验（对接 Setting）
- 附件与工单/回复关联

### 5.8 Background Jobs (Quartz)

**业务场景**：系统定时任务。

| 任务 | 调度频率 | 说明 |
|------|---------|------|
| `SLAOverdueCheckJob` | 每5分钟 | 扫描所有未解决工单，检查SLA是否逾期 |
| `AutoCloseResolvedTicketsJob` | 每天2:00 | 自动关闭超过N天的已解决工单 |
| `WeeklyReportJob` | 每周一8:00 | 生成上周工单统计报表 |
| `AuditLogCleanupJob` | 每天3:00 | 清理超过保留期的审计日志 |

**验证点**：
- `IJob` 接口实现
- `QuartzSchedulerService` 注册和调度
- `[BackgroundJob]` 编译期生成
- Job 中正确获取租户上下文（`ICurrentTenant`）

### 5.9 Caching (Redis + [CacheMo])

**业务场景**：缓存高频访问数据，减少数据库查询。

| 缓存项 | 策略 | TTL | 说明 |
|--------|------|-----|------|
| 分类树 | 主动失效 | 30 min | 工单分类选择时高频读取 |
| 知识库热门文章 | 被动过期 | 10 min | 首页/仪表盘展示 |
| SLA策略列表 | 主动失效 | 30 min | SLA计算时读取 |
| 租户Setting | 主动失效 | 10 min | 避免每次请求读Setting |

**验证点**：
- `ICrestCacheService` 缓存读写
- Redis 分布式缓存（`StackExchange.Redis`）
- `[CacheMo]` AOP 拦截器自动缓存方法返回值
- 主动失效（修改分类/SLA/Setting时清除对应缓存）

### 5.10 FluentValidation

**业务场景**：输入校验。

**验证器**：
| 验证对象 | 规则示例 |
|---------|---------|
| `CreateTicketDto` | Title 必填(5-200字符)、CustomerId 非空、CategoryId 非空 |
| `CreateCustomerDto` | Name 必填、Email 格式校验、Phone 格式校验（可选） |
| `UploadAttachmentDto` | 文件大小 ≤ Setting最大值、ContentType 在允许列表中 |
| `CreateKnowledgeBaseDto` | Title 必填(5-200字符)、Content 必填(≥20字符) |

**验证点**：
- `IValidationService` 集成
- 错误消息中文/英文（对接 Localization）
- Setting 值驱动的动态校验规则（如最大文件大小从Setting读取）

### 5.11 Localization

**业务场景**：支持中文和英文界面。

**验证点**：
- `ILocalizationService` 获取本地化文本
- `RequestLocalizationOptions` 中英文切换（`zh-CN` / `en`）
- 错误消息本地化（FluentValidation 错误）
- 邮件模板本地化
- 权限名称/描述本地化

### 5.12 HealthCheck

**业务场景**：监控系统各组件健康状态。

**检查项**：
- 数据库连接（`TenantHealthCheck`）
- Redis 连接
- 文件存储服务可用性
- 各外部依赖状态

**验证点**：
- `/health` 端点可访问
- ASP.NET Core HealthCheck 集成
- 返回 JSON 格式健康报告

### 5.13 Organization Hierarchy

**业务场景**：客服团队层级管理和客户公司归属。

**验证点**：
- `OrganizationHierarchyService` 构建团队树
- 客服主管查看下属客服的工单
- 客户公司的父/子组织关系
- 权限与组织层级联动（上级可见下级数据）

### 5.14 Virtual File System

**业务场景**：嵌入邮件通知模板、系统默认配置。

**验证点**：
- `IVirtualFileSystem` 读取嵌入资源
- `VfsModuleDiscovery` 扫描程序集嵌入文件
- 邮件模板（`EmailTemplates/ticket_created.html` 等）

### 5.15 其他验证点

| 能力 | 验证方式 |
|------|---------|
| Security Headers | `AddCrestSecurity()` 启用 HSTS/CSRF/安全头 |
| AOP [UnitOfWorkMo] | `TicketAppService.Assign()` 等复杂操作加事务标记 |
| Domain Events | LocalEventBus 订阅 `TicketCreatedDomainEvent` → 发送通知 |
| Permission Sync | `PermissionSyncHostedService` 将生成权限同步到 DB |
| Query Building | `FilterBuilder<Ticket>` + `SortBuilder<Ticket>` + `QueryRequest<Ticket>` |
| ConcurrencyStamp | EF Core 乐观锁配置 |
| Data Filter | Soft Delete（分类、SLA策略） + Tenant Filter |

---

## 6. 用户故事（核心流程）

### 6.1 租户入驻

```
1. 平台管理员创建新租户 (TenantAppService.Create)
2. TenantBootstrapper 创建独立数据库
3. TenantInitializationOrchestrator 运行 Migration + 种子数据
4. 创建租户管理员账号
5. 租户管理员登录 → 配置分类、SLA、客服账号
6. 准备就绪 → 开始接收工单
```

### 6.2 工单全生命周期

```
客户提交工单 → [Open]
    │
    ▼
客服认领/分配 → [InProgress] ────→ 需要客户补充信息 → [WaitingOnCustomer]
    │                                       │
    │                                       ▼
    │                              客户回复 → [InProgress]
    │
    ├──→ 需要第三方处理 → [WaitingOnThirdParty]
    │                           │
    │                           ▼
    │                   第三方回复 → [InProgress]
    │
    ▼
客服解决工单 → [Resolved] ────→ N天后自动关闭 → [Closed]
                    │
                    ▼
              客户回复重新打开 → [InProgress]
```

### 6.3 SLA 监控

```
工单创建 → SLA计时开始
    │
    ├──→ 超时未首次响应 → FirstResponseOverdue → 通知主管
    │
    └──→ 超时未解决 → ResolutionOverdue → 通知主管 + 升级
```

### 6.4 仪表盘

```
租户管理员/主管查看仪表盘：
├── 今日新增工单数
├── 各状态工单分布
├── 各客服工作量分布
├── SLA 达标率
├── 本月工单趋势图
└── 知识库热门文章 TOP 10
```

---

## 7. 强制验证 Checklist

以下每一项必须在测试中有对应覆盖：

### 多租户
- [ ] 租户A的客服看不到租户B的工单
- [ ] 租户创建的独立DB可正常Migration
- [ ] 租户删除后数据库被清理
- [ ] 后台Job运行时租户上下文正确

### Dynamic API
- [ ] 工单CRUD全部通过Generated Endpoint访问
- [ ] Filter/Sort/Page查询链正常工作
- [ ] 不存在Runtime Scanner路径

### 认证授权
- [ ] 无Token访问被拦截
- [ ] Agent角色无法调用Admin接口
- [ ] RefreshToken刷新正常

### Setting
- [ ] 租户A修改AutoCloseDays不影响租户B
- [ ] Setting变更后缓存失效
- [ ] 加密Setting正确存储

### Feature
- [ ] 免费套餐无法添加超过3个客服
- [ ] Feature Checker在应用层生效
- [ ] 套餐升级后Feature限额即时生效

### 文件管理
- [ ] 附件上传成功后生成可访问URL
- [ ] 超过Setting限制大小的文件被拦截
- [ ] 非法ContentType文件被拦截

### 后台任务
- [ ] SLA逾期检测定时任务正常触发
- [ ] 自动关闭任务的工单状态正确变更
- [ ] Job中租户上下文不丢失

### 缓存
- [ ] 分类树缓存命中后不查DB
- [ ] 知识库修改后缓存自动失效
- [ ] [CacheMo] 拦截器生效

### 审计
- [ ] 工单状态变更产生审计记录
- [ ] 敏感字段脱敏
- [ ] 审计清理任务正确执行

---

## 8. 实施分期

### Phase 1: 核心骨架（Week 1-2）
- 项目结构和模块搭建
- Domain.Shared + Domain 核心实体
- EF Core DbContext + Migration
- OpenIddict 认证配置
- 租户独立DB创建链路
- 种子数据

### Phase 2: 工单主链（Week 3-4）
- Ticket/Customer/Category/Message 完整CRUD
- Dynamic API AoT 验证
- FilterBuilder/SortBuilder 查询
- Permission & Role 权限矩阵
- Audit Logging 接入

### Phase 3: 平台能力整合（Week 5-6）
- Setting/Feature Management 完整接入
- File Management 附件上传
- SLA Policy 实体 + 计算
- Dashboard 统计接口
- FluentValidation 校验
- Localization 中文/英文

### Phase 4: 调度与缓存（Week 7-8）
- Quartz 后台任务（SLA检测、自动关闭、周报）
- Redis 缓存 + [CacheMo]
- KnowledgeBase 知识库
- Organization Hierarchy 团队层级
- Virtual File System 邮件模板

### Phase 5: 运维与收尾（Week 9-10）
- HealthCheck 端点
- Security Headers 配置
- Permission Sync 同步
- Audit Log Cleanup
- 完整集成测试
- 文档编写
