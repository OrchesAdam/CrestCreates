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

### S2 增量 11：manifest-owner 跳过（①，完成）

- CRUD outputs manifest 的 `DeclaredTypeFullNames` 改为**纯完整类型名**（owner 仍是每条 entry 的兄弟字段），`IncludeCrestCrudArtifacts` 把该 manifest 作为 `AdditionalFile` 交给编译器（`Exists` 守卫 + 去重，不进 `Compile`）。
- 新增唯一归属读取器 `CrudArtifactOwnership`（`src/Tooling/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudArtifactOwnership.cs`）：`JsonDocument` 解析，返回 `ImmutableSortedSet<string>`(Ordinal)；无 manifest/解析失败/非本文件一律返回同一个共享空集，异常不外泄。**记录一处事实**：`ImmutableSortedSet` 本身不实现值相等（`Equals` 为引用比较），因此 pipeline 稳定性靠「唯一空集实例 + 确定序」保证，测试也按内容断言而非 Equals。
- `EntitySourceGenerator` 对 `{E}Dto`/`Create{E}Dto`/`Update{E}Dto`/`{E}ObjectMappings` 四个产出点按**该 FQN 是否被 manifest 拥有**逐条跳过；`ServiceSourceGenerator` 跳过被拥有的 `I{Service}` 接口文件并把被拥有的 service 排除出 `AutoServiceRegistration`（正式 CRUD 激活留给 `AddGeneratedCrud`）。其余 Entity/手写 service 行为不变。
- 新测试 `CrudArtifactOwnershipSkipTests`（11 例）：构造 `[Entity] Product` + `[GenerateCrudService(..., DtoNamespace="Shop.Catalog.Dtos")]` 让两者真的撞同一 FQN；断言「有 manifest 时那四个文件消失、repository/permissions/Extensions 与另一个实体的产物全在」「按产物粒度跳过」「无 manifest 时同一批文件仍在」——三条 `Without_the_manifest…` 用例就是判别力的一半；另有不可解析/外来 AdditionalFile 不得清空产出且不得冒 AD0001、reader 返回值稳定性；`CrudJsonContractMsBuildBaselineTests` 新增真实工程一例：同时 `[Entity]` + 正式 CRUD 声明必须零 CS0101/CS0102/CS0433/CS0121 且 `ProductDto` 只声明一次（实测重复表现为 CS0102，不是 CS0101）。
- 复核（作者本地独立复跑）：CodeGenerator **416 通过 + 3 失败 / 419，0 skipped**，三条失败正是既有 `Formal_crud_must_emit_typed_input_envelopes_for_update_and_delete`、`Formal_crud_must_emit_typed_response_envelopes_as_json_roots`、`Compatibility_result_contract_must_not_wrap_through_object_typed_output`，未改口径；JsonContracts 单元 131/131；DynamicApi 73 + 2 / 75。
- 既存环境敏感（非本轮引入，记录不掩盖）：`PackageTests.Build_InternalManifestRemainsAssemblyScoped` 断言 MSBuild 英文子串 "inaccessible"，在非英文 locale 下会假失败；`DOTNET_CLI_UI_LANGUAGE=en` 下该套 29/29。应改为断言错误码而非本地化文本，留作独立小修。

当前计数：CodeGenerator **405 通过 + 3 失败 / 408，0 skipped**；DynamicApi **73 通过 + 2 失败 / 75，0 skipped**；JsonContracts 单元 **131/131**；JsonContracts 真实构建 PackageTests **29/29**（含 package/repository transport、增量、multi-targeting、global usings、package layout 不泄漏 task 程序集）。3 条 CodeGenerator 失败：`Formal_crud_must_emit_typed_input_envelopes_for_update_and_delete` 与 `Formal_crud_must_emit_typed_response_envelopes_as_json_roots` 仍在 driver 层要求 context/roots，而 root 注册按新主链归 JsonContracts 阶段（真实构建证据已 5/5 通过），下一步按已记录的跨进程方案把两阶段跑进 harness，不在检查器开洞；`Compatibility_result_contract_must_not_wrap_through_object_typed_output` 属 S3 typed result 范围。PackageTests 不覆盖 CRUD 专属场景：bundle 缺失自愈、声明移除后空 bundle、design-time 轻量生成、CRUD manifest owner/provenance 对账、外部全新目录一次构建——仍属 S2-6 待做。


### S2 增量 12：整体审查修复、②生命周期与③两阶段 driver

基于 cf44e947，由当前任务独立实施与验证。①的逐产物 FQN 跳过方向保留，补齐以下审查发现：

- 输出完整性：原 stamp 只验证 bundle 是否存在，未绑定 outputs manifest。新增 `CrestCrudGenerationState`，把完整输入 manifest、bundle 与 outputs manifest 的内容 hash 绑定到成功 stamp；缺失/篡改任一输出即失效并重新生成。失败不写新成功状态。
- 增量身份：输入 fingerprint 补最终 generation-disabled 值、RID、task assembly hash；模型 schema 版本由 CLI 明确验证，未知版本 CCCRUD006。configuration/TFM/源和引用内容继续参与输入集合。
- 路径边界：bundle/input/output/stamp/temp 五种覆盖路径都按相对 SDK intermediate root 校验，拒绝仅字符串前缀相同的兄弟目录；不再留下 input/stamp 未校验的缺口。
- design-time：CRUD 与 JSON 都以当前输入生成到独立 `design-time/` 分区，真实 build 的输出/stamp 不被 IDE 复用或覆盖；IDE 控制按 SDK Compile/SkipCompilerExecution 执行。
- 实际装配：正式 CRUD analyzer 同时核对 transport marker、schema 合法的 manifest 归属与编译中真实物化类型；布尔 marker 单独不能使空声明通过。
- provenance：JSON 阶段按 schema、owner assembly/service、完整 hint/type 集合、artifact hash 做双向对账；缺 manifest、旧/额外 owner 或不匹配产物报告 CCCRUD008。框架 envelope 类型必须唯一来自引用程序集，源内同名 shadow 或歧义报告 CCCRUD003。
- ownership reader：精确文件名与支持的 schema 才可声明归属，异常/外来文件仍回到同一个空集；Entity/Service/正式 CRUD 管线使用内容 comparer，不能以非空 ImmutableSortedSet 的引用相等或“确定序”冒充增量值稳定性。

