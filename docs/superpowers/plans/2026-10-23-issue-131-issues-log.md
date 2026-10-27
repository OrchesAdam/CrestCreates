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
| 真实仓库门禁 TI01/TI02/TI03 | 通过；枚举 156 个 active 非测试项目、2586 个 Compile 源、98 个项目实际编译、3139 个第一方 helper 成员入索引、1024 个身份出口、0 条诊断（既无违规也无覆盖失败）。**注**：这组数字是 R1 之前的目录近似口径；R1-01 修复后按真实 Compile 集合重算为 149 项目 / 2587 源（含 11 个 linked 源）/ 1034 个出口，见 I-09 |
| TI-C01a/b/c/d | 通过：项目/文件计数与独立枚举一致；八个端口在真实仓库均有观测（Success ≥5）；不可绑定的敏感出口必须出 TI900；缺构建输入出 TI900 |
| TI-P01/P02 | 通过：展示/日志/DTO/注释、普通对象 `.Name`、名称查目录后取 Id、id+name 双通道均不误报 |
| TI-N01/N02/N03 | 通过：8/6/6 条命中，规则分类为 TI001（上下文与 claim）/TI002（权限域、缓存前缀、实体过滤、成员写）/TI003（身份 getter、返回契约、构造 Id 槽、身份名局部变量被赋名称）；含别名、Trim、插值、`??`、named argument、`using` 别名、跨文件 helper 与跨项目 helper 规避形态 |
| TI-M01 | 通过：隔离副本把 resolver 首参数改为 `tenant.Name` → TI001 命中该出口且权威结构断言失败；副本仍可编译（无 TI900） |
| TI-M02 | 通过：claim 值改 `context.UserName` → 权威断言失败；`ResolveAuditCacheKey` 首参数改 `_currentTenant.Tenant?.Name` → TI002 + 权威断言失败 |
| TI-B01 | 通过 7/7：`tests/Framework/Ddd/CrestCreates.Application.Tests/Tenants/TenantIdentityDirectoryResolutionTests.cs` 使用正式 `HeaderTenantResolver`/`CurrentTenant`/`MultiTenancyMiddleware`/`IdentityClaimsBuilder`/`TenantCacheKeyContributor`，覆盖 GUID 头、名称头、名称本身是 GUID（id 查不到即 fail-closed，不退回名称）、重命名不改分区键、相似名称分区相异、停用租户不进身份槽、中间件帧内 `Id` 为实体 Id 且退出后恢复 |

已声明限制：仓库级有限模式守卫，不证明任意跨程序集/动态拼接/外部插件数据流；生成器模板中的字符串不作为已绑定运行时符号，实际生成端点由既有生成器/消费 fixture 回归覆盖。

### I-05 S3 归档执行（2026-10-23）

按设计 §5 固定顺序执行，未跳过任何输入采集：

1. 引用与方案输入清理（36 条 slnx 成员、1 条 Workflow→Draft.Abstractions、1 条 Workflow.Tests→Draft、2 条 CI Draft.Tests 步骤行、`samples/ProcurementApproval/CrestCreates.Sample.ProcurementApproval.slnx` 1 条 Draft.Abstractions 成员）。CI 仍保持人工禁用，本轮不改启用状态。
2. 移动到 `99_RecycleBin/issue-131-capability-map/`，保留原相对路径：7 个生产项目 + `tests/Metadata/Draft/CrestCreates.Draft.Tests` + 无消费者的 `CrestCreates.EventBus.Abstract/IEventStore.cs`（共 8 个 csproj + 1 契约文件）；`bin/obj` 随目录留痕，未删除任何文件。
3. `EventBus.Abstract` 其余在用契约保留在 active 目录，未按目录整包搬走；`IEventStore` 全无实现的断言由 `EventBusAbstractions_KeepTheirLiveContracts_ButNotTheArchivedStoreOnes` 锁定。
4. 归档 README 记录每项裁定、替代方向与外部消费者未知披露。
5. 既有防复活守卫 `ControlPlaneReferenceDataKernelArchitectureTests`（PG 不得注册旧 `IDraftStore`）保留；`DependencyBoundaryTests`/`RuntimePersistenceArchitectureTests` 中以禁止片段引用 `CrestCreates.Agent.Runtime` 的断言保留为守卫。

