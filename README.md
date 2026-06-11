# CrestCreates

CrestCreates 是一个模块化 .NET 10 企业级应用开发框架，基于领域驱动设计 (DDD) 和编译期代码生成，支持多 ORM、多租户、Trim 发布，并持续向 NativeAOT 友好主链收口。

## 核心特性

- **编译期代码生成** — 双管线：Roslyn Source Generator（逐项目）+ MSBuild BuildTasks（跨项目），替代运行时反射
- **模块化架构** — `[CrestModule]` 驱动的模块发现、依赖排序、自动注册
- **Dynamic API** — 编译期生成 Minimal API 端点，AoT 友好
- **多 ORM 支持** — 统一抽象层 `OrmProviders.Abstract`，实现 EF Core / FreeSql / SqlSugar
- **多租户** — 5 种识别策略 × 3 种隔离策略，租户生命周期管理
- **认证授权** — JWT / OAuth / OpenIddict，RBAC 权限系统
- **事件总线** — Local / RabbitMQ / Kafka / EventStore
- **分布式事务** — 基于 CAP 的最终一致性方案
- **AOP** — Rougamo/Fody 实现 UoW、缓存、审计等横切关注点
- **审计日志 / 缓存 / 健康检查 / 任务调度 / 本地化 / 文件管理**

## Major Feature 状态

CrestCreates 当前重点是把框架主链收口，而不是继续堆模块名。下面的状态按“是否已经能作为框架主链使用”划分；已完成不代表不再优化，表示主路径已经落地并有测试或示例覆盖。

### 已完成 / 主链可用

| Feature | 状态 | 说明 |
|---------|------|------|
| 编译期代码生成 | 已完成 | Source Generator + BuildTasks 已覆盖 DTO、Repository、Mapping、Permission、Validator、QueryBuilder、Dynamic API、模块初始化等主链。 |
| 模块系统 | 已完成 | 支持模块声明、依赖排序、生命周期初始化和编译期模块聚合；后续重点是诊断和插件化，不是重做模块主链。 |
| Dynamic API 主链 | 已完成 | 主路径已收口到 generated endpoints，支持 Swagger、权限元数据和集成测试；runtime reflection path 不再作为新增能力方向。 |
| DDD 基础设施 | 已完成 | Domain / Application / Contracts / ORM Provider 分层、实体基类、审计实体、仓储抽象、UoW 和应用服务基类已可用。 |
| 乐观并发控制 | 已完成 | 统一并发戳、仓储并发检查、HTTP 条件请求和并发异常响应已接入主链。 |
| 多租户基础能力 | 已完成 | 支持 Header / Query / Domain / Route / Cookie 等解析方式，配合租户上下文、数据过滤和权限边界。 |
| 租户数据库生命周期 | 已完成 | 新租户初始化编排、独立库初始化、迁移、种子数据、状态记录、失败重试和诊断记录已形成主链。 |
| 认证授权基础链路 | 已完成 | OpenIddict / JWT 登录、当前用户、租户边界、RBAC 权限校验和 Dynamic API 权限检查已可用。 |
| Setting Management | 已完成 | 支持定义、Host / Tenant / User 作用域、缓存、加密、Dynamic API 管理和运行时读取。 |
| Feature Management | 已完成 | 支持 Feature 定义、Global / Tenant 覆盖、解析顺序、缓存失效、权限边界、租户初始化、审计和 generated API。 |
| 全局异常处理 | 已完成 | 统一异常基类、业务异常、验证异常、权限异常、并发异常、错误响应格式和本地化资源已接入。 |
| 审计日志 | 已完成 | 请求级和方法级审计、查询 API、扩展属性和清理能力已接入示例和测试。 |
| DTO / 对象映射生成 | 已完成 | `ObjectMappingSourceGenerator` 已作为唯一映射主链，支持 DTO / Entity、Create、Update Apply、受保护字段、简单转换、导航路径诊断，并已接入 CRUD / Entity 生成器。 |
| 多 ORM 基础支持 | 已完成 | EF Core、FreeSql、SqlSugar Provider 已存在，统一仓储抽象已可用。 |
| 事件总线基础能力 | 已完成 | Local、RabbitMQ、Kafka、EventStore 等事件通道已有实现。 |
| 分布式事务基础能力 | 已完成 | CAP 集成、事务日志和补偿机制已有基础主链。 |
| 后台任务基础能力 | 已完成 | Quartz 调度、后台任务模型和平台化设计已有实现基础。 |
| 文件存储 / 本地化 / 缓存 | 已完成 | 平台基础能力已存在，并可被设置、异常、审计、Feature 等主链复用。 |

