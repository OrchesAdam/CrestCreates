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
- 结果：历史基线 124/124 复跑通过；新增 OrmProviders 53 条（22 正控绿 + 31 约定红），全量 177 total/146 passed/31 failed；新增 Generator 过滤集 4 条（1 EF 正控绿 + 3 约定红）；CodeGenerator 全量 451 total/448 passed/3 failed（3 红均为本切片约定项，运行 32m39s）。红例逐条对应 D/T/U/G，见证据文档 §3。
- 冻结：`CCCREPO001`（非 EF 生成请求）/`CCCREPO002`（未知 Provider），S4 接线；stamped 批次/谓词删除先拒绝形态冻结为 `NotSupportedException` 且零写，若 S2 变更须显式更新冻结记录。
- 探针：FreeSql/SqlSugar 仓储在显式 scoped 装配下均跟随平台 UoW（abandon 隐藏/Complete 持久）；SqlSugar 模块单例 client 为缺陷红例；两 SDK 的预取消 token 均未保持取消语义（FreeSql 包装为通用异常，SqlSugar 忽略 token）；in-flight 取消未验证。
- 延后：Q01（S4）、host 语义收紧与 EF 生成过滤器组合（S3）、PG/其他数据库、trim/NativeAOT 均不在 S1 结论内。

## S2 执行记录（2026-10-22）

- 状态：S2 完成，S3 尚未开始；已修改 EF/FreeSql/SqlSugar 仓储、EF UoW 与租户/软删除生成器。
- 提交：`66947304`（S2 实现与测试）。
- 结果：OrmProviders 182 total / 172 passed / 10 failed；10 条全部是 S3 范围 T/U 红例；D01–D09 与 A01–A02 全绿，历史 124 条全绿。Tenant/Soft filter 生成器过滤集 8/8。
- 实现：EF stamped 删除改为 tracked 软删除并保留 AuditInterceptor/concurrency；stamped 批次与谓词删除先拒绝；EF UoW flush 冲突翻译为 `CrestConcurrencyException`；生成强类型软删除过滤器并与租户过滤组合、带目标模型守卫；FreeSql/SqlSugar 五删除重载统一条件 UPDATE + 删除审计/stamp，SDK 查询默认 live-row，`GetQueryable` 明确 `NotSupportedException`。
- 故障控制：S1 物理删除版本作为真实反向对照；D06/D08 断言零写；`U01_EfCore_stale_stamp_delete_reports_conflict_at_complete` 验证延迟 flush 冲突。
- 证据：[S2 执行记录](../review/2026-10-22-issue-130-s2-baseline.md)、[S2 JSON](../review/2026-10-22-issue-130-s2-baseline-evidence.json)。
- 延后：T/U 10 条（S3）、Q01（S4）、PG/其他数据库、trim/NativeAOT、in-flight 取消。
- CodeGenerator 全量套件因生成器改动已复跑：453 total / 450 passed / 3 failed（32m06s），3 红均为 S1 约定 G 红例。

## S2 R1 复审修复记录（2026-10-22）

- 审查 head `b7f1d349` 新增 3 项 P2：host 组合表达式优先级、SDK 映射列、同名软实体方法冲突；S2 暂不放行。
- 修复提交 `d593d312`：租户 OR 加括号；FreeSql/SqlSugar live-row/Id/stamp 条件改由映射元数据生成；SqlSugar 查询过滤器按 client 去重；生成方法/索引使用全类型身份。
- 审查探针逐字入库（ORM 5 条、生成器 2 条），修复前 4/5 与 1/2 失败，修复后全部通过；新增映射列回归 4 条。
- 全量 OrmProviders **195 total / 185 passed / 10 failed**（10 仍为 S3 T/U）；模型过滤器 registry 测试加入串行集合并快照/恢复；stamped update 的同类映射条件同步修复（`666816c2`）。
- 证据：[R1 response](../review/2026-10-22-issue-130-s2-review-round1-response.md)；CodeGenerator 全量已复跑 **455 total / 452 passed / 3 failed（30m51s，仅 G 红）**。

