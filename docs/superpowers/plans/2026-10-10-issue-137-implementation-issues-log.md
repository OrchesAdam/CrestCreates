# Issue #137 实施问题记录（切片化实施，统一审查）

日期：2026-10-10
用途：记录 #137 实施过程中发现的问题、遗留项、环境边界与建议后续动作，供整体完成后统一审查。
设计依据：`docs/superpowers/specs/2026-10-10-issue-137-uow-transaction-propagation-design.md`（rev.3.1）
分支：`feat/issue-137-slice1-kernel-state-20261010-155153`（切片 1 起堆叠；PR #139 统一承载）

## 状态说明

- 每个条目格式：编号 / 类别（缺陷·遗留·环境·设计待判·流程）/ 说明 / 影响 / 建议动作 / 解决切片或后续轨道。

## 记录

| # | 类别 | 说明 | 影响 | 建议动作 | 状态 |
| --- | --- | --- | --- | --- | --- |
| I-01 | 遗留（AOP 轨道） | AOP 拦截器的服务解析仍走既有反射式 `MethodContextExtensions.GetService`（读取 Target 的 `ServiceProvider`/`Services`/`HttpContextAccessor` 属性，如 `CrestAppServiceBase.ServiceProvider`）。本切片未改动该机制；AOP 验收探针通过显式暴露 `ServiceProvider` 属性复现生产模式。 | 无该属性的被织入目标会静默跳过 UoW（manager 缺失 warn+skip 的既有兼容路径）；与「优先代码生成、减少反射」原则相悖 | 建议 AOP 轨道跟进：替换为类型化 scope 访问器（编译期/明确注册），并复审 warn+skip 语义 | 待后续轨道 |
| I-02 | 环境（本地验证） | SQLite 为单写者：EF 默认 `BEGIN IMMEDIATE`；requiresNew 的父子写事务不可重叠，本地 UoW 数据库用例按序开启写事务（注释已注明），嵌套隔离的并发语义由 PostgreSQL CI 套件覆盖 | 本地无法复现重叠写事务场景 | 保持 CI 为 PG 场景证据来源；本地改动涉及事务顺序时按序断言 | 已知/接受 |
| I-03 | 环境（本地验证） | Web.Tests 中 4 个 `CapabilityEndpointBoundaryTests` 在 worktree 失败（查找 `.git` 目录，worktree 中为文件），#124 记录在案；与本项无关 | 本地绿灯信号需扣除该项 | 无需处理；CI（非 worktree）通过 | 已知/接受 |
| I-04 | 环境（本地验证） | Boundary 套件 5 个失败：3 个 Testcontainers/Docker 不可用；2 个 Control Plane reference-data evidence ledger 依赖 CI 复位后再生产（本地不产生该证据） | 本地无法全绿该套件 | 以 CI 为准；本地按受影响套件验证 | 已知/接受 |
| I-05 | 遗留（切片 3/5 决策） | FreeSql/SqlSugar 的「未完成退出及时终止 / 丢弃未 flush 状态 / CAP lease」能力未落地（当前声明 fail-closed 的仅 requiresNew；终止依赖容器释放语义） | 非 EF Provider 的未完成退出没有即时事务终结保证 | 切片 3（终止/丢弃）与切片 5（lease）落位「实现或明确拒绝」并写入 Provider 矩阵 | 进行中 |
| I-06 | 环境（本地验证） | PostgreSQL/Testcontainers 依赖套件（IntegrationTests 等）本地不可运行（Docker 不可用） | 涉及 PG 的验收项本地不可复现 | 由 CI 验证并在 PR/记录中标注范围 | 已知/接受 |
| I-07 | 流程 | `dotnet build` 管道给 `tail` 会吞退出码；本仓库 CI 等价本地构建需：solution restore → 遗留 fixture restore → BuildTasks bootstrap → `--no-restore` 构建 | 误判构建结果 | 已写入项目记忆；PR 验证记录用重定向 + `$?` | 已记录 |
| I-08 | 遗留（切片 2） | 受管执行 token 的**链校验**已贯穿适配器构造的 builder/set 与 `EfCoreRepository`（可注入节点）；平台派生仓储基类 `EfCoreRepositoryBase` 的原始 IQueryable 终结路径仍用顶层帧语义（其派生仓储 ctor 未向基类传递节点）。期限污染的窗口仅限「独立 scope 在同一异步流 + 派生仓储原始 IQueryable 路径」 | 极端场景下独立 scope 可能读到他人截止时间（不含资源/事务路由） | 后续切片或 ORM 轨道：为派生仓储 ctor 传递节点（或基类经 DI 解析节点） | 待处理 |
| I-09 | 环境/测试 | InMemory 等非关系型 Provider 没有连接配置：ambient 日志键与读方连接校验对非关系型 Provider 自动退化为「声明类型」比较（读取连接串带保护）；无环境帧时快速返回不计算逻辑键 | 无（行为已定义） | 保持该保护；新增非关系型 Provider 时复核 | 已处理 |

（后续切片发现问题将追加到本表。）
