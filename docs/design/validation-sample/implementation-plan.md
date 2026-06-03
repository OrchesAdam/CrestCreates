# SaaS Helpdesk — 实施计划

## 1. 总体结构

```
samples/SaaSHelpdesk/
├── SaaSHelpdesk.sln
├── src/
│   ├── SaaSHelpdesk.Domain.Shared/
│   │   ├── SaaSHelpdesk.Domain.Shared.csproj
│   │   ├── HelpdeskEnums.cs
│   │   ├── HelpdeskConstants.cs
│   │   ├── HelpdeskPermissions.cs
│   │   ├── HelpdeskSettings.cs
│   │   ├── HelpdeskFeatures.cs
│   │   └── HelpdeskErrorCodes.cs
│   ├── SaaSHelpdesk.Domain/
│   │   ├── SaaSHelpdesk.Domain.csproj
│   │   ├── HelpdeskDomainModule.cs
│   │   ├── Entities/
│   │   │   ├── Ticket.cs
│   │   │   ├── TicketMessage.cs
│   │   │   ├── TicketAttachment.cs
│   │   │   ├── Customer.cs
│   │   │   ├── Category.cs
│   │   │   ├── KnowledgeBaseArticle.cs
│   │   │   ├── SLAPolicy.cs
│   │   │   └── TicketHistory.cs
│   │   ├── DomainEvents/
│   │   ├── DomainServices/
│   │   ├── Settings/
│   │   │   └── HelpdeskSettingDefinitionProvider.cs
│   │   ├── Features/
│   │   │   └── HelpdeskFeatureDefinitionProvider.cs
│   │   └── Repositories/
│   │       ├── ITicketRepository.cs
│   │       └── ICustomerRepository.cs
│   ├── SaaSHelpdesk.Application.Contracts/
│   │   ├── SaaSHelpdesk.Application.Contracts.csproj
│   │   ├── HelpdeskApplicationContractsModule.cs
│   │   ├── Dtos/
│   │   │   ├── Tickets/
│   │   │   ├── Customers/
│   │   │   ├── Categories/
│   │   │   ├── KnowledgeBase/
│   │   │   └── SLAPolicies/
│   │   ├── ITicketAppService.cs
│   │   ├── ICustomerAppService.cs
│   │   ├── ICategoryAppService.cs
│   │   ├── IKnowledgeBaseAppService.cs
│   │   ├── ISLAPolicyAppService.cs
│   │   └── IDashboardAppService.cs
│   ├── SaaSHelpdesk.Application/
│   │   ├── SaaSHelpdesk.Application.csproj
│   │   ├── HelpdeskApplicationModule.cs
│   │   ├── TicketAppService.cs
│   │   ├── CustomerAppService.cs
│   │   ├── CategoryAppService.cs
│   │   ├── KnowledgeBaseAppService.cs
│   │   ├── SLAPolicyAppService.cs
│   │   ├── DashboardAppService.cs
│   │   ├── DomainServices/
│   │   │   ├── TicketAssignmentService.cs
│   │   │   └── SLACalculator.cs
│   │   ├── EventHandlers/
│   │   │   ├── TicketCreatedHandler.cs
│   │   │   ├── TicketAssignedHandler.cs
│   │   │   └── TicketOverdueHandler.cs
│   │   └── Validators/
│   │       ├── CreateTicketDtoValidator.cs
│   │       ├── CreateCustomerDtoValidator.cs
│   │       └── UploadAttachmentDtoValidator.cs
│   └── SaaSHelpdesk.Web/
│       ├── SaaSHelpdesk.Web.csproj
│       ├── HelpdeskWebModule.cs
│       ├── Program.cs
│       ├── Localization/
│       │   ├── zh-CN.json
│       │   └── en.json
│       ├── EmailTemplates/
│       │   ├── ticket_created.html
│       │   ├── ticket_assigned.html
│       │   └── sla_warning.html
│       └── appsettings.json
└── test/
    ├── SaaSHelpdesk.Domain.Tests/
    ├── SaaSHelpdesk.Application.Tests/
    └── SaaSHelpdesk.IntegrationTests/
```

## 2. 分期实施详情

### Phase 1: 核心骨架

**目标**：项目可启动、租户可创建、认证可用

| 任务 | 产出 | 框架能力 |
|------|------|---------|
| 1.1 创建解决方案和项目结构 | .sln + 6个 .csproj | Modularity |
| 1.2 编写 Module 类 | 6个 `[CrestModule]` 类，声明依赖链 | Modularity |
| 1.3 创建 Domain 核心实体 | Ticket, Customer, Category, SLAPolicy | Domain |
| 1.4 创建 EF Core DbContext | HelpdeskDbContext + Migration | EF Core |
| 1.5 配置 OpenIddict | WebModule 中注册认证服务 | Auth |
| 1.6 配置多租户 | Header解析、独立DB策略、TenantBootstrapper | MultiTenancy |
| 1.7 种子数据 | 演示租户、管理员账号、默认分类 | MultiTenancy |
| 1.8 编写 Phase 1 集成测试 | 租户创建→DB创建→管理员登录→创建分类 | IntegrationTests |

### Phase 2: 工单主链

**目标**：工单全生命周期可用

