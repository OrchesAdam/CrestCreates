# Issue #130 S4 审查 R1 回复（Q01 两项 P2）

审查对象：精确 head `824da858`；报告 [2026-10-23-issue-130-s4-review-round1.md](2026-10-23-issue-130-s4-review-round1.md)。
修复提交：`1f8e592e`。测试全部串行执行。

## 1. 逐项映射

| Finding | 修复 | 证据 |
| --- | --- | --- |
| **S4-R1-1 Q01 无法检出 Price/DateTime 筛选或显式排序失效** | 重做种子数据：新增只违反 Price（`price-low`）、只违反 CreationTime（`date-old`）、只违反 Description（`desc-null`）、只违反 Status（`status-draft`）的诱饵行；两个匹配行命名为 `zeta`/`alpha`，使显式 Name 降序与默认 CreationTime 降序在非零页（PageIndex=1，PageSize=1）产生不同首项。主断言改为 total=2、首项 `alpha`；SQL 断言要求 SELECT/LIMIT 且含 `Price`、`CreationTime`、`ORDER BY`。新增 `Q01_query_gate_is_sensitive_to_each_filter_and_explicit_sort`，逐项移除条件后断言的 total/首项分别变为 3/`price-low`、3/`date-old`、3/`desc-null`、4/`status-draft`、2/`zeta` | 更新后的 Q01 门禁 **5/5 通过**；上述敏感性控制显式覆盖四个筛选与排序；审查旧探针的四个故障控制在旧断言上仍失败，说明旧门禁漏检已修复 |
| **S4-R1-2 Q01 写入门禁未证明 EF UoW 参与** | 新增测试侧 `RecordingDataBaseContext`（包装 EF 适配器，记录仓储是否观察到 `CurrentTransaction`）；正常 Create 后断言观察到受管事务。新增 `ThrowAfterSaveInterceptor`：在事务内 SQL 已刷新后抛出；生成 Create 失败后使用 fresh context 断言零残留。新增 `Q01_write_gate_is_sensitive_to_a_missing_unit_of_work`：移除 `AddUnitOfWork/AddEfCoreUnitOfWork` 后同样流程观察到无事务、失败写入残留一行，证明主门禁在 UoW 缺失时会失败 | 主写入控制 **1/1**；缺少 UoW 的负控 **1/1**；非事务仓储自动提交无法通过主断言 |

## 2. 回归结果

| 集合 | 结果 |
| --- | --- |
| Q01 更新门禁（SQLite 主/敏感性/UoW + PG） | **5 / 5 passed** |
| GeneratedCrud 全量 JIT | **28 / 28 passed** |
| GeneratedCrud NativeAOT 门禁 | **3 / 3 passed（1m44s）** |
| PostgreSQL UoW | 4 / 4 |
| OrmProviders 全量 | 255 / 255 |
| CodeGenerator 全量 | 461 / 461（R1 未改生成器） |
| Dependency Boundaries | 176 / 178（2 条既有 #69 evidence-ledger，无关） |

审查方原 `ReviewS4Probes` 只作为旧门禁漏检的演示，未原样入库；本轮以更新后的真实 Q01 门禁及新增的显式敏感性/负控为准。没有新增生产公开扩展点；测试包装器和 EF 拦截器均在测试项目内。

未验证范围不变：SQLServer/MySQL、ORM trim/NativeAOT、SQLite/PostgreSQL 之外的 generated CRUD、in-flight 取消、跨进程并发。S5 未启动。
