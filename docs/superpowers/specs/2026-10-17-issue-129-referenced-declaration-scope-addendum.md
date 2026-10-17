# #129 设计增补：引用程序集上的 service/contract/DTO 声明范围

- 状态：设计增补（待审查）。上游：`2026-10-17-issue-129-generated-http-controller-exit-design.md` rev.2 §4.1/§8.2/§14.4/§14.7.3。
- 定位：这是 **S2 实现的前置设计门禁**，不是 S2 实现。本文件只冻结声明范围、owner 边界、身份字段与取消语义，不声称任何 API 已存在。
- 证据口径：下文 file:line 均为当前代码事实（基线 `196b6f66` 起的同一实现面），S1 事实见 `2026-10-17-issue-129-s1-callgraph-and-baseline.md`。

## 1. 要解决的问题

rev.2 §14.4 要求「引用程序集上的真实 service/DTO/contract 是 B04 必测输入」，并禁止「用同名 stub」或「进入实现后用反射补洞」。当前声明面能不能表达「HTTP owner A 投影引用程序集 B 里的业务服务」？如果不能，必须先冻结最小增补，而不能在 S2 里边写边定。

## 2. 当前可表达性（事实）

| 事实 | 证据 | 含义 |
| --- | --- | --- |
| `CrestServiceAttribute` 只能标在 class 上 | `src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/CrestServiceAttribute.cs:4-5` | 声明目标是「本编译内的类型」，没有程序集级入口 |
| `CapabilityCompatibilityProjectionAttribute` 只能标在 Class/Method，属性只有 `CapabilityIdPrefix`/`RoutePrefix` | `.../CapabilityCompatibilityProjectionAttribute.cs:9-25` | 不能命名一个外部契约类型，也不能声明「被投影的是别的实现」 |
| 生成器用语法提供器扫描**本编译**的 class 声明 | `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/AppServiceCompatibilityGenerator.cs:40-64` | 引用程序集里的 `[CrestService]`/投影声明不可见 |
| 契约类型来自 `EnumerateContractTypes(symbol)`（实现类自身 + 其接口/基类） | `...AppServiceCompatibilityGenerator.cs:54-62`、`DynamicApiConventionAnalyzer` | 契约接口可以在引用程序集里（metadata symbol 可解析）；受限的是**实现类**必须在当前编译 |
| 现有真实消费者用「Host 内 facade」绕开该限制 | `samples/AssetManagement/.../Projections/AssetCompatibilityProjection.cs:8-9`、`samples/ProcurementApproval/.../Projections/ProcurementAppService.cs:8-9` | 今天引用服务只能通过本地 facade 表达，且 facade 自己实现契约 |
| `[CrestService]` 会触发业务 DI 自动注册 | `src/Tooling/CrestCreates.CodeGenerator/ServiceGenerator`（`AutoServiceRegistration.g.cs` 产物） | 若把「本地标记类 + `[CrestService]`」当投影锚点，会顺带注册一个假服务，必须避免 |

结论：当前**不能**声明「投影一个引用程序集里的实现」；可行的最小方向是保留「声明位于当前编译」这一既有语义，用 `ContractType` 指向（可能位于引用程序集的）契约，并允许一个**不实现契约的本地标记类**作为投影锚点。

## 3. 冻结决策

### 3.1 声明形态（单一链，不新增程序集级 Attribute）

```csharp
// 位于 HTTP owner（生成胶水所属程序集）的本地标记类，不是业务实现，也不会被 DI 注册
[CapabilityCompatibilityProjection(
    ContractType = typeof(IShippingQuotes),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/shipping-quotes")]
public static class ShippingQuotesHttpProjection
{
}
```

