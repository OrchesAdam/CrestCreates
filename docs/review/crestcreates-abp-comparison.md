# CrestCreates vs ABP Framework 全面对比分析

> **生成时间**: 2026-05  
> **方法**: 基于 `framework/src/` 全部 46 个项目的逐模块源码阅读 + `framework/test/` 24 个测试项目覆盖度分析  
> **评分基准**: ABP Framework 最新版本作为参照，每个功能点从架构设计、功能完整度、AoT 兼容性、生态系统四个维度综合打分

---

## 评分标准

| 评分 | 含义 |
|------|------|
| **9-10** | 生产就绪、功能完整、与 ABP 对标或超越 |
| **7-8** | 实现良好，存在少量差距 |
| **5-6** | 功能基础已建立，存在明显差距 |
| **3-4** | 早期阶段，仅有基本骨架 |
| **1-2** | 缺失或刚启动 |

---

## 一、核心基础设施

### 1.1 模块系统 — ⭐ 8.5/10

**ABP 做法**:  
运行时反射扫描 `[DependsOn]` 属性，构建 `AbpModule` 依赖树，按阶段触发。Castle DynamicProxy 辅助拦截。主链是运行时路径。

**CrestCreates 做法**:  
编译期 MSBuild 任务 `ScanModulesFromSource` → `GenerateAggregatedModuleCode` 生成 `ModuleAutoInitializer.g.cs`。Roslyn Source Generator 补充每个模块的 `AddXxxModule()` 扩展方法。主链是编译期路径。

**对比如下**:

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 生命周期 | 5 阶段 (PreInit/ConfigureServices/Init/PostInit/AppInit) | 5 阶段 (ConfigureServices/PreInit/Init/PostInit/AppInit)，顺序微调 |
| 依赖声明 | `[DependsOn]` 属性 | `[CrestModule(DependsOn = ...)]` 属性 |
| 模块发现 | 运行时反射扫描 | 编译期源码扫描 + 正则提取 |
| 拓扑排序 | 运行时计算 | 编译期拓扑排序 + 循环依赖检测 |
| 注册代码 | 运行时 `AddAbp()` 调用 | 编译期生成 `ModuleAutoInitializer.g.cs` |
| AoT 兼容 | ❌ 依赖运行时反射 | ✅ 编译期生成，无运行时反射 |

**结论**: 架构方向更正确——编译期生成为主链是 ABP 想做但没做到的。扣分在于生态成熟度和诊断工具不如 ABP 丰富。

---

### 1.2 实体与领域层 — ⭐ 9.0/10

**实体层级**:

```
ABP:                           CrestCreates:
IEntity<TKey>                  IEntity<TId>
  └─ Entity<TKey>                 └─ Entity<TId>
       ├─ AggregateRoot<TKey>          ├─ AggregateRoot<TId>
       │    └─ AuditedAggregateRoot    │    └─ AuditedAggregateRoot
       └─ AuditedEntity                └─ AuditedEntity
                                           └─ FullyAuditedEntity
```

**功能覆盖**:

| 能力 | ABP | CrestCreates |
|------|-----|-------------|
| 实体基类 | ✅ Entity, AggregateRoot | ✅ 完全对等，含 FullyAudited |
| 审计接口 | ✅ IHasCreationTime, IHasCreator 等 | ✅ 完全对等，外加 `IHasConcurrencyStamp` |
| 值对象 | ✅ `ValueObject` + `GetEqualityComponents()` | ✅ 完全一致 |
| 领域事件 | ✅ `IDomainEvent : INotification` + MediatR | ✅ 完全一致 + `IHasDomainEvents` 实体接口 |
| 软删除 | ✅ `ISoftDelete` + 全局查询过滤 | ✅ 完全对等，interceptor 自动 DELETE→UPDATE 转换 |
| 乐观并发 | ✅ `IConcurrencyStamp` / RowVersion | ✅ `IHasConcurrencyStamp` + 仓储级校验 |
| 仓储模式 | ✅ `IRepository<TEntity,TKey>` | ✅ 双层：`IRepository` (新抽象) + `CrestRepositoryBase` (领域主链) |
| 分页 | ✅ `IPagedResult` | ✅ `PagedResult<T>` (含 TotalPages/HasPrevious/HasNext) |

**结论**: 领域层设计与 ABP 高度一致，功能完全对等。FullyAudited 实体和 `IHasConcurrencyStamp` 是增量优势。

---

### 1.3 DI 与模块初始化 — ⭐ 8.5/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| DI 容器 | `IServiceCollection` 之上自建 `IAbpApplication` | 标准 `IHost` + `IServiceCollection`，无额外容器层 |
| 服务注册 | `[DependsOn]` 自动传播，`Context.Services` 注册 | `OnConfigureServices` 中注册，`[CrestModule]` 编译期收集 |
| 启动入口 | `AbpApplicationFactory.Create<TStartupModule>().Initialize()` | `.Build().InitializeModules().Run()` |
| 生命周期钩子 | `OnPreApplicationInit`, `OnApplicationInit` 等 5 个 | 5 个对应钩子，扩展 `ModuleBase` |

