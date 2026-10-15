# #128 问题记录与实施日志

日期：2026-10-14。基线：合并后 master `de50b33765fbdf8992c77ff15a126147c4cfeb7a`（PR #148 `0c6e574a` LocalEvent 硬化 + PR #149 文档同步）。
详细设计：[Spec](../specs/2026-10-14-issue-128-crud-json-compilation-contract-design.md)（Issue 评论 rev.1 逐字入库）。
实施计划：[implementation](2026-10-14-issue-128-crud-json-compilation-contract-implementation.md)。

## 当前状态（2026-10-14）

- 设计已发布并入库（Issue 评论 6165502876，rev.1）；**设计待审查，未启动实现**；设计审查通过后才推进 S2/S3。
- 在途 PR 盘点（S1 第 1 项）：无 open PR，无并行 CodeGenerator/JsonContracts 改动，基线串行安全。
- CI 保持暂停；不自动合并。

## S1 事实锁定

### 开工盘点（2026-10-14，master `de50b337`）

- 在途 PR：无 open PR；无并行 CodeGenerator/JsonContracts 改动，本分支可串行落地。
- **正式 Attribute 缺失**：`GenerateCrudServiceAttribute` / `GenerateEntityAttribute` 的唯一定义在 `tests/.../TestHelpers/SourceGeneratorTestHelper.cs:110/134`（测试 stub），Domain.Shared 无正式定义；正式 `EntityAttribute` 存在但不触发 CRUD 生成（生成器按 metadata full name 检测 stub 同名类型）。
- **#warning 位置**：`CrudServiceSourceGenerator.cs:987`（生成器源码自身），其后生成 `[ModuleInitializer]` 注册的 `IDynamicApiGeneratedProvider`（legacy bundle 形状），说明当前正式路径仍是 module-initializer + legacy provider。
- **探测入口**：生成器 `HasGenerateCrudServiceAttribute`（`CrudServiceSourceGenerator.cs:66`）；required types 检查在 `:241`。
- **Compatibility/JSON 运行面**：`CompatibilityBodyReader.ReadBodyAsync<T>`、`CompatibilityHttpResultMapper.WrapResult<T>`、`DynamicApiGeneratedRuntime.ReadBodyAsync/WrapResult`（legacy helpers 实际承载 CRUD body/result）；`AppServiceCompatibilityResultContractEmitter` 位于 CodeGenerator。
- **查询面**：`FilterDescriptor` 在 `Application.Contracts/DTOs/Common`；反射式 `QueryExecutor<T>` 在 `Application.Contracts/Query`，`CrestAppServiceBase.cs:222` 调用 `ApplyFilters`。
- **JsonContracts 接入点**：`CrestCreatesJsonContractGenerationDependsOn` hook 已存在于 `CrestCreates.JsonContracts.BuildTasks/build/CrestCreates.JsonContracts.Build.Common.props/.targets:73`，可直接建立显式 DependsOnTargets。

### S1 增量 1：正式 Attribute 与真实单 driver 基线（2026-10-14）

- **正式 Attribute 入库**：`src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/GenerateCrudServiceAttribute.cs`（sealed；`ctor(Type entityType)`；RoutePrefix/DtoNamespace/CapabilityIdPrefix/ExcludeProperties/FilterableProperties/SortableProperties，与 Spec §5.1 表面一致）。
- **真实单 driver 基线**：`tests/Tooling/CrestCreates.CodeGenerator.Tests/CrudServiceGenerator/CrudServiceRealDeclarationBaselineTests.cs`——自建引用闭包（真实 Domain.Shared + 框架引用 + 完整默认 facade 集）、**不注入 helper stub、不串行 driver**，一个真实 `CSharpGeneratorDriver`：
  - 绿灯（契约探针）：`[GenerateCrudService(typeof(Product))] partial ProductAppService` 在真实引用下编译通过（作者树零错误）。
  - **约定红（纯净原因）**：生成 DTO 命名为 `ProductAppServiceDto` 且位于 `Shop.Application.Products.Dtos`——当前生成器把被标注的 service 类当作实体；设计契约要求 `ProductDto`（来自 `typeof(Product)`）。
- 计数：CodeGenerator 全套 **369 通过 + 1 条约定红（370）**，无回归（既有 helper stub 用例不受正式 Attribute 影响——源声明优先遮蔽引用程序集）。
- 暂缓并记录：legacy `ModuleInitializer` provider 断言在当前闭包下空真（`HasDynamicApiSupport` 需要 ASP.NET 类型），按“失败原因纯净”原则不保留空真基线；由 S1 增量 2 的**真实 MSBuild/官方 STJ 复现**（真实消费项目闭包）承接。

### S1 增量 2：请求/响应/查询基线、诊断与 schema 冻结（2026-10-14）