- `ContractType` 必须是**可访问的闭合接口**（允许位于引用程序集，允许继承接口；rev.2 §4.1 已冻结该要求）。
- 标记类规则：
  - 带 `ContractType` 时**不要求** `[CrestService]`；此时 CEP030（「投影属性必须配合 `[CrestService]`」）收窄为「无 `ContractType` 的旧静态模式才报」。
  - 生成器只把它当声明锚点：不注册 DI、不实例化、不枚举它自己的公开方法（方法集合只来自 `ContractType` 及其继承接口）。
  - 与现有 facade 并存：若类实现了 `ContractType`（Asset/Procurement 形态），保持今天的行为（业务 DI 仍由模块/`[CrestService]` owner 负责），HTTP 激活不接管实例。
- 明确不做：程序集级 Attribute、扫描引用程序集、`Type.GetType`/反射发现、把 `[CrestService]` 放宽到 Interface。

### 3.2 DI 契约与缺失诊断

- 契约必须由**业务 owner**（被引用服务所属模块/Feature）以 `ContractType` 注册；HTTP owner 不注册、不替换、不生成第二个实例。
- 生成物在 Host 装配期校验「必需契约可解析」（与 rev.2 §14.4 的 required identities 同批），缺注册 → **启动期** `CCHTTP007`，不是首个请求的 500；校验不打开请求事务。
- 同一 `ContractType` 被两个 HTTP owner 声明 → `CCHTTP003`（identity/owner 冲突），不按发现顺序覆盖。

### 3.3 身份与 manifest（沿用 #128 形状，增加引用程序集维度；产物所有权与引用身份分开）

Action identity 输入冻结为：

```
httpOwnerAssemblyName          // 生成胶水所属程序集（本地声明所在程序集）
contractAssemblyName           // ContractType 的 defining assembly（允许是引用程序集）
ownerServiceFullMetadataName   // ContractType 的完整 metadata name（含泛型闭合参数）
closedContractMethodSignature  // 闭合方法签名（参数/返回/CT 形态）
configuration                  // RoutePrefix/Verb/Kind/BodyPolicy 等冻结配置
targetFramework, runtimeIdentifier
```

**产物所有权**（对应审查发现的 CCCRUD008 混入问题）：

- `DeclaredTypeFullNames` **只能**记录该 artifact 自己**声明/生成**的类型；不得混入引用程序集里的 DTO、枚举或契约。原因：CCCRUD008 的权威对账是把该字段与 `CrudArtifactEmitter.DeclaredTypeFullNames(artifact.Source)` 逐项比较（`src/Tooling/CrestCreates.JsonContracts.BuildTasks.Core/Semantic/CrudJsonSurfaceDiscovery.cs:184-233`），混入引用类型会直接触发 `CRUD artifact declared types or content hash do not match the current producer model`。审查实测：混入引用类型的正反两例 2/2 按此规则判定。
- 引用类型**单独**记录：新增并列字段 `ReferencedTypeFullNames`（纯 FQN 列表，来源于 action model 的符号身份），与 `DeclaredTypeFullNames` 分开序列化；契约的程序集身份由 `ContractAssemblyName` 记录。
- 每个 `ReferencedTypeFullNames` 条目在生成期必须以 symbol identity 解析为**唯一定义**（引用程序集 + 完整 metadata name + 泛型闭合参数）；解析不唯一或落在当前编译之外无法访问 → `CCHTTP001/004`，不写入 manifest。
- artifact entry 字段沿用 #128：`OwnerAssemblyName`/`OwnerServiceFullName`/`ArtifactHintName`/`DeclaredTypeFullNames`/`SourceLocation`/`ModelSchemaVersion`/`ContentHash`，并并列新增 `ContractAssemblyName` 与 `ReferencedTypeFullNames`；owner、contract 与引用类型三者不合并成一个字符串。
- 组合输入信封的命名与所有权规则不变（owner = HTTP owner 程序集 + action identity 的确定性编码）；引用程序集只贡献类型身份，不贡献信封 owner。

### 3.4 JSON 闭包