### Major TODO / 待增强

| Feature | 优先级 | 状态 | 要做的事 |
|---------|--------|------|----------|
| CRUD 主链增强 | P1 | **已完成** | ~ICrudAppService~ + ~CrestAppServiceBase~ + ~QueryExecutor~ + ~FilterBuilder/SortBuilder~ + ~CrudServiceSourceGenerator (DTO/服务/权限/Mapping/Endpoint 全生成)~。FreeSql/SqlSugar 仓储生成器为空桩，待补齐。 |
| 映射生态覆盖 | P2 | **已完成** | ~ObjectMappingSourceGenerator~ 支持 9 种转换 (Enum↔String↔Int、Guid↔String、NumericCast) + `[MapConvert]` 自定义转换器 + 导航路径 + 受保护字段。映射文档示例待补充。 |
| 多 ORM 一致性 | P1 | **部分完成** | EF Core 主链已完成（审计/软删除/多租户/并发/UoW/分页/领域事件）。FreeSql/SqlSugar 审计拦截器已实现，但软删除测试、多租户审计测试缺失；MongoDB 有基础仓储但无审计/并发/软删除/多租户。 |
| 后台作业平台化 | P1 | **已完成** | ~ISchedulerService~ (一次性/延迟/Cron 周期任务) + ~Quartz 适配器~ + ~JobRecord 历史~ + ~IJobExecutionHandler 生命周期~ + ~IBackgroundJobRetryPolicy~ (指数退避/固定延迟/不重试) + ~租户上下文传递~ + 集成测试。EF Core IJobHistoryRepository 待实现。 |
| 认证链路收口 | P1 | **部分完成** | ~OpenIddict 服务器~ (Password/RefreshToken/ClientCredentials/AuthorizationCode) + ~IdentityClaimsBuilder~ + ~CurrentUser~ + ~安全日志~ + ~跨租户拒绝测试~。`IIdentitySecurityLogWriter` 接口定义缺失；OAuth 项目为空壳；`SecurityService` 与 `Infrastructure.PasswordHasher` 存在密码哈希重复。 |
| 权限系统收口 | P1 | **已完成** | ~IPermissionChecker~ + ~PermissionGrantManager~ (授予/撤销) + ~PermissionGrantCacheService~ (缓存+失效) + ~TenantPermissionScopeValidator~ (租户边界) + ~SuperAdmin 旁路~ + ~PermissionMoAttribute AOP 拦截器~ + 跨租户/角色/用户维度测试。分布式缓存失效事件待补齐。 |
| 组织架构权限 | P2 | **部分完成** | ~Organization 实体~ + ~OrganizationHierarchyService~ + ~DataPermissionFilter~ (Self/Organization/OrganizationAndSub/Tenant/All) + Phase 5c: Organization Identity Kernel (OrganizationUnit/Position/UserOrganizationMembership/UserOrganizationRoleAssignment models, composite-key IOrganizationStore/InMemory, IOrganizationHierarchyService with tenantId scoping + cycle detection, IOrganizationIdentityService, DataPermissionScope stub, 42 tests). No OrganizationAppService, no database persistence, no API endpoints. |
| 本地化深化 | P2 | **部分完成** | ~ILocalizationService~ + ~异常消息本地化~ + ~en/zh-CN JSON 资源~ + ~ILocalizationResourceContributor~。验证错误本地化未接入；无可视化资源管理。 |
| 缓存一致性 | P2 | **部分完成** | ~ICrestCacheService~ + ~Memory/Redis 双后端~ + ~SettingCacheInvalidator~ + ~FeatureCacheInvalidator~ + ~TenantCacheInvalidator~ + ~CacheMo AOP~。Redis 未独立成包；无跨实例 pub/sub 失效；无缓存击穿保护。 |
| 事件总线稳定性 | P2 | **部分完成** | ~IEventBus~ + ~Local/RabbitMQ/Kafka 实现~ + ~IEventIdempotencyStore~ + ~EventRetryService~ + 单元测试。无 DLQ、无事件命名规范、无订阅规则、分布式幂等未接入、无集成测试。 |
| 分布式事务运维闭环 | P2 | **部分完成** | ~CAP 集成~ + ~2PC (Prepare/Commit/Rollback)~ + ~PersistentTransactionCompensator~ (指数退避重试) + ~CompensationRetryBackgroundService~。无状态机校验、无崩溃恢复扫描、无补偿幂等、无 Saga 模式。 |
| 模块诊断增强 | P2 | **部分完成** | ~编译期拓扑排序~ + ~循环依赖检测~ (构建期) + ~ConfigureServices 异常包装~ + ~TenantDiagnosticsAppService~。运行时依赖验证、初始化失败诊断 (非 ConfigureServices 阶段)、模块加载时序、Health Check 端点均缺失。 |
| MongoDB Provider | P3 | **部分完成** | ~MongoRepositoryBase~ (CRUD/租户/并发/审计/软删除) + ~DI 扩展~ + 仓储测试。无 DbContext 抽象、无事务支持、无租户数据库初始化。 |
| 插件化系统 | P3 | **部分完成** | ~PluginManager~ (发现/加载/初始化) + ~PluginManifest 模型~ + ~PluginAssemblyLoadContext~ (隔离) + ~依赖校验~。无版本兼容性检查、无热重载、无模块生命周期集成、无签名验证、无管理 UI。 |

