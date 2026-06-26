# AGENTS.md

## 项目定位

CrestCreates 是一个类 ABP Framework 的 .NET 10 企业级应用开发框架，基于 DDD、模块化、编译期代码生成和 AoT 友好运行时。

当前阶段的核心目标不是继续堆模块名，而是把框架主链做扎实：

- 优先使用编译期代码生成，减少运行时反射、扫描和动态 fallback。
- 优先保证 Trim / NativeAOT 友好。
- 优先收口唯一主链，避免双轨实现长期并存。
- 优先做可复用的平台能力，而不是业务级补丁。
- 让框架内核承载复杂性，让普通业务开发保持短路径。

一句话原则：

> 框架内核允许复杂，业务 Feature 必须简单；Control Plane 允许严肃，Runtime Handler 必须朴素；治理链路允许多层，普通开发体验必须短路径。

复杂不是问题，复杂性无归属才是问题。复杂性应被 CodeGenerator、BuildTasks、Control Plane、Registry、Contributor、Pipeline、强类型契约和诊断系统吸收，不能泄漏到普通业务 Handler、AppService 或 Sample 特例里。

---

## 最重要的工程原则

### 1. 第一性原理

从原始目标出发，不要直接沿用已有实现习惯。

如果目标是减少反射、提升 AoT 兼容性、统一框架主链，就不能继续把运行时扫描、反射调用、兼容性 fallback 当作正常主路径。

### 2. 唯一主链

同一能力一旦确定主实现，不要继续维护第二套“也能跑”的实现。尤其适用于：

- Dynamic API
- 模块构建 / 初始化
- 认证授权
- 租户创建 / 初始化
- Setting / Feature / Permission / Audit 等平台能力

允许短期过渡，但过渡必须明确、可删除、不能变成正式主链。

### 3. 最短正确路径

普通开发者的默认体验应该是：

1. 声明 Attribute / DTO / Entity / Service / Handler。
2. 编译期生成必要胶水代码。
3. 运行时只消费强类型 Registry / Pipeline / Context。

不要让普通业务代码理解模块扫描、Endpoint 注册、权限清单、租户初始化、审计落库、OpenAPI 聚合等内部流程。

### 4. 平台吸收复杂性

框架内部可以复杂，但必须有清晰边界：

- Source Generator 负责声明转代码。
- BuildTasks 负责跨项目聚合。
- Runtime Registry 负责只读发现和执行所需索引。
- Control Plane 负责治理、诊断、审查、预览和交接。
- Runtime Handler 只负责业务动作。

Handler 不应承担治理判断，不应拼字符串协议，不应使用 service locator，不应通过反射发现能力，不应复制认证、权限、租户、审计逻辑。

### 5. 变更前自检

每次改动前先问：

1. 这是在强化唯一主链，还是在保留双轨？
2. 这是在减少反射、提升 AoT，还是继续依赖 runtime 技术路径？
3. 这是平台能力，还是业务补丁？
4. 这套测试验证的是正式主链，还是过期链路？
5. 这次修改会不会误导后续维护者继续维护 legacy path？

如果第 1、2、4、5 条答案不理想，先调整设计。

---

## 构建与命令

```bash
# 解决方案是 .slnx 格式，根目录可直接运行
dotnet build
dotnet test

# 构建 / 测试单个项目
dotnet build src/Framework/Ddd/CrestCreates.Domain
dotnet test tests/Framework/Ddd/CrestCreates.Domain.Tests

# 运行单个测试
dotnet test --filter "FullyQualifiedName~CrestCreates.Application.Tests.Tenants.TenantAppServiceTests"
dotnet test --filter "FullyQualifiedName~SomeTestClass.SomeTestMethod"

# 运行示例应用
dotnet run --project samples/LibraryManagement/LibraryManagement.Web
dotnet run --project samples/SaaSHelpdesk/SaaSHelpdesk.Web

# 发布：默认 Trim，显式 AoT 需指定 CrestCreatesPublishMode=aot
dotnet publish samples/LibraryManagement/LibraryManagement.Web -c Release -r win-x64 --self-contained true
dotnet publish samples/LibraryManagement/LibraryManagement.Web -c Release -r win-x64 --self-contained true -p:CrestCreatesPublishMode=aot
```

