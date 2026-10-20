# #129 实施 issues log（生成 HTTP 控制器退出与正式投影收口）

设计：`docs/superpowers/specs/2026-10-17-issue-129-generated-http-controller-exit-design.md`（rev.1，GitHub 评论 6228947951）
S1 事实稿：`docs/superpowers/specs/2026-10-17-issue-129-s1-callgraph-and-baseline.md`
计划：`docs/superpowers/plans/2026-10-17-issue-129-generated-http-controller-exit-implementation.md`

环境态：CI 全部 workflow 保持手动禁用（用户要求，本地校验为准）；容器套件经 `DOCKER_HOST=unix:///run/user/1000/podman/podman.sock` 本地可跑。

---

## S1 事实/设计冻结（2026-10-17）

基线：`origin/master = 196b6f66048ef32d644eeb856d00d58ae9d34965`（PR #150 合并后）。工作分支 `feat/issue-129-generated-http-controller-exit-20261017-114410`。

### 已建立的事实

1. 复现前置：必须先 `dotnet build src/Tooling/CrestCreates.BuildTasks`，否则下游项目并行构建报 `MSB4062`（UsingTask 程序集缺失）。这一条不是本任务引入的缺陷，但会污染一切「先 build 再读产物」的证据，必须在文档里写明。
2. Ticket 的 HTTP 面 11 条端点全部来自 `DynamicApiAotSourceGenerator` 的 legacy `[CrestService]` + `GeneratedApiController/ApiOverride` 两条分支；`GET api/ticket/all` 由 `TicketApi`（`ApiOverride(CrudAction.GetList)`）唯一拥有，权限名 `Ticket.GetAll`。`TicketAppService` 今天没有任何 HTTP 投影 Attribute。
3. 端点级 `IAuthorizeData` 为空（11/11），鉴权发生在 handler 内 `DynamicApiGeneratedRuntime.EnsurePermissionAsync`；401 由 `UnauthorizedAccessException` 经 `DefaultCrestExceptionConverter` 产生。
4. body 绑定实际走 `DynamicApiGeneratedRuntime.ReadBodyAsync → CompatibilityBodyReader` 的反射 `JsonSerializer.Deserialize<T>(payload, options)`；UoW 走 `ExecuteAsync(context, new UnitOfWorkOptions(), …)`。
5. `DynamicApiRouteConvention` 的 6 个反射/描述辅助成员在仓库内零外部消费者，唯一活代码点是静态 `IsScalar`，被 `DynamicApiGeneratedRuntime.ValidateAsync:67` 调用（不存在名为 `LegacyRuntime` 的类型）。
6. `DynamicApiScanner` / `DynamicApiEndpointExecutor` 在 src/samples/tests/solutions 全零命中（早已退出），AGENTS.md 的表述是文档残留。
7. compat 投影链（`AppServiceCompatibilityGenerator`）真实消费者只有 Asset/Procurement 两个 Host 门面 + 2 个测试 fixture；其注册形态是 5 个 `ModuleInitializer` 写进程全局注册表，组合输入信封由 `AddSource` 在同轮 CoreCompile 内产生（官方 STJ 看不见），因此与 #128 的 ExplicitHost + JsonContracts 闭包不同轨。
8. 旧 CRUD bundle（`GeneratedCrudRegistration_*`、旧 `bool ExecuteAsync` 发射分支、`#warning`）在 src/samples 零消费者，仅生成器测试 harness 仍引用。

### 真实 HTTP 观测（`WebApplicationFactory<Program>` + Testcontainers PG）

| 场景 | 实测 |
| --- | --- |
| 合法 `POST /api/ticket` | 200，`{"data":{…},"code":200,"message":"操作成功"}` |
| `POST /api/ticket` 空 body | 400 `Crest.Request.InvalidArgument`（来自 `Ticket.SetTitle` 的 `ArgumentException`，不是请求校验） |
| `POST /api/ticket` 坏 JSON | 500 `Crest.InternalError` |
| `GET /api/ticket/{未知}` | 404 `{"code":404,"message":"资源不存在"}`（`DynamicApiResponse` 形态） |
| `GET /api/ticket/not-a-guid` | 500 |
| `GET /api/ticket/resolve?id={未知}` | 404 `Crest.Entity.NotFound` + details |
| `GET /api/ticket/close?id=zzz`、`assign?id=`（缺 agentId） | 500 |
| `PUT` 陈旧 `concurrencyStamp` | 200 且成功覆盖 |
| `DELETE` 缺 `If-Match` | 200 且已删除 |
| 匿名 GET/POST | 401 `Crest.Auth.Unauthorized` |
| 读响应 `concurrencyStamp` | 字段存在但恒为 `""` |
| `title="ab"`（违反 5-200 校验器） | 200 创建成功（校验器未执行） |

### 新发现的阻塞级基线缺陷（rev.1 设计未覆盖，必须进 rev.2）

- **EfCore UoW 绑定三重注册**：平台预设（`src/Platform/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs:138-139`）+ 样例 EF 模块（`samples/SaaSHelpdesk/SaaSHelpdesk.EntityFrameworkCore/Modules/EntityFrameworkCoreModule.cs:50-51`）+ 测试 fixture（`Fixtures/HelpdeskWebApplicationFactory.cs:384-385`）。`AddUnitOfWorkProvider` 不去重（`UnitOfWorkServiceCollectionExtensions.cs:124`），`UnitOfWorkProviderBindingRegistry.cs:26-37` 首次解析抛 `InvalidOperationException: Duplicate unit-of-work binding for provider 'EfCore'`。后果：master 上 SaaSHelpdesk 的**全部写动作**（生产 Host 与测试 Host 同）在 UoW 装配索引构建期失败，表现为 HTTP 400 `Crest.Operation.Invalid`。这解释了 `TicketApiTests` 的写例红。归属 S3（R01），S1 只用红例锁定，不做装配决策。
- **Ticket 权限名无定义**：投影要求 `Ticket.*`，样例只定义 `Helpdesk.Tickets.*`，`EntityPermissionsManifest.json` 为 `{"permissions": []}`（构建日志「with 0 entities」）。后果：除 SuperAdmin 外无人可调用任何 Ticket 端点，H03 的 403/200 对照无法成立。归属 S3（R02）。

### 入库测试

- 绿基线 `samples/SaaSHelpdesk/SaaSHelpdesk.Tests/TicketWireBaselineTests.cs`：6 条（冻结 11 条端点矩阵、端点授权元数据为空、匿名 401、`api/ticket/all` 来源与权限名、状态机语义保持（close 后 resolve → 400 `Crest.Operation.Invalid`）、无 SLA 时 assign 的 `dueDate=null`）。
- 红基线 `samples/SaaSHelpdesk/SaaSHelpdesk.Tests/TicketProjectionGapBaselineTests.cs`：8 条 R01–R08，每条只因其声称缺口失败；写路径红例内部使用显式命名的「测量归一化」消除 R01，保证归因单一。
- 采样 harness 已移出编译面：`99_RecycleBin/issue-129-s1-scratch/TicketWireCaptureTests.cs`（原样保留，仓库规则不删除文件）。

### 计数

| 套件 | 结果 |
| --- | --- |
| `SaaSHelpdesk.Tests` 全量（最终 head） | 39 pass + 72 fail / 111，0 skipped。约定红 10 条：`R01`、`R03`–`R08`、`F02`、`R14`、`U02`；既有红 62 条按 §9.2 归因不变 → 新增回归 0 |
| `SaaSHelpdesk.Tests` 新增测试 | 12 条新增中 8 条为事实/正向控制（`C11/C12/C13`、`P09/P10`、`U01/U03/U04`）全绿，4 条为目标红（`F02/R14/U02` + `R01` 保持） |
| `CrestCreates.CodeGenerator.Tests` 全量 | 457 pass + 1 fail / 458；唯一失败是同轮信封 MSBuild 探针在与其他 450 条并行时 12 分钟超时（非契约失败）。已改为与 CRUD 探针同一串行集合并把上限提到 20 分钟；单跑复核进行中（负例先完成且未报失败，正向对照仍在冷构建），结果在本文件后续补记 |
| `SaaSHelpdesk.Tests` 定向（新增两类） | 6 pass + 8 约定红 / 14 |
| `SaaSHelpdesk.Tests` 全量（含新增 14 条） | 29 pass + 70 fail / 99；其中 8 条是本文约定红，**62 条为 master 既有红**（非本切片引入） |

- 62 条既有红按错误消息归因：32 条消息体为 `400 Crest.Operation.Invalid`（写动作撞 R01 的 UoW 重复绑定）、17 条同为 `400`（断言未打印 body，同属写路径）、10 条 `POST /api/user` → `404`、2 条 `AggregateException: Unable to resolve service for type 'ITenantProvider'`（`AuthorizationStartupTests`）。
- `POST /api/user` 404 的静态原因：框架项目 `src/Framework/Ddd/CrestCreates.Application` 的编译期生成目录只有 `SchemaCapabilitySourceGenerator` 与 `ServiceSourceGenerator` 输出，**没有 `DynamicApiAotSourceGenerator` 产物**（`obj/Debug/net10.0/source-generators/CrestCreates.CodeGenerator/` 仅两目录），尽管 `UserAppService` 带 `[CrestService]`（`Identity/UserAppService.cs:17-18`）。也就是说 `AddApplicationServiceAssembly<UserAppService>()` 在生成面上是空集合。归属与影响面待 S3 判定（它同时决定样例里 Setting/Feature/AuditLog/User 等框架服务是否有任何 HTTP 面）。
- 结论：#129 的「sample 端点、认证/授权、租户和失败响应的真实集成用例通过」这一验收，在 master 上无法由现有样例套件直接证明；S3 必须在建立单一 UoW owner 与权限定义的同时修复这批基线红，或在迁移表中显式判定哪些服务应退出 legacy 面。

### 支持声明

- 本切片不新增任何支持声明。`NativeAOT-verified` 仍只覆盖 #128 已落证的「正式 CRUD + JSON 主链（linux-x64）」。Ticket 线路、手写服务正式投影、双租户选库、SaaSHelpdesk 整体在 S1 只取得 JIT/单 Host 观测事实。

### S1 未做（诚实清单）