## 快速开始

```bash
# 还原、构建、测试
dotnet restore
dotnet build
dotnet test

# 运行示例应用
dotnet run --project samples/LibraryManagement/LibraryManagement.Web

# 默认 Trim 发布（非 NativeAOT）
dotnet publish samples/LibraryManagement/LibraryManagement.Web -c Release -r win-x64 --self-contained true

# 显式 NativeAOT 发布验证
dotnet publish samples/LibraryManagement/LibraryManagement.Web -c Release -r win-x64 --self-contained true -p:CrestCreatesPublishMode=aot
```

### 发布模式

全局发布配置位于 `Directory.Build.Aot.props`。当前默认发布模式是 `trim`：启用 `PublishTrimmed=true`，但不默认启用 `PublishAot`。这样可以先保证框架和示例应用能够完成 Trim 发布，同时避免 EF Core、MVC、动态 LINQ 等尚未完全 AOT 化的链路在普通发布时直接失败。

| 模式 | 命令 | 行为 |
|------|------|------|
| 默认 / `trim` | `dotnet publish ...` 或 `-p:CrestCreatesPublishMode=trim` | `PublishTrimmed=true`，`PublishAot=false` |
| `aot` | `dotnet publish ... -p:CrestCreatesPublishMode=aot` | `PublishTrimmed=true`，`PublishAot=true`，启用 EF Core NativeAOT interceptor namespace |

测试项目在 `Directory.Build.targets` 中强制关闭 `PublishTrimmed` 和 `PublishAot`，避免 Moq、Castle DynamicProxy 等运行时代码生成链路被 Trim/AOT 约束干扰。

### 创建使用框架的项目

1. 创建 ASP.NET Core Web API 项目：
```bash
dotnet new webapi -n YourProjectName
```

2. 添加框架引用并导入 BuildTasks：
```xml
<ItemGroup>
  <ProjectReference Include="..\..\framework\src\CrestCreates.Modularity\CrestCreates.Modularity.csproj" />
  <ProjectReference Include="..\..\framework\src\CrestCreates.Web\CrestCreates.Web.csproj" />
  <!-- 按需添加其他模块 -->
</ItemGroup>

<Import Project="..\..\build\CrestCreates.BuildTasks\CrestCreates.Modules.props" />
```

3. 在 `Program.cs` 中注册和初始化模块：
```csharp
var builder = WebApplication.CreateBuilder(args);

// 注册框架模块（使用编译期生成的模块发现）
builder.Host.RegisterModules();

var app = builder.Build();

// 映射控制器 + 编译期生成的 Dynamic API 端点
app.MapControllers();
app.MapCrestAspNetCoreDynamicApi();

// 初始化模块生命周期
app.InitializeModules();

app.Run();
```

## 项目结构

