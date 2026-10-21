# #129 实施计划与设计交接

状态：S1/S2/S3 已按各轮审查放行（S3 head `1cca837c`）；S4 于 2026-10-21 修订为“消费者迁移与旧链退出”，当前进行中；S5 未开始。设计基线 `196b6f66048ef32d644eeb856d00d58ae9d34965`；规范见 `docs/superpowers/specs/2026-10-17-issue-129-generated-http-controller-exit-design.md`。开工前重新核对 master、进行中的 Tooling/Web PR 和 #128 交接，保持串行。

rev.2 全文已发布并回读核对：[最新设计](https://github.com/OrchesAdam/CrestCreates/issues/129#issuecomment-6230158783)。

rev.1 详细设计全文曾发布到 [#129 评论](https://github.com/OrchesAdam/CrestCreates/issues/129#issuecomment-6228947951)，发布后回读核对与本地 Spec 原文一致。

## S1 — 调用图、行为基线与设计冻结（需补件后复审）

- [x] 编译真实 sample，建立调用图与旧链11动作快照；独立复跑6绿+8约定红/14。证据来自 WebApplicationFactory，归一化写链另列，不等于生产进程直跑。
- [ ] 将 rev.2 的目标矩阵与旧快照分开：GetAll 旧端点 GetAll+业务 Search，正式统一 Search；正式权限五项，GET 写动作转 Update；统一404 CrestErrorResponse。同步事实稿与迁移表。
- [ ] 补真实引用程序集/官方STJ的组合信封同轮不可见与跨Host全局激活负例；producer/引用闭包正向守卫。完整B01–04在S2转绿，不能把S1缺证据直接挪走。
- [ ] 增强R03/R04/R08的零业务/事务/持久化断言；R06 fresh-scope验证未删；R07返回值与存储stamp精确相等及修改后更新。补合法stamp正例。
- [ ] 修正权限推断并入库两条审查控制：未定义名字可被现有grant/checker接受；单GetAll不能满足真实service的Search检查。
- [ ] 按测试FQN补62条既有红账本，32+17+10+2=61的差额及仅凭状态推断的根因不得带入完成结论。
- [ ] 冻结新 Attribute/model/manifest/诊断、failure bridge、error metadata 所有者及项目依赖图；原规范与本次设计的名字是拟定 API，不作为已存在能力。
- [ ] 输出具体 legacy consumer/owner 允许清单与归档替代表，审查通过后进入 S2。

## S2 — 通用 HTTP 投影与 JSON

- [x] S2.1 共享模型与声明面：`ContractType`/`RegistrationMode`/`AppServiceHttpActionAttribute`、`AppServiceHttpProjectionModel`+builder+wire convention（source-link）、CCHTTP001–008、静态注册器跳过显式 host 声明并给迁移告警；R1 复核后修正四项（显式 host 缺契约不落静态链、owner-qualified 默认 ID/信封、闭合泛型、私有 DIM），审查探针原样入库，定向 15/15 pass。
- [x] S2.2-A 共享 emitter：每契约一个 bundle（信封 + ExplicitHost 贡献 + 契约顺序的 typed binder/invoker/result + JSON roots/identities）+ 每程序集一个 `AddGeneratedAppServiceHttp()` 激活；逐 artifact manifest（共享 provenance + ContentHash）；R1 修复后 14 条审查探针 + 3 条自测 **17/17**。CRUD 已共享 provenance/JSON configurator 两条规则，binder/invoker/descriptor 完全归一仍待接续。
- [x] S2.2-A2 后端收口：中性 `HttpWireModel` + 共享 `HttpWireEmitter`；手写与 CRUD 前端都映射到同一 wire，CRUD 删除 21 个重复发射方法，激活使用共享 invoker 命名/JSON configurator；定向 24/24、CRUD 形状 54/54。
- [x] S2.2-B 工具链物化：BuildTasks 前置链（`PrepareCrestAppServiceHttpInputs` → 输入指纹 → state → `prepare-appservice-http` → `IncludeCrestAppServiceHttpArtifacts`）显式接入 `CrestCreatesJsonContractGenerationDependsOn`；JsonContracts 从符号重建 roots 并与 outputs manifest 逐 artifact 对账，生成 `HttpProjectionJsonContext_<owner>`（camelCase）由官方 STJ 在同一 CoreCompile 关闭；无工具链的正式声明 CCHTTP002 Error。证据：JsonContracts 144/144；真实 MSBuild 7/7（物化/STJ 同轮、manifest 篡改、未知 schema、no-op/clean/rename、design-time、TFM+RID、打包外部消费）；见 issues-log「S2.2-B3/B4」。Map 期 Host 激活与 manifest ownership 仍属 S2.3。
- [ ] 扩展前置工具链 ordinary Compile、完整 ownership/provenance、显式 DependsOn、诊断与错误状态；复用现有 stamp/transport，不复制实现。
- [ ] 自动 body/input/scalar/list/typed result/void/error contracts；官方 STJ 单 driver/真实 MSBuild 可见。
- [x] S2.3 AddGeneratedAppServiceHttp 显式贡献/handler/JSON 装配，业务 DI owner 不重复；自契约类模式支持，Asset/Procurement 迁移为 ExplicitHost 并在真实 Host 组合（capability registry 含 contributions、JSON context 优先、Map 期 fail-closed）。证据：DynamicApi 130/130、Procurement 26/26（正式兼容路由 golden）、Asset 29/29、CodeGenerator 551/551；Asset 兼容 HTTP 端点未在本环境起 PG Host 独立验证。
- [ ] B01–04/H08、生命周期与外部 packaged consumer 验收通过。

## S3 — Ticket 与真实 Host（先装配与启动，再验收业务）

- [x] 先落 PlatformDefault/ApplicationOwned 持久化 owner：SaaSHelpdesk由EF模块装配一次，Platform不残留SQL预设，factory不声明Provider；启动前验证零/重复binding和实际context身份。保留#137冲突拒绝。证据：U01/U02/U04 更新为单一 ApplicationOwned；`UnitOfWorkCompositionValidator` 零/重复 binding 启动失败；R01 1 个 binding。
- [x] 诊断真实Program启动挂起，阶段日志+有界等待+进程回收；生产PG监听/登录/Ticket读写证据独立于WebApplicationFactory。证据：真实 PG + Production + HTTPS 进程 4s 监听，health/login/create/read/validation 400/428/409/delete 200/404，SIGTERM 2s 退出（见 issues log「S3 第二增量」）。
- [x] ContractType 限定 ITicketAppService；完整路由/Kind/五权限定义，端点和业务校验同源，真实非SuperAdmin grant/revoke，继承基类未声明方法不暴露。证据：wire matrix 11 端点、F02/C11/C12/C13 绿，基类未声明方法不在动作集。
- [x] 六个写动作接唯一 UoW，11 动作 CT 传递；真实 typed validation 和 stamp 语义。证据：基类与手写写动作均真实织入，H07 六写各 1 次事务；R05/R06/R07/R15；R08 + 缺 validator fail-closed；H06 request token 到达业务服务。
- [x] Runtime 中立失败facts + Web adapter；按rev.2的kind/code/status映射，绑定失败局部400，业务/服务器JsonException不被全局改为400；null与异常资源不存在统一Web404；未知错误脱敏、取消事实独立。证据：`CapabilityFailureFacts`/classifier/presenter；H05 404/400/500 脱敏；CRUD typed 404 envelope 与 MCP capability code 保持。
- [x] Host 正式 capability 装配，既有认证/租户/审计保持；补双租户生产选库 factory，不能用 shared connection 证明隔离。证据：`DualTenantDatabaseIsolationTests` 3/3（真实 options contributor + UoW + 两 schema）；`TenantBoundaryAcceptanceTests` 3/3。
- [x] H01–08 真实 PostgreSQL/认证/授权/租户/失败/取消/UoW 验收通过。证据：见 issues log「S3 第二增量」逐项矩阵；H08 由 DynamicApi 138/138、JsonContracts 162/162 覆盖；Ticket NativeAOT 仍属 S5。
- [x] 统一审查 R1（1 P1 + 5 P2）修复：租户目录 scope 隔离与 HTTP 双租户验收、OCE R/K/L 身份分类、业务 JSON/Format 500、presenter 复用本地化权威、六个手写动作业务权限、共享 string converter 比较。证据见 issues log「S3 统一审查 R1 修复」。
- [x] 统一审查 R2（取消项）：真实 EF UoW deadline/caller 经 typed snapshot 接入分类、响应抑制与分类解耦、Capability 旧 TimedOut 断言同步。证据见 issues log「S3 统一审查 R2 修复」。
- [x] 统一审查 R3（取消组合路径）：快照改为帧链并按 execution token 合并（嵌套 Required）、`CombineWithCurrent` 记录 operation 派生 token、未匹配第三 token 保持 NonCaller。证据见 issues log「S3 统一审查 R3 修复」。
- [x] 统一审查 R4（三层 Required 帧序）：内核记录真实 execution parent（含 join），帧带 `DepthFromRoot` 并按深度合并，root caller 恒为真实请求。证据见 issues log「S3 统一审查 R4 修复」。

## S4 — 消费者迁移与旧链退出

> 2026-10-21 目标修订：S4 不再以“保留三个 Legacy 包 + allowlist”作为退出条件；必须把剩余消费者迁到正式投影，并让旧链不再参与构建和运行。

- [x] 更新 Spec/实施计划/契约 fixture/归档记录/PR 描述：S4 退出 = 构建图零 Legacy 项目、样例端点只由正式贡献发布、正式产物不调用旧 runtime/Add/Map。
- [x] SaaSHelpdesk 七个 AppService 全部改为正式投影：逐动作保留旧 route/verb/query/body/If-Match/permission，显式 validator；自定义写动作补 `[UnitOfWorkMo]`；删除 `AddLegacyGeneratedApi`/`MapLegacyGeneratedApi` 与 Legacy analyzer/runtime/Web 引用。
- [x] LibraryManagement Book/Category/Loan/Member 全部改为正式投影：因继承 CRUD 契约改用自契约投影适配器，保留旧 route/query/body/delete If-Match/permission；接入正式 precompile/JSON/Map 装配，删除 Legacy 引用与旧 Map。
- [x] 测试与 fixture 迁移：SaaS/Library 行为、错误响应、权限/UoW/校验断言改跑正式链；CompatibilityProjection.E2E、CapabilityEndpoint AOT fixture 去掉 Legacy analyzer/runtime；旧输出形状专用测试（legacy analyzer isolation、allowlist/map guard、legacy runtime/convention、S4-R1 probes）归档。
- [x] 三个 Legacy 项目、旧生成器/runtime/Add/Map/allowlist、Controller 生成器、旧 CRUD stub、TicketApi 与死反射 helpers 归档；从 `.slnx`、项目引用、`Directory.Build.Aot.props`、analyzer/package 分发移除。正式路由冲突预检保留在 `MapCrestCapabilityEndpoints`。
- [x] 新增退出门禁：构建图/解决方案零 Legacy 项目；正式程序集零旧类型/旧调用符号；样例 Host 路由集合只来自正式贡献；归档代码不在 Compile/analyzer/package glob；原样重新引入 legacy call 的变异能被守卫捕获。
- [x] 受影响回归：SaaSHelpdesk 全量、Library/IntegrationTests、Web、CodeGenerator、DynamicApi、Application、Capability、OrmProviders、JsonContracts、CompatibilityProjection.E2E、CRUD/AOT、Boundary；记录实际计数与既存红。S5 的 Ticket NativeAOT 在 S4 放行后开始。

## S5 — Native、回归与交接

- [x] Ticket native fixture 使用真实服务/DTO/正式投影，持久化叶子用诚实声明的静态内存 provider；不换成等价假业务服务。证据：`tests/Framework/Api/CrestCreates.TicketHttpProjection.AotFixture` + `.Tests`；真实 `TicketAppService`/DTO/validator，内存 Ticket/SLA 仓储与 UoW，fixture 自有 identity/permission/failure presenter；JIT 1/1。
- [x] publish/link/run、loopback、逐场景/总 sentinel、真实 metadata 拒绝和变异、进程回收证据。证据：原生门禁 3/3（绿色 + wrong-expectation + wrong-validation），15 场景 + `CRESTCREATES_TICKET_NATIVE_OK`，证据 `tests/artifacts/aot-native-evidence/ticket-native-http*.json`。
- [ ] 复跑 #128、CapabilityEndpoint 与受影响 LocalEvent/RabbitMQ/JSON/package/边界门禁；按最终提交留下实际计数。
- [ ] 同步 memory/AGENTS/#123 evidence matrix/migration/归档说明/支持边界，发布复审入口。
- [ ] 本任务不自动合并、不恢复 CI；设计发布不代表任一实现切片已完成。

## rev.2 的范围和交接要求

- POST /api/user 及 framework Setting/Feature/Audit HTTP缺失单独记账，不扫描引用程序集补路由，不为此给DDD Application加Web依赖。Ticket测试准备可以用正式业务服务/数据种子，然后必须走真实登录与权限；这不算User HTTP已修复，原相关失败测试不改成404正例、不skip。
- 双租户生产选库factory归S3；S1写清责任与拒绝条件，不要求提前完成S3实现。
- review入口：docs/review/2026-10-17-issue-129-s1-design-review-round1.md；两条事实控制补丁：docs/review/probes/2026-10-17-issue-129-s1-permission-facts.patch。
- 需把本地rev.2带回PR开发分支后逐项补件。本次只修订设计与评审材料，不替开发分支实现S2，不自动合并，不恢复CI。

## S2 前置：引用程序集声明范围设计增补（2026-10-17）

- 已交付 `docs/superpowers/specs/2026-10-17-issue-129-referenced-declaration-scope-addendum.md` 与契约 fixture 同步；事实探针 `ReferencedAssemblyScopeProbeTests` 2/2 绿（引用程序集内声明不可见、本地锚点 + 引用契约可投影）。
- 该增补通过审查后才启动 S2 实现；实现按增补 §4 的 R-01–R-06 作为 S2 验收。