新增守卫：
- `CapabilityMapCoverageArchitectureTests`（5 例）：active `src/**/*.csproj` 与地图索引集合等价、无重复、路径真实、每行有定位/裁定；12 条裁定条目齐备且保留侧 8 项目、归档侧 8 行（7 生产 + 1 测试）与回收站实际内容一致；地图相对链接（含 `#L` 锚点剥离）全部可解析。
- `ArchivedProjectRegressionArchitectureTests`（5 例）：active 项目引用、方案成员、源码命名空间与模块名不得回到归档项；归档内容按原相对路径可定位且 README 存在；回收站不得成为任何编译/打包输入。白名单只允许守卫文件自身与历史文档提及旧标识符。
- `PluginSystemIsolationArchitectureTests`（3 例）：生产链（src + samples 非测试项目）对 PluginSystem 引用为零即隔离成立；`PluginManager` 仍使用 `AssemblyLoadContext`/`GetTypes`/`ActivatorUtilities`，JIT-only 声明不失真；地图 D-12 必须同时含 experimental、JIT-only、未验证、限界保留。

索引重建：归档后 active `src` 项目由 135 → 128，地图 §4 与 §4.1 由脚本按当前文件系统重算，避免手工漂移（I-02 已记录该做法）。

### I-07 S4 验证与文档闭环（2026-10-23）