```
CrestCreates/
├── framework/
│   ├── src/              # 框架源码（46 个项目，扁平化布局）
│   ├── test/             # 测试项目（22 个）
│   └── tools/            # CodeGenerator（Roslyn Source Generator）
├── build/                # BuildTasks（MSBuild 跨项目代码生成）
├── samples/
│   └── LibraryManagement/  # DDD 示例应用
├── docs/                 # 文档
├── CrestCreates.slnx     # 解决方案文件（.slnx 格式）
├── Directory.Build.props # 全局构建配置（net10.0, 中央包管理）
├── Directory.Build.Aot.props # Trim / NativeAOT 发布模式配置
└── Directory.Packages.props  # 中央包版本管理
```

## 架构概览

### 分层依赖方向

```
Domain.Shared ← Domain ← Application.Contracts ← Application
                                ↓                      ↓
                          Infrastructure          OrmProviders.*
                                ↓                      ↓
                          Web/AspNetCore ←──────────(implements)
```

### 代码生成管线

| 管线 | 技术 | 作用域 | 输出 |
|------|------|--------|------|
| Source Generator | Roslyn Analyzer | 逐项目编译期 | DTO、Repository、Mapping、Permissions、Validator、QueryBuilder、DynamicApi 端点、Service 注册 |
| BuildTasks | MSBuild Task | 跨项目构建期 | ModuleManifest.json、ModuleAutoInitializer.g.cs、EntityPermissionsManifest.json |

### 关键属性

| 属性 | 作用 |
|------|------|
| `[CrestModule]` | 模块发现 → BuildTasks + SourceGenerator |
| `[CrestService]` | 服务 DI 注册 + 控制器/端点生成 |
| `[Entity]` | Repository、DTO、权限、QueryBuilder 生成 |
| `[GenerateCrudService]` | 实体 CRUD 服务生成 |
| `[GenerateRepository]` | 仓储生成（可指定 ORM） |
| `[GenerateObjectMapping]` | 编译期对象映射 |
| `[UnitOfWorkMo]` | AOP 事务边界 |
| `[CacheMo]` | AOP 缓存拦截 |

### 实体与服务模式

```csharp
// 领域实体
[Entity]
public class Book : AuditedEntity<Guid>
{
    // 私有 setter，构造函数校验，领域方法
}

// 应用服务
[CrestService]
public class BookAppService : CrestAppServiceBase<Book, Guid, BookDto, CreateBookDto, BookDto>, IBookAppService
// 自动获得：CRUD、权限检查、数据过滤、审计属性设置、UoW
```

### Dynamic API 路由约定

- 方法名 → HTTP 方法：`Create/Add/Insert` → POST，`Update/Put` → PUT，`Delete/Remove` → DELETE，`Get` → GET，`Query/Search` → POST
- 路由格式：`{prefix}/{kebab-case-service-name}/{action-route}`，`Async` 后缀自动去除

## 示例项目：LibraryManagement

完整的 DDD 图书馆管理系统，演示：

- 严格分层：Domain.Shared → Domain → Application.Contracts → Application → EntityFrameworkCore → Web
- 实体：Book、Category、Loan、Member（含领域事件）
- 服务：CRUD + 业务方法（借阅、归还、续借）
- 认证：OpenIddict + JWT
- 集成测试：WebApplicationFactory + PostgreSQL schema 隔离

## 技术栈

| 类别 | 技术 |
|------|------|
| 运行时 | .NET 10.0 |
| Source Generator | Roslyn (netstandard2.0) |
| ORM | EF Core 10 / FreeSql 3.5 / SqlSugar 5.1 |
| 认证 | OpenIddict 7.4 / JWT |
| 事件总线 | RabbitMQ / Kafka / EventStore |
| 分布式事务 | CAP 10 |
| 缓存 | Memory + StackExchange.Redis |
| 调度 | Quartz 3.17 |
| AOP | Rougamo/Fody 5 |
| 日志 | Serilog |
| 映射 | 编译期 GenerateObjectMapping |
| 验证 | FluentValidation |
| 中介者 | MediatR |
| 测试 | xUnit / FluentAssertions / Moq / AutoFixture |

## 设计原则

1. **唯一主链** — 确定主实现后不再维护第二套，编译期生成优先于运行时反射
2. **Trim 优先，NativeAOT 显式验证** — 默认保证 Trim 发布可用；NativeAOT 作为显式模式持续推进，不再默认阻断普通发布链路
3. **强类型优先** — 使用 contributor/definition/descriptor/provider 模式，不散落字符串拼装
4. **平台能力优先** — 优先做可复用的平台能力，而非业务级补丁

## 许可证

MIT License