**结论**: 功能对等。CrestCreates 没有自建容器层，更紧密跟随标准 .NET 模式。ABP 的自定义启动入口对非标准场景更灵活。

---

## 二、API 与 Web 层

### 2.1 Dynamic API — ⭐ 8.0/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 发现机制 | 运行时反射扫描 `IRemoteService` 接口 | **编译期** Source Generator 扫描 `[CrestService]` |
| 端点生成 | MVC 控制器（运行时动态注册） | **Minimal API** （编译期生成 `MapGet/MapPost/...`） |
| 路由约定 | `/api/{service}/{action}`，kebab-case | 对等：`/api/{kebab-service}/{action-route}`，Async 后缀剥离 |
| HTTP 动词推断 | Create→POST, Get→GET, Update→PUT, Delete→DELETE | 完全一致 |
| 权限映射 | `{Service}.{Action}` 约定 | 完全一致 |
| Swagger 集成 | `ISwaggerProvider` 扩展 | `DynamicApiSwaggerDocumentFilter` + `x-permissions` 扩展 |
| AoT 兼容 | ❌ 运行时反射 | ✅ 设计为 AoT 优先，`[SuppressMessage("AOT")]` |
| 结果包装 | `IWrapResult` 接口 | `DynamicApiResponse<T>` 包装器 |
| 参数绑定 | `[FromRoute]`/`[FromBody]`/`[FromQuery]` 自动 | `DynamicApiParameterSource` 分类（Route/Body/Query） |
| 验证 | `[DataAnnotation]` + FluentValidation | `IValidationService` 自动验证 Body 参数 |
| UoW 事务 | `[UnitOfWork]` 属性 | `[UnitOfWorkMo]` + 生成的端点自动管理 |

**遗留路径**: 旧的 `DynamicApiSourceGenerator`（MVC 控制器版）仍在 CodeGenerator 项目中，已添加 `[Obsolete]` 标记。主链是 `DynamicApiAotSourceGenerator`。

**结论**: 架构方向正确——编译期 Minimal API 生成 + AoT 友好是 ABP 尚未解决的问题。差距在于 ABP 有更多属性级控制（`[RemoteService]`）和更丰富的 Swagger 集成。

---

### 2.2 MVC / Controllers — ⭐ 7.0/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 基类控制器 | `AbpControllerBase` | `ApiControllerBase`（在 `CrestCreates.Web` 中） |
| 响应包装 | 自动 `WrapResult` | 手动调用 `Success<T>()`/`Error()` 辅助方法 |
| 异常过滤 | `AbpExceptionFilter` 自动处理 | `CrestException` → HTTP 状态码体系，但缺全局过滤器 |
| 审计 | 自动捕获 | `[AuditedMo]` AOP 属性 + 中间件双层 |
| 控制器生成 | Dynamic API 运行时创建 | `ControllerSourceGenerator` 编译期生成 + `CrudControllerSourceGenerator` |

**结论**: 基础能力在，但缺少 ABP 的全局自动响应包装和异常过滤。编译期控制器生成是优势，但人工编控器的开发体验不如 ABP 完善。

---

## 三、多租户 — ⭐ 8.5/10

**ABP 核心模式**: `ICurrentTenant` + 解析中间件 + `IMultiTenant` 过滤。

**CrestCreates 对等且部分超越**:

| 能力 | ABP | CrestCreates |
|------|-----|-------------|
| 租户上下文 | `ICurrentTenant`（AsyncLocal） | `ICurrentTenant`（AsyncLocal），完全一致 |
| 解析策略 | Header / 子域名 / Query / Cookie / Route | 对等 5 种 + `CompositeTenantResolver` 组合器 |
| 解析组合 | 单个策略 | 有序组合，返回第一个成功结果 |
| 数据隔离 | 列（Discriminator）+ 数据库（连接字符串） | 对等双模式：`Database` / `Discriminator` |
| 租户管理 | `TenantManager` + 管理 UI | `ITenantManager`（CRUD + SetActive + Delete 三种策略） |
| **租户初始化** | 手动处理 | ✅ `TenantInitializationOrchestrator` 5 阶段管线（DB Init→Migration→Seed→Settings→Features） |
| **安全边界** | 无 | ✅ `TenantBoundaryMiddleware` 验证用户所属租户 == 当前上下文 |
| 连接字符串 | 标准加密 | Base64 编码 + Mask 方法 |
| 连接字符串 | 共享/独立 | 支持 `ITenantInfo.ConnectionString` 共享或独立 |
| EF 模型缓存 | 无 | `TenantAwareModelCacheKeyFactory` 每租户独立编译模型 |
| **代码生成** | 无 | `TenantFilterSourceGenerator` 自动生成 `TenantFilter.g.cs` |

**结论**: 多租户是 CrestCreates 的强项。`TenantInitializationOrchestrator` 是 ABP 没有的领域能力，`TenantBoundaryMiddleware` 增加了安全纵深。主要差距在于缺少管理 UI。

---