| 类别 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build CrestCreates.slnx` | EXIT=0，0 error |
| 构建 | `dotnet build solutions/CrestCreates.All.slnx` | EXIT=0，0 error，5 warning（既有） |
| 构建 | Framework / Metadata / Platform / Runtime / Integrations 分层 slnx | 全部 0 error |
| 边界 | `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests`（podman socket） | 205/207 |
| 预存失败 | `ControlPlaneReferenceDataSpecTestSkeletonTests.EvidenceLedger*` ×2 | 在 `/tmp/crest131-baseline`（纯净 `348c4d3c`，不含任何 #131 改动）复现同一 2 条失败（176/178）→ 判定为基线预存，不是本轮回归；#131 净增 29 条通过用例 |
| 保留内核回归 | Workflow 100/100、DescriptorDraft 132/132、Organization 223/223、Localization 12/12、FileManagement 24/24、Web 102/102 | 全绿；Workflow 的 Draft 引用移除后无行为变化 |
| 租户行为 | `dotnet test …Application.Tests --filter Tenants|Identity` | 102/102（含新增 7 条 TI-B01） |
| TenantId 门禁 | 同上边界套件内 16 条 | 全绿；真实仓库 0 违规 / 0 覆盖失败（归档后活动项目 128） |
| 故障控制 | 删除地图一行 / 给 ModuleDiagnostics 注入 PluginSystem 引用 | 分别使覆盖门禁与隔离门禁失败；恢复后 `git status` 干净且两组守卫重新全绿。第一次注入用错相对路径深度（指向不存在目录）未触发门禁，纠正后生效——无效尝试如实记录 |
| 原生 | 未运行 | 本轮只触及文档、测试守卫与无消费者归档，不提升任何 `NativeAOT-verified`/`trimming-verified` 声明，也不新造空壳 fixture |
| CI | 未变更 | 三个 workflow 维持人工禁用 |

文档闭环：`docs/Feature/CapabilityMap.md`（状态改「归档（S3 已执行）」、归档证据链接重指向回收站、索引与归档索引按文件系统重建）；`AGENTS.md`（项目结构、依赖图、平台能力新增能力地图与 TenantId 主键契约两条、参考位置、最后更新日期）；`memory.md`（以维护者本地内容为基，新增 #131 实施段与 Wave 3 状态）；`docs/review/crestcreates-abp-comparison.md`（顶部失效横幅 + PluginSystem「生产就绪」两处具体撤回并链接 D-12）；`docs/Feature/OrmProviderSupportMatrix.md`（页首「尚未合并」改为 merge SHA `348c4d3c`，并声明其为 ORM 唯一来源）；设计文档追加 §7 实施记录（正文不改写）；实施计划状态行更新；证据落 `docs/review/2026-10-23-issue-131-implementation-evidence.json`。

### I-09 R1 审查修复（2026-10-24）

审查 head `40bf53eb`，8 项 P2 全部成立，逐一修复并补回归（同一分支，未合并）：

| 发现 | 根因 | 修复 | 回归 |
| --- | --- | --- | --- |
| R1-01 Compile 输入与条件编译 | `Load` 只递归枚举项目目录，且 parse options 未带 `DefineConstants`；`CrestCreates.CrudGeneration`/`HttpProjectionGeneration` 无自身 csproj，11 个 linked 源完全未被扫描 | 新增 `TenantIdentityCompileInputs`：默认 glob − 显式 `Compile Remove` + 显式 `Compile Include`（含目录外 linked 源）；常量按实际配置/TFM 推导（SDK implicit defines + 项目自有），`CSharpParseOptions` 带真实符号 | `TI_C01f`（linked 源必须被扫描）、`TI_C01g`（Remove 不得扫描）、`TI_C01h`（`#if DEBUG` 命中、`#if RELEASE` 不命中）、`TI_C01i`（3 个代表项目的 Compile 集合与 `DefineConstants` 与 `dotnet msbuild -t:AddImplicitDefineConstants -getItem:Compile -getProperty:DefineConstants` **逐项相等**）、`TI_C01a` 期望改为同一解析器 + 独立下界 |
| R1-02 局部别名丢 Undecidable | 环境只存 `bool`，写入只保留 `IsNameTainted` | 环境改存完整 `TaintValue`（taint/undecidable/origins），写入按合并语义保留最强状态 | `TI_N04`（直接调用 1 条 + 局部别名 1 条 + 再赋值 1 条，共 3 条 TI900） |
| R1-03 helper 局部赋值 | InlineBody 只把 initializer 与 return 合并，未处理赋值 | InlineBody 用替换后的参数状态对 helper 自身的声明与赋值做不动点，再只汇总 return；不再把任意 initializer 当返回值 | `TI_N05`（`key=""` 后 `key=tenant.Name` 的 helper 命中 2 条；正确解析目录并返回 Id 的 helper 不误报） |
| R1-04 构造命名参数 | Claim/TenantInfo 构造按书写位置取参数 | 改用 `IObjectCreationOperation` 的 `argument.Parameter` 映射（含显式/隐式 new、named/optional） | `TI_N06`（`new Claim(value:name, type:"tenant_id")`、`new TenantInfo(name:id, id:name)` 均命中） |
| R1-05 ITenantInfo.Id 写入 | 只看 `TenantId`/`CurrentTenantId` 名称 | 按属性所属类型/接口成员身份识别 `ITenantInfo.Id`，覆盖对象初始化与后续赋值，规则归 TI003 | `TI_N07`（对象初始化写入 + 直接赋值各 1 条；正确赋 Id 不误报） |
| R1-06 权威来源只看变量名 | 终端名匹配，别名即可放行 | 值流携带**来源集合**（成员/参数/方法 + 局部别名追溯），9 行锁定表改为核对来源集合；`string.Empty` 等静态常量与对象级污点不参与 | `TI_M03`（把 `context.UserName` 赋给局部 `tenantId` 再写两条 claim：TI01/TI03 仍绿，但 ledger 两条都失败）；真实仓库 ledger 仍 9/9 通过 |
| R1-07 Linux 反斜杠引用 | 直接把 `Include` 交给 `Path.Combine`，反斜杠成普通字符 | 新增 `ArchitecturePath.TryResolve`（先归一化分隔符、跳过 `$( )` 表达式），隔离与归档守卫共用 | `BackslashAndForwardSlashReferences_AreEquivalent`（同一目标两种写法都必须命中）、`BackslashReferences_AreResolvedBeforeComparison` |
| R1-08 证据链 | 盘点 JSON 未提交；设计 §7 三处链接错误 | 提交 `docs/review/2026-10-23-issue-131-design-inventory.json`；修正 §7 两处路径与三个归档源码链接 | `Issue131EvidenceLinks_AllResolve`（地图 + 设计 + 计划 + 日志四份文档的相对链接全部可解析，且两份证据 JSON 存在） |