1. S2 级契约探针（引用程序集 symbol identity + 真实 MSBuild 前阶段复跑，验证组合信封同轮闭包与 ExplicitHost 激活）未入库——按计划归属 S2 起点，本文 §8.2 已记录其当前事实与验收口径。
2. 双租户生产选库 factory 未建立（H04 前置）。
3. `dotnet run` 真实宿主直跑在未定位原因前长时间不监听，本切片证据全部来自 `WebApplicationFactory` 宿主；该限制已写明（§8.4）。
4. 设计 rev.2 尚未回写 GitHub（§2 的 6 条更正 + 两条阻塞缺陷需要发布）。

### 下一步

- 提交 S1（docs + 测试 + 回收站），推送分支并开 PR 请求设计审查；审查通过前不进入 S2。


## 复核注记（2026-10-17，保留上文作者原始记录）

PR #152 head 602d6e40 独立定向复跑6绿+8约定红/14，0 skipped。额外真实PG+manager/checker+AppService控制2/2通过：未定义Ticket.Get可授予非SuperAdmin并通过checker；只授Ticket.GetAll时真实GetAllAsync拒绝Ticket.Search。Helpdesk常量也不是已注册definition。上述原文“除SuperAdmin外无人可调用”应撤回。

生产组合静态为平台+EF模块两份binding，factory加第三份；生产进程尚未直跑成功，不能写成生产HTTP已经观测。全套既有红分类32+17+10+2=61，与62总数差一项，需FQN账本。R06只断状态、R07只断非空，尚不证明未删除和当前stamp精确一致。

目标契约以设计rev.2 §14为准：正式五权限、GetAll统一Search；Platform/Application持久化owner明确；Web404统一；User等缺失HTTP面单独记账；S1同轮闭包/Host激活/contract fixtures须补件后复审，再进入S2。当前S1未放行，CI暂停、不合并。详见2026-10-17-issue-129-s1-design-review-round1.md。

---

## S1 补件 R2（2026-10-17，回应 PR #152 复核 R1 的三项 P2 与账本修正）

审查 head：`602d6e40`；本轮在同一分支继续，未触碰生产实现（唯一非测试改动是测试 fixture 的装配声明，见「fixture 修复」）。CI 仍全部禁用。

### 对应关系

| 发现 | 补件 | 状态 |
| --- | --- | --- |
| R1-HTTP129-01 权限目录≠有效授权 | 撤回「除 SuperAdmin 外无人可调用」；R02 拆成 F02（五定义目录，红）+ C11/C12/C13（真实非 SuperAdmin 登录 + 真实 grant/revoke 的 HTTP 控制）；旧六名快照继续由 `TicketWireBaselineTests` 锁定；正式投影不再引入 `Ticket.GetAll` | 已入库；C11/C12/C13 绿，F02 红 |
| R1-HTTP129-02 契约冻结/探针未交付 | ① 真实 `dotnet build` 的同轮可见性探针（compat 生成的组合信封 vs 普通 Compile 对照，官方 STJ 生成器实测）；② 冻结契约 fixture `docs/superpowers/specs/fixtures/issue-129-projection-contracts.json` + 5 条一致性校验测试（失败桥 code/status 对既有权威、Runtime 传输中立、权限映射恰好覆盖 11 契约方法且只出现五个定义名、manifest/action identity 字段沿用 #128 形状、formal→legacy 边禁止） | 见「计数」 |
| R1-HTTP129-03 零副作用与当前 stamp 断言不足 | 新增 `TicketWriteSideEffectProbe`（测试侧装饰 `ITicketAppService`/`IUnitOfWorkManager`，生产无新增公共扩展点）；R03/R04/R08 断言状态码 + 业务调用集合 + 内核执行次数 + fresh-scope 持久化不变；R05 精确比较原标题与原 stamp；R06 fresh-scope 行仍在 + stamp 不变 + 再次请求仍 428；R07 响应 stamp 与数据库当前 stamp 精确相等；新增正向控制 P09/P10（合法 stamp 更新与 If-Match 删除，今天即绿，证明计数器不是空集） | 已入库并实测 |
| 账本 32+17+10+2=61 与 62 差额 | 三次全量差集逐 FQN 归因：51 条为 R01 装配冲突（在单绑定实验中转绿）、10 条为 `POST /api/user` 404（框架 HTTP 面缺失，按 rev.2 §14.6 单列）、1 条 `ITenantProvider` 容器校验失败；无「凭 400 推断同根因」的未归因项 | 已入库（S1 事实稿 §9.2） |
| rev.2 §14.7.5 装配来源证据 | `TicketPersistenceOwnershipBaselineTests`：U01 样例模块单独恰好 1 份绑定（绿）；U02 按程序集 MemberRef 实测引用绑定声明 API 的装配为 `CrestCreates.Web` 与 `SaaSHelpdesk.EntityFrameworkCore`，目标 1 个 owner（红）；U03 测试程序集不再声明 owner（绿）；U04 宿主仍可观测 >1 份，防止用删除测量路径冒充修复（绿） | 已入库并实测 |

### 同轮可见性探针口径

- 负例先断 producer 确实运行（本次构建产物里存在 `OrderAppService_Search_CompatibilityInput`），再断普通 Compile 的 `JsonSerializerContext` 无法解析它（实测 `CS0234`），并断官方 STJ 生成目录不含其 metadata。
- 正向对照用形状相同的普通 Compile 信封，实测由官方 STJ 关闭。两条都是**事实控制（绿）**，不是目标红：它们锁定「同轮不可见」这一机制，S2 的正式前阶段必须让手写投影落到普通 Compile 一侧，届时负例的构造方式会随之改写（不能靠放宽断言转绿）。

### fixture 修复（rev.2 §14.3 明确指令）

`samples/SaaSHelpdesk/SaaSHelpdesk.Tests/Fixtures/HelpdeskWebApplicationFactory.cs` 删除自身对 `AddUnitOfWork(OrmProvider.EfCore)` / `AddEfCoreUnitOfWork()` 的调用：fixture 只替换连接与叶子设施，不再声明第三个 owner。删除后宿主仍有 2 份冲突绑定（全量计数不变，70 fail），证明必须修的是装配 owner 而非测试代码；R01 目标计数继续固定为 1。

### 审查者的两条事实控制

`docs/review/probes/2026-10-17-issue-129-s1-permission-facts.patch` 以 `git apply` 原样入库为 `Review129S1PermissionFacts.cs`，实测 2/2 通过。

### 计数

| 套件 | 结果 |
| --- | --- |

---

## S1 补件 R3（2026-10-17，回应 head `09f73642` 复审）

环境态：CI 仍全部禁用；本轮生产实现不变，改动集中在测试与探针。

| 项 | 内容 | 实测 |
| --- | --- | --- |
| R3-1 同轮探针归因 | 改用 emit 的真实 FQN `CrestCreates.Generated.OrderAppService_Search_CompatibilityInput`；诊断采集纳入 warning；authored 文件禁止出现点名信封的 CS0234/CS0246；同一 context 增普通 Compile 根作为同次构建内 STJ 运行证据 | 待单跑（探针 3 条，共享 2 次冷构建） |
| R3-2 `U02` 误判 owner | `U02` 降为归属事实控制（绿，逐字列出两个装配调用点）；新增 `U05` 锁定真正的注册表不变量（同 provider 双绑定构建期确定性拒绝 / 单绑定通过 / 空图在 `GetRequired` 拒绝） | 样例侧：`U02`、`U05`、`U01`、`U03`、`U04` 全绿 |
| R3-3 `P09` 非法正例 | 载荷改为数据库 fresh-scope 回读的当前 stamp，并先断其非空；补业务调用恰一次、受管执行恰一次、行数不变、标题落库、stamp 轮转，以及用轮转值再发第二次更新的回读控制；`P10` 同步为真实 `If-Match` + 行数减一 | `P09`、`P10` 绿（此前 `P09` 因把 because 文案误当期望元素而虚红，已修） |
| R3-4 跨 Host 激活控制 | 同次构建生成语法树 AST 取证：`[ModuleInitializer]` 内向进程全局注册表写入 ≥4 处；输出中无 `ICapabilityEndpointContribution`/`AddCrestCapabilityEndpointContribution` | 待同批单跑 |

计数（本轮实测）：

| 套件 | 结果 |
| --- | --- |
| `SaaSHelpdesk.Tests` 全量 | 41 pass + 71 fail / 112，0 skipped；约定红 9 条（`R01`、`R03`–`R08`、`F02`、`R14`），既有红 62 条与 R1/R2 同一集合 → 新增回归 0 |
| `SaaSHelpdesk.Tests` Ticket 五类定向 | 18 pass + 9 约定红 / 27；`U01`、`U02`、`U03`、`U04`、`U05`、`P09`、`P10`、`C11`、`C12`、`C13` 与 `TicketWireBaselineTests` 6 条全绿 |
| `AppServiceHttpProjectionRoundProbeTests`（R3-1 + R3-4，单次真实构建共用） | 3 pass / 3，0 fail，耗时 15 分 9 秒：负例 `SYSLIB1030` 点名 `OrderAppService_Search_CompatibilityInput` 且 authored 文件无点名该信封的 CS0234/CS0246、STJ 无其 metadata；同次构建内普通 Compile 根 `ProbeOrdinary.OrdinaryEnvelope` 被 STJ 关闭；`[ModuleInitializer]` 内向 ≥4 个进程全局注册表写入且无 `ICapabilityEndpointContribution`/`AddCrestCapabilityEndpointContribution` 接缝 |

| `CrestCreates.CodeGenerator.Tests` 全量（最终 head `214426ea`） | 459 pass + 0 fail / 459，0 skipped，22 分 30 秒；上一 head 的唯一超时失败已随「单次构建 + 串行集合」消除 |

S1 补件 R3 到此三 P2 与跨 Host 控制均有实测支撑；是否放行由维护者判定，本轮不自称完成。

---

## S1 补件 R4（2026-10-17，回应 head `05fa50cb` 复审的两项 P2）