- **请求基线（红）**：`Formal_crud_request_binding_must_not_route_through_the_legacy_body_helper`——真实声明生成的正式产物仍以 `DynamicApiGeneratedRuntime.ReadBodyAsync` 绑定请求体（设计：typed `CapabilityEndpointBodyReader` + required 语义，空/空白/null/坏 JSON → 400 零业务）。harness 附**闭包前置断言**（`HasDynamicApiSupport` 所需类型全部可见，含从 ASP.NET 共享框架目录确定性加载 Http/Routing/Mvc.Core），杜绝空真。
- **响应基线（红）**：`Compatibility_result_contract_must_not_wrap_through_object_typed_output`——结果契约按 `WrapResult(ctx.Output, serviceProvider)` 以 object 静态类型包装（设计：按声明返回类型的受检 typed envelope）。
- **查询基线（红 ×2）**：`CrudQueryFilterWireBaselineTests`——真实 search payload（scalar 与同型 scalar array filter 值）今日在 `FilterDescriptor.Value` setter 处反序列化失败（JsonElement 被拒）；设计：有限值域归一器，值停留在有界表示（CLR 标量/数组或受限 JsonElement 暂存）。
- **诊断族冻结**：CCCRUD001–008 全仓零冲突，按 Spec §9 冻结登记（实施时登记到集中诊断目录；如冲突须整体调整）。
- **target/manifest schema 冻结**：`PrepareCrestCrudInputs` → `GenerateCrestCrudArtifacts` → `IncludeCrestCrudArtifacts` 三段接入既有 `CrestCreatesJsonContractGenerationDependsOn`；单一 `CrestCreates.Crud.g.cs` bundle + inputs/outputs manifests（owner、完整类型名、源位置、schema version、内容 hash）；compiler-visible marker 仅作核对，不单独作为完成证据。
- 计数：CodeGenerator **369 通过 + 3 约定红（372）**；DynamicApi **73 通过 + 2 约定红（75）**；无既有用例回归。

### S1 增量 3：真实 MSBuild/官方 STJ 复现（2026-10-14）

- `CrudJsonContractMsBuildBaselineTests`（CodeGenerator.Tests）：**自建真实消费项目**（仓库内 `tests/artifacts/` 临时目录，gitignored；构建后清理）——真实 `[GenerateCrudService(typeof(Product))] partial` 声明 + 官方 `[JsonSerializable]` CRUD 根（ProductDto/CreateProductDto/UpdateProductDto，`JsonSerializerIsReflectionEnabledByDefault=false`）+ 完整编译闭包（CodeGenerator 以 Analyzer 接入 + Domain.Shared/Domain/Application.Contracts/Authorization.Abstractions/Core.Abstractions/Aop/Validation），跑真实 `dotnet build`。
- **约定红（纯净原因）**：今天构建恰好以 **4 条 DTO 可见性错误**失败——`Shop.Application.Products.Contracts` 与三个 DTO 类型在同轮不可见（其余闭包全部编译通过，无其他错误）。设计契约：DTO 由前置物化进入普通 Compile，author/官方 STJ 根在同一构建内闭合、构建成功且生成上下文包含 `ProductDto`。
- 备注：去掉 ASP.NET/DynamicApi 引用是让失败原因保持单点（Service-as-entity 的 legacy bundle 另有 CS0535 阻断缺陷，属当前实现事实，随迁移记录，不影响本复现）。mapper/compatibility input/closed envelope 的补充负例需要 JSON 工具链闭包，与 S2 的 root 闭合一起落。
- 计数：CodeGenerator **369 通过 + 4 约定红（373）**，无既有用例回归。

### S1 增量 4：诊断登记、mapper 负例与信封基线（2026-10-14）

- **CCCRUD001–008 诊断登记**：`src/Tooling/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceDiagnosticCodes.cs`（const string 值）+ `CrudServiceDiagnostics.cs`（DiagnosticDescriptor 对象），按 Spec §9 冻结登记到集中诊断目录，全仓零冲突。
- **CCCRUD004 发射实现**：`CrudServiceSourceGenerator.ValidateEntityMapperClosure` 从 Attribute 构造参数提取真实实体类型（`ExtractEntityTypeFromAttribute`），校验 parameterless constructor 与 public setter 闭包；不满足时发射 CCCRUD004 并跳过该实体的生成，不产出空 mapping 假装支持。
- **mapper 负例基线（绿 ×2）**：`CrudServiceRealDeclarationBaselineTests` 新增——
  - `Entity_with_private_setter_properties_must_emit_CCCRUD004`：实体含 `private set` 属性，断言发射 CCCRUD004 且诊断消息识别该属性名。
  - `Entity_without_parameterless_constructor_must_emit_CCCRUD004`：实体无 public/protected parameterless constructor，断言发射 CCCRUD004 且诊断消息识别缺失构造器。
