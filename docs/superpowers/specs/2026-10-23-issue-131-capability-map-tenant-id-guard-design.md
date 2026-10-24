# #131 能力地图、长尾裁定与 TenantId 架构守卫设计

日期：2026-10-23  
需求：[Issue #131](https://github.com/OrchesAdam/CrestCreates/issues/131)，总控 #121，依赖 #122。  
状态：功能设计完成，供评审；本轮未实现守卫、归档项目、升级支持声明或关闭 Issue。

## 1. 目标与范围

把「项目存在」与「能力交付」分开，提供可导航、可追证、可维护的能力表，并把 TenantId 是租户上下文主键落实为可失败的架构测试。普通 Handler/AppService 不增加注册、权限、租户或支持矩阵判断；不新增运行时 Registry、扫描器、Feature 开关或第二条执行路径。

#131 的 11 类对象拆为 12 个裁定条目、15 个生产项目：Draft 必须区分通用业务草稿与 DescriptorDraft。保留真实平台内核与合法抽象边界，退出无消费者的预留名称及失实入口；不以源文件数决定成熟度。不重做 #129 generated-only 守卫，不重开 #130 ORM 修复，不实现动态插件 AOT 化、云文件产品化或通用事件存储。

本轮工作区源码基线是本地 master `8c8a71de6a47d7c0eb94e8260e9f94035b22a477`；另以只读 Git 对象比对本地已知 `origin/master` 的 #130 合并提交 `348c4d3c35b8682e6b47b6c2dd6058b046c1113a`，未切换 checkout、未刷新远端。15 个范围项目及 153 个源码文件、其直接引用和方案成员关系没有变化，逐项裁定适用于该合并版本。已额外阅读 #130 新 HostCurrentTenant、TenantFilterSourceGenerator、CurrentUserProviderAdapter 与 ORM 矩阵，相关守卫边界见 §4。实施 S1 仍须从届时实际 master 重新采集清单，再执行归档。既有 memory 修改与未跟踪审查材料保留，不覆盖。

## 2. 基线与证据纪律

[结构化盘点](../../review/2026-10-23-issue-131-design-inventory.json)记录每个项目的源码文件 SHA-256、直接 ProjectReference 消费者和根/分层解决方案成员关系，并记录 #130 合并版本的差异核对。方法是静态 XML 路径解析与排除 bin/obj 的源码盘点；不把条件引用当成已执行引用，不覆盖外部 NuGet 消费者，也不把零引用等同可直接删除。配合类型/模块名检索、实现阅读与测试源码检查裁定。

本轮没有运行 build/test/trim/native，也没有重新验证历史原生产物。下文「有测试」只表示现有测试入口；组合 fixture 的已交付状态引用既有证据，不扩张为整个项目背书。文件数仅供定位。

支持记录分为四个独立维度，禁止用一个「支持」覆盖全部含义：

- 定位：抽象契约、聚合装配、执行实现、Provider、实验实现、预留占位。
- 功能状态：已交付指定边界 / experimental / 未交付 / 归档；归档前写「拟归档」。
- 执行证据：JIT 用例及结果 / trimming-verified / NativeAOT-verified / 未验证 / 不适用。只有动态加载等明确依赖 JIT 的机制标 JIT-only；没有 AOT 证据不能推导 JIT-only。
- 处理结论：保留、限界保留或归档，附技术责任、退出/升级条件与迁移说明。

最终地图位于 `docs/Feature/CapabilityMap.md`，按下面的裁定条目提供锚点，不再建立第二份手工维护的完整矩阵。每项必须列项目、唯一装配入口、真实调用点（不仅是 csproj）、行为边界、测试/fixture、证据日期/SHA/命令/结果/限制、责任与结论。未运行写未运行，未知外部消费者写未知。历史审查是发现来源，不是当前支持证书。

## 3. 逐项功能裁定

### 3.1 拟归档的项目

| 条目 / 项目 | 当前消费者与行为事实 | 结论与迁移方向 |
| --- | --- | --- |
| ExternalApi：`src/Integrations/CrestCreates.Integration.ExternalApi` | 1 个 AssemblyMarker，无 ProjectReference 消费者；仍列入解决方案 | 未交付预留名称，拟归档。既有 HttpClient/具体集成承载真实需求；不为名称补一个新 facade |
| LegacyDatabase：`src/Integrations/CrestCreates.Integration.LegacyDatabase` | 同为 1 个 marker，无 ProjectReference 消费者 | 未交付，拟归档；真实数据库适配按既有 Persistence/Integration 边界立项，不能宣称已有旧库集成 |
| Agent.Runtime：`src/Runtime/Agent/CrestCreates.Agent.Runtime` | 1 个 marker、无引用/装配；注释称未来聚合 Memory + Authoring + ControlPlane，csproj 实际没有聚合引用 | 未交付，拟归档。执行面继续由现有 Capability/Workflow/Agent.Tools 等承载；Authoring/ControlPlane 不是待塞进 runtime 的执行内核 |
| Configuration：`src/Framework/Infrastructure/CrestCreates.Configuration` | 无 ProjectReference 消费者；Get 包装 IConfiguration，Set 抛 NotSupported，Subscribe 只存回调，NotifySubscribers 空循环 | 非正式配置管理能力，拟整体归档。启动配置用 IConfiguration/Options，运行时可管理配置用既有 Setting Management；Subscribe 不能宣称可用或给它再补一套管理存储 |
| 通用 Draft：`src/Metadata/Draft/CrestCreates.Draft{,.Abstractions}` | 4+2 个源码文件；Workflow 对 abstractions、Workflow.Tests 对实现有引用，但其 C# 中未检出实际使用；其余消费者为自身测试。TenantIsolatedDraftStore.DeleteAsync 直接透传，null context 放行 | 实验性业务草稿，拟归档两个生产项目与专用测试、移除 Workflow 无效引用。不是 DescriptorDraft 的别名，不自动做 payload 转换，不用其现有弱隔离用例证明治理主链安全 |
| EventStore：`src/Runtime/Eventing/CrestCreates.EventBus.EventStore` | 无 ProjectReference 消费者；仅内存 retry store、反射 JSON serializer；EventRetryService 用 Guid.NewGuid 删除原记录，不能表达真实重试结算 | 未交付可靠事件存储，拟归档。事件投递/重放继续走正式 LocalEvent/RabbitMQ/Outbox；它们不是 event sourcing 数据迁移目标，不宣称具备该能力 |

上述共 7 个生产项目。源码落点：[ConfigurationService](../../../99_RecycleBin/issue-131-capability-map/src/Framework/Infrastructure/CrestCreates.Configuration/Services/ConfigurationService.cs)、[Draft wrapper](../../../99_RecycleBin/issue-131-capability-map/src/Metadata/Draft/CrestCreates.Draft/TenantIsolatedDraftStore.cs)、[EventRetryService](../../../99_RecycleBin/issue-131-capability-map/src/Runtime/Eventing/CrestCreates.EventBus.EventStore/EventRetryService.cs)。

EventStore 的 `IEventStore` / `IEventStoreSerializer` / `IEventRetryStore` 仍位于 `CrestCreates.EventBus.Abstract/IEventStore.cs`。本轮检索未发现正式链消费；实施须重新核验接口、实现及生成输入，再将无消费者的该契约文件一并归档，不能只搬实现却留下可误导的新入口。独立保留文件中的在用契约，不能按目录名整包搬走 EventBus.Abstract。

通用 Draft 专用测试是历史实验行为，不迁往 DescriptorDraft.Tests；与正式内核的负向边界测试（如禁止 PG 注册旧 IDraftStore）仍有防复活价值，应保留或改为归档禁止引用守卫，不能因实验退出而放松边界。

拟归档项的治理责任归 #131 实施 PR；技术责任分别为 Integrations、Agent、Infrastructure/Settings、Metadata/Draft、Eventing。归档是本项明确退出条件，不用「将来需要」无限延期。发现实际消费者时先更新具体证据、给出迁移或重新裁定，不能静默留下兼容执行链。

### 3.2 保留与限界保留

| 条目 / 定位与装配 | 真实消费者 / 行为边界 | 功能与执行证据边界 | 责任与处理条件 |
| --- | --- | --- | --- |
| DescriptorDraft + Abstractions；治理契约/实现，`AddDescriptorDrafts` | Authoring、ControlPlane、DraftContracts、PG Provider、DescriptorControlPlane sample；validator/materializer/review/store 有真实调用 | 保留正式治理主链；内存与 PG store 分开列。现有 DescriptorDraft.Tests、PG AotHost reference-data round-trip；原生覆盖只认既有具体场景，完整 authoring/approval/activation 不由此推导 | Metadata/Draft + Agent 治理面；草稿不是批准，review/package 持久化不是 durable activation，不重开 #88 |
| Localization；基础服务 + contributor，`LocalizationModule` | AspNetCore 异常呈现、ControlPlane 消息有 ILocalizationService 调用；Validation 仅确认项目引用，不计作行为证据 | 保留已交付资源/contributor/culture scope 边界；LocalizationServiceTests 与 Web converter 测试存在。整包 trim/AOT、模块自动 Web 初始化仍未在本项验证 | Infrastructure/Localization；不因为文件少而搬走，不把它扩为新的 Setting 或租户来源 |
| OpenApi；HTTP 文档适配，`AddCrestOpenApi` / `MapCrestOpenApi` | Platform Web、OpenIddict、Library/SaaS/Asset/Procurement Hosts | 保留正式 HTTP 文档装配；现有 options/transformer 与样例入口是 JIT 证据入口。CapabilityEndpoint/Ticket native HTTP fixture 不证明 OpenAPI 文档生成或 Scalar UI 已 native 验证 | Framework/Api；新的文档投影消费正式 capability metadata，禁止重新激活 Controller/反射 scanner |
| FileManagement；执行服务 + 存储 Provider，`FileManagementModule` | 仅检出专用测试，没有 sample/Platform 生产装配。LocalFileSystemProviderTests 实际本地文件 round-trip；服务测试用内存元数据 | 限界保留：本地文件与内存元数据实验模块，有 JIT 测试；不是已交付的持久文件平台。Azure/S3 只记实现存在/外部执行未验证，不能从本地测试推导；整模块 trim/native 未验证 | Framework/Modules/FileManagement；#131 实施 PR 负责将限制写入地图/项目说明。升级须有真实 Host、持久元数据、租户鉴权与云服务成功/失败证据；未达前禁止提升宣传状态 |
| Organization + Abstractions；组织/身份/数据权限内核，`AddOrganizationKernel` | PG Provider、PG AotHost/CrashWorker、内核/持久化/边界测试；组织树、身份投影、数据权限与 cache 有实质逻辑 | 保留平台基础契约。Organization.Tests 与 PG AotHost reference data / versioned organization cache 是具体入口；只沿用其组合范围原生声明，不推导通用组织管理 UI 或所有 Provider | Framework/Modules/Organization + PG reference data；默认内存 store、NullOrganizationContextAccessor 的语义明确列示，不能宣传默认持久或自动获得授权上下文 |
| PluginSystem；动态 DLL 插件实验实现，`PluginSystemModule` | 未检出 ProjectReference 消费者或专用测试；PluginManager 做 AssemblyLoadContext、GetTypes、ActivatorUtilities 动态加载 | 限界保留独立 opt-in 实验项目，动态加载执行面 **JIT-only**，当前功能执行未验证；manifest 有 STJ context 不证明加载路径 AOT 可用。撤销旧比较文档「生产就绪」表述 | Integrations/PluginSystem；由 #131 实施 PR 落实实验说明与隔离守卫。下一次功能扩展前必须具备真实消费 Host、装载/初始化/失败/释放证据，否则不得扩展；选择退出则按本设计归档流程，不转为主链 fallback |

这一组共 8 个生产项目。「责任」是代码区域与交付责任，不伪造 GitHub assignee、团队或交付日期；保留实验项的状态维护由 #131 实施 PR 作者交付并接受仓库维护者审查，不代表有人已承诺另做产品化。

DescriptorDraft 精确 API 以 [DescriptorDraftServiceCollectionExtensions](../../../src/Metadata/Draft/CrestCreates.DescriptorDraft/DescriptorDraftServiceCollectionExtensions.cs)为准；PG 原生场景在 [AotHost](../../../tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/Program.cs) 的 reference-data 与 versioned organization cache 中。完整覆盖索引沿用 [#123 证据矩阵](../../review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md)，缺少可定位的执行记录时标「fixture 存在，结果待复核」，不能仅看到 Program.cs 就写 verified。

合法抽象项目独立保留的理由是上层消费契约、Provider 实现可替换、依赖边界可测；合法聚合项目的理由应是有消费者的静态装配。Agent.Runtime 目前不满足后者。不能因为此次 marker 归档，规定以后所有零代码聚合壳均违规。

### 3.3 代表性调用与测试导航

这些是本轮核对过的源码入口，属于调用/测试存在性证据，执行状态仍按 §2：

- DescriptorDraft：[CompanyCertificationGoldenScenarioHost](../../../samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioHost.cs) 调用 AddDescriptorDrafts；PG AotHost 保存并重新读取 IDescriptorDraftStore。通用 Draft 在 Workflow 只见 csproj 引用，没有对应 C# 调用，不能混计。
- Localization：[DefaultCrestExceptionConverter](../../../src/Framework/Web/CrestCreates.AspNetCore/Errors/DefaultCrestExceptionConverter.cs) 与 [DefaultDescriptorReviewMessageTemplateCatalog](../../../src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewMessageTemplateCatalog.cs) 消费 ILocalizationService；LocalizationServiceTests 覆盖 culture scope/资源 contributor。
- OpenApi：[CrestCreatesWebApplicationExtensions](../../../src/Platform/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs) 实际 Add/MapCrestOpenApi；Library/Asset/Procurement 的 Program.cs 也直接调用。Options 测试不能替代文档 endpoint/transformer 的完整集成结果。
- FileManagement：[LocalFileSystemProviderTests](../../../tests/Framework/Modules/CrestCreates.FileManagement.Tests/LocalFileSystemProviderTests.cs) 包含 UploadAndDownload_RoundTrip_PreservesContent；服务层有 tenant 参数检查测试，但不证明真实身份授权已接通。
- Organization：PG AotHost 的 RunVersionedOrganizationCacheAotScenarioAsync 与 reference-data 组织树/身份投影调用是具体组合入口；不是由 19 个文件或项目存在推导能力。
- PluginSystem：[PluginManager](../../../src/Integrations/CrestCreates.PluginSystem/Services/PluginManager.cs) 的 DiscoverPlugins/LoadPlugin/InitializePlugin 是实现入口，当前没有核实到调用它们的消费 Host，不能写成已验收生命周期。

## 4. TenantId 主键契约

### 4.1 允许的名称流与禁止的身份流

权威上下文仍是 `ICurrentTenant.Id`，已有 `ITenantContext.CurrentTenantId`、数据过滤上下文及事件快照只传递同一 ID；本项不新建 TenantKey 类型、第二上下文或第二身份目录。

TenantName / ITenantInfo.Name / Tenant.Name 可以用于展示、日志、DTO、名称检索和入口别名。别名必须经已有 tenant repository/provider 查询获得实体、保留入口已有 active/lifecycle 校验，再以实体 Id 进入上下文、身份声明、授权/隔离与缓存分区。不在授权/仓储层按名称重新解释身份；不在查不到 ID 时退回名称或 host。

现有 [HeaderTenantResolver](../../../src/Framework/Infrastructure/CrestCreates.MultiTenancy/Resolvers/HeaderTenantResolver.cs) 接受 GUID 或名称并返回 `Success(tenant.Id.ToString(), tenant.Name, ...)`。Query/Cookie/Route/Subdomain 也有名称查找行为；#131 不改 wire key、大小写/规范化、解析优先级和生命周期策略，不把 `X-Tenant-Id: tenant-name` 的兼容入口判成违规。`TenantConnectionStringAppService` 等按名称定位实体后使用 tenant.Id 同样合法。

#130 合并版本的 EF HostCurrentTenant 用 null Id 表示模型过滤的 host 帧，是明确的合法空身份；守卫不得要求它伪造 TenantId。新版 TenantFilterSourceGenerator 已把 required/optional/live-row 过滤静态组合，#131 验证其 ID 来源，不改其 host/shared-row 或拒绝写入语义；沿用 #130 的关系型测试入口。CurrentUserProviderAdapter 是已有 ICurrentUser 到 Provider 身份的 typed adapter，不是新租户权威。ORM 的 SQLite/PG/JIT/原生状态继续链接 #130 独立矩阵，不由能力地图重复背书。

| 允许 | 禁止 |
| --- | --- |
| DTO.TenantName = tenant.Name；日志写租户名；按名称搜索目录 | TenantId = tenant.Name / resolution.TenantName |
| FindByNameAsync(name) → tenant.Id → ChangeAsync(id) | ChangeAsync(name)、SetTenantId(name)，或 ITenantInfo.Id getter 返回 Name |
| tenantid / tenant_id 两个现有 claim 均使用同一权威 ID | 将名称写进这些 claim，或授权时以 principal 的名称匹配租户 |
| tenant.Id / currentTenant.Id 参与仓储/权限/缓存作用域 | TenantName 进入过滤表达式、权限 tenant scope、缓存租户前缀或连接分区 |
| SetTenantContext(id, name) 两参数各司其职 | SetTenantContext(name, name) 或把事件 TenantName 还原成 TenantId |

名称变化应仅影响展示/查询。测试用明确不同的 ID 与名称，包含名称看起来像 GUID、重命名和名称相似两租户，防止变量叫 tenantId 就被当作有效身份。ID 的比较/规范化继续使用现有主链，本项不顺带改变比较语义。

### 4.2 守卫落点与覆盖边界

在现有 `tests/Boundary/CrestCreates.DependencyBoundaries.Tests` 增加 `TenantIdentityArchitectureTests` 及内部分析 helper，复用已有 Microsoft.CodeAnalysis.CSharp；不发布 analyzer 包、不在 runtime 执行检查，也不引入新的 BuildTasks 产品。测试覆盖所有 active `src` 与 sample 的非测试 Compile 源，排除 bin/obj、99_RecycleBin 和测试项目；项目新增会自动进入枚举，不能靠固定文件清单漏掉新实现。生成器模板与实际生成端点通过现有生成器/消费 fixture 回归验证，不能把生成器中的字符串当已绑定运行时符号。

使用 Roslyn 语法/符号及方法内值流，按实际项目边界分析；引用真实声明/构建引用，不能通过把全部项目源码放入一个虚假程序集后忽略绑定错误来获得绿色。测试准备可用测试侧 helper/构建输入清单，不向业务项目添加运行时设施。敏感候选节点绑定失败必须报告覆盖失败（文件、位置、原因），不能以空结果跳过；合成样例才允许最小契约 stub。不是要求此测试重新诊断整个解决方案的所有无关编译错误。

守卫有三个部分，缺一不算交付：

1. **源与出口清单**：按全限定类型/成员身份识别租户实体及 ITenantInfo.Name、TenantName 属性/语义参数；识别 ID 上下文 getter、TenantId/CurrentTenantId 契约写入、上下文切换、resolution success 第一参数、事件 context 第一参数、租户 claim 值、权限/缓存/连接分区参数与实体租户过滤比较。端口表是测试内部代码，不是生产注册扩展点。命名只是候选发现，最终依成员身份/参数位置，不能对所有 `.Name` 或所有日志文本报错。
2. **有限值流**：传播局部变量/赋值、别名、括号/转换、?. / ?? / 条件分支、ToString/Trim/大小写、插值/拼接及 return；任何分支含 Name 值到身份出口即失败。同项目简单 helper 用参数→返回值摘要传播，不能靠 `var tenantId = tenant.Name` 或一层 helper 规避。未知 helper 承载名称→身份出口时报告不能判定并要求补摘要/证据，不视作已净化。目录 lookup 返回实体的 Id 是合法解析边界，**把 name Trim 或改变量名不是解析**。
3. **主链结构断言与行为回归**：锁定 middleware 使用 resolution.TenantId、权限/数据过滤/缓存使用既有 ID 上下文、IdentityClaimsBuilder 两种既有 claim 来自 context.TenantId；在实际目录解析用例中证明名称可进、解析 ID 出来。名称→对象包装（new TenantInfo 等）必须检查 Id 构造参数，不能认为类型叫 ITenantInfo 就已可信。

内部诊断分为名称进入上下文/claim（TI001）、授权/隔离/分区（TI002）、名称伪装成 ID getter/返回契约（TI003）、敏感节点分析不完整（TI900）。输出稳定的规则、相对文件/行列、源→出口与修复提示；代码编号不对外承诺为 generator 诊断 API。

全局规则不禁止 TenantName 声明、不要求改现有 DTO，不按目录豁免全部 resolver/AppService。允许边界精确到已核实成员与参数角色，附正反例；不设置「现有违规全部 baseline」名单。若当前主链发现名字充当主键，应修复该具体正式链并回归；发现超出范围的身份问题单独记录，不能通过新真相来源解决。

这是仓库级、有限模式的架构守卫，不能证明任意跨程序集、动态拼接或外部插件数据流安全。该限制必须写入测试说明；关键主链行为测试与真实身份/租户集成断言继续承担执行保证，不把架构测试宣传为通用安全分析器。

### 4.3 必需正反例及故障控制

| 编号 | 验收要求 |
| --- | --- |
| TI-P01 | 展示/日志/DTO 名称、普通对象 Name 均通过；仅包含字符串 TenantName 的注释不报错 |
| TI-P02 | Header 名称/ID、名称检索 AppService、事件 id+name 传递通过；同一目录实体的 Id 到上下文通过 |
| TI-N01 | 名称直接/局部别名/Trim/插值/?? fallback 进入 ChangeAsync、resolution.TenantId、事件 ID 槽，均命中 |
| TI-N02 | name 进入 identity claims、权限 scope、tenant cache prefix、实体 tenant filter/连接分区均命中；只把名称记录到 audit 展示字段通过 |
| TI-N03 | getter 返回 Tenant.Name；new TenantInfo(name, name, ...)；经一个 helper 返回名称再写 TenantId 均命中。named argument / using alias / fully-qualified 调用不能规避 |
| TI-C01 | 仓库枚举确实读到 active 新增项目；敏感符号未绑定时 TI900 失败；移走某个端口定义或删掉分析扫描不得让真实仓库门禁空跑 |
| TI-B01 | 使用不同 ID/name 的真实目录实体，名称解析后 currentTenant.Id 与 claim/cache/permission tenant slot 仍为 ID；rename 不改变 ID 对应隔离结果；失效/未知租户不进入业务 |
| TI-M01 | 在隔离临时副本把实际 resolver Success 第一参数的 tenant.Id 改 tenant.Name，仓库守卫失败且精确指向该出口 |
| TI-M02 | 在隔离副本把 IdentityClaimsBuilder 的 tenant claim 值改为 context.UserName，验证权威 ID 结构断言失败；把 AuditTenantContextResolver.ResolveAuditCacheKey 的首参数改为 `_currentTenant.Tenant?.Name`，验证 TI002 失败。两项均须保持代码可编译，不能以编译/启动错误充当捕获成功 |

TI-B01 优先扩展既有 tenant resolver / boundary acceptance 测试；能复用真实正式链则不造一个替代权限栈。其余合成反例只测试分析器能否识别模式，不能替代真实源码门禁和变异控制。需要 PG 的用例每次独立 `itest_{guid}` schema，不接触保留资产；纯语法边界用例不要求数据库。

## 5. 项目准入、实验隔离与归档协议

新增生产 csproj 必须在能力地图附准入记录：为什么既有项目不能承载、层次/依赖边界、消费者或有边界的静态聚合目的、唯一声明与装配路径、验证方式、支持级别、维护责任、未交付时的退出条件。抽象项目解释上下游隔离；聚合壳列真实引用/静态装配，不能仅预留 marker。以设计记录审查为准，不把复杂规划做成业务注册协议。

在 boundary 增加地图覆盖测试：active `src/**/*.csproj` 集合与地图项目索引一致、路径真实、无重复无漏项；当前 #131 范围逐项填全证据与结论。对范围外项目只建立导航记录并链接已有领域文档，**不假装本轮重评全部平台**。新增项目缺记录即失败，无法仅加个模块名过门禁。Markdown 地图的固定项目索引区用明确路径字段读取；不再复制一套运行时支持注册表。fixture/test/tool 项目按其作用单列索引或排除说明，不能错误要求其具备业务消费者。

PluginSystem 的隔离由静态引用边界测试证明：正式 Core/Framework/Metadata/Runtime/Persistence/Platform/样例主链没有 PluginSystem 引用或动态装配；只有显式 opt-in 的独立 JIT 消费 Host 可新增依赖，并带准确范围记录。不能给全体 Integrations 或 samples 一个豁免。所有 active 项目不得引用拟退出的旧项目、命名空间与模块注册；方案成员不是消费者，仍须清理。

归档操作只在实施阶段执行，顺序固定：

1. 在实际 master 采集直接/传递引用、using/类型/Attribute/模块依赖、字符串/生成输入、PackageReference/pack 输入、props/targets、root+全部分层 slnx、脚本/发布/AOT 清单、文档导航。外部包消费者无法证明不存在，迁移说明披露此限制，不承诺 API 无破坏。
2. 迁移正式消费者（若发现），移除无效引用及方案/build 输入。对 Draft 移除已确认无使用的 Workflow 及测试引用；维护防复活负向测试。
3. 将完整被替代项目/专用实验测试/死契约文件移动到 `99_RecycleBin/issue-131-capability-map/`，保留原相对路径与 README 裁定/来源/替代方向；永远不直接删除。不因归档导致核心 Abstract 包、Organization 或 DescriptorDraft 被连带移动。
4. 归档目录不得进入默认 Compile/ProjectReference/pack/analyzer/模块聚合；保留对旧路径的禁止引用测试属于守卫，不属于 active 消费。历史 docs/review 保持历史身份，活跃指引移除失实承诺或明确标注历史并链接地图。
5. 验证所有 slnx/项目/build 引用可解析、根及 All 方案构建、影响区域测试。记录命令/SHA/退出码；仅 rg 无匹配不算构建依赖已验证。

本项不新造生产入口，原则上不会触及已验证 runtime 的执行代码。若为 TenantId 修复修改正式执行路径，必须重跑对应既有 native publish/link/run gate；没有 native fixture 的包继续写未验证，不靠此任务补一个空壳 AotHost 提升标签。CI 保持当前人工禁用状态，本地验证不等于已启用持续门禁。

## 6. 文档与验收闭环

实现交付包含 CapabilityMap、必要的项目说明/迁移说明、归档清单、TenantId/实验隔离/项目覆盖边界测试、真实主链正反例与变异证据、AGENTS/memory 同步。`docs/review/crestcreates-abp-comparison.md` 现有 PluginSystem「生产就绪」是明确需要纠正的活跃引用：保留历史来源但加醒目的失效说明，并将具体失实判断改为限定状态/地图引用。不得扩散原有整包 AOT 或按文件数评分结论。

只读核对还发现 #130 合并版本 `docs/Feature/OrmProviderSupportMatrix.md` 页首仍写「尚未合并」；这是状态元数据滞后，不是推翻其逐组合证据。本项文档同步时按实际 merge SHA/证据修正页首并链接该唯一矩阵，不复制 ORM 支持表、不重开实现。

| #131 原验收 | 本设计落点 / 实施完成条件 |
| --- | --- |
| 能力表含定位/消费者/边界/支持/证据/结论 | §2–3；15 项目逐项索引，声明引用可定位证据，保留项有真实调用点，实验项有责任/退出条件 |
| 新项目说明承载理由，不新增执行路径 | §5；准入记录与全 active 项目索引覆盖测试；Registry/handler 数量不因地图增加 |
| TenantId 守卫，不误报名称展示/查询 | §4；仓库扫描、正反例、真实解析行为和实际源代码变异全部通过；敏感绑定失败不被跳过 |
| AGENTS/memory/宣传一致，归档前引用/方案/build 验证 | §5–6；迁移/归档证据、根/All 构建与受影响测试；状态同步区分设计/实施/合并 |

主要风险是误把抽象壳当占位、误把名称入口兼容当身份违规、外部消费者未知，以及把架构扫描结果夸大为安全或 AOT 保证。对应控制为逐项消费者核验、目录解析边界正反例、迁移披露、可定位执行证据。#131 设计完成不等于其验收已勾选，Issue 继续 open；具体实施见[实施计划](../plans/2026-10-23-issue-131-capability-map-tenant-id-guard-implementation.md)。

---

## 7. 实施记录（2026-10-23，S1–S4）

设计正文保持原样；本节记录实施时的实现口径与已确认偏差，供评审逐项核对。逐条证据见
[实施证据](../../review/2026-10-23-issue-131-implementation-evidence.json) 与
[实施日志](../plans/2026-10-23-issue-131-issues-log.md)。

- 基线：实施基线为 `348c4d3c`（= `origin/master`）。§1 的只读核对在实施基线上重跑，结果 `TOTAL_DIFF 0`（15 项目、153 源文件、直接消费者、方案成员），裁定无需改判；§5 步骤 1 额外发现并清理了 `samples/ProcurementApproval/CrestCreates.Sample.ProcurementApproval.slnx` 的 Draft.Abstractions 成员与两条运行 `CrestCreates.Draft.Tests` 的 CI 步骤（CI 保持人工禁用）。
- 归档口径：7 个生产项目 + 通用 Draft 专用测试项目 + 无消费者的 `IEventStore.cs` 移入 `99_RecycleBin/issue-131-capability-map/`，git 记录为 28 条 R100 重命名（逐字节一致、未删除、未改写）；`EventBus.Abstract` 其余在用契约留在 active。7 个项目均非 `IsPackable=true`，故无包输入影响。
- 守卫实现口径（三项强于或细化设计）：
  1. **跨项目第一方 helper**：设计要求同项目 helper 的参数→返回值摘要。实施额外建立第一方声明索引，使另一个生产项目里的 helper 也按其**真实源码**内联判定；不可判定仍出 TI900，不放行。
  2. **生成物的双重角色**：设计排除 `obj`。为避免把 authored 代码判成未绑定（真实构建里生成物就是 Compile 输入），实施把 `obj/{cfg}/{tfm}/{generated,source-generators}` 仅用于**符号解析**，扫描对象仍是 authored 源；生成器模板字符串因此不会被当成已绑定符号，实际生成端点继续由各自 fixture 断言。
  3. **引用集合**：共享框架 → 项目自身 bin 构建闭包 → `project.assets.json` 的 compile 资产，并按程序集简单名去重。不去重时旧版 `Microsoft.AspNetCore.Http.Abstractions` 2.3.9 与 10.0.0 并存会产生 CS0433，使整个文件所有类型绑定失败并误报数百条 TI900；这是实现约束，评审复现时须保留该去重顺序。
- 结构断言实现：9 行锁定表（中间件、四个 resolver 与扩展、两类 claim、审计缓存键、租户缓存失效器、contributor 内部转发），断言出口参数的**终端值名**确实来自 `Id`/`TenantId`/`ResolveTenantIdOrNull`；锁定行若找不到对应出口即失败，防止门禁空跑。TI-M02 的 `context.UserName` 变异正是由该断言（而非值流）捕获，与设计 §4.2 第 3 部分一致。
- 反例计数记录：TI-N01 命中 8 条 = 7×TI001 + 1×TI003（身份名局部变量被赋名称值）；TI-N02 命中 6 条 = 2×TI001（两类 claim，按设计「claim 归 TI001」）+ 4×TI002；TI-N03 命中 3×TI003 + 2×TI001（含一层 helper、named argument、`using` 别名）。设计未规定计数，此处仅为可复核性记录。
- 已声明限制（写入测试说明与地图）：解构声明 `var (a, b) = ...` 的别名传递不被跟踪；跨程序集动态拼接与外部插件数据流不在覆盖范围；架构测试不作为执行证据或 `NativeAOT-verified` 依据。
- 故障控制复现：删除地图任一行必须使覆盖门禁失败（已验证）；给正式主链项目加一条 PluginSystem 引用必须使隔离门禁失败（已验证；首次注入因相对路径深度错误指向不存在目录而未被触发，纠正后生效，该无效尝试也已记入证据）。
- 未做：不提升任何支持声明，不新增 native fixture，不改 CI 启用状态；Issue #131 保持 open，验收由维护者核对 §6 四条后勾选。

### 7.1 R1 审查修复（2026-10-24）

首轮实现审查（head `40bf53eb`）提出 8 项 P2，全部成立并在同一分支修复，逐条映射见
[实施日志](../plans/2026-10-23-issue-131-issues-log.md) I-09。相对设计正文的实质性收口：

- **扫描输入改为真实 Compile 集合**：默认 glob − 显式 `Compile Remove` + 显式 `Compile Include`（含项目目录外 linked 源），并用项目实际配置/TFM 的 preprocessor symbols 解析；推导值与 `dotnet msbuild -t:AddImplicitDefineConstants -getItem:Compile -getProperty:DefineConstants` 在代表项目上逐项相等（`TI_C01i`）。§4.2「覆盖所有 active Compile 源」现在按字面成立，而不是按目录近似。
- **值流状态与来源分离**：环境保存 Clean/Tainted/Undecidable 与来源集合，别名/再赋值/分支合并不再丢失状态；§4.2 第 2 部分的「不能靠 `var tenantId = tenant.Name` 或一层 helper 规避」扩展到未知来源经别名的情形（TI900），helper 内联改为对自身声明与赋值做不动点后只汇总 return。
- **身份出口按参数/属性符号而非书写位置**：Claim 与 ITenantInfo 构造使用 bound argument→parameter 映射（named/optional/隐式 new），`ITenantInfo.Id` 的对象初始化与后续赋值按属性所属接口识别（TI003）。
- **权威来源断言按来源集合**：§4.2 第 3 部分的行锁从「终端名像 ID」升级为「来源集合等于该行允许的成员/参数」，因此 `context.UserName → var tenantId → claim` 这类改名别名不再可能放行。
- **MSBuild 路径归一化**：引用守卫先归一化分隔符再解析（Linux 上反斜杠是普通字符），并覆盖 `$( )` 表达式；正/反斜杠等价负控入库。
- **证据链提交**：S1 盘点 JSON 纳入版本库，设计 §7 与归档源码链接修正，并新增 `Issue131EvidenceLinks_AllResolve` 覆盖地图/设计/计划/日志四份文档的相对链接。

「未做」不变：不提升任何支持声明、不新增 native fixture、不改 CI 启用状态。

### 7.2 R2 审查修复（2026-10-24）

第二轮审查（head `adbbf5dd`）提出 3 项 P2，全部成立并已修复；逐条映射见
[实施日志](../plans/2026-10-23-issue-131-issues-log.md) I-10。要点：

- **输入获取改为消费 MSBuild evaluated Compile 集合**（条件、导入、属性函数、TFM/配置常量均由 MSBuild 求值），守卫不再自带一份会与构建漂移的推导；每个项目+配置的结果按构建脚本与文件清单哈希缓存，求值失败一律 TI900。仍属「测试侧构建输入清单」，不新增生产设施。
- **常量 fallback 不被权威分支净化**：中性常量（`null`/`default`/空串/`throw`）不贡献来源；非空字符串常量贡献 `literal:"…"` 非权威来源，因此 `context.TenantId ?? "acme-east"` 无法通过权威来源锁定。
- **helper 形参不再自造来源**：被实参替换的形参以调用方来源为准；未被替换的顶层形参保留其声明角色，TI002 的 `tenantId` 参数行语义不变。透传式等价重构（直接属性 / 局部别名 / `Identity(x) => x`）来源集合一致。

同时说明一处测量更正：上一轮作者侧「隔离副本三处变异仍全通过」的结论因测试运行目录使用符号链接而被审查方撤回；本轮 R2 结论基于实体副本，作者侧同类结论已改用实体复制（`AnalyzeScratch`/`Mutate` 均写入独立临时目录并在 finally 删除）。

### 7.3 R3 审查修复（2026-10-24）

第三轮审查（head `0cc8ef9b`）提出 4 项 P2，全部成立并已修复；见
[实施日志](../plans/2026-10-23-issue-131-issues-log.md) I-11。

- **缓存与真实输入同源**：evaluated 输入的缓存键现在同时覆盖项目目录外 `Compile Include/Remove` 的搜索根（目录与文件清单签名），并在命中后校验路径仍存在；「第一次正确」不再隐含「热读正确」。
- **求值不可用即覆盖失败**：输入端把「MSBuild 求值失败（输入未知）」与「输入已知但缺符号上下文（无 bin/引用）」分开——前者无条件 TI900（不再被「Sources 为空」过滤），后者仅在已知清单含租户相关源时 TI900。
- **常量与 throw 的值来源**：常量字段按编译期值保留来源（跨文件、跨程序集一致），`throw` 在值域中性。权威来源锁定因此既拒绝「非空常量 fallback」，也不再误伤 fail-closed 失败分支。
