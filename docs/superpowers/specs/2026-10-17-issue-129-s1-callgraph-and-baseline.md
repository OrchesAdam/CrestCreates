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


## 复核注记（2026-10-17，保留上文作者原始记录）

PR #152 head 602d6e40 独立定向复跑6绿+8约定红/14，0 skipped。额外真实PG+manager/checker+AppService控制2/2通过：未定义Ticket.Get可授予非SuperAdmin并通过checker；只授Ticket.GetAll时真实GetAllAsync拒绝Ticket.Search。Helpdesk常量也不是已注册definition。上述原文“除SuperAdmin外无人可调用”应撤回。

生产组合静态为平台+EF模块两份binding，factory加第三份；生产进程尚未直跑成功，不能写成生产HTTP已经观测。全套既有红分类32+17+10+2=61，与62总数差一项，需FQN账本。R06只断状态、R07只断非空，尚不证明未删除和当前stamp精确一致。

目标契约以设计rev.2 §14为准：正式五权限、GetAll统一Search；Platform/Application持久化owner明确；Web404统一；User等缺失HTTP面单独记账；S1同轮闭包/Host激活/contract fixtures须补件后复审，再进入S2。当前S1未放行，CI暂停、不合并。详见2026-10-17-issue-129-s1-design-review-round1.md。

## 9. R2 补件（2026-10-17，回应 PR #152 R1 三项 P2 与账本修正）

### 9.1 撤回与更正

- 撤回 §4/§7 的「除 SuperAdmin 外无人可调用」推论。事实是：`PermissionGrantManager`/`PermissionChecker`（`src/Framework/Infrastructure/CrestCreates.Authorization/PermissionChecker.cs:40-58`、`PermissionGrantManager.cs:91-125`）不做 definition 查表，未定义名字照样能写入 grant 并被 checker 接受；`Helpdesk.Tickets.*` 也只是常量，仓库内没有任何已注册的 `IPermissionDefinitionProvider` 使用它。目录缺失由 F02 锁定，能否调用由 C11/C12/C13 用真实登录+真实 grant 锁定。
- 撤回「R02 补六个 definition 即可」的目标：`GET /api/ticket/all` 是双层门禁——端点元数据要求 `Ticket.GetAll`，继承的 `CrestAppServiceBase.GetAllAsync`（`CrestAppServiceBase.cs:207-214`）另要求 `Ticket.Search`。C11 实测：只授 `Ticket.GetAll` 时 HTTP 403 `Crest.Auth.Forbidden` 且 details 指向 `Ticket.Search`；同时授予两项才 200。按 rev.2 §14.2，正式目标为五权限且 `GetAll` 统一 `Ticket.Search`，旧 `Ticket.GetAll` 只保留在 §3 快照里，不再新增第六个定义。

### 9.2 既有失败按 FQN 归因（替换 §4 的 32+17+10+2 粗分）

方法：在同一 head 上跑三次全量并逐 FQN 取差集——① 原样（3 份 EfCore binding）；② 删除 fixture 自己那份重复装配后（2 份，rev.2 §14.3 明确 fixture 不得再声明第三个 owner）；③ 临时把 binding 归一为 1 份（仅作归因实验，未保留在代码里）。

| 观测 | 数量 |
| --- | --- |
| 全量（原样） | 70 fail / 31 pass / 101 |
| 删除 fixture 第三份声明 | 仍 70 fail（生产两份照样冲突，证明修复必须落在装配 owner，不是删测试代码） |
| 归一为 1 份 binding 后仍失败 | 18 |
| 因单一 binding 而转绿（= R01 归因） | 52（含本切片自己的 R01/U02 类红例 1 条） |

既有 62 条红因此拆为三类，全部按 FQN 记账：