- **输入信封基线（红）**：`Formal_crud_must_emit_typed_input_envelopes_for_update_and_delete`——设计契约（§7.1）：Update/Delete 必须发射 typed input envelope 组合 route/body/header（CapabilityEndpointMapper 对实际 input 做 SerializeToElement）；当前生成器未发射 `UpdateProductInput` / `DeleteProductInput`。
- **响应信封基线（红）**：`Formal_crud_must_emit_typed_response_envelopes_as_json_roots`——设计契约（§7.1–7.4）：所有响应信封必须作为 JSON root 发射（`DynamicApiResponse<ProductDto>`、`DynamicApiResponse<PagedResultDto<ProductDto>>`、`DynamicApiResponse` void/404）；当前生成器通过 object 静态类型 `ctx.Output` 包装。
- 计数：CodeGenerator **371 通过 + 6 约定红（377）**；DynamicApi **73 通过 + 2 约定红（75）**；无既有用例回归。
- S1 退出状态：真实 Attribute/owner/projection 模型编译样例 ✓；真实 MSBuild+STJ 失败用例 ✓；response/page/query/input-envelope/response-envelope 最小负例 ✓；CCCRUD001–008 诊断登记与 mapper 闭包校验 ✓；完整迁移表待 S2 落。设计审查通过后可推进 S2。

### S1 增量 5：R1 审查修复（2026-10-15）

- **R1-CRUD128-01 [P2] 查询红例触达 Value setter**：`CrudQueryFilterWireBaselineTests` 的 `FilterProbeJsonContext` 增加 `[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]`，使 JSON 属性绑定与 wire 一致（camelCase `filters/field/operator/value`）。两条红例现在正确失败于 `FilterDescriptor.Value` setter 的 `ArgumentException: 不支持的过滤值类型: System.Text.Json.JsonElement`，而非 Filters 集合为空。
- **R1-CRUD128-02 [P2] MSBuild 基线分离命名与同轮不可见**：`CrudJsonContractMsBuildBaselineTests` 增加前置守卫——先验证 producer 确实运行并生成 `ProductAppService`（确认生成器运行），再检查 build 成功与 STJ 闭合。新增 `Real_msbuild_producer_output_must_match_formal_naming_contract` 控制测试，独立验证 producer 输出命名契约（`class ProductDto` + `namespace Shop.Application.Products.Contracts`，不含 `class ProductAppServiceDto`），将命名缺口与同轮 STJ 不可见分离为可归因证据。
- **R1-CRUD128-03 [P2] mapper 校验尊重 ExcludeProperties 与输出只读属性**：`ValidateEntityMapperClosure` 提取 `ExcludeProperties`（`ExtractExcludePropertiesFromAttribute`），仅校验参与 Create/Update 输入映射的属性——排除 Id（输出只读）、ExcludeProperties、导航属性、平台排除字段。新增两个正向基线：`Entity_with_protected_id_setter_must_not_emit_CCCRUD004`（Id protected setter 不触发）、`Entity_with_excluded_private_setter_must_not_emit_CCCRUD004`（排除字段 private setter 不触发）。
- **R1-CRUD128-04 [P2] 构造器校验仅接受 public**：`hasParameterlessCtor` 改为仅接受 `Accessibility.Public`（不再接受 protected），因 mapper 是静态类型 `new TTarget { ... }` 无法调用 protected 构造器。新增 `Entity_with_protected_only_parameterless_constructor_must_emit_CCCRUD004` 基线，验证 protected-only 构造器触发 CCCRUD004。诊断消息修正为 "public parameterless constructor"。
- **R1-CRUD128-05 [P2] 信封基线语义化**：输入/响应信封基线从字符串包含改为正则匹配类型声明结构（`(class|record)\s+UpdateProductInput\s*\{`、`JsonTypeInfo<...>\s+\w+`），杜绝注释假绿。修正 Create/Update 响应注释：typed DTO 响应（`DynamicApiResponse<ProductDto>`），非 void。
- 计数：CodeGenerator **374 通过 + 7 约定红（381）**；DynamicApi **73 通过 + 2 约定红（75）**；无既有用例回归。所有红灯失败原因纯净，符合声称的能力缺口。
- S1 退出状态：R1 五项 P2 全部关闭；设计审查通过后可推进 S2。

### S1 增量 6：R2 审查修复（2026-10-15）

