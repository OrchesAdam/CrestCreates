# #129 S1 事实基线：调用图、Ticket 真实线路契约与设计冻结

- 状态：S1 交付稿（待设计审查）。基线提交 `196b6f66048ef32d644eeb856d00d58ae9d34965`（= PR #150 合并后的 master）。
- 上游：`docs/superpowers/specs/2026-10-17-issue-129-generated-http-controller-exit-design.md`（rev.1，已发布到 #129 评论）。
- 本文所有数字/线路/状态码均来自实际构建产物与实际 HTTP 调用；无一条取自测试注释、文档或属性声明文本。
- 命名约定：本文里带「拟」的名称仍是待冻结契约，不声称已存在；带 file:line 的是当前代码事实。

## 0. 可复现步骤（重要前提）

```bash
# 1) BuildTasks 必须先单独构建，否则任何下游项目在并行构建里报 MSB4062（UsingTask 程序集不存在）
dotnet build src/Tooling/CrestCreates.BuildTasks -c Debug
# 2) 再构建 sample，编译期生成物落盘
dotnet build samples/SaaSHelpdesk/SaaSHelpdesk.Web -c Debug
# 3) 真实 HTTP 观测（需要 podman）
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock dotnet test samples/SaaSHelpdesk/SaaSHelpdesk.Tests \
  --filter "FullyQualifiedName~TicketWireBaselineTests|FullyQualifiedName~TicketProjectionGapBaselineTests"
```

生成物读取点（同一次构建，非预填）：
`samples/SaaSHelpdesk/SaaSHelpdesk.Application/obj/Debug/net10.0/source-generators/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.DynamicApiGenerator.DynamicApiAotSourceGenerator/{GeneratedDynamicApiRegistry.g.cs,GeneratedDynamicApiEndpoints.g.cs,GeneratedDynamicApiControllerRegistrations.g.cs}`。

## 1. I01：Ticket 的真实调用图（声明 → 生成 → 注册 → Host → 路由 → 运行时）

| 环节 | 事实 | 证据 |
| --- | --- | --- |
| authored 声明 | `TicketAppService` 仅有 `[CrestService]` + 继承 `CrestAppServiceBase<Ticket, Guid, TicketDto, CreateTicketDto, UpdateTicketDto>`；**没有任何 HTTP 投影 Attribute** | `samples/SaaSHelpdesk/SaaSHelpdesk.Application/Services/TicketAppService.cs:20-21` |
| authored 旧 Controller | `TicketApi : CrestApiController`，`[GeneratedApiController("api/ticket")]` + 一个 `[ApiOverride(CrudAction.GetList)]` 的 `GetAllAsync` | `samples/SaaSHelpdesk/SaaSHelpdesk.Application/GeneratedApi/TicketApi.cs:8-20` |
| 生成器（唯一活路径） | `DynamicApiAotSourceGenerator` 分支 (a) 消费 `[CrestService]`/`ICrudAppService` 类；分支 (b) 消费 `GeneratedApiControllerAttribute`/`ApiOverrideAttribute`，并用 `overriddenActions` 抑制同动作的 AppService 端点 | `src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs:105-136,145-160,219-222,868-878,973-983,1066-1075` |
| 路由/动词/权限推导 | 名称→路由 `DynamicApiConventionAnalyzer.ResolveActionRoute`；动词 `:255-285`；权限 `ResolvePermission:116-140`（非 Create/Update/Delete/Get* 一律 `.Search`） | `src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiConventionAnalyzer.cs:52-114,116-140,255-285` |
| 注册（进程全局） | 生成的 `[ModuleInitializer]` 调 `DynamicApiGeneratedRegistryStore.Register(...)`；**无 per-Host 显式激活** | `DynamicApiAotSourceGenerator.cs:1019-1026`、`src/Framework/Api/CrestCreates.DynamicApi/DynamicApiGeneratedRegistryStore.cs` |
| Host 装配 | `builder.AddCrestWeb(o => o.UseGeneratedApi(api => api.AddApplicationServiceAssembly<TicketAppService>()))` → `MapCrestWeb()` → `MapCrestAspNetCoreDynamicApi` → `MapCrestDynamicApi`（只查 generated provider，找不到即抛） | `samples/SaaSHelpdesk/SaaSHelpdesk.Web/Program.cs:13-31,46`、`src/Platform/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs:172,214`、`src/Framework/Web/CrestCreates.AspNetCore/AspNetCoreModuleExtensions.cs:35-60`、`src/Framework/Api/CrestCreates.DynamicApi/DynamicApiExtensions.cs:17-63` |
| 运行时 helper（反射点） | 每个写动作 body 走 `DynamicApiGeneratedRuntime.ReadBodyAsync<T>` → `CompatibilityBodyReader`（`JsonSerializer.Deserialize<T>(payload, options)`，无 `JsonTypeInfo`）；权限走 handler 内 `EnsurePermissionAsync`；UoW 走 `ExecuteAsync(context, new UnitOfWorkOptions(), kernelToken => …)` | `GeneratedDynamicApiEndpoints.g.cs:1357,1425`、`src/Framework/Api/CrestCreates.DynamicApi/DynamicApiGeneratedRuntime.cs:39-41,43-63,91-136`、`src/Framework/Api/CrestCreates.DynamicApi/CompatibilityBodyReader.cs:21-26,54` |
| 端点级授权 | 端点仅挂 `DynamicApiPermissionMetadata` 作为元数据；`IAuthorizeData` 集合为空（已由 `TicketWireBaselineTests` 锁定） | 实测 `endpointAuthorize=[]`（11/11） |