②：新增真实 CRUD 构建矩阵，覆盖 clean/no-op、bundle/manifest 缺失自愈、manifest 改动、保留时间戳的内容修改、源文件改名、声明移除、禁用生成/未知 schema、design-time 隔离、TFM/RID、五种路径边界、布尔 marker 反事实与全新外部包首次消费。TFM/RID 在独立 repository-transport 消费工程验证，应用依赖复制为 binary references，不把仓库全局 analyzer ProjectReference 的既有框架传播问题混成工具链失败。

③：`CrudPrecompileProducer` 通过生产 CLI 的 prepare-crud / generate 两个命令在子进程物化普通源码，再让官方 STJ 与 ObjectMapping generator 在一个真实 driver 中共同消费；测试仅 build-reference tool 项目，未将 source-linked Core 与 analyzer 共同引为运行时引用。原输入/响应 roots 两条基线转绿。CLI 负例覆盖缺 manifest、未知 schema、错误 owner/type/hash 与框架 shadow。

附带修复已记录的 PackageTests locale 假失败：断言 CS0122，替代本地化英文 inaccessible。未修改 CI，未实施 S3 的 object-result/有限 filter 归一或 S4 的 GeneratedCrud 原生验收。

验证（当前源代码）：定向 39/39；CodeGenerator 全套 444 pass + 1 个 S3 约定红 / 445，0 skipped；JsonContracts 单元 131/131；PackageTests 29/29（zh-Hans locale，CS0122 断言成立）；DynamicApi 73 pass + 2 个 S3 查询约定红 / 75。两个 driver roots 旧红已通过生产两阶段 CLI + 同一 STJ/ObjectMapping driver 真正转绿；未更改 S3 红例名称或含义。

- 最终补证：实现提交 2580129438a31af4ed2557595d3c9ce56e09b0e9，Release 两阶段 driver 3/3；LocalEvent 真实 NativeAOT publish/link/run 1/1（linux-x64，11 场景+sentinel，publish/run 均 0，无 IL2026/IL3050，evidence sha 精确绑定实现提交）。②③及本轮审查修复完成，S3/S4 尚未实施。报告 docs/review/2026-10-15-issue-128-s2-review-and-completion.md。

## S3 增量 1：FilterDescriptor 有限 wire 值域（两条记账红转绿）

- 新增 `FilterValueDomain`（`Application.Contracts/DTOs/Common`）：唯一的有界判定——null、标量、**同类**标量数组；对象、嵌套数组、异构数组、数组内 null 元素、非有界元素一律拒绝。`FilterDescriptor.Value` 的 setter 改用它，且显式允许**受限 JsonElement 暂存**进入（§8 明确推荐）。
- 新增 `FilterDescriptorJsonConverter` 并以类型级 `[JsonConverter]` 挂在 `FilterDescriptor` 上（不引入全局 `JsonConverter<object>`，也不需要 wire context 额外注册；源生成 STJ 元数据自动走它）：`field` 必须是字符串；`operator` 接受数字与名称、未定义值确定性拒绝；`value` 用 `JsonDocument.ParseValue` 读出后按有界判定，克隆脱离 pooled doc 再交给 setter 复验；未知成员忽略（前向兼容）。写路径：暂存 JsonElement 原样写出，CLR 值按标量渲染（TimeSpan 用不变文化的 `c` 格式）。
- **有意扩展，须记录**：CLR 域新增 `decimal`/`double`/`float` 标量。此前 `new FilterDescriptor("Price", Equals, 19.5m)` 会抛（`IsPrimitive` 不含 decimal），§8 的值域明确要求 decimal 且实体价格字段就是 decimal；仍是**有界**集合，未引入任意 object/converter fallback。这是行为放宽，不是回归掩盖。
- 拒绝语义与 §7.3 对齐：wire 侧拒绝是 `JsonException`，类型化 body reader（`ReadNativeBodyAsync` 的 required 语义）会把它变成 **400 且零业务**；S3-3 的 binder 直接吃到这条路径。
- 测试：DynamicApi 由 73+2/75 → **95/95**（两条记账红转绿；新增 `FilterDescriptorWireDomainTests` 20 例：数字精确文本原样保留——long 极值与 28 位 decimal、同类数组不被猜类型、CLR 兼容面、对象/异构/嵌套/null 元素/未定义 operator/非对象 filter 的拒绝对照、JsonElement 与 CLR 两种写往返、未知成员忽略）。
- 回归确认：CodeGenerator **444 通过 + 1 红 / 445**（唯一红仍是兼容投影的 object 静态结果契约，属 S3-2，未改口径）；JsonContracts 单元 **131/131**；PackageTests（真实消费工程编译）在本轮后台复跑，结果随后记录。

## S3 增量 2：typed result 契约（第 3 条记账红转绿）

