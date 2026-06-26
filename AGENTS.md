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
dotnet build src/Framework/Ddd/CrestCreates.Domain
dotnet test tests/Framework/Ddd/CrestCreates.Domain.Tests

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
- CodeGenerator 在 `src/Tooling/CrestCreates.CodeGenerator/`，target 为 `netstandard2.0`
- BuildTasks 在 `src/Tooling/CrestCreates.BuildTasks/`，target 为 `net10.0`（注意不是 netstandard2.0）
- AoT 配置在 `Directory.Build.Aot.props`，默认 `trim` 模式；显式 AoT 需 `-p:CrestCreatesPublishMode=aot`
- 测试项目在 `Directory.Build.targets` 中强制关闭 Trim/AoT（Moq/DynamicProxy 不兼容）
- Source Generator 可通过 `-p:CrestCreatesCodeGeneration=false` 全局禁用（见 `Directory.Build.Aot.props`）
- Source Generator 输出到 `obj/{config}/{tfm}/source-generators/`（见 `Directory.Build.targets` 的 `CompilerGeneratedFilesOutputPath`）
- **CodeGenerator 测试陷阱**：测试中的 `AttributeDeclarations` 命名空间必须与生成器期望的完全匹配。CanonicalHash 属性在 `CrestCreates.Metadata.Abstractions.CanonicalHashing`（不是 `CrestCreates.Metadata.Abstractions`）。测试属性声明不能使用 `required` 关键字，否则 C# 编译器 CS9035 会抢先于生成器诊断触发。

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
├── src/
│   ├── Core/                    # 核心抽象（Core.Abstractions + Core）
│   ├── Framework/
│   │   ├── Modularity/          # 模块系统
│   │   ├── Ddd/                 # Domain.Shared, Domain, Application.Contracts, Application
│   │   ├── Infrastructure/      # 基础设施：Aop, Caching, Configuration, Security, Localization 等
│   │   ├── Api/                 # DynamicApi, OpenApi
│   │   ├── Web/                 # AspNetCore, HealthCheck, Authentication
│   │   ├── Modules/             # 业务模块：FileManagement, Form, Organization, Scheduling 等
│   │   └── Testing/             # 测试基础设施
│   ├── Metadata/
│   │   ├── (flat)               # Metadata.Abstractions, Metadata, ContextPack, Schema, Snapshot
│   │   └── Draft/               # DescriptorDraft, Draft（草稿/预发布状态）
│   ├── Runtime/
│   │   ├── Capability/          # Capability.Abstractions + Capability
│   │   ├── Workflow/            # Workflow.Abstractions + Workflow
│   │   ├── HumanTask/           # HumanTask.Abstractions + HumanTask
│   │   ├── Agent/               # Agent.Abstractions, Agent.Runtime, Agent.ControlPlane
│   │   ├── Eventing/            # Event, EventBus (Local, Kafka, RabbitMQ, EventStore 等)
│   │   ├── Audit/               # AuditLogging.Abstractions + AuditLogging
│   │   └── DistributedTransaction/ # DistributedTransaction + CAP
│   ├── Persistence/             # Data.*, DbContextProvider, MongoDB
│   ├── Platform/                # 组合入口：Web, Platform, Platform.AspNetCore, Platform.All 等
│   ├── Tooling/                 # CodeGenerator, Metadata.Analyzers, BuildTasks
│   └── Integrations/            # PluginSystem, Integration.ExternalApi, Integration.LegacyDatabase
├── tests/
│   ├── Boundary/                # 依赖边界测试
│   ├── Framework/               # Ddd, Infrastructure, Web, Modules, Testing
│   ├── Metadata/                # Core, Draft
│   ├── Runtime/                 # Capability, Workflow, HumanTask, Agent, Eventing, Audit 等
│   ├── Persistence/             # OrmProviders, MongoDB, Database.Migrations
│   ├── Tooling/                 # BuildTasks, CodeGenerator
│   └── (sample-specific tests)
├── samples/
│   ├── LibraryManagement/       # DDD 示例应用（6 项目）
│   └── SaaSHelpdesk/           # DDD 示例应用（7 项目）
├── docs/
│   ├── design/                  # 设计文档
│   ├── Feature/                 # 功能文档
│   ├── review/                  # 评审文档
│   └── superpowers/             # 设计规格（specs/）和工作计划（plans/）
├── 99_RecycleBin/               # 软删除回收站（文件删除规则见下）
├── solutions/                   # 分层 .slnx 文件（CrestCreates.All.slnx 为规范文件）
├── CrestCreates.slnx            # 主解决方案（.slnx XML 格式，非 .sln）
├── Directory.Build.props        # 全局构建配置（net10.0, Nullable, 中央包管理）
├── Directory.Build.Aot.props    # Trim / NativeAOT 发布模式配置 + SG 全局注入 + ModuleDiagnostics 全局注入
├── Directory.Build.targets      # 测试项目 Trim/AoT 禁用 + SG 输出路径
├── Directory.Packages.props     # 中央包版本管理
├── global.json                  # SDK 版本锁定
├── nuget.config                 # NuGet 源配置（仅 nuget.org）
├── memory.md                    # 平台闭环状态记录（关键决策和未完成工作）
├── AGENTS.md                    # 本文件
├── CLAUDE.md                    # Claude Code 用指令文件
└── .github/copilot-instructions.md  # GitHub Copilot 用指令文件（部分过时，见下方警告）
```

**注意**：`.github/copilot-instructions.md` 中的部分信息已过时（如使用 `OrmProviders.*` 命名、`ModuleA/ModuleB` 示例模块不存在、仍引用旧 `framework/src/` 路径等），不要以此为准。

### .slnx 虚拟文件夹组织

`.slnx` 按 `src/` 下的层级结构分组：
- `/src/Core/` — 核心抽象（Core.Abstractions, Core）
- `/src/Framework/Modularity/` — 模块系统
- `/src/Framework/Ddd/` — DDD 分层（Domain.Shared, Domain, Application.Contracts, Application）
- `/src/Framework/Infrastructure/` — 基础设施（Aop, Caching, Security, Localization 等）
- `/src/Framework/Api/` — DynamicApi, OpenApi
- `/src/Framework/Web/` — AspNetCore, HealthCheck
- `/src/Framework/Modules/` — 业务模块
- `/src/Metadata/` — 元数据核心
- `/src/Metadata/Draft/` — 草稿/预发布状态
- `/src/Runtime/Capability/` — 能力运行时
- `/src/Runtime/Workflow/` — 工作流运行时
- `/src/Runtime/HumanTask/` — 人工任务运行时
- `/src/Runtime/Agent/` — Agent 运行时
- `/src/Runtime/Eventing/` — 事件总线
- `/src/Runtime/Audit/` — 审计日志
- `/src/Persistence/` — 数据访问
- `/src/Platform/` — 组合入口
- `/src/Tooling/` — CodeGenerator, BuildTasks
- `/src/Integrations/` — 外部集成
- `/tests/` — 按同层分组

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
| Source Generator | Roslyn Analyzer | 逐项目编译期 | DynamicApi 端点、DTO、Repository、ORM Mapping、ObjectMapping、Permissions、Validator、CRUD Service、Module、Service 注册、BackgroundJobs、EnumDisplay、HealthCheck、Kafka、RabbitMQ、SchemaCapability（DescriptorRegistry + HandlerInvoker + RefValidation 诊断）、TenantDbContextFactory、TenantFilter、CompensationExecutor |
| BuildTasks | MSBuild Task | 跨项目构建期 | ModuleManifest.json、ModuleAutoInitializer.g.cs、EntityPermissionsManifest.json |

Source Generator 全局注入在 `Directory.Build.Aot.props`，通过 `OutputItemType="Analyzer"` 引用。

BuildTasks 有 4 个 Task：
- 编译前链：`ScanModulesFromSource` → `CollectModuleManifests` → `GenerateAggregatedModuleCode`（在 `CoreCompile` 前顺序执行）
- 编译后：`ScanEntityPermissions`（`AfterTargets="Build"`，不在编译前链中）

使用者需 import `src/Tooling/CrestCreates.BuildTasks/CrestCreates.Modules.props`。

**注意**：Controller 生成器（`ControllerSourceGenerator`、`CrudControllerSourceGenerator`）已标记 `[Obsolete]`，主链使用 `DynamicApiAotSourceGenerator` 生成的 Minimal API 端点。不存在独立的 QueryBuilder Source Generator。

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
Core ← Core.Abstractions
  ↑
Framework/Ddd: Domain.Shared ← Domain ← Application.Contracts ← Application
Framework/Infrastructure: Aop, Caching, Security, Localization, MultiTenancy, Authorization...
Framework/Api: DynamicApi, OpenApi
Framework/Web: AspNetCore, HealthCheck
  ↑
Metadata: Metadata.Abstractions ← Metadata, ContextPack, Schema, Snapshot
Metadata/Draft: DescriptorDraft, Draft
  ↑
Runtime: Capability, Workflow, HumanTask, Agent, Eventing, Audit, DistributedTransaction
  ↑
Persistence: Data.EFCore, Data.FreeSql, Data.SqlSugar, MongoDB
  ↑
Platform: Web, Platform.AspNetCore, Platform.All (组合入口)
Tooling: CodeGenerator, BuildTasks
Integrations: PluginSystem, ExternalApi, LegacyDatabase
```