构建关键点：

- SDK: .NET 10.0.100，`rollForward: latestMinor`，见 `global.json`。
- 主解决方案: `CrestCreates.slnx`，是 XML `.slnx`，不是 `.sln`。
- 分层解决方案在 `solutions/`，`solutions/CrestCreates.All.slnx` 是规范全量方案。
- 中央包管理: `Directory.Packages.props`。
- AoT / Trim 配置: `Directory.Build.Aot.props`，默认 `trim`，显式 AoT 使用 `-p:CrestCreatesPublishMode=aot`。
- 测试项目在 `Directory.Build.targets` 中关闭 Trim / AoT，因为 Moq / DynamicProxy 不兼容。
- Source Generator 可通过 `-p:CrestCreatesCodeGeneration=false` 全局禁用。
- Source Generator 输出到 `obj/{config}/{tfm}/source-generators/`。

Tooling 关键点：

- CodeGenerator: `src/Tooling/CrestCreates.CodeGenerator/`，target `netstandard2.0`。
- BuildTasks: `src/Tooling/CrestCreates.BuildTasks/`，target `net10.0`。
- BuildTasks 使用者需 import `src/Tooling/CrestCreates.BuildTasks/CrestCreates.Modules.props`。
- CodeGenerator 测试中的 Attribute namespace 必须与生成器期望完全匹配。例：CanonicalHash 属性在 `CrestCreates.Metadata.Abstractions.CanonicalHashing`，不是 `CrestCreates.Metadata.Abstractions`。
- 生成器测试里的属性声明不要使用 `required`，否则 C# 编译器 CS9035 会先于生成器诊断触发。

---

## 项目结构

```text
CrestCreates/
├── src/
│   ├── Core/                    # Core.Abstractions + Core
│   ├── Framework/
│   │   ├── Modularity/          # 模块系统
│   │   ├── Ddd/                 # Domain.Shared, Domain, Application.Contracts, Application
│   │   ├── Infrastructure/      # Aop, Caching, Configuration, Security, Localization 等
│   │   ├── Api/                 # DynamicApi, OpenApi
│   │   ├── Web/                 # AspNetCore, HealthCheck, Authentication
│   │   ├── Modules/             # 平台/业务模块
│   │   └── Testing/             # 测试基础设施
│   ├── Metadata/                # Metadata.Abstractions, Metadata, ContextPack, Schema, Snapshot
│   ├── Metadata/Draft/          # DescriptorDraft, Draft
│   ├── Runtime/                 # Capability, Workflow, HumanTask, Agent, Eventing, Audit 等
│   ├── Persistence/             # Data.EFCore, Data.FreeSql, Data.SqlSugar, MongoDB 等
│   ├── Platform/                # Web, Platform, Platform.AspNetCore, Platform.All
│   ├── Tooling/                 # CodeGenerator, Metadata.Analyzers, BuildTasks
│   └── Integrations/            # PluginSystem, ExternalApi, LegacyDatabase
├── tests/                       # 按 src 分层镜像组织
├── samples/                     # LibraryManagement, SaaSHelpdesk
├── docs/
│   ├── design/
│   ├── Feature/
│   ├── review/
│   └── superpowers/             # specs/ 和 plans/
├── solutions/                   # 分层 .slnx
├── 99_RecycleBin/               # 软删除回收站
├── memory.md                    # 平台闭环状态记录
├── AGENTS.md                    # 本文件，唯一指令入口
└── CLAUDE.md                    # 仅指向 AGENTS.md
```

注意：

- `.github/copilot-instructions.md` 中部分内容已过时，不要作为架构事实来源。
- 重大改动前查 `memory.md` 和 `docs/superpowers/specs/`。
- 新增模块、主链状态变化或重大架构决策后，应更新 `memory.md`。
- 永远不要直接删除文件或文件夹。移动到 `./99_RecycleBin/`，由人工确认删除。

---

## 依赖方向

```text
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
Platform: Web, Platform.AspNetCore, Platform.All
Tooling: CodeGenerator, BuildTasks
Integrations: PluginSystem, ExternalApi, LegacyDatabase
```

依赖边界由 `tests/Boundary/CrestCreates.DependencyBoundaries.Tests` 强制：

