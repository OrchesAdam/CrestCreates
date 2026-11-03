# Issue #159 实施计划 — 显式 unsupported / needs-clarification 终态

- 设计：[2026-11-03-explicit-unsupported-and-clarification-authoring-outcomes-design.md](../specs/2026-11-03-explicit-unsupported-and-clarification-authoring-outcomes-design.md)（rev.4，已修订设计审计 R1 四项 P2、R2-01 与 R3-01；D12/D14/D15/D16 已获裁定或按实测更正，待 S0 放行）
- 基线：`master` = `91d6574e`（PR #158 合并点，其父为 `9909365a` = PR #157）。#133 已合并并关闭，Asset B12 负例、`AssetUnsupportedIntentOracle`、`AssetAuthoringDiagnosticClassifier`、`ReviewerLiveOracleProbeTests` 均已在 master 上，S2 可直接在这些文件上继续，不需要等另一条分支。
- 分支：`docs/issue-159-explicit-unsupported-clarification-contract-20261103-134406`（设计与日志；实现切片在同分支续推，或按维护者裁定另开 `feat/issue-159-*`）。
- 验证口径：CI 暂停，推送前本地全绿；支持声明只写已执行证据。

## 切片

| 切片 | 内容 | 关键文件 | 退出条件 |
| --- | --- | --- | --- |
| S0 设计 | 本 spec + 本计划 + Issue #159 + R1/R2/R3 修订 | `docs/superpowers/specs、plans` | 静态审查通过（D1–D16；D12/D14/D15/D16 已裁定或按实测更正，余下待确认） |
| S1 契约与解析 | wire `8a.v1`、`outcome` 判别式、**Stage A 原始信封事实**（重复成员/未知成员/存在性/Kind，逐对象层，递归进 `payload`）、**Stage B 严格 DTO**（`AllowDuplicateProperties=false` **且** `PropertyNameCaseInsensitive=false`，spec D16）、封闭码表、归一化与边界、`DescriptorAuthoringStatus` + 两成员、结果新增两载荷类型、诊断码两枚、两个 source-generated JSON context 的**必需根**、提示披露与 `PromptTemplateVersion` v3（含「只发声明字段、禁止重复、拒绝时整段省略」）、全部 `7g.v1` 字面量迁移 | `Authoring.Abstractions/Authoring/*`、`Authoring/Parsing/*`（含新增内部 `DescriptorAuthoringEnvelopeFacts.cs`）、`Authoring/Prompting/*`、`Authoring/Json/*`、Authoring/Prompting/ControlPlane/Samples 测试 | **前置**：先对现有录制件做字段清点（有业务意义的新字段进正式设计；无关说明字段移出录制信封；禁止临时 allowlist 迁就旧录制件）。单测矩阵（spec §10.1 全 23 行）绿；变异 **(a)(b)(c)(d)(e)(f1)(f2)** 各按**所宣称机制**打红目标用例——(b) 依 R3 裁定从 S2 前移到本片（presence 是 Stage A 的本职，(f1)/(f2) 不能替代），靶例复用 `items:null`、`plan:{}` 两行 + 字段缺席的合法 control，不新增编号；(f1) 的别名样本必须固定 Stage B 大小写选项且除靶事实外全部数据合法，回证写实际的 `Status + 诊断码 + 草稿数`；(f2) 用「重复 + 单一错 hash」的**结果类别转换**记账；(g) 只作为「同时移除 Stage A 并放宽 Stage B」的联合负控记录，不得当作 Stage A 的独立证据；`ReflectionFallback` 类检查仍成立；已作废变异（删可达子类型注册 / 只删 Stage A 指望后值复活 / 把 payload 内重复当 Stage A 独占可见）不再出现在退出条件里 |
| S2 消费者与真实链路 | 样例 runner/fake agent 分支补全；Asset 真实链路：`unsupported` / `needsClarification` 两例（零草稿/零评审/零保存/gate 0/目录不变）+「散文拒绝仍不是澄清」+「不支持意图得到 drafts 仍是拒绝」负例；`AssetUnsupportedIntentOracle` 接受标签扩展（拒绝清单不缩）；`AssetAuthoringDiagnosticClassifier` 归类；B12 live 入口按新终态裁定 | `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/*`、Asset E2E（master `91d6574e` 起） | 全量 Asset E2E 绿（需本地 PG）；oracle 仍拒绝一切非意图归因结果。本片的 presence 断言是真实链路的零草稿/零保存/零 gate 事实，**不再承担**首次证明原始 presence 规则有效的责任（变异 (b) 已前移 S1） |
| S3 原生门禁 | AotFixture 新场景 `AgentAuthoringClarificationOutcome` / `AgentAuthoringUnsupportedOutcome` / `AgentAuthoringOutcomeShapeConflict`（拒绝的是**出现**的禁用成员：`null`/`{}`/`[]`/有内容；缺席是合法对照，也是 presence 的既有覆盖，不另开第三场景）/ **`AgentAuthoringCodeTableRejection`**（unknown reason code 与 unknown reasonCode 各一）/ `AgentAuthoringEnvelopeUnknownMember`（合法 drafts 样本仅加 item 层 `extra` ⇒ `InvalidProviderOutput`/unknown member、草稿 0；同次运行内无 `extra` 的 control 仍产出草稿）/ `AgentAuthoringEnvelopeDuplicatePrecedence`（**`payload` 子对象内重复键 + 单一错 hash** ⇒ `InvalidProviderOutput`/duplicate member、草稿 0，且不得退化为 `Blocked`/hash mismatch）；扩展 `AgentAuthoringWireDisclosure`（断言 `8a.v1`）；**同片修改 harness**：逐场景点名 sentinel 校验 + 失败控制独立运行（当前 harness 只发布并跑一次绿色产物、要求 exit 0 + 总 sentinel，不改就可能漏测仍收口） | `.../CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/Program.cs` + `.../AotFixture.Tests` | `PublishAot` + 原生 run 在 linux-x64 通过；两层分开：正确拒绝场景=绿色（exit 0 + 自身 sentinel），门禁负控=非零且无总 sentinel；码表越界场景与两个 Stage A 场景**各有独立 sentinel 名**并被 harness 点名核对。原生声明只覆盖实际跑过的样本，不外推整张 23 行矩阵，也不推导认证/审批链 |
| S4 文档与状态 | `usage-guide.md` 契约样例、`CapabilityMap.md` 四维、`memory.md`、`AGENTS.md` 当前平台状态 + AOT 分层、§5.1 B12 改判、#123 证据矩阵新场景行、7g spec 附录、本日志收口 | `docs/**`、`memory.md`、`AGENTS.md` | 声明与证据一一对应；未执行的场景写「未验证」；S5 未执行时该验收项保持 pending 且不关闭 Issue |
| S5 真实运行（**必需验收步骤**，非可选） | 带凭据 DeepSeek 跑 B12 不支持意图一次（新提示 + `8a.v1`），artifact 落 `/tmp` 不入库，按 spec §10.4/§10.7 判定 | 仅一次执行记录 | 覆盖判定只接受：`UnsupportedIntent`/`NeedsClarification` + 合法码 + 非空内容 + 零草稿/零评审/零保存/gate 0/目录不变。散文拒绝、无效输出、provider/预算失败、不支持意图上的 drafts 全部保持未达成；无凭据则该项 pending，不得自动勾选，也不得以单元/原生证据替代收口 |

