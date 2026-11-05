# Issue #159 实施计划 — 显式 unsupported / needs-clarification 终态

- 设计：[2026-11-03-explicit-unsupported-and-clarification-authoring-outcomes-design.md](../specs/2026-11-03-explicit-unsupported-and-clarification-authoring-outcomes-design.md)（rev.8；设计审计 R1–R4 全部关闭，**S0 已于 R4 放行、D1–D16 逐条确认**。S1 已实施、按实施审计 R1 补齐解码边界并于 R2 放行。S2 已实施并按实施审计 R1 把「历史归因」与「新契约覆盖」拆成两层判定、由 R2 放行。S3 已实施；R1 补上原生往返的读回状态断言，R2 再把「注入落地」与「目标拒绝」分成两套判据（设置失败走独立 SETUP-FAIL 通道，输入篡改类必须先出落地见证），并在 SDK 10.0.114 / ILCompiler 10.0.14 下重发门禁 **11/11**，同时解掉全量 build 的 NU1903 阻断（系统 rpm 侧仍待维护者升级），见「S3 实施日志」与 R1/R2 修复记录，S3 已实施并由 R3 放行（R1 补读回状态断言、R2 把「注入落地」与「目标拒绝」分成两套判据；原生门禁 linux-x64 11/11）。S4 已完成文档与状态同步——`usage-guide.md`、`CapabilityMap.md` §8、`memory.md`、`AGENTS.md`、§5.2 B12 改判、#123 证据矩阵新行与 7g spec 附录指针，见「S4 实施日志」。实施审计 S4 R1 发现 1 项 P2（指南把 `Blocked` 与成功态一起写成「草稿 ≥1」）与 1 项 P3（7g 附录把历史写成「缺少 `items` 时推断 drafts」），两项均已修订，见「S4 实施审计 R1 修复记录」，S4 已交付、按 R1 修订两项文档表述并由 R2 放行。S5 按已获准的单次范围执行完毕：真实 provider 对 B12 不支持意图答 `unsupported`，解析为 `UnsupportedIntent`（3 条合法条目 + 逐条 `AUTHORING_INTENT_UNSUPPORTED`），oracle 判 `ExplicitUnsupportedResult`，零草稿/零评审通过/零保存/零 gate、部署清单不变、未伪造能力，**B12 达成本契约覆盖**；边界为单次运行、单一意图类与模型画像，`NeedsClarification` 的 live 未执行。PR #160 与 Issue #159 保持 open 待复审裁定，不据此合并或关闭）
- 基线：`master` = `91d6574e`（PR #158 合并点，其父为 `9909365a` = PR #157）。#133 已合并并关闭，Asset B12 负例、`AssetUnsupportedIntentOracle`、`AssetAuthoringDiagnosticClassifier`、`ReviewerLiveOracleProbeTests` 均已在 master 上，S2 可直接在这些文件上继续，不需要等另一条分支。
- 分支：`docs/issue-159-explicit-unsupported-clarification-contract-20261103-134406`（设计与日志；实现切片在同分支续推，或按维护者裁定另开 `feat/issue-159-*`）。
- 验证口径：CI 暂停，推送前本地全绿；支持声明只写已执行证据。

## 切片

| 切片 | 内容 | 关键文件 | 退出条件 |
| --- | --- | --- | --- |
| S0 设计 | 本 spec + 本计划 + Issue #159 + R1/R2/R3 修订 | `docs/superpowers/specs、plans` | 静态审查通过（D1–D16；D12/D14/D15/D16 已裁定或按实测更正，余下待确认） |
| S1 契约与解析 | wire `8a.v1`、`outcome` 判别式、**Stage A 原始信封事实**（重复成员/未知成员/存在性/Kind，逐对象层，递归进 `payload`）、**Stage B 严格 DTO**（`AllowDuplicateProperties=false` **且** `PropertyNameCaseInsensitive=false`，spec D16）、封闭码表、归一化与边界、`DescriptorAuthoringStatus` + 两成员、结果新增两载荷类型、诊断码两枚、两个 source-generated JSON context 的**必需根**、提示披露与 `PromptTemplateVersion` v3（含「只发声明字段、禁止重复、拒绝时整段省略」）、全部 `7g.v1` 字面量迁移 | `Authoring.Abstractions/Authoring/*`、`Authoring/Parsing/*`（含新增内部 `DescriptorAuthoringEnvelopeFacts.cs`）、`Authoring/Prompting/*`、`Authoring/Json/*`、Authoring/Prompting/ControlPlane/Samples 测试 | **前置**：先对现有录制件做字段清点（有业务意义的新字段进正式设计；无关说明字段移出录制信封；禁止临时 allowlist 迁就旧录制件）。单测矩阵（spec §10.1 全 23 行）绿；变异 **(a)(b)(c)(d)(e)(f1)(f2)** 各按**所宣称机制**打红目标用例——(b) 依 R3 裁定从 S2 前移到本片（presence 是 Stage A 的本职，(f1)/(f2) 不能替代），靶例复用 `items:null`、`plan:{}` 两行 + 字段缺席的合法 control，不新增编号；(f1) 的别名样本必须固定 Stage B 大小写选项且除靶事实外全部数据合法，回证写实际的 `Status + 诊断码 + 草稿数`；(f2) 用「重复 + 单一错 hash」的**结果类别转换**记账；(g) 只作为「同时移除 Stage A 并放宽 Stage B」的联合负控记录，不得当作 Stage A 的独立证据；`ReflectionFallback` 类检查仍成立；已作废变异（删可达子类型注册 / 只删 Stage A 指望后值复活 / 把 payload 内重复当 Stage A 独占可见）不再出现在退出条件里。**实施审计 R1 追加退出条件**：协议级成员名、`payload` 递归成员名、三个信封值的解码各自必须有异常边界（M1/M2/M3 单守卫变异分别只打红本入口的靶例，presence/词表/Stage B/合法代理项对照在任何变异下保持绿），且正式调用方 `AuthorAsync` 收到分类结果而非异常 |
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

## 设计审计 R4 与 S0 放行（2026-11-03）

- 复审 `5def23e8`（rev.4）：**S0 放行，D1–D16 逐条确认，无新增可操作发现**；R1 四项、R2-01、R3-01 全部关闭。裁定：可进入 S1，先完成录制件字段清点。
- 放行只批准设计与验收方案，不含任何 `8a.v1` 执行事实；合并与关 Issue 仍由维护者操作。
- 实施切片在本分支续推（计划里的「另开 `feat/issue-159-*`」备选未启用，避免 Asset/oracle 出现第二份真相）。

## S1 实施日志

### I-01 录制件字段清点（D12 前置条件，先于任何解析改动）

清点方法：以 `promptInputHash`（authoring 信封独有成员，而非其他契约复用的 `contractVersion`）为锚，逐文件核出全部 provider answer 生产者，再按对象层列出成员名。

| 生产者 | 形态 | 信封层成员 | 判定 |
| --- | --- | --- | --- |
| `Authoring.Tests/OutputParserTests.cs`（9 个 builder） | `JsonSerializer.Serialize(new {…})` 匿名对象 | root：`contractVersion`/`promptInputHash`/`plan`/`items`；plan：`planId`/`intentText`/`assumptions`/`plannedDescriptorRefs`；ref：`namespace`/`id`/`version`；item：`descriptorKind`/`descriptorId`/`operation`/`rationale`/`payload`/`assumptions`/`memoryRefs` | 无越表字段；匿名对象不可能产生重复键 |
| `Authoring.Tests/UpdateBaseVersionParserTests.cs`、`LlmDescriptorAuthoringAgentTests.cs`、`ModelRequestProvenanceActivationProbeFixture.cs` | 同上 | 上述集合 + item `baseVersion` | 无越表字段 |
| `Authoring.Tests/Fixtures/CompanyCertificationLlmFixture.cs` | StringBuilder 逐行拼接（手写 JSON） | 与词表一致；`payload` 内含 `permissions`/`interaction`/`inputSchema`/`outputSchema`/`assigneeStrategy`/`outcomes`/`steps`(`target.kind`/`capability`/`humanTask`/`condition`/`transitions`) | 信封层无越表字段；多余名字全部落在 `payload` 内（D14 豁免域） |
| `Samples.Tests/CompanyCertificationAuthoringGoldenScenarioTests.cs:536` | `$$"""` 手写 raw 字面量 | 同上 | 同上 |
| `ControlPlane.JsonContracts.AotFixture/Program.cs:358,450` | 手写字符串拼接 | `optionalSchemaProperties`/`baseVersionProperty` 只注入 `payload.inputSchema/outputSchema` 与 item 层 `baseVersion` | 注入点不引入越表名 |
| Asset E2E：`AssetUnsupportedIntentNegativeCaseTests`（5 个）、`AssetApprovedInventoryHandoffAcceptanceTests`（2 个）、`AssetProposalReplayAcceptanceTests`、`AssetLiveProposalRetention`、`ControlPlane.Tests/AssetHumanApprovalActivationHandoffAcceptanceTests` | 匿名对象 + `JsonNode` 改写既有成员（`rationale`、`payload.name`） | 与词表一致 | 无新增名、无重复键 |
| `ReviewerLiveOracleProbeTests.cs:96` | 匿名对象 | item 缺 `payload`（`Capability` + `Create`） | **唯一需要改的录制件**：8a 的 item `payload` 是 step 8 必填成员，故补 `payload:{id,name,version}`，保留该探针原本的 Blocked/`UnknownDescriptorKind` 归因，不让它因形状缺失改判 |

结论：**0 个信封层未知成员、0 个重复成员**；真正需要全量迁移的是 `outcome` 判别式（18 个文件、所有既有录制件都缺它）与版本字面量。未引入任何 allowlist，未放宽解析。live artifact 在 `/tmp`（仓库外），本就是一次真实答题，不参与迁移。

### I-02 契约与解析实现（事实，含位置）