结论：Ticket 的 HTTP 面 100% 来自 legacy 生成投影；#128 的 ExplicitHost/JsonContracts 闭包**没有一条 Ticket 端点在用**。手写服务的 compat 投影（`AppServiceCompatibilityGenerator`）是另一条独立链，真实消费者只有 Asset/Procurement 两个 Host 门面 + 两个测试 fixture（见 §5）。

## 2. 对 rev.1 设计事实表的更正（必须进入 rev.2）

| rev.1 表述 | 实测事实 |
| --- | --- |
| 路径 `samples/SaaSHelpdesk/Application/...` | 实际是 `samples/SaaSHelpdesk/SaaSHelpdesk.Application/...`（项目名前缀不可省） |
| 「`CrestApiController` 未继承 MVC `ControllerBase`」 | 成立；且不存在 `GeneratedApiControllerBase` 类型，只有 `CrestApiController` |
| 「TicketApi 只重写 GET api/ticket/all」 | 成立，且该端点权限名是 **`Ticket.GetAll`**，不是 `Ticket.Search` |
| §5 矩阵 `GetAllAsync → Query / Ticket.Search` | 与真实投影不符：`GET api/ticket/all` = `Ticket.GetAll`（`GeneratedDynamicApiRegistry.g.cs` 末段 Ticket 组） |
| 「`TicketAppService` 需要补 compat 声明」 | `TicketAppService` 当前**完全没有** `[CapabilityCompatibilityProjection]`；它今天走 `DynamicApiAotSourceGenerator`，不是 compat 链。迁移含义：新增正式声明并让 compat/AOT 两个前端按 owner 跳过 |
| 「归因为残留辅助 API：`LegacyRuntime.ValidateAsync`」 | 无 `LegacyRuntime` 类型；实为 `DynamicApiGeneratedRuntime.ValidateAsync:67` 调静态 `DynamicApiRouteConvention.IsScalar`，且该静态方法是 `DynamicApiRouteConvention` 在仓库内的**唯一活消费者** |
| 未提及 | `TicketApiTests` 的注释与真实线路不符：`:159` 声称分页 `?pageIndex=0&pageSize=10`（实际 `GET /api/ticket/all`，无分页）、`:240/:288/:338` 声称 `POST /api/ticket/{id}/assign|resolve|close`（实际 `GET /api/ticket/assign?id=&agentId=` 等）、`:432/:456` 声称 `/api/ticket/customer/{id}`、`/assignee/{id}`（实际 `by-customer/`、`by-assignee/`） |