- **R2-CRUD128-01 [P2] MSBuild 独立证明同轮不可见**：`CrudJsonContractMsBuildBaselineTests` 的 `JsonRootsSource` 改为引用实际生成名称（`Shop.Application.Products.Dtos.ProductAppServiceDto`），而非正式契约名称（`.Contracts.ProductDto`）。前置守卫确认 producer 生成 `ProductAppServiceDto` 后，build 失败原因现在可归因为 STJ 同轮不可见（SYSLIB1030/CS0246 on DTO types），而非命名错误。命名契约由独立控制测试 `Real_msbuild_producer_output_must_match_formal_naming_contract` 覆盖。
- **R2-CRUD128-02 [P2] 平台字段排除**：`ValidateEntityMapperClosure` 扩展 `defaultExcludedProperties` 集合，包含 Create/Update DTO 排除的全部平台字段（Id、CreationTime、CreatorId、LastModificationTime、LastModifierId、IsDeleted、DeletionTime、DeleterId、ConcurrencyStamp、TenantId）。新增 `Entity_with_private_setter_on_platform_fields_must_not_emit_CCCRUD004` 基线，验证 TenantId/CreationTime/IsDeleted 的 private setter 不触发 CCCRUD004。
- **R2-CRUD128-03 [P2] 信封基线使用 Roslyn symbols**：输入/响应信封基线从正则匹配改为 Roslyn symbol 验证——`compilation.GetSymbolsWithName("UpdateProductInput", SymbolFilter.Type)` 确认实际类型声明存在，杜绝注释假绿。响应信封基线同时保留正则检查 `JsonTypeInfo<...>\s+\w+\s*[=;]` 验证实际字段声明。
- 计数：CodeGenerator **376 通过 + 6 约定红（382）**；DynamicApi **73 通过 + 2 约定红（75）**；无既有用例回归。所有红灯失败原因纯净，符合声称的能力缺口。
- S1 退出状态：R1 + R2 全部 P2 关闭；设计审查通过后可推进 S2。

### S1 增量 7：R3 审查修复（2026-10-15）

- **R3-CRUD128-01 [P2] MSBuild 诊断归因**：`CrudJsonContractMsBuildBaselineTests` 增加精确 STJ 诊断检查（SYSLIB1030 或 CS0246 on DTO types），并验证 STJ 未生成 DTO metadata（`JsonTypeInfo<global::Shop.Application.Products.Dtos.ProductAppServiceDto>` 不存在）。无关编译错误不再能满足断言，必须证明 STJ 同轮不可见。
- **R3-CRUD128-02 [P2] 捕获更新后 compilation**：`RunRealDriver` 和 `RunRealDriverWithSource` helper 改为捕获 `out var updatedCompilation`，返回包含生成类型的 compilation。输入信封符号检查现在可以看到生成的类型。
- **R3-CRUD128-03 [P2] 响应信封符号断言**：删除源码正则匹配，改为 Roslyn symbol 断言——遍历所有 `INamedTypeSymbol` 的 `IFieldSymbol` 成员，检查 `JsonTypeInfo<T>` 字段的类型参数（`DynamicApiResponse<ProductDto>`、`DynamicApiResponse<PagedResultDto<ProductDto>>`、`DynamicApiResponse`）。注释无法通过符号断言。
- 计数：CodeGenerator **375 通过 + 7 约定红（382）**；DynamicApi **73 通过 + 2 约定红（75）**；无既有用例回归。所有红灯失败原因纯净，符合声称的能力缺口。
- S1 退出状态：R1 + R2 + R3 全部 P2 关闭；设计审查通过后可推进 S2。

### S1 增量 8：R4 审查修复（2026-10-15）

- **R4-CRUD128-01 [P2] MSBuild 读取时序与分别记账**：`CrudJsonContractMsBuildBaselineTests` 重写为「写入随机干净目录 → await `dotnet build` 完成 → 才读取该次构建的 producer/STJ 产物」；不再在 build 前枚举不存在的 obj（旧红例真实原因是 DirectoryNotFoundException，属 fixture 回归）。产物落点实测为 `obj/{cfg}/{tfm}/source-generators/<Analyzer>/<Generator>/*.g.cs`（仓库 props 已开启 EmitCompilerGeneratedFiles），读取按该布局过滤。四条声称分别记账：
  - `Clean_probe_directory_exposes_the_same_build_producer_output_and_a_sound_closure`（正向控制）：干净目录一次构建后可见 producer 输出；authored `Declaration.cs` 零诊断；任何 build 错误只能位于生成文件（不得来自 authored 输入或未解析引用）。
  - `Current_msbuild_build_must_not_depend_on_same_round_mapping_output`（红，新事实单独记账）：只写正式声明的消费项目也必须能构建——当前以 CS0117（`ProductAppServiceObjectMappings` 无 ToTarget/Apply）与 CS1061（`ProductAppService` 无 Id）失败，全部位于生成 bundle。这是设计警告的「把不可见性从 DTO 转移到 mapping」在真实 MSBuild 下的证据，不与 JSON roots 声称混例。
  - `Current_msbuild_build_fails_because_stj_generates_no_metadata_for_the_same_round_dtos`（失败事实）：按**位置归因**——authored `JsonRoots.cs` 上的诊断必须全部是 SYSLIB1030，并逐个点到声明的三个 root 类型；同一次构建的 STJ 输出存在（官方 STJ 确实运行）但不含 `JsonTypeInfo<global::Shop.Application.Products.Dtos.ProductAppServiceDto>`。无关错误（mapping 同轮不可见位于生成文件）无法满足该断言。
  - `Formal_msbuild_build_must_close_crud_json_roots_in_the_same_build`（正式端到端契约红，新增）：正式 root 名称下要求一次构建 exit 0、roots 零诊断、STJ 输出含 `JsonTypeInfo<...Contracts.ProductDto>` 真实 metadata。当前 exit≠0（CS0234/CS0246 + legacy bundle 错误），失败事实与端到端契约因此分开记账，不再混为一例。
  - `Real_msbuild_producer_output_must_match_formal_naming_contract`（命名红，保持）。