- 运行时（`CrestCreates.DynamicApi`）新增 §7.3 的 typed、无 fallback overload：`WrapResult<T>(T, JsonTypeInfo<DynamicApiResponse<T>>)`、`WrapGetResult<T>(T?, JsonTypeInfo<DynamicApiResponse<T>>, JsonTypeInfo<DynamicApiResponse>)`、`WrapVoidResult(JsonTypeInfo<DynamicApiResponse>)`；全部直接 `Results.Json(envelope, info, statusCode)`，不经过旧的 `MapAotJson`（那条路径带反射 fallback，仍是 legacy 消费者专属）。
- 元数据访问走 `CapabilityEndpointJsonTypeInfoResolver.Require<T>(IServiceProvider)`：先在 host 的 `JsonOptions` 里解析，`NotSupportedException`（resolver 没有该类型元数据）与 null 一律收敛为**同一条确定性 `InvalidOperationException`**，这就是 §7.3 的 fail-closed；不会退到 reflection resolver 猜。
- 兼容投影的 result emitter 改为：非 void 先做**受检强类型转换**（`(T)ctx.Output!` / GET 用 `ctx.Output as T`），再调用 typed overload，并把 envelope 元数据从 `Require<T>` 取；旧的 `WrapResult(ctx.Output, serviceProvider)` 形态在正式 CRUD 生成物中彻底消失。既有正例测试里断言旧形态的三条断言同步改为 typed 契约（并新增“绝不出现 object 形态”的断言）。
- 测试：`TypedResultContractTests`（DynamicApi.Tests，5 例）用真实 DI host（`JsonOptions` 只挂源生成 context、无反射 resolver）执行 `IResult`：typed 结果 200 + camelCase body、GET null → 404 且无 data、void → 200 无 data、**缺元数据必须 fail-closed**、null 元数据引用的重载必须拒绝。DynamicApi 套件 **100/100**。
- 回归：AppServiceCompatibilityGenerator 定向 42/42（含转绿的记账红）；PackageTests 29/29（S3-1 的 FilterDescriptor 变更后复跑）；CodeGenerator 全套在本轮后台复跑，结果随后记录。

## S3 增量 3：ExplicitHost 激活面勘察结论与契约定型（S3-3/S3-4 执行依据）

勘察（读现有代码，不改码）确认的现状：

- 兼容投影全部发射 `[ModuleInitializer]` → **进程级静态注册**：`DescriptorProviderRegistry`（capability + endpoint descriptors）、`CapabilityEndpointBindingRegistry`（重复注册直接抛）、`CapabilityEndpointJsonContractRegistry`（body types）、`CapabilityHandlerResolverProvider`（invokers）、`CapabilityEndpointResultContractRegistration`（result mappers）、`AppServiceCompatibilityProjectionManifestRegistry`。没有一个 emitter 现在发 DI 扩展。
- 运行期：`CapabilityEndpointRegistry` / `CapabilityEndpointRegistryBootstrapper` / `CapabilityEndpointResultContractRegistry` 是 DI 单例（每 Host 一份 ✓），但 bootstrapper **只**读静态 provider bag；`EnsureBuilt` 之后新增的 provider 被静默忽略；一次 `Failed` build 会永久毒化（"Restart required"）；binding/json/result 三处仍是进程全局，跨 Host 泄漏与进程级重复注册抛错因此保留；`MapCrestCapabilityEndpoints` 第二次调用会重复映射路由（无 mapped-key 去重）。
- 现有 `ICapabilityEndpointDescriptorProvider` 只被 ModuleInitializer 喂；唯一的 DI 显式装配范式是 **LocalEvent**：生成 `ILocalEventRegistrationContributor` 贡献 + `AddLocalEvents(this IServiceCollection)`（`TryAddEnumerable`）+ 运行期 freeze 消费 DI，重名冲突走确定性诊断（CCCLE/CCCEV）而不是跳过。RabbitMQ 用“合并进同一 builder + 显式 provider”。→ CRUD 的 ExplicitHost 就照 LocalEvent 形状做。
- legacy CRUD 激活（`GeneratedCrudRegistration_*` + `DynamicApiGeneratedRegistryStore`）只服务旧链路；正式声明下它消失不影响 `MapCrestCapabilityEndpoints`。

定型契约（S3-3 实现时按此执行，不另开第二注册面）：

1. 运行期新增**每 Host 激活聚合**（DI 单例）：`CapabilityEndpointHostActivation`，由 `IEnumerable<ICapabilityEndpointGeneratorContribution>` 构造；贡献对象是**不可变、无 Host 捕获**的生成物，字段为：capability/endpoint descriptors、`(endpointId, version)` → binding contract、capabilityId → invoker、`(endpointId, version)` → result mapper、body types、`RequiredIdentityIds`。
2. 生成侧每程序集一个贡献类 + `AddGeneratedCrud(this IServiceCollection)`（`TryAddEnumerable`，幂等）；CRUD registration mode 固定 ExplicitHost，兼容 emitter 增加该模式（legacy 消费者保持 ModuleInitializer 不变）。
3. Map 路径（`MapCrestCapabilityEndpoints`）改为：静态 legacy 注册 + 本 Host 贡献合并 → 校验 `RequiredIdentityIds`（缺失即拒绝并指明 owner/action/type）→ registry 未 Built 时一次 Build（含贡献 descriptors），已 Built 则核对必需 identities/shape——**不得增量突变已冻结 registry，也不建第二个 registry**；ready 只在成功后提交，失败不永久缓存（Bootstrapper 的 `_built`/Failed 语义按此改造）。
4. per-Host 隔离：binding/invoker/result/body-type 查找优先走本 Host 聚合，legacy 静态注册只作为既有兼容消费者的来源；正式 CRUD 不得依赖静态注册可见性。第二次 Map 需按 (endpointId, version) 去重。
5. 迁移清单（改测试而不是放宽断言）：`AppServiceCompatibilityGeneratorTests` 中 ModuleInitializer 断言、`CapabilityEndpointIntegrationTests` 里靠反射清静态 provider 的隔离手法、`CompatibilityProjection.E2E.Tests` 与 `CapabilityEndpoint.AotFixture` 的 Program 装配，都要迁到 DI 贡献/显式 AddGeneratedCrud。

