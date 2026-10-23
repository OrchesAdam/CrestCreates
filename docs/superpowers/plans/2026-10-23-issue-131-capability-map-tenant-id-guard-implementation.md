# #131 能力地图与 TenantId 守卫实施计划

日期：2026-10-23  
依据：[功能设计](../specs/2026-10-23-issue-131-capability-map-tenant-id-guard-design.md)。  
状态：计划已完成，尚未执行。本轮只提交本地设计材料，不创建实现 PR、不发消息、不修改 GitHub Issue 或恢复 CI。

## S1：在实际 master 冻结清单与地图

1. 检查 checkout 与未提交修改；保留既有 memory/审查材料。同步实际 master 后记录精确 SHA，重新盘点 #131 的 15 个项目。设计已只读核对 `8c8a71de` → #130 合并 `348c4d3c`：范围源码/引用/方案不变，新增 host-null context 与静态过滤路径已纳入设计；此核对不能替代届时实施基线记录。
2. 重新确认 7 项目归档决策：ExternalApi、LegacyDatabase、Agent.Runtime、Configuration、Draft、Draft.Abstractions、EventBus.EventStore。查类型与模块调用、显式/传递项目及包引用、生成输入、所有 slnx、build/pack/AOT 输入；外部消费未知须披露。任何新增消费者必须先给迁移方案，不直接执行原归档清单。
3. 新建 `docs/Feature/CapabilityMap.md`：12 条裁定记录覆盖 15 项目，指定边界、唯一入口、真实调用点、功能状态、执行证据、技术责任与处理条件。其余 active 生产项目先建立路径/现有文档导航索引，明确未在本轮重评。
4. FileManagement 标本地/内存元数据实验状态，云 Provider 单列；PluginSystem 标实验 opt-in/JIT-only、功能执行未验证。Organization/Localization/OpenApi/DescriptorDraft 保留原有主链与准确证据范围。核对 `AddDescriptorDrafts` 等实际入口，不能复制过期命名。
5. 给出归档迁移说明和实验保留说明。Owner 记录代码区域与 #131 实施 PR 责任，不虚构个人指派；实验能力的后续升级不作为本项产品化承诺。

退出标准：地图条目不缺字段，所有引用/消费者可定位；设计与新 master 有差异时更新裁定并记录依据；没有用文件数或项目引用数替代行为判断。

## S2：交付测试侧 TenantId 架构守卫

落点：`tests/Boundary/CrestCreates.DependencyBoundaries.Tests/TenantIdentityArchitectureTests.cs` 及内部 helper/fixtures。继续使用现有 Roslyn 引用，不新增 production analyzer/registry。

1. 枚举 active src/sample 非测试 Compile 输入并按项目获取真实符号上下文。先用 TI-C01 检验实际输入、项目边界与敏感符号绑定；解析失败必须是失败，不能以无诊断替代分析成功。
2. 按设计 §4.2 实现 Name 源、身份出口、局部值流与简单 helper 摘要。关键 getter/claims/middleware/cache/filter 加权威来源结构断言；禁止宽泛文件豁免或静默 legacy baseline。
3. 合成正反例覆盖 TI-P01/P02、TI-N01/N02/N03；包括名字看起来像 GUID、别名、空值 fallback、named argument、using alias 和普通对象 Name。不测试业务不使用的反射链。
4. 扩展既有 tenant resolver/tenant boundary 测试，使用目录实体的不同 ID/name，覆盖名称解析、名称变化、未知/失效租户，证明最终上下文与身份/隔离槽仍是 ID。复用现有身份/权限主链。
5. 在临时副本运行 TI-M01/M02 三个变异：resolver 返回名称、tenant claim 改 UserName、audit 缓存 tenant 参数改租户名称。先保证可编译，再确认精确目标断言失败；恢复副本后通过。变异不写回正式源码。

退出标准：真实仓库门禁、正反例、行为回归和变异控制齐备；报告规则/文件/行列/来源→出口，覆盖限制明确。若修复正式 runtime 的既存违规，记录受影响执行链并复跑该链已有 NativeAOT publish/link/run，而非另造替代 fixture。