- **R01 装配冲突（51 条）**，转绿证据为 ③ 与 ① 的差集：
  - `AgentApiTests`：5 条 —— `CreateAsync_ShouldCreateAgent_ReturnsAgentDtoWithRole`, `CreateAsync_ShouldCreateAgent_WithoutRole`, `CreateAsync_WithNonExistentRole_ShouldStillCreateAgent`, `DeactivateAsync_ShouldDeactivateAgent_ReturnsIsActiveFalse`, `GetByIdAsync_ShouldReturnAgent_WhenExists`
  - `CategoryApiTests`：15 条 —— `CreateAsync_ValidInput_ShouldCreateCategory`, `CreateAsync_WithParent_ShouldSetParentId`, `DeleteAsync_ShouldDeleteCategory`, `FullLifecycle_CreateUpdateMoveReorderDelete_ShouldWorkEndToEnd`, `GetByIdAsync_ExistingId_ShouldReturnCategory`, `GetChildrenAsync_EmptyParent_ShouldReturnEmptyList`, `GetChildrenAsync_ShouldReturnChildrenOfSpecificParent`, `GetListAsync_WithPaging_ShouldReturnCategories`, `GetRootsAsync_ShouldReturnOnlyRootCategories`, `GetTreeAsync_WithParentAndChild_ShouldReturnCorrectTreeStructure`, `MoveAsync_CircularReference_ShouldBeRejected`, `MoveAsync_NullParent_ShouldMakeCategoryRoot`, `MoveAsync_ToNewParent_ShouldUpdateParentId`, `ReorderAsync_ShouldUpdateSortOrder`, `UpdateAsync_ShouldUpdateCategory`
  - `CustomerApiTests`：9 条 —— `ActivateAsync_ShouldActivateInactiveCustomer`, `ActivateAsync_ShouldReturn400_WhenAlreadyActive`, `CreateAsync_ShouldCreateCustomer_ReturnsCustomerDto`, `DeactivateAsync_ShouldDeactivateActiveCustomer`, `DeactivateAsync_ShouldReturn400_WhenAlreadyInactive`, `DeleteAsync_ShouldDeleteCustomer`, `GetByIdAsync_ShouldReturnCustomer_WhenExists`, `GetListAsync_ShouldReturnPagedCustomers`, `UpdateAsync_ShouldUpdateNamePhoneCompanyAndNotes`
  - `KnowledgeBaseApiTests`：12 条 —— `CreateAsync_ReturnsCreatedArticle`, `DeleteAsync_ExistingArticle_ReturnsOk`, `GetByIdAsync_ExistingArticle_ReturnsArticle`, `GetListAsync_ReturnsPagedResult`, `GetPopularAsync_WithPositiveCount_ReturnsArticles`, `IncrementViewCountAsync_IncrementsViewCount`, `PublishAsync_SetsIsPublishedTrue`, `SearchAsync_WithEmptyKeyword_ReturnsBadRequest`, `SearchAsync_WithMatchingKeyword_ReturnsArticles`, `SearchAsync_WithNonMatchingKeyword_ReturnsEmptyList`, `UnpublishAsync_SetsIsPublishedFalse`, `UpdateAsync_ExistingArticle_UpdatesTitleAndPersists`
  - `SLAPolicyApiTests`：7 条 —— `CreateAsync_WithAllPriorityLevels_ShouldReturnCreatedPolicy`, `DeleteAsync_NonExistingPolicy_ShouldReturnError`, `DeleteAsync_WithValidConcurrencyStamp_ShouldRemovePolicy`, `DeleteAsync_WithoutConcurrencyStamp_ShouldStillRemovePolicy`, `GetAllAsync_ShouldReturnAllPolicies`, `GetByIdAsync_ExistingPolicy_ShouldReturnPolicy`, `UpdateAsync_ExistingPolicy_ShouldApplyChanges`
  - `TicketApiTests`：3 条 —— `CreateAsync_ShouldCreateTicketAndReturnDto`, `DeleteAsync_ShouldRemoveTicket`, `UpdateAsync_ShouldUpdateAllFields`