这一条落地前不动 S3-5..S3-7 的顺序；S3-2 的 typed result 与 S3-1 的 wire 域已经是它们的前置。

## S3 计数（本轮收口）

- CodeGenerator 全套 **445 通过 / 0 失败 / 445，0 skipped**：S1 立下的三条契约红（命名与 roots 由 S2 实现转绿、`FilterDescriptor` wire 由 S3-1 转绿、object 静态结果契约由 S3-2 转绿）已全部退役，未改口径、未放宽断言。
- DynamicApi **100/100**（原 73+2 红 → 两红转绿 + 27 条新增：20 条 wire 域 + 5 条 typed result 运行时 + 2 条原有的其他用例计数随套件更新）。
- JsonContracts 单元 **131/131**；PackageTests（真实消费工程构建/pack）**29/29**，两套都在 S3-1/S3-2 之后复跑过。

## S3 增量 4：ExplicitHost 运行时面（S3-3 步骤 A 完成）

- 新增（`CrestCreates.DynamicApi`）：`CapabilityEndpointContribution`（不可变、无 Host 捕获的生成物载体：owner assembly、capability/endpoint descriptors、bindings、handler bindings、result bindings、body types、required identities）与三个键值/身份记录（`CapabilityEndpointHandlerBinding`、`CapabilityEndpointResultBinding`、`CapabilityEndpointRequiredIdentity{IdentityId, Owner, Action, TypeName, Version}`）。
- `CapabilityEndpointHostActivation`（DI 单例）：本 Host 贡献的唯一聚合；**host-first** 查找 binding/handler/result mapper，legacy 静态注册表降为兼容回退；重复 owner 在构造期确定性失败，不做静默合并。
- `CapabilityEndpointExtensions.AddCrestCapabilityEndpointContribution` 用 `TryAddEnumerable` 注册（同生成类型重复调用幂等）；`MapCrestCapabilityEndpoints` 改为两段解析（先 host 聚合后 legacy），按 `(endpointId, version)` 去重；**被贡献拥有的 endpoint 必须同时具备 binding/handler/result mapper，缺一即 fail-closed**，不走通用 fallback。
- `CapabilityEndpointRegistryBootstrapper`：静态 providers + 本 Host 贡献合并进**同一次** Build；已 Built 时只做必需 identities/owned endpoint 的核对（不突变冻结 registry）；`_ready` 仅成功后提交；`Failed` 不再永久毒化（`BuildRetryable` 把终态 Failed 复位为该 Host 作用域的重试，共享 `RegistryBase` 的 "Restart required" 语义与既有测试保持不变）。
- 记录两处与定型文本的差异（实现细节，非口径偏离）：贡献类型命名为 `CapabilityEndpointContribution`（合并了原计划的 provider 概念，避免双注册面）；`TryAddEnumerable` 要求贡献是**每程序集的具体类型**（不能用服务类型本身实例化），这正是生成物形状，已在实现里注明。
- 证据：DynamicApi **109/109**（新增 9 例：两 Host 隔离且无静态泄漏、贡献注册幂等、必需 identity 缺失→确定性失败且不映射、第二次 Map 不重复映射、缺 result mapper/handler 均 fail-closed、Build 失败不复用、host 覆盖 legacy、body types 合并）；CodeGenerator 仍 **445/445**；`CompatibilityProjection.E2E.Tests` 9/9；`CapabilityEndpoint.AotFixture` 构建 0 错误。
- 环境已知项（非本轮引入）：`CrestCreates.Web.Tests` 4 例文件扫描测试在 git worktree 下失败——其 `FindRepoRoot()` 要求 `.git` 是目录，而 worktree 的 `.git` 是文件，断言前即抛；扫描目标文件已人工核对不含被禁 legacy 符号。

## S3 增量 5：CRUD ExplicitHost 投影发射（S3-3 步骤 B）

