# CrestCreates 能力地图

日期：2026-10-23（设计 2026-10-23 定稿，S1–S3 已在实施 PR 落地）
需求：[Issue #131](https://github.com/OrchesAdam/CrestCreates/issues/131)，总控 #121，依赖 #122。
设计：[功能设计](../superpowers/specs/2026-10-23-issue-131-capability-map-tenant-id-guard-design.md)、[实施计划](../superpowers/plans/2026-10-23-issue-131-capability-map-tenant-id-guard-implementation.md)。
实施基线：master `348c4d3c35b8682e6b47b6c2dd6058b046c1113a`（PR #154 合并，含 #128/#129/#130）。
平台执行状态以 [memory.md](../../memory.md) 与 [#123 原生执行门禁证据矩阵](../review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md) 为准；本地图不复制 ORM 支持表，不重复背书。

## 1. 记录维度

每个条目用四个独立维度记录，禁止用一个「支持」覆盖全部含义：

| 维度 | 取值 |
| --- | --- |
| 定位 | 抽象契约 / 聚合装配 / 执行实现 / Provider / 实验实现 / 预留占位 |
| 功能状态 | 已交付指定边界 / experimental / 未交付 / 拟归档 / 归档 |
| 执行证据 | JIT 用例及结果 / trimming-verified / NativeAOT-verified / 未验证 / 不适用 |
| 处理结论 | 保留 / 限界保留 / 归档，附技术责任、退出或升级条件、迁移说明 |

证据纪律：

- 「有测试」只表示存在测试入口；组合 fixture 的已交付状态引用既有证据，不扩张为整项目背书。
- `NativeAOT-verified` 必须指向真实 `PublishAot` publish/link/run 记录，且逐项限定 RID 与场景范围。
- 未运行的写「本轮未运行」；外部 NuGet 消费者写「未知」。历史审查文档是发现来源，不是当前支持证书。
- 文件数仅供定位，不作为成熟度或占位判定依据。

静态盘点方法：XML 路径解析收集 `ProjectReference` 与 `.slnx` 成员，逐源码文件计算 SHA-256（排除 `bin/obj/99_RecycleBin`），配合类型/模块名检索与实现阅读。条件引用不当作已执行引用，零引用不等同于可直接删除。

## 2. #131 裁定条目

12 条裁定覆盖 15 个生产项目：已归档 7 个（D-01…D-06，落点见 §4.1 与 `99_RecycleBin/issue-131-capability-map/README.md`），保留或限界保留 8 个（D-07…D-12）。

归档状态口径：设计阶段写「拟归档」，S3 执行后本表写「归档（S3 已执行）」。防回流由 [ArchivedProjectRegressionArchitectureTests](../../tests/Boundary/CrestCreates.DependencyBoundaries.Tests/ArchivedProjectRegressionArchitectureTests.cs) 承担。

### D-01 预留占位：ExternalApi（已归档）

| 维度 | 记录 |
| --- | --- |
| 项目 | `src/Integrations/CrestCreates.Integration.ExternalApi/CrestCreates.Integration.ExternalApi.csproj` |
| 定位 | 预留占位（1 个 `AssemblyMarker.cs`，`namespace CrestCreates.Integration.ExternalApi;`） |
| 唯一装配入口 | 无。csproj 无 `ProjectReference`、无 `PackageReference`、无 `[CrestModule]` |
| 真实调用点 | 基线 `348c4d3c` 全仓检索：仅 `CrestCreates.slnx:225`、`solutions/CrestCreates.All.slnx:195`、`solutions/CrestCreates.Integrations.slnx:13` 的方案成员；无 ProjectReference 消费者，无 `using`、类型或模块依赖 |
| 功能状态 | 归档（S3 已执行）；归档前状态为未交付 |
| 执行证据 | 不适用；外部 NuGet 消费者未知 |
| 处理结论 | 归档。既有 HttpClient/具体集成承载真实需求；不为名称补新 facade。技术责任 Integrations。迁移：真实外部 API 适配按 `src/Integrations` 既有边界重新立项并附准入记录（设计 §5） |

### D-02 预留占位：LegacyDatabase（已归档）

| 维度 | 记录 |
| --- | --- |
| 项目 | `src/Integrations/CrestCreates.Integration.LegacyDatabase/CrestCreates.Integration.LegacyDatabase.csproj` |
| 定位 | 预留占位（1 个 `AssemblyMarker.cs`） |
| 唯一装配入口 | 无 |
| 真实调用点 | 仅 `CrestCreates.slnx:226`、`solutions/CrestCreates.All.slnx:196`、`solutions/CrestCreates.Integrations.slnx:14` 方案成员；无引用消费者 |
| 功能状态 | 归档（S3 已执行）；归档前状态为未交付 |
| 执行证据 | 不适用；外部 NuGet 消费者未知 |
| 处理结论 | 归档。真实旧库适配按 `src/Persistence` / `src/Integrations` 边界立项；在具备该能力前，任何文档不得宣称仓库已有旧库集成。技术责任 Integrations |

### D-03 聚合壳（无消费者）：Agent.Runtime（已归档）

| 维度 | 记录 |
| --- | --- |
| 项目 | `src/Runtime/Agent/CrestCreates.Agent.Runtime/CrestCreates.Agent.Runtime.csproj` |
| 定位 | 声称的聚合装配壳：`AssemblyMarker.cs` 注释为未来聚合 Memory + Authoring + ControlPlane；csproj 实际没有任何聚合 `ProjectReference` |
| 唯一装配入口 | 无（marker-only） |
| 真实调用点 | 方案成员 `CrestCreates.slnx:157`、`solutions/CrestCreates.All.slnx:136`、`CrestCreates.Platform.slnx:119`、`CrestCreates.Runtime.slnx:54`；另有两处负向守卫把它列为禁止引用片段：[DependencyBoundaryTests.cs:64](../../tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs#L64)、[:258](../../tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs#L258) 与 [RuntimePersistenceArchitectureTests.cs:66](../../tests/Boundary/CrestCreates.DependencyBoundaries.Tests/RuntimePersistenceArchitectureTests.cs#L66)。无执行面消费者 |
| 功能状态 | 归档（S3 已执行）；归档前状态为未交付 |
| 执行证据 | 不适用；历史计划文档（`docs/superpowers/specs/2026-07-16-phase-8f-agent-tool-projection-design.md`）只把它记为「future composition root」 |
| 处理结论 | 归档，负向守卫中的禁止片段保留为防回流。执行面继续由 Capability / Workflow / Agent.Tools 承载；Authoring/ControlPlane 不是待塞进 runtime 的执行内核。技术责任 Agent。**限制**：本次归档 marker-only 项目，不构成「以后所有零代码聚合壳均违规」的规则；有消费者的静态聚合壳在设计 §5 准入记录下仍合法 |

### D-04 非正式配置管理：CrestCreates.Configuration（已归档）

| 维度 | 记录 |
| --- | --- |
| 项目 | `src/Framework/Infrastructure/CrestCreates.Configuration/CrestCreates.Configuration.csproj`（3 源文件） |
| 定位 | 执行实现（`ConfigurationService` 包装 `IConfiguration`）+ `ConfigurationModule`（`[CrestModule]`） |
| 唯一装配入口 | [ConfigurationModule.cs:15](../../99_RecycleBin/issue-131-capability-map/src/Framework/Infrastructure/CrestCreates.Configuration/Modules/ConfigurationModule.cs) `services.AddScoped<IConfigurationService, ConfigurationService>()` |
| 真实调用点 | 仅本包内部。基线检索：`IConfigurationService` 在项目外无任何 `.cs` 出现；无 ProjectReference 消费者；方案成员 `CrestCreates.slnx:73`、`solutions/CrestCreates.All.slnx:48`、`CrestCreates.Framework.slnx:31`、`CrestCreates.Metadata.slnx:18`、`CrestCreates.Platform.slnx:32`、`CrestCreates.Runtime.slnx:16` |
| 功能状态 | 归档（S3 已执行）。归档前行为事实：[ConfigurationService.cs](../../99_RecycleBin/issue-131-capability-map/src/Framework/Infrastructure/CrestCreates.Configuration/Services/ConfigurationService.cs) 的 `Set<T>` 抛 `NotSupportedException`，`Subscribe<T>` 只把回调存进字典，`NotifySubscribers()` 是空循环体，`Refresh()` 仅 `IConfigurationRoot.Reload()` |
| 执行证据 | 无测试入口；不适用 |
| 处理结论 | 整体归档。启动期配置用 `IConfiguration`/Options，运行时可管理配置用正式 Setting Management（AGENTS「平台能力」）。不得宣称 `Subscribe` 可用，也不得为它另补一套管理存储。技术责任 Infrastructure/Settings |

### D-05 实验业务草稿：CrestCreates.Draft + CrestCreates.Draft.Abstractions（已归档）

| 维度 | 记录 |
| --- | --- |
| 项目 | `src/Metadata/Draft/CrestCreates.Draft/CrestCreates.Draft.csproj`（2 源文件）、`src/Metadata/Draft/CrestCreates.Draft.Abstractions/CrestCreates.Draft.Abstractions.csproj`（4 源文件） |
| 定位 | 实验业务草稿（`IDraftStore` / `DraftRecord` / `DraftQuery` / `DraftStatus` + `InMemoryDraftStore` / `TenantIsolatedDraftStore`） |
| 唯一装配入口 | 无 DI 扩展；`TenantIsolatedDraftStore` 需手工构造 |
| 真实调用点 | `src/Runtime/Workflow/CrestCreates.Workflow/CrestCreates.Workflow.csproj:20` 引用 Draft.Abstractions、`tests/Runtime/Workflow/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj:26` 引用 Draft；两个 C# 项目均未检出 `IDraftStore`/`DraftRecord`/`TenantIsolatedDraftStore` 使用。其余消费者只有专用测试 `tests/Metadata/Draft/CrestCreates.Draft.Tests/`。方案成员：`CrestCreates.slnx:126-127`、`solutions/CrestCreates.All.slnx:99-100`、`CrestCreates.Metadata.slnx`、`CrestCreates.Platform.slnx`、`CrestCreates.Runtime.slnx`，另有 `samples/ProcurementApproval/CrestCreates.Sample.ProcurementApproval.slnx:45` 引用 Draft.Abstractions；CI 步骤 `.github/workflows/ci.yml:180`、`full-validation.yml:116` 运行 `CrestCreates.Draft.Tests` |
| 功能状态 | 归档（S3 已执行）；实验性业务草稿，从未交付 |
| 执行证据 | JIT：`tests/Metadata/Draft/CrestCreates.Draft.Tests/`（`TenantIsolatedDraftStoreTests.cs` 等）存在；[TenantIsolatedDraftStore.cs](../../99_RecycleBin/issue-131-capability-map/src/Metadata/Draft/CrestCreates.Draft/TenantIsolatedDraftStore.cs) 的 `DeleteAsync` 直接透传内层 store，null context 时放行不过滤。其隔离强度**不用于**证明治理主链安全 |
| 处理结论 | 归档两个生产项目 + 专用测试 `CrestCreates.Draft.Tests`，并移除 Workflow/Workflow.Tests 的无效引用与上述方案、CI 输入。它不是 DescriptorDraft 的别名，不自动做 payload 转换。防复活边界（禁止 PG 重新注册旧 `IDraftStore`）保留为守卫，不随实验退出放松。技术责任 Metadata/Draft |

### D-06 未交付事件存储：CrestCreates.EventBus.EventStore（已归档）

| 维度 | 记录 |
| --- | --- |
| 项目 | `src/Runtime/Eventing/CrestCreates.EventBus.EventStore/CrestCreates.EventBus.EventStore.csproj`（4 源文件） |
| 定位 | 实验执行实现 + `EventStoreModule`（`[CrestModule]`），引用 EventBus.Abstract / Domain / Infrastructure |
| 唯一装配入口 | [EventStoreModule.cs:12-13](../../99_RecycleBin/issue-131-capability-map/src/Runtime/Eventing/CrestCreates.EventBus.EventStore/EventStoreModule.cs)：`IEventStoreSerializer → EventStoreJsonSerializer`、`IEventRetryStore → InMemoryEventRetryStore` 单例注册 |
| 真实调用点 | 无 ProjectReference 消费者、无测试；方案成员 `CrestCreates.slnx:180`、`solutions/CrestCreates.All.slnx:151`、`CrestCreates.Platform.slnx:134`、`CrestCreates.Runtime.slnx:68`。契约文件 `src/Runtime/Eventing/CrestCreates.EventBus.Abstract/IEventStore.cs`（`IEventStore`/`IEventStoreSerializer`/`IEventRetryStore`）在本包之外仅被本包实现，`IEventStore` 无任何实现或消费 |
| 功能状态 | 归档（S3 已执行）；归档前状态为未交付 |
| 执行证据 | 无测试入口。[EventRetryService.cs](../../99_RecycleBin/issue-131-capability-map/src/Runtime/Eventing/CrestCreates.EventBus.EventStore/EventRetryService.cs) 用 `Guid.NewGuid()` 新建再删除记录，不表达真实重试结算；序列化器为反射 JSON；重试存储为内存 |
| 处理结论 | 归档实现，并把无消费者的 `IEventStore.cs` 契约文件一并归档（保留 EventBus.Abstract 内其余在用契约，不按目录整包搬走）。事件投递与重放继续走正式 LocalEvent / RabbitMQ / Outbox 主链；它们不是 event sourcing 迁移目标，不宣称具备该能力。技术责任 Eventing |

### D-07 治理草稿主链：DescriptorDraft + Abstractions（保留）

| 维度 | 记录 |
| --- | --- |
| 项目 | `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/…csproj`（23 源文件）、`src/Metadata/Draft/CrestCreates.DescriptorDraft/…csproj`（16 源文件） |
| 定位 | 抽象契约（Abstractions）+ 执行实现（validator / materializer / review / hash / InMemory store） |
| 唯一装配入口 | [DescriptorDraftServiceCollectionExtensions.cs:11](../../src/Metadata/Draft/CrestCreates.DescriptorDraft/DescriptorDraftServiceCollectionExtensions.cs#L11) `AddDescriptorDrafts()`（`TryAddSingleton` 五契约）；PG store 由 `src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql` 贡献 |
| 真实调用点 | `AddDescriptorDrafts()` 实际调用：[CompanyCertificationGoldenScenarioHost.cs:223](../../samples/CrestCreates.Samples.DescriptorControlPlane/CompanyCertificationGoldenScenarioHost.cs#L223)、`samples/AssetManagement/tests/…/AssetControlPlaneApprovalHarness.cs:125`、`tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/ProjectionScopeNativeAotFixture.cs:52`、`tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AssetHumanApprovalActivationHandoffAcceptanceTests.cs:539`。项目消费者：Agent.Authoring、Agent.ControlPlane、Agent.DraftContracts、PG Provider/CrashWorker/Tests、两个样例解决方案 |
| 功能状态 | 已交付指定边界（Authoring → deterministic review → Control Plane 的草稿契约与内存 store；PG store 单列） |
| 执行证据 | JIT：`tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/`（validator / materializer / review / InMemory store / CanonicalHashing / Persistence）。原生：#123 矩阵 §2.1「Control Plane reference data / versioned org cache」行，`CRESTCREATES_DURABLE_CONTROL_PLANE_REFERENCE_DATA_OK` 等 sentinel，linux-x64。**边界**：原生覆盖只认所列场景；完整 authoring → approval → durable activation 不由此推导；草稿不是批准，review/package 持久化不构成 durable activation（#88 已判为应用侧范围，不重开） |
| 处理结论 | 保留正式治理主链。技术责任 Metadata/Draft + Agent 治理面 |

### D-08 基础服务：CrestCreates.Localization（保留）

| 维度 | 记录 |
| --- | --- |
| 项目 | `src/Framework/Infrastructure/CrestCreates.Localization/CrestCreates.Localization.csproj`（6 源文件） |
| 定位 | 基础服务 + contributor（`ILocalizationService`、资源 contributor、culture scope） |
| 唯一装配入口 | [LocalizationModule.cs:14](../../src/Framework/Infrastructure/CrestCreates.Localization/Modules/LocalizationModule.cs#L14)（`[CrestModule]`） |
| 真实调用点 | [DefaultCrestExceptionConverter.cs:88](../../src/Framework/Web/CrestCreates.AspNetCore/Errors/DefaultCrestExceptionConverter.cs#L88) 与 `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewMessageTemplateCatalog.cs` 消费 `ILocalizationService`。项目消费者：`CrestCreates.AspNetCore`、`CrestCreates.Web.Tests`、`Agent.ControlPlane.Tests`、`Localization.Tests`（`CrestCreates.Validation` 仅有项目引用，不计作行为证据） |
| 功能状态 | 已交付指定边界（资源 / contributor / culture scope） |
| 执行证据 | JIT：`tests/Framework/Infrastructure/CrestCreates.Localization.Tests/LocalizationServiceTests.cs`、`tests/Framework/Web/CrestCreates.Web.Tests/` 异常呈现用例。整包 trimming-verified / NativeAOT-verified **未验证**；模块自动 Web 初始化路径未在本项单独验证 |
| 处理结论 | 保留。不因文件少而搬走；不扩为新的 Setting 来源或第二租户来源。技术责任 Infrastructure/Localization |

### D-09 HTTP 文档适配：CrestCreates.OpenApi（保留）

| 维度 | 记录 |
| --- | --- |
| 项目 | `src/Framework/Api/CrestCreates.OpenApi/CrestCreates.OpenApi.csproj`（8 源文件） |
| 定位 | 执行实现（OpenAPI options + document transformers 适配） |
| 唯一装配入口 | [CrestOpenApiExtensions.cs:15](../../src/Framework/Api/CrestCreates.OpenApi/CrestOpenApiExtensions.cs#L15) `AddCrestOpenApi()` / `:68` `MapCrestOpenApi()` |
| 真实调用点 | [CrestCreatesWebApplicationExtensions.cs:99](../../src/Platform/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs#L99)、`:205`；`samples/LibraryManagement/LibraryManagement.Web/Program.cs:53,109`、`samples/AssetManagement/src/…/Program.cs:123,208`、`samples/ProcurementApproval/src/…/Program.cs:88,194`；消费者还含 SaaSHelpdesk.Web/EntityFrameworkCore |
| 功能状态 | 已交付指定边界（正式 HTTP 文档装配） |
| 执行证据 | JIT：`tests/Framework/Web/CrestCreates.Web.Tests/OpenApi/CrestOpenApiOptionsTests.cs`。**边界**：该测试只覆盖 options/transformer 注册，不替代文档 endpoint 的完整集成结果；CapabilityEndpoint 与 Ticket 的 native HTTP fixture **不证明** OpenAPI 文档生成或 Scalar UI 已原生验证，故本项 NativeAOT-verified = 未验证 |
| 处理结论 | 保留。新的文档投影必须消费正式 capability metadata，禁止重新激活 Controller/反射 scanner（#129 已退出）。技术责任 Framework/Api |

### D-10 实验文件模块：CrestCreates.FileManagement（限界保留）

| 维度 | 记录 |
| --- | --- |
| 项目 | `src/Framework/Modules/CrestCreates.FileManagement/CrestCreates.FileManagement.csproj`（26 源文件） |
| 定位 | 执行服务 + 存储 Provider（Local / AzureBlob / S3 / `InMemoryFileRepository`） |
| 唯一装配入口 | [FileManagementModule.cs:16](../../src/Framework/Modules/CrestCreates.FileManagement/Modules/FileManagementModule.cs#L16)（`[CrestModule]`，构造入参 `IConfiguration`） |
| 真实调用点 | 仅 `tests/Framework/Modules/CrestCreates.FileManagement.Tests/`。基线检索未检出任何 Platform 或样例 Host 装配 `FileManagementModule`，也无其他项目引用 |
| 功能状态 | experimental（本地文件 + 内存元数据实验模块）。**不是**已交付的持久文件平台 |
| 执行证据 | JIT：`LocalFileSystemProviderTests.UploadAndDownload_RoundTrip_PreservesContent` 为真实本地文件 round-trip；`FileManagementServiceTests` 用内存元数据，其 tenant 参数检查不证明真实身份授权已接通。`AzureBlobStorageProvider` / `S3StorageProvider` 只记「实现存在，外部执行未验证」。整模块 trimming-verified / NativeAOT-verified **未验证** |
| 处理结论 | 限界保留。技术责任 Framework/Modules/FileManagement，状态由 #131 实施 PR 维护。升级条件：真实 Host 装配 + 持久元数据 store + 租户鉴权接通 + 云 Provider 成功/失败证据；未达前禁止提升宣传状态。若选择退出，按设计 §5 归档流程处理，不转为主链 fallback |

### D-11 组织与数据权限内核：Organization + Abstractions（保留）

| 维度 | 记录 |
| --- | --- |
| 项目 | `src/Framework/Modules/CrestCreates.Organization.Abstractions/…csproj`（33 源文件）、`src/Framework/Modules/CrestCreates.Organization/…csproj`（19 源文件） |
| 定位 | 抽象契约（组织树 / 身份投影 / 数据权限）+ 执行实现（默认 `InMemoryOrganizationStore`、`NullOrganizationContextAccessor`） |
| 唯一装配入口 | [OrganizationServiceCollectionExtensions.cs:9](../../src/Framework/Modules/CrestCreates.Organization/OrganizationServiceCollectionExtensions.cs#L9) `AddOrganizationKernel()`；PG Provider 另行覆盖 store |
| 真实调用点 | `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/VersionedCacheConsistencyArchitectureTests.cs:65,118,133`、[AotHost Program.cs:674](../../tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/Program.cs#L674)、`:1185`、`tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/PostgreSqlOrganizationHierarchyCacheTests.cs:42`、`PostgreSqlControlPlaneReferenceDataRestartTests.cs:107,128`；项目消费者含 Organization.Tests、PG AotHost/CrashWorker/Tests、ReferenceData.Persistence.Testing |
| 功能状态 | 已交付指定边界（平台基础契约 + 内核实现） |
| 执行证据 | JIT：`tests/Framework/Modules/CrestCreates.Organization.Tests/`。原生：#123 矩阵 §2.1「Control Plane reference data / versioned org cache」行，`RunVersionedOrganizationCacheAotScenarioAsync`（[Program.cs:1053](../../tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/Program.cs#L1053)）与 `CRESTCREATES_VERSIONED_ORGANIZATION_CACHE_OK`、`CRESTCREATES_DURABLE_REFERENCE_ORGANIZATION_OK`，linux-x64。**边界**：只沿用所列组合范围；默认内存 store 与 `NullOrganizationContextAccessor` 语义明确列示——不宣传默认持久化，也不宣传自动获得授权上下文；不推导通用组织管理 UI 或其他 ORM Provider |
| 处理结论 | 保留平台基础契约。技术责任 Framework/Modules/Organization + PG reference data。不得因文件数少而搬走 |

### D-12 动态插件实验：CrestCreates.PluginSystem（限界保留）

| 维度 | 记录 |
| --- | --- |
| 项目 | `src/Integrations/CrestCreates.PluginSystem/CrestCreates.PluginSystem.csproj`（6 源文件） |
| 定位 | 实验实现（`PluginManager` 用 `AssemblyLoadContext`、`Assembly.GetTypes`、`ActivatorUtilities` 动态装载 DLL） |
| 唯一装配入口 | [PluginSystemModule.cs:9](../../src/Integrations/CrestCreates.PluginSystem/Modules/PluginSystemModule.cs#L9)（`[CrestModule]`） |
| 真实调用点 | 基线检索：无 ProjectReference 消费者，无专用测试；[PluginManager.cs](../../src/Integrations/CrestCreates.PluginSystem/Services/PluginManager.cs) 的 `DiscoverPlugins` / `LoadPlugin` / `InitializePlugin` 未核实到消费 Host。方案成员 `CrestCreates.slnx`、`solutions/CrestCreates.All.slnx`、`solutions/CrestCreates.Integrations.slnx`；另有历史规划 `docs/review/feature-plans/plugin-system.xml` |
| 功能状态 | experimental，独立 opt-in 项目。功能执行未验证 |
| 执行证据 | 动态装载执行面 **JIT-only**（明确依赖运行时动态装载的机制才可用该标签），且当前无 JIT 用例结果；`PluginManifestJsonContext` 有 STJ context **不证明** 装载路径 AOT 可用，故 NativeAOT-verified = 不适用/未验证 |
| 处理结论 | 限界保留，由 #131 实施 PR 落实实验说明与隔离守卫（见 [PluginSystemIsolationArchitectureTests](../../tests/Boundary/CrestCreates.DependencyBoundaries.Tests/PluginSystemIsolationArchitectureTests.cs)）。撤销 `docs/review/crestcreates-abp-comparison.md` 的「生产就绪」表述。下一次功能扩展前必须具备真实消费 Host 与装载/初始化/失败/释放证据，否则不得扩展；选择退出则按设计 §5 归档流程。技术责任 Integrations/PluginSystem |

## 3. 归档与准入

归档操作、顺序与验证要求见设计 §5；S3 已把 7 个生产项目、通用 Draft 专用测试与无消费者的 `IEventStore.cs` 移至 `99_RecycleBin/issue-131-capability-map/`，保留原相对路径与 README，并在 [ArchivedProjectRegressionArchitectureTests](../../tests/Boundary/CrestCreates.DependencyBoundaries.Tests/ArchivedProjectRegressionArchitectureTests.cs) 建立禁止回流守卫。

S3 之后，新增生产 csproj 必须同时在下面的项目索引中登记并附准入记录（由 `CapabilityMapCoverageArchitectureTests` 强制）：为什么既有项目不能承载、层次与依赖边界、消费者或静态聚合目的、唯一声明与装配路径、验证方式、支持级别、维护责任、未交付时的退出条件。缺记录即门禁失败。

## 4. 项目索引（active `src/**/*.csproj` 全量）

固定索引区，由 `CapabilityMapCoverageArchitectureTests` 读取校验：集合与 `src/**/*.csproj` 一致、路径真实、无重复、无漏项。D-xx 行是本轮逐项裁定；「导航」行只建立路径与既有文档链接，**未在本轮重评**，其支持声明不由本地图背书。`tests/**`、`samples/**` 与 fixture/tool 项目按其作用在 §5 单列，不要求具备业务消费者。

<!-- capability-map:project-index:start -->
| Project path | Layer | Record | Existing navigation |
| --- | --- | --- | --- |
| src/Core/CrestCreates.Core.Abstractions/CrestCreates.Core.Abstractions.csproj | Core | 导航（本轮未重评，不背书） | — |
| src/Core/CrestCreates.Core/CrestCreates.Core.csproj | Core | 导航（本轮未重评，不背书） | — |
| src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CrestCreates.DynamicApi.Abstractions.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Api/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Api/CrestCreates.OpenApi/CrestCreates.OpenApi.csproj | Framework | D-09 保留 | — |
| src/Framework/Ddd/CrestCreates.Application.Contracts/CrestCreates.Application.Contracts.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Ddd/CrestCreates.Application/CrestCreates.Application.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Ddd/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Ddd/CrestCreates.Domain/CrestCreates.Domain.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.Aop.Abstractions/CrestCreates.Aop.Abstractions.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.Aop/CrestCreates.Aop.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.Authorization.Abstractions/CrestCreates.Authorization.Abstractions.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.Authorization/CrestCreates.Authorization.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.Caching.Abstractions/CrestCreates.Caching.Abstractions.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.Caching/CrestCreates.Caching.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.DataFilter/CrestCreates.DataFilter.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.Infrastructure/CrestCreates.Infrastructure.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.Localization/CrestCreates.Localization.csproj | Framework | D-08 保留 | — |
| src/Framework/Infrastructure/CrestCreates.Logging/CrestCreates.Logging.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.MultiTenancy.Abstract/CrestCreates.MultiTenancy.Abstract.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.MultiTenancy/CrestCreates.MultiTenancy.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.Security.Abstractions/CrestCreates.Security.Abstractions.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.Security/CrestCreates.Security.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.Validation/CrestCreates.Validation.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Infrastructure/CrestCreates.VirtualFileSystem/CrestCreates.VirtualFileSystem.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Modularity/CrestCreates.Modularity/CrestCreates.Modularity.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Modules/CrestCreates.FileManagement/CrestCreates.FileManagement.csproj | Framework | D-10 限界保留 | — |
| src/Framework/Modules/CrestCreates.Form.Abstractions/CrestCreates.Form.Abstractions.csproj | Framework | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/Form/arch-design.md) |
| src/Framework/Modules/CrestCreates.Form/CrestCreates.Form.csproj | Framework | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/Form/arch-design.md) |
| src/Framework/Modules/CrestCreates.ModuleDiagnostics/CrestCreates.ModuleDiagnostics.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Modules/CrestCreates.Organization.Abstractions/CrestCreates.Organization.Abstractions.csproj | Framework | D-11 保留 | [arch-design.md](../../docs/Feature/Organization/arch-design.md) |
| src/Framework/Modules/CrestCreates.Organization/CrestCreates.Organization.csproj | Framework | D-11 保留 | [arch-design.md](../../docs/Feature/Organization/arch-design.md) |
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/CrestCreates.Scheduling.Abstractions.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Modules/CrestCreates.Scheduling.Quartz/CrestCreates.Scheduling.Quartz.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Testing/CrestCreates.Testing/CrestCreates.Testing.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Web/CrestCreates.AspNetCore.Authentication.OpenIddict/CrestCreates.AspNetCore.Authentication.OpenIddict.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Web/CrestCreates.AspNetCore/CrestCreates.AspNetCore.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Web/CrestCreates.HealthCheck.AspNetCore/CrestCreates.HealthCheck.AspNetCore.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Web/CrestCreates.HealthCheck.Mvc/CrestCreates.HealthCheck.Mvc.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Framework/Web/CrestCreates.HealthCheck/CrestCreates.HealthCheck.csproj | Framework | 导航（本轮未重评，不背书） | — |
| src/Integrations/CrestCreates.Integration.Abstractions/CrestCreates.Integration.Abstractions.csproj | Integrations | 导航（本轮未重评，不背书） | — |
| src/Integrations/CrestCreates.Integration.Cap.EfCore/CrestCreates.Integration.Cap.EfCore.csproj | Integrations | 导航（本轮未重评，不背书） | — |
| src/Integrations/CrestCreates.Mcp.Abstractions/CrestCreates.Mcp.Abstractions.csproj | Integrations | 导航（本轮未重评，不背书） | [arc-design.md](../../docs/Feature/MCP/arc-design.md) |
| src/Integrations/CrestCreates.Mcp.Memory/CrestCreates.Mcp.Memory.csproj | Integrations | 导航（本轮未重评，不背书） | [arc-design.md](../../docs/Feature/MCP/arc-design.md) |
| src/Integrations/CrestCreates.Mcp/CrestCreates.Mcp.csproj | Integrations | 导航（本轮未重评，不背书） | [arc-design.md](../../docs/Feature/MCP/arc-design.md) |
| src/Integrations/CrestCreates.PluginSystem/CrestCreates.PluginSystem.csproj | Integrations | D-12 限界保留 | — |
| src/Metadata/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj | Metadata | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/UnifiedMetadataModel/arch-design.md) |
| src/Metadata/CrestCreates.Metadata.AgentTool.Abstractions/CrestCreates.Metadata.AgentTool.Abstractions.csproj | Metadata | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/UnifiedMetadataModel/arch-design.md) |
| src/Metadata/CrestCreates.Metadata.ContextPack.Abstractions/CrestCreates.Metadata.ContextPack.Abstractions.csproj | Metadata | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/UnifiedMetadataModel/arch-design.md) |
| src/Metadata/CrestCreates.Metadata.ContextPack/CrestCreates.Metadata.ContextPack.csproj | Metadata | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/UnifiedMetadataModel/arch-design.md) |
| src/Metadata/CrestCreates.Metadata.Mcp.Abstractions/CrestCreates.Metadata.Mcp.Abstractions.csproj | Metadata | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/UnifiedMetadataModel/arch-design.md) |
| src/Metadata/CrestCreates.Metadata/CrestCreates.Metadata.csproj | Metadata | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/UnifiedMetadataModel/arch-design.md) |
| src/Metadata/CrestCreates.Schema.Abstractions/CrestCreates.Schema.Abstractions.csproj | Metadata | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/UnifiedMetadataModel/arch-design.md) |
| src/Metadata/CrestCreates.Schema/CrestCreates.Schema.csproj | Metadata | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/UnifiedMetadataModel/arch-design.md) |
| src/Metadata/CrestCreates.Snapshot.Abstractions/CrestCreates.Snapshot.Abstractions.csproj | Metadata | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/UnifiedMetadataModel/arch-design.md) |
| src/Metadata/CrestCreates.Snapshot/CrestCreates.Snapshot.csproj | Metadata | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/UnifiedMetadataModel/arch-design.md) |
| src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CrestCreates.DescriptorDraft.Abstractions.csproj | Metadata | D-07 保留 | [arch-design.md](../../docs/Feature/Descriptor/arch-design.md) |
| src/Metadata/Draft/CrestCreates.DescriptorDraft/CrestCreates.DescriptorDraft.csproj | Metadata | D-07 保留 | [arch-design.md](../../docs/Feature/Descriptor/arch-design.md) |
| src/Persistence/CrestCreates.Data.Abstractions/CrestCreates.Data.Abstractions.csproj | Persistence | 导航（本轮未重评，不背书） | [OrmProviderSupportMatrix.md](../../docs/Feature/OrmProviderSupportMatrix.md) |
| src/Persistence/CrestCreates.Data.Core/CrestCreates.Data.Core.csproj | Persistence | 导航（本轮未重评，不背书） | [OrmProviderSupportMatrix.md](../../docs/Feature/OrmProviderSupportMatrix.md) |
| src/Persistence/CrestCreates.Data.EFCore.MySql/CrestCreates.Data.EFCore.MySql.csproj | Persistence | 导航（本轮未重评，不背书） | [OrmProviderSupportMatrix.md](../../docs/Feature/OrmProviderSupportMatrix.md) |
| src/Persistence/CrestCreates.Data.EFCore.PostgreSql/CrestCreates.Data.EFCore.PostgreSql.csproj | Persistence | 导航（本轮未重评，不背书） | [OrmProviderSupportMatrix.md](../../docs/Feature/OrmProviderSupportMatrix.md) |
| src/Persistence/CrestCreates.Data.EFCore.SqlServer/CrestCreates.Data.EFCore.SqlServer.csproj | Persistence | 导航（本轮未重评，不背书） | [OrmProviderSupportMatrix.md](../../docs/Feature/OrmProviderSupportMatrix.md) |
| src/Persistence/CrestCreates.Data.EFCore/CrestCreates.Data.EFCore.csproj | Persistence | 导航（本轮未重评，不背书） | [OrmProviderSupportMatrix.md](../../docs/Feature/OrmProviderSupportMatrix.md) |
| src/Persistence/CrestCreates.Data.FreeSql/CrestCreates.Data.FreeSql.csproj | Persistence | 导航（本轮未重评，不背书） | [OrmProviderSupportMatrix.md](../../docs/Feature/OrmProviderSupportMatrix.md) |
| src/Persistence/CrestCreates.Data.SqlSugar/CrestCreates.Data.SqlSugar.csproj | Persistence | 导航（本轮未重评，不背书） | [OrmProviderSupportMatrix.md](../../docs/Feature/OrmProviderSupportMatrix.md) |
| src/Persistence/CrestCreates.DbContextProvider.Abstract/CrestCreates.DbContextProvider.Abstract.csproj | Persistence | 导航（本轮未重评，不背书） | [OrmProviderSupportMatrix.md](../../docs/Feature/OrmProviderSupportMatrix.md) |
| src/Persistence/CrestCreates.OrmProviders.MongoDB/CrestCreates.OrmProviders.MongoDB.csproj | Persistence | 导航（本轮未重评，不背书） | [OrmProviderSupportMatrix.md](../../docs/Feature/OrmProviderSupportMatrix.md) |
| src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/CrestCreates.Runtime.Persistence.PostgreSql.csproj | Persistence | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/RuntimePersistence/arch-design.md) |
| src/Platform/CrestCreates.Platform.Agent/CrestCreates.Platform.Agent.csproj | Platform | 导航（本轮未重评，不背书） | — |
| src/Platform/CrestCreates.Platform.All/CrestCreates.Platform.All.csproj | Platform | 导航（本轮未重评，不背书） | — |
| src/Platform/CrestCreates.Platform.AspNetCore/CrestCreates.Platform.AspNetCore.csproj | Platform | 导航（本轮未重评，不背书） | — |
| src/Platform/CrestCreates.Platform.Runtime/CrestCreates.Platform.Runtime.csproj | Platform | 导航（本轮未重评，不背书） | — |
| src/Platform/CrestCreates.Platform/CrestCreates.Platform.csproj | Platform | 导航（本轮未重评，不背书） | — |
| src/Platform/CrestCreates.Web/CrestCreates.Web.csproj | Platform | 导航（本轮未重评，不背书） | — |
| src/Runtime/Agent/CrestCreates.Agent.Abstractions/CrestCreates.Agent.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions/CrestCreates.Agent.Authoring.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/AgentControlPlane/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.Authoring.Http/CrestCreates.Agent.Authoring.Http.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/AgentControlPlane/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.Authoring/CrestCreates.Agent.Authoring.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/AgentControlPlane/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/CrestCreates.Agent.ControlPlane.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/AgentControlPlane/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.ControlPlane/CrestCreates.Agent.ControlPlane.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/AgentControlPlane/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.DraftContracts/CrestCreates.Agent.DraftContracts.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/Descriptor/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/AgentMemory/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.Memory.Accountability/CrestCreates.Agent.Memory.Accountability.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/AgentMemory/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.Memory.Llm/CrestCreates.Agent.Memory.Llm.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/AgentMemory/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Abstractions/CrestCreates.Agent.Memory.Projection.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/AgentMemory/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.Memory.Projection/CrestCreates.Agent.Memory.Projection.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/AgentMemory/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore/CrestCreates.Agent.Memory.ReadCore.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/AgentMemory/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Abstractions/CrestCreates.Agent.Memory.Tools.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | [AgentMemoryTools.md](../../docs/Feature/AgentMemoryTools.md) |
| src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/CrestCreates.Agent.Memory.Tools.csproj | Runtime | 导航（本轮未重评，不背书） | [AgentMemoryTools.md](../../docs/Feature/AgentMemoryTools.md) |
| src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/AgentMemory/arch-design.md) |
| src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions/CrestCreates.Agent.Prompting.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Agent/CrestCreates.Agent.Prompting/CrestCreates.Agent.Prompting.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Agent/CrestCreates.Agent.Tools.Abstractions/CrestCreates.Agent.Tools.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | [usage-guide.md](../../docs/Feature/AgentTools/usage-guide.md) |
| src/Runtime/Agent/CrestCreates.Agent.Tools/CrestCreates.Agent.Tools.csproj | Runtime | 导航（本轮未重评，不背书） | [usage-guide.md](../../docs/Feature/AgentTools/usage-guide.md) |
| src/Runtime/Audit/CrestCreates.Accountability.Abstractions/CrestCreates.Accountability.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/Accountability/arch-design.md) |
| src/Runtime/Audit/CrestCreates.Accountability/CrestCreates.Accountability.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/Accountability/arch-design.md) |
| src/Runtime/Audit/CrestCreates.AuditLogging.Abstractions/CrestCreates.AuditLogging.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Audit/CrestCreates.AuditLogging/CrestCreates.AuditLogging.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Capability/CrestCreates.Capability.Abstractions/CrestCreates.Capability.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/CapabilityExposure/arch-design.md) |
| src/Runtime/Capability/CrestCreates.Capability/CrestCreates.Capability.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/CapabilityExposure/arch-design.md) |
| src/Runtime/DistributedTransaction/CrestCreates.DistributedTransaction.CAP/CrestCreates.DistributedTransaction.CAP.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/DistributedTransaction/CrestCreates.DistributedTransaction/CrestCreates.DistributedTransaction.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.Event.Abstractions/CrestCreates.Event.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.Event/CrestCreates.Event.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.Abstract/CrestCreates.EventBus.Abstract.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.Abstractions/CrestCreates.EventBus.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.DeadLetter.EFCore/CrestCreates.EventBus.DeadLetter.EFCore.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.Kafka/CrestCreates.EventBus.Kafka.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.Local.Channel/CrestCreates.EventBus.Local.Channel.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.Local/CrestCreates.EventBus.Local.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.MediatorAdapter/CrestCreates.EventBus.MediatorAdapter.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.Runtime.Delivery.Abstractions/CrestCreates.Runtime.Delivery.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.Runtime.Delivery/CrestCreates.Runtime.Delivery.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/HumanTask/CrestCreates.HumanTask/CrestCreates.HumanTask.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Persistence/CrestCreates.Runtime.Persistence.Abstractions/CrestCreates.Runtime.Persistence.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/RuntimePersistence/arch-design.md) |
| src/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory/CrestCreates.Runtime.Persistence.InMemory.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/RuntimePersistence/arch-design.md) |
| src/Runtime/Persistence/CrestCreates.Runtime.Persistence/CrestCreates.Runtime.Persistence.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/RuntimePersistence/arch-design.md) |
| src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Workflow/CrestCreates.Workflow/CrestCreates.Workflow.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Tooling/CrestCreates.BuildTasks/CrestCreates.BuildTasks.csproj | Tooling | 导航（本轮未重评，不背书） | — |
| src/Tooling/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj | Tooling | 导航（本轮未重评，不背书） | — |
| src/Tooling/CrestCreates.JsonContracts.BuildTasks.Core/CrestCreates.JsonContracts.BuildTasks.Core.csproj | Tooling | 导航（本轮未重评，不背书） | — |
| src/Tooling/CrestCreates.JsonContracts.BuildTasks/CrestCreates.JsonContracts.BuildTasks.csproj | Tooling | 导航（本轮未重评，不背书） | — |
| src/Tooling/CrestCreates.JsonContracts.Tool/CrestCreates.JsonContracts.Tool.csproj | Tooling | 导航（本轮未重评，不背书） | — |
| src/Tooling/CrestCreates.Metadata.Analyzers/CrestCreates.Metadata.Analyzers.csproj | Tooling | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/CanonicalHash/arch-design.md) |
<!-- capability-map:project-index:end -->