| 发现 | 补件 | 实测 |
| --- | --- | --- |
| R3-HTTP129-01 修改后未从 HTTP 回读 stamp | 新增目标红 `R15`：GET 预热 → 用数据库当前 stamp 合法更新（存储 stamp 轮转）→ 再 GET，响应 stamp 必须等于新存储值；`R07` 保留首次回读；`P09` 措辞收窄为业务/UoW 正向（明确不宣称客户端响应 stamp 可复用），两类事实分开记账 | `R15` 红：`served=""` vs 存储 `3d08…`；`R07` 红同因；`P09` 绿 |
| R3-HTTP129-02 Execute 计数不能证明零事务；R04/R08 断言缺 | 计数器拆分 `LogicalExecutions` / `TransactionsStarted` / `UnobservedExecutions`；`BeginScope` 返回计数包装 scope（覆盖 `StartAsync` 正式入口），内核帧内解析不到 ambient 记入 `UnobservedExecutions`；新增 `P11` 真实 `BeginScope + StartAsync` 正例（断 `DbContext.Database.CurrentTransaction != null`、事务计数 1、逻辑执行 0）；`R03/R04/R08` 补齐零业务/零逻辑执行/零事务/零未观测/持久化不变 | `P11` 绿；`R03/R04/R08` 仍为既有目标红，新增断言均通过；`P09/P10` 断 `TransactionsStarted == 1` 通过 |

计数（本轮实测，最终 head 见提交）：`SaaSHelpdesk.Tests` 全量 **42 pass / 72 fail / 114**，0 skipped；约定红 10 条（`R01`、`R03`–`R08`、`R15`、`F02`、`R14`），既有红 62 条与 R2/R3 同一集合 → 新增回归 0。本轮只改样例测试与探针设施，未触及 CodeGenerator 套件（上一 head 459/459 仍适用）。

仍未闭合（不声称完成）：客户端响应 stamp 的可复用性在 S1 只作为目标红记录，生产映射修复属 S3；双租户选库 factory、生产启动根因按 rev.2 属 S3；引用程序集声明范围的小范围设计增补仍是对应 S2 的前置设计门禁。

---

## 设计增补：引用程序集声明范围（2026-10-17，S2 前置门禁）

- 交付：`docs/superpowers/specs/2026-10-17-issue-129-referenced-declaration-scope-addendum.md`（决策、身份字段、DI 契约、取消语义、S2 验收 R-01–R-06）。
- 契约 fixture 同步：`docs/superpowers/specs/fixtures/issue-129-projection-contracts.json` 增 `referencedAssembly` 段；action identity 增 `httpOwnerAssemblyName`/`contractAssemblyName`/`closedContractMethodSignature`；artifact entry 增并列字段 `ContractAssemblyName`。
- 事实探针（本次入库，2/2 绿）：`ReferencedAssemblyScopeProbeTests`
  - `R01a`：引用程序集内部声明的 `[CrestService]` + projection 不被消费编译发现（生成物为空、诊断为空）——这是「引用服务必须由本地锚点声明」的事实依据；
  - `R01b`：本地 facade 实现引用程序集里的契约时，投影照常生成——保留现有唯一声明链。
- 当前可表达性事实：`CrestServiceAttribute` 仅 Class（`CrestServiceAttribute.cs:4-5`）；projection 属性仅 Class|Method 且无 `ContractType`（`CapabilityCompatibilityProjectionAttribute.cs:9-25`）；生成器语法提供器只扫本编译 class（`AppServiceCompatibilityGenerator.cs:40-64`）；`EnumerateContractTypes` 允许契约来自 metadata。
- 决定：不新增程序集级 Attribute、不扫描引用程序集、不用反射补洞；以「本地标记类 + `ContractType`」为唯一入口，标记类不注册/不实例化、方法集合只来自契约；契约缺失注册 → 启动期 `CCHTTP007`；身份与 manifest 分离 owner 与 contract assembly。
- 验证：新增/更新测试 `ReferencedAssemblyScopeProbeTests`（2）+ `Issue129ContractFixtureTests`（6）共 8/8 通过；本轮未触及样例与探针构建，S1 放行结论不受影响。
- 状态：**设计增补 rev.4 待审查**；审查通过后才进入 S2 实现（不合并、CI 暂停）。

### 增补 rev.2（回应 2 项 P2：产物所有权与取消契约）

1. **产物所有权分开**：`DeclaredTypeFullNames` 只记录 artifact 自己声明/生成的类型；引用 DTO/契约另立并列字段 `ReferencedTypeFullNames`。依据是 CCCRUD008 的权威对账（`CrudJsonSurfaceDiscovery.cs:184-233` 把该字段与 `CrudArtifactEmitter.DeclaredTypeFullNames(artifact.Source)` 逐项比较），混入引用类型会直接触发「declared types or content hash do not match」；审查实测正反 2/2 与此一致。fixture 增 `manifest.ownership`（declaredOnly/字段名/禁止项/权威位置/证据）。
2. **取消契约完整冻结**：按 token 身份判定，分四类写清传播、HTTP 状态/响应与 UoW 事实——调用方中止（无 JSON 错误体、可中止连接、不重执行）、受管 deadline（响应未开始则 500 `Crest.InternalError` 脱敏，否则中止不二次写；`Unknown`/`Committed` 保留）、非调用方 OCE（内部故障 500 脱敏，不得当取消丢弃响应）、提交后取消/响应写失败（Committed 保留、不回滚）。
3. **验收矩阵同步**：R-06 拆为 R-06a/b/c；增补 §4.1 覆盖 rev.2 §10 的 H02/H07/N01/N02/D01 四行含义。
4. 验证：`ReferencedAssemblyScopeProbeTests`（2）+ `Issue129ContractFixtureTests`（8）共 **10/10 pass**（秒级）；新增 fixture 断言覆盖所有权字段分离、四类取消、验收矩阵扩展与增补文本一致性。

### 增补 rev.3（回应取消契约剩余 P2：原取消源与绑定阶段）

1. **按原始取消源分类**：定义 `R`=RequestAborted、`K`=受管内核/deadline token、`L`=linked token；deadline 会同时取消 `K` 与 `L`，因此不再用「`L` 已取消」判定调用方中止。分类顺序：`OCE.Token == R → CallerAbort`；`== K → ManagedDeadline`；`== L → R 是否取消 ? CallerAbort : ManagedDeadline`；第三 token/None → `NonCaller`；身份不可读 → `NonCaller`。
2. **同时取消的优先级**：`R`+`K` 同时取消且身份解析到 `L` 时，分类与响应抑制按 `CallerAbort`，事实同时记录 `CallerAbort+Deadline`（不丢 deadline 事实）。
3. **分类与响应抑制分开**：响应抑制只看传输状态（`R` 已取消或响应已开始则一律不写体，任何分类都不二次写入）；否则按分类写（`CallerAbort` 永不写体；`ManagedDeadline`/`NonCaller` → 500 `Crest.InternalError` 脱敏）。因此「客户端断开 + 第三 token OCE」归因 `NonCaller`，但响应仍被抑制，不改成调用方中止。
4. **绑定/验证阶段沿用同一规则**：`OCE(R)` → 无业务/无事务/无响应体；`OCE(K)` → 500 脱敏（响应未开始且 `R` 未取消）；第三 token/None → 500 脱敏；普通绑定失败仍走 converter 权威。
5. 验收：R-06 扩为 R-06a–e（新增 R-06d 链接身份解析与同时取消优先级、R-06e 绑定阶段同规则）；fixture 增 `tokens`/`classification`/`precedence`/`responseSuppression` 并同步 `bindingStage`；H02/H07 描述同步。验证：`ReferencedAssemblyScopeProbeTests`(2) + `Issue129ContractFixtureTests`(8) = **10/10 pass**。

### 增补 rev.4（回应取消分类边界 P2：None 先处理、来源必须已取消）

1. **None/default 先于身份匹配**：`OCE.Token` 为 `default(CancellationToken)`（含无 token 的 `new OperationCanceledException()`）直接归 `NonCaller`。否则当 `R` 本身是 None（无请求 token 的传输）时，默认 OCE 会命中 `OCE.Token == R` 被误判为 `CallerAbort`，从而抑制仍存活请求的响应。
2. **身份匹配后必须检查来源已取消**：`OCE.Token == R` 但 `R.IsCancellationRequested == false`（或 `== K` 但 `K` 未取消）→ `NonCaller`，不得仅凭 token 相等归因。
3. **L 的来源都未取消 → `NonCaller`**：只有 `L` 被取消才可能进入该步；`R`、`K` 都未取消时不得默认成 `ManagedDeadline`。
4. `R` 与 `K` 为同一 token、或 `L == R` 时按 `R` 的检查归因，事实记录 `R==K`，不重复计数。
5. 验收：新增 `R-06f`（None 优先、身份相等但来源未取消、L 来源全未取消）；fixture `classification` 重写为 0–5 步并新增 `edgeRules`；校验测试增加对应断言。验证：`ReferencedAssemblyScopeProbeTests`(2) + `Issue129ContractFixtureTests`(8) = **10/10 pass**，0 skipped。

---

## S2.1 共享模型与声明面（2026-10-17，S2 首个增量）

设计与增补审查均已通过（rev.4）；本轮按「shared model/emitter」顺序只交付模型与声明面，不宣称 S2 完成。

### 已交付

1. **声明面**（`src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/`）
   - `CapabilityCompatibilityProjectionAttribute` 增 `ContractType`（可指向引用程序集契约）与 `RegistrationMode`（默认 `ExplicitHost`）；
   - 新枚举 `HttpProjectionRegistrationMode`/`HttpProjectionVerb`/`HttpProjectionOperationKind`/`HttpProjectionBodyPolicy`；
   - 新 `AppServiceHttpActionAttribute`（Verb/Route/Kind/Permissions/Body/RequiresConcurrencyToken，仅声明数据，不引用 ASP.NET/Runtime）。
2. **共享模型**（`src/Tooling/CrestCreates.HttpProjectionGeneration/`，source-link 进 analyzer 与 JsonContracts 工具）
   - `AppServiceHttpProjectionModel`：owner/contract（含 `ContractAssemblyName`）、`Actions`（verb/route/kind/permissions/body/CT/route identity/closed signature/capability+endpoint id/envelope）、`ReferencedTypes`（与 declared 分开）、`RequiredJsonRootTypeNames`、`ActivationMethodName`；
   - `AppServiceHttpWireConvention`：复用 `CrudEnvelopeConvention`/`CrudWireConvention`，route/verb 派生与 legacy convention 对齐（S4 收口时删除旧副本）；
   - `AppServiceHttpProjectionModelBuilder`：按 `ContractType` 只枚举契约及其继承接口；标记类自身方法不投影；`LegacyStatic` 声明不由正式模型接管；诊断使用冻结的 `CCHTTP001–008`（含契约非接口/开放泛型、路由占位缺参、多 body、重复 method+route、重复 capability、契约与实现声明不一致、缺 CT 警告）。
