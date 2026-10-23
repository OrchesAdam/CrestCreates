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

### I-03 S2 守卫落点与实现口径（2026-10-23）

新增 `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/`：
`TenantIdentityArchitectureTests.cs` 与 `TenantIdentity/{TenantIdentityPorts, TenantIdentityProjectInventory, TenantIdentitySymbols, TenantIdentityFlow, TenantIdentityAnalyzer, TenantIdentityHelperIndex, TenantIdentitySyntheticFixture, TenantIdentityAuthoritativeLedger}.cs`。

- 真实主链口径：按项目边界逐一编译（Compile 源 = 项目目录 `.cs` 排除 `bin/obj`；引用 = 共享框架 + 该项目 `bin` 构建闭包 + `project.assets.json` 的 compile 资产，按程序集简单名去重；生成物 `obj/{cfg}/{tfm}/{generated,source-generators}` 只参与符号解析，不作为扫描对象）。端口表与规则全部是测试内部代码，生产侧零改动。
- CS0433 教训：早期未去重时 `Microsoft.AspNetCore.Http.Abstractions` 2.3.9 与 10.0.0 并存，导致整个文件所有类型绑定失败、误报 600+ TI900；去重顺序（共享框架 > 项目 bin > 包资产）后归零。同类：`_._` 占位资产必须剔除。
- 名称源按成员身份：`ITenantInfo.Name`、`CrestCreates.Domain.Permission.Tenant.{Name,NormalizedName,DisplayName}` 与任何名字归一化后为/以 `tenantname` 结尾的符号；裸 `.Name`（如 Widget）不报。
- 身份出口：显式端口表（ChangeAsync/SetTenantId/Success/SetTenantContext/三类 CacheKey）+ 通用网（CrestCreates 声明的任何名字为 `*TenantId` 的参数），新 API 不会静默成为未守卫的出口。
- 值流：括号/转换/await/`!`、`?.`、`??`、三元、插值与 `+` 拼接、`ToString/Trim/ToLower*` 传递、局部与字段别名（流不敏感不动点 6 轮）、同项目 helper 一层内联（参数→返回 + 局部写入），跨项目第一方 helper 经声明索引用其**真实源码**内联；内联失败或外部 helper 可能承载名称时报告 TI900，不判干净。
- 权威结构断言（§4.2 第 3 部分）：9 行锁定表，要求出口参数的终端值名确实来自 `Id`/`TenantId`/`ResolveTenantIdOrNull`；缺失该行即失败，防止「门禁空跑」。
- `nameof(...)` 常量、`throw` 表达式、`switch` 表达式、隐式 `new(...)`（真实 `IdentityClaimsBuilder` 用的是隐式形）均已覆盖；解构声明 `var (a, b) = ...` 的别名传递仍是已声明限制，写入测试说明。

### I-04 S2 验证结果（2026-10-23，基线 `348c4d3c` + S1 提交 `8dfe83f5`）

| 用例 | 结果 |
| --- | --- |
| 真实仓库门禁 TI01/TI02/TI03 | 通过；枚举 156 个 active 非测试项目、2586 个 Compile 源、98 个项目实际编译、3139 个第一方 helper 成员入索引、1024 个身份出口、0 条诊断（既无违规也无覆盖失败） |
| TI-C01a/b/c/d | 通过：项目/文件计数与独立枚举一致；八个端口在真实仓库均有观测（Success ≥5）；不可绑定的敏感出口必须出 TI900；缺构建输入出 TI900 |
| TI-P01/P02 | 通过：展示/日志/DTO/注释、普通对象 `.Name`、名称查目录后取 Id、id+name 双通道均不误报 |
| TI-N01/N02/N03 | 通过：8/6/6 条命中，规则分类为 TI001（上下文与 claim）/TI002（权限域、缓存前缀、实体过滤、成员写）/TI003（身份 getter、返回契约、构造 Id 槽、身份名局部变量被赋名称）；含别名、Trim、插值、`??`、named argument、`using` 别名、跨文件 helper 与跨项目 helper 规避形态 |
| TI-M01 | 通过：隔离副本把 resolver 首参数改为 `tenant.Name` → TI001 命中该出口且权威结构断言失败；副本仍可编译（无 TI900） |
| TI-M02 | 通过：claim 值改 `context.UserName` → 权威断言失败；`ResolveAuditCacheKey` 首参数改 `_currentTenant.Tenant?.Name` → TI002 + 权威断言失败 |
| TI-B01 | 通过 7/7：`tests/Framework/Ddd/CrestCreates.Application.Tests/Tenants/TenantIdentityDirectoryResolutionTests.cs` 使用正式 `HeaderTenantResolver`/`CurrentTenant`/`MultiTenancyMiddleware`/`IdentityClaimsBuilder`/`TenantCacheKeyContributor`，覆盖 GUID 头、名称头、名称本身是 GUID（id 查不到即 fail-closed，不退回名称）、重命名不改分区键、相似名称分区相异、停用租户不进身份槽、中间件帧内 `Id` 为实体 Id 且退出后恢复 |

已声明限制：仓库级有限模式守卫，不证明任意跨程序集/动态拼接/外部插件数据流；生成器模板中的字符串不作为已绑定运行时符号，实际生成端点由既有生成器/消费 fixture 回归覆盖。

### I-05 待办与开放项

- S3 归档动作、准入/覆盖/隔离守卫未开始；`.github/workflows/*.yml` 与 ProcurementApproval slnx 的 Draft 输入须在 S3 清理（CI 保持人工禁用状态，不改启用）。
- S4 全量构建/边界/回归验证与 AGENTS/memory/比较文档同步未开始；本轮不提升任何支持声明。
- 基线构建注意：`dotnet build CrestCreates.slnx` 在本 worktree 首次运行因 `tests/Runtime/Agent/CrestCreates.Agent.Memory.TypeForwardLegacyConsumer/obj/project.assets.json` 缺失而报 NETSDK1004（该项目不在 slnx 成员中，仅被 Projection.Tests 引用）；单独 `dotnet restore` 后根构建 EXIT=0、0 error。须在 S4 作为预存条件精确记录。