- Core 不引用上层 Framework / Metadata / Runtime / Persistence / Platform。
- Metadata.Abstractions 不引用 Framework / Runtime / Persistence / Platform。
- Runtime 不引用 Framework/Api、Framework/Web、Platform。
- Runtime 不引用具体 ORM Provider，如 FreeSql、SqlSugar。
- Persistence 不引用 Runtime 具体实现，如 Workflow、Agent、HumanTask。
- Tooling 不引用 Runtime 具体实现。

不要把领域抽象塞进 Web 层，不要把应用编排逻辑塞进仓储，不要把平台能力做成 sample 特例。

---

## 当前主链共识

### Dynamic API

主链必须是 Compile-time Generated。默认不能依赖 runtime reflection scanner / executor。

- 优先改 SourceGenerator、Generated Runtime、Generated Registry。
- 不要继续给 runtime scanner / executor 加新能力。
- 新测试优先验证 generated path。
- `DynamicApiScanner`、`DynamicApiEndpointExecutor`、runtime reflection fallback 不应再被当作一等公民长期维护。

Controller 生成器 `ControllerSourceGenerator`、`CrudControllerSourceGenerator` 已标记 `[Obsolete]`。主链使用 `DynamicApiAotSourceGenerator` 生成 Minimal API 端点。

### 模块系统

模块初始化主链依赖：

1. `CrestCreates.CodeGenerator`
2. `CrestCreates.BuildTasks`
3. 编译期生成的模块聚合初始化代码

不要把运行时扫描模块当作真实主链来设计。

### 代码生成双管线

| 管线 | 技术 | 作用域 | 输出 |
| --- | --- | --- | --- |
| Source Generator | Roslyn Analyzer | 逐项目编译期 | DynamicApi 端点、DTO、Repository、ORM Mapping、ObjectMapping、Permissions、Validator、CRUD Service、Module、Service 注册、BackgroundJobs、EnumDisplay、HealthCheck、Kafka、RabbitMQ、SchemaCapability、TenantDbContextFactory、TenantFilter、CompensationExecutor 等 |
| BuildTasks | MSBuild Task | 跨项目构建期 | ModuleManifest.json、ModuleAutoInitializer.g.cs、EntityPermissionsManifest.json |

BuildTasks 编译前链：

1. `ScanModulesFromSource`
2. `CollectModuleManifests`
3. `GenerateAggregatedModuleCode`

编译后任务：

- `ScanEntityPermissions`，`AfterTargets="Build"`。

### 平台能力

- Setting Management 已是正式平台能力。运行时可管理配置优先接 Setting Management，不要新造 ad-hoc 配置表。
- Feature Management 已是正式平台能力。关键决策：`Identity.SelfRegistration` 已替换为 `Identity.UserCreationEnabled`。
- 多租户统一使用 `TenantId` 作为上下文主键，不要混用 `TenantName`。
- 认证授权不要引入新的真相来源，不要复制 token / claims / permission 逻辑，优先复用现有身份、权限、租户上下文主链。
- Agent Control Plane 是治理面，不是运行时执行面。它可以审查、预览、提交激活请求，但不能绕过授权、批准自身变更、直接执行 runtime handler 或突变 runtime registry。

---

## 常用生成属性

| 属性 | 作用 |
| --- | --- |
| `[CrestModule]` | 模块声明，进入 BuildTasks + SourceGenerator |
| `[CrestService]` | 服务 DI 注册 + Dynamic API 端点生成 |
| `[Entity]` | Repository、DTO、权限、QueryBuilder 生成 |
| `[GenerateCrudService]` | 实体 CRUD 服务生成 |
| `[GenerateRepository]` | 仓储生成，可指定 ORM |
| `[GenerateObjectMapping]` | 编译期对象映射，替代 AutoMapper |
| `[MapFrom]` / `[MapIgnore]` / `[MapName]` | 对象映射属性级控制 |
| `[MapConvert]` | 自定义类型转换器 |
| `[DynamicApiRoute]` / `[DynamicApiIgnore]` | Dynamic API 路由定制或排除 |
| `[UnitOfWorkMo]` | AOP 事务边界，Rougamo/Fody |
| `[CacheMo]` | AOP 缓存拦截 |
| `[PermissionMo]` | AOP 权限拦截 |