## 每切片验证命令

```bash
# S1
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests
dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests

# S2（PG 由本地容器提供）
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock  # 容器起法见 #133 日志
ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING='Host=127.0.0.1;Port=5433;...' \
  dotnet test samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.E2E.Tests

# S3
dotnet publish tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture \
  -c Release -r linux-x64 --self-contained true -p:CrestCreatesPublishMode=aot
# 绿色运行：逐场景点名核对 sentinel（AgentAuthoringCodeTableRejection、AgentAuthoringEnvelopeUnknownMember、
#           AgentAuthoringEnvelopeDuplicatePrecedence 各有独立名）并要求总 sentinel 与 exit 0
# 负控运行：每个失败控制单独执行，要求非零退出且【没有】总 sentinel；两者不得混在同一次运行里判定

# S5（凭据只从环境取用，不打印、不入库；非交互 shell 需按 #133 日志的定向取值方式）
CREST_ASSET_LIVE_EVAL=1 CREST_ASSET_LIVE_EVAL_MAX_OUTPUT_TOKENS=16384 \
  dotnet test samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.E2E.Tests \
  --filter DeepSeek_UnsupportedIntent_YieldsRestrictedResult_AndFabricatesNoCapability

# 每片收尾的全量回归
dotnet build && dotnet test
```