3. **静态注册器收口（最小步）**
   - 带 `ContractType` 且非 `LegacyStatic` 的声明：静态 compat 生成器**跳过**并给 `CCHTTP002` 警告（避免与未来 ExplicitHost 双发布，同时不产生半套端点）；
   - 无 `RegistrationMode` 的旧声明：继续由静态注册器承接，但给 `CCHTTP005` 迁移警告；显式 `LegacyStatic` 不再告警。
4. **测试**
   - `AppServiceHttpProjectionModelTests`（7）：冻结矩阵（GET 写动作仍 Command、双 query 无组合信封、route+body 使用共享信封名）、引用契约/DTO 单列、非接口契约 CCHTTP001、LegacyStatic 不接管、缺 CT 警告、重复 method+route CCHTTP003、契约/实现声明冲突 CCHTTP008；
   - `AppServiceHttpProjectionDeclarationTests`（3）：显式 host 跳过 + CCHTTP002、缺 mode 的 CCHTTP005 且静态发射不变、显式 LegacyStatic 无迁移告警。
   - 定向 10/10 pass；全量 `CrestCreates.CodeGenerator.Tests`：**473 pass + 1 fail / 474**。唯一失败是本轮新增的 `CCHTTP005` 迁移警告进入了同轮探针的「authored 文件只允许 SYSLIB1030」过滤器（探针把生成器告警误当编译诊断），与本轮实现无关。已把该探针显式声明为 `LegacyStatic`（它测的就是旧静态链）并把过滤器限定为「编译器错误 + SYSLIB」，重跑 **3/3 pass**（15m09s）。因此本轮无新增回归。

### 未交付（S2.2/S2.3）

- 共享 emitter 提取与普通 Compile 产物（信封/binder/invoker/result/manifest/ownership）、`CrestCreatesJsonContractGenerationDependsOn` 接入、MSBuild 生命周期与外部包首消费（B01–B04）；
- `AddGeneratedAppServiceHttp` 贡献/handler/JSON configurator、Map 期 fail-closed、Asset/Procurement 迁移与 H08。
- 本轮不声称 S2 放行，不合并，CI 保持暂停。

### S2.1 审查 R1 修复（2026-10-17，4 项 P2）

| 发现 | 修复 | 证据 |
| --- | --- | --- |
| S21-R1-01 显式 ExplicitHost 缺 ContractType 仍生成全局静态注册 | 静态生成器现在区分四种模式：显式 `ExplicitHost` + `ContractType` → 跳过（CCHTTP002 警告，零产物）；显式 `ExplicitHost` 无 `ContractType` → 同样跳过并告警（不再静默发布静态链）；无 mode + `ContractType` → 视为 ExplicitHost（属性默认值语义）；无 mode 且无 `ContractType` → 旧静态链 + CCHTTP005 迁移警告。跳过路径不再发射空模型产物（`Actions.Length == 0` 直接跳过），避免遗留空 provider/ModuleInitializer | 审查者探针 `Explicit_host_without_contract_cannot_silently_publish_static` 由 0/5 组转为通过 |
| S21-R1-02 同短名契约的 capability ID / 组合信封名碰撞 | 默认 capability id 改为 owner assembly + 完整契约名（去 `global::`）+ action 的确定性编码；组合信封名为 `CrestCreates.Generated.{assembly}_{contractFullName}_{Action}_CompatibilityInput`（`SanitizeToken` 归一化 `.`/`+`/泛型符号）。显式 `CapabilityIdPrefix` 仍覆盖 ID，但信封始终含完整 owner 身份；CRUD 的 `CrudEnvelopeConvention` 未改动 | 审查者两条身份探针（不同显式前缀 / 默认 ID）通过 |
| S21-R1-03 合法闭合泛型接口被误判为开放泛型 | 由 `TypeParameters.Length > 0` 改为 `IsUnboundGenericType`；闭合泛型契约的 `ContractFullName` 含闭合参数，动作签名与结果类型按构造后类型记录 | `Closed_generic_contract_is_a_supported_closed_interface` 通过 |
| S21-R1-04 接口私有辅助方法纳入动作集 | 只枚举 `DeclaredAccessibility == Public` 的 ordinary 实例方法（C# 8 私有 DIM 是实现细节） | `Private_interface_helper_is_not_an_action` 通过 |

审查者的 5 条探针以 patch 原样入库（`ReviewS21ContractProbes.cs`）。本轮定向（5 审查探针 + 7 模型 + 3 声明）**15/15 pass**；全量 `CrestCreates.CodeGenerator.Tests` **479 pass / 0 fail / 479**，0 skipped（22m28s），无新增回归。默认 ID 语义变化影响正式声明（Ticket 将在 S3 声明时显式保留现有 `compat.appservice.ticket` 前缀，迁移表已记录），旧静态链不受影响。

S2.2 提取边界沿用审查意见：共享 HTTP 后端（envelope/contribution/binder/invoker/typed result/required identities/JSON root/Host configurator）先归一 wire action model 再调同一 emitter；实体发现、DTO/CRUD service、repository、mapping、permission、filter/sort/query adapter 留在 CRUD 前端；#128 的 not-found/result envelope 与 #129 的 Web CrestErrorResponse 责任分开；工具链复用既有 CLI/Task/fingerprint/stamp/DependsOn/分区与 packaged transport。

### S2.1 复审 R2 修复（S21-R2-HTTP129-01：identity 编码）

有损 slug（`SanitizeToken` 把 `.`/`_` 都归一成 `-`）导致两类问题：信封名含 `-` 不是合法 C# 类型名；`A.B.IOrder` 与 `A_B.IOrder` 归一后碰撞。

修复：`AppServiceHttpWireConvention.CanonicalToken` 改为**可逆、区分大小写**的编码——字母/数字原样保留，`_` → `_005F`，其余字符 → `_` + UTF-16 码元的四位十六进制；首字符为数字时前置 `_`。契约/owner/action 各自编码后：

- 组合信封名 = `CrestCreates.Generated.{CanonicalToken(owner)}__{CanonicalToken(contractFullName)}__{CanonicalToken(action)}_CompatibilityInput`（`__` 为字段分隔，编码内容不可能产生 `__`；类型名合法且单射）；
- 默认 capability id = `compat.appservice.{CanonicalToken(owner)}.{CanonicalToken(contractFullName)}.{kebab(action)}`；
- 显式 `CapabilityIdPrefix` 路径保持 `prefix + "." + kebab(action)`，以便 Ticket 在 S3 显式保留现有 `compat.appservice.ticket.*` id。

审查者新增 3 条身份探针（信封 FQN 可在普通 Compile 声明、`A.B`/`A_B` 默认 ID 不碰撞、不同显式 prefix 下信封不碰撞）以 patch 原样入库；与 R1 的 5 条及本切片 10 条定向合并 **18/18 pass**；全量 `CrestCreates.CodeGenerator.Tests` **482 pass / 0 fail / 482**，0 skipped（22m26s），无新增回归。

### S2.1 复审 R3 修复（首位数字前缀碰撞）

`CanonicalToken` 的「首字符为数字时补 `_`」不是单射：`"005FHost"` → `_005FHost`，而 `"_Host"` 的 `_` 转义同样是 `_005FHost`。修复为**编码首位数字本身**（`'0'` → `_0030`），删掉条件补 `_`；其余字符规则不变。于是 `"005FHost"` → `_003005FHost`，`"_Host"` → `_005FHost`，不再碰撞；空串仍 `_EMPTY`（与 `"EMPTY"`→`EMPTY`、`"_EMPTY"`→`_005FEMPTY` 均不同）。

审查者 2 条探针（token 单射、同源不同合法程序集的 ID/信封不同）以 patch 原样入库；与 R1/R2 探针及本切片定向合并 **20/20 pass**；全量 `CrestCreates.CodeGenerator.Tests` **484 pass / 0 fail / 484**，0 skipped（22m30s），无新增回归。

## S2.2-A 共享 HTTP emitter（2026-10-17）

按审查边界先物化 emitter，再动物化工具链；本轮不接入 BuildTasks、不激活真实 Host。

### 已交付

1. `AppServiceHttpProjectionEmitter`（`src/Tooling/CrestCreates.HttpProjectionGeneration/`，与模型一起 source-link 进 analyzer 与 JsonContracts 工具）从 `AppServiceHttpProjectionModel` 发射三份普通 Compile 产物：
   - **Envelopes**：组合输入信封（`CrestCreates.Generated.{canonical identity}`），成员为 `Id`/`Input`/query 参数；
   - **Contribution**：`CapabilityEndpointContribution` 派生类（capability/endpoint descriptors、typed binding contracts、handler bindings、result bindings、BodyTypes、`JsonContext`、`RequiredJsonRootTypes`、`RequiredIdentityIds`），外加每动作的 typed binder（`CapabilityEndpointJsonTypeInfoResolver.Require` + `CapabilityEndpointBodyReader.ReadNativeBodyAsync`，route/query 严格解析、缺失/非法 → 400）、typed result mapper（`WrapResult`/`WrapGetResult`/`WrapVoidResult` 的 typed overload）与 `ICapabilityContextAwareHandlerInvoker`；
   - **Activation**：`AddGeneratedAppServiceHttp()` + handler module + `IConfigureOptions<Http.Json.JsonOptions>` configurator（与 #128 相同的插入/追加规则），全部 `TryAdd*`，无 `ModuleInitializer`、无进程全局注册表、无 IServiceProvider 捕获。
2. `BuildManifestEntry`：`OwnerAssemblyName`/`OwnerServiceFullName`/`ContractAssemblyName`/`ArtifactHintName`/`DeclaredTypeFullNames`（仅本产物声明类型：contribution/handler/activation/invokers/envelopes）/`ReferencedTypeFullNames`（模型引用契约与 DTO）/`SourceLocation`/`ModelSchemaVersion`；与 addendum §3.3 的 CCCRUD008 约束一致。
3. 命名：`AppServiceHttpEmissionOptions.ForContract` 由 canonical token 派生 contribution/handler/activation/configurator/JSON context 名称；JSON context 名称以 `GeneratedHttpJsonContext` 冻结，供 JsonContracts 阶段生成。

### 证据

`AppServiceHttpProjectionEmitterTests`（3）：

