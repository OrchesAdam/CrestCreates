# AGENTS.md

## 项目定位

CrestCreates 是一个类 ABP Framework 的 .NET 10 企业级应用开发框架，基于领域驱动设计 (DDD) 和编译期代码生成。

当前阶段最重要的工程目标不是"继续堆模块名"，而是把框架主链做扎实：

- 优先使用编译期代码生成，减少运行时反射
- 优先保证 AoT 友好
- 优先收口唯一主链，避免双轨实现长期并存
- 优先做可复用的平台能力，而不是业务级补丁实现

---

## 构建与命令

```bash
# 解决方案是 .slnx 格式（非 .sln），全局命令可直接用
dotnet build
dotnet test

# 构建/测试单个项目
dotnet build framework/src/CrestCreates.Domain
dotnet test framework/test/CrestCreates.Domain.Tests

# 运行单个测试
dotnet test --filter "FullyQualifiedName~CrestCreates.Application.Tests.Tenants.TenantAppServiceTests"
dotnet test --filter "FullyQualifiedName~SomeTestClass.SomeTestMethod"

# 运行示例应用
dotnet run --project samples/LibraryManagement/LibraryManagement.Web
dotnet run --project samples/SaaSHelpdesk/SaaSHelpdesk.Web

# 发布（默认 Trim 模式，NativeAOT 需显式指定）
dotnet publish samples/LibraryManagement/LibraryManagement.Web -c Release -r win-x64 --self-contained true
dotnet publish samples/LibraryManagement/LibraryManagement.Web -c Release -r win-x64 --self-contained true -p:CrestCreatesPublishMode=aot
```

**构建关键点**：
- SDK: .NET 10.0.100，`rollForward: latestMinor`（见 `global.json`）
- 解决方案: `CrestCreates.slnx`（XML 格式的新方案，不是 `.sln`）
- 中央包管理: `Directory.Packages.props`，所有 NuGet 版本集中管理
- CodeGenerator 在 `framework/tools/`（非 `framework/src/`），target 为 `netstandard2.0`
- AoT 配置在 `Directory.Build.Aot.props`，默认 `trim` 模式；显式 AoT 需 `-p:CrestCreatesPublishMode=aot`
- 测试项目在 `Directory.Build.targets` 中强制关闭 Trim/AoT（Moq/DynamicProxy 不兼容）

---

## 第一原则

### 1. 第一性原理

必须从原始目标出发，不要直接沿用已有实现习惯。

如果一个需求的目标是减少反射、提高 AoT 兼容性、提升框架一致性，那么实现时就不能继续把运行时扫描、反射调用、兼容性 fallback 当作正常主路径。

### 2. 最短正确路径

不允许：兼容性方案、补丁性方案、双轨长期并存、兜底式设计、超出需求的扩展。

允许为了落地做过渡，但过渡必须明确、短期、可移除，不能变成正式主链。

### 3. 唯一主链

同一能力如果已经确定主实现，就不要再维护第二套"也能跑"的实现。尤其适用于：

- Dynamic API
- 认证链路
- 模块构建 / 初始化链路
- 租户创建 / 初始化链路

**变更前自检**：
1. 这是在强化唯一主链，还是在偷偷保留双轨？
2. 这是在减少反射、提升 AoT，还是在继续依赖 runtime 技术路径？
3. 这是平台能力，还是业务补丁？
4. 这套测试验证的是正式主链，还是过期链路？
5. 这次修改会不会误导后续维护者继续维护 legacy path？

如果第 1、2、4、5 条答案不理想，应先停下来调整设计。

---

## 项目结构

```
CrestCreates/
├── framework/
│   ├── src/              # 框架源码（77 个项目，扁平化布局，与 .slnx 虚拟文件夹映射）
│   ├── test/             # 测试项目（38 个）
│   └── tools/            # CodeGenerator（Roslyn Source Generator，target netstandard2.0）
├── build/                # BuildTasks（MSBuild 跨项目代码生成）
├── samples/
│   ├── LibraryManagement/  # DDD 示例应用（图书馆管理）
│   └── SaaSHelpdesk/     # DDD 示例应用（SaaS 工单系统）
├── docs/
│   ├── design/           # 设计文档
│   ├── Feature/          # 功能文档
│   ├── review/           # 评审文档
│   └── superpowers/      # 设计规格（specs/）和工作计划（plans/）
├── CrestCreates.slnx     # 解决方案（.slnx XML 格式，非 .sln）
├── Directory.Build.props # 全局构建配置（net10.0, Nullable, 中央包管理）
├── Directory.Build.Aot.props # Trim / NativeAOT 发布模式配置
├── Directory.Build.targets   # 测试项目 Trim/AoT 禁用 + SG 输出路径
├── Directory.Packages.props  # 中央包版本管理
├── global.json           # SDK 版本锁定
├── nuget.config          # NuGet 源配置（仅 nuget.org）
├── memory.md             # 平台闭环状态记录（关键决策和未完成工作）
├── AGENTS.md             # 本文件
├── CLAUDE.md             # Claude Code 用指令文件
└── .github/copilot-instructions.md  # GitHub Copilot 用指令文件
```