## 四、认证与授权

### 4.1 权限系统 — ⭐ 8.0/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 权限定义 | `PermissionDefinition` + `IPermissionDefinitionProvider` | 完全对等 |
| 权限组 | `PermissionGroupDefinition` | 完全对等 |
| 权限检查 | `IPermissionChecker.IsGrantedAsync()` | 对等 + 批量检查 `IsGrantedAsync(string[])` |
| 授予 | `PermissionGrantManager` + 缓存 | 对等 + 5 分钟 TTL 缓存 |
| 授予类型 | User / Role | User / Role（`ProviderType` 枚举） |
| 超级管理员 | `is_super_admin` claim | 完全一致 |
| AOP 拦截 | `[AbpAuthorize]` | `[PermissionMo]`（Rougamo 编译期 AOP） |
| **自动代码生成** | 手动定义 | ✅ `[Entity]` → `XxxPermissions` 类自动生成 |
| **租户作用域验证** | 无 | ✅ `TenantPermissionScopeValidator` 验证授予作用域 |

**结论**: 核心功能对等，自动代码生成和租户作用域验证是增量优势。ABP 的管理 UI 和生态成熟度更强。

---

### 4.2 OAuth / OpenIddict — ⭐ 7.5/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 集成 | OpenIddict + 管理 UI | `CrestCreates.AspNetCore.Authentication.OpenIddict` 模块 |
| Token | 完整（access/refresh/authorization code/client credentials） | 基础 token/refresh |
| 外部登录 | 微信/Google/Microsoft 等丰富 | `CrestCreates.AspNetCore.Authentication.OAuth` 模块（范围有限） |
| Claims 构建 | `IAbpClaimsPrincipalFactory` | `IdentityClaimsBuilder`（有单元测试） |

**结论**: ABP 远更成熟。CrestCreates 有基础 OAuth 和 OpenIddict 模块但范围和生态差距明显。

---

## 五、应用服务层 — ⭐ 8.0/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 基类 | `CrudAppService<TEntity,TDto,...>` | `CrestAppServiceBase<TEntity,TKey,TDto,TCreateDto,TUpdateDto>`（主链） |
| 旧基类 | 无 | `CrudServiceBase`（已 `[Obsolete]`） |
| 权限检查 | 手动或 `[Authorize]` | 自动检查 Create/Read/Update/Delete/Search |
| 审计 | 手动设置 | 自动设置 CreatorId/CreationTime/... |
| 数据权限 | 手动集成 | 自动 `IDataPermissionFilter` 租户/组织过滤 |
| 并发 | 手动处理 | 内置 `IHasConcurrencyStamp` 验证 |
| 动态查询 | `IQueryable` 扩展 | `QueryExecutor<T>` + `FilterDescriptor`/`SortDescriptor` |
| **代码生成** | 手动编写 | ✅ `[GenerateCrudService]` → 完整 CRUD 服务 + DTO + 映射 + 验证 + 权限 |

**结论**: 现代 CRUD 服务设计更完善——内置安全检查、数据权限、并发验证。`[GenerateCrudService]` 的代码生成能显著减少样板代码。`CrudServiceBase` 旧路径需清理。

---

## 六、配置管理

### 6.1 设置管理 — ⭐ 8.0/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 定义 | `SettingDefinition` + `ISettingDefinitionProvider` | 对等 + `ValueType`（String/Boolean/Int/Decimal/Json/EncryptedString） |
| 作用域 | Global / Tenant / User | 对等（`SettingScope` Flags） |
| 值解析 | `ISettingManager` + 作用域回退 | `ISettingProvider` + `ISettingValueResolver`，回退链：User→Tenant→Global→Default |
| 加密 | 无 | ✅ `EncryptedString` + `ISettingEncryptionService` |
| 缓存 | 基础 | ✅ `SettingCacheInvalidator` + `SettingCacheKeyContributor` |
| 管理 API | CRUD 应用服务 | `SettingAppService`（Get/Set/Delete per scope） |

**结论**: 超过 ABP——加密设置、更丰富的值类型、更完善的缓存失效。ABP 的 UI 管理是主要差距。

---

### 6.2 功能管理 — ⭐ 7.0/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 定义 | `FeatureDefinition` + `IFeatureDefinitionProvider` | 对等定义 + 缓存键贡献者 |
| 作用域 | Global / Tenant / User | Global / Tenant |
| 集成测试 | 有 | `FeatureManagementIntegrationTests` 验证设置、身份功能开关 |
| 安全拦截 | `[RequiresFeature]` | 存在但需验证成熟度 |

**结论**: 基础功能已覆盖并在集成测试中验证。差距在于作用域完整度和生态成熟度。

---

## 七、数据访问

