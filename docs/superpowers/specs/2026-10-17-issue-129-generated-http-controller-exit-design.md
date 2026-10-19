# #129 旧 Controller 退出与正式 HTTP 投影收口 — 详细设计 rev.2

## 1. 状态、基线与核心决策

- Issue：[#129](https://github.com/OrchesAdam/CrestCreates/issues/129)，总控 #121，前置 #128。
- 已核对 GitHub：PR #150 已合并；当前远端 master 为 **196b6f66048ef32d644eeb856d00d58ae9d34965**。本设计依据该提交源码，不依据本会话较旧的本地 master。
- 状态：**S1 已提交 PR #152（head `602d6e40`），事实稿已复核；S1 退出门禁尚未通过，S2 未获放行**。rev.2 根据 S1 事实修订，§14 为新增的明确决策与补证要求。下文新增名称均是拟定契约，不声称今天已经存在；合并权限、暂停 CI 的安排不变。
- 方案：保留 Ticket 的领域模型、手写 AppService、既有业务接口；将 HTTP 声明转为**有明确契约范围的编译期投影 → 普通 Compile 产物 → JsonContracts → 官方 STJ → 本 Host 显式贡献 → 既有 Capability 管线**。
- 复用 #128 的前置生成设施、ownership、强类型 body/result、Host activation、精确版本校验与 JSON 来源校验；不再为 Ticket 发出 legacy provider，也不复制 Dispatcher/UoW/权限/租户系统。
- 完成 Controller 退出，归档无消费者的反射辅助代码和旧 CRUD bundle；仍有真实消费者的 legacy Minimal API 必须物理隔离、显式装配、限定支持与退出清单，不能作为正式入口失败时的 fallback。

## 2. 已确认的代码事实

| 事实 | 对设计的约束 |
| --- | --- |
| TicketApi 继承的 CrestApiController 是普通类，未继承 MVC ControllerBase；DynamicApiAotSourceGenerator 消费 GeneratedApiController/ApiOverride 并生成 Minimal API | 本任务不是把正在运行的 MVC Controller 换个语法；退出的是旧声明、旧投影/注册/JSON 辅助链，必须检查最终 endpoint 来源 |
| TicketApi 只重写 GET api/ticket/all，其他动作来自 TicketAppService 的生成面 | 不能只删除一个文件后声称所有 Ticket 端点已迁移 |
| Ticket 有 protected 无参构造、私有 setter 和 SetTitle/AssignTo/Resolve/Close 等领域方法 | 不适用 #128 默认自动 CRUD mapper；不将 setter 改成 public，不生成第二个等价 CRUD service |
| 手写 AppServiceCompatibilityGenerator 仍通过多个 ModuleInitializer 注册 descriptor/binding/invoker/result/manifest；组合输入信封由 SG AddSource 产生 | 单加 CapabilityCompatibilityProjection Attribute 不能得到 #128 的 ExplicitHost 和同轮 JSON 闭包；需补通用手写服务投影前置阶段 |
| TicketAppService 的 Assign/Resolve/Close 当前没有 CT 参数；默认 HTTP convention 将其投影为 GET | 不能按 GET 自动归类为 Query；CT/UoW/权限须逐动作声明与验证 |
| 旧 convention 对这些动作落到 Ticket.Search，业务方法自身也未显式检查 Update 权限 | 不把现有权限缺口固化成兼容要求；三动作收紧为 Ticket.Update，并在迁移表明示 |
| CapabilityPipeline 会把普通业务异常转成 PIPELINE_ERROR，统一 mapper 默认产生 500；sample 原测试期望 InvalidOperationException 为 400 | 不能依赖外层 ExceptionHandlingMiddleware 捕获已被吞掉的异常；需复用已有错误权威并补管线桥接 |
| DynamicApiRouteConvention 实例方法使用反射，但当前仓库消费者只发现 DynamicApiGeneratedRuntime.ValidateAsync 调用静态 IsScalar | 归因为残留辅助 API，不能继续描述为正式路由依赖运行时 Attribute 扫描 |
| src 中未发现 DynamicApiScanner / DynamicApiEndpointExecutor 实现 | 不再把已退出实现列为待删除工作；新守卫应阻止重新引入 |
| HelpdeskDbContext 按 ICurrentTenant 的 ConnectionString 选库；现有测试 factory 将其替换为同一个 shared connection | 必须新增保留生产选库逻辑的双租户测试，不能用现有单连接测试推导数据库隔离 |

证据入口：samples/SaaSHelpdesk/{SaaSHelpdesk.Application/GeneratedApi/TicketApi.cs,SaaSHelpdesk.Application/Services/TicketAppService.cs,SaaSHelpdesk.Application.Contracts/Interfaces/ITicketAppService.cs,SaaSHelpdesk.EntityFrameworkCore/Modules/EntityFrameworkCoreModule.cs,SaaSHelpdesk.Tests/TicketApiTests.cs}；Tooling 的 DynamicApiGenerator/AppServiceCompatibilityGenerator/CrudServiceGenerator；DynamicApiRouteConvention.cs、CapabilityPipeline.cs、DefaultCrestExceptionConverter.cs。

## 3. 范围与交付边界

必须完成：

1. Ticket 全部已声明业务动作的真实路由、输入、响应、权限、租户、取消、UoW 和错误行为迁移。
2. 手写 AppService 使用通用 ExplicitHost HTTP 投影与自动 JSON 闭包，不手写 Ticket endpoint map、JSON root 表、invoker 或字符串 binding。
3. 主 Host 提供正式 capability HTTP 装配；legacy 装配必须使用明确名称及依赖，缺少正式贡献时确定性失败。
4. Controller generators、GeneratedApiController/CrestApiController/ApiOverride 路径退出，旧 CRUD stub bundle 退出，死反射 API 归档。
5. 新的真实构建、集成、NativeAOT、架构守卫、迁移指南及 #123 evidence matrix。

不顺带做：全量重写 SaaSHelpdesk 业务；将所有 Entity 改成自动 CRUD；给 ORM/AOT 整体背书；通用动态查询 DSL；重新实现身份/权限/租户/事务；移除健康检查等独立功能的合法 MVC 入口；为了“零 legacy 字符串”盲删仍有消费者的公共 API。

## 4. 正式声明与唯一生成链

### 4.1 声明面

扩展既有 CapabilityCompatibilityProjectionAttribute 的**正式模式**，拟新增 ContractType、RegistrationMode，默认新项目使用 ExplicitHost；旧静态模式只由隔离 legacy 工具链承接。迁移已有无 mode 声明时给明确诊断并完成仓库内消费者转换，禁止默默保留全局注册。

```csharp
[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(ITicketAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "/api/ticket",
    CapabilityIdPrefix = "compat.appservice.ticket")]
public class TicketAppService : CrestAppServiceBase<...>, ITicketAppService
{
    // 领域动作与已有映射继续留在这里。
}
```

新增仅含声明数据的动作定制 Attribute，拟名 AppServiceHttpActionAttribute，位于 Domain.Shared：Verb、相对 Route、OperationKind(Query/Command)、Permissions、BodyPolicy、必要的并发/验证声明。Verb 枚举和 Kind 不引用 ASP.NET/Runtime 实现；Attribute 通过 metadata full name + symbol identity 识别，不匹配短名。动作定制可放契约方法；实现方法同项不同值则诊断，不按发现顺序覆盖。

ContractType 有两种正式范围：

1. **接口契约**：可访问闭合接口（允许位于引用程序集），目标投影类必须实际实现它。只枚举该接口及其继承接口的正式方法；不默认扫实现类的其他 public 方法或所有其他接口。Ticket 的 11 个契约方法是首组冻结集合，GetList/Search/Count 等未在该契约声明的基类方法不得意外暴露。
2. **自契约**：ContractType 指向投影类自身（非 static）。只枚举该类显式标注 [AppServiceHttpAction] 的 public 实例方法；未标注的 helper/上下文访问器永不暴露，不要求新增空转 facade。Asset/Procurement 的迁移使用此模式。

两种模式下业务服务都只由其 owner（模块/CrestService/显式注册）注册一次；Host 装配期在映射任何端点前校验该注册（CCHTTP007），缺失注册是启动期失败，不是首个请求的 500。HTTP 激活不注册、不替换、不生成第二个业务实例。

Capability/endpoint identity 默认包含 owner assembly + service full metadata name + action；现有 ID 有消费者时显式保留 prefix。组合输入类型名必须包含完整 owner identity 的确定性编码，不能只用 TicketAppService_Update 短名；同短名跨 namespace/程序集、方法重载、路由冲突均需可定位诊断。

### 4.2 前置物化

将现有 compatibility convention/model/emitter 与 #128 共用 HTTP 产物部分提取为 shared-source 模型，拟 AppServiceHttpProjectionModel。CRUD 是其中一个输入前端，手写服务是另一个输入前端；DTO/CRUD 实现仍由 CrudGenerationModel 负责，不再复制一套 HTTP 发射规则。

流程固定为：

```text
SDK source/global usings/references
 → 已有 CRUD precompile（存在正式 CRUD 时）
 → 手写 AppService HTTP projection precompile
 → 统一的普通 Compile 投影产物及 ownership manifests
 → JsonContracts 发现完整 body/input/payload/result/error 契约
 → 普通 context/roots 源码
 → 同一次 CoreCompile：官方 STJ + 其他 SG
 → per-Host 显式激活与 Map 期校验
```

新前阶段复用现有 Task/CLI 隔离进程协议、输入 fingerprint、原子 write-if-changed、失败不记成功 stamp、路径边界、design-time/TFM/RID 分区与 packaged transport。可以扩展已有工具命令/公共 targets，不能建立第二套反射 assembly loader 或复制 stamp 引擎。顺序必须显式接入 CrestCreatesJsonContractGenerationDependsOn，不能依靠 BeforeTargets 的偶然先后。

物化内容：组合 input envelopes、capability/endpoint 描述、typed binder/invoker/result、required identities/JSON roots、handler module、程序集激活、投影 manifest。手写 service/interface/DTO 不由该阶段再生成一份；业务 DI 继续由既有模块/CrestService 注册，HTTP 激活不夺走业务 DI owner、不生成第二个实例。

现有 AppServiceCompatibilityGenerator 对被 manifest 拥有的正式声明只诊断/跳过；不能再次 AddSource 投影或 ModuleInitializer 注册。DynamicApiAotSourceGenerator 的 legacy 前端也必须按具体 owner/action ownership 跳过，不能因一个 Feature 激活而关闭整程序集其他真实 legacy 服务。

### 4.3 自动 JSON 闭包

必需 roots 从 action model 推导：CreateTicketDto/UpdateTicketDto、实际返回 TicketDto、IReadOnlyList<TicketDto>、List<TicketDto>、所有 route/query/body/header 组合信封、DynamicApiResponse<TicketDto>、两种实际静态 list 的 typed envelope、成功 void envelope，以及 Web 所有的资源不存在/失败 DTO。旧 404 envelope 只留在 legacy 快照，不是正式 Ticket 404 的第二条输出。

不能把 IReadOnlyList 与 List 当作同一静态契约，不注册 object envelope；无输入动作使用明确 no-input 语义，不引入任意 object 输入。单 Guid 输入若运行时 SerializeToElement 会用到，同样纳入 root 需求。

失败 DTO 由其正式所有者提供：既有 Web 的 CrestErrorResponseJsonContext 与 Feature context 分别声明所需契约，Host 显式组合并校验，避免 Feature 为错误 DTO 反向依赖 Web。#128 的 ProblemDetails 路径仍按其既有声明维护；不能假定一个 root 表适合所有错误 mapper。

最终 Http.Json.JsonOptions 是唯一来源。复用 R1/R2 已修复规则：框架公共 roots 的 Host 级可信归属、private root 属主约束、零 modifier 的精确默认 resolver 才作为 fallback、显式 modifier/派生/wrapper 冲突拒绝、缺 root 不被反射补齐。校验早于首个请求，失败不永久缓存 ready。

## 5. Ticket 路由、业务与兼容矩阵

下表是 rev.2 冻结的**目标契约**。S1 的旧链快照另外保存，不随新实现改写。现状 `GET /all` 的端点元数据要求 Ticket.GetAll，但真实服务另要求 Ticket.Search；目标显式统一为 Ticket.Search（§14.2）。Assign/Resolve/Close 从 Search 收紧为 Update，属于明示行为迁移。测试注释不是路由证据。

| 动作 | 保留的 method/route | 输入 | Kind / 权限 | 成功结果 |
| --- | --- | --- | --- | --- |
| CreateAsync | POST /api/ticket | required CreateTicketDto | Command / Ticket.Create | 200 + typed TicketDto envelope |
| GetByIdAsync | GET /api/ticket/{id} | Guid route | Query / Ticket.Get | 200 typed；不存在 404 void envelope |
| GetAllAsync | GET /api/ticket/all | CT，无 body | Query / Ticket.Search | 200 + IReadOnlyList<TicketDto> envelope |
| UpdateAsync | PUT /api/ticket/{id} | route + required UpdateTicketDto | Command / Ticket.Update | 200 + typed TicketDto envelope |
| DeleteAsync | DELETE /api/ticket/{id} | route + If-Match | Command / Ticket.Delete | 200 void envelope |
| AssignAsync | GET /api/ticket/assign?id=…&agentId=… | 两个 required Guid query | **Command / Ticket.Update** | 200 + typed TicketDto envelope |
| ResolveAsync | GET /api/ticket/resolve?id=… | required Guid query | **Command / Ticket.Update** | 同上 |
| CloseAsync | GET /api/ticket/close?id=… | required Guid query | **Command / Ticket.Update** | 同上 |
| GetByCustomerAsync | GET /api/ticket/by-customer/{customerId} | Guid route | Query / Ticket.Get | 200 + List<TicketDto> envelope |
| GetByAssigneeAsync | GET /api/ticket/by-assignee/{assigneeId} | Guid route | Query / Ticket.Get | 同上 |
| GetOverdueAsync | GET /api/ticket/overdue | CT，无 body | Query / Ticket.Search | 同上 |

三个 GET 写动作是**既存兼容路由**，不能新扩散这种 convention；本 issue 不擅自改成 POST 或通过 redirect 改变调用。显式标为 Command，走写权限/UoW、禁止缓存，记录在兼容路由退出表；后续 verb 改版需单独下游迁移，不能在此任务静默进行。

保留领域行为：Assign 的 SLA DueDate 计算；已 Closed 时 Resolve/Close 拒绝；重复 Resolve 的当前允许语义；未先 Resolve 仍可 Close 的当前允许语义；Customer/Assignee/Overdue 查询条件；DTO 命名、namespace、枚举 wire、nullability、camelCase、list shape、状态字段。

明确的安全/正确性收紧须写进迁移表：

- Assign/Resolve/Close 从旧 Search 推导收紧为 Ticket.Update；已有只读用户不能因拥有 Search 而修改工单。
- required body 的 empty/whitespace/null/坏 JSON → 400，零业务/零写入；必需 route/query 缺失或非法 Guid 同样 400。
- Ticket 为 IHasConcurrencyStamp；读/创建/更新 DTO 必须真实返回当前 stamp。Delete 缺 If-Match → 428；stale stamp 按既有并发异常契约拒绝，不删除任何行。
- Update 不得“看起来支持 stamp，实际上忽略客户端值”。S1 先落 stale update 的纯净负例；通过既有 repository/provider 与 typed DTO 契约补齐。若需改 CrestAppServiceBase/Entity DTO 契约，使用通用 typed stamp accessor/interface，禁止反射 GetProperty 或 Ticket 专属事务执行器。缺 stamp 的目标策略在本设计中为并发实体拒绝，作为明示迁移项，不偷偷沿用 blind overwrite。

## 6. Host 装配、权限、验证、租户与 UoW

### 6.1 显式装配

每程序集生成拟名 AddGeneratedAppServiceHttp() 的 Host 扩展，注册贡献、handler module、JSON configurator 与必要的 typed validation 胶水。重复调用幂等；正式 HTTP 与业务 service 注册分工明确。复用 AddCrestCapabilityEndpointContribution/MapCrestCapabilityEndpoints，不新建 dispatcher，不增量修改 frozen registry。

Platform Web 正式模式必须显式接上 capability runtime 与 endpoint mapping，沿现有 UseCrestWeb 的认证、租户、审计顺序运行；不能把 fixture 的空 PipelineBuilder/RemoveAll validators/bootstrap 方案移植进真实 sample。未请求激活的 Feature 保持无路由；只有 Host 明确声明需要该 Feature、却缺贡献时才启动拒绝，不能把“程序集已加载”解释为必须激活。

既有 UseGeneratedApi/AddCrestAspNetCoreDynamicApi/MapCrestAspNetCoreDynamicApi 指向 legacy 的事实必须在 API 上说清楚。正式入口不得以“没找到 provider/未 ready/缺 JSON metadata”为理由调用它们。新 Host 的默认模式不自动暴露所有已加载 AppService。

### 6.2 权限与验证

由现有权限服务和 Capability authorization middleware作权威校验；Ticket 全部正式端点声明 RequireAuthenticated，不靠业务方法抛异常替代 401。声明 permissions 来自冻结矩阵；正式 Ticket 使用 Ticket.Create/Get/Search/Update/Delete 五个已注册定义（§14.2），不继续产生 Ticket.GetAll。禁止 Ticket handler 手动读 token/claim 或静默忽略缺 permission checker。业务域操作原有校验继续执行。

Create/Update 使用现有 validators 的**编译期 closed typed 调用/注册**；必要时给现有 IValidationService 增 additive CT overload，旧 overload 委托它，不新造验证引擎。ValidationMiddleware 在 InputSchema=null 时放行，不构成已执行 FluentValidation 的证据。正式 Ticket 写动作的必需 validator 缺失应在装配阶段诊断，不能以缺服务即成功代替。

Host 不以 AddValidatorsFromAssemblyContaining 的运行时扫描作为新链 NativeAOT 证据。由现有编译期注册设施或本前置模型生成 typed IValidator<T> 注册；构造/DI 缺口先诊断，不让普通 Feature 手写五套基础设施。

### 6.3 租户

复用现有租户解析、ICurrentTenant/ITenantContext、认证主体与 TenantBoundary，不另造 TenantName/Header 权威。测试使用解析后的规范 TenantId，不能靠测试自己把一个随机 Guid 当成当前租户。

必须证明 token 租户与请求租户不一致、未知/禁用租户在业务前拒绝，且正常请求在 pipeline/service/UoW/provider 观察同一上下文。另以两个已登记租户的**不同 PostgreSQL schema/database 连接**验证选库与工单读写隔离，保留生产 DbContext options contributor；现有 shared connection factory 的测试只作为单租户行为测试。

Ticket 未直接实现租户行接口，Customer 的 Guid 列不能冒充新的规范上下文键。本次数据隔离证据限定为真实 per-tenant database/schema 路由，不推导“同一库中任意 Ticket 行自动过滤”。若 S1 发现生产路由或默认连接 fallback 允许未知租户落入 host 库，这是本链必须拒绝的装配缺口，不能用过滤结果为空掩盖。

### 6.4 CT/UoW

所有 11 个业务契约补齐 optional CancellationToken 并向仓储/SLA 查询/领域编排传递；现有不传 CT 的源码调用仍可编译，二进制接口变化在迁移指南明示。不能保留 CancellationToken.None 或复制请求 token 到 global 字段。

Create/Update/Delete + Assign/Resolve/Close 共六个写动作均使用 #137 唯一内核；明确一个事务 owner。已有基类织入方法不再套第二个自实现事务；新领域写动作通过现有 UnitOfWorkMo/受管执行入口和同一 ServiceProvider 桥接，缺 manager/provider 能力前置拒绝。

验证真实织入与提交/回滚计数，不能靠 Attribute 文本证明事务。响应写失败不回滚已提交业务；请求中止/受管 deadline 有界终止，保留 committed/unknown/rollback 等事实，不将取消包装为 JSON 失败后重执行业务。

## 7. 失败响应平台桥接

保留既有 DefaultCrestExceptionConverter 的错误码、认证区分、本地化、trace 和脱敏权威。目标：400 参数/验证/非法领域操作；401 未认证；403 无权限/租户边界；404 资源不存在；并发/前置条件按既有 4xx 契约；未知错误 500 且不泄露堆栈/内部消息。目标态的资源不存在统一由 Web 权威输出 CrestErrorResponse（404、Crest.Entity.NotFound），包括 GetById 返回 null 与业务抛 KeyNotFoundException；成功/void envelope 保持 typed DynamicApiResponse。旧 GetById 的 DynamicApiResponse 404 形状属于迁移项，不能同时宣称两种正式 404 形状。不得被普通 pipeline failure 改成空 500。

不能在 Ticket invoker 复制一份 catch-switch，也不能认为外层异常 middleware 能接住 PIPELINE_ERROR。采用小型平台桥接：

1. 在 Capability.Abstractions 定义 transport-neutral 的异常分类扩展点与有界 fault facts（拟名 ICapabilityExceptionClassifier/CapabilityFailureKind）；Runtime 只消费它，仍不引用 Api/Web/ORM。
2. Web/Platform adapter 复用已有 ICrestExceptionConverter，对 HTTP invocation 的已知异常保留安全错误事实；其他 transport 不借用伪造 HttpContext。结果不携带对外序列化的原 Exception、Type 或堆栈。
3. CapabilityEndpoint 的 HTTP failure mapper 消费这些事实并由既有 Web 错误呈现逻辑输出 CrestErrorResponse；普通 native capability 的既有错误映射不被强制改为 Ticket 格式。
4. CrestErrorResponse 的 metadata 由现有 Web source-generated context 提供，并作为 Host 必需 error contract 验证，不要求 Feature 引用 Web DTO。失败路径自身也必须在无反射 Host 可写出。

桥接的冻结形状与 code/status 对照见 §14.5，S1 仍须提交引用真实契约的判别探针；桥接必须配现有异常 converter 的同输入等价测试及 unknown-failure 脱敏负例，不成为任意 HttpStatusCode 向 Runtime 泄漏的通道。

## 8. Legacy 隔离与归档清单

### 8.1 确定归档

- samples/SaaSHelpdesk/SaaSHelpdesk.Application/GeneratedApi/TicketApi.cs：其 all 行为并入正式声明，旧声明不再参与编译。
- ControllerSourceGenerator、CrudControllerSourceGenerator：核对无真实 GenerateController/CrestCrudApiController 消费者后归档实现与维护其旧输出的测试。两者 Obsolete 提示中的 GeneratedApiController 也要退出，不能保留过时推荐。
- DynamicApiAotSourceGenerator 的 GeneratedApiController/ApiOverride/controller registration 分支及仅供它的 runtime abstractions：迁移唯一真实 consumer 后归档，不带入新的 legacy analyzer。
- CrudServiceSourceGenerator 旧无参 stub/bundle/provider/旧 bool ExecuteAsync 发射分支及专用旧测试：#128 正式声明诊断入口保留；#warning 只随该旧分支真实退出而移除，不能整类删除后丢失前阶段缺失诊断。
- DynamicApiRouteConvention：移出唯一活消费者 IsScalar 后，归档实例反射方法、相关 descriptor 辅助类型（仅在零消费者时）、多余 DI 注册。

归档路径建议 `99_RecycleBin/issue-129-controller-exit/<原相对路径>`，附每项调用图、替代入口及测试迁移说明。归档文件从 Compile/analyzer/package glob 显式排除；不能只是挪目录后仍被 SDK 编译或 analyzer 自动发现。

### 8.2 有消费者的 legacy Minimal API

从正式运行时/默认 analyzer 提取到明确的兼容项目/包，冻结为 CrestCreates.DynamicApi.Legacy 与 CrestCreates.CodeGenerator.Legacy；Platform 的旧 Add/Map 装配放在 CrestCreates.Web.Legacy，不让正式 Web 映射无条件调用它。依赖约束见 §14.4。新 analyzer 仍遵守 netstandard2.0/PrivateAssets 的分发要求，并从根目录默认 runtime Trim/AOT 配置及自动 analyzer ProjectReference 中正确排除，避免自引用、互相引用和运行时依赖渗漏。

保留对象仅限调用图证明仍使用的旧 AppService generated provider、registry/runtime helpers/body reader；不重新保留已无消费者的 MVC/controller 生成器。非反射 scalar helper 的唯一基线消费者是 legacy ValidateAsync，因此先放 legacy 内部有限类型分类器，不为“共享”向 Core 添加公共基础设施 API。Type flags/有限已知类型判定不等于成员扫描；正式路由仍由 Roslyn symbols 决定。

compatibility mapper 的旧无 JsonTypeInfo overload 若需保留，放 legacy owner；正式 typed overload/envelope factory 保持共享权威。不得让正式包反向引用 legacy 包造成环；已有外部旧签名按迁移指南明确 breaking/obsolete 处理，不留自动 fallback shim。

旧代码只经明确的 Add/MapLegacyGeneratedApi 及显式 analyzer/package 引用激活；默认工具链不把加载 legacy DLL 当 opt-in。旧 API 名称可提供短期 obsolete 转接，但只在 legacy package 内，且缺 provider 继续 fail-closed。支持标注 JIT-only/未验证；没有 trim 门禁就不写 trimming-verified。

SaaSHelpdesk 其他仍需 legacy 的服务允许短期共 Host，但要逐 owner/action 记录允许清单；Ticket 及新 formal fixture 全部排除。不能按整个 sample、整个 namespace 或整个 DynamicApi 程序集豁免守卫。重复 method/route、formal/legacy 对同动作双发布在启动前拒绝，不能靠 Map 调用顺序覆盖。

若生产手写服务（如 Asset/Procurement）已使用 compatibility projection，迁移到新的 ExplicitHost 默认，不增加 static/global 模式作为第二条正式链。代码位于业务 Application 而无 HTTP 依赖的服务不应因此被迫引入 legacy 包；必要的 HTTP 投影胶水留在现有 Host/Feature HTTP owner 中。

## 9. 架构守卫与诊断

新增 CCHTTP001–008（本次盘点未发现冲突，实施前复核登记）：001 声明/contract 不支持；002 未物化/缺工具链；003 owner/route/action/identity 冲突；004 JSON/input/output/error 闭包；005 formal 误用 legacy 或双发布；006 transport/manifest/stamp/path 状态；007 必需 Host 装配/验证/错误契约缺失；008 动作权限/Kind/CT/并发声明矛盾。已有 CEP/CCCRUD 诊断各守其 owner，不覆盖或复用不同含义。

守卫分三层：

- **依赖层**：正式 Runtime/Tooling 方向不变；Runtime 不引用 Web/Api；formal package/fixture 不依赖 legacy 包；legacy 单向依赖共享契约，默认 analyzer package 不包含 legacy generator。
- **生成/符号层**：正式投影产物不得调用 DynamicApiGeneratedRuntime、CompatibilityBodyReader、无 JsonTypeInfo 的 serializer/Results.Json、DynamicApiResponse<object>、旧 bool UoW、runtime scanner/executor/controller。按 Roslyn symbol/metadata reference 与产物 owner 判断，不以注释/短名/字符串 contains 作为唯一证据。
- **运行层**：EndpointDataSource 的规范 method/route 恰好一次；来源为正式贡献；不存在旧 Ticket provider/controller registration；缺激活、缺 metadata、frozen registry 不等价均拒绝且不留下半套路由。

允许清单必须写清具体项目/服务/理由/退出任务。增加新 formal 入口命中旧链时测试必须红；legacy 项目只隔离，不反向牵引正式 API 设计。

## 10. 验收矩阵

| ID | 必须证明的事实 |
| --- | --- |
| I01 | 在最新 master 上建立 authored → generator/Task → provider → Host → route → runtime helper 调用图；区分声明、注册、实际执行 |
| I02 | Ticket 11 动作的旧链 route/permission/DTO/error 快照，区分端点元数据与业务二次权限；分别标记原 factory、归一化 factory 和生产进程证据；纠正注释的 GET/POST、list/page 混淆 |
| B01 | 手写服务组合信封是 ordinary Compile；官方 STJ 与其他 SG 同轮可见，不用串行 driver 或预填 obj |
| B02 | 正式 DTO/result/list/error roots 元数据真实存在；缺单 root、同名假类型、错误 owner/hash、非生成 resolver 均拒绝 |
| B03 | clean/no-op/改名/移除声明/坏输出修复/失败无成功 stamp/design-time/多 TFM·RID/外部包首消费，用扩展后的正式前阶段复跑 |
| B04 | 同短名服务、继承泛型接口、重载、接口实现声明冲突、referenced DTO/service 场景不会串 owner 或多生成 |
| H01 | Ticket 全矩阵正确，GetAll 替代旧 ApiOverride，未声明基类方法不被自动暴露 |
| H02 | 缺 body/null/坏 JSON/非法 Guid/缺 query/header，在业务与写事务前拒绝；零 business、零 repo 写入 |
| H03 | 五个正式 Ticket 权限定义存在；非 SuperAdmin 的真实登录、授予/撤销、GetAll 单 Search 权限正例、读权限不可写及 Update 写正例；定义、grant、端点与业务二次校验一致 |
| H04 | 未知/禁用/主体与请求租户不匹配先拒绝；两租户生产选库与读写隔离实际成立，含调用方上下文恢复 |
| H05 | 既有业务状态/SLA/DTO/null/list/enum wire 正例不变；领域失败 400、资源不存在 404、unknown 500 脱敏可序列化 |
| H06 | typed validators 真执行；缺必需 validator 不假绿；CT 向业务/仓储/验证传播 |
| H07 | PlatformDefault/ApplicationOwned 单 owner，启动验证零/重复绑定；生产与 fixture 都仅一个 EfCore binding；唯一 UoW 真织入、提交/回滚、stale/missing stamp、返回 stamp 与存储精确相等、fresh-scope 零写入与响应失败事实 |
| H08 | 默认 JIT、Host-only JSON、modifier/派生/wrapper、两个真实 Feature 双序、精确 v1/v2 共存与不等价拒绝均回归通过 |
| L01 | Controller/旧 CRUD stub 分支真归档；默认 analyzer/compile/package 不加载；原样重新引入任一 formal legacy call 的变异能被守卫捕获 |
| L02 | 必要 legacy 只经 explicit opt-in；未 opt-in 无旧路由；同一动作 formal/legacy 双发布被拒绝；无自动 fallback |
| N01 | Native publish/link/run + loopback：Ticket body/组合 envelope/scalar/list/void/404/error 路径真实可执行 |
| N02 | 错误预期、漏 metadata、错误 owner/typed envelope 的变异必须使目标场景失败、非零退出、无总 sentinel、进程有界回收；metadata 负例仍激活真实贡献并预先核对目标 identities/动作，不能用零端点制造假绿 |
| R01 | #128 GeneratedCrud、既有 CapabilityEndpoint、LocalEvent/RabbitMQ 共享工具链、package/build、依赖边界的受影响门禁按最终 head 留证 |
| D01 | memory、AGENTS、#123 evidence matrix、migration、archive/callgraph/allowlist 与支持边界全部同步 |

红例要锁定纯净失败原因；负例同时缺身份/JSON/UoW/租户的测试不能作为某一缺口的证据。归档测试先迁移有效行为断言，再退役维护旧输出形状的断言；不能用减少测试数量伪装通过。

## 11. NativeAOT 证据设计

新增独立 Ticket HTTP fixture（拟 CrestCreates.TicketHttpProjection.AotFixture + Tests），引用真实 Ticket Domain/Application/Contracts 与正式生成工具链，调用真实 TicketAppService；仅替换 repository/provider 等持久化叶子，不能手写同名 DTO、等价 service、JSON root table 或 endpoint map。

内存查询不得依赖 BCL EnumerableQuery/Expression.Compile/反射动态 LINQ 在 AOT 下碰运气；消费 #128 已落证的静态受支持 provider 方案，为 Ticket 的实际表达式提供强类型执行与明确拒绝。没有宣称支持任意 ORM query shape。

native 场景至少含：create/update body；组合 route/body/If-Match；all 与按客户/客服列表；assign/resolve/close 的实际领域调用；未知 id 404；非法输入零业务；可序列化业务失败；metadata 装配拒绝。逐场景 PASS + 总 sentinel，正例与变异都运行原生产物，证据绑定 publish/link/run 的精确提交。

原生 fixture 的裁剪/内存身份权限设施逐项声明；不能拿它替代 sample 的真实认证、租户、EF 测试。业务闭环 JIT 使用真实 PostgreSQL sample Host；新 NativeAOT 声明只覆盖该原生组合。EF/SqlServer/其他 Provider、整个 SaaSHelpdesk、Windows/macOS/其他 RID 均不得由此推导已验证。

## 12. 实施切片与评审门禁

| 切片 | 工作内容 | 可审查交付/退出条件 |
| --- | --- | --- |
| S1 事实/设计冻结 | 全仓调用图、必要 legacy 消费清单、Ticket wire/权限/异常/租户/并发基线；冻结新声明、failure bridge、owner schema 与 package 依赖图 | 纯净红/绿控制与设计入库；所有主动 wire 收紧有迁移表；设计审查通过才进 S2 |
| S2 通用正式投影与 JSON | shared model/emitter、前阶段普通产物、ownership/诊断、自动 roots、ExplicitHost/handler/JSON 注册；迁移已有 formal compatibility consumers | 单 driver/真实 MSBuild/外部包首次消费及 B01–04、H08 通过；不靠 Ticket 手写基础设施 |
| S3 Ticket/Host 迁移 | 契约限定11动作、CT、权限/验证/UoW、失败桥接、stamp正确性、生产 Host 正式装配、双租户 factory | H01–08 真实集成通过；旧 Ticket route 每项被正式贡献唯一拥有；尚不宣称 NativeAOT |
| S4 隔离与归档 | legacy runtime/analyzer/adapter 单向依赖、默认入口退出、Controller与旧stub归档、scalar helper、守卫/迁移诊断 | L01/L02；有真实消费者才保留legacy；无归档文件混入Compile/analyzer/package |
| S5 原生/回归/交接 | 实际 Ticket native host、分场景/变异门禁、受影响回归、全部文档与支持声明 | N01/N02/R01/D01；精确head证据、零未解释回归；提交合并建议，不自动合并/恢复CI |

建议提交按“基线 → 通用平台能力 → sample迁移 → 隔离归档 → 证据文档”拆分；每阶段更新 issues log，记录实际计数、红例名称、环境态与支持限制。不预写预计测试数作为已验证证据。

## 13. 完成定义

Ticket 的所有对外动作由唯一正式编译期贡献执行；原 Controller 声明、旧注册和 JSON fallback 不再参与该调用图。普通 Feature 只声明契约、业务服务/validator 与少量动作差异；Host 显式激活。失败、权限、租户与事务复杂性回到既有平台 owner。

必要 legacy 明确隔离并可删除；无人消费残留已归档。真实集成与独立原生证据、变异及架构守卫均成立，支持声明准确。以上工作和设计评审闭环完成后 #129 才可关闭；发布本设计不代表开始实现或授权自动合并。


## 14. rev.2：S1 复核后的设计决策与退出要求（2026-10-17）

本节优先于 rev.1 评论及 S1 事实稿中的未决项。审查提交为 `602d6e40ba2f0b0841e96055d95cc71b54e3ce97`，生产代码与基线 `196b6f66` 相同；新增内容仅为文档与测试。独立复跑新增套件为 **6 pass + 8 约定红 / 14，0 skipped**。红例是待实现契约，不能将该结果写成实现通过。

### 14.1 事实修正及证据分层

- Ticket 今天走 DynamicApiAotSourceGenerator 的 CrestService/controller 两个分支，尚无 CapabilityCompatibilityProjection 声明。compat generator 是另外一条待收口前端，不能把它描述为当前 Ticket 的执行链。
- `GET /all`：旧端点要求 Ticket.GetAll；继承的 CrestAppServiceBase.GetAllAsync 又检查 Ticket.Search。SuperAdmin 控制不能证明普通用户只授予端点权限便能通过。
- HelpdeskPermissions.Tickets 仅是常量，仓库没有使用它注册的 IPermissionDefinitionProvider；Ticket.* 与 Helpdesk.Tickets.* 均不能仅凭常量声称已经进入权限目录。现有 PermissionGrantManager 和 PermissionChecker 不依赖 definition manager，未定义名字也可写入 grant 并被 checker 接受。因此 R02 证明目录缺失，不能证明“除 SuperAdmin 外无人可调用”。
- 平台预设和样例 EF 模块各注册一次 EfCore binding；原 factory 再注册一次。生产组合静态可证明有两项冲突，原 factory HTTP 实测三项；当前没有成功直跑生产进程的证据，不将其写成已经观测到的生产 HTTP 结果。
- 归一化 factory 的合法写入、领域规则以及 R05–R08 是受控实验；原 factory 仅用于 R01/旧线路观测。两类结果分栏，不能合并声称原 Host 写入成功或租户隔离成立。
- 全套 29/70/99 为作者报告，本轮只独立复跑 14 条。旧失败分类 32+17+10+2=61，与“62 条既有红”有一条未分类差额；S1 补按测试 FQN 的完整账本。无法从“都是 HTTP 400”认定17条具有相同根因，必须保留未归因状态或补异常证据。

### 14.2 权限目录与行为迁移：冻结唯一名称

正式 Ticket 权限只定义五项：Ticket.Create、Ticket.Get、Ticket.Search、Ticket.Update、Ticket.Delete。由 sample 的显式 IPermissionDefinitionProvider 注册到既有目录，permissions 常量、动作声明和基类 override 引用同一常量源。不能仅生成一个同名字符串表或往测试 seed 插权限行后声称注册完成。

正式 GetAll 使用 Ticket.Search，和真实 AppService 二次检查一致；GetById/ByCustomer/ByAssignee 使用 Ticket.Get；Overdue 使用 Ticket.Search；Assign/Resolve/Close 使用 Ticket.Update。既有业务中的权限检查不通过“正式入口已授权”的标志跳过，直接 AppService 调用仍沿原有权限服务执行必要检查。

旧 Ticket.GetAll 只存在于旧链快照与迁移文档，正式投影不再发布/授予该名。仅拥有 GetAll 而没有 Search 的用户在旧服务实际上也不能列举；不得自动将这种单独 grant 转成 Search 而意外扩大权限。已有 Search 授予保持有效，正常列表调用减少一项错误的额外门禁。三个 GET 写动作从 Search 改为 Update，不自动把读权限 grant 升级为写权限。

Helpdesk.Tickets.* 的旧常量不注册成并行运行时定义，也不在 checker 做 OR/alias fallback。若部署中确有以这些名字保存的 grant，提供可审阅的迁移清单，保留 provider、provider key、scope 和 tenant；Assign/ChangeStatus 到 Update 会扩大能力，必须单列人工迁移决定，不能批量自动提升。其它 Helpdesk 权限家族不在本次顺手改名范围。

R02 应拆为：旧端点权限快照继续锁六个旧名字；正式目标测试验证五个定义以及实际投影要求的每项 definition。不得为了保留旧 R02 的硬编码六项而新增一个正式投影根本不用的 GetAll 定义。修改目标断言须在 issues log 明确本 rev.2 的决策，不能冒充实现让原测试自然转绿。

H03 必须有实际登录的非 SuperAdmin：无 grant 403；单 Search 可访问 all/overdue；单 Get 可访问明细但不能列举；只读不能执行三个写动作；Update 允许声明的写动作；撤销后再次拒绝。目录存在、实际 grant 和 HTTP 执行分别断言，不能继续用 SuperAdmin 代替权限验收。

### 14.3 UoW 与持久化装配责任：冻结 owner，不放宽 #137 内核

问题来自多个组合层重复注册，不能修改 AddUnitOfWorkProvider 为 first-wins/last-wins、Skip(1) 或宽泛 TryAdd 来掩盖冲突。

在 CrestWebOptions 增加显式持久化所有权选择（拟 API `UseApplicationPersistence()`；默认模式 `PlatformDefault`，显式模式 `ApplicationOwned`）：

| 模式 | 数据装配 owner | 责任 |
| --- | --- | --- |
| PlatformDefault | Platform 的既有默认数据预设 | 保持依赖默认宿主的行为；一次 core UoW + 一次 Provider binding；不得再有应用模块为同 provider 重复装配 |
| ApplicationOwned | 应用的唯一 Persistence module | Platform 不注入默认 SqlServer options contributor、默认 DbContext/adapter、Provider binding 和默认迁移上下文清单；应用模块提供实际 DbContext/adapter、provider、仓储、迁移上下文及一次 UoW 装配 |

这不是关闭认证/审计/租户/Setting/Permission 管线的开关。Platform 中业务层注册与 provider-specific 默认数据预设需分开；最终图中它们解析到应用提供的正式仓储与 DbContext。SaaSHelpdesk 显式选择 ApplicationOwned，由 EntityFrameworkCoreModule 配置 PostgreSQL/HelpdeskDbContext、IDataBaseContext 与 EfCore binding。Host identity/OpenIddict、框架存储和迁移上下文也必须使用该模块明确选择的 provider，不允许残留 SqlServer contributor 与 Npgsql 对同一 context 叠加。

测试 factory 只替换连接/叶子设施，移除其 AddEfCoreUnitOfWork 重复调用，不再自己声明第三个 owner。正式验收 factory 不运行 Skip(1) 归一化。仅 S1 历史事实控制可以保留归一化，并显式标为测量设施。

在正式 Host ready/Map 前通过既有 bootstrap/启动校验装配一步验证最终 UnitOfWorkProviderBindingRegistry、必需 provider/default 与实际 DbContext/adapter 一致性；借同一初始化顺序完成，不单独 BuildServiceProvider，不启动第二个 bootstrap engine。重复或缺绑定必须启动期诊断，不能等第一个请求变成业务 400。此验证不预先打开请求事务，也不声称自动验证所有 Provider 能力。

正例覆盖默认平台宿主、应用持久化宿主、原样生产组合与 factory；负例覆盖 ApplicationOwned 缺 provider、同 provider 冲突、默认 SQL 与应用 PG 同 context 冲突。保留 #137 对确实不同绑定的确定性拒绝测试，验证真实六写动作 begin/commit/rollback 与资源身份。变更平台默认行为时回归 LibraryManagement 等实际消费者。

### 14.4 普通编译、引用程序集、ownership 与 legacy 依赖

延用 §4 的前阶段与现有 CRUD manifest 协议，不另造一套未校验的 JSON 文本所有权。输入必须包含源文本与编译选项、引用程序集、目标 assembly、配置/TFM/RID、声明与模型 schema、工具版本。输出继续记录 OwnerAssemblyName、OwnerServiceFullName、ArtifactHintName、DeclaredTypeFullNames、SourceLocation、ModelSchemaVersion、ContentHash；类型列表仍为纯 FQN，owner 是兄弟字段，不能改回 owner:type 拼接字符串。

手写投影扩展增加可版本化的 projection/action identity 与 referenced contract assembly/type identity。OwnerAssemblyName 是产出胶水的 HTTP owner；service/contract assembly 是被调用业务的来源，不能混成一个 owner。同一 action identity 以 owner、service、closed contract method 签名和配置得到确定值；不使用仅 action 短名，顺序无关。两个前端声明同一 artifact/action 时诊断；不能根据一次 unrelated manifest 命中而整程序集跳过。

冻结阶段语义：CRUD prepare/include → AppService projection prepare/include → JsonContracts prepare/include → 一次 CoreCompile。任务名称可按现有命名规范落地，但必须是显式 DependsOn 边，测试读取真实 MSBuild 执行与产物。此阶段使用既有隔离 CLI、source-generated transport JSON 与 state/write-if-changed 工具；需通用化时提取已有实现，不能复制另一份状态机。

引用程序集上的真实 service/DTO/contract 是 B04 必测输入，不能用同名 stub。是否提供额外 assembly-level 声明 API 不在本次悄悄定成第二套声明链：当前 Ticket/Asset/Procurement 使用已有声明目标；外部服务的声明范围、DI contract 与 HTTP owner 必须在 S1 探针和 schema 中明确，若现有 Attribute 无法表达则先提交小范围设计增补，不能进入实现后用反射补洞。

项目依赖冻结为：

- 正式 DynamicApi / Web 不引用任何 *.Legacy；Runtime 仍不引用 Api/Web/ORM。声明数据留 Domain.Shared；共享 model/emitter 为 Tooling shared-source，不让 analyzer 运行时加载 net10 工具程序集。
- CrestCreates.DynamicApi.Legacy 单向引用正式共享契约/typed runtime 和必要的既有依赖；承载旧 provider/registry/helper/body reader，正式 runtime 不转接回去。
- CrestCreates.CodeGenerator.Legacy 为 netstandard2.0 analyzer，仅包含仍有消费者的旧 Minimal API 前端。小型共用 Roslyn/model/emitter 源码通过 shared-source 引入，不引用一个含默认正式 Generator 集合的 analyzer 来凑实现。
- CrestCreates.Web.Legacy 放 Platform 层，引用 CrestCreates.Web 与 DynamicApi.Legacy，提供明确旧 Add/Map 适配；主 Web 包不引用它。样例按精确 consumer allowlist 显式 opt-in。
- 默认 analyzer/包分发不包含 Legacy，根目录自动 analyzer 引用及 AOT/Trim props 排除该 analyzer 自身；归档代码不混入任何 Compile/package glob。Controller 生成器与 TicketApi 的专属分支无消费者后归档，不进入 legacy analyzer。

S1 退出仍需将该表细化为具体项目引用图、action/consumer 允许清单和输入输出 schema fixture，并补真实引用程序集负例；仅把未来 API 名称列出来不算冻结完成。

### 14.5 failure bridge、绑定失败与唯一 404

冻结拟契约形状：Capability.Abstractions 中增加 `CapabilityFailureFacts` 与分类扩展点 `ICapabilityExceptionClassifier`。facts 包含封闭 `CapabilityFailureKind`、ErrorCode、SafeMessage、可选 SafeDetails；不含 HttpStatusCode、Exception、Type、stack 或任意扩展字典。作为 CapabilityExecutionResult 的 additive 字段，不替换既有 ErrorCode/Issues/Status，不作为业务 Output 序列化。分类器消费原 Exception 与真实 CapabilityExecutionContext，返回是否处理及只读 facts；无 HTTP adapter 时原 transport 的错误语义保持。

Web/Platform 的 scoped adapter 只在真实 HTTP invocation 上调用既有 ICrestExceptionConverter，使用请求原 HttpContext，不伪造上下文。允许的语义到状态映射如下；未知/不支持分类回到 Internal，禁止将任意上层 status 值透传进 Runtime。

| Kind | HTTP / code 权威 |
| --- | --- |
| InvalidRequest | 400 / Crest.Request.InvalidArgument |
| ValidationFailed | 400 / Crest.Validation.Failed |
| Unauthenticated / Forbidden | 401 / Crest.Auth.Unauthorized；403 / Crest.Auth.Forbidden |
| NotFound | 404 / Crest.Entity.NotFound |
| ConcurrencyConflict | 409 / Crest.Concurrency.Conflict |
| PreconditionRequired | 428 / Crest.Concurrency.PreconditionRequired |
| OperationInvalid | 400 / Crest.Operation.Invalid |
| Internal | 500 / Crest.InternalError；无内部 message/details |

绑定层对坏 JSON、非法 scalar、缺必要 route/query/body/header 的失败在 typed binder 内归一为请求错误；不能把全局所有 JsonException/FormatException 都改成 400，因为业务或服务器响应序列化抛这些异常可能是内部故障。边界外异常继续遵守 converter 的权威分类。必需 body 的 empty/whitespace/null 明确失败，失败时 handler 和写事务尚未开始。

异常被 pipeline 消化时保留安全分类 facts，由 HTTP presentation adapter 使用既有转换/本地化规则和原 request TraceId 输出 Web 所有的 CrestErrorResponse。对外文本/code 不从 PIPELINE_ERROR 的原始 Message 补猜。分类/呈现自身失败回到安全 500，不泄露原异常；正常非 HTTP capability 的 ProblemDetails/既有 mapper 不被整体替换。

GetById 返回 null 在 typed result 计划中明确映射 NotFound，与真实 KeyNotFoundException 共用 CrestErrorResponse 的 404 形状。成功仍为 typed DynamicApiResponse<T>/void。缺 metadata 的启动诊断与该运行期业务 404 区分；失败体 metadata 本身缺失必须前置拒绝。

调用方取消和 deadline 沿既有取消策略，不能被 classifier 重新变成 OperationInvalid/内部失败并重执行业务。S1 需冻结调用方中止与 timeout 的对外处理及 #137 committed/unknown 事实独立保留的测试口径，S3 实现和原生门禁验证。

### 14.6 Framework HTTP 缺失与真实宿主启动：限定范围

`AddApplicationServiceAssembly<UserAppService>()` 只选择生成 provider，不使被引用的 framework assembly 重新生成代码。POST /api/user 的404是独立的缺失 HTTP 面；不能给 DDD Application 加 ASP.NET 引用、扫描所有引用程序集或随手生成所有 public 方法来解决。

本 issue 的生产 HTTP 迁移范围仍为 Ticket、现有正式 compat consumers 以及明确允许的 legacy consumers。User/Setting/Feature/Audit 等框架服务的完整新 HTTP 面不在 #129 自动开通；必须在 S1 账本逐服务记录“实际有路由/未生成/测试依赖”，交给独立 HTTP 设计任务，不借 auth fixture 种子顺带新增身份管理 API。

Ticket 的真实认证/授权测试可以通过现有业务服务/正式数据种子建立普通用户、角色和 grant，再走实际 OpenIddict 登录和 HTTP 请求；测试准备与 User HTTP 功能验收分开。原有专门验证 /api/user 的失败测试保留原目标与归因，不通过改期望404、skip、删除或声称“鉴权已测所以 User API 已恢复”制造全套绿。#129 全量回归需要逐 FQN 比较既有失败，新增回归为零；明确未解决的框架 HTTP 面不计入已通过能力。

生产进程启动挂起另设 S3 的前置交付：真实 Program + PostgreSQL，记录模块初始化/迁移/seed/bootstrap 阶段及有界 startup deadline（测试配置，默认建议90秒）；超时保留日志、失败阶段、子进程 exit 与清理证据。不得静默禁掉 migration/bootstrap/tenant boundary 或增加一个只用于测试的空管线使其监听。修复实际根因后验证监听、真实登录、Ticket 一次读写与退出；无生产进程证据时只声明 WebApplicationFactory 观测，不能升级成生产直跑通过。

原 shared-connection factory 不承担 H04；保留生产选库 contributor 的双租户 factory 按 S3 交付，不要求在 S1 做完其整个实现，但 S1 必须写出连接/tenant/context 责任与拒绝条件。

### 14.7 S1 还要补什么；S2/S3 如何接续

1. 将 rev.2 与计划、S1 事实稿、issues log 同步；分开旧快照与正式目标，修正“只有 SuperAdmin”和单层 GetAll 权限推断，补62条既有失败的 FQN 分类差额。
2. 补当前实现下组合信封同轮不可见、per-Host 未激活仍受全局注册影响的纯净契约探针；使用真实引用程序集/官方 STJ 的对照，预检 producer 确实运行，不用不存在的未来 API 作为唯一失败原因。这是 S1 原退出要求；完整 B01–04 实现验收仍属 S2。
3. 补新声明/manifest/action identity、项目依赖图与 failure bridge 的 contract fixtures，明确未决的引用程序集声明范围和取消处理，不把“拟名列表”当成冻结完成。
4. 增强红例判别力：R03/R04/R08 同时检查业务/事务/持久化零副作用；R06 在 fresh scope 验证原行/stamp仍在；R07 与真实数据库当前 stamp精确相等，并包含真实修改后的 stamp；补合法 stamp 更新/删除正向控制。仅状态428或非空字符串不能证明没有删除或返回的是当前值。运行计数可由测试装饰器提供，不给生产增加测试专用公共扩展点。
5. R01 的测试归一化限制在测量设施，补“原生产组合两 binding / 原 factory 三 binding”的来源证据；正式转绿必须删除调用方重复装配，而不是让 registry 选择第一个绑定。

完成以上补件并复审后才放行 S2。S2 专注通用投影/JSON/ExplicitHost 和已有 consumer 迁移；S3 先完成平台持久化 owner/启动可诊断，再做 Ticket/五权限定义/validation/stamp/error/双租户实际验收。无需为观察 S1 缺口提前实现 S3，也不因文档 rev.2 发布自动解除门禁。S4/S5 顺序与证据范围保持；CI 继续暂停，不自动合并。