## 3. I02：冻结的 Ticket 线路矩阵（真实 Host + 真实 HTTP）

端点集合（`EndpointDataSource`，11 条，恰好一次，来源均为 legacy 生成投影）：

| Method / Route | DisplayName | 端点权限元数据 | 输入绑定（实测） | 观测结果 |
| --- | --- | --- | --- | --- |
| POST `api/ticket` | ITicketAppService.Create | `Ticket.Create` | required `CreateTicketDto`（反射 body reader） | 合法负载 200 `{"data":{...},"code":200,"message":"操作成功"}` |
| GET `api/ticket/{id}` | ITicketAppService.GetById | `Ticket.Get` | `Guid.Parse` 路由值 | 命中 200；未知 id **404 `{"code":404,"message":"资源不存在"}`**（非错误 DTO） |
| PUT `api/ticket/{id}` | ITicketAppService.Update | `Ticket.Update` | 路由 + required body | **陈旧 stamp 仍 200 且覆盖成功** |
| DELETE `api/ticket/{id}` | ITicketAppService.Delete | `Ticket.Delete` | 路由 + `If-Match` 头（可缺失） | **缺 If-Match 仍 200 删除** |
| GET `api/ticket/all` | **TicketApi.GetAll** | **`Ticket.GetAll`** | 无 body | 200 + `data` 数组 |
| GET `api/ticket/assign` | ITicketAppService.Assign | `Ticket.Search` | 两个 `Guid.Parse(query)` | 正常 200；缺 `agentId` → **500** |
| GET `api/ticket/resolve` | ITicketAppService.Resolve | `Ticket.Search` | 一个 `Guid.Parse(query)` | 未知 id → 404 `Crest.Entity.NotFound`（带 details） |
| GET `api/ticket/close` | ITicketAppService.Close | `Ticket.Search` | 一个 `Guid.Parse(query)` | 非法 Guid → **500** |
| GET `api/ticket/by-customer/{customerId}` | ITicketAppService.GetByCustomer | `Ticket.Get` | 路由 Guid | 200 数组 |
| GET `api/ticket/by-assignee/{assigneeId}` | ITicketAppService.GetByAssignee | `Ticket.Get` | 路由 Guid | 200 数组 |
| GET `api/ticket/overdue` | ITicketAppService.GetOverdue | `Ticket.Search` | 无 | 200 数组 |

未认证：`GET all` 与 `POST` 均 401 `Crest.Auth.Unauthorized`（来源：handler 内 `EnsurePermissionAsync` 抛 `UnauthorizedAccessException`，`DynamicApiGeneratedRuntime.cs:53-56`）。

错误呈现（两套并存，实测）：`CrestErrorResponse{Code,Message,Details,TraceId,StatusCode}`（`src/Framework/Web/CrestCreates.AspNetCore/Errors/`）与 `DynamicApiResponse{code:int,data,message}` 的 404/成功包装（`CompatibilityHttpResultMapper`）。`DefaultCrestExceptionConverter.cs:28-52` 的映射实测结果：`JsonException→500`、`FormatException→500`、`ArgumentException→400 Crest.Request.InvalidArgument`、`InvalidOperationException→400 Crest.Operation.Invalid`、`KeyNotFoundException→404 Crest.Entity.NotFound`、`UnauthorizedAccessException→401/403`。

## 4. S1 红基线（每条只因其声称的能力缺口失败）

入库文件：`samples/SaaSHelpdesk/SaaSHelpdesk.Tests/TicketProjectionGapBaselineTests.cs`。绿基线：`TicketWireBaselineTests.cs`（6 条）。实测计数（head 见 issues log）：红 8 / 绿 6 / 合计 14。