复现审查者的三处可编译注入：`TI_M03`（用户名经 `tenantId` 别名进 claim）、`TI_M04`（真实 Caching 项目内新增 `#if DEBUG` 的身份误用）、`TI_M05`（linked `CrudGeneration` 目录内新增身份误用）现在全部命中。作者侧记录：`BackslashAndForwardSlashReferences_AreEquivalent` 首次编写时把 `..` 深度写错（`src/Consumer` 只需一级回退），导致正斜杠用例未命中；修正后两例均命中，此无效尝试如实保留在本节。

### I-10 R2 审查修复（2026-10-24）

审查 head `adbbf5dd`，3 项 P2 全部成立，已修复并补回归（同一分支）：

| 发现 | 根因 | 修复 | 回归 |
| --- | --- | --- | --- |
| R2-01 条件 Compile 项被无条件执行 | 推导器只读 `Include`/`Remove` 属性，忽略元素与父 ItemGroup 的 `Condition`；Release-only 的 Remove 在 Debug 下也被应用 | 放弃推导，改为**直接读取 MSBuild evaluated 输入**（`dotnet msbuild -t:AddImplicitDefineConstants -getItem:Compile -getProperty:DefineConstants/-getProperty:TargetFramework`，按项目+配置缓存，缓存键含 csproj、共享 props/targets、项目内 props/targets 与文件清单）；求值失败即 `ReferenceError` → TI900，不顺延为「空集合」；条件、导入、属性函数交给 MSBuild | `TI_C01j`（Debug 包含被 Release-only Remove 的文件、Release 排除，且两种配置的常量分别为 `DEBUG`/`RELEASE`）；`TI_C01f/g/i` 在新输入源下继续通过；冷启动 149 项目约 90–120s，缓存后整条矩阵 1m18s |
| R2-02 非空常量 fallback 丢来源 | 所有字面量返回 Clean 且无来源，`?? "acme-east"` 与 `?? string.Empty` 的来源集合相同 | 区分中性常量（`null`/`default`/空字符串/`throw`）与非空字符串常量；后者保留 `literal:"<值>"` 来源，非权威分支不再被权威分支净化 | `TI_N08`（三类 fallback 的来源集合差异）、`TI_M06`（真实 `IdentityClaimsBuilder` 两条 claim 改 `?? "acme-east"` → ledger 两条都失败） |
| R2-03 透传 helper 被误报 | `InlineBody` 已把实参状态写入形参，但 `EvaluateSymbol` 又把形参自身的声明角色合并进来源 | 记录「已被实参替换的形参」集合，这些形参不再贡献自身来源（调用方来源胜出）；未被替换的顶层形参保留角色（TI002 的 `tenantId` 参数行仍依赖它） | `TI_N09`（直接属性 / 局部别名 / 透传 helper 三者来源集合完全相同）、`TI_M07`（真实 `IdentityClaimsBuilder` 加入 `Identity(string?)` 透传后 ledger 与违规均为空） |

实现过程中由新回归发现的自有缺陷（如实记录）：evaluated-input 缓存的键最初未包含项目绝对目录，导致复制出来的 scratch/变异副本可能命中另一棵树的 Compile 清单；`TI_M05` 在改造成实体副本后暴露该问题，已把绝对目录加入键。

R1 与 R2 的判据冲突处理：R2-03 要求「透传不新增来源」，R1-06 要求「UserName 别名不得放行」，二者由「替换后的形参不贡献自身来源、但保留调用方来源」同时满足——`TI_M03`（UserName→局部 tenantId）仍被拒绝，`TI_M07`（Identity 透传）通过。

### I-11 R3 审查修复（2026-10-24）

审查 head `0cc8ef9b`，4 项 P2 全部成立，已修复并补回归：