- JsonContracts 的输入已包含 `@(ReferencePathWithRefAssemblies)`（`build/CrestCreates.JsonContracts.Build.Common.targets:74-85`），因此引用程序集里的 DTO/枚举可以被发现；但必须显式验证：
  - 引用程序集 DTO 出现在 roots 与官方 STJ metadata 中；
  - 引用程序集里的同名短类型不与本地类型串 owner（B04 负例）；
  - 引用程序集不可写时仍能生成（不在引用程序集目录落文件）。
- 私有/内部 DTO 的可见性与 `InternalsVisibleTo` 不改变本决定：不可访问 → `CCHTTP001/004`。

### 3.5 取消处理（按原始取消源分类；分类与响应抑制分开）

**分类只看原始取消源，不看 linked token 是否取消。** 定义：

- `R` = `HttpContext.RequestAborted`（调用方中止源）
- `K` = 受管内核 token（deadline / 事务执行期限源）
- `L` = `CreateLinkedTokenSource(R, K).Token`（binder/invoker 实际传给业务的 token）

deadline 到期会同时取消 `K` 与 `L`，所以「`L` 已取消」不能作为调用方中止的依据；必须按 `OperationCanceledException.CancellationToken` 的身份回指到 `R` 或 `K`，再按下面的顺序判定：

```
1. OCE.Token == R                      → CallerAbort
2. OCE.Token == K                      → ManagedDeadline
3. OCE.Token == L                      → R.IsCancellationRequested ? CallerAbort : ManagedDeadline
                                         （两者都已取消时：CallerAbort 优先，同时记录 CallerAbort+Deadline）
4. OCE.Token 是第三个 token，或为 None  → NonCaller（token 身份缺失不构成调用方意图的证据）
5. 分类失败/无法读取 token               → NonCaller（随后按安全 500 处理）
```

**分类与响应抑制分开**：

- 响应抑制只看传输状态：`R.IsCancellationRequested` 或响应已开始 → 任何分类都**不写 JSON 错误体**、不二次写入；否则按分类写（`CallerAbort` 永不写体；`ManagedDeadline`/`NonCaller` → 500 `Crest.InternalError` 脱敏）。
- 因此「客户端同时断开 + 业务抛第三 token 的 OCE」会被分类为 `NonCaller`（内部故障事实），但响应仍因 `R` 已取消而被抑制；不得把这种情形改写成 `CallerAbort` 归因。

| 场景 | 分类 | HTTP 状态与响应 | UoW/事务事实 | 禁止 |
| --- | --- | --- | --- | --- |
| 调用方中止（仅 `R` 取消） | `CallerAbort` | 不写 JSON 错误体；响应未开始则连接中止，已开始不二次写入 | 按 #137 放弃/回滚未提交事务；Committed 保留 | 包装成 4xx/5xx JSON；重试或重执行 |
| 受管 deadline（仅 `K` 取消，`R` 仍存活） | `ManagedDeadline` | 响应未开始：**500 `Crest.InternalError` 脱敏**；已开始：中止连接 | `Unknown`/`Committed` 保留，不改为回滚成功 | 新 408/504 语义；重执行 |
| `R` 与 `K` 同时取消 | `CallerAbort`（优先级规则），事实同时记录 `CallerAbort+Deadline` | 按调用方中止抑制响应 | 同上，事实独立保留 | 只记录单一来源、丢失 deadline 事实 |
| 非调用方 OCE（第三 token / None） | `NonCaller` | 未抑制时 500 `Crest.InternalError` 脱敏；`R` 已取消则抑制响应 | 未提交则回滚 | 当作调用方取消静默丢弃响应 |
| 提交后取消 / 响应写失败 | 依上述分类，但结果以已提交事实为准 | 不改写已提交结果 | Committed 保留；通知失败不回滚 | 因响应阶段失败回滚业务 |

补充：

- **绑定/验证阶段使用同一规则**，不按阶段无条件归为调用方中止：绑定阶段出现 `OCE(R)` → `CallerAbort`（无业务、无事务、无响应体）；`OCE(K)` → `ManagedDeadline`（500 脱敏，前提是响应未开始且 `R` 未取消）；第三 token/None → `NonCaller`（500 脱敏）。绑定失败的普通异常仍按 converter 的既有权威分类。
- 同步 `Execute<TResult>` 与异步 `ExecuteAsync` 共用同一分类器；不得把取消状态复制到全局字段或跨请求缓存。
- 分类器只消费原 Exception、`CapabilityExecutionContext` 中的 `R`/`K` 身份与传输状态，不携带任意 HttpStatusCode。