### 4.1 归档项目索引

`99_RecycleBin/issue-131-capability-map/` 下的项目不再是 active `src/**/*.csproj`，由 `ArchivedProjectRegressionArchitectureTests` 校验其落点真实、且不进入任何 Compile/ProjectReference/pack/方案输入。

<!-- capability-map:archived-index:start -->
| Archived project | Recycle-bin path | Record |
| --- | --- | --- |
| src/Framework/Infrastructure/CrestCreates.Configuration/CrestCreates.Configuration.csproj | [src/Framework/Infrastructure/CrestCreates.Configuration/CrestCreates.Configuration.csproj](../../99_RecycleBin/issue-131-capability-map/src/Framework/Infrastructure/CrestCreates.Configuration/CrestCreates.Configuration.csproj) | D-04 |
| src/Integrations/CrestCreates.Integration.ExternalApi/CrestCreates.Integration.ExternalApi.csproj | [src/Integrations/CrestCreates.Integration.ExternalApi/CrestCreates.Integration.ExternalApi.csproj](../../99_RecycleBin/issue-131-capability-map/src/Integrations/CrestCreates.Integration.ExternalApi/CrestCreates.Integration.ExternalApi.csproj) | D-01 |
| src/Integrations/CrestCreates.Integration.LegacyDatabase/CrestCreates.Integration.LegacyDatabase.csproj | [src/Integrations/CrestCreates.Integration.LegacyDatabase/CrestCreates.Integration.LegacyDatabase.csproj](../../99_RecycleBin/issue-131-capability-map/src/Integrations/CrestCreates.Integration.LegacyDatabase/CrestCreates.Integration.LegacyDatabase.csproj) | D-02 |
| src/Metadata/Draft/CrestCreates.Draft.Abstractions/CrestCreates.Draft.Abstractions.csproj | [src/Metadata/Draft/CrestCreates.Draft.Abstractions/CrestCreates.Draft.Abstractions.csproj](../../99_RecycleBin/issue-131-capability-map/src/Metadata/Draft/CrestCreates.Draft.Abstractions/CrestCreates.Draft.Abstractions.csproj) | D-05 |
| src/Metadata/Draft/CrestCreates.Draft/CrestCreates.Draft.csproj | [src/Metadata/Draft/CrestCreates.Draft/CrestCreates.Draft.csproj](../../99_RecycleBin/issue-131-capability-map/src/Metadata/Draft/CrestCreates.Draft/CrestCreates.Draft.csproj) | D-05 |
| src/Runtime/Agent/CrestCreates.Agent.Runtime/CrestCreates.Agent.Runtime.csproj | [src/Runtime/Agent/CrestCreates.Agent.Runtime/CrestCreates.Agent.Runtime.csproj](../../99_RecycleBin/issue-131-capability-map/src/Runtime/Agent/CrestCreates.Agent.Runtime/CrestCreates.Agent.Runtime.csproj) | D-03 |
| src/Runtime/Eventing/CrestCreates.EventBus.EventStore/CrestCreates.EventBus.EventStore.csproj | [src/Runtime/Eventing/CrestCreates.EventBus.EventStore/CrestCreates.EventBus.EventStore.csproj](../../99_RecycleBin/issue-131-capability-map/src/Runtime/Eventing/CrestCreates.EventBus.EventStore/CrestCreates.EventBus.EventStore.csproj) | D-06 |
| tests/Metadata/Draft/CrestCreates.Draft.Tests/CrestCreates.Draft.Tests.csproj | [tests/Metadata/Draft/CrestCreates.Draft.Tests/CrestCreates.Draft.Tests.csproj](../../99_RecycleBin/issue-131-capability-map/tests/Metadata/Draft/CrestCreates.Draft.Tests/CrestCreates.Draft.Tests.csproj) | D-05 |
<!-- capability-map:archived-index:end -->