| ID | 断言 | 实测失败原因（单一） | 归属切片 |
| --- | --- | --- | --- |
| R01 | EfCore UoW 绑定恰好 1 个 | 实际 **3** 个：平台预设 `CrestCreatesWebApplicationExtensions.cs:138-139` + 样例 `SaaSHelpdesk.EntityFrameworkCore/Modules/EntityFrameworkCoreModule.cs:50-51` + 测试 fixture `Fixtures/HelpdeskWebApplicationFactory.cs:384-385`；`AddUnitOfWorkProvider` 用 `AddSingleton` 不去重（`Data.Abstractions/Extensions/UnitOfWorkServiceCollectionExtensions.cs:124`），`UnitOfWorkProviderBindingRegistry.cs:26-37` 首次解析即抛 `InvalidOperationException` | S3（写链与 UoW owner） |
| R02 | `Ticket.Create/Get/Update/Delete/Search/GetAll` 是已定义权限 | `IPermissionDefinitionManager.GetOrNull` 全为 null：样例只定义 `Helpdesk.Tickets.*`（`SaaSHelpdesk.Domain.Shared/Permissions/HelpdeskPermissions.cs:6-17`），`EntityPermissionsManifest.json` 为 `{"permissions": []}`（构建日志「with 0 entities」） | S3 |
| R03 | 坏 JSON body → 400 | 实测 500 `Crest.InternalError`（`JsonException` 无映射） | S3（§7 失败桥接） |
| R04 | 非法/缺失 Guid 路由或查询 → 400 | 实测 500（`Guid.Parse` 抛 `FormatException`，三处：路由 id、`close?id=`、`assign?id=` 缺 `agentId`） | S3 |
| R05 | 陈旧 stamp 的 Update → 409 且零写入 | 实测 200 且标题被覆盖：`CrestAppServiceBase.cs:256-259` 在映射后**回填原 stamp**，客户端值从不参与比较 | S3 |
| R06 | 缺 `If-Match` 的 Delete → 428 且不删行 | 实测 200 删除：`CrestAppServiceBase.cs:296-318` 无 stamp 时直接 fallthrough 到无条件删除 | S3 |
| R07 | 读契约暴露当前 stamp | `concurrencyStamp` 字段存在但恒为 `""`：`TicketAppService.MapToDto`（`:44-64`）未复制该字段 | S3 |
| R08 | 声明的 DTO validator 先于领域规则执行 | `title="ab"`（违反 `CreateTicketDtoValidator` 的 5-200 规则）实测 **200 创建成功**；即 `IValidationService` 未拒绝，且 `Program.cs:38` 用 `AddValidatorsFromAssemblyContaining`（运行时扫描）注册，与生成的同名校验器（`ServiceSourceGenerator` 输出 `SaaSHelpdesk.Application.Services.Validators.CreateTicketDtoValidator`）不同命名空间并存 | S3 |

注：R05–R08 的写路径观测需要临时消除 R01 的重复绑定（`WithSingleUnitOfWorkBinding()`，测试内注释已标明这是「测量归一化」而非修复）；R01 自身由独立断言锁定，因此不存在把多个缺口混在一条红例里。