## S3：归档与准入守卫

1. 按 S1 已复核清单移除失效引用及所有方案/build/pack 输入；先迁移发现的消费者，再移动项目。
2. 将 7 生产项目、通用 Draft 专用测试与已确认无消费者的 `EventBus.Abstract/IEventStore.cs` 移至 `99_RecycleBin/issue-131-capability-map/`，保留原相对路径、归档 README 与原状态。不直接删除文件、不整体搬走 Abstract 包。
3. 移除 Workflow → Draft.Abstractions、Workflow.Tests → Draft 的无效引用；保留/调整禁止 PG 重新注册旧 IDraftStore 等防复活测试。不要把实验测试改名搬到 DescriptorDraft 伪装正式主链测试。
4. 新增项目地图覆盖测试、归档项目/命名空间/模块禁止回流测试、PluginSystem 正式链隔离测试。新增项目准入理由见设计 §5；已有合法抽象/静态聚合壳可说明边界后保留。
5. 校验 active 项目、所有 slnx 引用、模块 manifests 和包输入都不落入回收站。负向守卫中的旧名称、历史审查记录和归档 README 不算正式消费者。

退出标准：没有悬空引用/方案成员或被自动 pack 的归档产物；合法平台内核仍可消费；地图索引与 active 生产项目集合一致。若保留某个原拟归档项目，须有更新后的具体裁定，不能留下隐式双轨。

## S4：验证与文档闭环

按变更范围执行一次必要验证，失败后只对修复和未决问题复跑，不反复扩大测试消耗：

| 类别 | 必需验证 |
| --- | --- |
| 静态完整性 | 地图项目索引唯一/全覆盖，链接存在，所有 root/分层 slnx 与引用可解析；归档输入未进入 Compile/pack |
| 构建 | `dotnet build` 与 `dotnet build solutions/CrestCreates.All.slnx`；预存失败单独给精确项目/错误/基线证据，不宣称全绿 |
| 边界 | DependencyBoundaries.Tests 的 TenantIdentity、项目准入/归档/插件隔离以及受影响既有规则；不需要 PG 的测试不依赖数据库 |
| 租户行为 | 新 TI-B01 和受影响现有 resolver/tenant boundary 正式链测试；按最终改动选择具体 FQN 并记录命令 |
| 保留内核回归 | Draft 依赖变化后的 Workflow.Tests；DescriptorDraft.Tests、Organization.Tests、Localization.Tests、FileManagement.Tests，以及 Web OpenApi/异常呈现相关用例 |
| 原生 | 仅触及声明/文档/无消费者归档时不扩大 verified 范围；触及既有 verified 执行链时，运行其真实 publish/link/run 成功/负例/sentinel 并保留精确模式与 RID |
| 故障控制 | TI-M01、TI-M02 目标失败及恢复通过；移除项目索引条目必须使地图门禁失败，新增正式主链到 PluginSystem 引用必须使隔离门禁失败 |

数据库测试使用新独立 schema/数据库，不访问保留资产，不改 CI 人工禁用状态。记录源码 SHA、dirty 差异/补丁身份、SDK、命令、退出码、测试数及范围；「有 fixture」与「执行通过」分开写。

同步 AGENTS 的主链/项目结构与支持声明、memory 的设计/实现状态和活跃宣传。修订旧 ABP 比较中的 PluginSystem 生产就绪结论，以及 #130 唯一支持矩阵页首仍写「尚未合并」的状态元数据；不复制其 ORM 支持表。历史审查保持历史身份并指向新地图。交付 `docs/review/2026-10-23-issue-131-implementation-evidence.json`（或实际实施日期文件）、迁移说明和验收逐项映射。

最终只有满足设计 §6 的原 Issue 四条验收才能宣称 #131 实施完成。功能设计完成不勾选实现验收，不授权自动合并。代码提交、PR 与合并按用户后续授权和仓库流程处理。