**注意**：`.github/copilot-instructions.md` 中的部分信息已过时（如使用 `OrmProviders.*` 命名、`ModuleA/ModuleB` 示例模块不存在等），不要以此为准。

### .slnx 虚拟文件夹组织

`.slnx` 将扁平化的 `framework/src/` 按功能分组：
- `/src/core/` — 核心框架项目
- `/src/modules/Authentication/` — `CrestCreates.AspNetCore.Authentication.OpenIddict`
- `/src/modules/Authorization/` — `CrestCreates.Authorization`
- `/src/modules/EventBus/` — Kafka, RabbitMQ, Local, EventStore, DeadLetter 等
- `/src/modules/Data/` — Data.Core, Data.EFCore, Data.FreeSql, Data.SqlSugar 及各数据库 Provider
- `/src/modules/SchedulingJob/` — `CrestCreates.Scheduling.Quartz`
- `/src/modules/HealthCheck/` — AspNetCore, Mvc
- `/src/modules/DistributedTransaction/` — CAP
- `/src/samples/` — `CrestCreates.Web` + LibraryManagement + SaaSHelpdesk
- `/src/test/` — 所有测试项目
- `/src/tools/` — CodeGenerator

### 重要约定

- **文件删除**：永远不要直接删除文件或文件夹。移动到 `./99_RecycleBin/` 然后由人工确认删除。
- **`memory.md`**：记录平台闭环状态、已完成功能、关键架构决策。新增模块或重大改动后应更新。
- **设计规格**：`docs/superpowers/specs/` 下存放重大特性的设计规格，修改相关领域前应先查阅。

---

## 当前架构共识

### 1. Dynamic API

主链必须是 **Compile-time Generated**，默认不能依赖 runtime reflection scanner / executor。

`DynamicApiScanner`、`DynamicApiEndpointExecutor`、runtime reflection fallback 都不应再被当作一等公民长期维护。

修改 Dynamic API 时：
- 优先改 SourceGenerator、Generated Runtime、Generated Registry
- 不要继续给 runtime scanner / executor 加新能力
- 新测试也应优先验证 generated path

### 2. 模块系统

模块初始化主链依赖 `CrestCreates.CodeGenerator` + `CrestCreates.BuildTasks` + 编译期生成的模块聚合初始化代码。

不要再把"运行时扫描模块"当作框架真实主链来设计。

### 3. 代码生成双管线

| 管线 | 技术 | 作用域 | 输出 |
|------|------|--------|------|
| Source Generator | Roslyn Analyzer | 逐项目编译期 | DynamicApi 端点、DTO、Repository、Mapping、Permissions、Validator、QueryBuilder、CRUD Service、Entity、Module、Service 注册、BackgroundJobs、Controller、EnumDisplay、HealthCheck、Kafka、RabbitMQ、SchemaCapability、TenantDbContextFactory、TenantFilter、CompensationExecutor |
| BuildTasks | MSBuild Task | 跨项目构建期 | ModuleManifest.json、ModuleAutoInitializer.g.cs、EntityPermissionsManifest.json |

Source Generator 全局注入在 `Directory.Build.Aot.props`，通过 `OutputItemType="Analyzer"` 引用。

BuildTasks 有 4 个 Task：`ScanModulesFromSource` → `CollectModuleManifests` → `GenerateAggregatedModuleCode` → `ScanEntityPermissions`。使用者需 import `build/CrestCreates.BuildTasks/CrestCreates.Modules.props`。

### 4. 关键属性

| 属性 | 作用 |
|------|------|
| `[CrestModule]` | 模块声明 → BuildTasks + SourceGenerator |
| `[CrestService]` | 服务 DI 注册 + 控制器/端点生成 |
| `[Entity]` | Repository、DTO、权限、QueryBuilder 生成 |
| `[GenerateCrudService]` | 实体 CRUD 服务生成 |
| `[GenerateRepository]` | 仓储生成（可指定 ORM） |
| `[GenerateObjectMapping]` | 编译期对象映射（替代 AutoMapper） |
| `[MapFrom]` / `[MapIgnore]` / `[MapName]` | 对象映射属性级控制 |
| `[MapConvert]` | 自定义类型转换器 |
| `[DynamicApiRoute]` / `[DynamicApiIgnore]` | Dynamic API 路由定制或排除 |
| `[UnitOfWorkMo]` | AOP 事务边界（Rougamo/Fody） |
| `[CacheMo]` | AOP 缓存拦截 |
| `[PermissionMo]` | AOP 权限拦截 |