## 已识别风险

| 风险 | 处置 |
| --- | --- |
| `contractVersion` 参与 prompt 输入哈希，版本升级会改变所有派生哈希；某些 fixture 可能钉了字面哈希 | S1 第一步先跑 `PromptInputHashTests`，逐个把字面量改为「同源重算」而不是放宽断言；禁止用跳过或宽松比较绕过 |
| Stage A 的「未知成员/别名一律拒绝」比 `7g.v1` 更严，可能打红既有录制件里多写的说明性字段 | S1 先跑一次「录制件字段清点」把多余成员列出来，逐条改录制件本身（不是放宽解析）；披露文本同步说明只允许声明字段 |
| 「删子类型 `[JsonSerializable]`」这类变异看似证明闭包，实际全绿（可达类型由根自动物化，已实测） | 该变异在 spec §10.6 明确作废；判别力只来自切断必需根或字段名漂移（e）、移除词表/别名规则（f1）、以及「重复 + 单一错 hash」的结果类别转换（f2） |
| 「严格 Stage B 看不到 payload 内的重复键」这句 rev.3 机制结论是错的（原样重放：严格分支对 `payload` 顶层与嵌套 `target.id` 都抛 `JsonException`），若照它设计靶例会把归因写成事实 | rev.4 已更正：payload 重复的 Stage A 价值是**优先级与固定诊断类别**，不是独占可见；独占证据仅剩未知成员/别名、presence vs null、raw-before-hash 的结果类别转换（spec §4.4/§6/§10.6/D14/D15） |
| 把别名绑定归因于「命名策略/源生成」也是错的：`PropertyNameCaseInsensitive=false` 且命名策略不变时别名不再绑定 | Stage B 固定该选项（D16），别名变为「不绑定因而被忽略」，拒绝仍由 Stage A step 3 负责；(f1) 的别名样本必须固定此选项并保证其余数据合法，否则变异回证不可判别 |
| AotFixture harness 目前只跑一次绿色产物，缺独立失败控制协议 | 列为 S3 必做交付物（不是假设）；两层分开判定，避免把正常拒绝输入和门禁负控混成同一退出码 |
| 版本单值化（不留 `7g.v1` 兼容）会让未迁移的外部录制响应全部失败 | 本仓库内全部生产者/录制件在 S1 一并迁移；外部调用方不存在（`AuthorAsync` 无生产调用方，见 spec §2）；历史 review/plan 文档保留原文，不声称它们仍可回放 |
| 提示改动影响真实模型行为，可能仍不按新契约答题 | S5 明确允许「未达成」结论入库；不放宽判定，不把 `NoItems` 猜成澄清；无凭据时该验收项 pending，不得自动勾选 |
| ~~PR #158 与本计划共用 Asset 文件，存在合并顺序冲突~~ 已消解：#158 于 2026-11-03 合并为 `91d6574e`，本分支已 rebase 到该 master，Asset/oracle 只有一份真相 | S2 直接在 master 版本上改 oracle，禁止另起第二份判定器 |
| 新终态漏出必需 JSON 根会导致原生路径反射失败（而非红测试） | S3 断言**必需根**可解析 + 保留 `ReflectionFallback_IsDisabled` 总门；冗余 annotation 不作为运行时前置 |

## 设计审计 R1 修订记录（2026-11-03）