### 7.1 多 ORM 支持 — ⭐ 8.0/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| EF Core | ✅ 完全支持 | ✅ 完全支持（`EfCoreRepository`, `EfCoreUnitOfWork`, 拦截器） |
| MongoDB | ✅ 完全支持 | ✅ `MongoRepositoryBase`，CRUD + 租户过滤（已测试） |
| FreeSql | ❌ | ✅ `FreeSqlProvider`（软删除 ⚠️ 部分，多租户 ✅，审计 ✅） |
| SqlSugar | ❌ | ✅ `SqlSugar`（并发 ✅，多租户 ✅） |
| **统一抽象层** | ❌ 无 | ✅ `IDataBaseContext`/`IDataBaseSet`/`IQueryableBuilder`/`IDataBaseTransaction` 四层 |
| DbContext 模型 | 单一 | ✅ `TenantAwareModelCacheKeyFactory` 每租户独立 EF 模型 |

> 详细 ORM 能力矩阵见 [`orm-capability-matrix.md`](./orm-capability-matrix.md)

**结论**: 在 ORM 支持广度（4 种）和统一抽象层设计上超过 ABP。提供者在具体能力对齐上存在差异（详见 ORM 能力矩阵）。

---

### 7.2 数据过滤 — ⭐ 8.5/10

| 能力 | ABP | CrestCreates |
|------|-----|-------------|
| 软删除 | `ISoftDelete` + 全局查询过滤 | 对等 + `DataFilterState` 运行时启用/禁用 |
| 租户过滤 | `IMultiTenant` + EF Core 全局过滤 | 对等 + `MultiTenancyInterceptor`（SaveChanges 级别） |
| 数据作用域 | 基本（租户级别） | ✅ **四级作用域**：Self / Organization / Org+Sub / All |
| 自定义过滤 | `IDataFilter<T>` API | `DataFilterState` 字典管理：`IsEnabled<T>()`/`SetFilterState<T>(bool)` |
| 代码生成过滤 | 手动配置 | ✅ `TenantFilterSourceGenerator` 自动生成 |

**结论**: 数据过滤超越 ABP——四级数据作用域、自动生成的租户过滤、SaveChanges 拦截器双层保护。

---

## 八、跨切面关注点

### 8.1 AOP 拦截器 — ⭐ 9.0/10

**这是 CrestCreates 相比 ABP 的显著技术优势**。

| 维度 | ABP (Castle DynamicProxy) | CrestCreates (Rougamo.Fody) |
|------|--------------------------|---------------------------|
| 技术原理 | 运行时动态代理（反射 + Emit） | **编译期 IL Weaving**（Fody 编织 IL 指令） |
| AoT 兼容 | ❌ 不可用 | ✅ 编译期已完成，无运行时反射 |
| 性能开销 | 每次调用都有代理链 | 零运行时开销，与手写代码相同 |
| 拦截器排序 | 无严格排序 | ✅ 8 级排序链：Exception(-1000)→UoW(-500)→Permission(-400) |
| UoW 拦截 | `[UnitOfWork]` | `[UnitOfWorkMo]` + `AsyncLocal<Stack>` 嵌套支持 |
| 缓存拦截 | 手动调用 | `[CacheMo("prefix", expiration)]` 声明式 |
| 权限拦截 | `[AbpAuthorize]` | `[PermissionMo]` + `PermissionOptions.PermissionMappings` |
| 审计拦截 | `[Audited]` | `[AuditedMo]` + 中间件双层充实 |
| 生命周期 | 需注册为 Interceptor | 属性即用，自动编织 |

**拦截器排序链（低到高执行）**:

```
ExceptionHandling (-1000) → UnitOfWork (-500) → Permission (-400) →
MultiTenant (-300) → DataPermission (-200) → Cache (-100) →
Audit (0) → Logging (100)
```

**结论**: CrestCreates 在 AOP 技术上优于 ABP。Rougamo 的编译期编织没有 Castle DynamicProxy 的运行时开销和 AoT 兼容性问题。排序链是 ABP 没有的精细控制。

---

### 8.2 缓存系统 — ⭐ 7.5/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 本地缓存 | `IMemoryCache` | `CrestMemoryCache`（`System.Runtime.Caching.MemoryCache`） |
| 分布式缓存 | Redis / `IDistributedCache` 标准 | `RedisCrestCache`（StackExchange.Redis，System.Text.Json） |
| 抽象层 | `IDistributedCache` 标准 | 三层：`ICrestCache` → `ICrestCacheService` → `[CacheMo]` |
| 键生成 | 字符串拼接 | ✅ `ICrestCacheKeyGenerator`（结构化：`prefix:tenantId:userId:parts`） |
| 声明式缓存 | ❌ 无 | ✅ `[CacheMo]` AOP 属性 |
| 租户感知 | 无 | ✅ 自动租户键生成 |
| Redis 反模式 | 无 | ⚠️ `RemoveByPatternAsync` 使用 `KEYS` 命令（生产环境问题） |

**结论**: `[CacheMo]` 声明式缓存和结构化键生成是独有优势。Redis `KEYS` 命令在生产的阻塞问题是必须修复的技术债。

---

