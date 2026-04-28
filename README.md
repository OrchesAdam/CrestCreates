# CrestCreates

CrestCreates 是一个模块化 .NET 10 企业级应用开发框架，基于领域驱动设计 (DDD) 和编译期代码生成，支持多 ORM、多租户、AoT 友好。

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

## 快速开始

```bash
# 还原、构建、测试
dotnet restore
dotnet build
dotnet test

# 运行示例应用
dotnet run --project samples/LibraryManagement/LibraryManagement.Web
```

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
├── Directory.Build.props # 全局构建配置（net10.0, AoT, 中央包管理）
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
| 映射 | AutoMapper / 编译期 GenerateObjectMapping |
| 验证 | FluentValidation |
| 中介者 | MediatR |
| 测试 | xUnit / FluentAssertions / Moq / AutoFixture |

## 设计原则

1. **唯一主链** — 确定主实现后不再维护第二套，编译期生成优先于运行时反射
2. **AoT 友好** — 所有主链路径必须 AoT 兼容，`PublishAot` 全局启用
3. **强类型优先** — 使用 contributor/definition/descriptor/provider 模式，不散落字符串拼装
4. **平台能力优先** — 优先做可复用的平台能力，而非业务级补丁

## 许可证

MIT License
