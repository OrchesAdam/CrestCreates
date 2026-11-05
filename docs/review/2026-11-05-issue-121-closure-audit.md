# Issue #121 关闭核对 — 2026-11-05

核对仓库：`OrchesAdam/CrestCreates`。远端 master：`64aeb1bd2fa3dab23c410c1b672ebb4034797cc4`（PR #161）。
本地共享 checkout 为 `348c4d3c`，存在用户已有修改；本轮使用 GitHub connector 读取远端事实，
未用旧 checkout 或未跟踪审查文件证明当前交付，未覆盖本地工作树。

## 结论与关闭门

#121 原工作表 #122–#132、补充 #137/#146 全部 closed/completed；逐个确认其实现 PR 的
`merged=true`、合并 SHA 和完成证据。B12 后续 #133/#159 也已合并完成。
没有发现本轮范围内仍待实现的运行主链任务。

**尚未满足的唯一关闭门是状态文档与 master 对齐。** 初次读取远端时，memory/AGENTS 仍有 #132/#133 待合并等旧状态；核查期间 PR #161
合并，已同步其中一部分，本分支随后基于该合并重整，保留其新增内容。
剩余 memory 的 Delivered capabilities 仍写 #132 待合并、Known gaps 仍写 #131 待做，
原生矩阵 #159 行仍写 S5 pending，且 #161 把 #132 的合并日期误写成 2026-11-03
（GitHub PR 元数据为 2026-10-30）。本次只修订这些状态并补齐总控交付记录，不扩大支持声明。
本报告所在 PR 合并后，按本轮用户授权回填 #121 清单并以 completed 关闭；合并前保持开放。
仓库 memory 的 “GitHub PR flow, never auto-merge” 与 #121 的“不授权自动合并”继续适用。

## 逐项交付映射

以下 Issue 均已读回为 completed，PR 均已读回为 merged。计数和运行事实为各项交付时记录，
不是本轮重新执行结果。

