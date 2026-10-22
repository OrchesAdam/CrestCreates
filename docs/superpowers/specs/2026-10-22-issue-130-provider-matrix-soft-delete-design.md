# Issue #130：Provider 支持矩阵与软删除契约

日期：2026-10-22；版本：rev.1；状态：设计草案，尚未实施/验收。

Issue：[#130](https://github.com/OrchesAdam/CrestCreates/issues/130)，总控 #121。
设计基线：master `8c8a71de6a47d7c0eb94e8260e9f94035b22a477`。
前置：#124/#137 的唯一 UoW 内核已合并；#128/#129 已合并。
本文中的「目标」不代表当前代码已经满足；当前事实与本轮验证见 §2。

## 1. 目标与范围

让开发者继续声明实体、注入仓储并调用唯一 UoW；Provider 包吸收查询过滤、删除、审计和事务参与的差异。交付按 **Provider × 数据库 × 能力 × 执行模式** 的支持矩阵，以真实代码、测试、发布与执行日志为依据。

本项保留 EF Core、FreeSql、SqlSugar，先修复暴露出来的基本契约。FreeSql/SqlSugar 初始定位 experimental；experimental 不能成为越租户、物理冒充软删除或事务未参与的豁免。无法实现的具体入口必须明确拒绝。没有消费者不足以直接裁定退出 Provider；若后续决定退出，须另记裁定、迁移说明，并归档文件。

不预设共享泛型仓储基类，不新增 16 套领域专用仓储，不扩展 RequiresNew/显式隔离级别到未实测 Provider，不新增跨库原子事务、Provider 通用 LINQ 翻译器、自动扫描或运行时反射兜底。MongoDB、FreeSqlCloud、多库切换和新的 ORM SDK 不在本项范围。

## 2. 基线事实与证据

### 2.1 代码与消费者

| 路径 | 当前事实 | 设计含义 |
| --- | --- | --- |
| [EF 平台仓储](../../../src/Persistence/CrestCreates.Data.EFCore/Repositories/EfCoreRepositoryBase.cs)、[通用仓储](../../../src/Persistence/CrestCreates.Data.EFCore/Repositories/EfCoreRepository.cs) | 普通删除走 tracked Remove；带 stamp 的 id 删除直接 ExecuteDeleteAsync | 后者绕过 SaveChanges 审计/软删除转换；两入口均须覆盖，不能只修一个 |
| [EF AuditInterceptor](../../../src/Persistence/CrestCreates.Data.EFCore/Interceptors/AuditInterceptor.cs) | Deleted → Modified，设置删除字段 | 仅证明 tracked 路径存在转换；不能证明所有查询默认隐藏软删除数据 |
| [FreeSql 仓储](../../../src/Persistence/CrestCreates.Data.FreeSql/Repositories/FreeSqlRepository.cs)、[拦截器](../../../src/Persistence/CrestCreates.Data.FreeSql/Interceptors/FreeSqlAuditInterceptor.cs) | 各删除重载调用 Delete；ConfigureSoftDelete 的 CurdBefore 只有 TODO；存在独立过滤/审计扩展 | 明确修复公开软删除承诺，不能靠移除 TODO 或另注册一个空 hook 完成 |
| [SqlSugar 仓储](../../../src/Persistence/CrestCreates.Data.SqlSugar/Repositories/SqlSugarRepository.cs)、[拦截器](../../../src/Persistence/CrestCreates.Data.SqlSugar/Interceptors/SqlSugarAuditInterceptor.cs) | Deleteable；hook 设置字段不能据此推导 SQL 已变为 UPDATE | 同样需要物理行保留与真实删除 SQL 的证据 |
| 两个 SDK 仓储的 GetQueryable | ToList().AsQueryable()，构造时保存 SDK 对象 | 查询在后续 Where/分页前已物化；不能声称等价 EF IQueryable/异步 Provider |
| [FreeSql 扩展](../../../src/Persistence/CrestCreates.Data.FreeSql/Extensions/FreeSqlServiceCollectionExtensions.cs)、[FreeSql 模块](../../../src/Persistence/CrestCreates.Data.FreeSql/Modules/FreeSqlOrmModule.cs)、[SqlSugar 模块](../../../src/Persistence/CrestCreates.Data.SqlSugar/Modules/SqlSugarOrmModule.cs) | 模块硬编码 SqlServer；SqlSugar 单例 client；FreeSql 部分重载 AddFreeRepository 扫描；未统一装配上述审计/过滤 hook | SDK 支持某数据库不等于模块装配正确；注册与实际仓储事务关系必须测 |
| [EF 租户生成器](../../../src/Tooling/CrestCreates.CodeGenerator/TenantFilterGenerator/TenantFilterSourceGenerator.cs) | EF 专用 IMultiTenant；host-null 条件允许全部租户 | 不能把该语义与 IMustHaveTenant、可空 IMultiTenant 混为一谈 |
| SDK 租户扩展 | 捕获配置时 TenantId；无 tenant 时直接 return；可空 IMultiTenant 允许 null 共享行 | 单例/跨 scope 切换有风险，必须经实际 A→B→host 及并行 scope 验证 |
| [RepositorySourceGenerator](../../../src/Tooling/CrestCreates.CodeGenerator/RepositoryGenerator/RepositorySourceGenerator.cs) | FreeSql/SqlSugar emit 为空；未知 Provider 默认 EF | 缺实现不能静默成功或降到另一个 Provider |
| [EntitySourceGenerator](../../../src/Tooling/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs) | 另有直接调用 SDK 的仓储生成分支 | 必须纳入声明/消费者清单，避免修好手写基类却保留生成绕过路径 |

本轮检索 samples 与 src/Platform：未发现 FreeSql/SqlSugar 的正式运行装配消费者；SaaSHelpdesk 仅有 FreeSql binding 测试占位。二者仍存在公开包、模块、Attribute 入口、生成器分支和真实 SQLite 测试，不按「零文件消费者」判定已退出。

### 2.2 本轮实际执行

- .NET SDK 10.0.112；精确基线上的 `CrestCreates.OrmProviders.Tests`：124 passed / 0 failed / 0 skipped。
- 现有 FreeSql/SqlSugar 的 `SoftDelete_ShouldMarkEntityAsDeleted` 仅断言 GetAsync 返回 null；其测试仓储还覆盖了 stamped delete 为 NotSupported，不能用该测试替代生产重载验证。
- 独立临时 SQLite 探针调用现有仓储 DeleteAsync(entity)，不启用过滤器，直接从 SDK 读原表计数：两者均 Before=1、After=0。证明现有成功测试接受了物理删除。此探针是现状观察，**不是修复验收通过**。
- 未运行新的 PostgreSQL、trim 或 NativeAOT 门禁；未将历史 #137/#128/#129 原生结果推导为 ORM SDK 证据。
- 结构化记录：[基线证据](../../review/2026-10-22-issue-130-baseline-evidence.json)。既有 PostgreSQL UoW 用例见 [UnitOfWorkPostgresIntegrationTests](../../../tests/Framework/Web/CrestCreates.IntegrationTests/UnitOfWorkPostgresIntegrationTests.cs)；本轮未复跑。

### 2.3 初始支持矩阵（事实，不是最终背书）

`supported` 表示指定组合/功能有执行证据；`experimental` 表示保留入口但证据或行为尚未闭合；`unsupported` 表示明确拒绝。执行模式另列：JIT 有测试、trimming-verified、NativeAOT-verified、未验证；**未验证不等于已证明不支持，也不等于 verified**。

| 能力/组合 | EF Core | FreeSql | SqlSugar |
| --- | --- | --- | --- |
| 基础仓储 JIT | SQLite 关系型 UoW 回归；部分仓储/并发测试使用 EF InMemory，不能合并为完整关系型覆盖 | SQLite CRUD/并发用例执行；完整平台契约 experimental | SQLite CRUD/并发用例执行；完整平台契约 experimental |
| tenant 读写隔离 | 已有生成 filter + interceptor + sample PG 历史证据；本项统一组合待验 | 独立扩展存在；默认装配、scope/写边界待验 | 同左；额外有 singleton client 生命周期风险 |
| 软删除全部重载 + 默认过滤 + audit | tracked 转换存在；stamped Delete 物理删除；组合不满足 | 已观察物理删除，不满足 | 已观察物理删除，不满足 |
| 事务参与/Required/退出丢弃 | 现有 SQLite 用例通过；PG 为既有独立用例，本轮未跑 | SDK UoW/abandon 用例存在；生产仓储同事务参与待验 | SDK Ado UoW 存在；生产仓储参与与 scope 隔离待验 |
| RequiresNew | 既有 EF 证据，按数据库组合声明 | unsupported，保留执行前拒绝 | unsupported，保留执行前拒绝 |
| 显式 isolation | SQLite Serializable；PG 三档已有用例；其他数据库逐项核对 | 未声明，现有内核拒绝 | 未声明，现有内核拒绝 |
| 正式 generated CRUD 查询 + ORM/UoW 组合 | 待独立关系型验证，不能引用内存 AOT fixture 背书 | 本项不补完整 generated CRUD；应明确拒绝不可执行投影 | 同左 |
| SQLServer/MySQL 等数据库 | 按每个已有 Provider 包/组合另列证据，不能由 SQLite 推导 | 模块硬编码 SqlServer 不是验证；未验证 | 同左 |
| ORM 包 trim / NativeAOT | 未在本轮验证；工厂/内存 CRUD/Ticket fixture 结果不覆盖完整 EF 链 | 未验证 | 未验证 |

矩阵每行最终应链接：注册入口、实际实体/仓储、测试 FQN、数据库与驱动版本、精确源码 SHA、命令/退出码/结果日志。业务实体 audit 字段与平台 Audit Logging 是不同能力，本项不宣称覆盖后者。

## 3. 冻结的行为契约（目标）

### 3.1 删除与查询

1. `ISoftDelete` 是软删除唯一实体标记。Delete(entity)、Delete(id)、Delete(id, stamp)、DeleteRange(entities)、DeleteRange(predicate) 全部进行逻辑删除，持久行保留；没有该标记的实体仍物理删除。
2. 默认所有 Get/List/Count/Any/Exists/分页入口排除已删除行。查询不得先拉全表再做 tenant/filter/分页。过滤必须在数据库执行；总数与 items 使用同一过滤语义。
3. stamp 删除以数据库当前行与 expectedStamp 做条件写；成功设置 IsDeleted/DeletionTime/DeleterId 并推进 stamp。陈旧 stamp 抛 CrestConcurrencyException，行与审计字段均不变。无 stamp 实体沿普通删除契约，不虚构并发控制。
4. 普通 id 删除不存在/已隐藏行保持无操作；stamp 删除条件匹配零行保持并发失败，不额外泄漏其他租户是否存在。重复无 stamp 删除不得反复刷新删除时间/删除人。
5. DeleteRange 带 stamp 实体当前没有「每行 expectedStamp」协议：统一预检整个输入后明确拒绝（含 predicate 版本按实体类型判定），不能部分执行或跳过并发保护。无 stamp 批次按 §3.3 的事务保证执行，异步枚举/物化不能造成先写后拒绝。
6. 不新增隐式 HardDelete/Restore。若现有正式消费者确需管理已删除行，先形成 tenant-preserving 的内部查询入口；不得为普通读软删除行调用 IgnoreQueryFilters 而一并取消租户约束。
7. 既有 GetQueryableUnfiltered 是低层绕过入口，须清点消费者并明确限制；本项不把它包装成普通软删除查询能力。没有可执行 IQueryable adapter 的 FreeSql/SqlSugar GetQueryable/GetQueryableUnfiltered 明确 NotSupported，不保留 ToList().AsQueryable 假翻译；保留真实 SDK 查询的异步仓储方法。

### 3.2 租户与审计

租户主键只用 `ICurrentTenant.Id`/TenantId。Provider 不解析 token/claims，不造第二租户目录。复用现有不同实体契约，逐项冻结：

| 实体标记 | tenant A 普通操作 | host/null 上下文 |
| --- | --- | --- |
| Domain.Shared 的 IMustHaveTenant | 仅 A；插入空 TenantId 自动填 A；显式 B 写入拒绝 | 普通写拒绝；普通读不自动获得全租户视图 |
| DataFilter 的可空 IMultiTenant | 查询 A 与显式 null 共享行；普通新增空值填 A；禁止写 B | host 自身 null 行；跨租户管理使用既有显式治理边界 |
| EF 专用 IMultiTenant | S1 列出现有实体与消费者，再生成静态适配；不强迫业务同时实现两套接口 | 现有 host-null=全租户是兼容差异，必须有迁移说明/真实负例后收紧，不能悄改 |
| 无租户标记 | 不附加租户语义 | 不附加租户语义 |

上表是本项建议的默认收紧方案，不冒充当前所有接口已同义。S1 必须锁定 host 管理和共享行的实际消费者：需要跨租户管理的内部路径必须显式标出，不能以普通 host scope 作为自动 bypass，也不能复制权限逻辑。

查询和 mutation 均校验持久租户事实；仅检查调用方提交的 entity.TenantId 不足以防伪造 detached entity。各 SDK 的 id/predicate/batch 写不能依赖查询 filter 自动覆盖写 SQL。原有带租户/软删除的模型过滤器必须组合，不能被后一次 HasQueryFilter 替换。

审计继续使用现有 ICurrentUser 权威；FreeSql/SqlSugar 的 ICurrentUserProvider 通过唯一 typed adapter 取同一身份，不建立独立用户上下文。一次操作使用同一 UTC 时间/用户快照，首次创建字段保留，修改字段/删除字段从真实持久化回读验证；不把实体 audit 字段等同审计日志事件。

### 3.3 事务、生命周期、取消

- `AddUnitOfWork` + Provider typed binding + BeginScope/StartAsync 是唯一平台链；SDK manager 只是 Provider 内部资源，不成为第二套业务入口。
- 同一 UoW 中两个仓储必须共享同一实际事务。Begin 后仓储 insert/update/delete 随 Complete 提交，异常/rollback-only/abandon 后用新连接证明不可见；只有「Begin/Commit 被调用」的 mock 不算证据。
- SDK client 的连接/事务/tenant/filter hook 不得跨请求 scope 共享可变状态。模块和扩展统一转发一个装配核心；SDK root 可按实际能力共享，scoped tenant/user 不得捕获进 singleton hook。
- RequiresNew、未知 isolation、CAP lease、跨资源原子性等沿 #137 已有 binding/gate 明确拒绝或保留已有支持；本项不放宽未验证能力声明。
- 每次操作组合调用方 token 与受管执行 token，复用 UnitOfWorkExecutionContext；适配器不能吞取消或把 Task.Run 当 SDK 命令可取消的证据。若 SDK 不支持执行中取消，要明确限制；不能宣称有界终止。已知无法满足所请求能力时先于业务/SQL 拒绝。
- 无事务模式逐写可持久化，不承诺批次原子性；事务模式中保证失败整批回滚。提交事实、通知失败、清理结果继续使用 #137 的四维结果，事件在持久状态变更之后发布。

## 4. 实现落点与唯一主链

### 4.1 EF Core

修复两类仓储的 stamped Delete，软删除不能再走 ExecuteDelete。优先复用 tracked 删除 + 唯一 AuditInterceptor 与 concurrency original value 协议，保证 UoW 内延迟 flush 仍在 Complete 处报告冲突。按 id 加载时应用 tenant/soft-delete 过滤；detached entity 写入先验证持久行归属，不能相信传入字段。

补齐静态软删除 filter，与现有租户模型配置组合。生成器只为已声明实体生成强类型配置，不能通过运行时 GetTypes/动态表达式扫描补齐。需要 SQL 层 TenantId 条件或原始租户并发值的实体，在生成 mapping/Provider 内落位，保证查询后到写入间也不发生越租户突变。具体 EF SQL 由 S1 真实关系型探针冻结，不凭 EF InMemory 通过验收。

### 4.2 FreeSql / SqlSugar

以现有异步仓储为执行面，在 Provider 包内部统一创建带 tenant + live-row 条件的查询/写入命令。删除显式执行条件 UPDATE 及 audit/stamp 更新，不能依赖 CurdBefore/DataExecuting 将 DELETE 自动改写。审计值由一个 Provider 内部动作入口处理，避免 hook 和仓储各自改写两次。无软删除实体继续 DELETE。

FreeSql 仓储必须实测绑定当前 SDK UoW 的 connection/transaction；注释「Orm 自动跟随」和 SDK Binding 方法存在不算证据。SqlSugar 改为每 scope 隔离事务状态，并确保仓储和 UnitOfWork 注入同一 client。迁移 singleton 注册、捕获首个 tenant 的过滤器与无效 ConfigureSoftDelete hook；旧入口要么转发真实装配核心，要么明确拒绝并说明替代调用。替换文件软删除归档。

不通过 service locator/扫描注册仓储。保留的手写扩展以闭合泛型显式 DI 注册；不新增 SDK 之外的运行时能力发现机制。

### 4.3 编译期入口与明确拒绝

本项不实现 FreeSql/SqlSugar 的完整 generated CRUD/IQueryable。两个仓储生成器中的相关声明统一编译期 Error，指向受验证的显式仓储路径；EntitySourceGenerator 不能继续生成直接 SDK CRUD 绕过契约。未知 Provider 同样 Error，不再默认 EF。生成器诊断编号在 S1 对照现有编号分配并冻结，不能靠 CS 错误/空输出/Warning 代替拒绝。

EF 正式生成仓储必须调用经过修复的 EF 执行路径。Source Generator 负责声明/typed mapping，BuildTasks 继续已有 CRUD/JSON 聚合；不新建 ORM BuildTask 管线。

不新造一个与 UnitOfWorkProviderCapabilities 并列的运行时支持注册表。文档矩阵描述证据，已有 UoW binding 承担可执行 gate，Provider 局部 API 承担不支持查询/注册方式的诊断。

## 5. 验收矩阵与证据纪律

保留 Provider 首轮共同数据库为**独立文件 SQLite**（真实关系型，不能用 EF InMemory 替代）；另复跑 EF + Npgsql/PostgreSQL 原有门禁并覆盖正式生成 CRUD 的关系型查询。SQLite 通过不推导 SQLServer/MySQL/PostgreSQL 其他 SDK 组合；后者如需升 supported，必须新增对应真实数据库用例与驱动。

| 编号 | 各保留 Provider 的真实执行要求 |
| --- | --- |
| T01–04 | A/B/host 上下文查询，optional 共享行；同 scope 切换、两个并行 scope；读/计数/分页一致且 SDK 命令在 DB 过滤 |
| T05–07 | 伪造 detached TenantId、跨 tenant id/predicate/batch 插入更新删除；精确拒绝/隐藏，业务表与 audit 字段零变化 |
| D01–05 | 五种删除重载；过滤前后独立读原表，存留行/IsDeleted/删除时间/删除人；普通读取全集隐藏；非软实体确实物理删除 |
| D06–08 | 当前/陈旧 stamp、重复删除、并发删除（独立连接/事务）；每行有且仅有可解释的胜者，失败零写；批次 stamp 预检拒绝 |
| A01–03 | 创建/修改/删除 audit 的真实持久化回读，用户切换与 scope 隔离，原始创建字段不变 |
| U01–05 | 正式注册+两个仓储+真实 Begin/Complete/异常/abandon/Required；新连接检查持久化与回滚；事务身份能对应实际写命令 |
| U06–08 | 已有不支持的 RequiresNew/isolation 在业务与 SQL 前拒绝；取消后连接可再使用，无隐藏遗留事务；不混淆取消来源 |
| G01–04 | 真实 Attribute/实际 analyzer 的消费项目 build；非 EF 生成请求与未知 Provider 预期诊断；EF 生成实体/仓储可运行；无 runtime scanner fallback |
| Q01 | EF 正式 generated CRUD 的 decimal/DateTime/nullable/enum 过滤、排序/分页经真实 SQL 执行，接唯一 UoW；不能仅验证表达式构造 |
| R01–03 | 注册顺序/重复/缺失、模块与扩展一致；missing scope/context/hook 明确失败；现有样例与 #137/#128/#129 受影响回归 |

数据库 fixture 每例独立数据库/schema（PG `itest_{guid}`），不复用保留资产。测试不能 override 要验收的生产重载。跨连接回读排除 ChangeTracker/SDK cache 假象。异步取消/并发测试须有持久事实控制，不能用 Task.Delay/异常类型代替结果断言。

至少保留三类故障控制：将逻辑 UPDATE 改为 DELETE 必须使 D 失败；拿掉租户条件必须使 T 失败；解除仓储事务绑定必须使 U 失败。负控失败要对应目标断言，不能被启动/映射错误提前满足。

执行模式逐项声明：沿用并复跑现有 UoW 原生门禁，覆盖本项触及的正式 runtime；这只证明其 fixture。若要升级 ORM 为 trimming-verified/NativeAOT-verified，必须单独 fixture 经过真实 DB/实体/仓储/事务/过滤/audit，分别 publish → link（AOT）→ 运行产物，含成功与失败控制、sentinel、退出码、进程回收、源码 SHA/数据库/驱动/RID 记录。未完成时保持「未验证」，本项不以全 SDK AOT 化为隐含交付承诺。

## 6. 交付与风险

最终支持矩阵建议落 `docs/Feature/OrmProviderSupportMatrix.md`，配套迁移说明、逐场景证据索引、tests 及 memory.md。数据库/SDK 版本以实际消费锁定版本为准（当前 FreeSql 3.5.308、SqlSugarCore 5.1.4.214）；升级版本不能无复验沿用旧证据。

主要兼容变化：物理删除变为真实软删除；batch stamp 的无保护行为转为拒绝；伪 IQueryable 转为明确不支持；host 普通读不再隐式全租户；生成器静默空输出/直连 SDK 分支转为 Error；singleton client 改 scoped。每项在 S1 列出消费者和迁移用法，先测试后切换，不保留第二条长期执行链。

实施分 S1 基线与契约、S2 删除闭环、S3 租户/审计/UoW 装配、S4 生成诊断与关系型组合、S5 证据与迁移交付。计划见 [实施计划](../plans/2026-10-22-issue-130-provider-matrix-soft-delete-implementation.md)。当前仅完成源码盘点、既有套件复跑和现状探针，未创建实现 PR、未更改生产 runtime、未恢复 CI、未合并或对外发布设计。