| 发现 | 根因 | 修复 | 回归 |
| --- | --- | --- | --- |
| R3-01 热缓存漏掉项目外 linked glob 的新增文件 | 缓存键只覆盖项目目录内的 `.cs` 清单，目录外 `Compile Include` 的搜索范围不在键内 | 键新增「项目外 Include/Remove 搜索根」的树签名（根/目录 mtime + `*.cs` 名称/长度/mtime）；读取缓存后校验所有缓存路径仍存在，缺失即重新求值 | `TI_C01k`（预热后新增 linked 文件必须进入清单、删除后必须消失） |
| R3-02 求值失败因 Sources 为空被过滤 | 分析器仅在 `ReferenceError != null && Sources.Any(敏感候选)` 时报 TI900 | 在任何依赖 Sources 的筛选之前无条件报告覆盖失败（`AnalyzeRepository` 与 `AnalyzeProject` 同口径） | `TI_C01l`（合法 SDK 项目 + 有效 bin + 注入求值错误 → Sources 为空仍必须出 TI900，且原因含求值失败） |
| R3-03 跨文件非空 const fallback 被抹去 | `BaseValueOf` 把所有静态字段/属性当空值 | 常量字段（含 metadata const）按 `HasConstantValue` 保留值来源：非空字符串 → `literal:"…"`，空常量/`string.Empty`/只读静态中性；非 const 静态仍中性 | `TI_N10`（跨文件 const / 空 const / static readonly 三种来源）、`TI_M09`（真实 claims builder + 另一文件 const → ledger 两条拒绝） |
| R3-04 throw 的异常消息被计入来源 | `throw` 表达式仍求值其异常对象，消息 literal 进入来源 | 值来源域中 `throw` 直接中性；异常参数内的真实写入仍由出口枚举覆盖 | `TI_N11`（带消息 / 插值消息 / 空消息三种 throw 的来源集合只含权威属性且无诊断）、`TI_M10`（真实 claims builder 改为 fail-closed throw → claims 行 ledger 干净、无违规） |

补充口径（R3-02 修复时确认）：把「求值失败」与「缺符号上下文」拆成两个状态。`EvaluationError`（MSBuild 求值失败）无条件 TI900；`ReferenceError`（已知 Compile 清单但缺 bin/引用）只在清单里确有租户相关源时才 TI900——来源已知且无租户代码时不存在覆盖缺口。这样 `CrestCreates.JsonContracts.Tool`（单文件 CLI，不在任何方案中，唯一源文件不含 tenant）不再产生假失败，而含 tenant 代码却缺引用的项目仍 fail-closed。

### I-12 R4 审查修复（2026-10-24）

审查 head `147d7c4e`，1 项 P2 成立（缓存依赖枚举未覆盖属性展开与导入 props 形式的外部 Compile glob），已修复：

- 新增 `TenantIdentityEvaluatedInputs.DependencyProbe`：遍历项目与可解析的 `<Import>` 闭包（含自动导入的根构建文件，深度上限 8、循环保护），收集贡献 Compile 项的构建文件与字面量外部搜索根。
- **缓存契约收紧为「可证明完整才缓存」**：`Compile Include/Remove` 若在替换**标准文件级属性**（`$(MSBuildThisFileDirectory)` / `$(MSBuildThisFile)` / `$(MSBuildProjectDirectory)` / `$(MSBuildProjectName)`）后仍含 `$(...)`，或任何 `<Import>` 无法解析，都标记 `IsFullyUnderstood=false`，直接调用同一 MSBuild 求值链——**跳过缓存，不跳过守卫**，不重建第二套 MSBuild 解释器。首版实现漏了这四个标准属性的替换，导致 `Directory.Build.props` 的 `$(MSBuildThisFileDirectory)` 导入让全部项目放弃缓存（热跑 4m20s）；补齐后 136 个项目恢复命中，热跑回到约 1m40s，21 个经构建 targets 注入 `$(...)` Compile 项的项目仍按设计每次重新求值。
- 已声明边界：缓存信任闭包 = 项目 + 可解析导入 + 自动导入的四个根构建文件；SDK 内部 props（不经仓库声明）不在闭包内，若其引入属性展开 Compile 项不会被探针识别（当前 SDK 只引入默认 glob 与生成输出）。
- 可缓存时的键覆盖：项目绝对目录 + 配置 + 依赖闭包内所有构建文件的**内容** + 项目内 `.cs` 清单 + 字面量外部根的树签名；命中后仍校验缓存路径存在。
- 代价：仓库中 21 个项目通过 Modules.props / JsonContracts targets 声明了 `$(...)` Compile 项，这些项目每次运行都重新求值（约 +13s），其余项目继续走缓存。