- **R4-CRUD128-02 [P2] 输入信封改为正式 owner/action 契约**：新增测试侧契约探针 `CrudFormalContractProbe`（`tests/.../CrudServiceGenerator/CrudFormalContractProbe.cs`）。锁定 S1 契约：capability input envelope 沿用既有共享 compatibility input 命名与归属规则 `CrestCreates.Generated.{Service}_{Action}_CompatibilityInput`（AppServiceCompatibilityGenerator 现行 `{symbol}_{method}_CompatibilityInput` 规则），owner=声明 service，Update 成员 `Id: global::System.Guid` + `Input: {owner}.Contracts.UpdateProductDto`，Delete 成员 `Id: Guid` + `IfMatch: string`（§7.4 route Id + If-Match；探针实体实现 `IHasConcurrencyStamp`），成员必须可读、非错误类型、静态类型按 symbol identity 比较，且 envelope 必须由 producer 声明并注册为官方 context 的 `[JsonSerializable]` root。红例先断言闭包守卫（引用程序集里 JsonSerializerContext/JsonTypeInfo`1/DynamicApiResponse`1/PagedResultDto`1 全部可解析），失败原因现为「producer 未声明 Update/Delete capability input envelope」。
- **R4-CRUD128-03 [P2] 响应 roots 按引用程序集 symbol identity 校验**：删除「字段短名 + JsonTypeInfo 名字匹配」。框架契约类型一律从**引用程序集**解析（`compilation.GetAssemblyOrModuleSymbol(reference)` 逐个命名空间走查，并排除 source assembly），闭合类型比较 `OriginalDefinition` + 类型参数 identity，ErrorType 直接拒绝；要求 producer 声明的官方 `JsonSerializerContext` 派生 context（partial 全部来自 producer 树，禁止手写 context），roots 必须包含 `ProductDto`、`PagedResultDto<ProductDto>`、`DynamicApiResponse<ProductDto>`、非泛型 `DynamicApiResponse`，并显式拒绝 `DynamicApiResponse<object>`。不再要求 metadata 必须呈现为字段形状（属性亦可）；「真实 metadata 已生成」的证据改由 MSBuild 端到端契约红承担（官方 STJ 在同一次构建产出 `JsonTypeInfo<...>`），Roslyn 层只证明官方 context 的 root 注册闭合。
- **反向对照入库（9 例，全部通过）**：输入侧空壳（ReviewUnrelated.UpdateProductInput/DeleteProductInput）、错误 owner、缺成员、错误成员类型、非 producer 所有；响应侧同名假契约（自带 JsonTypeInfo`1/DynamicApiResponse/PagedResultDto/ProductDto + 三个未初始化字段）、假 JsonSerializerContext 基类、官方 context 但零 root、只注册 `DynamicApiResponse<object>`。每例断言**目标原因**出现，杜绝因闭包缺失等旁因而假通过。
- 计数：CodeGenerator **386 通过 + 8 约定红（394，0 skipped）**；DynamicApi **73 通过 + 2 约定红（75，0 skipped）**；无既有用例回归。8 条红：DTO 命名（driver 与 MSBuild 各一）、legacy body helper、object 静态结果契约、输入信封、响应 roots、mapping 同轮不可见（MSBuild）、MSBuild 端到端契约。
- S1 退出状态：R1–R4 全部 P2 关闭；MSBuild fixture 时序回归已消除并新增干净目录控制运行；输入/响应契约均可拒绝同名假实现，反向对照与正向红例分别记账。设计审查通过后可推进 S2。

### S1 增量 9：R5 审查修复（2026-10-15）

- **R5-CRUD128-01 [P2] 响应 root 矩阵补全分页外层 envelope**：`ResponseRootViolations` 此前分别检查 `PagedResultDto<ProductDto>` 与 `DynamicApiResponse<ProductDto>`，但没有检查以分页类型为参数的外层 `DynamicApiResponse<PagedResultDto<ProductDto>>`——内层 payload 与单条 envelope 均已注册不代表分页响应 root 已注册，独立对照仅移除该 root 时 checker 返回零违规（假绿）。修订（`CrudFormalContractProbe.cs`）：
  - 必需响应 roots 改为**同一矩阵**维护（§7.1）：entity CRUD DTO、bounded page payload、typed DTO envelope、typed page envelope、void/404 envelope，另加已声明的 Create/Update/ListRequest 请求 payload roots；矩阵项缺失时报告该闭合类型的完整 FQN 与角色标签。
  - 闭合类型由引用程序集的 `PagedResultDto<T>`/`DynamicApiResponse<T>` 与 producer 的 `ProductDto` 逐层 `Construct` 得到；比较仍走 symbol identity（先 canonical identity，再 OriginalDefinition + 类型参数递归），未回退到短名或文本匹配。`SameType` 取代 `ContainsClosedRoot`，输入侧 root 判定同步采用同一比较。