- 产物断言：无 `ModuleInitializer`/`DescriptorProviderRegistry`/`CompatibilityBodyReader`/`DynamicApiGeneratedRuntime`；激活只经 `AddGeneratedAppServiceHttp`；贡献含权限、JSON roots、required identities 与 typed binder/mapper；GET 写动作保持 GET + Command；组合信封名与成员正确；
- manifest 断言：declared 不含契约/DTO，contract assembly 单列；
- **编译探针**：契约源 + 三份产物 + JSON context stub，引用真实运行时程序集（Metadata/Capability/DynamicApi/Schema/Options/Json）编译零错误。

定向 3/3 pass；全量 `CrestCreates.CodeGenerator.Tests` **487 pass / 0 fail / 487**，0 skipped（22m32s），无新增回归。

### 未交付（S2.2-B/S2.3）

- BuildTasks task/targets 物化、CLI/transport/stamp 复用、`CrestCreatesJsonContractGenerationDependsOn` 显式接入、design-time/TFM/RID 分区、外部包首消费（B01–B04 的 MSBuild 证据）；
- JsonContracts 生成 `HttpProjectionJsonContext_*` roots（当前 emitter 通过 options 引用该名称；stub 只用于编译探针）；
- 真实 Host 的 `AddGeneratedAppServiceHttp` 激活验收、Map 期 fail-closed、Asset/Procurement 迁移（S2.3）。

### S2.2-A 审查 R1 修复（1 P1 + 5 P2）

| 发现 | 修复 | 证据 |
| --- | --- | --- |
| S22A-R1-01 [P1] 输入/调用映射：纯 query 命令不可编译、route/query 同型参数被交换、If-Match 丢失 | 模型新增**契约顺序**的 `Parameters`（Route/Query/Body/Header/CancellationToken、Required、MemberName）；输入数 >1 或声明并发 token 时一律组合信封；binder 按契约顺序读取 route/query/body/header；invoker 按契约顺序从信封/单值取参并只在 CT 位置传 `ct`；`RequiresConcurrencyToken` 优先映射到契约的 `string?` stamp 参数，否则作为 input-only Header 成员（不传给业务） | 审查 2 条 query-only 探针、2 条参数顺序探针（query-first/route-first，实际执行 invoker 并断言 `Q:…;R:…`）、If-Match 绑定探针全部通过 |
| S22A-R1-02 [P2] CLR 支持集不一致：int/string 抛异常、`Task<int>` 与同步方法不可编译 | 模型记录 `ScalarKind`（Guid/String/整数/布尔/decimal/double/single/char/DateTime/DateTimeOffset/Enum）与 `ResultKind`（Void/SyncValue/TaskValue/ValueTaskValue/TaskVoid/ValueTaskVoid）；不可绑定标量在模型构建期 CCHTTP001，不再到 emitter 才抛；invoker 按 ResultKind 发射 `return await` / `return` / `await`；GET 值类型结果不再用 `as` | int/string 物化探针、`Task<int>`/同步 `string`/同步 `void` 编译探针通过 |
| S22A-R1-03 [P2] 同 owner 两契约激活歧义 | `Emit` 改为每契约**一个 bundle**（信封 + 贡献 + invoker）；新增 `EmitAssemblyActivation`：每 owner 程序集**一个** `AddGeneratedAppServiceHttp()` + 一个 handler module（注册全部 invoker）+ 一个 JSON configurator | 两契约 + 单次 `services.AddGeneratedAppServiceHttp()` 的编译探针通过（探针仅按允许范围改调用方式） |
| S22A-R1-04 [P2] manifest 不精确 | manifest 改为**逐 artifact 条目**；`DeclaredTypeFullNames` 用共享 provenance 规则从该 artifact 源码提取；新增 `ContentHash`（sha256）；activation 条目覆盖 configurator | 两条 manifest 探针（与 `CrudArtifactEmitter.DeclaredTypeFullNames` 全等、hint 对应单文件）通过 |
| S22A-R1-05 [P2] 实际 scalar 输入缺 JSON root | 输入侧 roots 覆盖：组合信封（CapabilityInput）、裸 scalar（CapabilityInput）、裸 body（Body），并逐参数收录标量类型 | `RequiredJsonRootTypes` 含 `typeof(Guid)` + `CapabilityInput` 的探针通过 |
| S22A-R1-06 [P2] 共用后端未提取 | 新增 `HttpProjectionShared`：`DeclaredTypeFullNames`（#128 provenance 规则）与 `EmitJsonConfigurator`（resolver 插入/追加策略）；**CRUD emitter 已改为调用这两条共享规则**（`CrudArtifactEmitter.DeclaredTypeFullNames` 委托、configurator 发射委托）。binder/invoker/descriptor 的 CRUD↔手写完全归一仍**未完成**，属下一增量 | CRUD 生成器单元 21/21；全量结果见补记；不声称该项已关闭 |

定向（14 条审查探针 + 7 条模型 + 3 条本切片 emitter 测试）**24/24 pass**；CRUD 生成器单元 21/21；全量 `CrestCreates.CodeGenerator.Tests` **501 pass / 0 fail / 501**，0 skipped（22m29s），无新增回归。CRUD↔手写 binder/invoker/descriptor 完全归一仍未完成（下一增量）。

### S2.2-A 后端收口（S22A-R1-06 的后续实现）

- 新增中性 **wire model**：`HttpWireModel` / `HttpWireAction` / `HttpWireParameter` / `HttpWireArgument` / `HttpWireJsonRoot`（Route/Query/Body/Header/CT、信封、invoker 参数序列、结果形状、not-found 语义、额外 JSON roots）。
- 新增共享后端 `HttpWireEmitter`：信封、capability/endpoint descriptors、typed bindings、handler bindings、typed result mappers、invokers、JSON roots、required identities 全部只此一份。
- **手写前端**：`AppServiceHttpProjectionEmitter` 现在只做「模型 → wire」映射与 bundle/激活/manifest 组装，不再持有发射规则；14 条审查探针 + 3 条 emitter 测试 + 7 条模型测试保持 **24/24**。
- **CRUD 前端**：`CrudArtifactEmitter.EmitCapabilityContribution` 改为构造 wire model 并调用 `HttpWireEmitter.EmitContribution`；删除 21 个重复的发射方法（descriptors/endpoints/bindings/handlers/results/body types/roots/identities/binder/mapper/invoker 等）；激活中的 invoker 名称改用共享命名，JSON configurator 继续调用共享策略。CRUD 独占的实体/DTO/service/repository/mapping/permission/query-adapter 生成不变。
- 验证：CRUD 形状测试 54/54；两前端定向 24/24；全量 `CrestCreates.CodeGenerator.Tests` **501 pass / 0 fail / 501**（22m11s）；CRUD NativeAOT 门禁**保持绿**：`CrestCreates.GeneratedCrud.AotFixture.Tests` **23/23 pass**（含 `GeneratedCrudNativeHttpGateTests` 的 publish/link/run、15 场景 sentinel、总 sentinel 与变异控制）。

### S2.2-A 复审 R2 修复（3 项 P2）

| 发现 | 修复 | 证据 |
| --- | --- | --- |
| S22A-R2-01 模型接受集大于可物化/执行集：可空 enum、ref、开放泛型方法 | 模型构建期：`ref/out/in` 参数与开放泛型方法直接 CCHTTP001 拒绝；可空标量（`Nullable<T>`/可空引用）记为 `Required=false`；共享 binder 对 enum 用**非空底层类型** `Enum.TryParse`（`State?` 不再对 `typeof(State?)` 调用），可选标量缺失时保留 default、有值时解析 | 审查 5 条边界探针（nullable enum 正例、非空 enum 正例、ref 拒绝、开放泛型拒绝、两 owner 激活）全部通过 |
| S22A-R2-02 公开激活身份不含 owner | `ForAssembly` 的 activation/handler/configurator 名称与 file 名/context 一样包含 canonical owner；扩展方法名仍为 `AddGeneratedAppServiceHttp`，Host 可通过生成符号 FQN 精确选择 owner | 两 owner 各自 PE emit 后被同一 Host 同时引用并分别调用 → 零错误 |
| S22A-R2-03 CRUD 输入信封仍是第二份规则 | `CrudArtifactEmitter.EmitCapabilityInputEnvelope` 改为调用共享 `HttpWireEmitter.EmitEnvelopeDeclaration`（保留 #128 每 action 文件名/hint、类型名与逐文件 manifest，冻结 roots/可空 header/wire 形状） | CRUD 形状/typed adapter/real-declaration **58/58**；全量与 NativeAOT 结果见补记 |

定向（手写模型/emitter + R1/R2 审查探针）**29/29 pass**；CRUD 形状/typed adapter/real-declaration 58/58；全量 `CrestCreates.CodeGenerator.Tests` **506 pass / 0 fail / 506**（22m17s）；CRUD NativeAOT fixture（含 linux-x64 publish/link/run 的 `GeneratedCrudNativeHttpGateTests`）**23/23 pass** → 三项修复无原生回归。

### S2.2-A 复审 R3 修复（1 项 P2：CRUD 信封 IfMatch 可空性）

- 共享 envelope 收口后 CRUD 的 `IfMatch` 变成非可空 `string`；在 `RespectNullableAnnotations=true` 的 Host 上序列化缺失 header 会抛 `JsonException`。修复：CRUD 的 header wire 参数恢复 `global::System.String?`；手写服务的引用类型参数保留 `?` 注解（`DisplayType`）；`typeof(...)` 位置对可空引用类型去掉 `?`（CS8639），`Nullable<T>` 值类型保持原样。
- 附带的 R3 边界探针（可选 `int?/Guid?/State?/string?` 的缺失与有值经真实 invoker、非法/必需缺失 → 400、CRUD 可空 header 序列化两种对照）全部通过；定向（R1 14 + R2 5 + R3 16 + 模型 7 + emitter 3）**45/45 pass**。
- 全量 `CrestCreates.CodeGenerator.Tests` **522 pass / 0 fail / 522**（22m28s）；CRUD NativeAOT fixture（含原生 publish/link/run）**23/23 pass**。

### S2.2-A 复审 R4 修复（2 项 P2）