回归：`TI_C01m` 以 literal / import / 属性展开 / 导入内属性展开四种等价形式，验证预热后新增、重命名、删除 linked 文件时热读与强制求值一致；并断言 literal 与 literal-import 形式命中缓存（`msbuild-cache`）、属性形式每次重新求值（`msbuild`）。`TI_C01k`（literal 增删）与条件 Compile 用例保留。

### I-13 R5 审查修复（2026-10-27）

审查 head `a6bd5194`，2 项 P2 成立，已修复：

- **R5-01 自动导入闭包**：探针改为从项目目录逐级向上（至文件系统根）发现 MSBuild 会自动导入的 `Directory.Build.props` / `Directory.Build.targets` / `Directory.Packages.props` / `Directory.Build.Aot.props`，把这些文件及其导入纳入依赖闭包；缓存键额外记录**整条祖先链上每个候选文件的「存在与否 + 内容哈希」**，因此在项目目录或中间目录新增一个更近的 `Directory.Build.props/targets` 会立即失效并重新求值（MSBuild 只导入最近的一个，但「最近的是哪一个」本身就随文件出现而改变）。
- **R5-02 分号列表**：`Compile Include/Remove` 按 `;` 拆分后逐项解析外部搜索根（此前只取整串、只得到第一个根）；含 `%(...)` 项元数据引用的声明视为不可证明，直接放弃缓存。

回归：`TI_C01n`（中间目录 `Directory.Build.targets` 的外部 glob：预热新增即见；随后在项目目录新增一个更近的 `Directory.Build.props`，只向新目录加文件也必须被读到；未变化时仍命中 `msbuild-cache`）、`TI_C01o`（`A/*.cs;B/*.cs` 单个列表与两个独立项等价，只在第二个根新增/重命名文件都必须反映到热读）。

### I-14 R6 审查修复（2026-10-27）

审查 head `45b59f97`，2 项 P2 成立，已修复：

- **R6-01 项引用 `@(...)`**：`Compile` 的 `Include`/`Remove`/`Exclude` 中出现 `@(`（项引用/变换）时，被引用项可能指向任意目录，探针无法列出其搜索根——直接标记 `IsFullyUnderstood=false`，项目绕过持久缓存。`Exclude` 一并纳入扫描（它同样使用项引用语法）。
- **R6-02 导入遍历预算**：达到深度上限（8）且当前文件仍有 `<Import>` 时，不再静默停止，而是标记不可证明并走重新求值分支；「未访问的依赖」不等同于「没有依赖」。

回归：`TI_C01p`（literal 与 `@(Items)` 两种等价声明：literal 未变化时命中缓存、`@` 形式每次重新求值且新增外部文件即见；`Remove="@(Skip)"` 同样触发重新求值）、`TI_C01q`（8 层导入链在预算内、可缓存且变化可见；10 层链超出预算 → 每次重新求值，新增外部文件仍必须被读到）。

### I-15 待办与开放项

- S3 归档动作、准入/覆盖/隔离守卫未开始；`.github/workflows/*.yml` 与 ProcurementApproval slnx 的 Draft 输入须在 S3 清理（CI 保持人工禁用状态，不改启用）。
- S4 全量构建/边界/回归验证与 AGENTS/memory/比较文档同步未开始；本轮不提升任何支持声明。
- 基线构建注意：`dotnet build CrestCreates.slnx` 在本 worktree 首次运行因 `tests/Runtime/Agent/CrestCreates.Agent.Memory.TypeForwardLegacyConsumer/obj/project.assets.json` 缺失而报 NETSDK1004（该项目不在 slnx 成员中，仅被 Projection.Tests 引用）；单独 `dotnet restore` 后根构建 EXIT=0、0 error。须在 S4 作为预存条件精确记录。