- 物化 bundle 现在包含完整 ExplicitHost 投影：每服务一个 `CapabilityEndpointContribution` 派生类型 + 每程序集一个 `AddGeneratedCrud(this IServiceCollection)`（经 `AddCrestCapabilityEndpointContribution` 注册），五动作的 binding/handler/result binding/required identity 齐备；模型侧新增 `IdWireKind`（按实体真实 Id 类型冻结路由解析）与激活命名约定（`EndpointId = "endpoint:" + CapabilityId`，绑定/结果/身份一律用同一约定，避免第二套命名）。
- 五动作 wire 语义按 §7.4 发射：Create/Search 走 `ReadNativeBodyAsync` + `Require<T>` 的 required 语义；Get 路由 Id 按静态类型解析、失败 400；Update/Delete 用组合 envelope（route Id + body / If-Match）；结果一律 typed overload + `Require<DynamicApiResponse<...>>`；正式 CRUD 产物中**不再出现**任何 legacy 注册符号。
- 证据（发射侧）：新增 `Materialized_bundle_must_activate_through_the_explicit_host_contribution` —— 断言 bundle 含 AddGeneratedCrud/贡献类型/DI 入口/四类绑定形状，且**不含**九个 legacy 注册符号（`[ModuleInitializer]`、`DescriptorProviderRegistry.Register`、`CapabilityEndpointBindingRegistry.Register`、`CapabilityEndpointJsonContractRegistry.RegisterBodyType`、`CapabilityHandlerResolverProvider.Register`、`CapabilityEndpointResultContractRegistration.Register`、`AppServiceCompatibilityProjectionManifestRegistry.Register`、`DynamicApiGeneratedRegistryStore`、`IDynamicApiGeneratedProvider`）。
- 消费闭包事实：正式 CRUD 消费项目必须显式引用 ASP.NET Core 与 DI 抽象（`Microsoft.AspNetCore.App` FrameworkReference），模型构建期按 CCCRUD005 确定性拒绝缺引用的项目；测试探针与 harness 已补齐该闭包（含 split-assembly 事实：`HttpContext` 在 `Microsoft.AspNetCore.Http.Abstractions`）。
- **未完成并如实记录**：运行期“五动作真实请求”集成证据本轮没有交付——手工 contribution 的 invoker/mapper 接线还差 5 例（服务未被调用、结果链路未对齐），该文件已移入 `99_RecycleBin/issue-128-s3-projection-integration-pending/`（附 README 说明去向），正式交付点是 **S3-7** 的真实 Host 集成证据（J/H/Q），不在本轮冒充完成。现有运行时段仍为 109/109，发射侧形状由上述断言承担。

## S3 增量 6：per-Host JSON 契约接线与元数据冻结（S3-5）

- 生成侧：`AddGeneratedCrud()` 额外注册一个生成的 `IConfigureOptions<Http.Json.JsonOptions>` 配置器，把 `CrestCreates.Generated.CrestCreatesCrudJsonSerializerContext` **追加**到宿主的 resolver 链（宿主已有 resolver 走 `JsonTypeInfoResolver.Combine(existing, generated)`，宿主自己的配置继续优先）；贡献类型新增 `JsonContext`（源生成 context 实例）与 `RequiredJsonRootTypes`（按模型的 action/component 标注的必需 root 集合，单一来源，不在 JSON 侧另造名字）。
- 运行期：`EnsureBuilt()` 在必需 identities 校验之后执行 `VerifyHostJsonContracts()`——对每个贡献的每个必需 root 同时要求 (a) 贡献自带的源生成 context 能解析该类型，(b) **宿主最终 `IOptions<JsonOptions>` 的 resolver 链中第一个给出答案的必须是这个 context**。因此「宿主只有 `DefaultJsonTypeInfoResolver`」也被拒绝：不靠反射 resolver 补洞，失败信息点名 owner/action/type/component，与该步骤前的 identity 校验风格一致；最终 options 每次按 Host 读取，不做进程级缓存。
- 证据：DynamicApi 新增 5 例（接线后才解析出的 generated-only root、宿主 resolver 仍优先、必需 root 缺失→ Map 失败点名、仅反射 resolver 的宿主必须失败、契约齐备时正常 Map）→ 套件 **114/114**；发射侧新增 `Materialized_bundle_must_declare_the_json_root_contract_and_host_wiring`（context 引用 + 恰好 10 个 root 及 action/component 标注 + 配置器/Combine/无 `DefaultJsonTypeInfoResolver`）→ 定向通过。
- **修正一处我此前引入的真实破坏**（诚实记录）：S3-2 的兼容 result emitter 用带可空标注的返回类型名生成 `ctx.Output as AssetResult?`，`as` 表达式非法（CS8651），`samples/AssetManagement`、`ProcurementApproval` 两个 Host 在 HEAD 上编译失败。修法是从源头改：compat 生成器的 `returnTypeName` 改用不带 `NullableReferenceTypeModifier` 的 `SymbolDisplayFormat.FullyQualifiedFormat`（envelope 的可空性由运行期泛型 `T?` 承担）。修后四个 sample（AssetManagement / ProcurementApproval / LibraryManagement.Web / SaaSHelpdesk.Web）全部 0 错误，compat 定向 42/42。
- 其余验证：JsonContracts 单元 131/131；CodeGenerator 全套在后台复跑（含真实构建探针），结果随后记录。

## S3 增量 7：查询值读取层（S3-6 步骤 i）

- 新增 `FilterValueReader`（`Application.Contracts/Query`）：生成式字段 adapter 的静态读取层。按**字段静态 CLR 类型**读取 S3-1 的受限 JsonElement 暂存或 CLR 值——decimal 走 `TryGetDecimal` 保精度、64 位整数保全域、分数不截断成整数、枚举只接受已定义名/值、数组逐元素读取且拒绝 null 元素；任何不匹配抛 `FilterValueFormatException`（§8：执行前拒绝，不做 `Convert.ChangeType`/token 猜测）。null 由 adapter 自己的 operator 分支处理，标量读取层拒绝 null。
- 证据：`FilterValueReaderTests` 9/9（28 位 decimal 原文保真、1e100 拒绝、19.5→int 拒绝、int64 极值、kind 不匹配四种、枚举已定义/未定义、数组逐元素与 null 元素、CLR 直通、经 S3-1 wire 域端到端读出）。
- **未完成（S3-6 步骤 ii，明确不在本轮冒充）**：CRUD emitter 仍在 `GetListAsync` 里调用反射式 `QueryExecutor<{entity}>.ApplyFilters`（`CrudArtifactEmitter.cs:475`）。下一步要按 `QueryableFields`（及声明的 Filterable/Sortable）生成 switch + 强类型 lambda 的 typed query adapter、稳定默认排序（CreationTime desc / Id desc + Id 次排序）、分页边界，并**只**替换该 CRUD 消费点；非法字段/运算/类型在执行前拒绝。发射侧需要形状断言（bundle 不再含 `QueryExecutor`、含 adapter 与默认排序），运行期证据并入 S3-7。