## S2 R2 复审修复记录（2026-10-22）

- 审查 head `b4664572` 剩余 2 项 P2：保留字映射列引用、`A.B.Row`/`A_B.Row` 名称碰撞。
- 修复提交 `9071e99a`：FreeSql 改用强类型接口表达式（SDK 翻译并引用映射列）；SqlSugar 用 `InstanceFactory.GetSqlBuilderWithContext(...).GetTranslationColumnName(...)` 引用 Id/stamp/live；生成方法名对完整类型身份做无歧义编码。
- 审查 R2 探针 4/4 与 2/2 通过；新增保留字映射控制 4 条；全量 OrmProviders 203 total / 193 passed / 10 failed（10 仍为 S3 T/U）。
- 证据：[R2 response](../review/2026-10-22-issue-130-s2-review-round2-response.md)；CodeGenerator 全量 **458 total / 455 passed / 3 failed（31m19s，仅 G 红）**。

## S3 执行记录（2026-10-22）

- 状态：S3 完成，等待审查；S4 未开始。
- 提交：`64dad166`（实现与测试）、`8754428f`（证据）。
- 结果：OrmProviders **216 total / 216 passed / 0 failed**；原 10 条 T/U 红例全部转绿；新增 9 条租户矩阵 + 4 条装配/事务/取消控制。CodeGenerator 全量 **458 total / 455 passed / 3 failed（31m34s，仅 G 红）**；Boundary 176/178（2 条既有 #69 evidence-ledger，无关）。
- 实现：SDK 逐操作租户解析与持久归属校验；EF 生成过滤器区分 required / DataFilter optional / EF 兼容 marker，并在 host 通过 `HostCurrentTenant` 应用；SqlSugar scoped client/interface 与 FreeSql/SqlSugar 装配核心统一；唯一 `ICurrentUserProvider` adapter；SDK 入口取消预检查。
- 兼容：required host 读 fail closed；optional host 仅共享行；EF 专用 optional host=all 保留为兼容差异；FreeSql 扩展停止仓储扫描；SqlSugar client scoped；租户 `UpdateRange` 拒绝。
- 证据：[S3 记录](../review/2026-10-22-issue-130-s3-tenant-assembly.md)、[S3 JSON](../review/2026-10-22-issue-130-s3-evidence.json)。
- 未验证：EF 专用 marker host 收紧、PG/其他数据库、ORM trim/NativeAOT、in-flight 取消、跨进程并发。

## S3 R1 复审修复记录（2026-10-23）

- 审查 head `8c8b24fe` 提出 2 P1 + 2 P2；修复提交 `1025368e`。
- EF 归属预读 `AsNoTracking`；SDK 读写租户谓词分离（共享 null 行 host 独占写）；Provider 仓储内部创建/修改/删除审计；所有 SDK 入口组合受管执行 token。
- ReviewS3Probes 22/22（修复前 8/22）；全量 OrmProviders **240/240**；新增正式 DI 审计回归 2/2。CodeGenerator 458/455/3（未改生成器），Boundary 176/178（既有 #69）。
- 证据：[S3 R1 response](../review/2026-10-23-issue-130-s3-review-round1-response.md)。测试按用户要求串行执行。

## S3 R2 复审修复记录（2026-10-23）

- 审查 head `7b06a5fb` 剩余 2 个 P2；修复提交 `733bea45`。
- 创建审计权威化（覆盖预填值、更新不写创建字段，含 EF）；SqlSugar 所有 SDK 命令传递组合 token，审计快照后/ SQL 前再次检查。
- ReviewS3R2Probes 8/8（修复前 3/8）；R1 探针 22/22；全量 OrmProviders **249/249**；新增 EF 审计回归 1/1。
- 证据：[S3 R2 response](../review/2026-10-23-issue-130-s3-review-round2-response.md)。测试串行执行。
