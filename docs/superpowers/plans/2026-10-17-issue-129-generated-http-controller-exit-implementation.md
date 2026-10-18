# #129 实施计划与设计交接

状态：设计 rev.2 已根据 PR #152 / `602d6e40` 的 S1 证据修订；S1 退出暂未通过，S2/S3 未开始。设计基线 `196b6f66048ef32d644eeb856d00d58ae9d34965`；规范见 `docs/superpowers/specs/2026-10-17-issue-129-generated-http-controller-exit-design.md`。开工前重新核对 master、进行中的 Tooling/Web PR 和 #128 交接，保持串行。

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
- [ ] S2.2-B 工具链物化：BuildTasks task/targets、CLI/transport/stamp 复用、显式 DependsOn、JsonContracts context 生成与 Map 期激活验收。
- [ ] 扩展前置工具链 ordinary Compile、完整 ownership/provenance、显式 DependsOn、诊断与错误状态；复用现有 stamp/transport，不复制实现。
- [ ] 自动 body/input/scalar/list/typed result/void/error contracts；官方 STJ 单 driver/真实 MSBuild 可见。
- [ ] AddGeneratedAppServiceHttp 显式贡献/handler/JSON 装配，业务 DI owner 不重复；转换已有 Asset/Procurement 等正式 compatibility consumer。
- [ ] B01–04/H08、生命周期与外部 packaged consumer 验收通过。

## S3 — Ticket 与真实 Host（先装配与启动，再验收业务）

- [ ] 先落 PlatformDefault/ApplicationOwned 持久化 owner：SaaSHelpdesk由EF模块装配一次，Platform不残留SQL预设，factory不声明Provider；启动前验证零/重复binding和实际context身份。保留#137冲突拒绝。
- [ ] 诊断真实Program启动挂起，阶段日志+有界等待+进程回收；生产PG监听/登录/Ticket读写证据独立于WebApplicationFactory。
- [ ] ContractType 限定 ITicketAppService；完整路由/Kind/五权限定义，端点和业务校验同源，真实非SuperAdmin grant/revoke，继承基类未声明方法不暴露。
- [ ] 六个写动作接唯一 UoW，11 动作 CT 传递；真实 typed validation 和 stamp 语义。
- [ ] Runtime 中立失败facts + Web adapter；按rev.2的kind/code/status映射，绑定失败局部400，业务/服务器JsonException不被全局改为400；null与异常资源不存在统一Web404；未知错误脱敏、取消事实独立。
- [ ] Host 正式 capability 装配，既有认证/租户/审计保持；补双租户生产选库 factory，不能用 shared connection 证明隔离。
- [ ] H01–08 真实 PostgreSQL/认证/授权/租户/失败/取消/UoW 验收通过。

## S4 — Legacy 隔离与归档

- [ ] 仍有消费者的旧 Minimal API runtime/analyzer/adapter 显式隔离、单向依赖、有限允许清单；无 provider 时无自动 fallback。
- [ ] TicketApi、Controller 两生成器、GeneratedApiController/ApiOverride 分支、旧 CRUD stub bundle、死反射 route helpers 及专用旧测试移到回收站。
- [ ] scalar helper 留在实际消费者所属边界；保留正式 CrudServiceSourceGenerator 诊断入口；旧 warning 随真实旧实现退出移除。
- [ ] 更新默认/legacy analyzer 分发、Compile/package globs、SDK 配置，避免归档代码继续生效或新 analyzer 循环依赖。
- [ ] L01/L02 的依赖、语义符号、运行路由与变异守卫通过。

## S5 — Native、回归与交接

- [ ] Ticket native fixture 使用真实服务/DTO/正式投影，持久化叶子用诚实声明的静态内存 provider；不换成等价假业务服务。
- [ ] publish/link/run、loopback、逐场景/总 sentinel、真实 metadata 拒绝和变异、进程回收证据。
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