## 5. 非 src 项目的作用说明

- `tests/**`：单元、集成与 boundary 测试项目；`Directory.Build.targets` 关闭 Trim/AoT（Moq/DynamicProxy 不兼容），因此其绿色不代表原生证据。
- `tests/**/…AotFixture*`、`…AotHost*`：NativeAOT publish/link/run 门禁宿主，逐项场景与 RID 见 [#123 证据矩阵](../review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md)。
- `samples/**`：LibraryManagement、SaaSHelpdesk、ProcurementApproval、AssetManagement、DescriptorControlPlane，作为正式链消费与 golden scenario 入口；样例项目集合变化不改变框架支持声明。
- `src/Tooling/**`：CodeGenerator（netstandard2.0 analyzer）、BuildTasks、JsonContracts 工具链、Metadata.Analyzers，属构建期 Tooling，不是运行时组件。

## 6. 与既有文档的关系

- ORM Provider 支持矩阵唯一来源：[docs/Feature/OrmProviderSupportMatrix.md](OrmProviderSupportMatrix.md)（本地图不复制其表）。
- 原生执行证据矩阵：`docs/review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md`。
- 平台闭环状态：`memory.md`；主链共识与支持声明规则：`AGENTS.md`。
- `docs/review/crestcreates-abp-comparison.md` 中 PluginSystem「生产就绪」为失实的活跃引用，已按本地图 D-12 纠正；历史审查文档保持历史身份并链接本地图。
