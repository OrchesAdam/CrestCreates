# #126 实施计划与 S1 实验记录

- 关联：[#126](https://github.com/OrchesAdam/CrestCreates/issues/126)、Spec `docs/superpowers/specs/2026-10-13-issue-126-local-event-typed-dispatch-replay-design.md`。
- 基线：master `a546bbb1`；分支 `feat/issue-126-typed-dispatch-replay-20261013-085538`。

## S1 实验记录（JSON 工具链顺序，2026-10-13）

- 实验工程：`/tmp/crest-126-s1-json-exp/`（JsonExp 控制台 + JsonExp.Sg 最小增量生成器）；SDK `10.0.112`。
- 命令：`cd /tmp/crest-126-s1-json-exp/JsonExp && dotnet run -c Release`。
- 结果：

```text
PASS: A1: Foo registered on source-declared stub context via injected roots
PASS: A2: round-trip via injected context
PASS: A3: unregistered type resolves to null (no reflection fallback)
PASS: B: SG-emitted [JsonSerializable] is NOT consumed by the STJ generator (ordering constraint confirmed)
PASS: C: build_property from imported targets visible to SG
PASS: D: ambient reflection JSON throws when disabled
S1_JSON_EXP_OK
```

- 过程性证据：
  - B 场景在补手工抽象成员前直接编译失败（`CS0534: BarCtx 不实现继承的抽象成员 JsonSerializerContext.GetTypeInfo(Type)/GeneratedSerializerOptions`）——STJ 生成器未消费另一生成器输出的硬证据；补实现后无重复成员冲突，行为确认为「未注册 → null」。
  - C 场景首次失败（属性值读不到），补 `<CompilerVisibleProperty Include="CrestCreatesJsonContractsEnabled" />` 后通过。全仓检索确认现有 `build_property.CrestCreatesDynamicApiAotGeneration`（DynamicApiAotSourceGenerator.cs:15）没有任何 CompilerVisibleProperty 声明，其禁用开关实际为死代码；记录为既有事实，不在本 Issue 修复。
- 关键实验文件（原件保留于 /tmp）：

`JsonExp.csproj`（要点）：

```xml
<PropertyGroup>
  <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
  <IsAotCompatible>true</IsAotCompatible>
</PropertyGroup>
<Import Project="sim.targets" />
<ItemGroup>
  <Compile Remove="gen/**/*.cs" />
  <Compile Include="gen/MyCtx.Roots.g.cs" />   <!-- 模拟 BuildTask 产物 -->
  <ProjectReference Include="../JsonExp.Sg/JsonExp.Sg.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

`gen/MyCtx.Roots.g.cs`（模拟工具链注入 roots；`MyCtx.cs` 为项目内 stub 部分）：

```csharp
using System.Text.Json.Serialization;
namespace Exp;
[JsonSerializable(typeof(Foo))]
public partial class MyCtx : JsonSerializerContext { }
```

`sim.targets`（含属性 + CompilerVisibleProperty）；断言路径：`MyCtx.Default.GetTypeInfo(typeof(Foo)) as JsonTypeInfo<Foo>` 非空 → `SerializeToUtf8Bytes(new Foo(...), fooInfo)` / `Deserialize(bytes, fooInfo)` round-trip。

**锁定**：codec roots 只走「CoreCompile 前磁盘源文件」路径（扩展现有 JsonContracts.BuildTasks 工具链）；SG 从不产出/引用 `[JsonSerializable]` 与上下文类名；工具链存在性经 `CompilerVisibleProperty` 属性判定，SG 侧缺工具链 + Replayable 声明 = 编译错误诊断。

## 切片工作分解

### S2 生成与唯一强类型 dispatcher

新增（EventBus.Abstractions）：`LocalEventAttribute`、`LocalEventLegacyIdentityAttribute`、`LocalEventInvoker`(+`<TEvent>`)、`LocalEventBinding`、`ILocalEventDispatchRegistry`、`ILocalEventRegistrationContributor`、`LocalEventDispatchRegistrationBuilder`、`LocalEventDispatch`（静态分发核心 + 预期清单/取消检查）、`LocalEventJsonCodecRegistry`、异常族（`UnknownLocalEventException`/`LocalEventDispatchConfigurationException`/`LocalEventRequiredHandlerMissingException`）；`DeadLetterMessage` 增可空 `LocalReplayKey`。

新增（EventBus.Local）：`LocalEventDispatchRegistry`（冻结/合并/冲突）、`LocalEventServiceCollectionExtensions.AddCrestLocalEventDispatch`；重写 dispatcher/bus/module。

新增（CodeGenerator）：`LocalEventGenerator/`（发现、贡献物、DI 绑定、诊断、工具链属性读取、per-assembly 公开扩展 `CrestCreates.Generated.{Asm}.LocalEventRegistrations.AddLocalEvents`）。

迁移：`LocalEventBusModule`、`LocalChannelEventBusModule`、`AddCrestWeb` 注册链；测试装配（EventBus.Tests / Local.Tests.Integration）；`HumanTaskCompletedEvent` 所属包显式启用；域事件真实消费方。

### S3 typed codec + 重放核心

JsonContracts.BuildTasks.Core：LocalEvent discovery（`[LocalEvent(Replayable=true)]`）→ `LocalEventJsonSerializerContext` roots + `LocalEventJsonCodecContributions.Register()`；`CompilerVisibleProperty` 契约属性。

EventBus.Local：`LocalEventDeadLetterRecorder`（同步/Channel 共用）、`LocalDeadLetterReplayExecutor`（状态/取消表）；重写 manager/background service；`InMemoryDeadLetterStore` 满队列同键更新修正 + 回归；`DeadLetter.EFCore` 列/映射 additive。

### S4 native 与收口

`tests/Runtime/Eventing/CrestCreates.EventBus.Local.AotHost`（+`.Tests`）：六场景、sentinel、脚本化 gate；架构守卫扩充；文档（Spec 偏差记录、memory.md、#123 证据矩阵）；归档 `LocalEventBus`（空子类，若确认无引用）与 `ILocalDeadLetterStore`（obsolete 零使用）。

## S2 盘点补充（迁移清单来源）

- 直接构造/注册旧组件的位置见 Spec §1 表 + Explore 报告（`AddCrestWeb`、两个 module、samples 的自定义 bus、Workflow 的 retired subscriber 不启用）。
- `HumanTaskCompletedWorkflowSubscriber`（retired）：保留文件，不启用其 contributor。
- `MediatorLocalEventBus`：保持独立，不接新索引。

## 验证门槛（本地）

- `dotnet build`（全量 0 错误）；CAP/UoW 相关既有套件回归（受影响装配面）。
- `tests/Runtime/Eventing/*` 全量；CodeGenerator/JsonContracts 工具链测试；Boundary 依赖/守卫测试。
- S4：真实 `dotnet publish -p:CrestCreatesPublishMode=aot -r linux-x64` + 运行 sentinel 断言（证据日志留存 /tmp，摘要入 issues log）。
- 判别验证：对关键负例（未声明事件、预检后移、容量阻断、动态 JSON）还原旧行为确认新测试会失败。