## 九、消息与事件 — ⭐ 8.0/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 本地事件 | `ILocalEventBus` → MediatR | `IDomainEvent:INotification` + `IDomainEventPublisher` → MediatR |
| RabbitMQ | `IRabbitMqEventBus` | `RabbitMqEventBus` + `[RabbitMqSubscribe]` 属性 |
| Kafka | `IKafkaEventBus` | `KafkaEventBus` + `[KafkaSubscribe]` 属性 |
| **事件存储** | ❌ 无 | ✅ `IEventStore` + `IEventRetryStore` |
| **幂等性** | ❌ 无 | ✅ `IEventIdempotencyStore` + `InMemoryEventIdempotencyStore` |
| **重试机制** | ❌ 无 | ✅ `EventRetryService` + 指数退避（最多 5 次） |
| **租户上下文** | ❌ 无 | ✅ `TenantEventContext` 附带租户/组织元数据 |
| 死信 | 无 | 无 |
| 声明式订阅 | 编程注册 | ✅ `[RabbitMqSubscribe]`/`[KafkaSubscribe]` 属性 + 代码生成 |

**结论**: 事件系统功能比 ABP 更全——事件存储、重试、幂等性检查都是 ABP 没有的内建能力。声明式订阅通过代码生成避免了运行时反射。

---

## 十、后台作业与调度 — ⭐ 7.5/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 后台作业 | `IBackgroundJob<TArg>` 简单队列 | `IJob<TArg>` + `IBackgroundJobRetryPolicy`（3 种策略） |
| 定时调度 | 无内建（需 Quartz） | ✅ `ISchedulerService`：周期/延迟/即时 + Cron 表达式 |
| Quartz 集成 | `AbpQuartzModule` | `QuartzSchedulerService` + `JobAdapter` + `DIJobFactory` |
| 租户感知 | ❌ 无 | ✅ `JobExecutionContext` 携带 TenantId/OrgId/UserId |
| 声明式 | ❌ 无 | ✅ `[BackgroundJob]` + 代码生成注册 |
| **执行钩子** | ❌ 无 | ✅ `IJobExecutionHandler`：OnScheduled/Started/Succeeded/Cancelled |
| **重试策略** | ❌ 无 | ✅ 指数退避 / 固定延迟 / 不重试 |

**结论**: 调度系统功能覆盖超过 ABP 的标准后台作业——定时调度、租户感知、执行钩子、声明式注册。ABP 的优势在于持久队列和管理 UI。

---

## 十一、可观测性

### 11.1 审计日志 — ⭐ 8.0/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| HTTP 级别 | 基础中间件 | ✅ URL/Method/ClientIP/UserAgent/Body/StatusCode |
| 服务级别 | `[Audited]` 拦截器 | `[AuditedMo]` AOP + ServiceName/MethodName/Parameters |
| 敏感数据脱敏 | ❌ 无 | ✅ `IAuditLogRedactor` JSON 遍历 + 正则脱敏 |
| 清理策略 | ❌ 无 | ✅ `DeleteAsync(DateTime)` 保留策略 |
| 配置 | 基础 | ✅ `AuditLoggingOptions`：URL 忽略/敏感属性/体大小限制 |
| 持久化 | `IAuditLogStore` | `IAuditLogService` → `IRepository<AuditLog>` |

**结论**: 功能超过 ABP——敏感数据脱敏、保留策略、丰富配置。ABP 的管理 UI 是主要差距。

---

### 11.2 日志框架 — ⭐ 9.0/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 底层框架 | `ILogger<T>`（可配 Serilog） | **Serilog 主链**，`.UseCrestSerilog()` 自动配置 |
| 输出端 | 可配置 | Console + File（滚动）+ Seq + MSSqlServer |
| 丰富器 | 基础 | MachineName/ThreadId/AppName/EnvironmentName/LogContext |
| 请求日志 | 无内建 | `RequestLoggingMiddleware`：TraceId/User/Tenant/Duration |
| 级别控制 | appsettings | appsettings 命名空间覆盖 |

**结论**: Serilog 主链方案优于 ABP 的默认 `ILogger` + 可选 Serilog。`RequestLoggingMiddleware` 的丰富请求日志是增量功能。

---

### 11.3 健康检查 — ⭐ 4.0/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 端点 | 无内建 | `/health` + `/health/{tag}` |
| 检查实现 | N/A | `MemoryHealthCheck`（stub）、`DatabaseHealthCheck`（stub）、`RedisHealthCheck`（stub） |
| 真实检查 | N/A | ❌ 全部仅 `Task.Delay(10)` |

**结论**: 存在但仅有骨架，所有检查实现都是占位符。需要重写生产级检查。

---

## 十二、扩展基础设施

### 12.1 文件管理 — ⭐ 8.0/10

| 维度 | ABP (BLOB) | CrestCreates |
|------|-----------|-------------|
| 提供者 | 本地 + Azure + AWS | 对等：LocalFileSystem + AzureBlob + AmazonS3 |
| 租户隔离 | 容器/目录 | 支持 |
| 验证 | 基础 | 扩展名/大小白名单 |
| 预签名 URL | ❌ 无 | ✅ |
| 元数据 | 基础 | MIME / 大小 / 创建者 |
| 访问模式 | public/private | public/private |

