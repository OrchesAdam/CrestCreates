# #129 S4 legacy 归档与迁移记录（受版本控制交付）

归档时间：2026-10-20；本记录入库时间：2026-10-21。本文是 S4 归档的**PR 交付工件**；`99_RecycleBin/issue-129-controller-exit/` 下的同名 README 与源码副本只是本地软删除备份，`99_RecycleBin/` 被 `.gitignore` 忽略，**不属于 PR 交付内容**。

除特别说明外，下列文件/分支均无生产 authored 消费者，替代入口为正式 ExplicitHost HTTP 投影（`[CapabilityCompatibilityProjection(ContractType=...)]` + precompile/JSON 工具链）或正式 `[GenerateCrudService(typeof(TEntity))]`。

## 归档清单与调用图结论

| 归档项 | 调用图结论 | 替代入口 | 测试迁移 |
| --- | --- | --- | --- |
| `src/Tooling/CrestCreates.CodeGenerator/ControllerGenerator/ControllerSourceGenerator.cs` | 仅 `CodeGenerator.Tests` 的 `[CrestService(GenerateController=true)]` 测试服务触发；src/samples 无消费者 | 正式 ExplicitHost 投影 | 无专用测试；测试服务的 `GenerateController` 标记已移除 |
| `.../ControllerGenerator/CrudControllerSourceGenerator.cs` | `[CrestCrudApiController]` 无 authored 消费者 | 正式 CRUD ExplicitHost | `CrudControllerSourceGeneratorTests` 原样归档 |
| `src/Framework/Ddd/CrestCreates.Domain.Shared/Attributes/CrestCrudApiControllerAttribute.cs` | 仅上述生成器消费 | 无 | 随生成器归档 |
| `src/Framework/Api/CrestCreates.DynamicApi/GeneratedApi/{CrestApiController,GeneratedApiControllerAttribute,ApiOverrideAttribute,CrudAction}.cs` | 仅 legacy controller 生成分支与专用测试消费；`TicketApi` 已归档 | 正式投影；不再提供 controller 推荐 | `LegacyGeneratedApiControllerAbstractionsTests`、`GeneratedApiControllerSourceGeneratorTests` 归档 |
| `DynamicApiAotSourceGenerator` 的 controller 分支（`[GeneratedApiController]`/`[ApiOverride]`/controller registration） | 无 authored 消费者（仅 inline 测试） | 正式投影 | 专用测试归档；移除前快照见下节 |
| `CrudServiceSourceGenerator` 旧无参 stub / bundle / provider / 旧 bool ExecuteAsync 发射分支 | 仅以测试 stub 属性进入，无生产声明；旧 bundle 的 `ExecuteAsync(context, true, ...)` 无对应重载，物化即不可编译 | 正式 `[GenerateCrudService(typeof(TEntity))]` + CRUD precompile/JSON；参数化声明仍保留全部正式诊断 | `CrudServiceSourceGeneratorTests`、`CrudServiceMainlineSourceGeneratorTests` 归档；正式 `CrudServiceRealDeclarationBaselineTests` 等保留 |
| `DynamicApiRouteConvention` 实例反射路径 | 实例反射方法零消费者；唯一活成员 `IsScalar` 仅被 `DynamicApiGeneratedRuntime.ValidateAsync` 使用 | `DynamicApiGeneratedRuntime` 内部私有类型分类器（非反射、有限已知类型） | 无专用测试 |
| `TicketApi`（SaaSHelpdesk 手写 controller 面） | 路由/动作由正式 Ticket ExplicitHost 贡献唯一拥有；`/api/ticket/all` 不再走 controller | 正式 Ticket 投影 | HTTP 契约由正式 wire matrix/路由测试覆盖 |

## 移除前快照

`DynamicApiAotSourceGenerator` 移除 controller 分支前的完整源码（1665 行）有两份可复核工件：

- 本仓库 `docs/review/2026-10-21-issue-129-dynamic-api-aot-source-generator-pre-controller-removal.cs.txt`（入库文本副本）。
- Git 历史：`26439113^:src/Tooling/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs`（S4.1 提交的父提交）。

## 仍保留并隔离的 legacy

- `[CrestService]` 旧 Minimal API 生成器与静态 compatibility 生成器只存在于显式 `CrestCreates.CodeGenerator.Legacy` analyzer；默认 analyzer 不再包含，只有 opt-in 项目引用。
- 旧运行时（registry/runtime/body reader/options/descriptors/conventions）在 `CrestCreates.DynamicApi.Legacy`；旧 Web 激活在 `CrestCreates.Web.Legacy`。
- 所有公开激活入口都经过同一 allowlist 门禁：`AddLegacyGeneratedApi` 注册精确的 per-service/action 策略；`MapLegacyGeneratedApi`、保留的 `MapCrestDynamicApi` 以及 `DynamicApiGeneratedRegistryStore.MapGeneratedEndpoints` 都要求该策略，缺失即 `CCHTTP007` 且零映射。策略按 CLR `Type` 身份（含程序集）匹配，不接受同名跨程序集类型。
- Legacy analyzer/runtime 不声明 trimming/NativeAOT-verified；CapabilityEndpoint.AotFixture 的移动仅保持 legacy compatibility 证据，不外推到正式 Ticket 链。

## 测试迁移与退出清单

- 归档的专用测试：`LegacyGeneratedApiControllerAbstractionsTests`、`GeneratedApiControllerSourceGeneratorTests`、`CrudControllerSourceGeneratorTests`、`CrudServiceSourceGeneratorTests`、`CrudServiceMainlineSourceGeneratorTests`。
- 保留并迁移到正式链的测试：正式 wire matrix/路由、CRUD RealDeclaration baseline、JSON 契约闭包、CapabilityEndpoint 注册与 Map 守卫。
- 退出条件（S4 复审通过即完成）：默认 analyzer/正式 runtime/正式 Web 不含 legacy 生成器、registry/runtime/Add/Map；Host 只能经 `AddLegacyGeneratedApi` + `MapLegacyGeneratedApi` 激活；正式 CRUD 只走 `[GenerateCrudService(typeof(TEntity))]` + ExplicitHost；无 controller 生成入口。
- 外部旧 `GeneratedApiController`/`ApiOverride` 消费者会 breaking：按 S4 计划归档，不保留过时推荐。

## 本地备份说明

`99_RecycleBin/issue-129-controller-exit/` 的源码副本与 README 由本地软删除流程维护，受 `.gitignore` 忽略。PR #152 交付以本文和 `docs/review/artifacts/` 为准；若本地备份缺失，可按上表路径从 Git 历史恢复（`git log --diff-filter=D -- <path>`）。
