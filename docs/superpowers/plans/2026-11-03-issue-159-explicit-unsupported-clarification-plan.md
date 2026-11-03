# Issue #159 实施计划 — 显式 unsupported / needs-clarification 终态

- 设计：[2026-11-03-explicit-unsupported-and-clarification-authoring-outcomes-design.md](../specs/2026-11-03-explicit-unsupported-and-clarification-authoring-outcomes-design.md)（rev.1，待静态审查）
- 基线：`master` = `9909365a`（PR #157 合并点）。#133 / PR #158 尚未合并，本计划里凡引用 Asset B12 用例与 oracle，均指该 PR 的 head `1db29f48`，其改动落在 S2 之后的分支重建步骤。
- 分支：`docs/issue-159-explicit-unsupported-clarification-contract-20261103-134406`（设计与日志；实现切片在同分支续推，或按维护者裁定另开 `feat/issue-159-*`）。
- 验证口径：CI 暂停，推送前本地全绿；支持声明只写已执行证据。

## 切片

| 切片 | 内容 | 关键文件 | 退出条件 |
| --- | --- | --- | --- |
| S0 设计 | 本 spec + 本计划 + Issue #159 | `docs/superpowers/specs、plans` | 静态审查通过（D1–D10 裁定落地） |
| S1 契约与解析 | wire `8a.v1`、`outcome` 判别式、封闭码表与边界、`DescriptorAuthoringStatus` + 两成员、结果新增两载荷类型、诊断码两枚、两个 source-generated JSON context、提示披露与 `PromptTemplateVersion` v3、全部 `7g.v1` 字面量迁移 | `Authoring.Abstractions/Authoring/*`、`Authoring/Parsing/*`、`Authoring/Prompting/*`、`Authoring/Json/*`、Authoring/Prompting/ControlPlane/Samples 测试 | 单测矩阵绿；变异 (a)(c)(d)(e) 各打红目标用例；`ReflectionFallback` 类检查仍成立 |
| S2 消费者与真实链路 | 样例 runner/fake agent 分支补全；Asset 真实链路：`unsupported` / `needsClarification` 两例（零草稿/零评审/零保存/gate 0/目录不变）+「散文拒绝仍不是澄清」负例；`AssetUnsupportedIntentOracle` 接受标签扩展（拒绝清单不变）；`AssetAuthoringDiagnosticClassifier` 归类；B12 live 入口按新终态裁定 | `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/*`、Asset E2E（先 rebase 到合并后的 master 或 PR #158 head） | 全量 Asset E2E 绿（需本地 PG）；oracle 仍拒绝一切非意图归因结果 |
| S3 原生门禁 | AotFixture 新场景 `AgentAuthoringClarificationOutcome` / `AgentAuthoringUnsupportedOutcome` / `AgentAuthoringOutcomeShapeConflict`，扩展 `AgentAuthoringWireDisclosure`（断言 `8a.v1`），per-scenario sentinel 与负控非零退出 | `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/Program.cs` + `.Tests` | `PublishAot` + 原生 run 在 linux-x64 通过；负控不产出总 sentinel |
| S4 文档与状态 | `usage-guide.md` 契约样例、`CapabilityMap.md` 四维、`memory.md`、`AGENTS.md` 当前平台状态 + AOT 分层、§5.1 B12 改判、#123 证据矩阵新行、7g spec 附录、本日志收口 | `docs/**`、`memory.md`、`AGENTS.md` | 声明与证据一一对应；未执行的场景写「未验证」 |
| S5 真实运行（可选门槛） | 带凭据 DeepSeek 跑 B12 一次，记录三终态之一；若模型仍散文拒绝则如实保持「候选缺口待验证」 | artifact 落 `/tmp`，不入库 | 裁定按实际输出改判，绝不放宽 oracle 凑绿 |

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
# 随后按 fixture 约定执行原生产物并核对 per-scenario sentinel

# 全量回归
dotnet build && dotnet test
```

## 已识别风险

| 风险 | 处置 |
| --- | --- |
| `contractVersion` 参与 prompt 输入哈希，版本升级会改变所有派生哈希；某些 fixture 可能钉了字面哈希 | S1 第一步先跑 `PromptInputHashTests`，逐个把字面量改为「同源重算」而不是放宽断言；禁止用跳过或宽松比较绕过 |
| 版本单值化（不留 `7g.v1` 兼容）会让未迁移的外部录制响应全部失败 | 本仓库内全部生产者/录制件在 S1 一并迁移；外部调用方不存在（`AuthorAsync` 无生产调用方，见 spec §2）；历史 review/plan 文档保留原文，不声称它们仍可回放 |
| 提示改动影响真实模型行为，可能仍不按新契约答题 | S5 明确允许「未达成」结论入库；不放宽判定，不把 `NoItems` 猜成澄清 |
| PR #158 与本计划共用 Asset 文件，存在合并顺序冲突 | S2 开始前先把 #158 合并或把本分支 rebase 到其 head，顺序由维护者裁定；不在两边各做一份 oracle |
| 新 outcome 类型漏出 JSON root 会导致原生路径反射失败（而非红测试） | S3 场景必须逐类型断言 `JsonSerializerContext` 可解析，并保留 `ReflectionFallback_IsDisabled` 类总门 |

## 日志约定

实施期在本文件追加 `I-01…` 逐条记录：事实（含文件:行）、决策与偏离、当次真实命令尾部输出（计数不是形容词）、变异判别力证据（红→绿）、未做的事。审查轮（R\*）按维护者节奏另起小节，不覆写历史。