**依赖边界**（由 `tests/Boundary/CrestCreates.DependencyBoundaries.Tests` 强制执行）：
- Core 不引用上层（Framework, Metadata, Runtime, Persistence, Platform）
- Metadata.Abstractions 不引用 Framework, Runtime, Persistence, Platform
- Runtime 不引用 Framework/Api, Framework/Web, Platform
- Runtime 不引用具体 ORM Provider（FreeSql, SqlSugar）
- Persistence 不引用 Runtime 具体实现（Workflow, Agent, HumanTask）
- Tooling 不引用 Runtime 具体实现

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
- 测试基类在 `tests/Framework/Testing/CrestCreates.TestBase/`，层次结构为扁平型（非线性链式）：
  ```
  TestBase                      ← 根基类（IFixture, IServiceProvider, mock 注册）
  ├── DomainTestBase            ← 直接继承 TestBase
  ├── ApplicationTestBase       ← 直接继承 TestBase
  ├── IntegrationTestBase       ← 直接继承 TestBase
  │   └── ApiTestBase<TStartup> ← 继承 IntegrationTestBase
  ```

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
- 模块发现 props：`src/Tooling/CrestCreates.BuildTasks/CrestCreates.Modules.props`
- 依赖边界测试：`tests/Boundary/CrestCreates.DependencyBoundaries.Tests/`

---

**最后更新**: 2026-06-18
