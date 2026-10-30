# #132 设计基线与可运行缺口证据

日期：2026-10-27。基线：master `5f4cfc8f37d6ef0e996598eb08ae0922b8b60a34`。交付身份：功能设计/缺口复现，非生产 retention 实现验收。

GitHub 已核实 [#131](https://github.com/OrchesAdam/CrestCreates/issues/131) 为 completed， [PR #155](https://github.com/OrchesAdam/CrestCreates/pull/155) 已合并，状态同步 [PR #156](https://github.com/OrchesAdam/CrestCreates/pull/156) 合并产出上述基线。[#132](https://github.com/OrchesAdam/CrestCreates/issues/132) 当前 open、评论为空，其范围是设计裁定与可运行缺口复现；本轮没有远程修改。

共享工作目录仍是 `348c4d3c`，含既有 memory 修改与大量未提交审查文档。未切换/重置它。Git 对象已含当前 master，使用 Git archive 导出独立 `/tmp` 快照；旧基线到当前 master 的本次审计范围生产实现未发生变更。JSON 记录精确源码哈希与实际 SDK，所有编译来自 ProjectReference 的源代码，未复用旧二进制。

## 1. 执行方式

运行[复现脚本](probes/issue132/run.py)，命令见[README](probes/issue132/README.md)。先 restore/build（单节点 `-m:1`），再执行生成 DLL。无 PostgreSQL、容器、HTTP 或 NativeAOT 发布。完整构建有现存 generator/nullable/trim analyzer 警告；本轮未压制警告或提升支持声明。

结果：[结构化证据](2026-10-27-issue-132-design-gap-evidence.json)，10/10 baseline observations matched，总 sentinel `CRESTCREATES_ISSUE132_GAPS_REPRODUCED`，执行 exit 0。绿色表示现有行为按预期被观察到；其中 7 条是缺口，不是验收通过。

## 2. 真实关系型 case matrix

| Case | 实际输入/执行 | 观察 | 分类 |
| --- | --- | --- | --- |
| C01 | tenant-a，过期/近期/精确 cutoff 各一行 | 仅过期删除；近期和边界保留 | 现有行为控制 |
| C02 | A/B/host 过期各一行，当前 A | 仅 A 删除；B/host 保留 | 现有隔离控制 |
| C03 | null host 上下文，同上三行 | 一次删除 3；服务内没有独立的 AllTenants 授权参数 | 隐式扩大破坏范围；不推导外部 HTTP 可利用性 |
| C04 | 过期行持久 ExtraProperties protection hint | 行仍被删 | 没有原生 hold 契约，不把 fixture hint 当合法 hold 配置 |
| C05 | 近期行，未来两天 BeforeTime | 行被删 | 任意截止时间绕过保留期 |
| C06 | 近期行，RetentionDays=-1 | 行被删 | 负天数未拒绝 |
| C07 | 1101 个过期行，一次调用 | 删除 1101 | 无 batch 上限协议 |
| C08 | 过期行持久 cleanup-evidence hint | 行被删 | 没有独立治理收据/保护协议，hint 不等于真实 receipt |
| C09 | 预先取消 CT，一个过期行 | 抛取消，行保留 | 仅验证预取消，未证明 in-flight 取消/提交后行为 |
| C10 | Count 后、目标 DELETE 前，interceptor 执行独立 DELETE | 竞争语句删 1；目标语句真实结果 0；服务返回 1 | 计数不是本次删除事实 |

C10 使用同一个 SQLite 连接，确定性模拟两条语句之间的竞争交错；用 NonQueryExecuted 读取目标语句的真实 affected rows。它不是两连接/跨进程并发保证。Provider 新实现仍必须执行真实 PostgreSQL 两连接、崩溃与事务故障门禁。

Fixture 仅提供原生 DbContext 模型、可变 ICurrentTenant、固定 ISettingProvider 和竞争语句 interceptor。AppService、EF 仓储、EF adapter/base 均为实际生产源码；没有复制删除谓词或替换仓储。直接调用绕过 HTTP，不能据此断言任何真实宿主的认证/授权门禁状态。

## 3. 正式链静态事实

- [清理入口](../../src/Framework/Ddd/CrestCreates.Application/AuditLog/AuditLogCleanupAppService.cs) 第 38–53 行优先消费 BeforeTime，再调用旧仓储；未检查 future/negative policy、hold、batch 或 receipt。
- [EF 仓储](../../src/Persistence/CrestCreates.Data.EFCore/Repositories/EfCoreAuditLogRepository.cs) 第 124 行起；第 154 行 Count，第 158 行 ExecuteDelete，但最终返回 Count。测试执行了这段代码。
- [正式 sink](../../src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlAuditSink.cs) 第 34 行条件插入 `runtime_audit_envelopes`，第 90 行按完整性比较 Duplicate/Conflict；删除这一行会同时丢失其唯一身份判断。此处是静态设计推论，本轮没有执行“删行后重投”的 PG 实验。
- [migration V003](../../src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlRuntimeMigrationRunner.cs) 第 1099 行创建的表没有 hold、清理 run/batch receipt 或 retention 投影。
- [Accountability 架构](../Feature/Accountability/arch-design.md) 已把旧 AuditLog 标为 compatibility model，并将正式 query/retention 排除在原交付范围。

因此原 Issue 所述“已有写入/脱敏/查询”不能转换为正式存储 retention 已有可用基础设施。应为正式 Accountability 增加最小生命周期协议，并退出旧裸删链；不扩建第二个 AuditLog retention Provider。

## 4. 交付结论与限制

[功能设计](../superpowers/specs/2026-10-27-issue-132-audit-retention-governance-design.md)已定义 owner、契约、设置、scope/权限、hold/索引、预览、事务、部分失败/Unknown、幂等 ledger 与独立收据，并映射到 P01–P09 Provider/原生门禁。[实施清单](../superpowers/plans/2026-10-27-issue-132-audit-retention-governance-implementation.md)给出 S0–S4 有界后续。

裁定建议为先实施 PostgreSQL 固定存储 V1；其他 Provider、通用查询/归档与治理元数据 GC 明确延期。待维护者确认 Global policy、PG-only 与无限期最小 ledger/receipt 的产品取舍后，才可称设计裁定完成。没有新增生产实现、没有对 PostgreSQL retention 执行集成/native 测试、没有关闭 #132、没有创建或合并 PR，也没有启用 CI。