- 状态：`DescriptorAuthoringStatus.cs:12,15` 追加 `UnsupportedIntent = 6`、`NeedsClarification = 7`（既有 0–5 编号不变，D1）。
- 载荷：新文件 `Authoring.Abstractions/Authoring/DescriptorAuthoringUnsupportedOutcome.cs`、`DescriptorAuthoringClarificationOutcome.cs`（含两张封闭码表 `…ReasonCodes.All` + `IsKnown`，ordinal 比较）；`DescriptorAuthoringResult.cs:16-20` 增 `Unsupported`/`Clarification`，`Snapshot()` 深拷贝两级列表。
- 诊断码：`DescriptorAuthoringDiagnosticCodes.cs:44-49` 增 `AUTHORING_INTENT_UNSUPPORTED` / `AUTHORING_INTENT_NEEDS_CLARIFICATION`，并新增 `AllValues`（14 枚，供闭包断言）。
- Stage A：新文件 `Authoring/Parsing/DescriptorAuthoringEnvelopeFacts.cs` —— 声明词表（root/plan/ref/item/unsupported/reasons/needsClarification/questions）、按对象路径收集 `name → ValueKind`、重复成员与未知/别名成员拒绝、递归进 `payload` 及其子对象做重复扫描（词表在 `payload` 停止）。内部类型，无公共钩子（D9/D11）。
- Stage B：`JsonDescriptorAuthoringOutputParser.cs:22-30` 的 context 选项钉死 `AllowDuplicateProperties=false` + `PropertyNameCaseInsensitive=false`，并保留 `DefaultIgnoreCondition=WhenWritingNull`（否则披露示例会写出 `"items":null`，与规则自相矛盾——已由 `WireDisclosureTests` 的 `NotContain` 断言锁定）。
- 顺序：§6 步骤 1–13 逐条实现（`Parse` 内注释编号）；终态占位 `plan_intent_unsupported` / `draftset_intent_unsupported` / `plan_needs_clarification` / `draftset_needs_clarification` 与零草稿。
- 提示：`DescriptorAuthoringWireDisclosure.cs` 披露 `8a.v1` 判别式、三形状、严格性三句（只发声明名 / 禁止重复 / 拒绝时整段省略）、两张码表、`payload` 界线，并把两终态示例改为经同一 parser context 序列化；`DefaultDescriptorAuthoringPromptBuilder.cs:16-25,89-93` 系统提示与收尾指令同步；`LlmDescriptorAuthoringAgentOptions.cs:11-12` → 模板 `descriptor-authoring-prompt-template-v3` + `8a.v1`；`DefaultDescriptorAuthoringPromptInputFactory.cs:20` → `8a.v1`。
- 迁移：src/tests/samples 的 `7g.v1` 字面量已全部改写；唯一保留的旧版本字面量是 `OutputParserEnvelopeMatrixTests.Row09_…`（旧版本拒绝 fixture，§12 按名豁免）。
- 一处生产可见性变更：`CrestCreates.Agent.Authoring.csproj` 增加 `<InternalsVisibleTo Include="CrestCreates.Agent.Authoring.Tests" />`，用于断言钉死的 options 与生成根；仓库既有约定（Organization/Metadata/Mcp.Memory 等同款），不是新的公共扩展点。

### I-03 执行结果（当次真实计数，非形容词）

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests` | **125 通过 / 0 失败 / 0 跳过**（原 76 + 新增 49：§10.1 矩阵 25 行用例 + 终态/闭包/选项/码表断言） |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests` | 21 通过 / 0 失败 |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests` | 575 通过 / 0 失败 |
| `dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests` | 34 通过 / 0 失败 |
| `dotnet test samples/…E2E.Tests --filter AssetUnsupportedIntent\|ReviewerLiveOracleProbe\|AssetAuthoringDiagnosticClassifier` | 38 通过 / 0 失败（离线 oracle 组；本机无 PG，全量 Asset E2E 属 S2，未执行） |
| `dotnet build`（全解决方案，restore 后） | 成功，0 错误 |

未执行：Asset 全量 E2E（需 PG）、原生 publish/link/run、带凭据 live。

### I-04 变异判别证据（S1 退出条件；每条 = 红 → 恢复绿，日志在 `/tmp/crest-issue159-s1/mutations/`）

判别记录一律写实际 `Status + 诊断码 + 草稿数`，不写「红了」。

| 变异 | 改了什么 | 红的用例（仅靶例） | 前 → 后（状态 / 草稿数） |
| --- | --- | --- | --- |
| (a) 绕过 `outcome` 判别式，缺 `items` 视为 drafts | `outcome` 为 null 时默认 `drafts` | 1 个：`Control_MissingOutcome` | `InvalidProviderOutput` → **`Succeeded`**（1 草稿） |
| (b) presence 降为 null/空判定 | `EnvelopeMemberInventory.Has` 改为真值判断 | 5 个：Row02、Row03、Row04、Row04b、Row21c | Row02/Row03 → **`UnsupportedIntent`**；Row04 → **`Succeeded`**；Row04b → **`NeedsClarification`**；Row21c 归因文本改变（共享 `Has`） |
| (b) 反向确认 | 同上 | Row03b（`plan` 带内容）**保持绿** | null/空实现分不清「成员出现」与「有内容」，正是该靶例要证明的界线 |
| (c) hash 检查移到 outcome 检查之后 | 交换 step 6 与 step 7/8 | 1 个：Row19 | `Blocked`+`PromptHashMismatch` → **`InvalidProviderOutput`**（未知 outcome 抢先） |
| (d) 封闭码表改宽松字符串 | 删两处 `IsKnown` 守卫 | 2 个：Row20、Row20b | `InvalidProviderOutput` → **`UnsupportedIntent`** / **`NeedsClarification`**（0 草稿） |
| (e) 声明名漂移 | DTO `[JsonPropertyName("outcome")]` → `"Outcome"` | 2 个：`AliasMembers_…`、`WireDisclosure…Examples…` | 示例解析红 + 别名断言显示 `dto.Outcome = "unsupported"`（必需字段名是真实闭包边） |
| (e2) 切断 wire root 注册 | 删 `[JsonSerializable(typeof(DescriptorAuthoringProviderOutputDto))]` | —— 生产侧 **编译失败** CS1061 ×2（parser 与 disclosure 两个调用点） | 该边无法「静默变绿」：断了就构建不过 |
| (f1) 只删 Stage A 未知成员/别名规则 | 删 `declared.Contains` 分支 | 5 个：Row12、Row12b、Row13、Row14、`AliasMembers_…` | Row12/Row12b/Row13 → **`Succeeded`**（1 草稿）；Row14 → **`NeedsClarification`**（0 草稿）。全部是终态转换，非消息路由 |
| (f2) 只删 Stage A 重复成员规则 | 删 `kinds.ContainsKey` 与 payload `seen.Add` 两处 | 8 个：Row06、Row06b、Row07、Row08、Row10、Row15、Row16、Row18 | **功能类别转换**：Row16、Row18 → **`Blocked`**+hash mismatch（严格 Stage B 在 step 9 才抛，step 6 先定案）；Row06b → `Blocked`。其余 5 行状态仍 `InvalidProviderOutput`，只是诊断来源改为 Stage B 反序列化异常 → 按 R2-01/R3 口径记为「归因红」，不记作 Stage A 的独立证据 |
| (g) 联合负控 | (f2) + `AllowDuplicateProperties = true` | 9 个：上述 8 行 + `ParserContext_ResolvesTheEnvelopeRoot…` | Row06、Row08、Row10、Row15 → **`Succeeded`**（后值接受恢复，含 `payload` 内重复键也被透传）；Row06b/Row16/Row18 → `Blocked`。**只有这个组合**才恢复后值读取，故它只是联合负控，不作 Stage A 独立证据 |

每条变异执行后 `git checkout -- ` 还原源文件，最终 Authoring 套件复跑 **125/125** 绿。

### I-05 S1 未做的事（不冒充）

- 未新增原生场景、未执行 `PublishAot`，未修改 AotFixture 的场景集合与 harness（S3）。
- 未跑 Asset 全量 E2E（无本地 PG），未改 `AssetUnsupportedIntentOracle` 的接受标签与 gap-lock 改判（S2）。
- 未做带凭据 live（S5），未更新 `CapabilityMap.md` / `memory.md` / `AGENTS.md` / `usage-guide.md` 的能力声明（S4）。
- 口径差一处：`unsupported`/`needsClarification` 段与其内 `reasons`/`questions` 数组的「必填 + Kind」确实在 §6 step 8（`Row05b` 打在此处）；**条目内部**的 `code`/`message`/`id`/`reasonCode`/`question` 与数组 Kind 在终态构建里判定，状态与诊断码与 §6 相同，只是位置晚于 hash 检查。矩阵靶例不依赖这一先后，设计文字未改。

## S1 实施审计 R1 修复记录（2026-11-04，S1-R1-01 单项 P2）

审查 head `14bf51fb`，报告 `docs/review/2026-11-04-issue-159-s1-review-round1.md`（审查者工作区副本）。S0 与 D1–D16 放行保持，S1 暂不放行，先补原始字符串解码的异常边界。

### I-06 缺陷事实与红基线

`JsonDocument.Parse` 接受未配对的 surrogate 转义（`"\uD800"`），失败发生在**解码时**：`JsonProperty.Name` 或 `JsonElement.GetString()` 抛 `InvalidOperationException`（`Cannot read incomplete UTF-16 JSON text as string with missing low surrogate`），既不在 step 1 的 `JsonException` catch 里，也不在 step 9 的 catch 里。

红基线在未修改 head `14bf51fb` 上执行（`dotnet test …Authoring.Tests --filter OutputParserRawStringDecodeTests|AuthorAsync_UndecodableRawStringMember`）：**9 失败 / 5 通过（14）**，9 条失败原因全部是上述同一异常，无旁因：

| 入口 | 失败靶例 |
| --- | --- |
| step 4 `contractVersion` 值 | `RawDecode_ContractVersionValue_IsClassifiedNotThrown` |
| step 5 `promptInputHash` 值 | `RawDecode_PromptInputHashValue_IsClassifiedNotThrown` |
| step 7 `outcome` 值 | `RawDecode_OutcomeValue_IsClassifiedNotThrown` |
| step 2–3 协议级成员名 | `RawDecode_RootMemberName_…`、`RawDecode_ItemMemberName_…`、`RawDecode_UndecodableNameWithWrongHash_…` |
| step 2–3 `payload` 递归成员名 | `RawDecode_PayloadMemberName_…`、`RawDecode_NestedPayloadMemberName_…` |
| 正式调用方 | `AuthorAsync_UndecodableRawStringMember_ReturnsInvalidProviderOutputToTheCaller` |

5 条绿色对照（修复前后都必须保持绿，用来钉住边界归属）：未知成员在文档顺序上早于坏名时仍以未知成员定案（`…KeepsTheVocabularyFactFirst`）；合法代理项对在 `unsupported.message` 与 `payload.name` 正常解码（两条 `LegalSurrogatePair…`）；`payload` 内**值**的同类输入由 step 13 既有 per-item 边界归为 `Blocked` + `AUTHORING_INVALID_PROVIDER_OUTPUT`（`…KeepsTheExistingPerItemBoundary`）；条目 `message` 的同类输入由严格 Stage B 归为反序列化失败（`…IsClassifiedByTheStrictProjection`）。

### I-07 修复（事实，含位置）

- `src/Runtime/Agent/CrestCreates.Agent.Authoring/Parsing/DescriptorAuthoringEnvelopeFacts.cs`：新增 `DecodeMemberName(JsonProperty)` / `DecodeStringValue(JsonElement)`（只捕获 `InvalidOperationException`，只包住解码调用本身，无全局 catch）与 `UndecodableMemberNameMessage(path)`；`TryCollectObject` 与 `TryCollectOpenContent` 的每个成员名先解码再参与词表与重复判断，名字不可解码时报 `Envelope contains a member name at {path} that cannot be decoded as UTF-16 text.`。同一 `foreach` 内保持文档顺序，因此坏名不会抢在更早的未知/重复事实之前。
- `…/Parsing/JsonDescriptorAuthoringOutputParser.cs`：`ReadString` 改为 `ReadString(target, name, out bool undecodable)`，「缺席 / 非字符串 / 不可解码」三种原因分开；step 4/5/7 各自在原有比较之前先报 `Provider output member '…' cannot be decoded as UTF-16 text.`。步骤顺序、严格 Stage B 两个选项、23 行矩阵与 §10.5/§10.6 集合均未改动——这是一条边界，不是一个新优先级（§12 禁止发明 Stage A 事实的口径保持）。
- 回归：新增 `tests/…/OutputParserRawStringDecodeTests.cs`（13 条）与 `LlmDescriptorAuthoringAgentTests.AuthorAsync_UndecodableRawStringMember_…`（1 条，证明正式调用方收到分类结果）。
- 设计同步：spec rev.5 —— §4.4 增解码机制行、新增 rule 7、§6 步骤 2–3/4/5/7 注记、Changelog。契约文字与码表未变。

### I-08 单守卫变异判别（每条 = 红 → 还原绿；源文件先物化到 `/tmp/issue159-s1r1-fixed/`，逐条 `cp` 还原）

| 变异 | 改动 | 红例 | 类别 |
| --- | --- | --- | --- |
| M1 | 协议级名字入口 `DecodeMemberName(property)` → `property.Name` | 3 个：Root 名、Item 名、坏名+错 hash | `InvalidProviderOutput` → 抛 `InvalidOperationException`（正式调用方收不到分类结果） |
| M2 | `payload` 递归名字入口同上 | 2 个：payload 名、payload 嵌套名 | 同上，且 M1 的 3 条保持绿 → 两处名字入口各自被锁 |
| M3 | `ReadString` 内 `DecodeStringValue(value)` → `value.GetString()` | 4 个：version、hash、outcome、agent 端到端 | 同上，且 5 条名字靶例保持绿 → 值入口独立成立 |

三条变异红例合计 9 = 红基线条数，对照 5 条在任何变异下都不红；还原后复跑全套 **139/139**。

### I-09 执行结果（本轮真实计数）

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests` | **139 通过 / 0 失败**（S1 的 125 + 14 条新回归） |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests` | 21/21 |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests` | 575/575 |
| `dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests` | 34/34 |
| `dotnet test samples/…E2E.Tests --filter AssetUnsupportedIntent\|ReviewerLiveOracleProbe\|AssetAuthoringDiagnosticClassifier` | 38/38（离线 oracle 组） |
| `dotnet build`（全解决方案） | 0 错误，218 个既有警告；无新增 CS8604 |

