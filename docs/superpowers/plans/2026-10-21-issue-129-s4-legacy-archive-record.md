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

## 消费者迁移与 Legacy 项目归档（2026-10-21 目标修订）

S4 的目标由“隔离兼容”修订为“消费者迁移与旧链退出”。迁移顺序：

1. SaaSHelpdesk 七个 AppService（Agent/Category/Customer/CustomerPortal/Dashboard/KnowledgeBase/SLAPolicy）改为正式接口投影，逐动作保留旧 route/verb/query/body/If-Match/permission，自定义写动作补 `[UnitOfWorkMo]`，body validator 显式声明。
2. LibraryManagement Book/Category/Loan/Member 改为正式自契约投影适配器：继承 CRUD 的 `GetListAsync` 用显式分页 query 参数，`QueryAsync` 保留原请求 body，旧默认值语义用可空参数回落；适配器在模块注册一次，业务服务仍由原 owner 注册。
3. 测试/fixture 迁移：SaaS/Library 行为、错误响应、权限/UoW/校验断言改跑正式链；`CompatibilityProjection.E2E` 与 `CapabilityEndpoint.AotFixture` 去掉 Legacy analyzer/runtime；旧输出形状专用测试归档。
4. 三个 Legacy 项目（`CodeGenerator.Legacy`/`DynamicApi.Legacy`/`Web.Legacy`）、旧生成器、registry/runtime、Add/Map、allowlist、旧 CRUD stub、TicketApi 与 controller 分支全部归档；从解决方案、项目引用、`Directory.Build.Aot.props` 和 analyzer/package 分发移除。
5. 正式路由冲突预检保留在 `MapCrestCapabilityEndpoints` 的批量预检中；legacy 侧的 allowlist/route guard 随包归档。

## 退出门禁

- 有效构建图、解决方案、包分发与样例 Host **零** Legacy 项目/程序集引用。
- 正式程序集不含 legacy 类型或旧 Add/Map/runtime 符号；样例端点只由正式贡献发布。
- 归档代码不在 Compile/analyzer/package glob；原样重新引入 legacy call 的变异能被守卫捕获。
- 旧链专用测试已归档；SaaS/Library 业务行为、错误响应、权限/UoW/校验与原生断言在正式链保留。
- 外部旧 `GeneratedApiController`/`ApiOverride` 消费者 breaking：按 S4 计划归档，不保留过时推荐。

## 执行结果（2026-10-21）

- 三个 Legacy 项目的源码与本文件记录的旧测试已通过 `git mv` 归档到：
  - `99_RecycleBin/issue-129-controller-exit/src/Tooling/CrestCreates.CodeGenerator.Legacy/`
  - `99_RecycleBin/issue-129-controller-exit/src/Framework/Api/CrestCreates.DynamicApi.Legacy/`
  - `99_RecycleBin/issue-129-controller-exit/src/Platform/CrestCreates.Web.Legacy/`
  - `99_RecycleBin/issue-129-controller-exit/tests/...`（legacy analyzer/runtime/map/isolation/probe 专用测试）
  这些重命名由 Git 跟踪，但从解决方案、项目引用、`Directory.Build.Aot.props`、Compile/analyzer/package glob 中移除，不参与构建。
- 该目录下的临时 README 与构建产物（`bin/obj`）仍是本地未跟踪内容，不属于 PR 交付；审核入口以本文件和 `docs/review/` 为准。
- `samples/SaaSHelpdesk` 与 `samples/LibraryManagement` 的正式投影、测试迁移与退出门禁见 issues log 的「S4 消费者迁移与旧链退出」；`CompatibilityProjection.E2E` 与 `CapabilityEndpoint.AotFixture` 已改为正式投影并以真实 publish/link/run 验证。
- S4 R2 复审补充：`CompatibilityHttpResultMapper` 的旧 `IServiceProvider`/无 metadata 反射 overload 与 `MapAotJson` fallback 已从正式包移除，源码保存在本目录 `src/Framework/Api/CrestCreates.DynamicApi/CompatibilityHttpResultMapper.reflection-overloads.cs.txt`；正式包只保留 `JsonTypeInfo` 必需 overload。Agent 的 `CreateAgentDtoValidator` 改为手写、可被 precompile 发现并接 `RequiresValidator`。Library Loan 三类 validator 曾短暂加入，但上一 Library Host 未注册 `IValidationService`、legacy body 校验实际未启用，正常载荷被误拒，已撤回并归档到本目录 `samples/LibraryManagement/.../Validators/`；Library 正式链保持旧行为。缺工具链 CCHTTP002（Error）、退役 LegacyStatic/隐式静态/方法级声明 CCHTTP001 由默认 analyzer 在每次编译收口。