- **正向与单项反例入库**：`Complete_declared_model_must_satisfy_both_response_and_input_contracts`（完整声明模型必须零违规，并断言该模型 compilation 无错误、闭包守卫为空——证明 checker 能接受正确模型，红例不是"永远不可能通过"）；`Response_root_contract_must_reject_each_single_missing_root`（8 例：dto/create/update/list/page/dto-envelope/page-envelope/void，每次只删一个 `[JsonSerializable]` 注册，必须点名该闭合类型）；`Input_envelope_contract_must_reject_a_missing_capability_input_root`（update-input/delete-input，声明存在但未注册为 root 必须被输入契约拒绝）。R4 的 9 例结构反例保持。
- **审查探针入库**：`tests/.../CrudServiceGenerator/ReviewR5ResponseRootProbes.cs` 逐字收录 R5 复审评论附带的探针（完整响应模型 / 仅移除分页外层 root / 完整输入模型）。在 `94c24031` 上前者为 pass、后者 fail（checker 零违规）；在当前 head 三项全部通过。
- 说明：本轮试图在本地临时把矩阵项置空以自证新反例的判别力，被权限分类器判定为"故意破坏测试"而阻止，未执行；判别力证据为审查者在本头的独立对照（零违规）+ 该对照用例在本头的失败事实，以及入库探针的原样复现。
- 计数：CodeGenerator **400 通过 + 8 约定红（408，0 skipped）**（含已入库的 3 个 ReviewR5 审查探针；主套件为 397 + 8 / 405）；DynamicApi **73 通过 + 2 约定红（75，0 skipped）**；无既有用例回归。8 条红与增量 8 登记的名称一致，未新增或改口。
- S1 退出状态：R1–R5 全部 P2 关闭；响应 roots 以矩阵维护且每项单独可验，真实 metadata 生成仍按约定由 S2 端到端红验收。

## S1 放行与 S2 交接（2026-10-15，R6 复审通过）

- R6 复审在精确 head `2ca5e9b3` 独立复跑：CodeGenerator **400 + 8 / 408**、DynamicApi **73 + 2 / 75**，root 删除矩阵/完整模型/既有反例全部通过，未发现新增 P1/P2。**S1 设计/基线门禁通过，可进入 S2**；放行不代表八条契约红已实现，也不代表 S2/S3/S4 或 NativeAOT 验收通过。
- S2 必须转绿的八条红（名称集合与上一轮完全一致，不重命名、不改口）：
  1. `Generated_dtos_must_be_named_after_the_entity_not_the_service`
  2. `Real_msbuild_producer_output_must_match_formal_naming_contract`
  3. `Formal_crud_request_binding_must_not_route_through_the_legacy_body_helper`
  4. `Compatibility_result_contract_must_not_wrap_through_object_typed_output`
  5. `Formal_crud_must_emit_typed_input_envelopes_for_update_and_delete`
  6. `Formal_crud_must_emit_typed_response_envelopes_as_json_roots`
  7. `Current_msbuild_build_must_not_depend_on_same_round_mapping_output`
  8. `Formal_msbuild_build_must_close_crud_json_roots_in_the_same_build`
- S2 范围（按既有 Spec/计划，不开第二条主链）：从同一 CrudGenerationModel/emitters 物化普通 Compile bundle，范围覆盖 DTO/interface/service/mapping seeds/compatibility input/result/activation（不能只搬 DTO 而留下 mapping 同轮依赖，即红例 7）；显式串起 CRUD precompile → Include Compile → JsonContracts → 官方 STJ/其余 SG 的单次 CoreCompile；旧 `CrudServiceSourceGenerator` 对正式声明收口为诊断入口；测试改为读取普通 Compile 的 producer 产物并复用本轮契约检查（不为 `GeneratedTrees` 非空保留旧 SG bundle 断言）；验证 clean/声明移除/缺输出修复/增量/design-time/TFM·RID/package 首次消费与 ownership·provenance。声明层正向模型只证明检查器行为，真实官方 metadata 闭合必须由正式 MSBuild 证据承担（红例 8）。
- DynamicApi 两条查询红仍属 S1 记账范围之外的 §8 工作，按计划在 S2/S3 的 filter 归一与编译期字段访问处处置；本轮不为其改口径。

## S2 增量 10：前置物化与 JSON 契约主链（进行中，未放行）

本轮落地 S2 骨架并跑到真实构建证据，**尚有 1 个未闭合缺陷**，因此标记为进行中、不宣称任何退出。

已完成并经构建验证：