| 发现 | 修复 | 证据 |
| --- | --- | --- |
| `TypeOfDisplay` 以名称子串判断 `Nullable<T>`，误判合法引用 DTO（`Box<T>?`/`Nullable<T>?`）→ `typeof` CS8639 | 删除字符串启发式；wire 参数与 JSON root 携带**结构性** `ClrTypeFullName`（引用类型去 `?`、`Nullable<T>` 保留），`typeof`/BodyTypes/roots 全部使用该 CLR display | 审查 2 条引用泛型 body 探针通过（模型 body 类型以 `?` 结尾、产物零错误） |
| 手写服务自动补出的可选 `IfMatch` 仍为非空 → 严格 nullable JSON 序列化失败 | 合成 Header 参数声明 `global::System.String?`（CLR 类型仍为 `System.String`），`Required=false`、`IsContractParameter=false` 保持不变 | 审查 3 条官方 STJ + `RespectNullableAnnotations` 探针（原始产物/缺失或提供 header）通过，序列化为 `"IfMatch":null`/`"stamp"` |

定向（R1 14 + R2 5 + R3 16 + R4 5 + 模型 7 + emitter 3）**50/50 pass**；CRUD 形状 58/58；全量 `CrestCreates.CodeGenerator.Tests` **527 pass / 0 fail / 527**（22m27s）；CRUD NativeAOT fixture（含原生 publish/link/run）**23/23 pass**。

## S2.2-B1 隔离预编译引擎（2026-10-17）

- 新增 `AppServiceHttpPrecompileEngine`（`src/Tooling/CrestCreates.JsonContracts.BuildTasks.Core/HttpProjection/`）：
  - 用 `JsonContractCompilationFactory` + 真实 `ReferencePathWithRefAssemblies` 建 Roslyn compilation（无反射/无 assembly load）；
  - 只接管带 `ContractType` 的正式声明；无 ContractType 的旧静态声明原样留给 legacy 前端（不诊断、不物化）；
  - 每个契约产出 1 个 bundle（信封 + 贡献 + invoker），每个 owner 程序集产出 1 个激活（per-owner 公开类型名）；manifest 逐 artifact 记录 `OwnerAssemblyName`/`OwnerServiceFullName`/`ContractAssemblyName`/`DeclaredTypeFullNames`/`ReferencedTypeFullNames`/`SourceLocation`/`ModelSchemaVersion`/`ContentHash`；
  - 生产者编译使用合成程序集名，物化前把 owner/本程序集契约身份**归一为真实程序集名**（B3 真实构建发现该归一不完整：capability id/envelope 名仍含合成名，已改为直接以真实 owner 名编译——见 S2.2-B3/B4 节）；
  - 模型错误（ref/开放泛型/非接口契约等）fail closed，零产物；输出对同一输入确定（顺序 + 内容哈希）。
- 测试：`AppServiceHttpPrecompileEngineTests`（4）——双契约 → 2 bundle + 1 activation + 逐 artifact manifest；legacy 声明不被接管；不支持形状零产物；同输入确定。JsonContracts BuildTasks 套件 **135/135 pass**。

### S2.2-B 剩余

- 隔离 CLI 命令（`prepare-appservice-http`）与 transport DTO/JSON context；
- BuildTasks task + targets（Prepare/State/Generate/Include、显式 `CrestCreatesJsonContractGenerationDependsOn`、design-time/TFM/RID 分区）；
- JsonContracts 生成 `HttpProjectionJsonContext_*` roots 与 Host JSON configurator 验证；
- 真实 MSBuild 探针（B01–B04）与外部包首消费。

### S2.2-B2 隔离 CLI 与传输（2026-10-17）

- 新增 `prepare-appservice-http` 工具命令 + `AppServiceHttpPrepareRequest/Response` 传输 DTO（source-generated STJ，反射关闭），复用 `BundleBytes` + 逐 artifact manifest 的既有协议；manifest 条目增加 `RequiredJsonRootTypeNames`（供 JSON 阶段生成 context）。
- 回归：`CrestCreates.JsonContracts.BuildTasks.Tests` **135/135 pass**。
- **仍未交付**（B 未完成）：BuildTasks 输入清单/state/Generate/Include 四个 target、`CrestCreatesJsonContractGenerationDependsOn` 接线、design-time/TFM/RID 分区、JsonContracts `HttpProjectionJsonContext_*` roots 生成与 Host configurator 校验、真实 MSBuild 探针（B01–B04）与外部包首消费。本轮不声称 B 完成。

## S2.2-B3/B4 完成：BuildTasks 物化、JSON 闭包与真实 MSBuild 探针（2026-10-19）

### 交付

- **BuildTasks 前置阶段**（`CrestCreates.AppServiceHttp.Build.Common.targets`）：
  - `PrepareCrestAppServiceHttpInputs`（`@(Compile)` + 真实 `ReferencePathWithRefAssemblies`，排除自身输出、CRUD bundle 与 JSON 生成物）→ `WriteCrestAppServiceHttpInputManifest`（schema `appservice-http-inputs-v1`，内容哈希/编译器选项/模型与工具版本/TFM/Configuration/RID）→ `InvalidateMissingCrestAppServiceHttpOutput`（`appservice-http-state-v1`）→ `GenerateCrestAppServiceHttpSurface`（隔离 CLI `prepare-appservice-http`，write-if-changed + stamp）→ `IncludeCrestAppServiceHttpArtifacts`（bundle 进 `@(Compile)`，outputs manifest 进 `@(AdditionalFiles)`）；`Clean` 清理。
  - 三个 target 显式追加到 `CrestCreatesJsonContractGenerationDependsOn`，包与仓库 transport 共用；输入清单/state 任务由 CRUD 与手写前端共用同一写入器（schema 参数化），未建立第二套 stamp 引擎。
- **JsonContracts 闭包**：
  - `AppServiceHttpJsonSurfaceDiscovery` 从真实符号重建投影模型（只认 `ContractType` 锚点），与 outputs manifest 逐 artifact 对账：owner、schema、ContentHash、DeclaredTypeFullNames、RequiredJsonRootTypeNames；任何缺失/多余/伪造 root 直接 CCHTTP006，同一契约多锚点 CCHTTP003。
  - roots 由共享 `HttpWireEmitter.PlanJsonRoots` 派生（信封、body/裸 scalar、payload、`DynamicApiResponse<T>`、void envelope），JSON 阶段按符号解析（含 typeref 转发、`Nullable<T>`、数组、嵌套泛型；同 FQN 多 owner 拒绝）。
  - 生成 `CrestCreates.Generated.HttpProjection.HttpProjectionJsonContext_<owner>`（camelCase），与激活中的 configurator 名称一致；官方 STJ 在同一 CoreCompile 产出全部 metadata。
- **工具链 fail-closed**：`CrestCreatesAppServiceHttpJsonToolchain` 标记 + 生成器读取；有 `ContractType` 但未装配工具链时 CCHTTP002 升级为 Error（装配时仍为静态注册器跳过 Warning，不双发布）。CCHTTP005 的 legacy 行为不变。
- **真实构建暴露并修复的身份缺陷**：预编译最初用合成程序集名编译，capability id / envelope 类型名会泄漏 `HttpProjectionPrecompile`；现直接以真实 owner 程序集名编译，身份与静态契约一致（正例断言 `compat.appservice.Probe.…` 且不含合成名）。
- **CLI 输出修正**：`generate` 只要写出有效 response 就不再把“仅含警告的 exit=1”当成传输错误（CJC013 只在无 response 时报告），避免 warning 使构建假红。

### 证据

- `AppServiceHttpJsonSurfaceDiscoveryTests` **9/9**：roots 重建与 manifest 对账、camelCase context、缺 `DynamicApiResponse` 引用 → CCHTTP004、篡改 hash/删除条目/伪造 root/删除 root → CCHTTP006、无 manifest 拒绝、同契约双锚点 CCHTTP003。
- 真实 MSBuild 探针 `Issue129AppServiceHttpMsBuildTests` **7/7**（linux，`dotnet build` 原始产物）：
  1. 单次构建物化 bundle（含组合信封 + 激活）并由官方 STJ 关闭 WidgetDto/CreateWidgetDto/List/IReadOnlyList/typed+void envelope；`CCHTTP002` 仅为警告；manifest 逐 artifact hash/roots 正确；输入清单 schema 正确；
  2. 篡改 outputs manifest → CCHTTP006（无 CJC013 噪声），移除篡改后恢复成功；
  3. 未知 model schema → CCHTTP006 且不记录新成功 stamp，随后恢复；
  4. no-op 保持 stamp/bundle、缺 outputs manifest 时失效 stamp 并修复、clean 删除并可确定重建、源文件改名进入输入清单；
  5. design-time 写 `design-time/` 分区、不触碰 runtime stamp；修改契约后 design-time 反映新路由而 runtime 保持，随后 runtime build 收敛；
  6. 多 TFM（net10.0 / net10.0-windows）与 RID（linux-x64 / linux-arm64）各自分区，RID 构建不覆盖无 RID 输入清单；
  7. 打包 `CrestCreates.JsonContracts.Build` 包后，全新外部消费者零仓库 import 完成物化 + JSON 闭包 + STJ metadata。
- `CrestCreates.JsonContracts.BuildTasks.Tests` **144/144**。
- 全量 `CrestCreates.CodeGenerator.Tests` **535 pass / 0 fail / 535**，0 skipped（26m25s）；CRUD NativeAOT fixture（linux-x64 publish/link/run）**23/23 pass**（共享后端重构无原生回归）；`SaaSHelpdesk.Web` 真实样例构建成功（新前置阶段在无锚点项目上为空操作）。

### 边界（未在本节声称）

- ExplicitHost 的真实 Host 激活、Map 期 fail-closed、Asset/Procurement 迁移仍属 S2.3；本章只证明编译期物化与 JSON 闭包。
- 原生 AOT 仍由 S5 的 Ticket fixture 验证；本节探针为 JIT 构建证据。

## S2.2-B 复审 R1 修复（4 项 P2，2026-10-19）

审查 head `3a1ebb26`；评审探针 `docs/review/probes/2026-10-19-issue-129-s22b-r1-probes.patch` 原样入库（`ReviewS22BProbes`）。