### I-10 本轮未做（不冒充）

- 未新增/执行原生 `PublishAot` 场景与 harness 改造（S3），未跑需 PG 的 Asset 全量 E2E 与 oracle 接受标签改判（S2），未做带凭据 live（S5，本轮未读取任何凭据），未同步 `CapabilityMap.md` / `memory.md` / `AGENTS.md` / `usage-guide.md`（S4）。
- `payload` 内**值**的未配对转义仍由 step 13 既有 `catch (Exception)` 归类为 `Blocked` + per-item `AUTHORING_INVALID_PROVIDER_OUTPUT` 诊断：本轮只用控制靶例锁定该事实，没有把它升级为 Stage A 原始事实，也没有改写那段既有 catch。

## S2 实施日志（2026-11-04，S1 由 R2 放行后续推）

S1 复审 R2 结论：`e1e8553d` 放行，「可进入 S2。PG、原生门禁与 live 仍按后续切片验收。」本片只动消费者与证据面，**未改任何 `src/` 生产代码**（9 个改动文件全在 `samples/**` 与 `tests/Framework/Testing/**`）。

### I-11 oracle 接受标签（拒绝清单不缩）

`AssetUnsupportedIntentOracle`：

- `AssetUnsupportedIntentObservation` 增 `ExplicitOutcomeEntryCount`，由新增的 `CountExplicitOutcomeEntries(DescriptorAuthoringResult)` 计算：`unsupported` 数「同时有 code 与 message」的 reason，`needsClarification` 数「同时有 id、reasonCode 与 question」的 question。判定用观测到的真实条目数，不接受状态名单独作为证据。
- 新增标签 `ExplicitClarificationResult`，以及两个 pending 标签 `UnsupportedOutcomeWithoutEntryPending` / `ClarificationOutcomeWithoutEntryPending`；`AcceptedResult` 仍等于 `ExplicitUnsupportedResult`（旧断言含义不变），谓词 `IsAcceptedResult(label)` 只接受两个显式终态标签。
- `UnsupportedIntent` / `NeedsClarification` 以前落 `_` 分支（→ pending），现在各自成支并要求「自带诊断码 + 条目数 > 0 + 零草稿 + 零评审通过」。
- 既有条目全部保留：`Blocked`+三个 intent-level 码仍 accepted；`ProviderUnavailable`/`Failed`/`InvalidProviderOutput`/`PromptHashMismatch`/预算耗尽/`FabricatedCapabilityClaim`/`ArtifactsRejectedByReviewPending`/`NoArtifactProducedPending` 的判定与标签字面量未变。

### I-12 诊断分类器

`AssetAuthoringDiagnosticClassifier` 改为**按码优先**：两枚新码各有类别，`AUTHORING_INVALID_PROVIDER_OUTPUT` 内部再按 8a.v1 parser 实际消息族细分；其余一律 `Other` 且不回传消息内容（脱敏语义保留）。同时删掉两处 7g 死映射：`"Provider output is missing required 'plan' section."`（现文本是 `Provider output with outcome 'drafts' is missing the required 'plan' object.`，归入 `OutcomeShapeConflict`）与 `"Provider output deserialized to null."`（Stage A 已保证 object 根，step 9 的 null 分支不可达）。签名从 `Classify(message)` 改为 `Classify(code, message)`，三个调用点全部改用真实 `diagnostic.Code.Value`，不留两套口径。

判别口径升级：测试不再断言手写字符串，而是把 17 种真实信封喂给正式 `JsonDescriptorAuthoringOutputParser`，断言其产出的诊断归类到期望类别（含 `EnvelopeUndecodableMember` 两条，直接绑到 S1 R1 的新消息）。

### I-13 样例 runner 与 fake agent

`CompanyCertificationAuthoringGoldenScenarioRunner.RunUntilDraftSetReviewAsync` 在通用 block **之前**新增两条分支，落 `FinalDecisionSource = "AuthoringUnsupportedIntent"` / `"AuthoringNeedsClarification"`，`BlockReason` 只引用码表条目与 `missingInputs`/`evidenceRefs` 标识（不复制模型文本），`PerDraftReviewResults` 空且不进入 save/review 步骤。`FakeCompanyCertificationAuthoringAgent` 增加 `FakeCompanyCertificationAuthoringOutcome{Drafts, UnsupportedIntent, NeedsClarification}`，默认 `Drafts`（Phase 7f 行为与既有调用不变），显式终态按正式 parser 的形状构造（占位 Plan/DraftSet 身份、零草稿、每条一个 Warning 诊断）。

消费者回归（`CrestCreates.Samples.Tests`）：两条终态各自断言「零草稿 / 零评审 / 零保存（`IDescriptorDraftStore.ListAsync` 空）/ 无最终清单」，另加一条三态可区分性断言（两个新 `FinalDecisionSource` 与不可用答案的 `AllOrBlock` 三者互异，占位 `DraftSetId` 分别钉死）。既有 AllOrBlock 用例补 `FinalDecisionSource.Should().Be("AllOrBlock")`，保证「不可用答案」仍走原路径。

### I-14 Asset 真实链路与 gap-lock 改判

`AssetUnsupportedIntentNegativeCaseTests` 新增 4 条（recorded provider，无网络、无凭据）：

| 用例 | 事实 |
| --- | --- |
| `IntentNeedingNewDomainAlgorithm_ExplicitUnsupportedOutcome_IsAttributable_AndDeploysNothing` | `UnsupportedIntent` + `CAPABILITY_UNSUPPORTED` + `evidenceRefs=[visible:workflow:wf_asset_maintenance_review:1]` + 单条 `AUTHORING_INTENT_UNSUPPORTED` 诊断；`AssertBoundaryClosedAsync`；伪造提交仍 `NotFound`；oracle → `ExplicitUnsupportedResult` 且 `IsAcceptedResult` 为真 |
| `IntentMissingPolicyInformation_ExplicitClarificationOutcome_IsAttributable_AndDeploysNothing` | `NeedsClarification` + question `risk-threshold`/`MISSING_POLICY_INPUT`/`missingInputs=[riskThreshold, policyOwner]` + 单条 `AUTHORING_INTENT_NEEDS_CLARIFICATION`；边界闭合；oracle → `ExplicitClarificationResult` |
| `ExplicitClarificationOutcome_IsDistinguishableFromAProseRefusal_AndBothStayFailClosed` | 显式澄清 vs 散文拒绝 vs 静默空答三链对照：显式与后两者**状态与诊断码都不同**；散文拒绝与静默仍同码（诚实保留：契约只让「显式请求」可表达，不是所有拒绝都变成澄清）；三条链各自边界闭合；散文链 `IsAcceptedResult` 为假且标签为 `ProviderOutputRejectedPending` |
| `UnsupportedOutcomeWithAContradictoryDraftsSection_IsRejected_NotAccepted` | `outcome:"unsupported"` 同时带 `items` ⇒ `InvalidProviderOutput`（Stage A 形状冲突），真实链路上 oracle 不接受 |

gap-lock 改判：原 `IntentMissingPolicyInformation_FailsClosed_AndClarificationIsNotDistinguishable` 更名为 `IntentMissingPolicyInformation_WithAProseRefusal_FailsClosed_AndIsNotAClarification`，其载荷 `BuildNeedsClarificationResponse` 更名 `BuildProseRefusalResponse`；该用例继续断言散文拒绝= `InvalidProviderOutput` + 边界闭合，注释改为指向新的显式终态用例（#133 缺口的另一半已由 `8a.v1` 关闭）。`ReviewPassingFabricatedClaim_IsRejectedByTheOracle…` 补断言：伪造 `drafts` 答案在两个接受标签下都不被接受（§10.4「不支持意图得到 drafts 仍是拒绝」）。

边界断言的一处收紧：`AssertBoundaryClosedAsync` 原来无条件要求 `DraftSet.DraftSetId` 为空（7g 时代所有拒绝路径留空占位）。两个新终态按 S1 契约携带占位身份，故改为按状态断言精确字面量（`draftset_intent_unsupported` / `draftset_needs_clarification`，其余仍须空）。这比原断言更强：泄漏真实 draft-set 身份会直接失败，零草稿、零存储、零评审工件、审计空、gate 0、目录不变六项一条未减。

### I-15 判别自证（单守卫变异，物化副本 → 逐条还原）

| 变异 | 改动 | 红例 |
| --- | --- | --- |
| M-A | oracle：`UnsupportedIntent` 无条件接受（绕过 `IsExplicitTerminal`） | 2 条：`ExplicitOutcomeWithoutEntryContent_IsNotAccepted(UnsupportedIntent)`、`NothingWithoutAnIntentLevelAttributionIsEverAccepted(UnsupportedIntent)` |
| M-B | oracle：删 `NeedsClarification` 分支（落 `_`） | 4 条：oracle 两条 + Asset 显式澄清 + 三链可区分性 |
| M-C | runner：删两条新分支（回落通用 `AllOrBlock`） | 3 条 Samples 消费者用例 |
| M-D | 分类器：删两枚新码臂 | 3 条：Theory 的 unsupported/clarification 两行 + 专项断言 |
| M-E | 分类器：删三条信封消息臂 | 4 条 Theory 行（unknown/duplicate/undecodable 名与值） |

每条变异只打红其所属能力的靶例，还原后复跑全套。M-D/M-E 说明分类器类别绑定的是正式 parser 的真实输出，不是手写文本；M-A/M-B 说明「显式终态被接受」依赖条目内容与零候选，而非状态名本身。

