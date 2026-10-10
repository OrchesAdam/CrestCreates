# Issue #124 统一 UnitOfWork 装配、Provider 选择与生命周期 — 设计记录

日期：2026-10-10
实施基线：master `85bdbb017a479503d250b6edd174a05daf620a7b`（PR #134 merge）
关联：[Issue #124](https://github.com/OrchesAdam/CrestCreates/issues/124)、[Issue #121](https://github.com/OrchesAdam/CrestCreates/issues/121)
本文档为设计裁定与迁移记录；验证结果见文末「验证记录」。

## 一、真实注册与调用图（基线事实）

### 1.1 两套 factory / manager 的定义与消费者

| 入口/消费者 | 当前契约 | Provider 来源 | 生命周期 | 问题 | 迁移去向 |
| --- | --- | --- | --- | --- | --- |
| `OrmModuleBase.OnConfigureServices` | `Data.Abstractions` `IUnitOfWorkFactory` + `IUnitOfWorkManager` | 反射扫描（`AppDomain.GetAssemblies` + `GetService(type)`，类型名字符串） | scoped（factory/manager） | 运行时扫描；AOT 不友好；错误定位差 | 注册单元化：`AddUnitOfWork` + 子类由 `RegisterOrmServices` 注册 typed binding |
| `UnitOfWorkServiceCollectionExtensions.AddUnitOfWork(defaultProvider)` | 同上（Data 契约） | 反射扫描 | scoped | 重复调用 last-wins；默认值靠参数 | 保留签名；重复同值幂等、冲突诊断；默认规则见 §二.4 |
| `AddUnitOfWork<TFactory>`（Data 扩展） | 自定义 `IUnitOfWorkFactory` | 无 binding（自定义 factory） | scoped | 真正自定义扩展点 | 保留；文档明确其绕过 binding 注册的语义 |
| `UnitOfWorkManager`（Data） | `IUnitOfWorkScope`/`CurrentOrNull`/`BeginScope`/`Begin`/`Execute(Async)` | `IUnitOfWorkFactory` 注入 | scoped manager + AsyncLocal 环境栈 | `requiresNew` 走同一 scoped factory → 真实 DI 下拿回同一 scoped UoW（共享 DbContext，嵌套隔离不成立） | 保留契约；requiresNew 改为受管子 scope（见 §二.3），真实 DI 验证 |
| EfCore/SqlSugar/FreeSql OrmModule `RegisterOrmServices` | 注册具体 UoW（scoped） | 各自模块显式构造 | scoped UoW（singleton client for SqlSugar；FreeSql IFreeSql scoped） | 具体 UoW 注册与 binding 分离会双实例 | 新增 provider 侧 typed binding（`sp.GetRequiredService<ConcreteUoW>()`，无类型字符串） |
| samples：LibraryManagement / SaaSHelpdesk `EntityFrameworkCoreModule` | `AddUnitOfWork(EfCore)` + 手工 `AddScoped(new EfCoreUnitOfWork(...))` | 手工构造 | scoped | 与模块路径重复声明 | 保留结构，补 binding 注册（同一 API） |
| `CrestCreatesWebApplicationExtensions`（Platform Web） | 同上 | 手工构造 | scoped | 同上 | 同上 |
| AOP `UnitOfWorkMoAttribute` | `IUnitOfWorkManager`（Rougamo service locator） | DI | 拦截器作用域 | manager 缺失时 warn+skip（无 ORM 应用兼容路径） | 契约不变；skip 行为保留并文档化；不改 service locator 模式（AOP 边界内） |
| `DynamicApiGeneratedRuntime.ExecuteAsync` | `IUnitOfWorkManager`（`context.RequestServices.GetService`） | DI | 请求作用域 | manager 缺失时直接执行 | 同上，契约不变 |
| Infrastructure `UnitOfWork/UnitOfWorkFactory.cs`（factory+`IUnitOfWorkManager`+`OrmOptions`+`ConfigureOrm`+`AddUnitOfWork`+delegate） | `CrestCreates.Infrastructure.UnitOfWork` | `Type.GetType` + 反射 fallback + 吞异常 `try/catch` | scoped/singleton 定义不完整 | **死代码**：全仓无消费者（Web 扩展的 `AddUnitOfWork(OrmProvider.EfCore)` 因重载解析绑定到 Data 侧；Infrastructure 仅剩 using） | **归档至 `99_RecycleBin/`**；迁移说明见 §三.2 |
| `Domain.Shared.Enums.OrmProvider` | 元数据/Attribute 面（`[GenerateRepository]`/`[GenerateEntity]` 等） | — | — | 与 `Data.Abstractions.OrmProvider` 同名、语义不同、无转换 | 保留两者；语义区分记录（§二.5），不合并 |
| `FreeSqlUnitOfWorkManager`（FreeSql 项目） | `FreeSql.UnitOfWorkManager` 的 SDK adapter | 官方 SDK | scoped | 与通用 manager 同名但**不是**第二套通用主链 | 保留在 FreeSql 项目内（provider-internal） |
| `FreeSqlServiceCollectionExtensions.AddFreeSqlWithUow` | IFreeSql 从 `UowManager.Orm` 解析（跟随事务） | 工厂 lambda | scoped | 与 OrmModule 路径两处注册 | 保留（provider 内部注册路径）；补 binding |
| `tests/…OrmProviders.Tests/UnitOfWorkManagerTests` | FakeFactory | `new` per call | 测试手动构造 | 不能证明真实 DI 隔离 | 保留并扩展为真实 DI + 真实数据库用例（§四） |
| `tests/Framework/Web/…LegacyGeneratedDynamicApiRuntimeTests.TestUnitOfWorkManager` | 自定义 `IUnitOfWorkManager` 单例 | 测试替身 | singleton | 适配新契约 | 更新实现 |
| 生成器（CrudService `[UnitOfWorkMo]`、DynamicApi `ExecuteAsync(..., requiresTransaction)`、`GenerateRepository` 的 `OrmProvider` 属性名读取） | — | — | — | 生成产物依赖 AOP/运行时契约 | 无需更改（契约保持）；生成器测试按需回归 |

### 1.2 调用图（迁移后）

```
模块/扩展（OrmModuleBase.RegisterOrmServices、samples/platform 模块）
  ├─ 注册具体 UoW（scoped，provider 构造责任在 provider 包）──────────────┐
  └─ services.AddUnitOfWorkProvider(provider, sp => sp.GetRequiredService<ConcreteUoW>())(typed binding)
        ↓ 装配期收集（singleton bindings）
  UnitOfWorkFactory（scoped）: Create(provider) → binding.Factory(调用方 scope)   ← 无反射/无 fallback
        ↑
  UnitOfWorkManager（scoped）: BeginScope(...)
        ├─ 有环境 && !requiresNew → 复用（isOwner=false）
        ├─ 有环境 && requiresNew → IServiceScopeFactory 子 scope → 子 scope 内 factory/UoW
        └─ 无环境 → 从调用方 scope 解析 UoW（保持“每请求一个 UoW/DbContext”共享模型）
        ↓
  AOP / DynamicApiGeneratedRuntime / 业务（仅见 IUnitOfWorkManager / IUnitOfWorkScope）
```

## 二、设计裁定

### 1) 唯一公共契约与所在层

保留 `CrestCreates.Data.Abstractions` 的 `IUnitOfWorkFactory`、`IUnitOfWorkManager`、`IUnitOfWorkScope` 作为唯一公共契约（理由：Runtime/Api 层已依赖该契约；manager 具备环境栈/嵌套/requiresNew 语义；Infrastructure 版本为死代码）。其**现有反射扫描实现被替换**。基础层（Data.Abstractions）不引用任何 ORM 具体实现或类型名。

### 2) Provider 构造责任与 typed binding

- 构造责任在 **provider 包/模块**：provider 注册 `Func<IServiceProvider, IUnitOfWork>` **静态强类型委托**（通常为 `sp => sp.GetRequiredService<EfCoreUnitOfWork>()`），不出现反射、`Type.GetType`、字符串类型名。
- 新增 `UnitOfWorkProviderBinding`（provider、typed factory、`SupportsRequiresNew` 能力声明）与 `AddUnitOfWorkProvider(...)` 扩展；bindings 在装配期收集为单例，factory（scoped）构造时一次性建索引，之后不再被每个 scoped factory 修改。
- factory/binding 不捕获 root `IServiceProvider`：scoped factory 使用调用方 scope 的 SP；子 scope 场景由子 scope 内的 factory 解析。业务 Handler 不接触 factory/registry（service locator 不外泄）。
- `Create` 失败语义：缺 binding → 确定性异常（列出已注册 provider 与指引）；不返回 null、不跳过、不自动换默认 Provider。

### 3) scope / Dispose 责任

| 实例 | 创建者 | Dispose owner | 说明 |
| --- | --- | --- | --- |
| 调用方 scope UoW（无环境首个 / `Begin`） | 从调用方 scope 解析 | manager 在 scope 结束时 `UoW.Dispose()`（释放事务资源） | DbContext/连接等仍归 DI 容器；`EfCoreUnitOfWork.Dispose` 仅释放事务对象 |
| requiresNew UoW | manager 创建的子 scope | manager：`UoW.Dispose()` + 子 scope `Dispose()` | 一次性释放；子 scope 结束恢复父 Current |
| 复用 scope（isOwner=false） | — | 不释放、不提交外层 | 保持现状 |
| 构建失败（factory.Create 抛） | — | 不污染 Current | `BeginScope` 先 Create 后入栈 |

- 异常语义：提交/回滚失败或依赖解析失败 → 恢复父 Current；原始业务异常不被清理异常替换（清理失败仅记录后再抛原异常）——保持并测试。
- 生命周期/领域事件保持“提交成功后发布”（`EfCoreUnitOfWork.CommitTransactionAsync` 现有顺序不回改）。

### 4) 默认 Provider 选择规则（替代隐藏 last-wins）

1. 调用方显式 `provider` 参数（`Begin`/`BeginScope`/`Execute(Async)`）优先。
2. 应用显式声明 `AddUnitOfWork(defaultProvider: X)`（重复调用相同值幂等；不同值 → 确定性诊断）。
3. 未显式声明时：恰好注册 1 个 provider binding → 该 provider 为默认（单 ORM 应用保持短路径）。
4. 注册多个 binding 且无显式默认 → `provider == null` 的入口抛确定性异常（列出已注册 provider，指引显式选择或声明默认）。
5. 配置文件不参与默认选择：Infrastructure 的 `ConfigureOrm(configuration)` 属归档旧路径；如需恢复须按新证据另行设计。

### 5) 枚举与元数据语义区分

- `CrestCreates.Data.Abstractions.OrmProvider`：**运行时装配面**（factory/manager/binding）。
- `CrestCreates.Domain.Shared.Enums.OrmProvider`：**元数据/Attribute 面**（`[GenerateRepository]`/`[GenerateEntity]` 等声明，供生成器读取），保持独立、不合并、不互转。
- 两者成员序一致（EfCore=0/SqlSugar=1/FreeSql=2），文档记录以避免选错。

### 6) requiresNew 与 Provider 能力

- `requiresNew` 在有环境时通过**子 DI scope** 获取独立 UoW/DbContext/连接；子 scope 结束恢复父 Current。
- binding 声明 `SupportsRequiresNew`；不支持者（SqlSugar：共享 singleton `ISqlSugarClient`）在使用 `requiresNew` 时给出**确定性 `NotSupportedException` 诊断**（不扩大 Provider 承诺、不静默共享事务）。
- 无子 scope factory 的手动构造（单测/自定义场景）：回退为 `factory.Create`（记录为手动路径语义；DI 主链始终有子 scope 能力）。

### 7) 兼容与归档

- 无兼容 shim 保留 Infrastructure 版本；直接归档文件 + 更新 using。
- `AddUnitOfWork<TFactory>` 保留为真正的自定义 factory 扩展点（文档注明不经 binding registry）。

## 三、归档清单

1. `src/Framework/Infrastructure/CrestCreates.Infrastructure/UnitOfWork/UnitOfWorkFactory.cs` → `99_RecycleBin/issue-124-infrastructure-unitofwork/`（附迁移说明：改用 `CrestCreates.Data.Abstractions` 契约与 `AddUnitOfWork`；配置来源语义去除）。
2. 因反射路径消除而失去理由的 suppression：
   - `Data.Abstractions/UnitOfWorkBase/UnitOfWorkFactory.cs` 的 `IL2026/IL3050` suppression（随实现替换删除）。
   - `Extensions/UnitOfWorkServiceCollectionExtensions.cs` 的 `IL2091` suppression（泛型注册经评估：`AddUnitOfWork<TFactory>` 为显式泛型注册，重写为 `TryAddScoped(typeof(TFactory))` 后判断；若仍需要则保留并注明原因）。
   - `Modules/OrmModuleBase.cs` 的重复 using（CS0105）修复。

## 四、验证记录

本地环境：linux-x64、.NET SDK 10.0.112；本机 Docker 不可用，依赖 Testcontainers 的套件（IntegrationTests / SaaSHelpdesk.Tests / PG AotFixture）仅在 CI 验证；相关测试工厂已同步迁移到统一注册入口。

| 命令/场景 | 结果 |
| --- | --- |
| `dotnet build CrestCreates.slnx` | 0 错误（迁移后全仓编译） |
| `dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests` | 56/56（含新增 19 个 UoW 用例） |
| ├─ `UnitOfWorkDiRegistrationTests`（10 用例） | 单绑定默认 / 显式默认 / 显式参数优先 / 多绑定无默认诊断 / 缺绑定诊断 / 重复绑定诊断 / 冲突默认诊断 / 幂等 / requiresNew 子 scope 隔离与父恢复 / 不支持 requiresNew 诊断 / 依赖失败不污染 Current |
| ├─ `EfCoreUnitOfWorkDatabaseTests`（4 用例，真实 SQLite 文件库） | 提交持久化 / 回滚无残留 / requiresNew 独立 DbContext 与独立事务（含内层不关闭外层事务、父恢复）/ 领域事件在提交持久化之后发布 |
| └─ `UnitOfWorkManagerTests`（4 用例，手动构造路径） | 既有 3 用例保持 + 回滚失败不替换原始业务异常 |
| UoW native 门禁（本分支新 fixture） | `CRESTCREATES_UNITOFWORK_NATIVE_PIPELINE_OK`；publish/link/run 40s（本地热缓存） |
| `dotnet test tests/Framework/Api/CrestCreates.DynamicApi.Tests` | 73/73（生成运行时 `ExecuteAsync` 契约未变） |
| `dotnet test tests/Framework/Infrastructure/CrestCreates.Infrastructure.Tests` | 2/2（AOP 契约未变） |
| `dotnet test tests/Framework/Web/CrestCreates.Web.Tests` | 107/111；4 个 `CapabilityEndpointBoundaryTests` 失败为 worktree 环境性（查找 `.git` 目录，worktree 中为文件），与本项无关且未触碰相关文件 |
| CI（`ci.yml` / `full-validation.yml`） | 新增 `Test — UnitOfWork native pipeline gate` 步骤，linux-x64 必跑 |

基线缺口复核（修复前）：`UnitOfWorkFactory` 依赖 `AppDomain.CurrentDomain.GetAssemblies()` + 反射类型名解析；真实 DI 下 requiresNew 拿回同一 scoped 实例（FakeFactory 测试无法暴露）。修复后二者均被真实 DI/原生门禁覆盖。

## 五、每 Provider 验证范围与 #130 交接

| Provider | 本轮验证范围 | 未验证项（交接） |
| --- | --- | --- |
| EF Core | 统一装配路径 + 默认/显式选择 + requiresNew 子 scope 独立 DbContext/事务 + 真实 SQLite 提交/回滚 + 事件顺序 + native 装配门禁（静态 UoW） | PostgreSQL 真实库下的嵌套隔离由 CI 集成套件间接覆盖；EF Core 自身 AOT/trim 能力边界交 #130 |
| FreeSql | 绑定登记（`supportsRequiresNew: true`，作用域内 IFreeSql/连接隔离）+ 编译/发布通过 | 绑定路径下的运行级事务/隔离用例、SDK AOT 边界交 #130；既有 FreeSql 仓储测试仍走 SDK adapter 直连路径 |
| SqlSugar | 绑定登记（共享单例客户端 → `requiresNew` 确定性 NotSupportedException 诊断，含单测） | 运行级事务用例、SDK AOT 边界交 #130 |
| MongoDB | 无 UoW 能力（维持 `ModuleBase`），未变 | 无 |

本 native fixture 只证明**生产装配机制**（同一正式 `AddUnitOfWork`/`AddUnitOfWorkProvider` API），不证明 EFCore/FreeSql/SqlSugar SDK、数据库驱动或完整仓储 AOT 已验证；真实 ORM 集成证据独立记录（JIT-only/未验证按实声明）。

## 六、不再命中的旧公开契约/扫描路径清单

| 旧路径 | 处置 | 位置 |
| --- | --- | --- |
| `CrestCreates.Infrastructure.UnitOfWork` 整组（factory/manager/OrmOptions/ConfigureOrm/AddUnitOfWork/delegate） | 归档（死代码，无消费者） | `99_RecycleBin/issue-124-infrastructure-unitofwork/`（含 README 迁移说明） |
| `Data.Abstractions.UnitOfWorkFactory` 反射扫描（`AppDomain.CurrentDomain.GetAssemblies` + `GetService(type)` + 类型名字符串 + `NotSupportedException` 回退） | 替换为 typed binding registry（确定性问题诊断） | `UnitOfWorkBase/UnitOfWorkFactory.cs`、`UnitOfWorkProviderBindingRegistry.cs` |
| `OrmModuleBase.GetOrmProvider()`（模块隐式声明默认，实际由最后注册覆盖） | 移除；改为绑定 + 默认选择规则 | `Modules/OrmModuleBase.cs` |
| `Data.Abstractions/UnitOfWorkBase/UnitOfWorkFactory.cs` 的 IL2026/IL3050 suppression | 随实现替换删除 | 同上 |
| `Extensions/UnitOfWorkServiceCollectionExtensions.cs` 的 IL2091 suppression | 替换为 `DynamicallyAccessedMembers(PublicConstructors)` 注解 | `AddUnitOfWork<TFactory>` |
| `AddUnitOfWork(OrmProvider defaultProvider = OrmProvider.EfCore)` 的隐式 EfCore 默认 | 参数改为可空（显式声明语义）；单绑定自动默认保持短路径 | 同上 |