## 4. 验收与探针计划（S2）

| 编号 | 必须证明 |
| --- | --- |
| R-01 | 引用程序集里的投影声明**不被**消费编译发现（纯净事实）；本地 facade/`ContractType` 标记才是唯一入口 |
| R-02 | `ContractType` 指向引用程序集契约时，投影只枚举该契约（及其继承）方法；标记类自身方法不暴露 |
| R-03 | 引用程序集 DTO 的真实 JSON 闭包（作为 `[JsonSerializable]` root 且官方 STJ 产出 metadata） |
| R-04 | 缺契约 DI 注册 → 启动期 `CCHTTP007`，零路由、零请求期 500 |
| R-05 | 同短名类型跨程序集不串 owner（B04）；身份包含 `contractAssemblyName` + 闭合签名 |
| R-06a | 调用方中止：无 JSON 错误体、业务只观察到一次取消、无重执行、`#137` 未提交事实按放弃/回滚处理 |
| R-06b | 受管 deadline：响应未开始时 500 `Crest.InternalError` 脱敏；`Unknown`/`Committed` 事实不被改写 |
| R-06c | 非调用方 OCE（第三 token / None）：判定为内部故障（500 脱敏），不得当作取消静默丢弃响应 |
| R-06d | linked token 身份回指：deadline 仅取消 `K` 时不命中 `CallerAbort`；`R`+`K` 同时取消按优先级归 `CallerAbort` 且事实同时记录 |
| R-06e | 绑定/验证阶段沿用同一分类：`OCE(R)` 无响应体、`OCE(K)` 500 脱敏、第三 token/None → 500 脱敏 |

本轮先交付 R-01 与 schema/fixture 的冻结；R-02–R-06c 进 S2/S3 实现门禁。

### 4.1 验收矩阵同步（覆盖 rev.2 §10 的对应行）

| 既有行 | 增补后的含义 |
| --- | --- |
| H02（输入拒绝） | 增加「绑定阶段取消」：无业务调用、无事务；响应体与状态按 §3.5 的同一分类规则（`OCE(R)` 无体、`OCE(K)`/第三 token → 500 脱敏），不按阶段统一归因 |
| H07（UoW 事实） | 增加 R-06a–e：调用方中止 / 受管 deadline / 非调用方 OCE / 同时取消优先级 / 绑定阶段同规则；HTTP 状态与事务事实各自固定，取消不重执行 |
| N01/N02（原生门禁） | 原生场景增加一条调用方中止与一条非调用方 OCE，验证分类不依赖异常类型字符串 |
| D01（文档交接） | 迁移指南写明「deadline 对外为 500 脱敏」「调用方中止无错误体」两条客户端可见行为 |

本增补对 rev.2 §10 的扩展以本节为准；未列出的行不变。

## 5. 影响面

- Domain.Shared：`CapabilityCompatibilityProjectionAttribute` 增 `ContractType`/`RegistrationMode`（additive，旧声明行为不变）；`AppServiceHttpActionAttribute` 仍按 rev.2 §4.1 冻结。
- 生成器：新增 ContractType 模式的声明锚点识别与 CEP030 收窄；既有 facade 模式不回归。
- 工具链：manifest/identity 增 `ContractAssemblyName`；JsonContracts 输入不变（已含引用程序集），新增引用程序集场景的发现与归属校验。
- 契约 fixture：`docs/superpowers/specs/fixtures/issue-129-projection-contracts.json` 增 `referencedAssembly` 段与 identity 输入（本轮已同步）。
- 不做：程序集级声明 API、引用程序集扫描、反射补洞、把本增补当作 S2 已实现。