**结论**: 功能对等。预签名 URL 支持是增量功能。ABP 有管理 UI。

---

### 12.2 虚拟文件系统 — ⭐ 7.5/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 寻址 | `{Module}/{Path}` | `{ModuleName}/{RelativePath}`（`VirtualPath` 记录） |
| 提供者 | `EmbeddedResourceProvider` | 对等 + `PhysicalFileProvider` + `CodeGeneratorResourceProvider` |
| 文件监视 | `IFileChangeToken` | 支持 |
| 覆盖机制 | `ReplaceEmbeddedResource` | `ModuleResourceProvider` 聚合多个提供者 |

**结论**: 功能对等。ABP 在主题/UI 应用场景更丰富。

---

### 12.3 测试基础设施 — ⭐ 7.0/10

| 维度 | ABP | CrestCreates |
|------|-----|-------------|
| 基础类 | `AbpIntegratedTest<T>` | `TestBase` → `DomainTestBase` → `IntegrationTestBase` → `ApiTestBase<TStartup>` |
| 模拟 | Moq | Moq + AutoFixture + AutoMoq + `TestDataBuilder<T>` |
| 集成测试 | `WebApplicationFactory` | `WebApplicationFactory` + **Testcontainers PostgreSQL**（每类 schema 隔离） |
| 测试项目数 | 中等 | 24 个测试项目 |
| 代码生成测试 | 无 | `CodeGenerator.Tests` 覆盖所有生成器 |
| 跨 ORM 测试 | EF Core + MongoDB | EF Core + MongoDB + FreeSql + SqlSugar |

**结论**: `Testcontainers` 的 PostgreSQL schema 隔离方案优秀。测试覆盖度高。基础类比 ABP 薄。

---

### 12.4 代码生成 — ⭐ 9.5/10

**这是 CrestCreates 相比 ABP Framework 的最大优势**。

| 生成器 | ABP | CrestCreates |
|--------|-----|-------------|
| 模块注册 | CLI 模板初始 | MSBuild 任务 + Source Generator 编译期生成 |
| DTO | 手动编写 | `[Entity]` → DTO / CreateDto / UpdateDto |
| 仓储 | 手动编写 | `[GenerateRepository]` → IRepository + EF Core 实现 |
| CRUD 服务 | 手动编写 | `[GenerateCrudService]` → 完整服务 + 接口 + 验证 |
| 映射 | AutoMapper（运行时反射） | `[GenerateObjectMapping]` → 编译期强类型映射 |
| 验证器 | 手动编写 | `[Entity]` → FluentValidation 自动生成 |
| 权限定义 | 手动定义 | `[Entity]` → `XxxPermissions` 类 |
| 控制器 | 手动或 Dynamic API | `ControllerSourceGenerator` 编译期 |
| 端点 | Dynamic API 运行时 | `DynamicApiAotSourceGenerator` → Minimal API |
| 订阅 | 手动注册 | `[KafkaSubscribe]` / `[RabbitMqSubscribe]` → 注册代码 |
| 后台作业 | 手动注册 | `[BackgroundJob]` → `BackgroundJobsExtensions.g.cs` |
| 租户过滤 | 手动编写 | `TenantFilterSourceGenerator` 自动生成 |
| DbContext 工厂 | 手动编写 | `TenantDbContextFactorySourceGenerator` 自动生成 |

**Source Generator 完整列表（16 个）**:

1. EntitySourceGenerator
2. CrudServiceSourceGenerator
3. ServiceSourceGenerator
4. ControllerSourceGenerator
5. CrudControllerSourceGenerator
6. RepositorySourceGenerator
7. ObjectMappingSourceGenerator
8. ModuleSourceGenerator
9. DynamicApiAotSourceGenerator
10. KafkaSubscriptionSourceGenerator
11. RabbitMqSubscriptionSourceGenerator
12. BackgroundJobsSourceGenerator
13. HealthCheckSourceGenerator
14. TenantFilterSourceGenerator
15. TenantDbContextFactorySourceGenerator
16. CompensationExecutorSourceGenerator

**结论**: ABP 没有内建的编译时代码生成系统。CrestCreates 的 16 个 Source Generator 从属性注解自动化 DTO、仓储、服务、映射、验证、权限、控制器、端点——大幅减少样板代码。编译期映射消除了 AutoMapper 的运行时反射。

---

### 12.5 编译期映射系统 — ⭐ 9.0/10

`[GenerateObjectMapping]` 在编译期生成 `MapTo{Target}()` / `MapFrom{Source}()` 扩展方法。

| 维度 | AutoMapper (ABP 默认) | CrestCreates 编译期映射 |
|------|----------------------|------------------------|
| 技术 | 运行时反射 + 表达式树 | 编译期 IL 代码生成 |
| AoT 兼容 | ❌ | ✅ |
| 性能 | 首次创建映射需预热 | 零开销，等效手写代码 |
| 方向控制 | 单向/双向 | 支持（`Both`/`SourceToTarget`/`TargetToSource`） |
| 导航属性 | 需 `.ForMember()` | ✅ 自动 null 条件保护 |
| 值转换器 | `IValueConverter` | 支持 |
| 可扩展性 | `BeforeMap`/`AfterMap` | `BeforeApplyTo`/`AfterToDto` 分部方法 |
| 错误反馈 | 运行时异常 | 编译期错误 |

