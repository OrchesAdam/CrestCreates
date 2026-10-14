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

### 待执行（S1 其余交付）

真实 MSBuild/官方 STJ 同轮不可见复现（真实消费项目闭包：BuildTasks 前后阶段 + 官方 STJ 生成上下文缺失 CRUD roots 的可执行红灯）；mapper/compatibility input/closed envelope 的补充负例（与 MSBuild 复现同批）。设计审查通过后推进 S2/S3。