- **框架服务缺 HTTP 面（10 条）**：`POST /api/user` 返回 404，`CrestCreates.Application` 没有 `DynamicApiAotSourceGenerator` 产物；按 rev.2 §14.6 单列，不在 #129 顺带开通 User/Setting/Feature/Audit 的 HTTP 面，也不通过改期望或删测试凑绿：
  - `AssignAsync_WithActiveSlaPolicy_ShouldSetDueDateAndAssign`, `AssignAsync_WithoutSlaPolicy_ShouldAssignWithoutDueDate`, `CloseAsync_AlreadyClosed_ShouldThrowInvalidOperationException`, `CloseAsync_NotResolved_ShouldStillSucceed`, `CloseAsync_ShouldCloseResolvedTicket`, `GetByAssigneeAsync_ShouldReturnAssigneeTickets`, `GetOverdueAsync_ShouldReturnOverdueTickets`, `ResolveAsync_AlreadyClosed_ShouldThrowInvalidOperationException`, `ResolveAsync_AlreadyResolved_ShouldStillSucceed`, `ResolveAsync_ShouldResolveOpenTicket`
- **容器构建校验（1 条）**：`UseAuthorization_ShouldProcessRequest_WhenAddCrestAuthorizationRegistersServices` —— `AggregateException: Unable to resolve service for type 'CrestCreates.MultiTenancy.Abstract.ITenantProvider'`（`TenantPermissionScopeValidator` 依赖链）。
- 差额已闭合：`51 + 10 + 1 = 62`，不存在「凭 HTTP 400 推断同根因」的未归因项；此前的 19 条无响应体 400 全部落在 R01 归因集合内（由 ③ 转绿事实支撑，而非由状态码推断）。

### 9.3 红例判别力增强（回应 R1-HTTP129-03）

- `TicketWriteSideEffectProbe`（`samples/SaaSHelpdesk/SaaSHelpdesk.Tests/TicketWriteSideEffectProbe.cs`）在测量宿主里装饰 `ITicketAppService` 与 `IUnitOfWorkManager`，记录业务调用名与内核执行次数；计数只来自测试侧装饰器，生产未新增任何测试专用公共扩展点。
- R03/R04/R08 现同时断言状态码、业务调用集合为空（或不含被拒动作）、`UnitOfWorkExecutions`、以及 fresh-scope 的持久化行数/内容不变；R08 今天正是因 `ServiceCalls == ["CreateAsync"]` 而红，直接证明校验器未执行。
- R05 增加「原标题与原 stamp 在 fresh scope 精确不变」；R06 增加「行仍在 + stamp 不变 + 再次请求仍是 428」（消除「删完再返 428」的假绿）；R07 改为「响应 stamp 与数据库当前 stamp 精确相等」，并在真实修改后要求等于新值。
- 正向控制 `P09/P10`（今天即绿）证明这套计数器确实能观测到业务调用、内核执行 1 次、stamp 轮转与删除生效；否则 R05/R06/R08 的空计数断言可能是假绿。

### 9.4 装配来源证据（回应 R1-HTTP129-05 项）

- `U01`：只调用样例持久化模块即可得到恰好 1 份 EfCore binding（应用自持能力成立）。
- `U02`（红）：按程序集元数据引用 `AddEfCoreUnitOfWork` 的装配实测为 `CrestCreates.Web` 与 `SaaSHelpdesk.EntityFrameworkCore` —— 即生产组合确实两份；目标是恰好一个 owner（PlatformDefault/ApplicationOwned）。这不是靠注释或计数猜测，而是读 MemberRef。
- `U03`（绿）：测试程序集不再引用绑定声明 API（fixture 的第三份声明已按 rev.2 §14.3 删除）；`U04`（绿）要求宿主仍可见 >1 份 binding，防止用「删除测量路径」冒充修复。
- R01 的目标计数仍为 1，当前观测为 2（历史上是 3，其中一份来自 fixture，已删除）。

### 9.5 仍未交付（不声称完成）

- R1-HTTP129-02 要求的同轮闭包 / Host 激活 / contract fixtures 见 §10 状态；双租户选库 factory 与真实生产进程启动根因按 rev.2 属 S3。


## 10. R3 修正（2026-10-17，回应 09f73642 复审的三项 P2 与跨 Host 控制）