R01 的影响面必须显式记账：**在 master 上，SaaSHelpdesk 生产 Host 与测试 Host 的全部写动作都在 UoW 绑定索引构建期失败**（先前记录为「`POST /api/ticket` 返回 400 `Crest.Operation.Invalid`」，根因即此）。全量套件实测：`SaaSHelpdesk.Tests` 29 pass / 70 fail / 99，其中本切片约定红 8 条，master 既有红 62 条（32 条消息体即 `Crest.Operation.Invalid`、17 条同为 400 写失败、10 条 `POST /api/user` → 404、2 条 `ITenantProvider` 未注册的 DI 构建校验失败）。
`POST /api/user` 404 的静态原因已定位：框架项目 `CrestCreates.Application` 的编译期生成目录只有 `SchemaCapabilitySourceGenerator` 与 `ServiceSourceGenerator` 输出，没有 `DynamicApiAotSourceGenerator` 产物，尽管 `UserAppService` 带 `[CrestService]`（`src/Framework/Ddd/CrestCreates.Application/Identity/UserAppService.cs:17-18`）——即 `AddApplicationServiceAssembly<UserAppService>()` 在生成面上是空集合；这同时波及 Setting/Feature/AuditLog/User 等框架服务是否有任何 HTTP 面，归属判定留给 S3。
因此 #129 的「真实集成用例通过」验收必须先以这批基线修复为前提，不能靠新增测试的数量推导。

## 5. legacy 消费者盘点与允许清单（归档判定依据）

零代码消费者（可直接归档，仅需同时退役其专属旧输出测试）：

| 对象 | 声明位置 | 消费者事实 |
| --- | --- | --- |
| `ControllerSourceGenerator` | `src/Tooling/CrestCreates.CodeGenerator/ControllerGenerator/ControllerSourceGenerator.cs:16`（`:15` Obsolete） | 触发条件 `[CrestService(GenerateController = true)]` 只出现在生成器测试源（`tests/Tooling/CrestCreates.CodeGenerator.Tests/Services/{ProductService,OrderService,TestProductService}.cs`）；src/samples 零消费者 |
| `CrudControllerSourceGenerator` | `.../ControllerGenerator/CrudControllerSourceGenerator.cs:13` | 触发 Attribute `CrestCrudApiControllerAttribute`（`Domain.Shared/Attributes/…:9`）真实应用点为 0；其产物基类 `CrudControllerBase`（`Framework/Web/CrestCreates.AspNetCore/Controllers/CrudControllerBase.cs:14`）无子类 |
| `DynamicApiRouteConvention` 反射实例方法 | `src/Framework/Api/CrestCreates.DynamicApi/DynamicApiRouteConvention.cs:10,21,53,85,110,130` | 五个方法 + `ResolveReturnDescriptor` 全部零外部调用；仅静态 `IsScalar:189` 有 1 个消费者（`DynamicApiGeneratedRuntime.cs:67`）；DI 注册 `DynamicApiExtensions.cs:27` 只服务这些死方法 |
| `DynamicApiScanner` / `DynamicApiEndpointExecutor` | — | src/samples/tests/solutions 全零命中；只存在于 AGENTS.md/评审文档表述。守卫改为「禁止重新引入」 |

仍有真实消费者（必须显式隔离，不得自动 fallback）：

| 对象 | 消费者 |
| --- | --- |
| `DynamicApiAotSourceGenerator` 的 `[CrestService]` 投影分支 + `DynamicApiGeneratedRuntime`/`CompatibilityBodyReader`/`CompatibilityHttpResultMapper` | SaaSHelpdesk 与 LibraryManagement 两个样例的全部生成端点（`Program.cs` 经 `AddCrestWeb/UseGeneratedApi` 与直接 `MapCrestAspNetCoreDynamicApi`，见 `samples/LibraryManagement/LibraryManagement.Web/Program.cs:81,106`） |
| `GeneratedApiControllerAttribute`/`ApiOverrideAttribute`/`CrestApiController` | 仅 `TicketApi.cs:8-19` 一处真实消费者（迁移该点后归零） |
| 手写服务 compat 投影（`AppServiceCompatibilityGenerator`，5 个 `ModuleInitializer` 进程全局注册） | `samples/AssetManagement/.../Projections/AssetCompatibilityProjection.cs:8-9`、`samples/ProcurementApproval/.../Projections/ProcurementAppService.cs:8-9`、`tests/Framework/Api/CrestCreates.CapabilityEndpoint.AotFixture/TestAppService.cs:11`、`tests/Framework/Api/CrestCreates.CompatibilityProjection.E2E.Tests/GreetingAppService.cs:6` |
| `DynamicApiResponse<object>` | 仅两个 Host 手写 JSON context（`ProcurementHostJsonContext.cs:14`、`AssetHostJsonContext.cs:15`）；正式 CRUD 链已禁止（`CrudFormalContractProbe.cs:267`） |
| 旧 CRUD bundle（`GeneratedCrudRegistration_*` + `DynamicApiGeneratedRegistryStore.Register`，`CrudServiceSourceGenerator.cs:1148-1240`，`#warning` at `:1197`；旧 `bool ExecuteAsync` 分支 `:1360,1401,1415`） | src/samples 零消费者；仅生成器测试 harness 桩仍引用（`tests/Tooling/CrestCreates.CodeGenerator.Tests/CrudServiceGenerator/*`） |