| 发现 | 修复 | 证据 |
| --- | --- | --- |
| S22B-R1-HTTP129-01 JSON 类型解析丢失可空泛型/误拒嵌套泛型 | 删除整段 display 文本解析；`AppServiceHttpProjectionModelBuilder` 在构建模型时同时产出每个动作的**真实根符号**（`AppServiceHttpRootSymbol`：action/component/display/ITypeSymbol）。发现阶段按共享 `PlanJsonRoots` 的 `(action, component, display)` 取符号；仅 envelope（按编译期声明名解析）与 `DynamicApiResponse<T>`（引用程序集开放泛型 + 结果符号 `Construct`）需要构造，均是同一 compilation 的符号。`Box<int>?` 保持 `Nullable<Box<int>>`，`Outer<int>.Inner<string>` 正确绑定 | 审查 7 条探针 **7/7**（string/Box<int> 正控 + Nullable 泛型/嵌套泛型精确 symbol identity）；JsonContracts 151/151 |
| S22B-R1-HTTP129-02 未核对实际 Compile 产物 | 发现阶段对每个预期 artifact，在 `compilation.SyntaxTrees` 的实际编译输入中验证**逐 artifact 源内容**仍存在（与重新发射的权威源码相等）；manifest hash/owner/roots 对账保持。路由篡改或删除实际声明 → CCHTTP006 | 审查 unit 探针（marker-less bundle 路由替换）通过；真实 MSBuild `Forged_bundle_after_include_must_fail_closed_and_recover` 通过（Include 后 sed 替换 bundle，exit≠0 + CCHTTP006；移除篡改后恢复）；评审 real-build 脚本 `actual-bundle-tamper` exit 1 + CCHTTP006 |
| S22B-R1-HTTP129-03 显式 Host 缺 ContractType 被当作 legacy | 新增 `AppServiceHttpProjectionModelBuilder.OwnsFormalHttpSurface`：有 ContractType 或显式 `RegistrationMode=ExplicitHost` 即进入 builder；仅“无 mode 无 ContractType”与显式 `LegacyStatic` 留给 legacy。显式 Host 缺 ContractType → CCHTTP001 Error、零产物 | 审查 unit 探针通过；真实 MSBuild `Explicit_host_without_contract_type_must_fail_with_cchttp001` 通过（exit≠0、CCHTTP001、无 bundle/stamp）；评审 real-build 脚本 `actual-missing-contract` exit 1（日志 CCHTTP001）；旧无 mode/LegacyStatic 控制保持（既有 generator/engine 测试） |
| S22B-R1-HTTP129-04 双锚点在 producer 抛重复键异常 | 在排序后、建立字典/发射前执行全局 owner 冲突检查：重复契约（消息含契约与**两个 anchor 的完整位置**）、跨契约重复 capability id / endpoint id / method+route；命中即 CCHTTP003、零产物、无成功 stamp | 审查 unit 探针通过（CCHTTP003，artifacts 空）；真实 MSBuild `Duplicate_contract_anchors_must_report_cchttp003_in_the_producer` 通过（CCHTTP003、无 “same key”、无 bundle/stamp）；评审 real-build 脚本 `actual-duplicate-anchor` exit 1 + CCHTTP003、无 fatal |

### 追加真实构建控制（评审要求）

- `Referenced_contract_and_dtos_must_close_in_the_consumer_build`：Contract/DTO 位于独立 `Shop.Contracts` 程序集，投影锚点在 Consumer；真实构建成功、manifest `ContractAssemblyName=Shop.Contracts`、JSON context 注册引用 DTO、官方 STJ 在 Consumer 同轮生成 `JsonTypeInfo<Shop.Contracts.WidgetDto/CreateWidgetDto>`，零 SYSLIB1030。
- `Removing_the_last_projection_must_clear_bundle_manifest_and_json_roots`：移除最后一个正式声明后重建成功；bundle 无 contribution、outputs manifest entries=0、JSON 源不再含 `HttpProjectionJsonContext_`、无 CCHTTP002。
- 既有 7 条 MSBuild 控制保持通过；本轮 MSBuild 套件 **12/12**。

### 最终验证（R1 head）

- 审查探针 `ReviewS22BProbes` **7/7**；`CrestCreates.JsonContracts.BuildTasks.Tests` **151/151**。
- 真实 MSBuild 套件 **13/13**（新增：伪造实际 bundle、显式 Host 缺 ContractType、双锚点、引用程序集契约/DTO via 官方 STJ、移除最后声明、可空/嵌套泛型 body 的官方 STJ；原 7 条保持）。
- 评审 real-build 脚本最终跑：baseline exit 0；bundle 替换 exit 1 + CCHTTP006；缺 ContractType exit 1 + CCHTTP001；双锚点 exit 1 + CCHTTP003（含两个 anchor 位置），无 “same key”。
- 全量 `CrestCreates.CodeGenerator.Tests` **541 pass / 0 fail / 541**，0 skipped（27m55s）。
- CRUD NativeAOT fixture **23/23**（linux-x64 publish/link/run）；`SaaSHelpdesk.Web` 构建成功。

## S2.2-B 复审 R2 修复（2 项 P2，2026-10-19）

审查 head `240e9754`；R2 探针原样入库：`ReviewS22BR2ArtifactProbes`、`ReviewS22BR2RouteProbes`。

| 发现 | 修复 | 证据 |
| --- | --- | --- |
| S22B-R2-HTTP129-01 非活动源码可满足实际产物核验（R1-02 延续） | 删除 `Contains(text)` 检查；改为将每个预期 artifact 解析为 token 序列，并要求该序列在某个实际 syntax tree 的**活动 token 流**中连续出现。注释、`#if false` 禁用文本、raw string 均不产生匹配 token；manifest hash/owner/roots 对账保留 | 审查 4 条 artifact 探针 **4/4**（active + comment/disabled/literal 三反例）；真实 MSBuild `Inactive_original_text_must_not_satisfy_the_actual_artifact_check` 三 placement 均 exit≠0 + CCHTTP006 并恢复；评审 `r2-real-build.py`：baseline 0，bundle 篡改/comment/disabled/literal 均 1 + CCHTTP006 |
| S22B-R2-HTTP129-02 跨契约冲突未比较最终路由 | 新增共享 `AppServiceHttpWireConvention.FullRoute(prefix, relative)`；投影 emitter 与 producer 冲突检查**共用**该构造。不同 prefix + 同 relative 不再误报；不同拆分得到相同最终路由会被拒绝 | 审查 4 条 route 探针 **4/4**；真实 MSBuild `Cross_contract_conflicts_must_compare_the_final_emitted_route` 四种组合符合预期；评审 `r2-route-real-build.py`：不同前缀同 relative 0、同最终路由不同拆分 1、真冲突 1、不同路由 0 |

### 最终验证（R2 head）

- `CrestCreates.JsonContracts.BuildTasks.Tests` **159/159**（含 R1 7 条 + R2 8 条审查探针）。
- 真实 MSBuild 套件 **20/20**（原 13 + 新增 3 inactive placement + 4 route 组合）。
- 评审 `r2-real-build.py`：baseline 0；bundle 篡改/comment/disabled/literal 均 exit 1 + CCHTTP006；缺 ContractType 1 + CCHTTP001；双锚点 1 + CCHTTP003。
- 评审 `r2-route-real-build.py`：不同前缀同 relative 0；同最终路由不同拆分 1；真冲突 1；不同路由 0。
- 全量 `CrestCreates.CodeGenerator.Tests` **548 pass / 0 fail / 548**，0 skipped（30m14s）。
- CRUD NativeAOT fixture **23/23**（linux-x64 publish/link/run）；`SaaSHelpdesk.Web` 构建成功。

## S2.2-B 复审 R3 修复（1 项 P2，2026-10-19）

审查 head `9399b056`；R3 ownership 探针原样入库：`ReviewS22BR3OwnershipProbes`。

| 发现 | 修复 | 证据 |
| --- | --- | --- |
| S22B-R3-HTTP129-01 活动 token 未绑定 manifest 声明身份（R1-02/R2-01 延续） | 删除“任意 syntax tree 匹配 token”的检查。现在对每个预期 artifact 的 `DeclaredTypeFullNames`：解析生产者源码得到声明节点与 metadata name → 用 `compilation.GetTypeByMetadataName` 解析**真实 symbol** → 要求恰有一个 source `DeclaringSyntaxReferences`、属于当前 owner 程序集、与其余 artifact 声明同处一个 bundle syntax tree → 对该 FQN 自己的声明做**活动 token 全序列比较**。`Decoy.CrestCreates...` 是不同 symbol，无法满足正确 FQN 的声明校验；注释/禁用/raw string 同样不参与 | 审查 ownership 探针 **3/3**（original 放行、直接伪造拒绝、wrong-namespace 拒绝）；R1/R2 探针保持 18/18；新增真实 MSBuild `Wrong_namespace_active_copy_must_not_vouch_for_the_real_types`：Decoy 原始副本 + 真实 FQN 伪造路由 → exit≠0 + CCHTTP006，移除变异后恢复；评审 `r3-real-build.py`：baseline 0、直接篡改 1+CCHTTP006、wrong-namespace 1+CCHTTP006、缺 ContractType 1+CCHTTP001、双锚点 1+CCHTTP003 |

### 最终验证（R3 head）

- JsonContracts **162/162**（含 R1 7 + R2 8 + R3 3 审查探针）。
- 真实 MSBuild 套件 **21/21**（R2 的 20 + wrong-namespace 拒绝/恢复）。
- 评审 `r3-real-build.py`：baseline 0；直接篡改 1 + CCHTTP006；wrong-namespace 1 + CCHTTP006；缺 ContractType 1 + CCHTTP001；双锚点 1 + CCHTTP003。
- 全量 `CrestCreates.CodeGenerator.Tests` **549 pass / 0 fail / 549**，0 skipped（30m57s）。
- CRUD NativeAOT fixture **23/23**（linux-x64 publish/link/run）；`SaaSHelpdesk.Web` 构建成功。

## S2.3 真实 Host 激活、Map fail-closed 与 Asset/Procurement 迁移（2026-10-19）

### 声明与生成

- **自契约（self-contract）模式**：`AppServiceHttpProjectionModelBuilder` 允许 `ContractType` 指向**投影类自身**（非 static）；只枚举该类上显式标注 `[AppServiceHttpAction]` 的 public 实例方法（未标注的 helper/上下文访问器不暴露）。这是无独立接口的兼容投影迁移路径，不新增空转 facade。
- **Asset**：`AssetCompatibilityProjection` 迁移为 `RegistrationMode=ExplicitHost`、`ContractType=typeof(self)`，保留 legacy capability id 前缀 `compat.appservice.asset-compatibility-projection`、路由 `/api/assets/compat/{assetId}` 与 `/api/assets/compat/list`；权限收敛到真实已登记权限 `AssetManagement.Assets.Read/Search`；两个动作补齐 optional CT 并传递到应用服务。
- **Procurement**：`ProcurementAppService` 同样迁移；保留 `compat.appservice.procurement.get`、`/api/procurement/query/{requestId}` 与 `Procurement.Get`（golden 测试的既有消费者）；补齐 CT。
- **Host 激活**：两个 Host 引入 JsonContracts repository transport，并调用生成的 `AddGeneratedAppServiceHttp()`（贡献 + handler module + JSON context configurator）；业务 DI 仍由既有 `[CrestService]`/显式注册拥有，生成激活不重复业务实例。