1. 同轮探针归因错误（P2）：探针原先把信封写成 `ProbeHandwritten.OrderAppService_Search_CompatibilityInput`，而 emit 端真实命名空间是 `CrestCreates.Generated`（`AppServiceCompatibilityEndpointEmitter.cs:43,135`）。错误 FQN 使失败原因是普通名称解析（CS0234），不是同轮不可见。现在：
   - 引用正确 FQN，并新增反向守卫——authored 文件上不得出现点名该信封的 `CS0234/CS0246`；
   - 诊断采集覆盖 warning 级（`SYSLIB1030` 是 STJ 的 warning，旧过滤器只看 `: error` 才导致「无诊断」假象）；
   - 同一个 context 里同时声明一个普通 Compile 根 `ProbeOrdinary.OrdinaryEnvelope`，因此「官方生成器在本次构建确实运行」由同一次构建内的 `JsonTypeInfo<global::ProbeOrdinary.OrdinaryEnvelope>` 证明，而不是靠另一个测试；信封本身仍无 metadata，且必须点名 `SYSLIB1030`；
   - producer 预检额外要求信封确实落在 `namespace CrestCreates.Generated`。
   - 三次控制共享两次冷构建（类别内 `IAsyncLifetime`），并与 CRUD 探针同串行集合；单跑时长上限 20 分钟。
2. `U02` 误判 owner（P2）：MemberRef 只说明「谁调用绑定声明 API」，不能说明最终图里有几份。`U02` 改为事实控制（绿），逐字列出实测两个装配点；真正的不变量交给 `R01`/`U04`（宿主最终解析出的绑定数）与新增 `U05`：同一 provider 两份绑定在构建索引时确定性拒绝、单绑定通过、空图在 `GetRequired` 处拒绝（保留 #137 语义，不改 first-wins）。修好装配后 `U02` 仍可绿（只少一个调用点），不会像原断言那样永远红。
3. `P09` 不是合法正例（P2）：请求体现在携带从数据库 fresh-scope 回读到的当前 stamp（先断该值非空，避免空值让控制变成假绿），并断：仅 `UpdateAsync` 一次业务调用、恰好一次受管执行、行数不变、标题落库、存储 stamp 轮转；再用轮转后的 stamp 发第二次更新，断 stamp 再次轮转（证明返回给客户端的 stamp 可用于下一次前置条件）。`P10` 同步改为真实 `If-Match`、仅一次 `DeleteAsync`、一次受管执行、行数减一、行与 stamp 均消失。
4. 跨 Host 激活控制（复审另需项）：新增 `Projection_activation_is_process_global_and_has_no_host_scoped_seam`，在同一真实构建的生成语法树上按 AST 取证——`[ModuleInitializer]` 方法体内直接写入 `DescriptorProviderRegistry.Register` / `CapabilityEndpointBindingRegistry.Register` / `CapabilityHandlerResolverProvider.Register` 等进程全局注册表（≥4 处），且整个生成输出不含 `ICapabilityEndpointContribution`/`AddCrestCapabilityEndpointContribution`，即宿主无处可拒绝。该条记录的是「注册不受 Host 作用域」的当前机制；正式的 `AddGeneratedAppServiceHttp()` 拒绝式激活由 S2 发射并配相应红例，本轮不提前断言尚不存在的 API 名称作为唯一失败原因。

5. R3 复核结果（实测）：三条控制共用单次真实构建 3 pass / 3（15 分 9 秒）——负例 `SYSLIB1030` 点名信封、authored 文件不含点名该信封的 CS0234/CS0246、STJ 无其 metadata；同次构建内普通 Compile 根由 STJ 关闭；`[ModuleInitializer]` 内向 ≥4 个进程全局注册表写入且无可拒绝的 Host 接缝。样例侧全量 41 pass + 71 fail / 112（约定红 9，既有红 62，新增回归 0）。

6. `CrestCreates.CodeGenerator.Tests` 全量在最终 head `214426ea` 复跑：459 pass / 0 fail / 459（22 分 30 秒），无 skipped；上一 head 的唯一失败（每条控制各建一次导致的超时）已随单次构建合并消除。