| 发现 | 落地 |
| --- | --- |
| R1-01 信封校验不完整 | spec §4.1 改成「存在性/Kind」语义 + §4.4 新增原始信封事实层（实测机制表：`JsonDocument` 保留重复成员与 `ValueKind.Null`；默认 `AllowDuplicateProperties=true` 后值覆盖前值；`false` 在源生成路径抛 `JsonException`）+ §6 顺序重写（重复/未知成员先于版本与 hash）+ §10.1 十六行矩阵把审计的五种形态与 null/空/重复组合全部变成 expected-red 用例 |
| R1-02 JSON 闭包变异无判别力 | spec §5 改写闭包口径（根权威、可达即物化、detached 才需显式注册）+ §10.2 断言改为「必需根可解析 + 字段名往返 + 反射禁用」+ §10.6 作废原 (e) 并新增切断必需根/字段名漂移 (e) 与移除 Stage A 的 (f)（注：该 (f) 判据被 R2-01 推翻，见下方 R2 修订记录）；计划风险表记入「看似证明实为全绿」的教训 |
| R1-03 原生验收漏码表越界 | spec §10.5 新增 `AgentAuthoringCodeTableRejection`（两种 unknown 码各一）并分离「正确拒绝=绿色」与「门禁负控=非零无总 sentinel」两层；spec §9/计划 S3 把 harness 改动列为必做交付物与退出条件 |
| R1-04 live 口径不一致 | S5 由「可选门槛」改为必需验收步骤（计划切片表 + spec §12 新条）；spec §10.7 写清成功判定四要件与四类保持未达成的输出，并明确「无凭据 → pending，不得自动勾选、不得以单元/原生证据收口」；§10.4 补「不支持意图得到 drafts 仍是拒绝」 |
| 文字收口两条 | §4.3 增加归一化定义与非空要求（审计探针记的 `"question":"\t\n"`、`missingInputs:[" "]` 现被拒）+ 重复条目相等语义；§12 明确旧版本拒绝 fixture 例外，禁止为满足 grep 删负例或拼字符串 |
| D1–D10 审计建议 | 全部采纳并在 §13 表格逐项标注落地位置；另新增 D11（两阶段解析）、D12（未知成员拒绝）、D13（闭包证据口径） |

## 日志约定

实施期在本文件追加 `I-01…` 逐条记录：事实（含文件:行）、决策与偏离、当次真实命令尾部输出（计数不是形容词）、变异判别力证据（红→绿）、未做的事。审查轮（R\*）按维护者节奏另起小节，不覆写历史。

## 设计审计 R2 修订记录（2026-11-03）

| 项 | 落地 |
| --- | --- |
| R2-01 [P2] (f) 判据不成立 | 我自己复现了严格 Stage B 的行为（反射关闭、仅源生成 context，7 输入 × 宽松/严格）：重复 `promptInputHash`/`outcome` 与 `payload` **之外**的重复 `id` 都抛 `JsonException` ⇒ 删 Stage A 不会恢复后值接受；同时 `payload` 以原始 `JsonElement` 透传（内部重复与 `customField` 原样保留）、item 层未知字段两种配置都静默通过、命名策略下 `"Unsupported"` 别名仍被绑定。spec §10.6 因此改为 (f1) 移除未知成员/大小写别名规则、(f2) 只移除原始重复规则并用「重复 outcome + 单一错 hash」锁定优先级，(g) 标为联合负控；§12 明确禁止把「诊断来源变了」当成功能判别。**（后一半机制描述被 R3-01 推翻：严格 Stage B 对 `payload` 内重复同样抛异常，别名绑定来自 `PropertyNameCaseInsensitive` 而非命名策略。rev.3 的那次修订写进了 spec，rev.4 才更正；见下方 R3 修订记录。）** |
| payload 裁定（接受，限定范围） | spec §4.4 规则 6 + 词表段落：内部名字豁免、**重复键不豁免**（Stage A 递归进 payload 及其子对象）、item 层 `payload` 本身仍须是 object、不宣称未知字段会被 materialization 保留或生效 |
| D12 裁定（接受，附迁移条件） | spec §13 D12 改写为已裁定；plan S1 前置「录制件字段清点」并禁止 allowlist 漂移（本节第一行退出条件） |
| (c) 判据补强 | spec §10.1 增加「单一错 hash + 未知 outcome」行；§10.6 (c) 指明只有这一组合能区分先后，普通错 hash 行不能 |
| 文字同步两条 | §10.5 把 forbidden 的「absent」改正为合法对照、只拒绝出现的成员；§5 的 `JsonDocument` 说明去掉「trim/AOT-safe」式表述，改为「不依赖反射；原生执行由 S3 验证，本设计不预支 NativeAOT 声明」 |
| PR body 过期 | 本 PR body 同步到 rev.3：基线 `91d6574e`、S5 为必需验收、D1–D15、变异集合 (a)–(g) 与两条已作废变异 |