### Host 组合与 Map fail-closed

- **Capability registry**：Host 在 `builder.Build()` 之后、`MapCrestCapabilityEndpoints()` 之前，用静态 provider + 原生 catalog + **本 Host 实际激活的 contributions** 构造 capability registry；`Map*` 的既有校验要求每个 contributed capability 与冻结 registry 精确等价。遗漏贡献 → 映射前确定性失败，不静默丢端点。
- **HTTP JSON**：生成的 projection context 保持在最终 `Http.Json.JsonOptions` 链的最前；Host 自己的 source-generated combined resolver 插在 reflection 默认 resolver 之前（不再整体替换 resolver 链）。Projection roots 由生成的 context 回答，Host 自有类型仍由 Host resolver 回答。
- **证据**：`CapabilityEndpointHostActivationTests.ContributedCapability_MissingFromAuthoritativeRegistry_FailsClosed` 通过（零端点、确定性错误）。

### 运行时与样例

- Procurement 样例全套 **26/26 pass**，其中既有 golden 用例实际请求正式路由 `/api/procurement/query/{id}` 并断言响应；`DescriptorAndJsonContracts_AreExactAndComplete` 证明冻结 capability registry 精确包含 `compat.appservice.procurement.get`，contributions 已进入权威 registry。
- Asset 样例 **29/29 pass**；构建成功并生成正式 bundle/manifest/JSON context，路由与 legacy capability id 逐字保留（`compat.appservice.asset-compatibility-projection.get/list`）。Asset 兼容 HTTP 端点未在本环境独立起 Host 验证（其 E2E 需要 PostgreSQL runtime 连接）；共享的正式激活/JSON/权限路径由 Procurement 运行时覆盖。
- DynamicApi 套件 **130/130**；JsonContracts **162/162**；CodeGenerator 全量 **551/551**（30m44s）；CRUD NativeAOT **23/23**；Procurement 样例 **26/26**、Asset 样例 **29/29**（最终 head 复跑）。

## S2.3 复审 R1 修复（2 项 P2，2026-10-19）

审查 head `84cf10f1`；审查探针原样入库：Procurement `ReviewS23HostProbes`、Asset `ReviewS23AssetCompositionProbes`。

| 发现 | 修复 | 证据 |
| --- | --- | --- |
| S23-R1-HTTP129-01 缺业务契约注册未在启动/Map 前拒绝 | 平台贡献新增强类型 `RequiredServiceTypes`；AppService HTTP emitter 为每个契约贡献声明 `typeof(ContractType)`（CRUD 保持空，不改变其输出）。`CapabilityEndpointHostActivation` 收集 owner+contract，`CapabilityEndpointRegistryBootstrapper` 在整批端点映射前用 `IServiceProviderIsService` 校验注册；缺失 → `CCHTTP007`（含 owner 与 contract 全名）、零路由，绝不推迟到首个请求的 500。 | 审查 Procurement 探针 **3/3**：重复激活正例、Active/Opted-out 双 Host 隔离、缺业务注册 → 启动异常含 CCHTTP007 + `ProcurementAppService`；`ReviewS23HostProbes` 原样入库通过。新增 DynamicApi `ContributedCapability_MissingFromAuthoritativeRegistry_FailsClosed` 保持通过。 |
| S23-R1-HTTP129-02 Asset 缺少自契约服务注册 | Asset Host 由业务 owner 显式注册 `AssetCompatibilityProjection`（scoped），与 Procurement 的既有显式注册一致；`[CrestService]` 元数据保留。 | 审查 Asset DI 图探针 **2/2**（false/true 两分支）：真实 Program 的注册序列含 `AssetApplicationService`、`CapabilityEndpointContribution` 与 `AssetCompatibilityProjection`；Asset 原套件 29/29。 |

### 自契约声明面同步

- Spec §4.1：ContractType 明确两种正式范围（接口契约 / 自契约），自契约只枚举显式标注 `[AppServiceHttpAction]` 的 public 实例方法，helper 不暴露；两种模式业务服务均由 owner 注册一次，Host 映射前 CCHTTP007 校验。
- Addendum §3.1/§3.2：补充自契约规则与 R-05 验收行；保留 R-04 的启动期 CCHTTP007、零路由、零请求期 500。
- 冻结 fixture：新增 `declarationScopes`（interfaceContract/selfContract/hostComposition）并把 status 更新为 `frozen-for-review-s2.3`；新增 fixture 漂移测试 `Self_contract_scope_and_host_composition_are_frozen`。
- XML：`CapabilityCompatibilityProjectionAttribute.ContractType`、`AppServiceHttpActionAttribute` 与 builder 摘要说明两种范围与业务 DI owner。

### 最终验证（S2.3 R1 head）

- Procurement 样例 **29/29**（26 原有 + 3 审查探针）；Asset 样例 **29/29**、Asset E2E composition 探针 **2/2**；契约 fixture 测试 **9/9**。
- JsonContracts **162/162**；DynamicApi **130/130**；CodeGenerator 全量 **552/552**（31m35s）；CRUD NativeAOT **23/23**。
- 审查探针：Procurement `ReviewS23HostProbes` **3/3**（含缺业务注册 → CCHTTP007）、Asset `ReviewS23AssetCompositionProbes` **2/2**；契约 fixture 漂移测试 **9/9**。

## S3 第一增量：Ticket 正式投影与真实 Host 装配（2026-10-20）

### 交付

- `ITicketAppService` 11 个方法全部加 `[AppServiceHttpAction]`：Create/GetById/GetAll/Update/Delete/Assign/Resolve/Close/GetByCustomer/GetByAssignee/GetOverdue；路由、verb、Kind、body 与设计矩阵一致；Assign/Resolve/Close 冻结为 GET + Command + `Ticket.Update`；GetAll 统一 `Ticket.Search`。
- `TicketAppService` 标记 `ContractType=typeof(ITicketAppService)`、`ExplicitHost`、`api/ticket`、`compat.appservice.ticket`；六个领域查询/写动作补齐 optional CT 并向仓储传播。
- 新增 `TicketPermissionDefinitionProvider`，注册设计冻结的五个定义 `Ticket.Create/Get/Search/Update/Delete`（不再产生 `Ticket.GetAll`）。
- `SaaSHelpdesk.Application` 接入 JsonContracts repository transport；真实 Host 调用 `AddCapabilityRuntime`/`AddCrestCapabilityEndpoints`/`AddGeneratedAppServiceHttp` 并 `MapCrestCapabilityEndpoints`；注册 `ISchemaValidator`（正式能力管线需要）。
- 旧 `TicketApi` 归档到 `99_RecycleBin/issue-129-controller-exit/...`；`/api/ticket/all` 由正式贡献唯一拥有。
- 平台共享 root 规则补充：框架标量（string/Guid/数值/bool/日期等）允许任意宿主 source-generated context 回答（OpenAPI context 与贡献 context 元数据一致），feature-private DTO/envelope 仍必须由属主 context 回答。
- 测试迁移：`TicketWireBaselineTests` 改为断言正式 11 端点矩阵（capability id/route/permission），该测试通过；权限基线改为 Search/Update 目标；`TicketWriteSideEffectProbe` 适配 CT 签名。

### 当前已知未闭合（S3 后续增量）

- 权限拒绝响应体仍为空 403（正式 capability pipeline 的失败桥接尚未实现）→ 权限测试的目标 body 断言仍红，属 S3-c/R-04。
- 重复 EfCore UoW binding（三分支）尚未收口，写动作测量仍需 normalization → S3-b。
- Update/Delete 的 client stamp 语义（缺失 428、stale 拒绝、DTO stamp 回读）尚未实现 → S3-c。
- 租户上下文/双租户生产选库、typed validators、H01–H08 尚未完成 → S3-b/c/d。
- 本增量不声称 S3 完成；正式 wire 矩阵测试已绿，其余目标测试按上述增量继续。

## S3 第一增量复审 R1 修复（2 项 P2，2026-10-20）

| 发现 | 修复 | 证据 |
| --- | --- | --- |
| S3-R2-01 共享 JSON root 校验过宽：仅凭 `JsonSerializerContext` 基类即接受，反射 metadata 与改写 Guid/响应字段的 metadata 均通过 | `CapabilityEndpointRegistryBootstrapper` 对 shared roots：由本 Host **激活的贡献 context** 可信；其他 host context 必须是 source-generated 且与属主 context 的 metadata **契约等价**（kind/type/number handling；object 比较属性名/类型/required/custom converter；value 类型比较内置 converter 身份；`OriginatingResolver` 为 `DefaultJsonTypeInfoResolver` 一律拒绝）。真实 SaaSHelpdesk Host 在 Map 前把激活贡献 context 移到 resolver 链首。 | 审查 `ReviewS3_shared_root_must_not_trust_a_context_base_type_over_actual_contract` **8/8**（owned/canonical-host 通过，modified/reflection 拒绝）；DynamicApi 全套 138/138；既有 shared void root 双贡献测试保持通过。 |
| S3-R2-02 wire 探针前缀过时：仍匹配 `api/ticket`，正式路由带前导 `/`，筛出零端点 | `TicketWireBaselineTests` 的 authorization-metadata 筛选改为 `/api/ticket`；新增审查路由探针按 `TrimStart('/')` 归一化后断言 11 个正式端点、display prefix 与零 endpoint 授权元数据。 | 审查 `ReviewS3TicketRoutingProbes` 1/1；正式 wire matrix + endpoint metadata 测试 3/3。 |

### 仍属 S3 后续增量

- 失败桥接（权限拒绝空 403 → CrestErrorResponse）、EfCore UoW 重复 binding、stamp/428/stale/DTO 回读、双租户生产选库、typed validators、H01–H08。S3 整体不放行。
- 本轮证据：DynamicApi 138/138、JsonContracts 162/162、CRUD NativeAOT 23/23。