S1 判定：R01 类「装配唯一性」修复与旧 CRUD bundle 归档互不阻塞；compat 投影的两个样例消费者在 S2 迁移到 ExplicitHost，S4 才能把 legacy analyzer 物理隔离。

## 6. 待冻结契约（S1 提案，评审通过后即为 S2 输入）

声明面（扩展现有 Attribute，不新建第二条声明链）：

```csharp
// src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/CapabilityCompatibilityProjectionAttribute.cs
// 拟新增：ContractType(Type?)、RegistrationMode(HttpProjectionRegistrationMode，默认 ExplicitHost)
// 现有 RoutePrefix/CapabilityIdPrefix 语义不变（:19,:25）
[CapabilityCompatibilityProjection(
    ContractType = typeof(ITicketAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/ticket",
    CapabilityIdPrefix = "compat.appservice.ticket")]
```

- 拟新增动作定制：`AppServiceHttpActionAttribute`（Domain.Shared，仅声明数据：Verb、相对 Route、`OperationKind`、Permissions、BodyPolicy、并发/验证声明）；枚举不引用 ASP.NET/Runtime 类型；Attribute 以 metadata full name + symbol identity 识别。
- 共享模型：拟名 `AppServiceHttpProjectionModel`（`src/Tooling/`，与 `CrudGenerationModel` 并列，CRUD 与手写服务共享同一 HTTP 发射后端；DTO/CRUD 实现仍归 `CrudGenerationModel`）。
- 前置阶段接入点：把新 target 名加入 `$(CrestCreatesJsonContractGenerationDependsOn)`（`build/CrestCreates.JsonContracts.Build.Common.props:5,11`，消费于 `…Common.targets:75`），产物落入 `@(Compile)`；复用 `WriteIfChangedFile` 与 `CrestCrudGenerationState` 同族协议，不新建 stamp 引擎、不新建第二套 assembly loader。
- Host 激活：拟名 `AddGeneratedAppServiceHttp()`（每程序集贡献 + handler module + per-Host JSON configurator），复用 `AddCrestCapabilityEndpointContribution`/`MapCrestCapabilityEndpoints`（`CapabilityEndpointExtensions.cs:58-69,84-131,179-238,251-289,300-405`）。
- 诊断：CCHTTP001–008（本仓 grep 无冲突；CEP001-022/CCCRUD/CJC 各守其 owner，不复用）：001 声明不支持；002 未物化/缺工具链；003 owner·route·action·identity 冲突；004 JSON/input/output/error 闭包；005 formal 误用 legacy 或双发布；006 transport/manifest/stamp/path 状态；007 必需 Host 装配/验证/错误契约缺失；008 动作权限/Kind/CT/并发声明矛盾。
- 失败桥接：`ICapabilityExceptionClassifier` + `CapabilityFailureKind`（`Capability.Abstractions`，transport-neutral、有界事实）；Web/Platform adapter 复用现有 `ICrestExceptionConverter`；错误 DTO metadata 归 `CrestCreates.AspNetCore` 的既有 source-generated context。R03/R04 的目标态即桥接的第一批断言（`JsonException→400`、`FormatException→400`）。