一处诚实边界：`CountExplicitOutcomeEntries` 的「同时具备 code 与 message」内容校验，在真实 parser 输出上不可被单独打红（§4.3 边界已保证条目非空），它锁的是**观测层**——由 oracle 单测用构造观测直接覆盖（条目数 0、码不匹配、草稿泄漏、评审通过四种）。不把该防御性检查记作变异证据。

### I-16 执行结果（本片真实计数）

| 命令 | 结果 |
| --- | --- |
| Asset 全量 E2E：`ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING='Host=127.0.0.1;Port=5433;…' dotnet test samples/…E2E.Tests` | **123 通过 / 0 失败 / 2 跳过（共 125）**；跳过的是 `CREST_ASSET_LIVE_EVAL` 未开启的两条 live 入口 |
| 其中需 PG 的类：`--filter "AssetProposalReplay\|AssetLiveProposalRetention\|GoldenScenarioProcess"` | 8 通过 / 0 跳过（本地 `crestcreates-asset-pg-local` 容器，PostgreSQL 16.15） |
| authoring/oracle/分类器/probe 子集：`--filter "AssetUnsupportedIntent\|AssetAuthoringDiagnosticClassifier\|ReviewerLiveOracleProbe"` | 81 通过（S1 时同一过滤器为 38） |
| `dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests` | **37/37**（原 34） |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests` | 139/139 |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests` | 575/575 |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests` | 21/21 |
| `dotnet test samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.Tests` | 29/29 |
| `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests`（带 `DOCKER_HOST` podman 套接字） | 236/238：2 条证据账本用例红。**归因：非本片引入**——在 `e1e8553d` 全新 detached worktree（无任何 S2 改动）复跑同一过滤器同样 2 失败；未注入 `DOCKER_HOST` 时另有 3 条 Testcontainers 失败，注入后转绿 |
| `dotnet build`（全解决方案） | 0 错误 |

### I-17 S2 未做的事（不冒充）

- 未新增原生场景、未执行 `PublishAot`/link/run，未改 AotFixture 场景集合与 harness（S3）。
- 未做带凭据 DeepSeek live（S5）：本轮未读取任何凭据、未发模型请求；Asset E2E 里 2 条 live 入口保持 skipped，#159 的该验收项仍 pending。
- 未同步 `CapabilityMap.md` / `memory.md` / `AGENTS.md` / `usage-guide.md` 的能力声明，也未改 §5.1 B12 裁定文字与 #123 证据矩阵（S4）。
- 未改任何 `src/` 生产代码、未动 `8a.v1` 契约文字与 §10.1 的 23 行矩阵、§10.5 原生场景清单、§10.6 变异清单。
- 未开启 `CREST_ASSET_LIVE_EVAL`，未把 live 结果写成已执行；`AssetUnsupportedIntentOracle` 的接受标签扩展只由离线 recorded provider 证据支撑。

## S2 实施审计 R1 修复记录（2026-11-04，S2-R1-01 单项 P2）

审查 head `409fc8cb`，报告 `docs/review/2026-11-04-issue-159-s2-review-round1.md`。S0/S1 放行保持，S2 暂不放行。缺陷性质是新契约覆盖的错误通过，不是部署边界失效。

### I-18 缺陷事实

统一 oracle 仍把历史归因（`Blocked` + `AUTHORING_UNKNOWN_DESCRIPTOR_KIND` / `AUTHORING_UNSUPPORTED_DRAFT_OPERATION` / `AUTHORING_GOVERNANCE_BOUNDARY_VIOLATION`）映射到覆盖接受标签，而 B12 入口只凭该标签调用 `IsAcceptedResult`。审查者用仓库既有 `ReviewerResponseModelClient("unsupported-kind")` 原样重放完整离线入口（正式 agent/parser/review 组合、无网络、无凭据）实测：wire 为 `8a.v1`、`outcome=drafts`、AuthoringStatus `Blocked`、诊断 `AUTHORING_UNKNOWN_DESCRIPTOR_KIND`、**`ExplicitOutcomeEntryCount = 0`**、标签 `ExplicitUnsupportedResult`、**入口不抛（通过）**、草稿/评审通过/保存/gate 调用与成功全 0、目录未变、HTTP 状态 0。

即：这只能证明「框架拒绝了一个不支持的候选」，不能证明「模型使用了显式契约」。§10.4/§10.7 要求覆盖只来自 `UnsupportedIntent` / `NeedsClarification` + 合法码 + 非空条目；零副作用不能替代显式终态。而仓库里既有的绿色 `ReviewerLiveOracleProbeTests` 把这条输入钉成「应通过」，所以全量回归看不见这个缺口。

### I-19 修复：一套 oracle，两层判定

- `AssetUnsupportedIntentOracle` 新增标签 `LegacyIntentLevelBlockAttribution`；`Blocked` + 三个 intent-level 码改为落该标签，仍与「无 intent-level 诊断的 Blocked」以及所有旧拒绝标签区分。
- `IsAcceptedResult` 只接受 `ExplicitUnsupportedResult` / `ExplicitClarificationResult`；新增 `IsHistoricalAttribution` 让历史归因可被上报与 adjudicate，但永不作为覆盖。仍是同一套 oracle、同一处 `Classify`，未起第二套判定器。
- 三个旧拒绝码、零副作用事实、散文拒绝与伪造 drafts 负例、`ProviderUnavailable`/`Failed`/`InvalidProviderOutput`/预算/`PromptIntegrityFailure` 各标签全部保留不变（拒绝清单未缩，接受清单按要求缩）。
- B12 入口断言（`AssetDeepSeekLiveAuthoringEvaluationTests`）删除「或 intent-level block」这句错误放行文字，改为要求显式终态 + 合法码 + 条目数 ≥1 + 零 artifact，并把 `IsHistoricalAttribution` 写进失败消息；artifact 仍只记元数据。

### I-20 更新的旧控制与新增判别用例

| 项 | 变更 |
| --- | --- |
| `ReviewerLiveOracleProbeTests` | `unsupported-kind` 行由「应通过」改为 `shouldReject: true`，并加注释说明 §10.4/§10.7 的改判理由；该行原本断言的零副作用与 recorded provider 消费检查一字未动 |
| `AssetUnsupportedIntentProbeIsolationTests` | 隔离载体对同一路径的期望同步改为 `shouldReject: true`（隔离性质不依赖该入口是否放行，三条用例继续存在） |
| `AssetB12CoverageEntryTests`（新增 3 条） | 完整离线入口 + recorded provider 的仓库内常驻判别：历史 `drafts`+Capability 候选 → 标签 `LegacyIntentLevelBlockAttribution`、`ExplicitOutcomeEntryCount = 0`、七项零副作用成立、**入口必须抛**；显式 unsupported → `UnsupportedIntent`、条目数 1、入口必须通过、零副作用；显式 needsClarification → `NeedsClarification`、条目数 1、入口必须通过、零副作用。每条都断言 recorded provider 被消费且 `HttpResponse.HttpStatus = 0`（拒绝不得来自凭据/传输/预算） |
| `AssetUnsupportedIntentOracleTests` | 三个旧码逐条改为断言历史标签且 `IsAcceptedResult` 为假（另加「历史归因 + 候选漏进评审」仍非覆盖）；原 `NothingWithoutAnIntentLevelAttributionIsEverAccepted` 改为 `NothingShortOfAnExplicitTerminalOutcomeWithContentIsEverAccepted`，`Blocked` 行由「接受」改为「不接受」；逐标签可接受性表新增历史标签行（假） |
| 真实链 #133 用例 | 两条 `…YieldsExplicitUnsupportedResult…` 更名为 `…HistoricalIntentLevelBlock…`，authority-claim 用例同补判定断言：标签为历史归因、`IsHistoricalAttribution` 真、`IsAcceptedResult` 假；三例的零副作用断言与部署目录不变断言保留 |

### I-21 判别自证（单守卫变异，物化 → 逐条 `cp` 还原）

| 变异 | 改动 | 红例 |
| --- | --- | --- |
| N-A | 把 `Blocked`+intent-level 码改回 `ExplicitUnsupportedResult`（即原缺陷） | **12 条**：完整入口历史归因用例、三条真实链旧用例、四条 oracle 用例、`ReviewerLiveOracleProbeTests` 的 unsupported-kind 行、两条隔离用例。修复前全量回归对此**零红**（旧控制把它钉成应通过），这正是审查者指出的盲区 |
| N-B | `UnsupportedIntent` 绕过内容/计数门槛直接接受 | 2 条（该状态的条目缺失用例 + 「除显式终态外一律不接受」Theory 行） |
| N-C | `NeedsClarification` 同上 | 2 条（对应 NeedsClarification 两行） |

三组红例互不相交，还原后离线组 **86/86** 复跑绿。

### I-22 执行结果（本轮真实计数）

| 命令 | 结果 |
| --- | --- |
| Asset 全量 E2E（同一本地 PG，`ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING='Host=127.0.0.1;Port=5433;…'`） | **128 通过 / 0 失败 / 2 跳过（共 130）**，2 条跳过仍是未开启 `CREST_ASSET_LIVE_EVAL` 的 live 入口 |
| 离线 oracle/分类器/probe/覆盖入口子集 | 86 通过 / 0 失败（S2 交付时为 81） |
| `CrestCreates.Samples.Tests` | 37/37 |
| `CrestCreates.Agent.ControlPlane.Tests` | 575/575 |
| `dotnet build`（全解决方案） | 0 错误 |

`Authoring 139`、`Prompting 21`、Asset 单元 29 本轮未重跑（无 `src/` 改动，S1/S2 既有证据沿用，并在此标注为本轮未独立执行）。

### I-23 本轮仍未做（不冒充）

- 未发起真实模型请求：覆盖改判只由离线 recorded provider 与 oracle 单测支撑；S5 带凭据 live 仍 pending，本轮未读取任何凭据。
- 未做 S3 原生场景与 harness 改造、未做 S4 文档与状态同步。`docs/review/2026-10-10-framework-architecture-review-follow-up.md` §5.1 的 B12 裁定文字、`CapabilityMap.md`、`memory.md`、`AGENTS.md` 尚未反映「历史归因 ≠ 新契约覆盖」这一层，按计划归入 S4，且 `docs/review/2026-11-03-issue-133-b12-unsupported-intent-log.md` 里的 #133 当时结论属历史记录，不改写。
- 未改 `src/` 生产代码、未动 `8a.v1` 契约文字与 §10.1 的 23 行矩阵、§10.5 原生场景清单、§10.6 变异清单。

## S3 实施日志（2026-11-04，S2 由 R2 放行后续推）

### I-24 本片交付

新增 `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/AgentAuthoringOutcomeNativeAotFixture.cs`，在**已发布并实际执行的 NativeAOT 产物内**运行 §10.5 的场景集合：

| 场景 sentinel | 钉住的契约事实 | 同运行内的对照 |
| --- | --- | --- |
| `AgentAuthoringOutcomeRequiredRoots` | `DescriptorAuthoringResult` / 两个终态 outcome / reason / question 五个根全部可解析，且解析链里没有 `DefaultJsonTypeInfoResolver` | — |
| `AgentAuthoringUnsupportedOutcome` | `UnsupportedIntent` + 两条合法码 + evidenceRefs + `draftset_intent_unsupported`、诊断码 `AUTHORING_INTENT_UNSUPPORTED`/Warning；经 `DescriptorAuthoringJsonSerializerContext` 往返与 `Snapshot()` 后字段仍在；结果 JSON 里 `unsupported/reasons/code/message/evidenceRefs` 逐名可见且不含 `clarification` | 与澄清场景互斥 |
| `AgentAuthoringClarificationOutcome` | `NeedsClarification` + `risk-threshold`/`MISSING_POLICY_INPUT`/`missingInputs`/空 `candidateValues` + 占位 plan/draftset id；往返与 `Snapshot()` 保形；JSON 含 `clarification/questions/reasonCode/missingInputs/candidateValues` 且不含 `unsupported` | 同上 |
| `AgentAuthoringOutcomeShapeConflict` | **出现**的禁用成员一律拒绝：`items:null`、`plan:{}`、`items:[]`、带内容的 `plan`、`unsupported:{}`、带内容的 `needsClarification`、澄清下的 `items:null` —— 七条各自断言 `InvalidProviderOutput` + 单条诊断 + 消息点名该成员 + 草稿 0 + 两个终态对象为 null | 缺席形态（前两个场景）作为合法对照，不列为拒绝例 |
| `AgentAuthoringCodeTableRejection` | 未知 `unsupported.reasons[].code` 与未知 `questions[].reasonCode` 各一条，消息含 “is outside the closed code table”，草稿 0、终态对象 null；词表按 ordinal：同一码小写仍越界 | 同运行内只换码文的合法信封必须落到各自终态 |
| `AgentAuthoringEnvelopeUnknownMember` | item 层 `"extra"` ⇒ `unknown member 'extra'`、草稿 0；别名 `"Unsupported"` ⇒ `unknown member 'Unsupported'`（Stage B 关大小写敏感后别名不绑定，拒绝只能来自 Stage A） | 同一信封去掉 `extra` 必须在同一运行内真的产出 1 条草稿 |
| `AgentAuthoringEnvelopeDuplicatePrecedence` | payload 子对象重复 `id` + 单一错 hash ⇒ `duplicate member 'id'`（非 `Blocked`/hash mismatch）；根层重复 `outcome` + 错 hash ⇒ `duplicate member 'outcome'` 且消息不含 hash 码 | 无 Stage A 事实、仅错 hash 的信封仍必须 `Blocked` + `AUTHORING_PROMPT_HASH_MISMATCH` |
| `AgentAuthoringWireDisclosure`（扩展） | 披露文本断言 `Wire reference (contract 8a.v1):`、`outcome is required and is one of: drafts, unsupported, needsClarification.`、「never repeat a member at one level」、「null-instead-of-omitted」、「omit plan and items entirely (not null, not {})」、两张闭码表原文、散文拒绝禁令；模板版本 v3、`ContractVersion == 8a.v1`；**四条示例逐条走正式 parser**：两条 drafts 示例仍物化为 HumanTask/Workflow（保留 S1 前的逐步字段断言），两条拒绝示例必须真的落到各自终态且零草稿 | 示例与规则同源自证：披露若与 DTO/解析器漂移，原生门禁即红 |

`Program.cs` 改动：`Main` 接参数、`--authoring-gate-control=<name>` 只接受登记名（未知名在跑任何场景前退出 4，防止拼错的负控静默走绿配置）、绿色聚合与总 sentinel 只在全部场景通过时发布。

### I-25 首次执行原生门禁就发现的既有红灯（本片修复，记账在此）

S1 把披露示例从 2 条扩到 4 条，但 AotFixture 的 `AgentAuthoringWireDisclosure` 仍钉 `examples.Length == 2`。`8a8a7409` 起该门禁实际是红的：本次第一次以 JIT 方式执行该产物即得 `FAIL [AgentAuthoringWireDisclosure]: Expected two wire examples, got 4.`，`SerializeDeserialize_RepresentativeToolRoots:FAIL`。S1/S2 的回执都明确写了「未执行原生 PublishAot」，所以这条红灯没进任何一轮全量回归（`dotnet test` 全解决方案会跑到它——本轮之前没人跑到，是因为它只在带 linux-x64 发布门禁的执行里出现；本片把它列为每片收尾必跑）。本片删除了旧的两例钉法，改为四例逐条消费并断言终态，红灯关闭。**这条不是 S3 新引入的缺陷，也不是掩盖：记录原因是「后续切片不得假设前片门禁绿」。**

### I-26 harness 两层协议（同片改，§10.5 要求）

`ControlPlaneJsonContractsAotFixtureTests` 重写为三层，一次 `PublishAot` 由全类共享（`Lazy`）：

1. 绿色运行：exit 0 + **逐个点名** 14 条场景 sentinel（含新增 7 条）必须为 `:PASS`、聚合行 `:PASS`、全输出不含 `:FAIL`、群 sentinel `CONTROL_PLANE_AGENT_AUTHORING_OUTCOME_NATIVEAOT_OK` 与总 sentinel 都在。总 sentinel 不再单独充当证据——它只证明聚合，点名才证明场景真的跑了。
2. 负控运行：`TheoryData` 钉死 7 对（控制名 ⇒ 唯一应变红的场景名）。每条单独执行同一二进制，要求非零退出、自己那条 `:FAIL`、总 sentinel 与群 sentinel 都不出现，且**点名场景集合里红掉的必须恰好等于那一条**（聚合行按构造必然跟随，已从可比集合里排除）。
3. 未知控制名：非零、无任何 sentinel、且连 `ReflectionFallback_IsDisabled:PASS` 都不得出现。

一层不变的「正确拒绝」样本（非法输入被原生断言拒绝）留在绿色侧，门禁负控永不与它混在同一次运行判定。

### I-27 判别自证（生产侧变异，物化副本 → 逐条 `cp` 还原 → `cmp` 验字节一致）

| 变异 | 改动（只切一个守卫） | 原生门禁红例 |
| --- | --- | --- |
| M1 | 摘掉 Stage A 的「声明名/未知成员」规则，其余 Stage A 步骤与 Stage B 全留 | `AgentAuthoringEnvelopeUnknownMember:FAIL`（其余场景全绿，聚合行跟随）；9 条用例 8 红：绿运行 + 六条与它无关的负控隔离断言 |
| M2 | 只摘掉 Stage A 的原始重复成员规则 | `AgentAuthoringEnvelopeDuplicatePrecedence:FAIL`，7 红/2 绿 |
| M3 | presence 规则削弱为「只有字符串值的禁用成员才算出现」（即 §10.6(b) 的 null-only 实现） | `AgentAuthoringOutcomeShapeConflict:FAIL`，7 红/2 绿 |
| M4 | 两张闭码表 `IsKnown` 恒真（§10.6(d)） | `AgentAuthoringCodeTableRejection:FAIL`，7 红/2 绿 |

四条变异各自只打红它名下那一条场景，证明这四个原生场景不是「只要抛就红」的空门禁；全部还原后 `cmp` 逐字节一致，`git status` 只剩本片的三个测试文件，原生门禁复跑 **9/9 绿**，`Authoring 139/139`、`ControlPlane 575/575` 复跑绿。

### I-28 执行结果（本轮真实计数）

原生门禁（本片主证据）：

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.Tests -c Release`（`PublishAot` + `-r linux-x64 --self-contained` → 原生 link → 执行原生产物） | **9 通过 / 0 失败（共 9）**：1 条绿色点名运行 + 7 条负控逐条独立运行 + 1 条未知控制名拒绝 |
| 绿色运行的原生输出 | 14 条点名场景全部 `:PASS`、无 `:FAIL`、`CONTROL_PLANE_AGENT_AUTHORING_OUTCOME_NATIVEAOT_OK` 与 `CONTROL_PLANE_JSON_CONTRACT_NATIVEAOT_OK` 均出现；`publish.log` 内该 fixture 项目 0 条 IL2026/IL3050 |
| 逐条负控的原生输出 | 每条恰好 1 条点名场景 `:FAIL`（其余场景仍绿），聚合行跟随，两个 sentinel 均不出现，退出码非零 |
| I-27 四条生产变异的原生门禁 | 见 I-27 表：各只打红名下场景；还原 `cmp` 逐字节一致后复跑 **9/9 绿** |
| JIT 侧先行执行（同一片，用于抓既有红灯） | 修复前 `FAIL [AgentAuthoringWireDisclosure]: Expected two wire examples, got 4.`；修复后 7 条新 sentinel 全 `PASS` |

