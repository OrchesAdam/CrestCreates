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

### fixture 修复（rev.2 §14.3 明确指令）

`samples/SaaSHelpdesk/SaaSHelpdesk.Tests/Fixtures/HelpdeskWebApplicationFactory.cs` 删除自身对 `AddUnitOfWork(OrmProvider.EfCore)` / `AddEfCoreUnitOfWork()` 的调用：fixture 只替换连接与叶子设施，不再声明第三个 owner。删除后宿主仍有 2 份冲突绑定（全量计数不变，70 fail），证明必须修的是装配 owner 而非测试代码；R01 目标计数继续固定为 1。

### 审查者的两条事实控制

`docs/review/probes/2026-10-17-issue-129-s1-permission-facts.patch` 以 `git apply` 原样入库为 `Review129S1PermissionFacts.cs`，实测 2/2 通过。

### 计数

| 套件 | 结果 |
| --- | --- |
