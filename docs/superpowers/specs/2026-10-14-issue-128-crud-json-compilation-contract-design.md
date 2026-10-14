# #128 CRUD JSON 编译期契约闭环 — 详细设计 rev.1

## 1. 状态、基线与结论

- Issue：[#128](https://github.com/OrchesAdam/CrestCreates/issues/128)，总控 #121；前置 #123、#127、#146 已交付。
- 设计基线：远端 master **de50b33765fbdf8992c77ff15a126147c4cfeb7a**（PR #149 文档同步；PR #148 merge 为 0c6e574a2872e1a6412e861b97e543910ae1b876）。已核对远端 ref 与该提交源码。
- 本文是**待审查的设计**，没有实施运行时代码，也没有声称新 CRUD 验收已通过。本文覆盖 Issue 的详细实施范围与退出标准。
- 决策：**BuildTasks 前置物化 CRUD 产物 → 普通 Compile 输入 → 既有 JsonContracts 发现闭合 roots → 官方 STJ Source Generator → 强类型请求/响应 binding**。正式 CRUD HTTP 使用既有 AppService-to-Capability compatibility projection；同一动作只保留这一条 HTTP 执行路径。
- 不要求普通 Feature 手写 DTO、JSON type table、映射方法、Capability Handler 或五份 endpoint spec。编译期工具承担这些胶水。

## 2. 当前代码事实与修订后的问题定义

| 事实 | 代码位置/影响 |
| --- | --- |
| CrudServiceSourceGenerator 在同一 SG 轮生成 DTO、IAppService、mapping 声明、实现和旧 Dynamic API provider | src/Tooling/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs；#warning 在约 987 行，是生成器自身源码中的警告 |
| CRUD body/result 仍调用 DynamicApiGeneratedRuntime.ReadBodyAsync/WrapResult | 两者落入 legacy compatibility helpers；生成一个 context 并不会自动改变这些调用 |
| 现有 SourceGeneratorTestHelper.RunGenerators 逐个 driver 更新 compilation | tests/Tooling/CrestCreates.CodeGenerator.Tests/TestHelpers/SourceGeneratorTestHelper.cs；它给后一个 generator 人为提供前一个输出，不能证明真实单次编译的跨 SG 可见性 |
| 正式 Domain.Shared 中没有 GenerateCrudServiceAttribute / GenerateEntityAttribute 定义 | 同名定义在上述测试 helper；正式 EntityAttribute 存在，但不会触发当前 CrudServiceSourceGenerator 的 HasGenerateCrudServiceAttribute |
| EntitySourceGenerator 另有 DTO/mapping 声明产出 | [Entity] 与 CRUD 组合时必须明确产物 owner，不能靠 AddSource 的先后顺序去重 |
| Compatibility result emitter 用 ctx.Output 推导泛型参数 | AppServiceCompatibilityResultContractEmitter 当前形成 DynamicApiResponse<object>；既有原生 HTTP fixture 手写该 root，不能作为新 CRUD typed envelope 的证据 |
| 分页查询还存在两个运行期障碍 | FilterDescriptor.Value 是 object 且 setter 不接受 JSON 默认还原的 JsonElement；QueryExecutor<T> 按字段字符串反射建表达式，默认排序也会经过该路径 |
| JsonContracts 已有成熟前置 target、输入 manifest、路径约束及 package/repository 两种 transport | 直接复用；不得另做一个运行时 JSON 发现器 |

因此 #128 的问题是：**正式声明入口、跨生成器编译顺序、请求绑定、响应信封与实际 CRUD 查询执行必须形成同一条可证明的链路**。不能用 test-only Attribute、串行 driver、手写 DTO/context 或空查询假实现证明交付。

## 3. 范围与边界

纳入：

1. 可从新项目真实引用的 CRUD 声明 Attribute 和明确的程序集产物归属。
2. CRUD 前置生成、JsonContracts 自动闭合 roots、官方 STJ 同次编译可见性。
3. 既有 compatibility projection 上的 CRUD 五动作、强类型 body/input/result、启动期完整契约校验。
4. 为搜索/分页主链必需的有限 JSON filter 值归一与编译期字段访问。
5. clean、增量、声明移除、跨项目、design-time、multi-TFM/RID、package transport 的正确性。
6. 原生 HTTP fixture、迁移说明、边界守卫与 #129 交接。

不纳入：

- 迁移全部旧 Controller/普通 legacy AppService；#129 承接。
- 新造 Dispatcher/Capability Pipeline/UoW/权限、租户或审计真相来源。
- ORM NativeAOT 背书、通用查询 DSL/任意动态 LINQ、所有 Provider 的能力补齐；#130 独立裁定。
- 全框架 JSON converter 插件发现、任意 object 多态、运行时扫描/反射 fallback。
- 要求业务重排为新的独立 Contracts 项目，或把 namespace 当作物理程序集迁移指令。

## 4. 方案比较与选择

| 方案 | 判断 |
| --- | --- |
| SG 同轮追加 DTO + JsonSerializable/context | 不选。官方 STJ 看不到另一个 SG 新产物；再跑一次 generator driver、依赖旧 obj 或使用 experimental generator 顺序都不构成交付 |
| 强制拆独立 Contracts 项目 | 不选为默认。会把工具链问题转成每个 Feature 的项目编排负担；已有独立契约可以保留 |
| JsonContracts 自己复制一份 DTO 规则 | 不选。字段/排除规则、nullable、mapping 与实际 DTO 会漂移 |
| BuildTasks 前置物化，JsonContracts 消费普通源码，STJ 正式生成 | **选择**。一个产物 owner、一份生成模型和 emitter；JSON 工具链继续负责 root/closure，STJ 负责序列化元数据 |

前置范围不能只搬 DTO：IAppService、mapping seeds、投影 input envelope/JSON root 与实际服务方法签名也要来自同一模型。否则会把不可见性从 DTO 转移到 mapping、compatibility input 或返回值。

## 5. 正式声明与开发体验

### 5.1 一处显式声明，物理 owner 清楚

在 Domain.Shared 提供正式、sealed 的 GenerateCrudServiceAttribute，**首选形式为 Application/Feature 程序集上的 partial service 声明**：

~~~csharp
// Product 是当前项目或已引用 Domain 程序集里的实体。
// Attribute 完整 namespace：CrestCreates.Domain.Shared.Attributes
[GenerateCrudService(typeof(Product), RoutePrefix = "/api/products")]
public partial class ProductAppService
{
    // 可选业务扩展；默认 CRUD 无须写方法。
}
~~~

最小契约：

- 构造参数 EntityType 必须是可访问、非开放泛型的实体类型。
- 声明类必须是 top-level、非泛型 partial class；生成 AppService/interface/DTO 的**当前程序集**就是 owner。
- 默认 DTO namespace 为声明 service namespace 下的 Contracts；DtoNamespace 可显式覆盖。生成 namespace 不推导物理项目位置。
- RoutePrefix、DtoNamespace、CapabilityIdPrefix 以及 ExcludeProperties/FilterableProperties/SortableProperties 是可选声明配置；默认规则只定义一次。
- 实体可以来自引用程序集；不要求改 Domain 的依赖方向。Domain-only 项目不会因实体 Attribute 自动生成依赖 HTTP/Application 的实现。
- 不复活宽泛 GenerateEntityAttribute 或通过同短名/EndsWith 猜 Attribute。正式识别使用 metadata full name + symbol identity。测试必须引用真实 Attribute。
- 测试 stub 中的无参 [GenerateCrudService] 不是已交付公共契约，不能据它宣称必须维持一条生产 fallback。盘点若发现真实消费者自定义了旧声明，则提供一次性迁移诊断与替换说明。
- 首组支持默认 concrete AppService。旧测试里的 GenerateAsBaseClass 不能自动变成第二条正式生成策略；真实需求需给明确的 concrete 激活/投影规则后另行设计，不能把 abstract 类型注册进 DI。

字段规则沿用、集中并冻结为 CrudGenerationModel：

- Id、TenantId、审计和软删除字段不从 Create input 写入；Update 的 ConcurrencyStamp 按既有并发契约保留，不能映射覆盖实体 stamp。
- navigation 不自动进入默认 wire DTO。scalar/enum/nullable/有限静态集合按明确契约处理；无法静态闭合的成员编译期拒绝。
- entity construction / 可写成员必须满足现有 mapping 契约；private setter、无可用构造器等不能靠反射解锁，也不能生成空 mapping 假装支持。自定义 partial 扩展遵循现有 mapper 扩展协议。
- Id/DTO 命名、路由、权限和投影身份都进入 manifest。以 assembly + service full metadata name 区分 owner，不能按 entity short name 合并。
- CapabilityId 沿用 compatibility projection 命名规则并纳入完整 owner identity；显式 prefix 可保持既有外部标识。ID/路由碰撞必须诊断，不能靠注册顺序覆盖。

### 5.2 装配短路径

使用者一次导入框架构建工具链；包消费和仓库消费分别有标准 props/targets，不写 source glob 去捞 obj。

生成每程序集的 AddGeneratedCrud() 扩展，放在程序集限定的 generated namespace，Host 显式调用所需程序集的扩展：

~~~csharp
builder.Services.AddCapabilityRuntime();
builder.Services.AddAccountability();
builder.Services.AddCrestCompatibilityProjection();
builder.Services.AddGeneratedCrud(); // 使用对应程序集限定 namespace；多程序集可显式静态调用

// repository、UoW provider、身份/权限等仍通过原有正式装配入口配置。
var app = builder.Build();
app.MapCrestCapabilityEndpoints();
app.Run();
~~~

这些是 Host 基础装配，业务 Handler/AppService 不写 JSON 注册或基础设施定位逻辑。AddGeneratedCrud 幂等；同 scoped AppService 的 interface/concrete 解析指向同一实例。跨程序集不扫描 AppDomain，也不把所有已加载程序集自动当作已激活 Feature。

## 6. 唯一编译链与产物 owner

### 6.1 分工

| 所有者 | 职责 |
| --- | --- |
| CrestCreates.BuildTasks | CRUD precompile 的 MSBuild 入口、输入 manifest、路径/增量/owner 校验、将产物纳入 Compile |
| 共享纯编译期模型/emitters | 从真实 Roslyn symbol 生成 DTO、interface、permissions、mapping seeds、AppService、typed query adapter、compatibility 投影及激活胶水 |
| 既有 JsonContracts.Tool 执行设施 | 复用隔离 Roslyn 进程/请求响应与 transport；新增独立 prepare-crud 命令承载前阶段，不能把 CLR assembly load/reflection 作为模型来源 |
| JsonContracts.BuildTasks/Core | 读取包含 CRUD 物化源码的真实 Compile，发现 CRUD roots/closure/provenance，产生 ordinary context/JsonSerializable 源码 |
| 官方 STJ SG | 只在正式 CoreCompile 中处理普通输入，生成 JsonTypeInfo |
| 其余 Source Generators | 消费已物化 DTO/mapping seeds；同一 driver 中运行，不依赖彼此的新输出 |

共享模型/emitters从现有 CodeGenerator 提取并**source-link 同一份实现**到需要的 Tooling 项目，避免给 analyzer 增加难以分发的运行时依赖。不得把 Runtime/Persistence 具体程序集引用进 Tooling。构建工具不能作为应用运行时依赖进入发布目录。

CRUD 前阶段直接物化：DTO、IAppService、permission 定义、mapping declarations、服务实现、既有 compatibility capability/endpoint/input/invoker/result/manifest 的胶水。projection emitters 与手写 AppService 的现有 generator 共享，不维护 CRUD 专用 Dispatcher 或第二份 binder。

旧 CrudServiceSourceGenerator 对正式 CRUD 变为声明/装配一致性诊断入口，停止 AddSource 输出旧 CRUD bundle/provider；旧相应实现与失效测试归档。AppServiceCompatibilityGenerator/EntitySourceGenerator/ServiceRegistrationGenerator 对 manifest 已拥有的具体产物明确跳过，不能再输出一份同名文件。普通 [Entity] 和非 CRUD 手写 AppService 的既有行为不因这个 owner 标记被无条件关闭。

### 6.2 目标顺序

~~~text
SDK 准备当前 inner build
  → GenerateGlobalUsings + ResolveReferences
  → PrepareCrestCrudInputs（真实源文件 + ref assemblies + 编译选项）
  → GenerateCrestCrudArtifacts（普通 .g.cs + provenance/产物 manifest）
  → IncludeCrestCrudArtifacts（Compile Remove/Include 去重）
  → PrepareCrestCreatesJsonContractInputs
  → GenerateCrestCreatesJsonContracts
  → IncludeCrestCreatesJsonContractGeneratedSource
  → CoreCompile：官方 STJ + mapping/其他 SG 在一个真实 driver 中执行
  → publish/link/run
~~~

使用现有 CrestCreatesJsonContractGenerationDependsOn hook 建立**显式 DependsOnTargets**。不能只依赖两个 BeforeTargets=CoreCompile 的偶然排列。

前阶段只分析 authored sources、SDK global usings 与当前 TFM 的 ReferencePathWithRefAssemblies；排除自己上次 CRUD/JSON 输出及 compiler source-generator 目录。先生成 CRUD bundle，再构造 JSON 的语义 compilation。已知的下游生成方法尚不存在可以留给最终编译完成，但实体/成员/DTO 类型的错误必须当场诊断，不得吞异常生成一个不完整表。

每项产物携带 owner、完整类型名、源位置、模型 schema version、内容 hash。compiler-visible marker 只用来核对前阶段存在，必须同时校验实际 manifest/文件；一个布尔属性不能单独作为“契约已完成”的证据。CrestCreatesCodeGeneration=false 与仍存在的正式 CRUD 声明组合必须给确定性构建错误，并排除旧 bundle；条件编译移除声明则生成空集合，不能继续暴露旧端点。

### 6.3 增量与设计时

- 以当前 Configuration/TFM/RID 的 IntermediateOutputPath 为边界，建议输出单一 CrestCreates.Crud.g.cs + inputs/outputs manifests，避免逐实体孤儿文件。不得在源目录生成文件或从旧 SG 输出目录回读。
- 输入 fingerprint 包括**完整源/引用列表及内容**、transitive referenced assembly 内容、global usings、常量、nullable/lang version、配置选项、模型/emitter/task/tool 版本；文件移除、重命名和条件编译变化都能失效。
- 每次验证预期输出集合；stamp 存在而源码缺失要重新生成。无 CRUD 声明时输出合法空 bundle/空 manifest，绝不能继续编译旧 DTO/endpoint。
- write-if-changed + 临时文件校验后原子替换；失败不能留下可消费的“成功 stamp”。并行 build 使用不同 inner-build 路径；同路径并发有受控写入协议。
- 删除/替换仓库源码归档到 99_RecycleBin；构建缓存的 Clean/原子替换按工具链生命周期处理，不用提交旧 obj 维持成功。
- DesignTimeBuild 使用同一模型的**轻量、当前输入生成**，提供 DTO/interface/service surface 给 IDE。design-time 输出与真实 build stamp 分离，不能把 IDE 成功缓存当 publish 证据。CRUD context/root 声明也由同一 JsonContracts 模型在独立 design-time 输出路径物化，再交给 IDE 的官方 STJ；不能仍跳过 JSON 阶段、留下引用不存在 Context.Default 的 service surface。引用尚不可用则给有限诊断、不复用过期类型；SDK 未运行 analyzers 时不声称完成整个生成编译。
- Package/Repository transport 互斥，配置路径不写死 Debug；全新外部目录的包消费者必须一次 build 成功。buildTransitive / Tooling 依赖必须 PrivateAssets，不渗入运行时 deps/publish。

## 7. JSON 契约、绑定与结果

### 7.1 自动 roots 与 closure

每个激活 CRUD surface 自动产生：

- Create{Entity}Dto、Update{Entity}Dto、{Entity}ListRequestDto；
- route/body/header 组合生成的 Update/Delete 等 compatibility input envelopes（CapabilityEndpointMapper 会对实际 input 做 SerializeToElement，不能只注册外层 request body）；
- {Entity}Dto、PagedResultDto<{Entity}Dto>；
- **DynamicApiResponse<{Entity}Dto>、DynamicApiResponse<PagedResultDto<{Entity}Dto>>、DynamicApiResponse（void/404）**；
- 上述类型可序列化成员的静态闭包、显式 enum/集合/nullable 以及使用到的错误 DTO。

JsonContracts增加 CRUD discovery/provenance，复用既有 normalizer/validator 和 writer，不手写 JSON serializer。public DTO、internal envelope/context 的可访问性分别校验；same-name 不同 assembly 类型不能混同。缺失闭包在 build 报错；支持的 converter 必须显式、静态可构造且有证据，不能退到 object/general resolver。

context 支持 metadata mode；HTTP 默认 camelCase、大小写兼容沿用现有规范。JSON options 的来源仍是该 Host 最终 Http.Json.JsonOptions，不能悄悄无视宿主的明确配置而总用另一个静态 Default options。

### 7.2 per-Host 组合、启动校验与拒绝时机

- 生成 typed JSON contributor，由该程序集 AddGeneratedCrud 注册；借助 IConfigureOptions/PostConfigureOptions 将 source-generated resolver 接入该 Host 的 JsonOptions，再读取最终 options，解析并锁定本 Host 的 CRUD JsonTypeInfo。
- 不做 process-global codec/options/factory cache；静态生成元数据/不可变代码声明不能捕获 IServiceProvider、scope 或可变 Host options。
- CRUD metadata 必须由已声明的 source-generated contributor 提供。即使 JIT Host 有 DefaultJsonTypeInfoResolver，也不能靠它补齐缺失 CRUD contract。源码 context Default 可作为 resolver，但最终 metadata/options 的绑定及定制行为必须有组合测试，不能依赖链顺序猜测。
- 启动/Map 阶段校验 body、capability input envelope、payload、closed result envelope、void/404/error roots，全部早于首个请求；不只复用当前“body types 非空”的弱检查。
- MapCrestCapabilityEndpoints 对 CRUD DI descriptor providers 与既有 providers 做一次权威汇总：CapabilityRegistry 未 Built 时完成单次 Build，再由同一 endpoint registry 构建/映射；已 Built 则核对所需 IDs/version/shape，缺失即拒绝并指出应在 Build 前激活 Feature。不得增量突变已冻结 registry 或建第二个 registry。bootstrap ready 标记仅在成功构建与校验后提交；不得继承当前 endpoint bootstrapper 在 Build 前先置 _built、失败后永久跳过重试的行为。
- 对 CRUD descriptor 采用 DI 的显式 provider contributions；已有 binding/invoker/result 注册设施可存放不可变、无 Host 捕获的 generated code，但 CRUD 激活集合/JSON 校验只来自本 Host。未激活 Feature 不得靠全局加载补齐；不能污染另一 Host 的必需 roots。
- 每个激活动作的 typed binding/invoker/result mapper 都是必需契约；缺 result mapper 不能降级走 CapabilityEndpointResultMapper 的通用 output/fallback 分支。同 ID 不同声明、不同 codec、缺 resolver、缺返回值 root 都 fail closed。诊断含 assembly/service/action/direction/type，不缓存一次失败为永久 ready。
- contracts-only/Domain 项目不被生成 HTTP runtime 引用；缺实际 Application/HTTP 引用时给编译期装配诊断，不静默生成半条链。

### 7.3 强类型 body 与强类型信封

body 复用 CapabilityEndpointBodyReader 的 typed overload；生成 binder 显式传 JsonTypeInfo<T> 和 CT。正式新声明的 required Create/Update/Search body 固定为：empty/whitespace/JSON null/坏 JSON → 400，业务调用/仓储写入为 0。这是对旧 helper 的 empty→new DTO 语义的明确退出；不能默默把坏输入变成默认 DTO 继续执行。普通非 CRUD compatibility endpoint 的历史语义本 Issue 不擅自改变。

result emitter 必须按声明的返回类型做受检强类型转换，再调用 CompatibilityHttpResultMapper 的**typed、无 fallback overload**：

~~~csharp
// API 形状示意；与现有 envelope factory 共用构造规则
WrapResult<ProductDto>(typedOutput, JsonTypeInfo<DynamicApiResponse<ProductDto>>);
WrapResult<PagedResultDto<ProductDto>>(page, pageEnvelopeInfo);
WrapGetResult<ProductDto>(typedOrNull, productEnvelopeInfo, voidEnvelopeInfo);
WrapVoidResult(voidEnvelopeInfo);
~~~

绝不能由 ctx.Output 的 object 静态类型推导 T，也不能事后 GetType()+反射 JSON 猜真实返回契约。输出类型不符是平台诊断/500，不重执行业务、不回滚已提交结果；序列化与响应写失败不得把已提交事实伪装成回滚。

typed overload 在元数据缺失时直接拒绝，不调用旧 MapAotJson fallback。旧 overload 仅供隔离 legacy 消费者，不能成为正式 CRUD 的分支；共享的是 envelope 构造与 HTTP 语义，不复制一套 wrapper。

### 7.4 wire/业务行为矩阵

| 动作 | 路由 | 输入 | 响应 |
| --- | --- | --- | --- |
| Create | POST {prefix} | CreateDto，required | 200 + DynamicApiResponse<Dto> |
| GetById | GET {prefix}/{id} | 静态 Id parser | 200 + typed envelope；不存在 404 + 无数据 envelope |
| Search/GetList | POST {prefix}/search | ListRequestDto，required | 200 + typed paged envelope，包含 items/count/page 信息 |
| Update | PUT {prefix}/{id} | route Id + UpdateDto，required | 200 + typed envelope；保留并发 stamp 契约 |
| Delete | DELETE {prefix}/{id} | route Id + If-Match（并发实体） | 200 + void envelope |

路由 prefix 取明确声明/既有 convention；不得因重构改为 GET 搜索或把 filter 字段丢掉。权限名保持 {Entity}.Create/Get/Search/Update/Delete。Search 为 Query，Create/Update/Delete 为 Command，不能仅凭 POST 推导 Search 是写命令。

权限、tenant、audit、validation、UoW 使用现有 Capability/AppService/内核链。写操作使用唯一 UoW，生成 invoker 传下受管 CT；不得再发出旧 bool ExecuteAsync 调用或绕过 #137 的执行 token。输入失败先于业务/UoW 写副作用；事务结果与响应写入结果独立。

## 8. 搜索 JSON 与有界原生查询

该部分是原生 Search/Paged 退出标准的必要范围，不把 QueryExecutor 全项目重写成通用平台工程。

- 保留 FilterDescriptor 的现有 CLR 调用面；为 JSON wire 定义一个**有限值域 converter/归一器**，限于 null、scalar、同类 scalar array，不允许任意对象、多态类型名或 object converter fallback。
- 推荐 wire 暂存为受限 JsonElement，明确允许该受控表示进入 FilterDescriptor.Value；生成的字段 adapter 根据**字段静态 CLR 类型**读取 string/bool/int/long/decimal/Guid/date/enum/nullable 等，并输出准确类型。不得靠 JSON number 猜字段类型或通过 Convert.ChangeType 反射。
- 不引入全局 JsonConverter<object>。FilterDescriptor 专用 converter 的读写、现有 CLR scalar/集合兼容及 numeric precision 必须有测试；嵌套 object/非同类数组/不支持 token 明确 400。
- 根据 FilterableProperties/SortableProperties 与实体成员生成 switch + 强类型 lambda：Equals/比较/string contains/starts/ends/In/NotIn/null 与 OrderBy/ThenBy 等只为受支持组合发出代码。field/type/operator 不匹配在执行查询前拒绝，不忽略、不降级全表查询。
- 新 CRUD Search 不调用反射式 QueryExecutor<T>.ApplyFilters/ApplySorts，包括默认排序。分页边界复用 PagedRequestDto，排序稳定规则保持 CreationTime desc 或 Id desc，并定义同值时 Id 次排序。
- 只替换当前 generated CRUD 消费点；旧 QueryExecutor 的归档/其他消费者迁移单列交接，不能为了本 fixture 删掉尚有消费者的公共 API。
- 原生 fixture 用明确的 memory repository/query provider 执行真实生成的服务/adapter，并验证筛选/排序/总数/分页。若 BCL IQueryable 的某个操作组合无法完成原生执行，S1 落证后必须收敛到静态受支持执行方案或明确拒绝，不能为绿灯换假 AppService/屏蔽 Search。EF/其他 ORM 的翻译能力仍独立声明。

## 9. 诊断、迁移与唯一主链守卫

新增诊断族拟定为 **CCCRUD001–008**（实施时登记到集中诊断目录，发现冲突必须整体调整并在 S1 冻结）：

| 代码 | Error 条件 |
| --- | --- |
| 001 | 正式声明有误：Attribute/实体/partial service/布局不支持 |
| 002 | precompile/JSON transport 未装配、实际输出缺失或版本不一致 |
| 003 | 类型/owner/路由/descriptor identity 冲突、重复产物 |
| 004 | DTO/closure/member/converter 不可静态支持或不可访问 |
| 005 | 目标项目缺 Application/HTTP 必需引用或程序集归属错误 |
| 006 | precompile 输入/路径/写入/增量状态损坏 |
| 007 | filter/sort 配置或字段/operator 类型不支持 |
| 008 | 正式 CRUD 意外生成/消费 legacy serialization/provider，或 JSON root 与 action manifest 不一致 |

运行期诊断区分 MissingBody/Input/OutputEnvelope/VoidContract、OutputTypeMismatch、RegistryNotReady/AlreadyBuiltMissingContribution，带 owner/action/type；取消继续传播，不能改写成 JSON 错误。

迁移：

1. 新项目使用真实 GenerateCrudService(typeof(Entity)) service 声明 + 标准工具链 + AddGeneratedCrud。
2. 现有手写 DTO/独立 Contracts 无需自动搬迁；转换为自动生成时先明确 owner/FQN，禁止同类型两程序集并存后让 resolver 碰运气。
3. 旧 generated CRUD provider、重复 DTO emitter、串行 driver 专用测试按消费者盘点归档；#warning 只在正式替代主链及证据成立后移除。
4. 更新 AGENTS 的实际 Attribute 表、memory/#123 evidence matrix 与 migration 文档；从“生成器测试编译”改为准确支持声明。
5. #129 消费这条已完成的 generated CRUD/JSON 策略，迁移 TicketApi 与剩余旧 Controller；不把 #128 的 API/JSON 缺口延期给 #129。

边界测试至少禁止正式 CRUD 产物调用 DynamicApiGeneratedRuntime 的 JSON helpers、CompatibilityBodyReader、无 JsonTypeInfo 的 Results.Json/serializer、DynamicApiResponse<object>、runtime reflection QueryExecutor；允许有证据且明确隔离的 legacy 消费者。

## 10. 验收矩阵

| ID | 必须证明的行为 |
| --- | --- |
| D01 | 全新外部项目只引用真实框架包/声明实体+partial service，一次 build 自动形成 DTO/service/mapper/projection/JSON；无 test-only Attribute 或预填 obj |
| D02 | SDK/单个真实 GeneratorDriver 中同时运行所有参与 SG + 官方 STJ，不能依赖串行 driver；mapping 和 context 均真实编译 |
| D03 | [Entity] + CRUD/多实体/同短名跨 namespace、引用实体/partial DTO 扩展，owner 唯一且冲突有明确诊断 |
| D04 | Feature.Application → Host 的 project-reference 和 packaged Feature → 外部 Host 两种路径；Host 不重生成 Feature DTO，不手写根表 |
| J01 | Create/Update body、update/delete input envelope、DTO/page/typed result/void roots 全闭合；移除任一方向仍确定失败 |
| J02 | result 类型保持泛型 T；ctx.Output 类型不符拒绝，未生成 object envelope；未知 member/converter 不 fallback |
| J03 | Host A/B 不共享可变 options/codec，未激活 Feature 不补齐另一 Host；注册顺序、重复 activation 等价 |
| H01 | 原生 POST Create → GET → PUT Update → POST search → DELETE → GET 404，断言字段值/nullable/enum/decimal/Unicode/响应结构/调用次数与状态 |
| H02 | required 空/空白/null/坏 JSON/错误字段类型、无效 Id、缺并发 stamp：确定 4xx 且零业务写；请求取消零后续写 |
| H03 | 缺 body/input/result/void metadata 在业务前拒绝；负例仍激活真实 descriptor 与 binding，不能以“无端点”满足断言 |
| Q01 | 原生 filter/sort/page，含非空 Filters/Sorts、默认排序、decimal/Guid/enum/null、In；非法值不忽略、无全表退化 |
| B01 | no-op 增量输出不变；Entity/DTO partial/reference assembly/options 变化使 DTO/JSON 同步；声明移除/改名/禁用不会留下旧端点 |
| B02 | clean/rebuild、输出文件丢失但 stamp 尚在、跨项目引用 schema 变化、并行 TFM/RID 均正确 |
| B03 | DesignTimeBuild 无过期 surface、不写正式成功 stamp；包/仓库 transport 互斥且 Release 工具路径正确 |
| R01 | 现有 JsonContracts、LocalEvent、RabbitMQ、手写 compatibility 原生 HTTP、UoW 回归保持；受影响共享文件必须重验 |

验收不能只检查“生成了文件/含 JsonSerializable”。必须请求真实端点、核对数据结果、确认错误未进入业务、验证返回信封内容。

## 11. 原生 fixture 与证据声明

新增独立 GeneratedCrud.AotFixture + Tests（具体程序集名在 S1 固定），复用现有 HTTP native gate 的发布/端口/readiness/超时/进程回收/evidence 设施，不能拿 WebApplicationFactory 代替。

- 第一组 linux-x64，Domain → Feature.Application → Host；真实生成 DTO、service、mapper、投影、JSON、query adapter。使用明确声明能力的 memory repository/UoW 与确定性身份/权限设施，只替换外部设施，不手写等价 CRUD service，不删正式调用来过门禁。
- 显式 CrestCreatesPublishMode=aot、JsonSerializerIsReflectionEnabledByDefault=false；当前 head 的 Release publish、native link、运行原生产物和 loopback HTTP 请求全部完成。
- 每个验收组输出独立 scenario:PASS，另有总 sentinel；harness 同时断言 HTTP status/body/副作用/进程已回收，缺一项不得泛化 OK。
- 证据包含完整 SHA/RID/SDK、publish 命令/退出码、native 产物、请求 transcript、断言/失败原因、进程回收结果。坏预期/缺 metadata 负例必须让门禁红且留下失败证据。
- 独立 JIT 集成可复用既有实际认证/tenant/UoW 测试；其证据不能变成 native fixture 覆盖声明。
- NativeAOT-verified 仅限实际组合，不推导 EF/FreeSql/SqlSugar、所有验证器/converter、生产认证审计持久化或整个 Sample。未验证项目明确写未验证。
- CI 保持当前暂停；先本地交付可执行命令与证据，之后是否恢复 CI 由用户决定。

## 12. 实施切片与审查门禁

| 切片 | 交付 | 退出 |
| --- | --- | --- |
| S1 事实锁定/设计/基线 | 真实 Attribute/owner/projection 模型编译样例；真实 MSBuild+STJ 失败用例；response/page/query 最小负例；完整迁移表 | 失败原因纯净；新设计审查通过后才推进 S2/S3 |
| S2 precompile 与 JSON 契约 | 共享模型/emitters、BuildTasks 阶段、正式入口、Compile hook、CRUD roots/closure、package/incremental/design-time | D/B/J 契约通过，单 driver 不靠残留 obj；运行时切换未完成部分标清 |
| S3 正式 typed HTTP/执行链 | compatibility 投影唯一装配、typed input/result、完整 startup validation、受管 CT/UoW、有限 query adapter | 真实 JIT 端点与负例通过，旧 CRUD fallback 消费清零，执行/提交/响应事实不混同 |
| S4 原生与迁移收尾 | native HTTP 场景、跨项目/包证据、受影响回归、归档/文档/矩阵/#129 交接 | 最终 head 原生发布链接请求通过；证据与支持声明一致；实现审查闭环 |

可以一个 PR 按 S1–S4 分提交；不合并“删 warning 但正式链尚不可用”的过渡版本。不新增自动触发标签，不臆定负责人/截止日期，不自动派发开发或合并。共享 CodeGenerator/JsonContracts 改动基于本次合并基线串行落地。

## 13. 参考

- [官方 STJ source generation：ASP.NET Core 与多个 context/resolver 组合](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)。它支持 source-generated resolver 链，但不能替代前置类型可见性、typed root 校验和本项目的实际原生证据。
- 仓库相关设计：2026-07-24 control-plane-json-contract-root-generation；2026-07-09 phase-8d appservice compatibility projection；2026-05-07 dto-mapping；2026-10-14 #146 hardening rev.3。