| 任务 | 产出 | 框架能力 |
|------|------|---------|
| 2.1 创建 DTO 和 Contract 接口 | 各 CRUD DTO + AppService接口 | Application.Contracts |
| 2.2 实现 AppService | TicketAppService 等继承CrestAppServiceBase | Application |
| 2.3 Dynamic API 注册 | 编译期生成全部 Endpoint | DynamicApi AoT |
| 2.4 FilterBuilder/SortBuilder | 工单列表多条件过滤+排序分页 | Application.Contracts |
| 2.5 权限模型 | Agent/Supervisor/Admin 三级权限矩阵 | Authorization |
| 2.6 审计接入 | 工单状态变更审计 | AuditLogging |
| 2.7 TicketMessage 回复链 | 客服/客户/系统消息三通道 | Domain |
| 2.8 编写 Phase 2 集成测试 | 完整工单CRUD、权限拦截、审计记录验证 | IntegrationTests |

### Phase 3: 平台能力整合

**目标**：Setting/Feature/File/校验/本地化全部就位

| 任务 | 产出 | 框架能力 |
|------|------|---------|
| 3.1 Setting定义 + Provider | HelpdeskSettingDefinitionProvider | Setting Management |
| 3.2 Feature定义 + Provider | HelpdeskFeatureDefinitionProvider | Feature Management |
| 3.3 配置文件管理 | LocalFileSystemProvider注册 | File Management |
| 3.4 附件上传对接 | 工单/回复附件上传下载 | File Management + Setting |
| 3.5 FluentValidation | 4个Validator | Validation |
| 3.6 多语言 | zh-CN.json / en.json | Localization |
| 3.7 SLA Policy CRUD | 完整SLA策略管理 | Setting |
| 3.8 Dashboard 统计接口 | 工单统计、代理人工作量 | Application |
| 3.9 编写 Phase 3 集成测试 | Setting作用域、Feature限制、文件校验、多语言 | IntegrationTests |

### Phase 4: 调度与缓存

**目标**：后台任务、缓存、知识库、组织层级就位

| 任务 | 产出 | 框架能力 |
|------|------|---------|
| 4.1 Quartz 配置与注册 | Scheduler服务启动 | Scheduling.Quartz |
| 4.2 SLA逾期检测Job | SLAOverdueCheckJob | Scheduling + MultiTenancy |
| 4.3 自动关闭工单Job | AutoCloseResolvedTicketsJob | Scheduling |
| 4.4 周报生成Job | WeeklyReportJob | Scheduling |
| 4.5 Redis缓存配置 | StackExchange.Redis注册 | Caching |
| 4.6 [CacheMo] 应用 | 分类树/知识库方法缓存 | AOP + Caching |
| 4.7 知识库完整CRUD | KnowledgeBaseArticle + 浏览量 | Application |
| 4.8 组织层级 | OrganizationHierarchyService | Infrastructure |
| 4.9 VFS邮件模板 | 嵌入资源→模板渲染 | VirtualFileSystem |
| 4.10 编写 Phase 4 集成测试 | Job执行验证、缓存命中、VFS读取 | IntegrationTests |

### Phase 5: 运维与收尾

**目标**：健康检查、安全配置、完整测试覆盖

| 任务 | 产出 | 框架能力 |
|------|------|---------|
| 5.1 HealthCheck端点 | /health 端点 + 各组件检查 | HealthCheck |
| 5.2 安全头配置 | CSRF/HSTS/X-Frame等 | Security |
| 5.3 Permission Sync | 权限清单同步 | Application |
| 5.4 Audit Log Cleanup | 清理Job + 策略 | AuditLogging |
| 5.5 租户删除链路 | TenantDeletionManager | MultiTenancy |
| 5.6 Domain Events 端到端 | 事件发布→处理→通知占位 | EventBus.Local |
| 5.7 TicketHistory 记录 | 每个状态变更写History | Domain |
| 5.8 AOP [UnitOfWorkMo] | 复杂操作事务标记 | AOP |
| 5.9 全量集成测试 | 端到端业务流程 | IntegrationTests |
| 5.10 文档 | README + 架构说明 | - |

## 3. 外部依赖

| 依赖 | 用途 | 备注 |
|------|------|------|
| PostgreSQL | 主数据库 | 每租户独立DB |
| Redis | 分布式缓存 | Phase 4 |
| Quartz.NET | 后台任务调度 | Phase 4 |
| 本地文件系统 | 附件存储（开发环境） | Phase 3 |
| S3/MinIO | 附件存储（可选） | Phase 3 |

## 4. 不做但解释原因

| 不做项 | 原因 |
|--------|------|
| FreeSql/SqlSugar/MongoDB Provider | 需单独验证，在后续多ORM验证项目中覆盖 |
| RabbitMQ/Kafka EventBus | 需单独验证，在后续微服务事件驱动项目中覆盖 |
| CAP 分布式事务 | 同上 |
| 插件系统 | 业务场景不需要热加载，后续单独验证 |
| 独立OAuth模块 | 已用OpenIddict覆盖OAuth流程 |
| 真实邮件发送 | 仅做事件占位，不接入SMTP |
| 实时通知（WebSocket） | 非框架能力验证核心目标 |

## 5. 风险

| 风险 | 缓解 |
|------|------|
| 每租户独立DB导致测试环境复杂 | Phase 1 使用 Docker Compose 预置 PostgreSQL，CI 中通过测试容器动态创建/销毁租户DB |
| Redis 在 CI 环境不可用 | CI 中通过 Docker Compose 预置 Redis；Phase 4 缓存允许退化为内存缓存 |
| Quartz 调度与多租户上下文传递 | 参考框架现有Job实现模式，先写单元测试验证上下文传递 |
| SLA业务规则复杂度 | 先实现最简版本（固定时间），再扩展 BusinessHours/Calendar |