其余回归（同轮）：

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests` | 139/139 |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests` | 575/575 |
| `dotnet test samples/…E2E.Tests`（本地 PG 5433） | **128 通过 / 2 跳过（共 130）**，与 S2 记录逐条一致；2 条跳过仍是未开启 `CREST_ASSET_LIVE_EVAL` 的 live 入口 |
| `dotnet test tests/Framework/Testing/CrestCreates.Samples.Tests` | 37/37 |
| `dotnet build`（全解决方案） | 0 错误 |

全解决方案 `dotnet test` 一轮：69 套绿 / 18 套红。18 套红全部是容器/发布并行竞态类，逐套带 `DOCKER_HOST`＋本地容器复跑归因如下（未注入时 Testcontainers 连不上、broker 拒绝连接、多个原生门禁在同一轮里互相抢 `obj/project.assets.json`）：

| 套件 | 带环境复跑 | 归因 |
| --- | --- | --- |
| RabbitMQ 集成 37、LocalEvent 集成 116、RabbitMQ AotHost 原生 2 | 全绿 | broker/套接字缺失导致的首轮红 |
| Agent.Tools AotFixture 1、CapabilityEndpoint AotFixture 13、GeneratedCrud AotFixture 28、AssetManagement AotFixture 3、Procurement AotFixture 9、Ticket AotFixture 6、PostgreSql AotFixture 1 | 全绿 | 同一轮内并行 `publish` 的 NuGet restore 竞态（错误行是 `error : The file '…/obj/project.assets.json' already exists`） |
| MongoDB 8红/10、Kafka 集成 9红/3、Database.Migrations 2红、PostgreSql.Tests 3红/485 | 同样红 | **基线复现**：在 `d5a73b5f`（本片改动之前）新建 detached worktree 同环境复跑，红例数完全一致 |
| `CrestCreates.IntegrationTests` 40红/9、`SaaSHelpdesk.Tests` 11红/154 | 同样红 | 根因 `Npgsql 42P01: relation "OpenIddictScopes" does not exist`（该样例 Host 需要的迁移/建表未在本地 PG 落）；IntegrationTests 未在基线 worktree 独立复跑（该 worktree 构建 BuildTasks 时命中 `MSB4062`），故此两条记为「原因已观测、基线复跑未完成」，不写成已证 |
| DependencyBoundaries 236/238 | 同样红 2 条 | 与 S2 记录的同一基线红例（证据账本），已在 `e1e8553d` 干净 worktree 复现过 |

### I-29 S3 未做的事（不冒充）

