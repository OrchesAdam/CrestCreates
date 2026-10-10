# 框架整体架构审查复核与 GitHub 任务编排

日期：2026-10-10
原始报告：`docs/review/2026-10-10-framework-overall-architecture-review.md`
静态复核代码基线：`cd4d7208751d4578c673ffd4a04d1e694c94dd7a`
总控：[Issue #121](https://github.com/OrchesAdam/CrestCreates/issues/121)

> **文档状态（2026-10-10 · Issue #122 校准）**：本文件首轮复核内容（「判断」「对原报告的修订意见」「已创建任务」）保留为历史记录；文末「#122 校准」章节包含逐项修订分类表、#103–#108 吸收映射、#87/#88 边界与 B12 裁定、后续任务对应，是当前生效版本。评分与完成度引用规则见原始报告顶部阅读提示。

## 判断

报告对唯一主链、编译期生成、AOT 证据和外围集成的关注是合理的，适合作为问题发现清单。但其完成度评分和“生产级”结论没有统一可复现度量，流程状态与部分技术归因错误，不宜直接作为路线图或生产就绪承诺。

本轮完成文件/调用点静态核对、GitHub Issue/PR 状态和决策记录核对，并创建任务；未重新运行构建、测试或原生发布。Issue 中的验收条目是未来完成条件，不是本轮通过记录。

## 对原报告的修订意见

| 项目 | 复核结论 | 对编排的影响 |
| --- | --- | --- |
| HTTP AOT | 确认 ci.yml 发布后缺实际原生 HTTP 请求验证；AotFixture.Tests 使用 WebApplicationFactory。aot-validation.yml on: []，Host app.Run() 常驻 | 补受控启动、readiness、真实请求、失败传播、日志与进程回收；不能只加阻塞 run 命令 |
| Core/Workflow | 测试数量和 IsAotCompatible 项目数不能证明能力缺失。PG AotHost 与 Golden App 已有执行覆盖，但需明确边界 | 先建立场景/Provider/RID 证据矩阵，再补真正缺口，不预设新建独立 fixture |
| UnitOfWork | 两套工厂均含 AppDomain 扫描，Data 侧还有 AOT/trim suppressions | 统一契约同时替换两边扫描，不简单保留 Data 原实现 |
| CAP | 有 AddCap、真实 publisher、持久化 logger/compensator；模块入口却注册内存版本 | 优先修两入口语义分裂与假补偿，不能整体判空壳或直接重命名归档 |
| LocalEvent | 非泛型分发 MakeGenericMethod/Invoke；dead-letter manager 进入该入口 | 验收需覆盖失败重试/重放，不能只验证泛型 happy path |
| RabbitMQ | 反射寻找生成订阅 + AppDomain 按事件名扫描确实存在 | 强类型消费生成元数据，核查真实 broker 与 SDK 能力边界 |
| DynamicApiRouteConvention | 有 Attribute/MethodInfo 反射 API，但 generated runtime 当前找到的是 IsScalar 调用；AddDynamicApi 注册该类不能证明反射方法被执行 | 先确认调用图，再清理/隔离遗留 API，不能把静态存在直接等同主链执行 |
| CRUD JSON | 生成器确有同轮 DTO/JsonSerializerContext 缺口警告 | 修编译顺序与契约，不只消警告；方案不预设唯一技术 |
| ORM / 占位项目 | 文件数与重复比例不足以决定质量或必须共享基类；合法聚合壳须保留 | 先按消费者/行为列能力矩阵，再有据重构或归档 |
| PR 状态 | GitHub 开放 PR 查询结果为 0；#107 closed/unmerged；#119 merged；#120 closed/unmerged，其修复进入 #119 提交 | 不安排“清积压 ready PR”；后续文档应区分 merged 与 closed、代码被吸收 |
| #87/#88/#118 | 三项已 completed；#118 手写 consumer activation 工厂已由生成主链替代 | 不重复开工、不自动重开 |
| durable activation | #88 最新决策明确 D 类应用侧；与旧 memory 的范围冲突 | 遵守已裁定范围，只有新的可复现跨应用失败证据才讨论推翻 |
| Audit | retention 是可讨论候选，本轮静态审查不等于合规需求证明 | 先做有限设计裁定，未授权扩大成完整治理工程 |

状态来源：[PR #107](https://github.com/OrchesAdam/CrestCreates/pull/107)、[PR #119](https://github.com/OrchesAdam/CrestCreates/pull/119)、[PR #120](https://github.com/OrchesAdam/CrestCreates/pull/120)、[Issue #87](https://github.com/OrchesAdam/CrestCreates/issues/87)、[#88 最终决策](https://github.com/OrchesAdam/CrestCreates/issues/88#issuecomment-5906675433)、[Issue #118](https://github.com/OrchesAdam/CrestCreates/issues/118)。

关键代码位置：
- `.github/workflows/ci.yml`、`.github/workflows/aot-validation.yml`
- `tests/Framework/Api/CrestCreates.CapabilityEndpoint.AotFixture.Tests/AotFixtureTests.cs`
- `src/Persistence/CrestCreates.Data.Abstractions/UnitOfWorkBase/UnitOfWorkFactory.cs`
- `src/Framework/Infrastructure/CrestCreates.Infrastructure/UnitOfWork/UnitOfWorkFactory.cs`
- `src/Runtime/DistributedTransaction/CrestCreates.DistributedTransaction.CAP/Modules/DistributedTransactionCapModule.cs`
- `src/Runtime/DistributedTransaction/CrestCreates.DistributedTransaction.CAP/Extensions/DistributedTransactionCapServiceCollectionExtensions.cs`
- `src/Runtime/Eventing/CrestCreates.EventBus.Local/DefaultLocalEventDispatcher.cs`
- `src/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ/Consuming/RabbitMqConsumer.cs`
- `src/Framework/Api/CrestCreates.DynamicApi/DynamicApiGeneratedRuntime.cs`
- `src/Tooling/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`

## 已创建任务

| 波次 | Issue | 依赖 |
| --- | --- | --- |
| 0 | [#122 [P1][基线] 校准架构审查、memory 与已交付主链状态](https://github.com/OrchesAdam/CrestCreates/issues/122) | 无 |
| 1 | [#123 [P1][NativeAOT] 补齐 HTTP 原生请求门禁并建立 Core/Workflow 证据矩阵](https://github.com/OrchesAdam/CrestCreates/issues/123) | #122 |
| 1 | [#124 [P1][UnitOfWork] 统一工厂契约与 Provider 注册，移除两侧运行时扫描](https://github.com/OrchesAdam/CrestCreates/issues/124) | #122 |
| 1 | [#125 [P1][CAP] 收口模块与扩展注册入口，隔离内存补偿并核实事务承诺](https://github.com/OrchesAdam/CrestCreates/issues/125) | #122 |
| 2 | [#126 [P1][LocalEvent] 用强类型注册统一泛型发布与重试分发](https://github.com/OrchesAdam/CrestCreates/issues/126) | #123 |
| 2 | [#127 [P1][RabbitMQ] 静态消费生成订阅与事件契约，移除反射查找桥](https://github.com/OrchesAdam/CrestCreates/issues/127) | #123 |
| 2 | [#128 [P1][CRUD JSON] 解决生成 DTO 的编译顺序与序列化契约缺口](https://github.com/OrchesAdam/CrestCreates/issues/128) | #123 |
| 3 | [#129 [P2][Dynamic API] 完成旧 Controller 退出并收口反射辅助 API](https://github.com/OrchesAdam/CrestCreates/issues/129) | #128 |
| 3 | [#130 [P2][ORM] 建立 Provider 支持矩阵与软删除契约，不先抽重复基类](https://github.com/OrchesAdam/CrestCreates/issues/130) | #124 |
| 3 | [#131 [P2][能力地图] 裁定占位与长尾模块，补充 TenantId 架构守卫](https://github.com/OrchesAdam/CrestCreates/issues/131) | #122 |
| 后续候选 | [#132 [P2][设计裁定] Audit retention/governance 最小平台契约与失败用例](https://github.com/OrchesAdam/CrestCreates/issues/132) | #122 |

执行建议：#122 → #123/#124（最多两个实现 PR）→ 空槽推进 #125 → #126 → #127 → #128 → #129。#130 在 #124 后；#131 可穿插；#132 仅形成设计裁定。本轮不派发编码、不设置触发自动执行的 ai-ready 标签、不臆定负责人或截止日期、不自动合并。

P1 表示下一轮主链可信度工作，尚无生产事故证据，不沿用报告的 P0 紧急定级。所有新 Issue 只完成编排，未声明技术债已经修复。Provider 的 AOT 能力独立举证；无 trim 证据时也不能随意改写 trimming-verified。移除文件归档到 99_RecycleBin。

总控的任务清单作为跟踪入口；每个工作项包含边界、依赖与完成条件。实施前按最新 master 更新证据，合并后更新 memory 与能力表。

---

# #122 校准（2026-10-10）

## 1. 核查快照与交付记录

| 项 | 记录 |
| --- | --- |
| 核查时间 | 2026-10-10 09:32–09:40（UTC+08:00，CST） |
| 仓库 / 默认分支 | `github.com/OrchesAdam/CrestCreates` / `master` |
| 远端 master SHA | `cd4d7208751d4578c673ffd4a04d1e694c94dd7a`（`git ls-remote`，与本地一致；相对首次静态复核基线 `cd4d7208` 无新增提交，结论不受新提交影响） |
| 工作区状态 | 仅 12 个未跟踪 docs 文件（用户产物）；无已跟踪文件修改；本项不批量 add、不清理、不覆盖 |
| GitHub 状态查询（2026-10-10） | 开放 PR = 0；#87/#88/#118 `completed`；#119 merged（2026-10-09）；#120 closed 未合并；#121–#133 open |
| 证据来源 | GitHub API（issue/PR 状态、PR 文件清单 REST 分页）；git（`rev-parse`/`ls-remote`/`merge-base --is-ancestor`）；master 工作树文件与行号；`docs/review/` 归档 |
| 判断适用范围 | 仅覆盖上述 SHA 的静态事实；未重跑构建/测试/原生发布；历史 CI 与 live 证据仅引用不重放 |
| 未验证部分 | B12 的行为层失败（需 live 凭据 + PostgreSQL）；保留容器的运行态（仅验证到容器存在）；历史 native 运行结果（引用既有门禁定义，未执行） |

## 2. 逐项修订分类表

状态图例：✅ 确认 / ⚠️ 误判或需限定 / ✔️ 已解决 / ⏳ 待验证（主观或证据不足）。证据为 GitHub 链接，或基线 `cd4d7208` 下的源码路径/行号。

### 总体结论（报告 §一）

| 原结论 | 复核状态 | 证据 | 修订结论 | 后续处理 |
| --- | --- | --- | --- | --- |
| 8.5/10、~70%、「生产级质量」 | ⏳ 主观 | 无统一可复现度量 | 保留为原审查主观评价；不作为生产就绪保证或验收事实 | 阅读提示（本项） |
| ① AOT 验证证据与文档声称不匹配 | ✅ 确认（范围限定） | `ci.yml:239-246`、`aot-validation.yml`（`on: []`） | 缺口集中在 HTTP fixture 原生执行；见 §2 表 | #123 |
| ② 未声明双轨与主链反射残留 | ✅ 确认 | 见架构清单各条 | | #124–#129 |
| ③ 外围半成品 + 「大量未合并 ready PR 造成状态漂移」 | ⚠️ 部分误判 | 开放 PR = 0；#106–#108 经 #109 吸收后关闭 | 半成品模块事实成立；PR 状态漂移不成立 | #130/#131 |

### 能力与缺口（报告 §二）

| 原结论 | 复核状态 | 证据 | 修订结论 | 后续处理 |
| --- | --- | --- | --- | --- |
| §2.1 真闭环清单 | ✅ 确认（口径：文件/调用点存在，未重跑测试） | 抽样：`DynamicApiScanner`/`DynamicApiEndpointExecutor` 在 src/tests 零命中（物理删除） | 保留；引用时注明未重跑 | — |
| §2.2 Audit retention 未实现 | ✅ 确认 | `src/Runtime/Audit` 无 retention/purge 实现 | 写入/脱敏/查询既有范围与 retention 未交付范围分开表述 | #132（仅设计裁定） |
| §2.2 / §3.2-P0-2 / §五.1 HTTP NativeAOT | ✅ 确认 + 修正 | 仅 publish：`ci.yml:239-240`；`.Tests` 走 `WebApplicationFactory`（`AotFixtureTests.cs:16,69`，JIT）；`aot-validation.yml` 禁用。对照有原生 run 门禁：Memory JSON contracts（`run-json-contract-aot-gates.sh`）、ControlPlane JsonContracts（`ci.yml:251-252`）、PG AotHost（`ci.yml:168`）、Asset/Procurement（`:117,123`） | 缺口 = 执行原生产物并做 HTTP 请求断言；「降级为 trimming-verified」不适用（无 trim 证据），应写「未验证/待确认」；不能只加阻塞 run | #123 |
| §2.2 Workflow/Core AOT | ⚠️ 限定 | PG AotFixture.Tests 为 publish+link+run，sentinel 含 `CRESTCREATES_WORKFLOW_CONDITION_AOT_OK` 等；Asset/Procurement fixture 原生 `Process.Start` | 「无独立 fixture」属实；测试/声明数量不推导能力缺失；组合覆盖边界需矩阵化 | #123 |
| §2.2 半成品模块清单 | ⚠️ 部分确认 | 模块文件规模事实 | 「半成品」定性后置逐项裁定；Organization 含身份/组织内核，不整体否定 | #131 |
| §2.3 重资产未覆盖（Agent 子树 / JsonContracts / PostgreSql store） | ✅ 确认 | AGENTS.md 原无对应条目 | 已录入 AGENTS.md 能力地图 | 本项 |
| §2.4 测试版图数字 | ⚠️ 限定 | — | 数字 ≠ 质量/AOT 能力结论 | 不作验收依据；#123 |

### 架构问题清单（报告 §3.2）

| 原结论 | 复核状态 | 证据 | 修订结论 | 后续处理 |
| --- | --- | --- | --- | --- |
| P0-1 UnitOfWork 双轨 | ✅ 确认 + 扩大 | Infra：`UnitOfWork/UnitOfWorkFactory.cs:97-123,173-175`；Data：`UnitOfWorkBase/UnitOfWorkFactory.cs:14,16,47-49` | 两侧均含运行时扫描（含 Data 侧 suppressions）；不将任一侧现实现认定为最终主链 | #124 |
| P1-3a LocalEvent 反射分发 | ✅ 确认 | `DefaultLocalEventDispatcher.cs:43,46,52`；dead-letter（`DefaultLocalDeadLetterManager`/`InMemoryDeadLetterStore`）同包 | 验收需覆盖失败重试/重放，不只泛型 happy path | #126 |
| P1-3b RabbitMQ 反射查找 | ✅ 确认 | `RabbitMqConsumer.cs:62,390-392` | 强类型消费生成元数据，核查 broker/SDK 边界 | #127 |
| P1-3c DynamicApiRouteConvention | ⚠️ 限定（归因修正） | 实例 `Resolve*` 方法无运行时消费者；generated runtime 仅用静态 `IsScalar`（`DynamicApiGeneratedRuntime.cs:65`）；DI 注册 `DynamicApiExtensions.cs:27` | 「反射代码残留」≠「反射进入主链」；正式路由逻辑在编译期 `DynamicApiConventionAnalyzer` | #129 |
| P1-4 CAP 玩具实现 | ⚠️ 修正 | 真实 `AddCap`/持久化补偿：`Extensions/DistributedTransactionCapServiceCollectionExtensions.cs:37,48`；模块入口注册内存版：`Modules/DistributedTransactionCapModule.cs:18`；`DefaultTransactionCompensator.cs:14,35-38` | 非整体空壳；核心风险是两入口语义分裂与假补偿 | #125 |
| P1-5 FreeSql/SqlSugar 半完成 | ⚠️ 限定 | 重复率/文件数事实保留 | 先按消费者/行为建立 Provider 能力矩阵与独立 AOT 声明，不预设共享基类 | #130 |
| P1-6 CRUD JSON #warning | ✅ 确认 | `CrudServiceSourceGenerator.cs:987`（生成器自身源码） | 生成 DTO body/response 契约缺口属实；删警告 ≠ 解决 | #128 |
| P2-7 Obsolete 生成器产物仍被消费 | ✅ 确认 | `CrestApiController`/`CrudControllerSourceGeneratorTests` 仍在 | 完成退出，兑现「过渡可删除」 | #129 |
| P2-8 占位项目无退出标准 | ✅ 确认（存在性） | 现有 ≤3 文件项目（含合法聚合壳） | 逐个写明意图/退出标准或归档 | #131 |
| P2-9 Sample activation 工厂 | ✔️ 已解决 | `15fbe3f2`（master 祖先）；`[GenerateOutboxConsumerActivation]`：`AssetMaintenanceWorkflowService.cs:179`、`ProcurementHumanTaskIntegration.cs:187`、`WorkflowContinuationOutboxConsumer.cs:9` | 手写 `Replace` 工厂已移除，生成路径为正式主链；C88-01 关闭 | 完成（#118/#119） |
| P2-10 边界规则未覆盖 | ✅ 确认 | `DependencyBoundaries.Tests` 仅有引用类规则（DynamicApi 相关为项目引用规则） | 两条口头原则均缺守卫 | #131（TenantId）；#129（Dynamic API 守卫评估） |
| P2-10（附）模块系统已收口 | ✅ 确认 | 纯编译期聚合，无运行时扫描残留 | 保留 | — |

### 流程风险（报告 §3.3）

| 原结论 | 复核状态 | 证据 | 修订结论 | 后续处理 |
| --- | --- | --- | --- | --- |
| 「大量 ready PR 长期未合并」 | ⚠️ 误判 | 开放 PR = 0；#106–#108 经 [PR #109](https://github.com/OrchesAdam/CrestCreates/pull/109)（`c0160360`）吸收后关闭 | 积压推断错误；本地 worktree 堆积（20+ 个）属实但与 PR 状态无关 | 更正（本项） |
| 「master（PR#119/#120 已合并）」 | ⚠️ 误判 | [#119](https://github.com/OrchesAdam/CrestCreates/pull/119) merged；[#120](https://github.com/OrchesAdam/CrestCreates/pull/120) closed 未合并，修复提交 `1a55931d` 在 master | 区分 merged / closed / 内容被吸收 | 更正（本项） |
| memory.md 与 master 不同步 | ✔️ 已解决 | memory.md 已重写；旧 handoff 归档 | | 本项 |

### 垃圾代码与演进建议（报告 §四、§五）

| 原结论 | 复核状态 | 证据 | 修订结论 | 后续处理 |
| --- | --- | --- | --- | --- |
| §四 垃圾集中带 | ✅ 大体确认（未逐项重验） | 与 §3.2 各条一致 | 随各后续项处置，不另立工作 | — |
| §五.1 AOT 修复建议 | ✅ 方向 + 方式修正 | 同 HTTP/Core 行 | 不能只加阻塞 run；不得降格 trimming-verified；fixture 不预设 | #123 |
| §五.8 durable activation「单一焦点、不可关闭主线」 | ⚠️ 被替代 | [#88 最终决策](https://github.com/OrchesAdam/CrestCreates/issues/88#issuecomment-5906675433)；#87/#88 completed | 不重开；推翻需新的可复现跨应用失败证据 | —（见 §4） |
| §五.9 Audit retention | ⚠️ 限定 | 候选而非合规已证实 | 仅设计裁定，不扩为完整治理工程 | #132 |
| §五.10 流程治理（PR SLA/worktree） | ⚠️ 部分误判 | 开放 PR = 0 | SLA 无对象；worktree 清理不在本项范围 | — |
| §五.11 边界测试两条规则 | ✅ 确认 | 同 P2-10 | | #131/#129 |
| §五.12 长尾模块决策 | ✅ 确认 | 同 §2.2 半成品 | | #131 |
| 方向性：Agent 治理面单设章节 | ✔️ 已落实 | AGENTS.md 能力地图更新 | | 本项 |
| 方向性：物理删除传统 / 项目数膨胀提醒 | ✅ 保留 | — | 维持原则 | — |

## 3. #109 对 #103–#108 的吸收映射

吸收判定依据：① [PR #109](https://github.com/OrchesAdam/CrestCreates/pull/109) 描述声明 "Consolidated merge of PRs #92–#108"；② #103–#108 的 REST 文件清单与 #109 的 180 文件 squash diff 对比：**全部 src/tests/samples 文件均在 #109 变更集且存在于 master `cd4d7208`**，唯二例外为被替换归档的两个文件（下表标注）；③ **closed ≠ merged**：#103–#108 在 GitHub 上均 closed/unmerged，内容经 #109 单一 squash 提交 `c0160360` 进入 master（friction log 基线记为 "PR #109 merged to master (c0160360), covering PRs #91-#108"）。

| 原 PR | 原承诺 / 主要行为 | 最终状态 | #109 或其他吸收提交 | 当前实现与测试证据（master） | 未吸收部分及原因 |
| --- | --- | --- | --- | --- | --- |
| [#103](https://github.com/OrchesAdam/CrestCreates/pull/103) | durable human review ownership 设计 | closed；设计进入 master | `c0160360` | `docs/superpowers/plans/2026-09-23-durable-activation-review-cutover.md` | durable activation 本体按 #88 判 D 类（应用侧），非本链缺口 |
| [#104](https://github.com/OrchesAdam/CrestCreates/pull/104) | 保留 review hash 输入跨序列化 | 吸收 | `c0160360` | `DescriptorDraftReviewHashInput` + JSON context、`DefaultDescriptorDraftReviewHashService`；`DescriptorDraftReviewHashServiceTests` | 无 |
| [#105](https://github.com/OrchesAdam/CrestCreates/pull/105) | review/package 投影绑定可见性 scope | 吸收（部分实现被后续取代） | `c0160360` | `ReviewProjectionScopeBindingTests`、`DescriptorActivationDiagnosticCodes`；旧 `ReviewResourceSnapshot.cs` 归档 `99_RecycleBin/2026-09-24-review-artifact-store/` | 原快照文件随 envelope/store 设计被替换 |
| [#106](https://github.com/OrchesAdam/CrestCreates/pull/106) | review report 输入快照 | 吸收 | `c0160360` | `DescriptorReviewReportInputSnapshot`、`DefaultDescriptorReviewReportBuilder`；`DescriptorReviewReportBuilderTests`、`ReviewReportInputNativeAotFixture` | 无 |
| [#107](https://github.com/OrchesAdam/CrestCreates/pull/107) | review artifact 持久化 + hash 解析统一 | 吸收（resolver 组件被 #108 链取代） | `c0160360` | `IAgentReviewArtifactStore`、InMemory + `PostgreSqlAgentReviewArtifactStore`、`AgentReviewArtifactFactory`；`AgentReviewArtifactStoreTests`、`PostgreSqlAgentReviewArtifactStoreTests`、`ReviewArtifactStoreNativeAotFixture`；旧 `InMemoryActivationBindingArtifactResolver.cs` 归档 `99_RecycleBin/2026-09-29-package-evidence-store/`，后继 `DefaultActivationBindingArtifactResolver` | 原 resolver 被替换归档 |
| [#108](https://github.com/OrchesAdam/CrestCreates/pull/108) | package/evidence artifact 精确父绑定持久化 | 吸收 | `c0160360` | `IAgentPackageArtifactStore`、`AgentPackageArtifactFactory/Validator`、InMemory + `PostgreSqlAgentPackageArtifactStore`（V015）；`PostgreSqlAgentPackageArtifactStoreTests`、`LongFingerprintTests`、`PackageArtifactStoreRuntimeTests`、`PackageArtifactFactoryProjectionTests`、`PackageEvidenceArtifactNativeAotFixture` | 无 |

**边界说明（必须区分）**：review artifact 与 package/evidence artifact 的持久化存在，**不证明** activation request/decision/runtime installation 已 durable——后者当前为内存实现：`DefaultDescriptorActivationRequestService.cs:31`（`ConcurrentDictionary`）、`InMemoryRuntimeActivationGate`、`InMemoryDescriptorActivationAuditor`；与 #88 决策 6/7（D 类）一致，无运行时安装。

## 4. #87/#88 边界与 durable activation 现状

- [Issue #87](https://github.com/OrchesAdam/CrestCreates/issues/87) 关闭报告（completed，2026-09-30）：B01–B11 通过；**B12 记为「⚠️ 已知 gap：unsupported intent 需单独 negative test case」**。
- [#88 最终决策](https://github.com/OrchesAdam/CrestCreates/issues/88#issuecomment-5906675433)：C88-01（B 类）→ 已由 #118/#119 交付；durable activation 聚合/CAS/replay 与 submission 身份/HumanTask 事务 → **D 类（应用侧）**；#50/#51 F 类、#76/#57 E 类。
- 现状（master `cd4d7208`）：review/package artifact 有 InMemory + PostgreSQL 持久化；激活请求/门/审计为内存实现；无 runtime installation。
- **冲突记录（保留历史）**：2026-09-30 handoff 曾称 "Only review/package artifacts are addressed... Issue87 cannot close"，并建议将 durable activation 作为后续主线；该状态已被 #88 最终决策与 #87 关闭取代。保留上述历史描述于归档 handoff（`2026-10-10-memory-handoff-archive.md`），现状以本节为准。
- **推翻条件**：只有出现新的、可复现且跨应用的框架失败证据，才讨论独立范围复议；本项未发现此类证据。#87/#88 不重开，#50/#51/#76/#57 不顺带启动。

## 5. B12 裁定（unsupported intent）

**裁定结果：确认缺口（静态）；行为层为「候选缺口/待验证」。**

| 项 | 内容 |
| --- | --- |
| 最小场景 | 对既有 Asset 维护演化链路提交超出支持范围的意图（需新领域算法/缺必要策略信息） |
| 期望行为 | spec B12：显式 unsupported / needs-clarification 结果；不伪造能力、不部署 |
| 当前行为 | 无 B12 negative case：live eval 仅 happy path 单用例（`AssetDeepSeekLiveAuthoringEvaluationTests`）；`src/Runtime/Agent/` 无 clarification 结果契约；确定性 parser 仅阻断结构级不支持（`OutputParserTests.UnsupportedOperation_Returns_Blocked`，operation 级） |
| 复现性 | 行为层失败需 live 凭据 + PostgreSQL，本次未运行 → 不声称已证实（候选缺口/待验证） |
| 责任归属 | Agent Authoring / Control Plane 负边界覆盖（live eval case set + 确定性边界断言） |
| 后续任务 | [#133 B12 — Unsupported Intent Negative Case](https://github.com/OrchesAdam/CrestCreates/issues/133)（bounded；不重开 #87） |

## 6. 后续任务对应（更新）

| 波次 | 工作项 | 状态 |
| --- | --- | --- |
| 0 | [#122](https://github.com/OrchesAdam/CrestCreates/issues/122) 基线校准 | 交付中（本文档所在 PR） |
| 1 | [#123](https://github.com/OrchesAdam/CrestCreates/issues/123) NativeAOT 门禁 / [#124](https://github.com/OrchesAdam/CrestCreates/issues/124) UnitOfWork / [#125](https://github.com/OrchesAdam/CrestCreates/issues/125) CAP | 待 #122 合并后启动（受 #121 最多 2 个实施 PR 约束） |
| 2 | [#126](https://github.com/OrchesAdam/CrestCreates/issues/126) LocalEvent / [#127](https://github.com/OrchesAdam/CrestCreates/issues/127) RabbitMQ / [#128](https://github.com/OrchesAdam/CrestCreates/issues/128) CRUD JSON | 依赖 #123 |
| 3 | [#129](https://github.com/OrchesAdam/CrestCreates/issues/129) Dynamic API 退出 / [#130](https://github.com/OrchesAdam/CrestCreates/issues/130) ORM 矩阵 / [#131](https://github.com/OrchesAdam/CrestCreates/issues/131) 能力地图 | 依赖前序 |
| 候选 | [#132](https://github.com/OrchesAdam/CrestCreates/issues/132) Audit retention 设计裁定 | 设计级 |
| 新增（本项裁定） | [#133](https://github.com/OrchesAdam/CrestCreates/issues/133) B12 unsupported intent negative case | 独立小任务，归 Agent Authoring / Control Plane 负边界 |
