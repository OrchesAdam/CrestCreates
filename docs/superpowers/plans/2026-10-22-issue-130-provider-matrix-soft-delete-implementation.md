# Issue #130 实施计划

日期：2026-10-22；设计 rev.1；状态：设计草案，S1–S5 尚未实施。
基线 master：`8c8a71de6a47d7c0eb94e8260e9f94035b22a477`。
规格：[Provider 支持矩阵与软删除契约](../specs/2026-10-22-issue-130-provider-matrix-soft-delete-design.md)。

## S1 — 消费者、行为与可判别基线

- 按注册/生命周期/实体接口/生成入口建立调用图；列出两个 EF 仓储、两个 SDK 仓储、两个仓储生成器、模块、扩展、样例和测试。
- 以现有 124/124 作为历史回归基线，保留完整 FQN。不要把现有 soft-delete null 断言当契约验收，也不要 override 生产 stamp 方法。
- 补设计 T/D/A/U/G/Q/R 中目标红例：五删除重载真实留行、默认过滤、stamp 回读/冲突/批次拒绝、scope tenant 切换、detached 写、同事务回滚、真实生成消费构建。
- 冻结 IMustHaveTenant / 可空 IMultiTenant / EF 专用 marker 的 host/共享行迁移清单及精确拒绝语义；确定现有治理入口承接需要的跨租户管理。
- 为 FreeSql SDK 事务参与与 SqlSugar scope client 做直接真实连接探针；确定 SDK 精确版本的取消能力，不以注释或 Task.Run 推导。
- 对照现有编号分配编译期诊断，记录有效 EF 正向控制，避免无关编译错误假红。

出口：每个目标红例均先触达对应业务路径，另有真实正向控制；设计差异与实现责任明确。该出口不等于功能完成。

## S2 — 删除语义与查询闭环

- 修复 EF 两个 stamped Delete 的物理旁路，保留唯一 AuditInterceptor/concurrency/UoW 完成协议。
- FreeSql/SqlSugar 显式逻辑 UPDATE；无软删除实体继续物理 DELETE；五重载同一 Provider 内语义一致。
- 所有普通查询组合 tenant 与 live-row 条件；raw 对照只在 fixture 使用。幂等删除、stamp 冲突、批次预检与删除审计持久化对齐规格。
- 移除 ToList().AsQueryable 伪查询；SDK Provider 的不支持 IQueryable 入口给明确诊断和迁移说明。

出口：三个 Provider 的 D/A 相关真实 SQLite 矩阵通过；强制 DELETE 故障控制能击穿旧假绿；原有适用测试继续通过。

## S3 — 租户、身份与 UoW 装配

- 统一各 Provider 模块/扩展的 typed 装配核心；不引入另一套 UnitOfWorkFactory/支持注册表。
- 修正跨 scope 可变 client/hook 与固定首个 TenantId；写路径验证数据库原始归属，查询/写 SQL 不越租户。
- 身份沿现有 ICurrentUser，ICurrentUserProvider 只做 typed 适配；确保审计装配无需业务 handler 手动设置。
- 证实两个生产仓储共享实际事务、回滚/abandon 零残留；操作层传递组合 token，保留既有明确拒绝能力。
- 替换文件进入 99_RecycleBin；旧公开装配入口仅允许转发同一核心或精确拒绝，不留静默弱配置。

出口：T/A/U/R SQLite 实际数据库矩阵通过，tenant/事务两类故障控制失败归因正确；本项修改到的正式 runtime 原生门禁复跑通过。

## S4 — 编译期入口与 EF 正式组合

- RepositorySourceGenerator 非 EF 空实现、未知 Provider 默认 EF 改为确定性 Error；EntitySourceGenerator 相关直连 SDK 分支不再绕过已冻结契约。
- 不在此处实现完整非 EF generated CRUD，也不制造新的通用 IQueryable adapter。诊断告诉消费者使用受验证的显式异步仓储入口。
- EF 生成 mapping/filter 与正式生成仓储接既有执行路径；覆盖 filter 叠加、不依赖运行时扫描。
- 真实 MSBuild/analyzer 消费项目验收 G；EF 正式 generated CRUD 在关系型 SQLite/PG 上验证 Q01，复跑现有 PG UoW 门禁。