- 未做 S4：`CapabilityMap.md`、`memory.md`、`AGENTS.md`、`usage-guide.md` 与 `docs/review/…follow-up.md` §5.1 的 B12 裁定文字仍未反映新终态与原生门禁证据；`docs/review/2026-11-03-issue-133-b12-unsupported-intent-log.md` 的 #133 当时结论作为历史记录不改写。
- 未做 S5：本轮未读取任何凭据、未发起真实模型请求；两条 live 入口保持 skipped，#159 的该验收项仍 pending。
- 原生声明只覆盖 I-24 表里**实际执行过的信封样本**与 linux-x64 一个 RID：不打折成「§10.1 全 23 行矩阵原生通过」，也不推导认证、审批、activation 链，其他 RID 未验证。
- 未改任何 `src/` 生产代码：四条变异全部还原并 `cmp` 验字节一致，`git status` 只剩本片的三个测试文件（fixture 新文件、fixture `Program.cs`、harness 测试）。
- 未新增第二条判定链：原生场景与 unit/Asset 层共用同一个 `JsonDescriptorAuthoringOutputParser`，fixture 侧负控只改**断言的期望值**，没有为可观测性加生产测试钩子。
## S3 实施审计 R1 修复记录（2026-11-05，S3-R1-01 单项 P2）

审查 head `a9c79e59`，报告 `docs/review/2026-11-05-issue-159-s3-review-round1.md`。S0/S1/S2 放行保持；S3 暂不放行。审查独立复验了提交原样的原生门禁 **9/9 绿**，缺口在验收断言的覆盖面，不在门禁是否真的执行。

### I-30 缺陷事实

`AgentAuthoringUnsupportedOutcome` / `AgentAuthoringClarificationOutcome` 的 `terminal` 判断钉的是**序列化前**的 `result.Status`；`roundTrip` 分支只比读回对象的结构化内容与零草稿，没有比 `restored.Status`。因此「往返把拒绝/澄清读成 `Succeeded`」这种状态误读可以照常报 PASS——正好漏掉 #159 用状态区分终态的核心契约。审查在隔离 fixture 里只把序列化 JSON 的 `status:6/7` 改成 `status:0` 走同一官方 source-generated Deserialize，重新 PublishAot/link/run 后两个场景仍 PASS、总 sentinel 仍出现、整套 9/9 仍绿；补两条读回状态比较后同一篡改即 exit 2、两场景 FAIL。

### I-31 修复

1. 两个 roundTrip 正向分支各自把 `restored.Status == UnsupportedIntent` / `== NeedsClarification` 提到比较链首位，结构化字段、互斥（`Clarification is null` / `Unsupported is null`）、零草稿与 `Snapshot()` 断言原样保留。
2. 判别回证不写成一次性手工实验，而是落成两条**常驻门禁负控**（`ControlNames` + harness `TheoryData` 各登记）：
   - `unsupported-roundtrip-status-poisoned`、`clarification-roundtrip-status-poisoned` 走 `RoundTripWithStatus(result, Succeeded)`：同一 source-generated 序列化产物上只改 `status` 节点，再用同一 context 反序列化；helper 自身要求「改写必须真的落到读回对象」，落不到就直接抛——这样一条静默失效的负控不可能被读成绿。场景仍断言**正确**的读回终态，所以真实代码必然让它红，符合「正确拒绝=绿、门禁负控=非零且无 sentinel」两层不混的既有协议。

### I-32 判别自证（只改测试侧一处）

| 实验 | 改动 | 原生结果 |
| --- | --- | --- |
| 修复后原样 | 两条读回状态比较 + 两条状态负控在册 | `PublishAot` + link + run：**11 通过 / 0 失败（共 11）**＝1 绿色点名运行 + 9 条负控逐条独立运行 + 1 未知控制名拒绝；两条状态负控各自只红名下场景（`run-unsupported-roundtrip-status-poisoned.log` → 仅 `AgentAuthoringUnsupportedOutcome:FAIL`；`run-clarification-roundtrip-status-poisoned.log` → 仅 `AgentAuthoringClarificationOutcome:FAIL`），聚合行跟随，群/总 sentinel 均不出现 |
| 反例（counterfactual） | 只删掉新加的两条 `restored.Status` 比较，其余一字不动 | **9 通过 / 2 失败**，失败的恰是两条状态负控：被篡改的原生产物重新变成 exit 0 且照常打出 sentinel，于是「负控必须非零」的断言红。即这两条比较正是该状态误读的唯一判别点，去掉即复现审查指出的绿灯 |
| 还原 | 与反例前快照 `cmp` 逐字节一致后复跑 | **11/11 绿**；`Authoring 139/139`、`ControlPlane 575/575` |

`src/` 仍一行未改；本片改动只在 fixture 场景文件与 harness 测试文件。

### I-33 本轮另观测（不属于本片，未处理）

`dotnet build` 全解决方案本轮出现 1 条错误：`tests/Integrations/CrestCreates.Mcp.Memory.AotFixture` 报 `error NU1903: 包 "Microsoft.DotNet.ILCompiler" 10.0.13 具有已知的 高 严重性漏洞（GHSA-p3jp-9v83-76w9）`，同包在 `ControlPlane.JsonContracts.AotFixture` 里仍是警告级。同一项目在锁定当前状态单独构建即可复现，与本片的源码改动无关——它是 NuGet 漏洞库刷新后把既有版本钉升成错误（本片 S3 交付时的全量 `dotnet build` 为 0 错误，此后未动过任何包）。处置需要改包版本或调 audit 策略，属维护者裁定，本轮不擅自变更依赖。
## S3 实施审计 R2 修复记录（2026-11-05，S3-R2-01 单项 P2 + 工具链裁定）

审查 head `d8040bd1`，报告 `docs/review/2026-11-05-issue-159-s3-review-round2.md`。原 S3-R1-01 关闭，S0/S1/S2 放行保持；S3 仍暂不放行，新增 S3-R2-01。

### I-34 缺陷事实

`RoundTripWithStatus` 在改写没落地时抛 `InvalidOperationException`，而这个异常被 `RunScenario` 的通用 catch 打成目标场景的 `场景:FAIL`——正好满足 harness 对负控的全部判据（非零、目标场景 FAIL、两个 sentinel 缺席）。您给的单行变异（只删 `envelope["status"] = (int)poisonedStatus;`，保留两条正确状态比较与 helper 自检）在真实 publish/link/run 下得到 `AgentAuthoringUnsupportedOutcome:FAIL` + `FAIL [...]: The status poison did not reach the read-back object`，整套仍 **11/11**。即：设置阶段失败冒充了目标断言的拒绝，负控什么都没证明。缺陷在判定语义，不在读回状态比较——后者上一轮已补且保留。

### I-35 修复：注入落地要有见证，设置失败走独立通道

1. 新增 `GateControlSetupException`；注入器的每一步前置/自检都改抛该类型：请求的状态与原值相同（等于没篡改）、序列化产物里没有 `status` 成员可改、改写后读回对象状态未变。
2. `RunScenario` 先单独捕获它，打印 **`${name}:SETUP-FAIL`**（不含 `:FAIL` 子串，因此不可能冒充目标场景的预期 FAIL），仍返回 false 让进程非零、sentinel 缺席。
3. 注入真正落地后才打印见证行 `CONTROL_SETUP_LANDED <场景> status=<原值>-><篡改值>`，harness 据此区分两族负控的成功判据。
4. 按裁定一保持**一张登记表、一个 harness**：`GateNegativeControls` 增第三列 `requiresLandedInjection`，两条 roundtrip-status 负控为真，其余七条期望翻转类为假。所有负控一律要求输出不含 `:SETUP-FAIL`；输入篡改类还必须先出现落地见证，再出现目标场景 `:FAIL`。绿色运行额外断言既无见证也无 SETUP-FAIL（它不该武装任何注入）。仅改名字或分数组不算修好，改的是判据。

### I-36 判别自证（全部为真实原生 publish/link/run，SDK 10.0.114）

| 配置 | harness 结果 | 原生输出关键行 |
| --- | --- | --- |
| 提交原样 | **11 通过 / 0 失败（共 11）** | 两条状态负控各自 `CONTROL_SETUP_LANDED …status=UnsupportedIntent->Succeeded` / `…NeedsClarification->Succeeded` 之后才 `AgentAuthoring*Outcome:FAIL`；绿色运行 14 条点名场景全 PASS、无 FAIL、无见证、无 SETUP-FAIL |
| CF-1 您要求的失效注入反例：只删 `envelope["status"] = (int)poisonedStatus;` | **9 通过 / 2 失败**，失败恰是两条 roundtrip-status 负控 | `AgentAuthoringUnsupportedOutcome:SETUP-FAIL` + `SETUP-FAIL […]: the status tamper did not reach the object AgentAuthoringUnsupportedOutcome reads back (observed UnsupportedIntent)`，且**没有** `CONTROL_SETUP_LANDED`；harness 因「缺见证 / 出现 SETUP-FAIL」判红，不再把它当成目标拒绝 |
| CF-2 只删两条 `restored.Status` 比较（R1 反例复跑） | **9 通过 / 2 失败**，仍是那两条负控红 | 注入照常落地，但场景不再比状态，于是被篡改的产物报 PASS → 负控的「必须非零」断言红。R1 的判别点保持 |
| 还原 | 快照 `cmp` 逐字节一致后复跑 **11/11 绿** | `Authoring 139/139`、`ControlPlane 575/575` |

### I-37 工具链 NU1903 处置（裁定二）

- 采用同 feature-band 的 **SDK 10.0.114**：`dotnet-install.sh --version 10.0.114 --install-dir ~/.dotnet`。系统 rpm 侧 Fedora 仓库当前只到 `dotnet-sdk-10.0-10.0.113-1.fc45`（另有 11.0.100~rc），且本机 `sudo` 需要密码，故未做系统级替换——`/usr/bin/dotnet` 仍是 10.0.113，**该构建阻断在这条路径上仍然存在**，需要您在系统包里升 `dotnet-sdk-10.0` / `dotnet-sdk-aot-10.0` 到 10.0.114 对应版本。
- 确认 pack 已到修复版：新 SDK 下 `dotnet restore tests/Integrations/CrestCreates.Mcp.Memory.AotFixture` 解析出 **`Microsoft.DotNet.ILCompiler/10.0.14`**，`~/.dotnet/shared/Microsoft.NETCore.App/10.0.14`；官方 10.0.14 安全公告（2026-10-25）列出的 SDK 正是 10.0.114/10.0.403，受影响范围 `>=10.0.0 <10.0.14`。
- **未关闭审计、未加 NoWarn、未降 NU1903 严重级**：仓库内没有任何 `NuGetAudit` / `WarningsNotAsErrors` / `NoWarn NU1903` 覆盖（本轮 grep 确认），MCP fixture 原有 `TreatWarningsAsErrors=true` 保持。
- 重发并执行相关原生门禁（见 I-38）。

### I-38 执行结果（补丁工具链后的真实计数）

| 命令（`DOTNET_ROOT=~/.dotnet`，`dotnet` 10.0.114） | 结果 |
| --- | --- |
| `dotnet build`（全解决方案） | **0 错误，NU1903 命中 0**（此前 1 条 error NU1903） |
| `dotnet test tests/Integrations/CrestCreates.Mcp.Memory.AotFixture.Tests` | 1/1（publish/link/run） |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.AotFixture.Tests` | 1/1 |
| `dotnet test …CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.Tests` | **11/11**（1 绿色点名运行 + 9 负控逐条独立运行 + 1 未知控制名拒绝） |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests` / `…ControlPlane.Tests` | 139/139 / 575/575 |