---

## 代码规范

- 类名、接口名、属性名、方法名使用 PascalCase。
- 类名必须是名词或名词短语。
- 异步方法以 `Async` 结尾。
- 私有字段使用 `_camelCase`。
- 命名空间避免与第三方库冲突，例如使用 `CrestCreates.Data.EFCore`，不要使用 `CrestCreates.Infrastructure.EntityFrameworkCore`。
- 注释只解释复杂逻辑或设计意图，不写显而易见的注释。

实现偏好：

- 优先代码生成，不优先反射。
- 优先强类型，不优先字符串拼装。
- 优先 contributor / definition / descriptor / provider 模式，不散落拼 cache key、route、provider 语义。
- 优先收口主链，不优先横向扩展模块名。
- 公共 API 不暴露内部基础设施细节。
- Facade / AppService 负责编排，不复制核心运行时逻辑。
- Shared execution core 负责循环、状态迁移、持久化、终态事件等重复行为。

---

## 测试要求

测试基础设施：

- xUnit 2.9.3 + FluentAssertions + Moq + AutoFixture。
- 集成测试使用 `WebApplicationFactory<Program>` + Testcontainers PostgreSQL。
- 每测试独立 schema，命名形如 `itest_{guid}`。
- 测试基类在 `tests/Framework/Testing/CrestCreates.TestBase/`。

测试基类结构是扁平型，不是线性链式：

```text
TestBase
├── DomainTestBase
├── ApplicationTestBase
├── IntegrationTestBase
│   └── ApiTestBase<TStartup>
```

测试原则：

- 测试信号必须和正式主链一致。
- 如果主链已 AoT 化，不要大量维护 runtime reflection path 测试。
- 优先真实集成测试覆盖认证链路、租户链路、Dynamic API 主链、Setting Management、Feature Management、权限与上下文联动。
- 新增测试前先问：这条路径还是不是正式主链？这个测试会不会误导后续维护者继续修 legacy path？
- 生命周期事件应在持久状态变更之后发布。
- 区分顺序幂等、并发安全和分布式 exactly-once，不要用弱测试暗示强保证。

---

## 当前平台状态

以 `memory.md` 为最新状态记录。当前关键共识：

- Tenant Management：主链已基本闭环。
- Setting Management：正式平台能力。
- Feature Management：正式平台能力。
- Dynamic API AoT：generated path 是正式主链。
- Audit Logging：统一写入、脱敏、查询已闭环；清理 / governance 仍需最终确认。
- CRUD 主链：SourceGenerator 全生成方向。
- 权限系统：授予、撤销、缓存、租户边界、SuperAdmin、AOP 已收口。
- 后台作业：ISchedulerService + Quartz + 重试策略 + 租户上下文。
- ObjectMapping：SourceGenerator，支持多种转换、自定义转换器和导航路径。
- Metadata / Descriptor 治理链路：Topology、Impact、Compatibility、Package、Stable Hash、Canonical Hash profile、Agent Control Plane 等属于严肃治理面，不应把复杂性下放给普通 Runtime Handler。

---

## 工作方式

- 开始重大改动前，先读 `memory.md` 和相关 `docs/superpowers/specs/`。
- 设计或计划要强化主链，不要重新打开已关闭路径。
- 发现文档和代码冲突时，以代码事实 + `memory.md` 最新状态为准，并修正文档。
- 可以保留短期迁移层，但必须标明退出路径。
- 不要为测试方便引入生产主链不会使用的公开扩展点。
- 不要为了局部功能复制认证、权限、租户、审计、配置、Feature 判断逻辑。

参考位置：

- 平台状态记录：`memory.md`
- 设计规格：`docs/superpowers/specs/`
- 工作计划：`docs/superpowers/plans/`
- 示例项目：`samples/LibraryManagement/`、`samples/SaaSHelpdesk/`
- 模块发现 props：`src/Tooling/CrestCreates.BuildTasks/CrestCreates.Modules.props`
- 依赖边界测试：`tests/Boundary/CrestCreates.DependencyBoundaries.Tests/`

---

**最后更新**: 2026-06-26