| Issue | 交付 PR | Merge SHA | 核对的结果与证据 |
| --- | --- | --- | --- |
| [#122](https://github.com/OrchesAdam/CrestCreates/issues/122) | [#134](https://github.com/OrchesAdam/CrestCreates/pull/134) | `85bdbb01` | 基线修订、PR 吸收映射与 B12 有界裁定 |
| [#123](https://github.com/OrchesAdam/CrestCreates/issues/123) | [#135](https://github.com/OrchesAdam/CrestCreates/pull/135) | `e27aaf77` | HTTP 原生请求正负例、失败传播/回收、MCP/Agent.Tools 接线与矩阵 |
| [#124](https://github.com/OrchesAdam/CrestCreates/issues/124) | [#136](https://github.com/OrchesAdam/CrestCreates/pull/136) | `988bfd2f` | 唯一 typed UoW 装配、真实 DI/SQLite 生命周期与原生工厂门禁 |
| [#137](https://github.com/OrchesAdam/CrestCreates/issues/137) | [#139](https://github.com/OrchesAdam/CrestCreates/pull/139) | `1dc4a9cc` | 执行内核/传播/资源身份、ORM 124/124、PG 4/4、native 1/1 |
| [#125](https://github.com/OrchesAdam/CrestCreates/issues/125) | [#140](https://github.com/OrchesAdam/CrestCreates/pull/140) | `d4202e5e` | 唯一 CAP 入口、配置校验、PG+RabbitMQ 原子发布；单元 41/41、集成 38/38 |
| [#126](https://github.com/OrchesAdam/CrestCreates/issues/126) | [#142](https://github.com/OrchesAdam/CrestCreates/pull/142) | `1cad6e35` | typed dispatch/replay、JSON 闭包、native 五场景；后由 #146 补强 |
| [#127](https://github.com/OrchesAdam/CrestCreates/issues/127) | [#145](https://github.com/OrchesAdam/CrestCreates/pull/145) | `5d3de8c5` | 静态订阅、canonical codec、真实 broker 37/37、trim/native 双门 |
| [#146](https://github.com/OrchesAdam/CrestCreates/issues/146) | [#148](https://github.com/OrchesAdam/CrestCreates/pull/148) | `0c6e574a` | Host 隔离/单次准备/稳定键/治理与租户恢复、native 11 场景、PG 升级 |
| [#128](https://github.com/OrchesAdam/CrestCreates/issues/128) | [#150](https://github.com/OrchesAdam/CrestCreates/pull/150) | `196b6f66` | CRUD precompile → JsonContracts → ExplicitHost，native 15 场景及负控 |
| [#129](https://github.com/OrchesAdam/CrestCreates/issues/129) | [#152](https://github.com/OrchesAdam/CrestCreates/pull/152) | `2f4c5402` | 旧 Controller/Legacy 归档、Ticket 正式投影、native 15 场景及控制 |
| [#130](https://github.com/OrchesAdam/CrestCreates/issues/130) | [#154](https://github.com/OrchesAdam/CrestCreates/pull/154) | `348c4d3c` | Provider 支持矩阵与软删除；ORM 267/267，按数据库/查询/写入分别限界 |
| [#131](https://github.com/OrchesAdam/CrestCreates/issues/131) | [#155](https://github.com/OrchesAdam/CrestCreates/pull/155) | `9c39e187` | 15 项目/12 裁定、7 项目归档、TenantId 有限模式守卫及真实链路 |
| [#132](https://github.com/OrchesAdam/CrestCreates/issues/132) | [#157](https://github.com/OrchesAdam/CrestCreates/pull/157) | `9909365a` | 有界设计及 PG retention V1、JIT admission 与原生九场景/负控 |
| [#133](https://github.com/OrchesAdam/CrestCreates/issues/133) | [#158](https://github.com/OrchesAdam/CrestCreates/pull/158) | `91d6574e` | B12 确定性负例与 live 事实；当时的契约缺口交给 #159 |
| [#159](https://github.com/OrchesAdam/CrestCreates/issues/159) | [#160](https://github.com/OrchesAdam/CrestCreates/pull/160) | `1bf4a7e7` | 8a.v1 两显式终态、139 单测、Asset 128+2 skip、native 11/11、单次 live |

状态同步：#146 → PR #149（`de50b337`）；#128 → PR #151（`8c8a71de`）；
#131 → PR #156（`5f4cfc8f`）；#133/#132 → PR #161（`64aeb1bd`）；#159 → PR #162（`95ae8374`）。
#132 超出本总控“有界设计裁定即可”的最低交付，已交付的 V1 范围不应被扩大为完整治理系统。

## 证据边界与明确保留项

- 正式原生证据按 [#123 矩阵](2026-10-10-issue-123-native-execution-gates-evidence-matrix.md)
  逐场景/RID/Provider 解释；HTTP fixture 不证明完整认证/租户/bootstrap。
- [ORM 矩阵](../Feature/OrmProviderSupportMatrix.md) 是唯一组合支持来源；
  不由 UoW/CRUD fixture 推导整个 EF/FreeSql/SqlSugar SDK 的 NativeAOT 能力。
  CAP 的 trim/NativeAOT、SqlServer/Kafka，以及跨库原子性等均未因本总控关闭得到支持。
- [能力地图](../Feature/CapabilityMap.md) 保留实验/延期项，不把目录存在写成能力已交付。
  [审查修订 §5.2](2026-10-10-framework-architecture-review-follow-up.md) 与 #159 单次 live
  相符；真实模型 clarification、其他模型/意图、可重复性仍未验证。
- CI 按维护者决定保持人工禁用；历史接线/历史成功与当前本地验证分开列账。
  #159 用 SDK 10.0.114 / ILCompiler 10.0.14 验收；其余七项原生门禁未在该版本复验，
  系统 SDK NU1903、全方案测试环境与既有未完成基线核验仍按 #159 交付记录独立处理，
  不将其伪装为本轮重跑通过，也不重新定义为 #121 新验收项。
- #87/#88 应用侧 durable activation/CAS/replay 等裁定不重开；LocalEvent/RabbitMQ
  的重放并发互斥、崩溃恢复、per-handler checkpoint 和 exactly-once 等未承诺项保持原边界。

## 本轮核查方式

读取 #121 正文/评论、15 个相关 Issue 及完成评论、对应 20 个交付/同步 PR 元数据，
并读取远端 master 的 memory、AGENTS、能力地图、ORM 矩阵、#123 矩阵与审查 follow-up。
纯文档状态修订：检查 diff/空白、逐项匹配 Issue completed 和 PR merged/SHA、
确认只修改状态与补充核对记录；未运行 dotnet build/test/native publish，未启用 CI，
未调用付费模型、未操作保留资产、未合并 PR。