出口：精确诊断的正负消费构建成立；EF 关系型查询与 UoW 组合有独立证据；生成器/JsonContracts/正式 HTTP 受影响回归通过。

## S5 — 支持矩阵、迁移、发布证据

- 输出最终 OrmProviderSupportMatrix，每个组合链接代码、FQN、版本、SHA、命令、数据库和日志；不可把 experimental 或未验证改成 supported/verified。
- 明列保留 Provider 的 SQLite 核心保证、EF PG 证据及其余数据库缺口；CAP、RequiresNew、isolation、取消、generated CRUD 分项声明。
- 更新迁移说明，覆盖 host 语义、软删除、batch stamp、IQueryable、生成诊断与 client 生命周期；需要退出的能力保留替代用法与归档记录。
- 有 ORM trim/native 升级声明才运行对应完整 SDK fixture 的发布/执行/失败控制；工厂或内存 Host 原生证据独立记账。没有该证据保留「未验证」。
- 更新 memory.md，做依赖边界与最终 diff 检查；CI 保持既有状态，人工审查流程处理 PR/合并。

出口：Issue 五项验收逐条落到证据，保留 Provider 的核心行为不再假绿/静默退化；未验证范围清楚，设计记录与实现无冲突。

## 验证入口

```bash
dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests
dotnet test tests/Framework/Web/CrestCreates.IntegrationTests --filter FullyQualifiedName~UnitOfWorkPostgresIntegrationTests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

新增 fixture 的准确项目路径、过滤器和数据库准备方式在 S1 固定后补入；不得先填写未存在的「通过命令」。历史 #128/#129 的内存 CRUD/Ticket NativeAOT 门禁按实际受影响范围复跑，不能计入 ORM SDK 的发布证据。

本轮仅执行已有 OrmProviders.Tests 124/124 与两个临时 SQLite 现状观察，未实施上述目标红例/修复/PG/发布门禁。设计交付不授权自动派发、合并或恢复工作流。

## S1 执行记录（2026-10-22）

- 状态：S1 完成，S2 尚未开始；未修改 EF/FreeSql/SqlSugar 生产仓储、拦截器、模块或扩展。
- 分支/提交：`feat/issue-130-provider-matrix-soft-delete-20261022-104210`，S1 基线提交 `2f3d64e4`。
- 证据：[S1 基线与契约冻结](../review/2026-10-22-issue-130-s1-baseline.md)、[结构化证据](../review/2026-10-22-issue-130-s1-baseline-evidence.json)、[设计基线证据](../review/2026-10-22-issue-130-baseline-evidence.json)。
- 结果：历史基线 124/124 复跑通过；新增 OrmProviders 49 条（22 正控绿 + 27 约定红），全量 173 total/146 passed/27 failed；新增 Generator 过滤集 4 条（1 EF 正控绿 + 3 约定红）。红例逐条对应 D/T/U/G，见证据文档 §3。
- 冻结：`CCCREPO001`（非 EF 生成请求）/`CCCREPO002`（未知 Provider），S4 接线；stamped 批次/谓词删除先拒绝形态冻结为 `NotSupportedException` 且零写，若 S2 变更须显式更新冻结记录。
- 探针：FreeSql/SqlSugar 仓储在显式 scoped 装配下均跟随平台 UoW（abandon 隐藏/Complete 持久）；SqlSugar 模块单例 client 为缺陷红例；两 SDK 的预取消 token 均未保持取消语义（FreeSql 包装为通用异常，SqlSugar 忽略 token）；in-flight 取消未验证。
- 延后：Q01（S4）、完整 CodeGenerator 套件（S4）、host 语义收紧与 EF 生成过滤器组合（S3）、PG/其他数据库、trim/NativeAOT 均不在 S1 结论内。