**结论**: 替代 AutoMapper 的正确方向——编译期生成消除了运行时反射，AoT 友好，无预热开销。

---

## 十三、CrestCreates 独有功能

### 13.1 多 ORM 统一抽象 — ⭐ 8.5/10

`IDataBaseContext` → `IDataBaseSet` → `IQueryableBuilder` → `IDataBaseTransaction` 四层抽象。ABP 没有等效设计。支持 EF Core + MongoDB + FreeSql + SqlSugar 四种 ORM。

### 13.2 插件系统 — ⭐ 8.0/10

`PluginSystem` 模块：`plugin.json` 清单驱动、`AssemblyLoadContext` 隔离、生命周期状态机、运行时启用/禁用。生产就绪但 AoT 不兼容。

### 13.3 分布式事务（Saga / CAP） — ⭐ 6.0/10

`IDistributedTransactionManager` + `ITransactionCompensator` + CAP 实现。架构设计良好但实现仍是原型阶段。`CompensationRetryBackgroundService` 存在但未完全验证。

---

## 十四、缺失功能

| 功能 | 评分 | ABP 对标 | 说明 |
|------|------|---------|------|
| Blazor UI 框架 | ❌ 0/10 | ✅ 完整 Blazor Server/Wasm UI | 完全缺失 |
| SignalR / 实时通信 | ❌ 0/10 | ✅ Hub 基类 + 通知系统 | 完全缺失 |
| 文本模板系统 | ❌ 0/10 | ✅ `ITemplateDefinitionManager` + 动态渲染 | 完全缺失 |
| 数据迁移/播种 | ⚠️ 4/10 | ✅ 完整数据播种器 | 仅有租户级别的初始化管线 |

---

## 综合评分汇总

### 全部 31 项评分

| # | 功能模块 | 评分 | VS ABP | 备注 |
|---|---------|------|--------|------|
| 1 | 模块系统 | ⭐ 8.5 | ✅ 超越 | 编译期生成为主链 |
| 2 | 实体与领域层 | ⭐ 9.0 | ✅ 持平 | 层级完整，设计一致 |
| 3 | DI 与模块初始化 | ⭐ 8.5 | ✅ 持平 | 更标准 .NET 模式 |
| 4 | Dynamic API | ⭐ 8.0 | ✅ 超越 | AoT 优先，编译期端点 |
| 5 | MVC/Controllers | ⭐ 7.0 | ⚠️ 略逊 | 缺自动异常过滤和包装 |
| 6 | 多租户 | ⭐ 8.5 | ✅ 超越 | 初始化管线 + 安全边界 |
| 7 | 权限系统 | ⭐ 8.0 | ✅ 持平 | + 自动代码生成 + 租户作用域 |
| 8 | OAuth/OpenIddict | ⭐ 7.5 | ⚠️ 略逊 | 生态差距 |
| 9 | 通用 CRUD | ⭐ 8.0 | ✅ 持平 | 内置安全检查和数据权限 |
| 10 | 设置管理 | ⭐ 8.0 | ✅ 超越 | 加密 + 更丰富值类型 |
| 11 | 功能管理 | ⭐ 7.0 | ⚠️ 略逊 | 基础覆盖，生态不足 |
| 12 | 多 ORM 支持 | ⭐ 8.0 | ✅ 超越 | 4 种 ORM + 统一抽象 |
| 13 | 数据过滤 | ⭐ 8.5 | ✅ 超越 | 四级数据作用域 + 自动生成 |
| 14 | AOP 拦截器 | ⭐ 9.0 | ✅✅ 大幅超越 | 编译期 IL 编织，零反射 |
| 15 | 缓存系统 | ⭐ 7.5 | ⚠️ 略逊 | 声明式缓存优；Redis KEYS 反模式 |
| 16 | 事件总线 | ⭐ 8.0 | ✅ 超越 | 事件存储 + 重试 + 幂等 |
| 17 | 后台作业与调度 | ⭐ 7.5 | ✅ 超越 | 定时调度 + 钩子 + 租户感知 |
| 18 | 审计日志 | ⭐ 8.0 | ✅ 超越 | 脱敏 + 清理策略 |
| 19 | 日志框架 | ⭐ 9.0 | ✅ 持平 | Serilog 主链 |
| 20 | 健康检查 | ⭐ 4.0 | ❌ 不足 | 仅骨架，检查均为 stub |
| 21 | 文件管理 | ⭐ 8.0 | ✅ 持平 | 3 个提供者 |
| 22 | 虚拟文件系统 | ⭐ 7.5 | ✅ 持平 | 功能对等 |
| 23 | 测试基础设施 | ⭐ 7.0 | ✅ 持平 | Testcontainers 隔离优秀 |
| 24 | 代码生成 | ⭐ 9.5 | ✅✅ 大幅超越 | **最大优势** |
| 25 | 编译期映射 | ⭐ 9.0 | ✅✅ ABP 无 | 替代 AutoMapper |
| 26 | 分布式事务 | ⭐ 6.0 | ⚠️ ABP 无 | 架构好，实现原型 |
| 27 | 插件系统 | ⭐ 8.0 | ✅ ABP 无 | ALC 隔离，生产就绪 |
| 28 | Blazor UI | ❌ 0.0 | ❌ 缺失 | — |
| 29 | SignalR | ❌ 0.0 | ❌ 缺失 | — |
| 30 | 文本模板 | ❌ 0.0 | ❌ 缺失 | — |
| 31 | 数据迁移/播种 | ⚠️ 4.0 | ❌ 不足 | 仅租户级别 |