### 5. Setting Management

已是正式平台能力。后续涉及运行时可管理配置时，优先接 Setting Management，不要重新造 ad-hoc 配置表。

### 6. Feature Management

已是正式平台能力。支持 Feature 定义、Global/Tenant 覆盖、解析顺序、缓存失效、权限边界、租户初始化、审计和 generated API。

关键决策：`Identity.SelfRegistration` 已替换为 `Identity.UserCreationEnabled`。

### 7. 多租户

- 统一使用 `TenantId` 作为上下文主键，不要混用 `TenantName`
- 新能力必须能和 `CurrentTenant` 主链对齐

### 8. 认证授权

不要再引入新的认证真相来源，不要复制 token / claims / permission 逻辑，优先复用现有身份、权限、租户上下文主链。

---

## 分层与依赖方向

```
Domain.Shared ← Domain ← Application.Contracts ← Application
                                ↓                      ↓
                          Infrastructure          Data.* (ORM Providers)
                                ↓                      ↓
                          Web/AspNetCore ←──────────(implements)
```

- Contracts 不依赖 Application 实现
- Domain 不依赖 Web
- Infrastructure 是实现，不应反过来定义核心业务抽象
- ORM Provider 项目命名已从 `OrmProviders.*` 迁移到 `Data.*`（如 `CrestCreates.Data.EFCore`、`CrestCreates.Data.FreeSql`、`CrestCreates.Data.SqlSugar`）

不要：把领域抽象塞进 Web 层、把应用编排逻辑塞进仓储、把平台能力实现成 sample 特例。

---

## 代码规范

### 命名

- 类名/接口名/属性名/方法名使用 PascalCase，类名必须是名词或名词短语
- 异步方法以 `Async` 结尾
- 私有字段使用 `_camelCase`
- 命名空间避免与第三方库冲突（如 `CrestCreates.Data.EFCore`，不用 `CrestCreates.Infrastructure.EntityFrameworkCore`）

### 注释

只解释复杂逻辑或设计意图，不要写废话注释。

### 实现偏好

- 优先代码生成，不优先反射：除非明确无法走生成链，否则不考虑 runtime path
- 优先强类型，不优先字符串拼装：用 contributor / definition / descriptor / provider 模式，不要散落字符串拼 cache key、route 规则、provider 语义
- 优先收口，不优先横向扩展：模块存在主链缺口时先补闭环再加新能力

---

## 测试要求

### 测试基础设施

- 框架：xUnit 2.9.3 + FluentAssertions + Moq + AutoFixture
- 集成测试：`WebApplicationFactory<Program>` + Testcontainers PostgreSQL，每测试独立 schema（`itest_{guid}`）
- 测试基类继承链：`TestBase` → `DomainTestBase` → `ApplicationTestBase` → `IntegrationTestBase` → `ApiTestBase<TStartup>`
- 测试基类在 `framework/test/CrestCreates.TestBase/`

### 测试原则

- 测试信号必须和主链一致：如果主链已 AoT 化，就不要大量维护 runtime reflection path 测试
- 优先真实集成测试：认证链路、租户链路、Dynamic API 主链、Setting Management、权限与上下文联动
- 新增测试前先问：这条路径还是不是正式主链？这个测试会不会误导后续维护者继续修 legacy 而不是修主链？

---

## 当前平台状态

参考 `memory.md` 获取最新平台闭环状态。关键已完成项：

- Tenant Management（主链闭环）
- Setting Management（平台能力）
- Feature Management（平台能力）
- Dynamic API AoT 主链
- 审计日志（统一写入 + 脱敏 + 查询；清理闭环尚未完全确认）
- CRUD 主链增强（SourceGenerator 全生成）
- 权限系统收口（授予/撤销/缓存/租户边界/SuperAdmin/AOP）
- 后台作业平台化（ISchedulerService + Quartz + 重试策略 + 租户上下文）
- 映射生态（ObjectMappingSourceGenerator，9 种转换 + 自定义转换器 + 导航路径）

---

## 参考位置

- 平台状态记录：`memory.md`
- 设计规格：`docs/superpowers/specs/`
- 示例项目：`samples/LibraryManagement/`、`samples/SaaSHelpdesk/`
- 模块发现 props：`build/CrestCreates.BuildTasks/CrestCreates.Modules.props`

---

**最后更新**: 2026-06-11
