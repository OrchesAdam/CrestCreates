# Issue #131 实施日志 — 能力地图、长尾裁定与 TenantId 架构守卫

设计：[功能设计](../specs/2026-10-23-issue-131-capability-map-tenant-id-guard-design.md)
计划：[实施计划](2026-10-23-issue-131-capability-map-tenant-id-guard-implementation.md)
交付：[能力地图](../../Feature/CapabilityMap.md)

## 基线

| 项 | 值 |
| --- | --- |
| 实施基线 SHA | `348c4d3c35b8682e6b47b6c2dd6058b046c1113a`（= `origin/master`，2026-10-23 fetch 核对） |
| 分支 | `feat/issue-131-capability-map-tenant-id-guard-20261023-174957` |
| SDK | .NET 10.0.100（`global.json`，`rollForward: latestMinor`） |
| 设计基线 | 本地 master `8c8a71de`（PR #151 合并） |
| 基线核对 | 设计 §1 声明 `8c8a71de → 348c4d3c` 范围不变；本轮在 `348c4d3c` 重新采集复核，见 I-01 |

## 逐条记录

### I-01 S1 重新盘点 15 项目（2026-10-23）

方法：脚本遍历 worktree（排除 `bin/obj/99_RecycleBin`），逐 `.cs` 计算 SHA-256；解析全仓 `.csproj` 的 `ProjectReference` 得到直接消费者；解析 `CrestCreates.slnx` 与 `solutions/*.slnx` 的 `<Project Path>`（相对各 slnx 目录归一）得到方案成员；结果与设计 `docs/review/2026-10-23-issue-131-design-inventory.json` 逐项比对。

结果：`TOTAL_DIFF 0` — 15 项目的源码文件集合与 SHA-256、直接 ProjectReference 消费者、方案成员关系全部一致；源码文件合计 153（与 15 项目 `source_file_count` 之和相同）。设计裁定适用于实施基线 `348c4d3c`，无需改判。

设计 §5 步骤 1 要求的补充采集（超出设计清单的 build 输入）在本轮一并补齐：

| 归档候选 | 额外输入（设计清单未列明的部分） |
| --- | --- |
| CrestCreates.Draft.Abstractions | `samples/ProcurementApproval/CrestCreates.Sample.ProcurementApproval.slnx:45` 项目成员 |
| CrestCreates.Draft / Draft.Abstractions | `.github/workflows/ci.yml:180`、`.github/workflows/full-validation.yml:116` 运行 `CrestCreates.Draft.Tests` |
| CrestCreates.Agent.Runtime | `tests/Boundary/…/DependencyBoundaryTests.cs:64,258`、`RuntimePersistenceArchitectureTests.cs:66` 以禁止片段形式引用其路径（守卫，非消费者） |
| CrestCreates.EventBus.EventStore | `src/Runtime/Eventing/CrestCreates.EventBus.Abstract/IEventStore.cs` 三个契约仅由本包实现/消费，`IEventStore` 全无实现 |
| 全部 7 项 | `Directory.Build.props` / `Directory.Build.Aot.props` / `CrestCreates.Modules.props` 均按 glob 与 `MSBuildProjectName` 条件工作，无按名枚举；7 个归档候选都没有 `IsPackable=true`（全仓 packable 仅 `Agent.Memory.Projection`、`Agent.Memory.ReadCore`、`JsonContracts.BuildTasks` 三项与 `CrestCreates.BuildTasks.nuspec`，均不在 #131 范围） |

### I-02 S1 能力地图落盘（2026-10-23）

`docs/Feature/CapabilityMap.md`：§1 四维记录与证据纪律；§2 十二条裁定条目（D-01…D-12，覆盖 15 项目，逐条含定位、唯一装配入口、真实调用点带 file:line、功能状态、执行证据与限制、责任与退出/升级条件）；§3 归档与准入；§4 全量 active `src/**/*.csproj` 项目索引（135 行，机器可读区，D-xx 行 15 条、其余标「导航（本轮未重评，不背书）」）+ §4.1 归档索引区；§5 非 src 项目作用说明；§6 与既有文档关系。

索引区由脚本按当前文件系统重算后写入标记区，S3 归档后重跑即自动把归档项移出 active 索引、落入归档索引，避免手工漂移。

链接自检：全部相对链接可解析；仅两个前向链接（`PluginSystemIsolationArchitectureTests.cs`、`ArchivedProjectRegressionArchitectureTests.cs`）在 S3 落地前不存在，属计划内。

### I-03 待办与开放项

- S2 `TenantIdentityArchitectureTests`（含 TI-P/N/C/B/M 全部正反例与变异控制）未开始。
- S3 归档动作、准入/覆盖/隔离守卫未开始；`.github/workflows/*.yml` 与 ProcurementApproval slnx 的 Draft 输入须在 S3 清理（CI 保持人工禁用状态，不改启用）。
- S4 构建/边界/回归验证与 AGENTS/memory/比较文档同步未开始；本轮不提升任何支持声明。