## S3 增量 8：typed query adapter 发射（S3-6 步骤 ii 完成）

- 模型：`CrudFieldModel` 新增 `Kind`（Unsupported/String/Bool/Int32/Int64/Decimal/Double/Guid/DateTime/Enum）、`IsNullable`、`EnumTypeDisplay`，由 builder 从属性 symbol（special type / `Nullable<T>` 解包 / `TypeKind.Enum`）计算，**不从显示字符串解析**；`CrudGenerationModel` 新增 `FilterableFields`/`SortableFields`（声明优先、缺省为全部可查询字段）与 `EntityHasCreationTime`。float/byte/short/DateTimeOffset/byte[] 等落 `Unsupported`，不生成 case。
- 发射：每服务新增 `{Entity}TypedQueryAdapter`（`Execute(IQueryable<T>, ListRequestDto) → PagedResultDto<T>`：Count → Skip/Take → mapper 投影）；`GetListAsync` 只调用它，反射式 `QueryExecutor<T>.ApplyFilters/ApplySorts` 与旧的 `AllowedQueryFields`/`EnsureAllowed*` 守卫一并移除——**switch 即唯一拒绝权威**。过滤按字段 kind 生成强类型 lambda（字符串 Equals/NotEquals/Contains/StartsWith/EndsWith/In/NotIn；数值/日期 Eq/Ne/Gt/Ge/Lt/Le/In/NotIn；Guid/Enum Eq/Ne/In/NotIn；Bool Eq/Ne；可空类型带 null 运算），值一律经 `FilterValueReader` 按静态类型读取；排序用 `switch(Field)+Direction` 生成 `OrderBy/OrderByDescending/ThenBy/ThenByDescending`；默认排序 `CreationTime desc`（无则 Id desc）+ `Id` 次排序；分页复用 `PagedRequestDto` 的 `GetSkipCount()/PageSize`。
- 计划级拒绝用新类型 `QueryPlanException : CrestBusinessException`（`Application.Contracts.Query`，保留 `Crest.Crud.InvalidFilterField/UnsupportedFilterOperator/InvalidSortField` 错误码与业务错误面），值/类型不匹配仍是 `FilterValueFormatException`。
- 证据：`CrudTypedQueryAdapterTests` 4/4（形状：kind/nullability/精确 reader 调用/operator lambda/默认排序/**不含 `QueryExecutor`**/不含旧守卫；`BundleCompileErrors` 为空；负例：无可查询字段仍发射 adapter 与两条拒绝路径并回落 Id desc；声明 Filterable/Sortable 集合限制 case）；Application.Tests 174/174；DynamicApi 114/114；CodeGenerator 全套后台复跑（451 预期），结果随后记录。
- 说明（执行者记录、我接受）：为让「bundle 编译错误为空」的断言真实，测试闭包显式补齐 `System.Linq.Queryable`/`CrestCreates.Aop`/`CrestCreates.Schema.Abstractions`/`System.Text.Encodings.Web`；负例不能用 `ExcludeProperties` 排除全部可映射字段（会触发 ObjectMapping OM001），改用 Id-only 实体。

## S3 增量 9：真实 Host 五动作 HTTP 证据（S3-7 步骤 1）

- 在已有真实 JIT fixture（`tests/Framework/Api/CrestCreates.CompatibilityProjection.E2E.Tests`，`WebApplicationFactory<Program>` + 真实 capability pipeline + `MapCrestCapabilityEndpoints`）上新增 CRUD 形状的 ExplicitHost 激活：`WidgetFeature/` 下含五动作 DTO、两个 capability input envelope、scoped 内存服务（记录调用与并发戳、尊重 CT）、一个**镜像生成物形状**的 `CapabilityEndpointContribution`（descriptor/binding/handler/result/required identity/body types + S3-5 的 `JsonContext` 与 10 个 required root）、以及 S3-5 形状的 `IConfigureOptions<JsonOptions>` 接线（本地 camelCase 源生成 context，宿主先清掉默认反射 resolver，再由 configurator 合并——反射不是答案来源）。
- 用例 10 例（内容断言，非仅状态码）：Create 200+信封数据+服务恰一次；Create 缺体/空体 → 400 且服务零调用；坏 JSON → 400；GetById 命中 200 / 未知 id → 404 void 信封（无 data）/ 坏 id → 400；Search 带 camelCase filter payload → 200 分页信封（items/count）；Update 路由 id 与体进入服务；Delete 的 If-Match（去引号）进入服务；interface/concrete 同 scoped 实例。E2E 套件 **19/19**（原 9 + 新 10，0 失败）。
- **通过本 fixture 实证暴露两处真实缺陷（不掩盖、记为 S3 未闭合项）**：
  1. **生成物缺 handler 注册**：实测若只按当前 `AddGeneratedCrud` 注册（贡献 + scoped 服务 + JSON），POST `/api/widget` 返回 **500（HANDLER_NOT_FOUND）且服务零调用**——贡献的 handler 要经 `ICapabilityHandlerModule` 组合（与生成的 `GeneratedCapabilityHandlerModule` 同一模式）才会进入 dispatcher。fixture 里用一处**显式标注的桥**（手写 module）绕过，生成侧必须在下一步发射等价注册。
  2. **能力注册表未汇总贡献**：真实 Host 的 `CapabilityRegistry` 构建仍只读静态 `DescriptorProviderRegistry`，贡献的 capability descriptors 需要 Host 自己桥接；按 §7.2，`MapCrestCapabilityEndpoints` 应在未 Built 时做**一次**权威汇总（静态 providers + 本 Host 贡献），已 Built 则核对必需 identities。