- **共享模型（S2-1）**：新增 `src/Tooling/CrestCreates.CrudGeneration/`（纯源集，无 csproj），`CrudGenerationModel` + `CrudGenerationModelBuilder` + `CrudArtifactEmitter` 同一份实现 source-link 进 `CrestCreates.CodeGenerator`（netstandard2.0）与 `CrestCreates.JsonContracts.BuildTasks.Core`（net10.0）。模型一次定义：实体与声明 service 分离、默认 DTO 落 `<service ns>.Contracts`（`DtoNamespace` 覆盖）、Read/Create/Update 字段排除表、§7.4 五动作 route/permission/capability 表、capability input envelope 命名（沿用既有 `{Service}_{Action}_CompatibilityInput`）、CCCRUD001–008 纯数据诊断。正式声明只按 attribute 的 metadata full name + `typeof(Entity)` 构造形态 + symbol identity 识别；旧测试 stub 同短名不再被当作契约。
- **前置物化链（S2-2）**：Tool 新增 `prepare-crud`（`CrudPrecompileEngine` + transport，引用集合为空即 CCCRUD006 fail-closed；`CrestCreatesCodeGeneration=false` + 仍有正式声明 → CCCRUD002 确定性失败）；BuildTasks 新增 `WriteCrestCrudInputManifest`（源/引用**内容 sha256** + langversion/constants/nullable/TFM/configuration/model/tool 版本）与 `GenerateCrestCrudArtifacts`（路径信任、有界超时、write-if-changed、声明数>0 而 bundle 为空即失败、输出 owner/FQN/源位置/schema/content-hash manifest）；`build/CrestCreates.Crud.Build.Common.targets` 定义 `PrepareCrestCrudInputs → GenerateCrestCrudSurface → IncludeCrestCrudArtifacts` 并显式追加进既有 `CrestCreatesJsonContractGenerationDependsOn`（bundle 与 `source-generators` 目录被排除出下一次输入）；新增 `CrestCreatesCrudJsonToolchain` compiler-visible marker。
- **旧链路收口（S2-4）**：`CrudServiceSourceGenerator` 对正式声明只跑共享 builder 报 CCCRUD 诊断、不再 AddSource 任何产物；marker 缺失即 CCCRUD002。legacy/stub 旧 bundle 路径保留给尚未迁移的旧测试（S4 归档），未变成正式主链第二轨。
- **JSON 侧（S2-3，进行中）**：`CrudJsonSurfaceDiscovery` + `JsonContractRootSourceKind.CrudSurface` + writer 的 `WriteCrudRootBlock`：由同一模型在真实 compilation 中解析 DTO/Create/Update/ListRequest/两个 capability input envelope，并按引用程序集 open generic `Construct` 出 `PagedResultDto<Dto>`、`DynamicApiResponse<Dto>`、`DynamicApiResponse<PagedResultDto<Dto>>`、void envelope，写入一个 `CrestCreates.Generated.CrestCreatesCrudJsonSerializerContext`（camelCase，internal）；public DTO 与 internal envelope 的可访问性分别校验；缺声明报 CCCRUD002，缺引用报 CCCRUD005（且只在存在 CRUD 声明时要求，Abstractions 类项目不再被误伤）。

真实构建证据（干净 obj、`tests/artifacts/probe2` 仓库内探针，导入 Repository props/targets）：CRUD bundle 19KB 物化、inputs/outputs manifest 与 stamp 生成、DTO/mapping 被 JsonContracts 正常发现；既有消费者 `CrestCreates.DynamicApi`、`CrestCreates.Accountability.Abstractions`、`samples/LibraryManagement/LibraryManagement.Application` 均 Build succeeded，工具链新链对存量工程无附带破坏。

未闭合缺陷（已缩小，且存在**假绿风险**，不含糊）：