## 设计审计 R3 修订记录（2026-11-03）

| 项 | 落地 |
| --- | --- |
| R3-01 [P2] payload 严格反序列化的事实写错 | 维护者原样重放我的探针（三文件 hash 一致，未改源码），实测严格 Stage B 对 `payload` 顶层 `id` 与嵌套 `target.id` 的重复键**同样抛 `JsonException`**——我 rev.3 写的「透传/只拒绝 payload 外重复」与我自己那次运行日志矛盾。spec §4.4 表格重写（lenient 保留原文 vs strict 在返回 `JsonElement` 前抛出）、§4.4 规则 2/6、词表段落、§6 step 2 注释与优先级段落、§10.1 两行 payload 靶例、§10.6 (g) 后的作废说明、D14/D15 全部按实测更正；重复检查仍递归进 `payload`，但理由改为**固定优先级 + 固定诊断类别** |
| 大小写因果 | 对照实验只改 `PropertyNameCaseInsensitive`（命名策略、DTO、输入不变）：Web defaults 下 `"Unsupported"` 绑定，false 下不绑定。spec §4.4 表格「name casing」行、规则 1、§6 step 3、§9 选项行、D16 与风险表按此重写：Stage B 必须钉死该选项，别名变为「不绑定→被忽略」，拒绝仍由 Stage A 负责；(f1) 的别名样本必须固定该选项且其余数据全合法 |
| 待定一：(f1)/(f2)/presence | 裁定落地：(f1) 证词表、(f2) 证重复优先级，各自成立；presence 用**既有 (b)** 前移到 S1 退出条件，靶例复用 `items:null`、`plan:{}` + 字段缺席 control，不新增变异编号；S2 退出条件改真实链路事实断言。作废清单补第三条：把 payload 内重复当独占可见证据 |
| 待定二：S3 最低原生范围 | 保留「一次词表 + 一次重复优先级」两个点名场景：`AgentAuthoringEnvelopeUnknownMember`（加 `extra` 拒绝 + 不加 `extra` 的 control 同运行内仍产草稿）与 `AgentAuthoringEnvelopeDuplicatePrecedence`（**payload 子对象重复 + 单一错 hash**，必须不得退化为 `Blocked`/hash mismatch）；存在性由既有 `AgentAuthoringOutcomeShapeConflict` 覆盖，不新增第三场景；各有独立 sentinel，harness 点名核对，负控另跑 |
| 记录纪律 | 本行是我自己犯的错：探针日志里已打印 `duplicate id`，我在把观测转写成机制结论时按印象外推成「payload 内看不见」。今后写探针结论必须逐行对照当次输出，且明确区分「谁拒绝了」与「谁先拒绝」 |
| PR body | 本 PR body 同步到 rev.4：机制表两行更正、Stage B 选项成对钉死、变异集合含 (b) 前移 S1、S3 两个 Stage A 场景与 sentinel 命名 |

教训记两条，避免以后再犯：**「下游层也会拒绝同一输入」不能替代「上游层确实运行」**；同一条输入被两层都拒绝时，能记账的只有**结果类别转换**（状态 + 诊断码 + 草稿数的变化），不是消息文本或「确实红了」。判别变异因此只能打在存在性 vs null、词表/别名、以及 raw-before-hash 的类别转换上。
