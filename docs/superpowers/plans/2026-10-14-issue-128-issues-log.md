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