- 仍未做（S3-7 步骤 2，明确不在本轮冒充）：请求中止/取消在 HTTP 层的传播、UoW 提交与响应写失败的独立事实、以及生成物（真实工具链）fixture（属 S4 的 `GeneratedCrud.AotFixture`）。

## S3 增量 10：handler module 发射（G2 生成侧关闭）

- 生成物现在为每程序集发射 `GeneratedCrudHandlerModule_{assembly}`（`ICapabilityHandlerModule`，`Id` = owner assembly，`Apply` 用 `resolver.Register(capabilityId, invoker)` 注册本程序集全部五动作 invoker），并在 `AddGeneratedCrud` 里经 `TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityHandlerModule, ...>())` 注册——即 fixture 里那处手写桥所模拟的正是这个契约（`CapabilityHandlerResolver` 由模块组合，见既有 schema capability 生成器的同一模式）。
- 证据结构（双向）：(a) 形状断言扩展 `Materialized_bundle_must_activate_through_the_explicit_host_contribution`——bundle 必含 `ICapabilityHandlerModule`、`GeneratedCrudHandlerModule_`、以及五条 `resolver.Register("Shop.Application.ProductAppService.{Action}"`；(b) E2E fixture 已证明「注册该模块后，真实 Host 经真实 dispatcher 五动作全绿」（19/19）。合起来：机制可用（b）+ 生成物确实注册它（a）。
- 仍开放（G1，下一步）：真实 Host 的 `CapabilityRegistry` 构建仍只读静态 provider bag，贡献的 capability descriptors 需要 Host 自己桥接（fixture 里对应那处 capability provider 桥仍存在）；按 §7.2 应在 `MapCrestCapabilityEndpoints` 未 Built 时做一次权威汇总（静态 + 本 Host 贡献），已 Built 则核对必需 identities。

## S3 增量 11：Map 期能力注册表权威汇总（G1 关闭）

- `MapCrestCapabilityEndpoints` 现在在**端点注册表构建之前**执行一次能力注册表汇总：未 Built 时用「静态 `DescriptorProviderRegistry` providers + 本 Host 贡献的能力描述符（经最小的 `IDescriptorProvider<CapabilityDescriptor>` 适配器承载，无全局状态）」一次性 `Build`，随后立即核对每个贡献能力确实可解析；已 Built（Host 自建）时逐个核对，缺失抛确定性异常并点名 capability id 与 owner 命名空间——不静默跳过、不走通用 fallback。
- 顺序是本轮的真正坑：第一版把汇总插在 `bootstrapper.EnsureBuilt()` **之后**，而端点注册表的构建内部就会校验 endpoint→capability 引用，于是 fixture 全红（`RegistryValidationException: ... references missing Capability ...` 19/19 失败）。移到 `EnsureBuilt()` 之前即修复——这正是「一次权威汇总」必须在映射前完成的证据。
- 证据：真实 Host fixture **删除**了它手动的 `capabilityRegistry.Build(...)` 与 `WidgetContributionCapabilityProvider` 手写桥（连带删除死代码），五动作 + greeting 兼容投影 **19/19 全绿**——即"Host 只调 `AddGeneratedCrud()`-等价注册 + `MapCrestCapabilityEndpoints()`"即可，无需自己桥接能力注册表。
- 负向用例已补（`CapabilityRegistryHostAggregationTests`）：真实 `CapabilityRegistry` 先以空 provider 集建好、再激活一个拥有 `WidgetFeature.WidgetAppService.Create` 的贡献 → `MapCrestCapabilityEndpoints` 抛 `InvalidOperationException` 且消息点名该 capability id。顺带记录两条 API 事实：`RegistryValidationEngine<T>` 需要显式 validators 集合；`TryAddEnumerable` 要求贡献是**具体派生类型**（不能用服务类型自身实例），测试里用空的 `ProbeContribution : CapabilityEndpointContribution` 表达。DynamicApi 套件 **115/115**。
- S3 剩余：S3-7 第二步（请求中止/取消在 HTTP 的传播、UoW 提交与响应写失败独立），然后进入 S4（生成物 + 原生 fixture）。

## S3 增量 12：取消在 HTTP 层的传播（S3-7 步骤 2a）

- fixture 服务加了**显式 opt-in 门闸**（`WidgetCallRecorder.Armed` + TCS + 进入信号 + 观测取消标志 + 完成标志，全部 host-scoped、无静态），`CreateAsync` 在门闸武装时 `await gate.Task.WaitAsync(cancellationToken)`，捕获 `OperationCanceledException` 记录后重抛；未武装时行为与之前完全一致（既有用例不受影响）。
- 新用例（`WidgetCrudE2ETests` 第 11 例）：武装门闸 → 用测试自持的 `CancellationTokenSource` 发起 `POST /api/widget`（不 await）→ 有界轮询（25ms 间隔、10s 上限）等到服务进入 → `Cancel()` → 断言 (a) 请求在界内以 `OperationCanceledException` 族结束、(b) 服务**观测到取消**且 `LastCreateToken.IsCancellationRequested`、(c) 未记录"完成"。结果：E2E **20/20**（连跑两遍）、DynamicApi **115/115**、CodeGenerator **451/451**。
- **牙齿证明**（执行者做了变异反向验证）：把服务改成 `WaitAsync(CancellationToken.None)` 后，用例在 10s 边界确定性失败（"aborted request must settle instead of hanging or completing"），随后还原——这条断言不是空真的。
- 记录一条链路事实：TestHost 会把客户端 token 取消接到服务端 `RequestAborted`；pipeline 会把 handler 的 OCE 映射成 504 且该响应不会到达已中止的客户端——因此本用例的证据是 **recorder 观测**，不是响应码。
- 归入 S4（有据可依，不是拖延）：**UoW 提交 vs 响应写失败独立** 与**并发戳不匹配语义**都需要真实 memory repository/UoW 与真实 generated service——计划 S4 原文即要求"memory repository/UoW 明确能力且走真实 generated service/typed query/Capability 执行"，本 fixture 是手工形状探针，不在这里造假 UoW。S3-7 的 HTTP 证据部分（五动作 wire、400 零业务、路由 id/If-Match 传输、取消传播）已全部交付。