**基于 27 个非缺失功能的加权平均分：7.8 / 10**

---

## 六边形能力图

```
                    代码生成 (9.5)
                       ▲
                      /|\
                     / | \
            实体层 (9.0)│ AOP (9.0)
                   /    │    \
                  /     │     \
           多租户(8.5)──┤──数据过滤(8.5)
                 │      │      │
                 │      │      │
        审计日志(8.0)─ 模块(8.5)─ 事件总线(8.0)
                 │             │
                 │             │
           权限(8.0)─ CRUD(8.0)── 多 ORM(8.0)
                 │             │
                 │             │
           设置(8.0)── 文件(8.0)── 插件(8.0)
                 │
                 │
            Dynamic API(8.0)
```

---

## 核心结论

### CrestCreates 的 5 大核心优势

1. **代码生成体系（9.5/10）**: 16 个 Source Generator + 4 个 MSBuild Task 构成完整的编译期自动化管线，从属性注解自动化 DTO、仓储、服务、映射、验证器、权限、控制器、端点。**这是 ABP 目前完全不具备的能力**。

2. **AoT 与编译期优先架构**: 模块初始化、Dynamic API 端点、对象映射、AOP 拦截器全部走编译期路径。**ABP 的 Castle DynamicProxy + 运行时反射在 .NET 10/AoT 时代是硬伤**。

3. **统一多 ORM 抽象**: `IDataBaseContext`/`IDataBaseSet`/`IQueryableBuilder` 架构支持 EF Core + MongoDB + FreeSql + SqlSugar 四种 ORM，ABP 只有 EF Core + MongoDB。

4. **更完善的企业功能**: 分布式事务 Saga（CAP）、插件系统（ALC 隔离）、设置加密、审计日志脱敏、四级数据作用域——这些是 ABP 没有或只有基础版本的功能。

5. **AOP 编译期编织（Rougamo）**: 比 Castle DynamicProxy 更高效、AoT 友好的拦截方案，且明确了 8 级拦截器排序链。

### 5 大核心差距

1. **Blazor/UI 框架缺失（0/10）**: ABP 提供完整的 Blazor Server + WebAssembly 组件化 UI 框架，CrestCreates 完全无 UI 层。

2. **SignalR/实时通信缺失（0/10）**: 无法支持实时通知、WebSocket 场景。

3. **生态与社区差距**: ABP 有丰富的预置模块（CMS、支付、多语言）、商业版支持、管理 UI、文档生态。

4. **健康检查（4/10）**: 仅有骨架，所有检查实现都是 stub。

5. **认证社交登录集成**: OAuth 模块存在但范围有限，缺乏 ABP 丰富的社交登录提供者。

### 总体定位

**CrestCreates 不是一个"仿 ABP"，而是一个定位更激进的"下一代 ABP"**——优先编译期、优先 AoT、优先单链。在企业级框架的核心基础设施（模块、实体、服务、权限、多租户、审计、事件）上已经达到或部分超过 ABP 的水平。主要短板在 UI 层和实时通信层面，这符合其专注后端框架的定位。

按 AGENTS.md 中"收口框架主链"的优先级，**当前工程方向完全正确**。下一步的关键不是继续加模块，而是把现有的原型级功能（健康检查、分布式事务）做实，并清理遗留的 runtime path（`CrudServiceBase`、`DynamicApiSourceGenerator`）。

### 与现有 Roadmap 的关系

本评分与此前 `crestcreates-abp-roadmap.xml` 中识别的差距一致，但补充了量化的基线。建议：

- **第一批修复（先做）**: 并发控制、多租户生命周期、全局异常处理、Dynamic API 主链收口、设置管理收口——这些模块已有 8.0+ 评分，差距主要在闭环和测试，而非重新设计。
- **第二批增强**: CRUD 主链、DTO 映射、多 ORM 一致性、后台作业、认证链路、权限系统——这些模块 7.5-8.0 分，需要功能增强而非基础重建。
- **第三批打磨**: 本地化、缓存、事件总线、分布式事务、模块系统——评分 6.0-7.5，有基础但需完善。
- **最后做**: MongoDB、插件系统——已有的 `PluginSystem` 比 roadmap 预期成熟，MongoDB 也有测试覆盖。
