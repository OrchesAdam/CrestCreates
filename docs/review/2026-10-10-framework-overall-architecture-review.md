# CrestCreates 框架整体架构审查报告

- **日期**: 2026-10-10
- **范围**: src/（134 项目 / 16.1 万行）、tests/（93 项目 / 17.8 万行 / ~5136 测试方法）、samples/（29 项目）、docs 与 CI
- **方式**: 只读静态审查，四维度并行核查（架构一致性 / 代码质量 / 测试证据 / 能力完成度），所有结论附文件级证据

> **复核状态 / 阅读提示（2026-10-10 · [Issue #122](https://github.com/OrchesAdam/CrestCreates/issues/122)，基线 `cd4d7208751d4578c673ffd4a04d1e694c94dd7a`）**
>
> 本文件保留为静态审查原始版本，以维持审查历史，不静默修改。以下内容已被复核修正或限定，请勿再把原句当作当前事实：
>
> 1. 报告中的评分（8.5/10、~70% 等）、测试方法和"生产级"表述是单次静态审查的主观评价，缺少统一可复现度量，**不作为生产就绪保证或验收事实**；引用支持声明时必须逐项指向证据。
> 2. 部分流程状态（ready PR 积压、#119/#120 合并状态）与个别技术归因（CAP、ORM、DynamicApiRouteConvention、AOT 声明降级方式）已被修正；正文中以 `> **复核修订**` 标注的位置以标注为准，完整逐项裁定见文末勘误索引与 [`2026-10-10-framework-architecture-review-follow-up.md`](./2026-10-10-framework-architecture-review-follow-up.md)。

---

## 一、总体结论

| 维度 | 评分 | 一句话结论 |
| --- | --- | --- |
| 架构设计 | 8.5/10 | 「唯一主链 + 编译期生成 + 平台吸收复杂性」原则执行到位，依赖边界有 158 条强制规则，是同类自研框架中少见的纪律性 |
| 基础功能完成度 | ~70% | DDD 四层、平台能力（Tenant/Setting/Feature/Permission/Audit 写入/BackgroundJob/ObjectMapping）真闭环；治理面 durable activation ~60%；长尾模块 20-30% |
| 代码健康度 | 7/10 | 内核纪律优秀（TODO 仅 2 条、无 NoWarn 放水、无注释代码坟场）；垃圾集中在清晰可枚举的外围带 |
| 测试与验证证据 | 7.5/10 | 5136 测试、真实 Testcontainers 集成、多个 publish-and-run AOT fixture；但 HTTP 主链 NativeAOT 声称缺「运行原生产物」硬证据 |
| 主链收口度 | 8/10 | 模块系统、Dynamic API scanner 已物理删除；残留 3 处双轨 + 5 处主链未隔离反射 |

**核心判断**：框架内核（Core / Ddd / Metadata / CodeGenerator / 权限 / 租户 / Setting / Feature）已达到生产级质量，「框架内核承载复杂性、业务 Feature 短路径」的设计哲学在 LibraryManagement / SaaSHelpdesk 两个 Golden App 中得到验证。当前最大的风险不是架构缺陷，而是三类「收口欠账」：① AOT 验证证据与文档声称不匹配；② 少数未声明的双轨与主链反射残留；③ 外围半成品模块（CAP、FreeSql/SqlSugar、占位项目）与大量未合并 ready PR 造成的状态漂移。

> **复核修订**（#122）：③ 中「大量未合并 ready PR」不成立——复核时 GitHub 开放 PR 为 0；#106–#108 已由 #109 吸收后关闭，#119 已合并，#120 关闭未合并（修复进入 #119 链）。外围半成品模块与占位项目的事实仍成立，定性移交 #130/#131 逐项裁定。「生产级质量」为原审查主观评价，见顶部阅读提示。

---

## 二、基础功能与完成度评估

### 2.1 已验证的真闭环（声称与代码一致）

| 能力 | 证据 |
| --- | --- |
| Tenant Management | `src/Framework/Ddd/CrestCreates.Application/Tenants/` 15 文件，含 Bootstrapper、DeletionGuard、ConnectionString、SettingDefaults/FeatureDefaults Seeder |
| Setting / Feature Management | `Application/Settings/`、`Application/Features/` 各约 10 文件，含 Store/Resolver/审计/权限定义 |
| Permission | `Infrastructure/CrestCreates.Authorization/` Checker/GrantManager/GrantStore（走仓储）/TenantScopeValidator/AOP 拦截器齐备 |
| BackgroundJob | ISchedulerService + Quartz + 4 种重试策略 + 历史仓储 + 集成测试 |
| ObjectMapping | SourceGenerator 8 文件，samples 已消费生成产物 |
| Dynamic API AoT | `DynamicApiAotSourceGenerator` 为主链；`DynamicApiScanner`/`DynamicApiEndpointExecutor` 在 src/tests **零命中**，已物理删除 |
| Workflow / HumanTask | 条件策略、挂起提交、outbox 续跑；PG AOT fixture 含崩溃恢复 sentinel |
| Control Plane 审查链 | authoring → 确定性 review（hash 输入契约 / 投影 scope 绑定 / report 快照）→ package evidence store（双 provider、原子插入、PG V015）→ HumanTask 审批联动 |
| 模块系统 | 纯编译期聚合（BuildTasks `GenerateAggregatedModuleCode` → `ModuleAutoInitializer.g.cs`），**无运行时扫描残留**，已完全收口 |

### 2.2 声称大于实际或诚实标注的缺口

- **Audit 清理 / governance 未实现**：`src/Runtime/Audit` 无任何 retention/purge 实现。文档声称本身诚实，属已知缺口。
- **HTTP 主链 NativeAOT-verified 不成立（按自身定义）**：ci.yml 对 CapabilityEndpoint fixture 只做 publish（native link）不做 run；有 run 步的 `aot-validation.yml` 已 `on: []` 禁用。AGENTS.md 的严格定义要求 publish **and** run。
- **Workflow 无独立 AOT fixture**，仅靠 PG AotHost sentinel 间接覆盖；**Core 的 NativeAOT-first 是传递性推论**，全 src 仅 1 个 csproj 声明 `IsAotCompatible`。
- **半成品**：Localization（6 文件）、OpenApi（8）、PluginSystem（6）、FileManagement（26）、Organization（19）、EventStore（4）。
- **占位项目**：`Integration.ExternalApi`、`LegacyDatabase`（各 1 文件）、`Metadata/Draft/CrestCreates.Draft`（2 文件）、`Infrastructure/Configuration`（3 文件）、`CrestCreates.Agent.Runtime`（AssemblyMarker 自述 "Currently empty — reserved"）。

> **复核修订**（#122）：HTTP 缺口确认。但范围需精确：active ci.yml 中 Memory JSON contracts、ControlPlane JsonContracts、PG AotHost（含 `CRESTCREATES_WORKFLOW_CONDITION_AOT_OK` 等原生 sentinel）、Asset/Procurement Golden App fixture 均有原生 publish/link/run 门禁；缺口集中在 CapabilityEndpoint HTTP fixture（仅 publish/link，其 `.Tests` 走 WebApplicationFactory/JIT）以及 MCP/Agent.Tools 主 fixture（仅 publish/link）。Core/Workflow 的「无独立 fixture、声明数量少」不足以推导能力缺失，组合覆盖边界与独立证据矩阵由 [#123](https://github.com/OrchesAdam/CrestCreates/issues/123) 建立。半成品/占位项目的规模事实成立，定性逐项移交 #131。

### 2.3 文档未覆盖的重资产

- **Agent 子树 21 个项目**（Memory.Accountability / Projection / ReadCore / Llm / Tools / Authoring / ControlPlane）是全库最大投资，AGENTS.md 能力地图几乎未提。
- `Tooling/CrestCreates.JsonContracts.*`（3 项目）、`Metadata.AgentTool.Abstractions`、`Metadata.Mcp.Abstractions`、`Runtime.Persistence.PostgreSql`（承载 Control Plane 持久化，远超「Persistence 抽象」定位）。

### 2.4 测试版图

| 区域 | 项目数 | 测试方法数 | 评价 |
| --- | --- | --- | --- |
| Runtime | 40 | ~2407 | 最厚但严重不均：Agent 独占 1747，DistributedTransaction 仅 4 |
| Framework | 21 | ~881 | 主链集成测试真实（WebApplicationFactory + Testcontainers PG + itest schema，零 Moq） |
| Metadata | 6 | 718 | 良好 |
| Tooling | 5 | 464 | 良好 |
| Boundary | 1 | 155 | 纯架构规则，主动封死 legacy 回归 |
| Core | 1 | **23** | 与「NativeAOT-first / verified」声称严重不匹配 |
| Integrations | 7 | 118 | 基本只有 MCP 有测试 |

> **复核修订**（#122）：表中数字为静态统计，不等于质量或 AOT 能力结论——测试方法数与 `IsAotCompatible` 项目数不能推导「不支持 NativeAOT」。能力判断以真实 publish/link/run 证据为准，矩阵由 #123 建立。

亮点：边界测试包含 `LegacyAuditLoggingIsNotEnabledByDefault`、`McpProductionSources_DoNotUseForbiddenExecutionOrReflectionFallbacks`、`ProcurementMainlineAndNativeAotAcceptanceTestsAreGuarded` 等防退化规则——这是框架级项目最难得的部分。

---

## 三、架构设计评估

### 3.1 做得好的

1. **唯一主链执行坚决**。Dynamic API 的 runtime scanner/executor 是物理删除而非降级保留，消除了「第二套也能跑的实现」这一最常见的框架腐化源。
2. **双管线代码生成职责清晰**。Source Generator（逐项目）与 BuildTasks（跨项目聚合）分工明确，生成物覆盖 20+ 类胶水代码。
3. **依赖边界有强制力**。AGENTS.md 声明的 6 条依赖规则全部有对应边界测试，抽查 Core / Metadata.Abstractions / Runtime 的 ProjectReference 无违规。
4. **治理面与运行面分层正确**。Agent Control Plane 审查/预览/提交激活请求，不绕过授权、不直接执行 runtime handler，符合「Control Plane 允许严肃，Runtime Handler 必须朴素」。
5. **测试信号与主链一致**。legacy 路径无残留测试误导维护者；集成测试不拿 Mock 冒充。

### 3.2 架构问题清单（按严重度）

**P0 — 必须近期处理**

1. **UnitOfWorkFactory 未声明双轨**。`CrestCreates.Data.Abstractions.IUnitOfWorkFactory`（经 OrmModuleBase 注册）与 `CrestCreates.Infrastructure.UnitOfWork.IUnitOfWorkFactory`（经 AddUnitOfWork 注册）是两个命名空间的同名接口+同名实现并存，无任何 Obsolete 标记或退出说明；后者还含 `Type.GetType` 硬编码三个 ORM 类型名 + `catch{}` 吞异常 + AppDomain 全程序集扫描 fallback（`UnitOfWorkFactory.cs:97-182`）。这是当前 src/ 内最严重的双轨。

   > **复核修订**（#122）：双轨确认，且范围扩大——`Data.Abstractions` 侧的 `UnitOfWorkFactory.cs`（`UnitOfWorkBase/UnitOfWorkFactory.cs:47-49`）同样含 AppDomain 扫描，并带 IL2026/IL3050 suppressions（:14、:16）；Infrastructure 侧反射位于 `UnitOfWorkFactory.cs:97-123、173-175`。收口时不能把任一侧现实现认定为最终主链，由 [#124](https://github.com/OrchesAdam/CrestCreates/issues/124) 统一契约并移除两侧扫描。
2. **HTTP 主链 AOT 证据缺口**（见 2.2）。要么补 run 步，要么把文档声称降级为 trimming-verified。文档声称与证据不匹配比没有证据更危险——它会误导后续所有 AOT 决策。

   > **复核修订**（#122）：「降级为 trimming-verified」不是可选等价项——该项目没有对应 trim 证据，按校准规则应写「未验证 / 待确认」。修复不能只加阻塞 run 命令，需受控启动、readiness、真实 HTTP 请求断言、失败传播与进程回收，由 [#123](https://github.com/OrchesAdam/CrestCreates/issues/123) 承接；AGENTS.md 声明同步校准。

**P1 — 影响主链可信度**

3. **主链上的未隔离反射**（违反 AOT 分层声明规则）：
   - `EventBus.Local/DefaultLocalEventDispatcher.cs:43-46` — MakeGenericMethod 分发 + 注入 IServiceProvider 做 service locator；
   - `EventBus.RabbitMQ/Consuming/RabbitMqConsumer.cs:62,390-401` — 反射「寻找」生成器产物 + AppDomain 按短名解析事件类型（生成主链与反射桥并存）；
   - `DynamicApi/DynamicApiRouteConvention.cs:12` — 运行时读 Attribute 定路由。

   > **复核修订**（#122）：LocalEvent（:43 MakeGenericMethod、:46 Invoke、:52 service locator，dead-letter 同包）与 RabbitMQ（:62 Type.GetType 生成注册表、:390-392 AppDomain 扫描）确认，分别移交 [#126](https://github.com/OrchesAdam/CrestCreates/issues/126)/[#127](https://github.com/OrchesAdam/CrestCreates/issues/127)（验收需覆盖失败重试/重放）。`DynamicApiRouteConvention` 需限定：该类的实例反射方法（`ResolveServiceRoute/ResolveHttpMethod/ResolveActionRoute`）在 src/tests/samples 中未找到运行时消费者，`AddDynamicApi` 的注册不能证明其被执行；generated runtime 仅消费静态 `IsScalar`（`DynamicApiGeneratedRuntime.cs:65`）；正式路由逻辑在编译期 `DynamicApiConventionAnalyzer`。属「反射代码残留」，不是反射进入主链；由 [#129](https://github.com/OrchesAdam/CrestCreates/issues/129) 收口。
4. **`DistributedTransaction.CAP` 是玩具实现**：`DefaultTransactionCompensator.CanCompensateAsync` 硬编码 return true、补偿存内存字典、`BeginAsync` 空操作、9 处 CS1998、废话中文注释。挂着 CAP 的名字但没有 CAP 的任何语义（无持久化 outbox、无 broker 确认）。要么补齐，要么明确降级为抽象演示并移出正式 Provider 名单。

   > **复核修订**（#122）：整体归因过强，需修正。该项目不是空壳：存在真实 `AddCap`（`DistributedTransactionCapServiceCollectionExtensions.cs:48`）、`CapDistributedEventPublisher` 与 `PersistentTransactionCompensator`（:37）。实际风险是入口语义分裂——模块入口注册内存版 `DefaultTransactionCompensator`（`DistributedTransactionCapModule.cs:18`），扩展入口注册持久化版；`CanCompensateAsync` 硬编码 true（`DefaultTransactionCompensator.cs:35-38`）与内存字典（:14）属实。不能整体判为玩具或直接改名归档，由 [#125](https://github.com/OrchesAdam/CrestCreates/issues/125) 收口两入口与补偿真实性。
5. **FreeSql / SqlSugar 是半完成 Provider**：两个 Repository 约 78% 同构复制粘贴；FreeSql 审计拦截器软删除 todo 未实现；EFCore 有 16 个具体仓储而它们各只有 1 个通用仓储。AGENTS.md 的 AOT 分层声明对它们缺失。

   > **复核修订**（#122）：重复率与文件数量不足以直接决定「必须抽共享基类」。先按消费者/行为建立 Provider 能力矩阵、独立声明 AOT 支持级别，再有据重构或归档，由 [#130](https://github.com/OrchesAdam/CrestCreates/issues/130) 承接。
6. **CRUD DTO 序列化回退 legacy path**：`CrudServiceSourceGenerator.cs:987` 主动 `#warning`——生成 DTO 无法用应用侧 JsonSerializerContext，每次编译刷警告，与「删除 legacy fallback」原则直接冲突，需 BuildTask 方案根治。

   > **复核修订**（#122）：警告位置在生成器自身源码（`CrudServiceSourceGenerator.cs:987`，随 CodeGenerator 项目编译触发），指向生成 DTO body 的编译顺序契约缺口。修复需要解决实际 body/response 序列化契约，删警告不等于解决；由 [#128](https://github.com/OrchesAdam/CrestCreates/issues/128) 承接。

**P2 — 收口欠账**

7. **Obsolete 生成器的旧产物仍在被消费**：SaaSHelpdesk `TicketApi` 仍用 `CrestApiController`，`CrudControllerSourceGeneratorTests` 仍在维护。Obsolete 了生成器但没完成退出，属「过渡变成长期」。
8. **占位项目无退出标准**：ExternalApi / LegacyDatabase / Draft / Configuration / Agent.Runtime 等 1-3 文件项目共约 30 个 ≤3 文件项目，部分是合法聚合壳，部分是无声明的占位。
9. **Sample 平台复杂性泄漏**：AssetManagement / ProcurementApproval 手写 `Replace(ServiceDescriptor...)` outbox consumer activation 工厂（Issue 88 C88-01 已定性为框架缺口，issue-118 生成器正在消除，方向正确需跟到底）。

   > **复核修订**（#122）：已解决。PR #119（merged，master `cd4d7208`）落地生成路径后，Asset/Procurement 正式 Host 与 Workflow consumer 均已移除手写激活实现，改用 `[GenerateOutboxConsumerActivation]`（`AssetMaintenanceWorkflowService.cs:179`、`ProcurementHumanTaskIntegration.cs:187`、`WorkflowContinuationOutboxConsumer.cs:9`）+ `AddOutboxRequiredConsumer` 注册；生成器对「已有手写实现」有显式诊断。C88-01 主链缺口关闭，不再作为待实现框架缺口。
10. **边界规则未覆盖**：「Dynamic API 必须 compile-time generated」「多租户统一 TenantId」两条 AGENTS.md 明规则无边界测试强制，仅靠删除和惯例维持。

### 3.3 流程风险

- **大量 ready PR 长期未合并**（PR106/107 及更早）、worktree 堆积，主分支状态落后于已完成工作。对一个 134 项目的框架，这是比任何单点技术债都大的演进风险——后续工作的 base 不断漂移，review 成本指数上升。
- memory.md 仍是 2026-09-30 的 handoff，与 master（PR#119/#120 已合并）已不同步。

> **复核修订**（#122）：以上两条原文基于 09-30 memory 的过期推断，当前事实为——
>
> 1. **「ready PR 积压」不成立**：2026-10-10 复核时 GitHub 开放 PR 为 0（[open PR 查询](https://github.com/OrchesAdam/CrestCreates/pulls?q=is%3Apr+is%3Aopen)）。#106–#108 等并未长期滞留：其内容经 [PR #109](https://github.com/OrchesAdam/CrestCreates/pull/109)（merged 2026-09-30，`c0160360`）整体吸收后关闭；[PR #119](https://github.com/OrchesAdam/CrestCreates/pull/119) 已合并（2026-10-09，`cd4d7208`）；[PR #120](https://github.com/OrchesAdam/CrestCreates/pull/120) 关闭未合并，其修复提交 `1a55931d` 已进入 #119 链，不能写成「两者均已合并」。**worktree 堆积属实**（本机仍保留 20+ 个旧 worktree），但由本地 worktree 数量推导 PR 积压是错误推断。
> 2. **memory.md 不同步确认，已由本项修复**：memory.md 已更新为当前状态，原 2026-09-30 handoff 归档至 [`2026-10-10-memory-handoff-archive.md`](./2026-10-10-memory-handoff-archive.md)。
>
> 保留原句以记录审查历史；以本修订为准。

---

## 四、垃圾代码评估（严重度 3/10）

- **标记纪律极好**：全 src 仅 2 条真实 TODO、无 `#if false`、无注释代码坟场、无全局 NoWarn、`#pragma` 仅 16 处且多带理由。
- **垃圾集中带清晰**：① DistributedTransaction.CAP 整个项目；② FreeSql/SqlSugar 复制粘贴对；③ UnitOfWorkFactory 反射 fallback；④ CRUD legacy JSON path。全部位于外围 Provider/集成层，**未污染内核主链**，清理成本低、边界明确。
- 46 个 async 方法无 await（CS1998），其中 9 处在 CAP，建议随 CAP 处置一并清理。
- `99_RecycleBin/` 机制在用但体量小（388K），说明废弃代码大多真删了——与「物理删除 legacy」策略一致，可接受。
- 公共 API 几乎无 XML 文档注释（`///` 共 8446 行但 Domain/Application 核心类型近 0），符合内部规范但对框架类产品（消费者是其他开发者）偏弱，属定位问题而非垃圾。

---

## 五、演进建议（按优先级）

### 近期（1-2 个迭代）：把「声称」变成「证据」

1. **修复 AOT 证据链**：重新启用 `aot-validation.yml` 或在 ci.yml 给 CapabilityEndpoint fixture 补 run 步；为 Core 和 Workflow 建独立 publish-and-run fixture（可复用 PG AotHost 的 sentinel 模式）；在此之前把 AGENTS.md 中 HTTP/Workflow 的声称降级为 trimming-verified。

   > **复核修订**（#122）：方向确认，方式修正——(a) 不能只补阻塞式 run，需受控启动/readiness/真实请求/失败传播/进程回收；(b) Core/Workflow 是否建独立 fixture 先由证据矩阵决定，不预设；(c) 声明校准不能写 trimming-verified（无对应 trim 证据），应写「未验证 / 待确认」。全部由 [#123](https://github.com/OrchesAdam/CrestCreates/issues/123) 承接。
2. **收口 UnitOfWork 双轨**：二选一（建议保留 `Data.Abstractions` 侧，它是 OrmModuleBase 主链），给另一方标 Obsolete 并定删除窗口；删除 `Type.GetType` + AppDomain 扫描 fallback，改为生成器/显式注册。
3. **处置 CAP**：二选一——(a) 补齐真实 CAP 语义（持久化 outbox + broker 确认），或 (b) 重命名为 InMemory demo 并从正式 Provider 名单/AOT 声明中剔除。当前状态最糟：名字承诺了语义，实现没有。

   > **复核修订**（#122）：处置前先修正归因——项目含真实 `AddCap`/publisher/持久化补偿器，问题核心是模块入口与扩展入口注册不同实现。先修入口分裂与假补偿，再决定补齐或降级；由 [#125](https://github.com/OrchesAdam/CrestCreates/issues/125) 承接。

### 中期：消除结构性重复与外围欠账

4. **ORM Provider 策略收敛**：FreeSql/SqlSugar 提取共享 Repository 基类消除 78% 复制；明确每个 Provider 的支持级别声明（按 AGENTS.md AOT 分层规则）；补齐 FreeSql 软删除审计或显式标注不支持。
5. **主链反射隔离**：LocalEventBus 分发改为生成器注册委托（与 RabbitMQ 订阅生成器同思路）；RabbitMqConsumer 直接调用生成产物而非反射查找；DynamicApiRouteConvention 的路由信息移入编译期生成。
6. **完成 Obsolete 退出**：SaaSHelpdesk TicketApi 迁到 generated Minimal API，删除 CrudControllerSourceGenerator 及其测试，兑现「过渡必须可删除」。
7. **占位项目立法**：要么给每个占位项目写明意图与退出标准（进 AGENTS.md 或各自 README），要么移入 99_RecycleBin。30 个 ≤3 文件项目对新人理解成本很高。

### 长期：完成治理主线与平台长尾

8. **完成 Issue 87/88 durable activation**：durable 聚合/CAS/replay、submission 操作身份、request+HumanTask 事务、activation intent/receipt、真实 runtime 安装——这是当前唯一一条「开了头且明确不可关闭」的主线，建议作为下一阶段单一焦点，避免再开新战线。

   > **复核修订**（#122）：该建议已被 [#88 最终决策](https://github.com/OrchesAdam/CrestCreates/issues/88#issuecomment-5906675433)替代，不成立。durable activation 聚合/CAS/replay 与 submission 身份/HumanTask 事务被判为 **D 类（应用侧范围）**，不属于框架缺口；#87/#88 均已 completed，不得据此重开。当前 master 事实：review/package artifact 已有持久化（InMemory + PostgreSQL），但 activation 请求/门/审计仍为内存实现（`DefaultDescriptorActivationRequestService.cs:31`、`InMemoryRuntimeActivationGate`、`InMemoryDescriptorActivationAuditor`）——「工件持久化」不等于「durable activation」。仅当出现新的、可复现的跨应用失败证据，才讨论独立范围复议。
9. **Audit retention/governance**：写入/脱敏/查询闭环后，清理策略是审计能力的合规必需项。
10. **流程治理**：设定 ready PR 合并或关闭的 SLA（如 2 周）；清理陈旧 worktree；每次主链状态变化后同步 memory.md 与 AGENTS.md 能力地图（补上 Agent 子树、JsonContracts、Runtime.Persistence.PostgreSql）。

    > **复核修订**（#122）：PR 合并 SLA 当前无适用对象（开放 PR = 0）；陈旧 worktree 清理不在本项授权范围（本地保留 20+ 旧 worktree，属维护者决策）。memory.md 同步与能力地图补充已由本项完成；Agent 子树、JsonContracts、Runtime.Persistence.PostgreSql 已录入 AGENTS.md。
11. **边界测试补两条规则**：Dynamic API compile-time-only、统一 TenantId——把口头原则变成强制。

    > **复核修订**（#122）：复核确认边界测试目前只有项目引用类规则（`DependencyBoundaryTests.cs` 对 DynamicApi 的约束属引用规则），两条口头原则均无强制守卫。TenantId 守卫由 [#131](https://github.com/OrchesAdam/CrestCreates/issues/131) 承接；Dynamic API compile-time-only 守卫随 [#129](https://github.com/OrchesAdam/CrestCreates/issues/129) 收口范围一并评估。
12. **长尾模块决策**：Localization / OpenApi / FileManagement / Organization / PluginSystem / EventStore 逐个决定「补齐到闭环」或「明确标注半成品并从平台能力宣传中移除」，避免半成品静默膨胀。

### 方向性建议

- **继续加注 Agent 治理面**：这是该框架与 ABP 系产品差异化最大的资产（21 项目 + 确定性 review + 哈希契约 + NativeAOT JSON 契约），但文档存在感与投资严重不匹配。建议在 AGENTS.md 为其单设章节，明确它与 Runtime 执行面的边界。
- **警惕项目数量膨胀**：134 个 src 项目 vs ~70% 完成度，意味着大量认知开销花在导航上。未来新能力优先进入现有项目，新建项目应有「为什么不能用现有项目承载」的书面理由。
- **保持「物理删除 legacy」的传统**：这是本框架区别于大多数腐化框架的核心优势，任何「保留双轨过渡」的请求都应默认拒绝，除非带明确删除日期。

---

## 附：审查方法说明

本报告由四个并行只读审查通道汇总：① 架构一致性与主链收口（反射热点、双轨、依赖方向）；② 代码质量（TODO/桩代码/重复/警告源）；③ 测试版图与 AOT 证据核实；④ 能力地图与文档声称逐项对账。所有关键结论均有文件级证据，主要证据文件已在正文标注。

---

## 勘误索引（2026-10-10 · Issue #122 复核）

> 修订基线：`cd4d7208751d4578c673ffd4a04d1e694c94dd7a`（github.com/OrchesAdam/CrestCreates，master，与远端一致）。逐项证据与完整裁定见 [`2026-10-10-framework-architecture-review-follow-up.md`](./2026-10-10-framework-architecture-review-follow-up.md)。本索引只列被**修正/限定/已解决**的结论；其余结论状态（确认 / 待验证）以修订表为准。

| # | 原结论位置 | 原表述（历史） | 复核结论 | 证据 |
| --- | --- | --- | --- | --- |
| E1 | §一、§3.3 | 「大量 ready PR 长期未合并」「大量未合并 ready PR 造成的状态漂移」 | 误判：开放 PR = 0；#106–#108 经 #109 吸收后关闭；worktree 堆积属实但≠PR 积压 | [PR #109](https://github.com/OrchesAdam/CrestCreates/pull/109)、#109 合并 `c0160360` |
| E2 | §3.3 | 「master（PR#119/#120 已合并）」 | 修正：#119 已合并（`cd4d7208`）；#120 关闭未合并，修复提交 `1a55931d` 进入 #119 链 | [PR #119](https://github.com/OrchesAdam/CrestCreates/pull/119)、[PR #120](https://github.com/OrchesAdam/CrestCreates/pull/120) |
| E3 | §2.2、§3.2-P0-2、§五.1 | HTTP AOT「补 run 或降级 trimming-verified」 | 修正：降级不是等价项（无 trim 证据→写「未验证/待确认」）；不能只加阻塞 run；由 #123 承接 | `ci.yml:239-246`、`aot-validation.yml`（`on: []`）、`AotFixtureTests.cs:16,69` |
| E4 | §2.4 | Core「23 测试方法…严重不匹配」 | 限定：数字≠质量/能力；以 publish/link/run 证据矩阵为准 | #123 |
| E5 | §3.2-P0-1 | UnitOfWork「后者还含 Type.GetType…」 | 扩大确认：两侧工厂均有运行时扫描；Data 侧带 IL2026/IL3050 suppressions | `Infrastructure/UnitOfWork/UnitOfWorkFactory.cs:97-123,173-175`、`Data.Abstractions/UnitOfWorkBase/UnitOfWorkFactory.cs:14,16,47-49` |
| E6 | §3.2-P1-3 | 「DynamicApiRouteConvention:12 运行时读 Attribute 定路由」 | 限定：实例反射方法无运行时消费者；generated runtime 仅用静态 `IsScalar`；属残留非主链执行 | `DynamicApiGeneratedRuntime.cs:65`、`DynamicApiExtensions.cs:27` |
| E7 | §3.2-P1-4、§五.3 | CAP「玩具实现」「整体无 CAP 语义」 | 修正：真实 AddCap/publisher/持久化补偿存在；核心问题是两入口注册分裂与假补偿 | `Extensions/DistributedTransactionCapServiceCollectionExtensions.cs:37,48`、`Modules/DistributedTransactionCapModule.cs:18` |
| E8 | §3.2-P1-5、§五.4 | FreeSql/SqlSugar「必须抽共享基类」 | 限定：重复比例不构成决策依据；先 Provider 能力矩阵 | #130 |
| E9 | §3.2-P2-9 | Sample 手写 activation 工厂「需跟到底」 | 已解决：#118/#119 后生成路径落地，手写实现已移除 | `15fbe3f2`（master 祖先）、`[GenerateOutboxConsumerActivation]` 消费点 |
| E10 | §五.8 | durable activation「唯一不可关闭主线、单一焦点」 | 被替代：#88 判 D 类（应用侧）；#87/#88 已 completed，不重开；推翻需新的可复现跨应用失败证据 | [#88 最终决策](https://github.com/OrchesAdam/CrestCreates/issues/88#issuecomment-5906675433) |
| E11 | §五.10 | 「ready PR SLA」 | 无适用对象（开放 PR = 0）；worktree 清理不在本项范围 | 2026-10-10 GH 查询 |
| E12 | §五.11 | 边界测试两条规则缺失 | 确认；TenantId → #131，Dynamic API 守卫随 #129 评估 | `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/` |
| E13 | §一、§2.4 | 评分/完成度/「生产级」 | 限定：主观评价、无统一度量，不作为生产就绪保证或验收事实 | 顶部阅读提示 |