## S4 增量 1a：生成物 fixture 骨架（Domain + Feature），并暴露一处真缺陷

- 新增（名称按计划锁定）：`tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture.Domain`（仅实体 `Widget : IEntity<Guid>, IHasConcurrencyStamp`，含 CreationTime/ConcurrencyStamp 供默认排序与并发契约使用；无任何手写 DTO/service）与 `...AotFixture.Feature`（唯一手写文件就是 `[GenerateCrudService(typeof(Widget), RoutePrefix = "/api/widgets")] public partial class WidgetAppService { }`，工具链按真实消费方式导入：analyzer ProjectReference + `FrameworkReference Microsoft.AspNetCore.App` + Repository transport props/targets + `JsonSerializerIsReflectionEnabledByDefault=false`）。两个项目已登记进 `CrestCreates.slnx` 与 `solutions/CrestCreates.All.slnx`。
- 生成物实证（Feature 的 `obj/Debug/net10.0/`）：`CrestCreates.Crud.g.cs` 含 `WidgetDto/Create/Update/ListRequest`、`IWidgetAppService : ICrudAppService<...>`、`WidgetTypedQueryAdapter`、`WidgetCrudPermissions`、`GeneratedCrudContribution_*`、`AddGeneratedCrud`；`CrestCreates.JsonContracts.g.cs` 含 `CrestCreatesCrudJsonSerializerContext` 与全部 roots（含嵌套 paged envelope 与两个 capability input envelope，camelCase）；STJ 产出 25 个文件含 `JsonTypeInfo<...WidgetDto>`；inputs/outputs manifest 与 stamp 齐备（owner + sha256 内容 hash），重建 no-op 时 bundle/stamp 哈希不变。
- **真缺陷（本 fixture 当场暴露，src 侧待修）**：`DynamicApiAotSourceGenerator` 仍把正式 CRUD 服务当作 legacy Dynamic API 服务——因为 bundle 的 service 同时带 `[CrestService]` 且实现 `ICrudAppService<>`，该生成器额外产出 `GeneratedDynamicApiRegistry.g.cs`（末尾 `[ModuleInitializer] … DynamicApiGeneratedRegistryStore.Register(...)`，**进程级全局注册**）与 `GeneratedDynamicApiEndpoints.g.cs`（legacy 路由经 obsolete `DynamicApiGeneratedRuntime.ReadBodyAsync`，编译期 CS0618 可见）。这正是 §9 CCCRUD008 禁止的"正式 CRUD 消费 legacy provider/serialization"，bundle 自身符号干净不代表整棵树干净——S3-3 的形状断言只查了 bundle，这条要补到"整次编译"层面。修法方向（下一轮首项）：用 S2-① 的 `CrudArtifactOwnership`（manifest as AdditionalFile）让 `DynamicApiAotSourceGenerator` 跳过 CRUD-manifest 拥有的服务，与 Entity/ServiceRegistration 的跳过同一机制；并补一条"正式声明下整棵生成树不含 legacy 注册符号"的断言。
- 另记：生成 DTO 的非空 string 成员会产生 CS8618 警告（未初始化）；是否加 `= string.Empty` 待与映射契约一起定，先记录不掩盖。

## S4 增量 1b：修掉正式 CRUD 的 legacy 注册（并补整次编译断言）

- `DynamicApiAotSourceGenerator` 接入 S2-① 的 `CrudArtifactOwnership`（manifest 作为 AdditionalFile，同一读取器、同一归属规则）：服务 FQN 被 CRUD manifest 拥有时**跳过** legacy Dynamic API 表面生成；ownership 为空（旧工程、无 CRUD）时行为完全不变。
- 修后实证：`CrestCreates.GeneratedCrud.AotFixture.Feature` 清 obj 重建——`source-generators/.../DynamicApiAotSourceGenerator/` **不再有任何输出**，`GeneratedDynamicApiRegistry.g.cs` / `GeneratedDynamicApiEndpoints.g.cs` 消失，编译输出无 CS0618（此前 legacy 端点引用 obsolete `DynamicApiGeneratedRuntime.ReadBodyAsync`）。
- 断言提升到"整次编译"层面：MSBuild 探针新增 `AllGeneratedFiles`（obj 下全部 `*.g.cs` 内容）并在端到端契约用例中断言——整棵树不含 `DynamicApiGeneratedRegistryStore`、不含 `DynamicApiGeneratedRuntime.ReadBodyAsync`、不含 `GeneratedDynamicApiRegistry`，且编译输出无 `CS0618`。该用例已通过。
- 说明：S3-3 的形状断言只覆盖了 bundle 自身（"bundle 符号干净"），这次证明**整棵树**才是有意义的边界；两处断言各司其职（bundle 形状 vs 整次编译）。CodeGenerator 全套在后台复跑（预期 451 + 既有新增计数不变），结果随后记录。