- 已修：JSON 发现侧原先拿 `model.EnvelopeName(...)`（**短名**）当 FQN 查类型，命名空间走查必然 0 命中，于是把 bundle 里真实存在的 capability input envelope 报成 CCCRUD002。现在改用 `CrudEnvelopeConvention.EnvelopeFullName(...)`；同时把 `Compilation.GetTypeByMetadataName`（歧义与缺失都返回 null）换成「源程序集 + 逐个引用程序集」的命名空间走查，多命中直接按 §9 原义报 CCCRUD003「一个 CRUD 契约类型只能有一个 owner」，并把无 CRUD 声明的工程从 envelope 引用检查里排除（此前 Abstractions 类项目会被误报 CCCRUD005）。
- 仍未闭合：修完后 `tests/artifacts/probe2` 变 Build succeeded，但生成的 `CrestCreates.JsonContracts.g.cs` 里 `CrestCreatesCrudJsonSerializerContext` 命中数为 **0**，说明 JSON 步的 `CrudGenerationModelBuilder.Build` 这次返回 0 个模型（更早一次它确实返回过模型并报出 envelope 缺失）。也就是当前"构建成功"不等于根闭合——这个绿不能被当作 red 8 的证据。
- 假绿被查出并修掉（关键）：`tests/artifacts/probe2` 之前 Build succeeded 却没有任何 CRUD context，真因是「JSON 无产物就早退」的判断写在了 engine 侧而不是 writer 侧——`JsonContractSourceWriter.WriteContextSource` 的 `Contexts/LocalEvent/IntegrationEvent` 全空即返回，把新加的 `CrudRoots` 分支整个跳过。条件补上 `&& model.CrudRoots.Count == 0` 后，真实构建一次即闭合：`CrestCreates.JsonContracts.g.cs` 内 `CrestCreatesCrudJsonSerializerContext` 注册 §7.1 全 10 个 root（含嵌套 `DynamicApiResponse<PagedResultDto<ProductDto>>` 与两个 capability input envelope），同一次 CoreCompile 里官方 STJ 产出对应 `JsonTypeInfo<…>` 元数据，无 SYSLIB1030。对账守卫（manifest owner vs JSON 发现 owner，缺失即 CCCRUD008）同时改为传绝对路径（task cwd 不可假设为工程目录）并纳入请求字段。
- 探针改造完成（S2-5 的 MSBuild 部分），5 条全部通过，且各自单独记账：干净目录控制（构建完成后才读同次产物、authored 零诊断、错误只可能位于生成文件）；producer 命名契约（bundle 含 `class ProductDto` + `.Contracts`，不含 `ProductAppServiceDto`）；同轮 mapping 依赖消失（exit 0）；端到端 root 闭合（10 个 root 注册 + STJ 真实 metadata + outputs manifest 的 owner 与 sha256 content hash）；反事实（不导入工具链时正式声明必须 CCCRUD002 失败、零物化、不得回退旧 bundle）——后者取代 S1「同轮不可见」失败事实用例，S1 的那条声称已被 S2 的实现取代，改记为反事实守卫，不保留旧口径。
- 口径更正：RS2008（analyzer release tracking）**不是 CCCRUD 引入的新债**——本仓库没有任何 `AnalyzerReleases.*` 文件，既有的 ADP/CEP 规则同样报警；因此改记为「仓库级既存缺口」，不在 #128 里偷偷补，也不谎称已处理。
- 死路记录（免得下一轮重走）：曾试着让 driver 层 harness 直接引用 `CrestCreates.JsonContracts.BuildTasks.Core`，好在测试内跑 JSON 阶段——不可行，因为共享模型是 source-link 进 CodeGenerator 与 Tool 两个程序集的，测试工程同时引用两者会让 `CrudGenerationModel` 之类类型 CS0433 歧义。结论：driver 层继续只主张 producer 结构（root 注册与真实 metadata 由 MSBuild 证据承担，现状即如此），若要在测试内跑完整两阶段，harness 必须**跨进程**调用工具（`dotnet exec Tool.dll prepare-crud|generate`，与 MSBuild task 同一条边界），而不是引用其类型；不在检查器上开洞。
- 本轮另修一处我自己引入的仓库级破坏：CRUD targets 里用了不存在的 MSBuild 函数 `HasPathRootedPath`，一度让所有导入 JSON 工具链的工程构建失败（`CrestCreates.Accountability.Abstractions` 首先命中），已换成 `$([System.IO.Path]::IsPathRooted(...))` 并复验 DynamicApi / Accountability.Abstractions / LibraryManagement.Application 均 Build succeeded。
- 本地提交 458b4c2f、cb92ec8f **未推送**：S2 未闭合前不把混合口径推给审查。

测试迁移（S2-5，部分）：driver 层 4 条契约红改为读取 producer 产物（`CrudPrecompileProducer`）；其中 `Formal_crud_request_binding_must_not_route_through_the_legacy_body_helper` 与 `Generated_dtos_must_be_named_after_the_entity_not_the_service` 因新产物真实转绿（断言的是物化 surface 含 `class ProductDto` + `.Contracts`，不含 `ProductAppServiceDto`，且不含 legacy body helper/CompatibilityBodyReader）；输入信封与响应 roots 两条仍红，原因即上面的未闭合缺陷。harness 里不再断言 bundle 编译错误——mapper 成员由 sibling generator 在真实 CoreCompile 里补，这条声称归 MSBuild 证据。

当前计数：CodeGenerator **405 通过 + 3 失败 / 408，0 skipped**；DynamicApi **73 通过 + 2 失败 / 75，0 skipped**；JsonContracts 单元 **131/131**；JsonContracts 真实构建 PackageTests **29/29**（含 package/repository transport、增量、multi-targeting、global usings、package layout 不泄漏 task 程序集）。3 条 CodeGenerator 失败：`Formal_crud_must_emit_typed_input_envelopes_for_update_and_delete` 与 `Formal_crud_must_emit_typed_response_envelopes_as_json_roots` 仍在 driver 层要求 context/roots，而 root 注册按新主链归 JsonContracts 阶段（真实构建证据已 5/5 通过），下一步按已记录的跨进程方案把两阶段跑进 harness，不在检查器开洞；`Compatibility_result_contract_must_not_wrap_through_object_typed_output` 属 S3 typed result 范围。PackageTests 不覆盖 CRUD 专属场景：bundle 缺失自愈、声明移除后空 bundle、design-time 轻量生成、CRUD manifest owner/provenance 对账、外部全新目录一次构建——仍属 S2-6 待做。
