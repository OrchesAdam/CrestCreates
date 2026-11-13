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
| 归档前装配入口（不可用于 active Host） | [EventStoreModule.cs:12-13](../../99_RecycleBin/issue-131-capability-map/src/Runtime/Eventing/CrestCreates.EventBus.EventStore/EventStoreModule.cs)：`IEventStoreSerializer → EventStoreJsonSerializer`、`IEventRetryStore → InMemoryEventRetryStore` 单例注册 |
| 归档前调用点（以下方案成员均已移除） | 无 ProjectReference 消费者、无测试；原方案成员 `CrestCreates.slnx:180`、`solutions/CrestCreates.All.slnx:151`、`CrestCreates.Platform.slnx:134`、`CrestCreates.Runtime.slnx:68`。契约文件 `src/Runtime/Eventing/CrestCreates.EventBus.Abstract/IEventStore.cs`（`IEventStore`/`IEventStoreSerializer`/`IEventRetryStore`）在本包之外仅被本包实现，`IEventStore` 无任何实现或消费 |
| 功能状态 | 归档（S3 已执行）；归档前状态为未交付 |
| 执行证据 | 归档实现无运行测试入口。[EventRetryService.cs](../../99_RecycleBin/issue-131-capability-map/src/Runtime/Eventing/CrestCreates.EventBus.EventStore/EventRetryService.cs) 用 `Guid.NewGuid()` 新建再删除记录，不表达真实重试结算；序列化器为反射 JSON；重试存储为内存 |
| 处理结论 | 归档实现，并把无消费者的 `IEventStore.cs` 契约文件一并归档（保留 EventBus.Abstract 内其余在用契约，不按目录整包搬走）。事件投递与重放继续走正式 LocalEvent / RabbitMQ / Outbox 主链；它们不是 event sourcing 迁移目标，不宣称具备该能力。技术责任 Eventing |
| 退出守卫（R3-17） | [ArchivedProjectRegressionArchitectureTests](../../tests/Boundary/CrestCreates.DependencyBoundaries.Tests/ArchivedProjectRegressionArchitectureTests.cs) 与 #132 共用每项目一次 Debug MSBuild 求值及语法遍历：精确禁止七个旧类型、旧命名空间、项目/包身份、原 active 路径与实际归档路径重新进入 Compile/资源/有效 NuGet 源码分发输入。变异涵盖真实归档接口改名、非 `.cs` Compile、alias、typed 模块注册、导入与属性展开引用、有效 Pack/BuildAction/PackagePath。注释、历史字符串、移除输入、Pack=false 和相似在用接口为阴性对照。现有 generated module manifests 另行盘点无旧模块/契约名，不将已生成产物存在性作为干净检出的测试前提。仅仓库有限模式退出守卫，不代表全配置/全 TFM、任意 target 生成输入或 EventStore 功能执行证据；执行结果见 `memory.md` R3-17。 |

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
| src/Framework/Modules/CrestCreates.Scheduling.Abstractions/CrestCreates.Scheduling.Abstractions.csproj | Framework | typed 调度契约；R3-19 指定边界见 §10 | R3-19 |
| src/Framework/Modules/CrestCreates.Scheduling.Quartz/CrestCreates.Scheduling.Quartz.csproj | Framework | Quartz RAM 执行实现，JIT-only；见 §10 | R3-19 |
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
| src/Persistence/CrestCreates.OrmProviders.MongoDB/CrestCreates.OrmProviders.MongoDB.csproj | Persistence | 限界保留；R3-08 仓储语义与执行边界见支持矩阵 §1.0.1 | [OrmProviderSupportMatrix.md](../../docs/Feature/OrmProviderSupportMatrix.md) |
| src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/CrestCreates.Runtime.Persistence.PostgreSql.csproj | Persistence | 导航；#132 retention provider + V016/V017 已交付（见 §7） | [arch-design.md](../../docs/Feature/RuntimePersistence/arch-design.md) |
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
| src/Runtime/Audit/CrestCreates.Accountability.Abstractions/CrestCreates.Accountability.Abstractions.csproj | Runtime | 导航；#132 retention 契约已交付（见 §7） | [arch-design.md](../../docs/Feature/Accountability/arch-design.md) |
| src/Runtime/Audit/CrestCreates.Accountability/CrestCreates.Accountability.csproj | Runtime | 导航；#132 retention executor 已交付（见 §7） | [arch-design.md](../../docs/Feature/Accountability/arch-design.md) |
| src/Runtime/Audit/CrestCreates.AuditLogging.Abstractions/CrestCreates.AuditLogging.Abstractions.csproj | Runtime | 导航；#132 未改动此项目 | — |
| src/Runtime/Audit/CrestCreates.AuditLogging/CrestCreates.AuditLogging.csproj | Runtime | 导航；#132 旧 cleanup 退出、admission 归入 Application（见 §7） | — |
| src/Runtime/Capability/CrestCreates.Capability.Abstractions/CrestCreates.Capability.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/CapabilityExposure/arch-design.md) |
| src/Runtime/Capability/CrestCreates.Capability/CrestCreates.Capability.csproj | Runtime | 导航（本轮未重评，不背书） | [arch-design.md](../../docs/Feature/CapabilityExposure/arch-design.md) |
| src/Runtime/DistributedTransaction/CrestCreates.DistributedTransaction.CAP/CrestCreates.DistributedTransaction.CAP.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/DistributedTransaction/CrestCreates.DistributedTransaction/CrestCreates.DistributedTransaction.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.Event.Abstractions/CrestCreates.Event.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.Event/CrestCreates.Event.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.Abstract/CrestCreates.EventBus.Abstract.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.Abstractions/CrestCreates.EventBus.Abstractions.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.DeadLetter.EFCore/CrestCreates.EventBus.DeadLetter.EFCore.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.Kafka/CrestCreates.EventBus.Kafka.csproj | Runtime | 指定结算边界，JIT-only；见 §9 | R3-18 |
| src/Runtime/Eventing/CrestCreates.EventBus.Local.Channel/CrestCreates.EventBus.Local.Channel.csproj | Runtime | 导航（本轮未重评，不背书） | — |
| src/Runtime/Eventing/CrestCreates.EventBus.Local/CrestCreates.EventBus.Local.csproj | Runtime | 导航（重放并发/取消有限裁定见 [#146 §8.1](../superpowers/specs/2026-10-14-issue-146-local-event-framework-hardening-design.md#81-r3-16-有限裁定并发所有权与取消后的副作用2026-11-12)，不整体背书） | — |
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
- 原生执行证据矩阵：`docs/review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md`（含 §#159 显式终态场景行与其工具链版本边界）。
- 平台闭环状态：`memory.md`；主链共识与支持声明规则：`AGENTS.md`。
- `docs/review/crestcreates-abp-comparison.md` 中 PluginSystem「生产就绪」为失实的活跃引用，已按本地图 D-12 纠正；历史审查文档保持历史身份并链接本地图。

## 7. #132 审计保留治理（V1 交付记录）

日期：2026-10-27。设计：[设计](../superpowers/specs/2026-10-27-issue-132-audit-retention-governance-design.md)、
计划：[实施计划](../superpowers/plans/2026-10-27-issue-132-audit-retention-governance-implementation.md)。
本节只记录交付事实与支持声明，不重评 §2 的 #131 定位结论，不复制 ORM Provider 矩阵。

| 项目 | 定位 | 功能状态 | 执行证据 | 处理结论 |
| --- | --- | --- | --- | --- |
| `CrestCreates.Accountability.Abstractions` | 抽象契约（scope/policy/run/batch/hold/`IAuditRetentionStore`/精确 receipt 读取） | 已交付指定边界（V1） | retention 过滤 38/38；PG 20/20 | 保留；后续扩展必须保持 receipt/Unknown 语义 |
| `CrestCreates.Accountability` | 执行实现（`AuditRetentionExecutor`，终态拒绝新批、receipt 对账、镜像失败优先、显式导出） | 已交付指定边界（V1） | retention 过滤 38/38；PG 20/20；原生九场景 | 保留；不得新增公开故障开关或裸删旁路 |
| `CrestCreates.AuditLogging` + `CrestCreates.Application` | admission 与 typed job（含 `AuditLogging.Retention.Export`） | 已交付指定边界（V1） | Application retention 21/21；P08 真实身份/权限/Setting/HTTP 2/2 | 保留；旧 cleanup 已归档到 `99_RecycleBin/issue-132-audit-retention/` |
| `CrestCreates.Runtime.Persistence.PostgreSql` | Provider（V016/V017、sink、retention store） | 已交付指定边界（V1，仅 PostgreSQL 固定数据库） | PG 20/20；sink/迁移集成过滤 22/22；NativeAOT linux-x64 九场景 + 总 sentinel + 三类负控 | 限界保留；EF/FreeSql/SqlSugar/MongoDB、其他 RID、自动调度未验证 |

支持声明：正式 PostgreSQL retention V1 的已执行场景为 **NativeAOT-verified（linux-x64）**；
真实认证/权限/Setting/generated HTTP 组合由 JIT P08 覆盖，不推导原生认证链；自动 Quartz
调度因缺少认证主体保持关闭，不属于已交付能力。PR #157 审查轮 R1 的 2 个 P1/2 个 P2
（终态 run 仍可提交新批、恢复误报 failed/0 与取消覆盖 Completed、累计删除记入单批事实、
后批镜像掩盖前批失败且缺显式导出）与 R2 的 2 个 P2（创建故障恢复跳过 ReasonCode 绑定、
确认提交后 receipt 读故障降级为 Unknown）已修复并补入回归；原生负控为 fixture 内 store
装饰器变异，不等价于真实 SQL/事务变异。

纪律：新增 retention Provider/RID/策略类型时按组合单独补证据，更新本表与 `memory.md`；
不得从本节推导其他项目或组合的支持状态。

## 8. #159 显式 unsupported / needs-clarification 终态（交付记录）

日期：2026-11-05。设计：[设计 rev.9](../superpowers/specs/2026-11-03-explicit-unsupported-and-clarification-authoring-outcomes-design.md)、
计划：[实施计划与切片日志](../superpowers/plans/2026-11-03-issue-159-explicit-unsupported-clarification-plan.md)。
本节只记录交付事实与支持声明，不重评 §2/§4 的定位与导航结论，不复制 ORM Provider 矩阵，也不改变 §5 对 fixture 项目的单列口径。

| 项目 | 定位 | 功能状态 | 执行证据 | 处理结论 |
| --- | --- | --- | --- | --- |
| `CrestCreates.Agent.Authoring.Abstractions` | 抽象契约（`outcome` 判别式、`UnsupportedIntent`/`NeedsClarification` 两终态与载荷类型、两张闭码表、两枚诊断码、契约侧 JSON 根） | 已交付指定边界（`8a.v1`） | JIT：`CrestCreates.Agent.Authoring.Tests` 139/139（§10.1 全 23 行矩阵）；NativeAOT linux-x64：`AgentAuthoringUnsupportedOutcome` / `AgentAuthoringClarificationOutcome` 结构化往返（含读回状态） | 保留；新增终态必须同时进入词表、码表与 JSON 根闭包，不得留兼容旁路 |
| `CrestCreates.Agent.Authoring` | 执行实现（Stage A 原始信封事实 + Stage B 严格 source-generated 投影 + 归一化/边界 + 提示披露 `descriptor-authoring-prompt-template-v3`） | 已交付指定边界（`8a.v1`） | JIT：同上 139/139；变异 (a)(b)(c)(d)(e)(f1)(f2) 各按所宣称机制打红；NativeAOT linux-x64：`AgentAuthoringOutcomeShapeConflict`、`AgentAuthoringCodeTableRejection`、`AgentAuthoringEnvelopeUnknownMember`、`AgentAuthoringEnvelopeDuplicatePrecedence`、`AgentAuthoringWireDisclosure`（四条披露示例逐条过正式 parser）；`AgentAuthoringOutcomeRequiredRoots` + `ReflectionFallback_IsDisabled` | 保留；Stage A 只拥有信封事实，禁止把 payload 内层字段名纳入词表；解码失败由执行读取的那一步报告，不新增优先级层 |
| `CrestCreates.Agent.Authoring.Http` | Provider 适配（OpenAI-compatible 客户端） | 已交付指定边界（本片只迁移录制件与版本字面量） | JIT：`Authoring.Tests` 内 provider/录制件用例；原生：本切片未新增该项目的场景 | 保留；本片不据此项目推导原生 HTTP 认证/租户链 |
| Asset 样例真实链路（`samples/AssetManagement/**/E2E.Tests`） | 消费者与治理证据（oracle 两层判定：新契约覆盖 vs 历史归因） | 已交付指定边界 | JIT + 本地 PG：Asset 全量 E2E **128 通过 / 2 跳过（共 130）**，跳过项为未开启 `CREST_ASSET_LIVE_EVAL` 的 live 入口；`AssetB12CoverageEntryTests` 常驻判别历史归因不得计为覆盖。带凭据 live（S5，单次、获准范围）：B12 不支持意图 → `UnsupportedIntent` + 3 条合法条目，oracle 判 `ExplicitUnsupportedResult`，零草稿/零评审/零保存/零 gate、部署清单不变、未伪造能力 | 保留；`Blocked` + intent-level 码永远是历史归因，不是 `8a.v1` 覆盖；散文拒绝与「不支持意图得到 drafts」仍为拒绝；live 覆盖只到实际跑过的那一个意图类与那一次运行 |
| 样例 golden scenario（`samples/CrestCreates.Samples.DescriptorControlPlane`） | 消费者（runner/fake agent 三分支） | 已交付指定边界 | JIT：`CrestCreates.Samples.Tests` 37/37（显式终态零保存/零评审 + `FinalDecisionSource` 三向唯一） | 保留；显式终态不得进入草稿集合审查 |

支持声明：`8a.v1` 两个显式终态与四类信封事实在**已执行样本范围**内为 **NativeAOT-verified（linux-x64，验收入口为用户目录官方 SDK 10.0.114 + 隐式 ILCompiler 10.0.14）**。该声明：

- 不扩展为 §10.1 全 23 行矩阵原生通过（全矩阵为 JIT 单测证据）；
- 不推导认证、审批、activation、durable activation 链，也不推导 Windows/macOS 等其他 RID；
- 不覆盖其余七个原生门禁（CapabilityEndpoint、GeneratedCrud、AssetManagement、Procurement、Ticket、PostgreSql、RabbitMQ AotHost）——它们仍按 [#123 证据矩阵](../review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md) 的旧工具链记录列账，尚未在 10.0.114 下复验；
- 默认 `/usr/bin/dotnet` 仍为 10.0.113，`dotnet build` 在该入口下仍会命中 `error NU1903`（GHSA-p3jp-9v83-76w9，修复版 10.0.14）。系统包升级属环境维护项，本节不宣称全环境已迁移；任何任务/CI 复用该门禁时必须显式选择 10.0.114，避免裸 `dotnet` 静默回退旧 host。
- **带凭据真实模型答题（S5）已按获准范围执行一次**：B12 不支持意图 → 真实 provider 答 `unsupported`（HTTP 200、
  `FinishReason=stop`、16384 预算被接受，非预算/provider 旁因），解析为 `UnsupportedIntent` + 3 条合法条目 + 逐条
  `AUTHORING_INTENT_UNSUPPORTED`，oracle 判 `ExplicitUnsupportedResult`，零草稿/零评审通过/零保存/零 gate、部署清单
  不变、未伪造能力 —— 该意图类在此路径上按 `8a.v1` 计为覆盖。边界同样明确：**单次运行**不支撑可重复性或概率声明；
  仅覆盖单一意图类与单一模型画像（请求 `deepseek-v4-flash`，观测 provider `deepseek`、model `deepseek-flash`）；
  `NeedsClarification` 的 live **未执行**，其证据仍只有单元/真实链路/原生三层；live 运行在 JIT 宿主内，不新增原生覆盖；
  凭据未打印/入库，工件留在 `/tmp` 且只含元数据（字符数与 token 数，无响应或推理正文）。

纪律：新增 outcome、码表项或终态载荷时，必须同批更新词表/码表/JSON 根闭包、§10.1 矩阵行、原生场景与 sentinel 点名、`memory.md` 与 `AGENTS.md` 的对应声明，并补对应变异判别；不得以「状态名出现」替代「内容 + 零副作用」的覆盖判据，也不得从本节推导其他项目或组合的支持状态。

## 9. R3-18 Kafka 结算与支持边界

日期：2026-11-12。定位为 Kafka 传输执行实现，限界保留，技术责任 Eventing。唯一装配使用现有 `AddKafkaEventBus`，宿主通过新增 `AddKafkaSubscription(KafkaSubscriptionInfo)` 显式贡献现有 typed invoker。库内静态空 registry 不再是消费者发现源；客户端生成的列表不会因同名类型自动覆盖库内列表。宿主可逐项贡献其列表，须同时注册 handler 与对应 JSON context；本次未修订 Kafka generator 的输出协议或证明属性声明可自动装配。

- 每个 topic 只允许一项订阅，重复项（含不同事件类型）在启动前拒绝；可在单个 typed invoker 内显式编排多个 handler。订阅的 `ConsumerGroup` 若非空必须等于 `KafkaOptions.ConsumerGroupId`，不再默默忽略不同 group。主题名、FullName 事件身份和信封 wire 保持原协议，canonical identity 迁移仍属 R3-33。
- `EnableAutoCommit=true` 拒绝；自动 offset store 固定关闭，`AutoCommitIntervalMs` 保留源码兼容但不参与结算。空/坏信封、无订阅、事件类型不符、缺 JSON type info、空/坏 payload 直接转原始 DLQ，保留正文（含 tombstone/null）、key、全部原始 headers，再附原因和来源 topic/partition/offset。普通 handler 失败仍按信封 RetryCount 与现有预算重投原 topic，耗尽后转 DLQ；关联身份保留。DLQ suffix 必须非空。
- 仅在 producer 报告 `PersistenceStatus.Persisted` 后 commit 当前记录。消费循环串行执行；重投、DLQ、commit 或消费故障直接终止循环并请求宿主停止，绝不继续消费后一条记录。修复故障后由部署方重启宿主，使用同一 group 恢复；没有内置自动重启。Stop 仅取消，Close/Dispose 归消费循环所有。
- Forward 与 commit 没有跨资源原子性；确认后、commit 前崩溃或取消仍可能重复投递，handler 的部分副作用也可能重复。producer idempotence 不是分布式 exactly-once，不承诺该保证，也不承诺 handler 重试后业务顺序不变。
- producer pool 的容量预留、借用身份、归还及停机在同一锁协议下维护；创建失败归还容量，等待者在停机后拒绝，已借出的 producer 由归还方最终释放。Dispose/DisposeAsync 不等待调用方持有的 lease；调用方仍须遵守一次 Get/一次 Return，归还后不得继续操作 producer。未借用/外来/直接重复归还被拒绝；原始 producer API 没有每次借用 token，不能检测归还后再次借出期间的陈旧持有者误归还。未引入公开测试工厂或后台 Task.Run。

执行证据为 **JIT-only（linux-x64，Confluent.Kafka 2.12.0 + Apache Kafka 4.0.0 单 broker、单进程宿主）**：[KafkaSettlementTests](../../tests/Runtime/Eventing/CrestCreates.EventBus.Kafka.Tests.Integration/KafkaSettlementTests.cs) 从真实 `AddKafkaEventBus`/DI subscription/hosted consumer 执行，覆盖毒消息后正常消息、原始 DLQ 保留、broker 拒绝超限 DLQ 写入时零提交，以及同 group 新宿主恢复、handler 重投预算。缺 type info 场景下第二条记录也不可执行，明确隔离两条，其他毒消息有同分区正常业务字段对照。`unknown-subscription` 由内部确定性单元用例验证，真实 broker 订阅只会投递已订阅 topic，不宣称该分支有 broker 执行证据。池并发用同步屏障验证创建失败恢复满容量与停机/借还/等待交错；commit 故障、未确认 delivery、取消和 Stop 所有权用 SDK 接口替身验证。准确命令及结果见 `memory.md` R3-18。

未执行 trim 或 NativeAOT publish/link/run，无原生门禁，不新增原生支持声明；SASL/TLS、多 broker 故障、分布式 exactly-once、跨进程 rebalance 与 broker 重启恢复未验证。测试的“重启”是同一测试进程内销毁并重建 Host，未声称 kill OS 进程或重启 Kafka。

## 10. R3-19 Quartz 执行与支持边界

日期：2026-11-12。定位为既有后台作业契约与 Quartz 适配，技术责任 Scheduling。唯一装配为 `AddQuartzScheduling()`，`SchedulingQuartzModule` 委托同一入口；该入口包含 adapter 和默认 failure handler，不强制引入内存历史仓库。`IJobExecutionHandler` 可选，只增加跟踪，不再决定业务参数、身份或租户上下文是否存在。

- 参数化任务显式调用 `AddQuartzJobArgs(MyJsonContext.Default.MyArgs)`，注册 `JsonTypeInfo<TArg>`；缺合约在创建调度状态前拒绝。`AddAuditRetentionApplication()` 已直接提供 `AuditRetentionJobArgs` 的源生成 `JsonTypeInfo` singleton（默认 PascalCase，保留 nullable TenantId），不依赖 Quartz；组合 scheduler 时无需 Host 另补此 codec，自动 retention 调度仍关闭。运行时仅按这一合约解码一次，`NoArgs` 使用强类型常量；无反射序列化 fallback。周期参数新增 `ISchedulerService.RegisterAsync<TJob,TArg>(metadata, args)`；原无参数 generic 重载仅接受 `NoArgs`，其他参数类型明确拒绝。接口替身/外部实现需补新增重载。失败记录由新增 `JobFailureContext.ArgsJson` 保留原始 JSON，手动构建 failure context 的调用方若需要历史参数也须提供该属性。
- 返回 `JobId` 同时保存实际 Quartz name/group 和独立稳定 UUID；业务、历史、GetAll 和控制操作共用它。`GetAll` 的 JobType/ArgType 来自 typed 注册的类型，而非 adapter 反射推断。命名任务不再把任意名字解析成 GUID。重复 name/group 在 Start 后、任何 pause/历史/job 写入前 CheckExists，抛包含 name/group 的 `InvalidOperationException`，保留原任务与 trigger；重新定义需显式 Delete/Register。注册互斥闸门内顺序为 CheckExists → Enabled 暂停 → Scheduled 通知 → ScheduleJob，并发重复不会产生幽灵历史，通知失败不留下任务或 trigger。UUID-only 控制仍映射 UUID 命名的单次任务。
- 每次 fire 由 factory 包装 job 独占一个 AsyncScope，官方 `SimpleJobFactory` 的异步 return 路径等待其释放，包括构造异常、业务异常与取消；adapter 使用该 scope，不另开子 scope。同一 JobKey 使用 `DisallowConcurrentExecution`，不同 JobKey 可并发。
- `MaxRetries` 表示初次执行之外的重试次数：attempt 1，最多到 `1 + MaxRetries`。每次原始 cron fire 从 1 开始，重试计数及原始 ScheduledAt 只放独立单次 trigger，不写 MergedMap、不替换 cron。metadata.Retry 优先（含 0 禁用），否则使用既有 failure/execution handler 策略与全局 JobRetryOptions；自定义 handler 仍须履行有界策略契约。默认 retry delay 为 1 秒、指数退避默认上限 1 分钟，绝对上限 1 天；非正自定义 handler delay 收敛为 100 ms。配置 count/倍率/期限非法则拒绝。默认 handler 原先把 MaxRetries 当总尝试数的行为已迁移，同名选项现在会多一次初始执行。
- cron misfire 显式为 FireAndProceed（FireOnceNow），保留 Quartz 原 SmartPolicy 的合并补发一次行为；`Enabled=false` 在插入前暂停专属 trigger group，防止立即执行竞态；Resume 从实际 JobDetail 读取稳定 UUID，清除专属组暂停并恢复任务触发器，使后续重试不继承初始暂停。命名控制（含 `JobId.Create` 的空 UUID）与 UUID 单次任务使用同一恢复路径。metadata.Timeout 是协作 deadline，和 Quartz interrupt token 合并传入业务及 context；取消不创建重试。已完成业务的成功回调在业务 catch 外执行，历史写入故障不会重跑业务，Quartz listener 仍可观察异常。调度/历史写入不构成分布式原子事务。
- 显式指定 TenantId 时要求 `ICurrentTenant` 与 `ITenantProvider`，经现有 provider 按权威 Id 解析，再用可恢复 `Change` frame；缺少设施、未知/不匹配租户均在业务构造前拒绝。host 不指定租户时设施仍可选，已注册时显式进入空 frame，不回退名称。公开调度 tenant/org/user 参数仍为 nullable GUID，其他字符串 Id 未扩展；未建立认证 principal，审计 retention 自动调度仍保持关闭。
- Start/Stop 在异步锁内登记生命周期状态，每个 service 实例使用独立 Quartz scheduler 名称；并发 Start 只创建一个 scheduler，Start/Stop 竞争不丢失已创建实例。Stop 在锁外调用并等待 Quartz shutdown；并发 Stop/Dispose 共享同一完成任务，等待运行任务及 scope 释放。停机中的业务回调调度 API 会及时收到 stopped 拒绝；开始 shutdown 后不由调用方取消中断，停止后拒绝重启。支持容器同步 `Dispose`，阻塞直到完整 shutdown 完成，重复释放共享完成结果；优先使用 `DisposeAsync`。非协作任务可阻塞停机及同步释放，不承诺强制终止。

执行证据：`tests/Framework/Modules/CrestCreates.Scheduling.IntegrationTests/QuartzSchedulerIntegrationTests.cs` 通过真实 RAMJobStore/adapter/DI 验证参数、有无历史、命名 cron、延迟/立即、有限重试与下一周期、构造/取消/失败释放、同任务串行、命名取消、deadline、host/A/B、类型查询和并发启动；同步屏障、Quartz listener/trigger 状态与释放计数作为断言，参数测试关闭 STJ 默认反射兜底。PR #183 P2 补证：disabled 命名重复注册保留一条 Scheduled、原 trigger/暂停组且恢复后仅执行一次；通知失败不留下任务或历史；普通 ServiceProvider 同步 Dispose 在时限内完成、释放异步资源、移出 scheduler repository 且重复释放/终态拒绝成立。最终真实集成 36/36、调度单元 46/46、Application 271/271（含无 scheduler 的 codec singleton 与字段往返），SDK 10.0.114 串行执行，均零跳过。新审计 JSON 合约无 trim 告警，Application 既有 Settings/Permissions/Features 六条告警仍在。准确命令及结果见 `memory.md` R3-19。

支持声明为 **JIT-only（linux-x64，Quartz 3.17.1 RAMJobStore，已执行场景）**。本次固定使用 RAM；Type/Retry/Timeout 等 map 值为进程内对象，不构成持久 JobStore 协议。持久化 JobStore、进程重启恢复、多节点集群、数据库 history provider、其他 RID、认证主体链、trim 和 NativeAOT 均未验证；没有调度 native fixture，不新增 native publish/link/run 声明。源生成 JSON 测试不能替代 NativeAOT 证据。