## 7. 主动收紧迁移表（写清「旧行为 → 新行为 → 影响」）

| # | 现网行为（实测） | 目标行为 | 迁移影响 |
| --- | --- | --- | --- |
| M1 | `GET /api/ticket/assign\|resolve\|close` 以 `Ticket.Search` 授权（读权限即可改工单） | 三动作显式 Command，统一 `Ticket.Update` | 只读角色失去写能力；需在权限定义 provider 内提供迁移说明 |
| M2 | `GET /api/ticket/all` 权限名 `Ticket.GetAll` | 归入冻结矩阵后仍保留该名或显式改名 `Ticket.Search`（二选一，须在 rev.2 判定并写明） | 若改名需同批迁移 grant 数据与文档 |
| M3 | 全部 `Ticket.*` 权限名无定义（R02） | 由权限定义 provider 声明，非 SuperAdmin 可授予并实测 403/200 | 样例权限面补齐；`Helpdesk.*` 与 `Ticket.*` 命名家族需在文档说明并存事实 |
| M4 | 缺 body → 域内 `ArgumentException`；空/坏 JSON → 500 | required body 的 empty/null/坏 JSON → 400，业务与写事务前拒绝、零写入 | 客户端错误码变化（500→400） |
| M5 | 非法/缺失 Guid → 500 | 400 | 同上 |
| M6 | Update 忽略客户端 stamp、陈旧覆盖返回 200 | 陈旧 stamp → 409 `Crest.Concurrency.Conflict`，零行写入 | 现有「盲覆盖」客户端必须传 stamp |
| M7 | Delete 缺 `If-Match` → 直接删除 | 428 `Crest.Concurrency.PreconditionRequired` | 无 stamp 的删除调用失败 |
| M8 | 读响应 `concurrencyStamp` 恒为空（R07） | 真实返回当前 stamp | 依赖空值的客户端转为可用 |
| M9 | `GET /api/ticket/{id}` 未命中返回 `{"code":404}` 包装；`GET /api/ticket/resolve?id=…` 未命中返回 `Crest.Entity.NotFound` | 统一为已声明 typed 404 契约，禁止普通 pipeline failure 变成空 500 | 错误体形态收敛 |
| M10 | Assign/Resolve/Close 无 CT 形参（`ITicketAppService.cs:16-21`） | 11 个契约方法统一补可选 `CancellationToken` | 二进制接口变化，需在迁移指南标注 |

## 8. S1 未覆盖项（不得当作已完成）

1. 双租户「按生产选库逻辑」的 PostgreSQL schema/database 隔离测试尚未建立：现 fixture 用同一 `_sharedConnection` 替换全部 DbContext（`HelpdeskWebApplicationFactory.cs:330-347`）并把租户基础设施替换为 NoOp（`:412-424`），因此 H04 只能由新 factory 证明。另 `Ticket` 未实现租户行接口，本次数据隔离证据限定为 per-tenant database/schema 路由。
2. `S2` 级「同轮 JSON 闭包 / ExplicitHost 激活 / 进程全局注册」的契约探针（#128 式引用程序集 symbol identity 探针 + 真实 MSBuild 前阶段复跑）未入库，留给 S2 起点；本文只记录其当前事实（compat 链仍是 `ModuleInitializer` 进程全局、组合信封由 `AddSource` 同轮产生）。
3. NativeAOT Ticket fixture（N01/N02）与守卫三层（§9）在 S4/S5 交付。
4. 样例真实宿主 `dotnet run`（本地 PG）在启动阶段长时间未监听 5199（无错误日志），未定位原因；本文的 HTTP 证据均来自 `WebApplicationFactory<Program>`（生产 Program.cs 管线 + fixture 的 DB/审计替换），因此「生产 Host 直跑」这一条仍需单独复现与归因。