其余原生门禁（CapabilityEndpoint、GeneratedCrud、AssetManagement、Procurement、Ticket、PostgreSql、RabbitMQ AotHost）本轮未在 10.0.114 下重发，其既有证据仍是 10.0.113 产出的原生执行结果；本轮把它们记为「工具链未升级后复跑」，不写成 10.0.114 证据。

### I-39 本轮仍未做（不冒充）

- 未做 S4 文档/状态同步，未做 S5 带凭据 live：本轮仍未读取任何凭据、未发模型请求。
- 未做系统级 SDK 包升级（无 sudo 口令），仓库默认 `dotnet` 路径仍会撞 NU1903；这是留给维护者的构建阻断，不属于本片源码引入。
- 原生声明仍只覆盖 I-24 表中实际执行过的信封样本与 linux-x64 一个 RID，不推导 §10.1 全 23 行、其他 RID、认证/审批/activation 链。
- `src/` 仍一行未改：CF-1/CF-2 只动 fixture 测试文件，均 `cmp` 还原。
## S4 实施日志（2026-11-05，S3 由 R3 放行后续推）

范围：把 S1–S3 的已执行事实同步到「唯一导航面 + 状态记录 + 工程指令 + 契约指南 + 裁定回填 + 证据矩阵」六个面，并把 `7g.v1` 历史文档标为已被取代而不改写。**本片零 `src/` 改动、零测试改动**；退出条件按切片表逐条自证：声明与证据一一对应、未执行项写「未验证/pending」、S5 未执行故 Issue 不关闭。

### I-40 同步落点与内容口径

| 文件 | 改了什么 | 刻意不做什么 |
| --- | --- | --- |
| `docs/Feature/AgentControlPlane/usage-guide.md` | §18 头部加 `8a.v1`（#159）契约说明；§18.2 的 `switch` 补两个终态分支及其载荷/占位/零草稿；§18.5 重写为「Stage A 信封事实 → Stage B 严格投影」两段表 + 三终态载荷表 + 边界 + 拒绝类诊断码原表；新增两个终态的最小 wire 样例（与披露示例同源）；§18.9 约束表补 6 行（`outcome` 必需、presence 非 truthiness、码表封闭 Ordinal、显式终态零草稿、**历史归因≠新契约覆盖**、`contractVersion` 参与哈希）；§17.2/17.3 补 fake agent 三分支与 runner 的两个新 `FinalDecisionSource`；§18.8 把「注册 20 个类型」改为按根列举（实为 27 个 `[JsonSerializable]` 根）；§19.1/19.2/19.4 的 prompt 字面量改用生产常量（`descriptor-authoring` / `descriptor-authoring-prompt-template-v3` / `8a.v1`） | 不动工具 DTO/审查报告等与本片无关的章节；不删 `7g.v1` 历史叙述（只保留一句「不再被接受」的说明行） |
| `docs/Feature/CapabilityMap.md` | 新增 §8「#159 交付记录」四维表（Abstractions / Authoring / Authoring.Http / Asset 真实链路 / 样例 golden scenario），每条执行证据只写实际跑过的套件与场景；支持声明逐条列出四条不外推边界（23 行矩阵、RID、认证/审批/activation、其余七个门禁）+ SDK 入口边界 + S5 pending；纪律段写「新增终态=词表+码表+根闭包+原生场景同批变更」；§6 交叉链接指向 #123 矩阵新行 | **不动 §4 项目索引的「导航（本轮未重评，不背书）」行**——重评属 #131 式裁定，本片只另立交付记录（沿用 §7 #132 的先例） |
| `memory.md` | 顶部新增 2026-11-05 #159 条目（分支/PR、契约、两阶段解析、消费者、证据、支持声明、仍待办）；「Delivered capabilities」的 Agent 治理面条目补 `8a.v1` 与历史归因口径；Known gaps 的 #133 条目在原文明示「后续状态见顶部 #159 条目，本条为历史证据」 | 不改写 #133 条目正文的当时裁定与实跑记录 |
| `AGENTS.md` | AOT 能力分层块新增「Agent Authoring 显式终态契约 8a.v1」逐项声明（场景清单、两层负控、验收入口 SDK 10.0.114/ILCompiler 10.0.14、七个门禁未复验、默认 `/usr/bin/dotnet` 仍 10.0.113 且命中 NU1903）；平台能力段新增 `8a.v1` 契约条（presence/码表/两阶段/历史归因不计覆盖）；当前平台状态段新增 #159 条目；「最后更新」改 2026-11-05 | 不把任何声明写成整项目/整链背书；不引入新的抽象或流程 |
| `docs/review/2026-10-10-framework-architecture-review-follow-up.md` | 新增 §5.2「#159 交付后改判」：契约缺口已由 `8a.v1` 承载；§5.1 的 `Blocked`+intent-level 路线重述为**历史归因、永不计覆盖**（含 `ReviewerLiveOracleProbeTests` 改判）；「澄清不可判别」缺口如何被 `outcome` + 条目边界关闭；现有证据与仍未达成（S5 pending、工具链边界）；§6 任务表加 #159 行 | §5.1 各行原文一字不改，保留 #133 当时的历史证据身份 |
| `docs/review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md` | §2.1 更新「Control Plane JSON 契约」行为逐场景点名清单；新增「Agent Authoring `8a.v1` 显式终态」行（11/11、逐场景 sentinel、两层负控与 `CONTROL_SETUP_LANDED` 见证、M1–M4 与 CF-1/CF-2 判别回证、覆盖限制）；§三 追加**工具链版本边界**条（验收入口 10.0.114、默认 host 仍旧、七个门禁未复验、CI/任务须显式选择以免裸 `dotnet` 静默回退） | 不把升级前门禁的证据改写为 10.0.114 证据；不宣称全环境迁移完成 |
| `docs/superpowers/specs/2026-07-01-phase-7g-…-design.md` | 文末新增「Appendix — superseded wire contract」：列出被 `8a.v1` 取代的四项事实（判别式、两终态、presence、两阶段），并声明正文为历史、`7g.v1` 答题现被判 `InvalidProviderOutput`、历史 `Blocked`+intent-level 路线只是归因；头部 Status 加「历史批准；wire 已被取代」标注 | 不重写正文任何一节（含 `baseVersion` 附录），历史可追溯优先于整洁 |

### I-41 声明与证据的一一对应自查

- `7g.v1` 残留：`grep -rn "7g\.v1" src --include=*.cs` = **0 命中**；`samples/**` 仅 1 处、`tests/**` 仅 1 文件，均为**旧版本拒绝负例**（`AssetAuthoringDiagnosticClassifierTests` 的 `ContractVersionMismatch` 行、`OutputParserEnvelopeMatrixTests` 的版本行），属验收清单明示豁免的负例字面量；文档侧残留全部位于历史记录文件（`docs/review/2026-09-*`、`docs/superpowers/plans/2026-07-*`、7g spec 正文与本计划），按裁定保持原文。
- 数字口径：本日志与 `memory.md`/`AGENTS.md`/地图/指南里出现的每个计数（139/139、575/575、37/37、128 通过/2 跳过共 130、11/11、9 条负控、M1–M4、CF-1/CF-2、27 个根、7 个未复验门禁）都取自 S1–S3 已记录的执行结果，不新造数字；未跑的写「未验证/未复验/pending」。
- 能力地图门禁复跑：`dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests --filter CapabilityMap` → **7/7 绿**（§4 索引区未动，故覆盖校验不受本片影响）。

### I-42 spec §12 验收清单状态（本片只做勾选与注明，不改契约文字）

已按证据勾选 10 项：契约区分、presence 四种出现形态、重复/未知先于值检查、Stage B 选项钉死、版本单值、单元矩阵 + 真实链路 + 原生场景与 harness、变异判别按机制、JSON 闭包断言必需根、`7g.v1` 迁移、无新增抽象/评测平台/持久化工件/测试钩子。

**保持未勾 2 项，都因 S5**：

- 「One credentialed live run executed and recorded (S5)」——本轮仍未读取凭据、未发模型请求。
- 「Support statements updated to match executed evidence; **live verdict recorded either way** without reclassification」——前半句已随本片完成，但该项同时要求记录 live 裁定结果；按「未执行不得当作已完成」的口径整项留空，并在项下注明原因，而不是勾选后附说明。

Issue #159 因此保持 open，不以单元/原生证据替代收口。

### I-43 S4 执行结果（真实计数）

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests --filter CapabilityMap`（SDK 10.0.114 入口） | 7 通过 / 0 失败 |
| 文档改动后 `dotnet build`（全解决方案，SDK 10.0.114） | **0 错误 / NU1903 命中 0**（BUILD_EXIT=0，2 条既有 BuildTasks 警告） |
| `src/` 与 `tests/` 差异 | 本片**未改任何代码文件**，仅 `docs/**`、`memory.md`、`AGENTS.md` |

### I-44 S4 未做的事（不冒充）

- 未做 S5：不带凭据、不跑真实模型，B12 的覆盖判定保持 pending；Issue #159 与 PR #160 均不关闭、不合并。
- 未重评 §4 项目索引的导航行，未新增任何能力地图条目以外的背书；未修改 `CapabilityMapCoverageArchitectureTests` 的索引区内容。
- 未升级系统 RPM 工具链，未把其余七个原生门禁改写成 10.0.114 证据。
- 未改写 #133/7g 等历史文档的事实与裁定原文；本片只在它们旁边加「已被取代/后续状态」指针。

## S4 实施审计 R1 修复记录（2026-11-05）

审查者发现 **1 项 P2 + 1 项 P3**，S0–S3 放行保持；两项都是我在 S4 里写下的表述失准，不是代码缺陷（`src/` 差异为 0 行）。裁定：P2 按「成功载荷与拒绝结果分开描述、保留原子拒绝语义」修；P3 只改本次新增附录的历史说明，7g 正文原样保留。

### I-45 P2：指南把 `Blocked` 混进「草稿 ≥1」

事实核对（`src/Runtime/Agent/CrestCreates.Agent.Authoring/Parsing/JsonDescriptorAuthoringOutputParser.cs`，逐行读回）：

| 形状 | 代码位置 | 身份与草稿数 |
| --- | --- | --- |
| `drafts` 成功 | :273-276 要求 `items` ≥1；:283-292 `TryParseItem` 返回 true 必产出非 null draft；:302 `PlanId = dto.Plan.PlanId ?? plan-<hash 前 16>`；:310 `DraftSetId = plan.PlanId` | 草稿 **≥1**，`DraftSetId == Plan.PlanId` |
| `drafts` 拒绝（hash） | :118-126 → `CreateResult(Blocked, emptyPlan, emptyDraftSet, …)` | 草稿 0，`PlanId = ""`、`DraftSetId = ""` |
| `drafts` 拒绝（任一 item） | :283-286 同一出口形状 | 草稿 0，同上 |
| `unsupported` / `needsClarification` | :410-411 / :522-523 | 草稿 0，固定占位身份 `plan_intent_unsupported`/`draftset_intent_unsupported`、`plan_needs_clarification`/`draftset_needs_clarification` |

修法：
- `docs/Feature/AgentControlPlane/usage-guide.md` §18.5 载荷表把 `drafts` 拆成「成功」与「`Blocked`」两行，表前一句改为「三种 `outcome` 的形状互斥，但 **`outcome` 与 `Status` 不是一一对应**」，表后保留原子拒绝段落并写明「`Status` 是 drafts 系」≠「一定拿到草稿」；空串身份明确「不是占位值」，与两终态的占位身份区分。
- §18.2 的 `switch`：成功分支注上「Drafts ≥1 且 `DraftSetId == Plan.PlanId`」，`Blocked` 分支补「草稿 0 + 空串身份、不返回部分草稿」。
- `AGENTS.md` 平台能力 `8a.v1` 条补同一句形状区分（工程指令面不得留错）。
- 判别锚点（本片的 `tests/` 改动，非生产行为改动）：`OutputParserTests.UnknownDescriptorKind_Returns_Blocked` 加 `PlanId`/`DraftSetId` 空串 + 草稿空断言；`OutputParserEnvelopeMatrixTests.Row11_…_IsBlocked` 加同一组空串断言；`OutputParserTests.ValidHumanTaskItem_Returns_Succeeded` 加 `DraftSetId == Plan.PlanId`。此前只有 `Drafts.Should().BeEmpty()`，指南新写的「空串身份」在测试里没有对应断言。

### I-46 P3：7g spec 新附录把历史写错

- 原写「`7g.v1`: an envelope whose drafts were inferred from the absence of `items`」。核对被取代的 parser（`git show 8a8a7409^:…/Parsing/JsonDescriptorAuthoringOutputParser.cs` 步骤 5）：`items` 缺失或为空是 **`InvalidProviderOutput` + "Provider output contains no items."**，不是 drafts 的推断来源。
- 改为：`7g.v1` **没有显式 `outcome` 判别器**，草稿信封是唯一被接受的形状，`items` 缺失/为空按输入形状错误拒绝——不支持的意图除了报一个输入形状错误之外没有别的可说。
- 7g 正文（含 `baseVersion` 附录）与其余历史文档一字未改；附录底部设计指针标签随 rev 号更新（rev.5 → rev.7），本计划、`memory.md`、`CapabilityMap.md` §8、§5.2 的同一指针同步。

### I-47 自查中另发现的一处状态自相矛盾（非审查者发现，如实记）

`memory.md` 顶部 #159 条目是 S4 落的，但其「Still pending」仍写「S4 文档/状态同步待办」——同一条目内自相矛盾。已改为「S4 已交付、按 R1 修订后待复审」，并把 S5 写成维护者已获准的单次范围（现有 B12 入口、16384 预算、凭据不打印/入库、仅 `/tmp` 元数据、不自动重试、结果照原样记录）。`AGENTS.md` 当前平台状态与 follow-up §6 任务表的状态文字同步为「S4 已交付待复审、S5 pending（已获准单次执行）」。

### I-48 R1 执行结果（真实计数，SDK 10.0.114 入口）

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests --filter CapabilityMap` | 7 通过 / 0 失败 |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests`（含 I-45 三处新断言） | **139 通过 / 0 失败** |
| 变异 M5：把 `Blocked` 的两条出口改为携带 `plan_blocked`/`draftset_blocked` 身份（即指南被写错的那种读法） | 恰 **2 条目标用例打红**（`Row11_SingleHashMismatch_WithValidClarificationBody_IsBlocked`、`UnknownDescriptorKind_Returns_Blocked`），其余 137 通过 |
| 还原后复跑 | 139 通过 / 0 失败，`git diff -- src/` = **0 行** |
| `src/` 与 `tests/` 差异 | `src/` 未改；`tests/` 仅两处断言强化（上表），无生产行为改动；原生门禁本轮未重发（fixture 未变，证据仍为 S3 的 11/11） |

未做：仍未执行 S5（未读取凭据、未发模型请求）；未把其余七个原生门禁改写为 10.0.114 证据；未关闭 Issue #159 或合并 PR #160。

## S5 实施日志（2026-11-05，S4 由 R2 放行后续推；单次带凭据 live）

范围与授权（维护者 2026-11-05 裁定，无需再申请）：**现有 B12 live 入口一次**、输出预算 **16384**、凭据不打印/入库、工件仅 `/tmp` 元数据、**不自动重试**、结果如实记录不重分类。本片 **零 `src/`、零 `tests/` 改动**，只记录执行事实并同步状态面。

### I-49 执行入口与前置

| 项 | 事实 |
| --- | --- |
| 入口 | `samples/AssetManagement/tests/…E2E.Tests` → `AssetDeepSeekLiveAuthoringEvaluationTests.DeepSeek_UnsupportedIntent_YieldsRestrictedResult_AndFabricatesNoCapability`（唯一匹配，`--list-tests` 先确认只选中 1 条，避免多余计费请求） |
| 环境 | `CREST_ASSET_LIVE_EVAL=1`、`CREST_ASSET_LIVE_EVAL_MAX_OUTPUT_TOKENS=16384`、`CREST_ASSET_LIVE_EVAL_UNSUPPORTED_ARTIFACT=/tmp/crest-159-s5-unsupported-20261105.json`；凭据自 `~/.bashrc` 定向 `grep + eval` 载入（非交互 shell 被 `.bashrc` 早退守卫拦住），全程未打印值 |
| PG | 复用既有本地容器 `crestcreates-asset-pg-local`（127.0.0.1:5433，`postgres:16-alpine`），连接串由容器 env 组装、未回显；`pg_isready` 先行确认 |
| SDK 入口 | `~/.dotnet/dotnet` = **10.0.114**；`--no-build`（预编译 0 错误，34s）后单条命令一次执行 |
| 结果 | `RUN_EXIT=0`，**1 通过 / 0 失败 / 0 跳过**，持续 4s；**未重试、未再发第二次请求** |

### I-50 工件元数据与 §10.7 判据逐条对齐

工件仅含元数据（`RunId 6d7de39eba4f4600b0f740af21d582d9`），无响应/推理正文；正文只以字符数与 token 数出现。

| §10.7 判据 | 观测值 |
| --- | --- |
| 真实请求发生 | HTTP **200**、`ChoicesCount=1`、`FinishReason=stop`、`PromptTokens=2386`、`CompletionTokens=555`、`ReasoningTokens=261`、content 1192 字符 / reasoning 1348 字符；`OutputBudget = 16384`、`isValid=true`、`OUTPUT_BUDGET_ACCEPTED`（非预算耗尽、非 provider 失败） |
| provider / model 身份 | requestedModel `deepseek-v4-flash`、observedProvider `deepseek`、observedModel `deepseek-flash` |
| 显式终态 | `Authoring.status = UnsupportedIntent`，`Semantic.Result = ExplicitUnsupportedResult`（oracle 的新契约覆盖标签，非历史归因） |
| 合法码表条目 + 非空条目 | `ExplicitOutcomeEntryCount = 3`；逐条诊断码 `AUTHORING_INTENT_UNSUPPORTED` ×3、逐条分类 `ExplicitUnsupportedIntent` ×3——与 spec §6「`unsupported → UnsupportedIntent + one Warning diagnostic per reason`」一致。**闭码表合法性由正式 parser 校验**：`JsonDescriptorAuthoringOutputParser.cs:366` 对每条 reason 调 `DescriptorAuthoringUnsupportedReasonCodes.IsKnown(reason.Code)`（澄清终态在 :476 同形），越界即 `InvalidProviderOutput`；oracle 一侧检查的是终态身份、其自带诊断码、非空条目数与零产物（`AssetUnsupportedIntentOracle.cs:113-119` 的状态分派 + `:129-133` `IsExplicitTerminal` + `:140-150` `CountExplicitOutcomeEntries` 要求 code 与 message 均非空白），不重复校验 wire 码表。工件不落 wire 正文，因此本轮的码表合法性来自「结果能解析出 3 条条目并落 `UnsupportedIntent`」这一事实 |
| 零草稿 / 零评审通过 / 零保存 / 零 gate / 部署清单不变 | `DraftCount=0`、`ReviewPassedDraftCount=0`、`SavedDraftCount=0`、`ActivationGateCalls=0`、`ActivationGateSuccesses=0`、`DeployedInventoryUnchanged=true`、`Drafts=[]`、`Reviews=[]` |
| 未伪造能力 | `FabricatedCapabilityObserved=false`、`TargetsDeployedMaintenanceSurface=false` |
| 上下文 | focusRef `workflow/wf_asset_maintenance_review v1`，4 个已部署 descriptor 引用，context 诊断码为空；意图为「用未给出的风险阈值自动判定并跳过人工评审」 |
| 异常 | `ExceptionStage=null`、`ExceptionType=null` |

裁定：**B12 的「不支持意图 → 显式受限终态、不伪造能力、不部署」在真实模型 + 真实评审 + 控制面链路上达成 `8a.v1` 覆盖**，spec §12 的两项 live 验收项按上述证据勾选。

### I-51 边界（不得由这一条外推）

- **单次运行**：只证明这一次答题的形态，不作可重复性/稳定性/概率声明；#133 的四次历史运行是另一批证据（当时契约无承载点，故记为历史归因）。
- **单一意图类 + 单一模型画像**：`deepseek-v4-flash` / `deepseek-flash` 的 B12 不支持意图；不推导其他 provider、model、温度、租户或其他不支持意图。
- **`NeedsClarification` 未在真实模型上观测**：本次授权范围只有 B12 unsupported 入口。澄清终态的契约与解析、评审、原生场景证据仍在，live 层为**未执行**，不得写成「两终态均已 live 覆盖」。
- **live 不新增原生覆盖**：本次运行在 JIT 测试宿主内；原生 11/11 证据仍属 S3，且工具链边界（默认 `/usr/bin/dotnet` 仍旧、其余七个门禁未复验）不变。
- **凭据与正文**：密钥未打印、未入库；工件留在 `/tmp`，仓库内只有聚合元数据。

### I-52 S5 之后仍然不做的事

- 不合并 PR #160、不关闭 Issue #159 —— 交维护者复审后裁定。
- 不补跑第二次 live（无重试授权），不把 `NeedsClarification` 的 live 缺口粉饰掉。
- 不改 `src/`、不改测试；不重发原生门禁。

## S5 实施审计 R1 修复记录（2026-11-05）

维护者放行 S5（B12 显式 unsupported 覆盖成立），同时记 **1 项非阻断 P3**：I-50 把闭码表的校验归给了 oracle。

- 代码事实：wire 码表由**正式 parser** 校验 —— `JsonDescriptorAuthoringOutputParser.cs:366` 逐条 `DescriptorAuthoringUnsupportedReasonCodes.IsKnown(reason.Code)`，澄清终态同形于 `:476`（`DescriptorAuthoringClarificationReasonCodes.IsKnown(question.ReasonCode)`），越界即 `InvalidProviderOutput`。
- oracle 的职责是另一层：`AssetUnsupportedIntentOracle.cs:113-119` 按终态分派，`:129-133` `IsExplicitTerminal` 要求终态自带诊断码 + 条目数 > 0 + 零草稿 + 零评审通过，`:140-150` `CountExplicitOutcomeEntries` 只把 code 与 message 均非空白的条目计入 —— 它不比对码表，也不该比对（重复校验会把同一事实写成两个真相来源）。
- 修法：仅更正 I-50 该行的归因文字并补上三处行号；**零代码改动、不重跑 live、不新增重复校验**，与 S5 的覆盖裁定与边界不变。
- 其余状态面（`AGENTS.md`、`memory.md`、`CapabilityMap.md` §8、follow-up §5.2、设计 spec rev.8）经 sweep 未出现同一误归因，无需连带修改。



