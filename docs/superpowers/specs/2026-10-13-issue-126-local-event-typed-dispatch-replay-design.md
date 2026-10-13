# #126 LocalEvent 强类型分发与重放 — 设计规格（Spec）

- 关联 Issue：[#126](https://github.com/OrchesAdam/CrestCreates/issues/126)（波次 2，总控 #121）。
- 基线：master `a546bbb1`（#137/#125 已合并）。
- 上游契约：#126 Issue 评论（2026-10-13 详细设计，指定为详细工作契约）。本 Spec 落实该契约并锁定 S1 编译实验结论；不削弱任何行为与验收边界。
- 目标：正常发布、Channel 消费、人工死信重试、后台死信重试使用**同一强类型分发索引**；可重放事件的 JSON 编解码**静态绑定**；业务只声明事件/handler 并显式启用生成注册。

## 1. 现状事实（基线代码）

| 位置 | 现状 | 本 Issue 处理 |
| --- | --- | --- |
| `src/Runtime/Eventing/CrestCreates.EventBus.Local/DefaultLocalEventDispatcher.cs:14-56` | `MakeGenericMethod`+`MethodInfo.Invoke` 反射分发；泛型入口另行解析 handler | 重写为唯一强类型索引分发 |
| `src/Runtime/Eventing/CrestCreates.EventBus.Local/DefaultLocalEventBus.cs:33-108` | 失败入 DLQ 用 `JsonSerializer.SerializeToUtf8Bytes(@event, eventType)`（Type overload 动态 JSON）；validator 收 `GetType().Name`（短名） | typed codec 记录；canonical 名称 |
| `src/Runtime/Eventing/CrestCreates.EventBus.Local.Channel/BackgroundChannelLocalEventBusConsumer.cs:46-94` | 复制动态 JSON 入 DLQ 逻辑 | 共用失败记录组件 |
| `src/Runtime/Eventing/CrestCreates.EventBus.Local/DefaultLocalDeadLetterManager.cs:43-99` | `Type.GetType` 动态反序列化；解析失败发生在 `MarkRetrying` 之后；非 ILocalEvent 也可能标成功 | 共用 `LocalDeadLetterReplayExecutor` |
| `src/Runtime/Eventing/CrestCreates.EventBus.Local.Channel/LocalDeadLetterBackgroundService.cs:32-116` | 复制类型解析/反序列化/状态迁移；未知类型 `continue` 后永久 Retrying；用全局 options.MaxRetries 而非消息预算 | 仅负责调度，共用重放核心 |
| `src/Runtime/Eventing/CrestCreates.EventBus.Local/InMemoryDeadLetterStore.cs:22-31` | 容量检查先于同键覆盖 → 满队列时同 MessageId 状态更新被阻断 | 容量仅限制新增键 |
| `src/Framework/Ddd/CrestCreates.Domain/DomainEvents/IDomainEvent.cs` | `IDomainEvent : ILocalEvent`，域事件经 `DomainEventPublisher`→`ILocalEventBus` 进入同一总线 | 纳入本模型（严格 membership 同样适用） |
| `src/Runtime/HumanTask/CrestCreates.HumanTask/HumanTaskCompletedOutboxHandler.cs:37-83` | optional lane：bus 缺失/超时/失败一律不阻塞 ack | 行为保留；失败记录走新 recorder |

`IEventValidator`：生产环境无注册（`RegistryEventValidator` 只在测试使用）；`AddCrestWeb` 也未注册 validator——当前 `DefaultLocalEventBus` 必选 ctor 依赖属潜在 DI 缺口，本次一并修正为可选依赖。

## 2. 唯一权威与注册模型

保留业务调用面：`ILocalEvent`、`ILocalEventHandler<TEvent>`、`ILocalEventBus`、`ILocalEventDispatcher`。新增只读执行索引 `ILocalEventDispatchRegistry`（activation/dispatch 索引，**不替代** `IEventRegistry`，不重定义事件名称/版本/权限/审计/可靠性）。

### 2.1 注册项（`LocalEventBinding`，EventBus.Abstractions）

```csharp
public sealed class LocalEventBinding
{
    public Type EventType;                    // 精确 CLR 事件类型
    public LocalEventInvoker Invoker;         // 编译期闭合的强类型调用
    public string LocalName;                  // 完整 metadata 类型名（canonical 本地名；不用短名）
    public string LocalReplayKey;             // 编译期确定的 "本地 replay key"
    public int ReplayVersion = 1;             // [LocalEvent(Version)]，默认 1
    public bool Replayable;                   // 明确能力，默认 false = dispatch-only
    public Func<JsonTypeInfo?>? CodecFactory; // Replayable 时必须非空，冻结时解析为 JsonTypeInfo
    public IReadOnlyList<Type> ExpectedHandlerTypes;   // 生成的 handler binding 清单（装配诊断）
    public IReadOnlyList<(string Name, int Version)> LegacyIdentities; // 旧身份静态 alias
    public string SourceAssembly;             // 诊断
}
```

规则：

1. 事件身份：有治理元数据（`IEventRegistry.GetByPayloadType` 命中）→ 记录时 `EventName`/`EventDescriptorId`/版本用 descriptor 值；否则 `EventName` = metadata 完整类型名，版本 = `[LocalEvent].Version`（默认 1）。**LocalReplayKey 恒为编译期本地键**（见 §4），不冒充治理 `EventDescriptorId`。治理与本地版本不一致在**记录时**确定性报错（见 §5.3）。
2. 未接入治理元数据的普通 `ILocalEvent` 可声明参与分发；不强迫新建治理 descriptor。
3. 同一 Type 的多个贡献**合并**（跨程序集 fan-out）；replay 元数据（key/version/replayable）不一致 = 冻结期拒绝。跨项目同一事件多 handler 是合法 fan-out，不是重复。
4. 最终装配禁止：`Type.GetType`、程序集/AppDomain 扫描、`MakeGenericType/Method`、`MethodInfo.Invoke`、`Expression.Compile`（架构守卫 + 行为测试双重保护）。`typeof/GetType()` 仅用于精确静态索引与诊断。
5. Registry 不捕获 IServiceProvider/scope/handler 实例；冻结后只持有静态元数据、委托与安全的 JSON type info。

### 2.2 贡献与冻结

- `ILocalEventRegistrationContributor { string Id; void Register(LocalEventDispatchRegistrationBuilder builder); }`。
- 生成扩展对每个含本地 handler/声明事件的程序集产出 contributor 实例；宿主显式调用该扩展即完成启动（见 §3.3）。
- `LocalEventDispatchRegistrationBuilder`：生成产物与手工装配写入**同一 builder**，受相同冲突校验（低层 `Add(LocalEventBinding)` + 手工 `Declare<TEvent>(bool replayable = false, int version = 1)`；手工绑定的 handler 顺序按 DI 显式注册顺序）。
- 冻结：`ILocalEventDispatchRegistry` 以 DI 单例工厂实现，`Build()`/首次解析时快照全部 contributor + 手工配置并校验：重复 Type 元数据冲突、replay key/version 冲突、Replayable 缺 codec、codec 类型不匹配、legacy alias 冲突。违规抛 `LocalEventDispatchConfigurationException`（确定性、可读、含逐条违规行）。
- 组成与调用顺序无关（contributor 先加后加均可）；重复调用同一扩展幂等（contributor 按 Id 去重）。

## 3. 生成入口与开发体验

### 3.1 增量生成器（`CrestCreates.CodeGenerator/LocalEventGenerator/`）

- 发现规则（编译期语义分析，非类型名匹配）：
  - 实现 `ILocalEventHandler<TEvent>` 的具体类 → 该闭合 TEvent 的 handler binding（`ExpectedHandlerTypes`）；同一 handler 实现多个闭合接口时**分别生成**。
  - 携带 `[LocalEvent]` 的事件类型 → 声明 binding（即使本程序集零 handler）。
- 产出（`internal`/`public` 见 3.3）：contributor + 每事件强类型 invoker 委托 + handler DI 注册（`TryAddEnumerable` Scoped，typed binding）+ 本地身份常量 + 手工 alias 清单。
- 诊断（ID `CCCLE001+`，全仓唯一性先检查）至少覆盖：开放泛型事件/handler、不可访问类型、无法生成 activation、重复冲突（同程序集同 Type 重复声明）、replay 声明缺 codec 工具链（见 §4.3）、未封闭 object/interface/abstract payload 的多态契约。
- 生成关闭（`CrestCreatesCodeGeneration=false` 或未启用 contributor）→ 无兜底、无扫描。

### 3.2 事件声明属性（EventBus.Abstractions）

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class LocalEventAttribute : Attribute
{
    public int Version { get; set; } = 1;
    public bool Replayable { get; set; }          // 默认 false = dispatch-only
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
public sealed class LocalEventLegacyIdentityAttribute : Attribute
{
    public LocalEventLegacyIdentityAttribute(string legacyTypeName, int version = 1);
}
```

- **Replayable 是显式能力**：默认 dispatch-only；开启 = 在**事件定义类型**上加 `Replayable = true` 且事件所属项目导入 JSON 契约工具链（§4.3）。失败时不临时猜测。
- dispatch-only：普通分发可用；handler 失败时**不写 DLQ 记录**，报告「dispatch-only 不能记录 DLQ」并保留原 handler 异常（不伪造成功记录）。
- `[LocalEventLegacyIdentity("旧类型名", 版本)]` 声明旧 payload 身份 → 冻结期生成静态 alias 表；**绝不 Type.GetType**。无法映射的旧消息明确报告且保留原记录。

### 3.3 显式启用契约（Host 调用静态生成产物）

- 每程序集生成（仅在存在本地 handler/声明事件时）：命名空间 `CrestCreates.Generated.{程序集简单名（非法字符替换为 _）}`，`public static class LocalEventRegistrations`：
  - `public static IServiceCollection AddLocalEvents(this IServiceCollection services)`：注册 contributor + handler DI 绑定（幂等）。
- Host/框架包的公开装配入口**显式调用**对应程序集的 `AddLocalEvents()`：
  - 框架自身包：在各自 `AddXxx()`（如 `AddCrestWeb`、`AddHumanTaskRuntime`）内部调用**同程序集**生成扩展；
  - 业务程序集：Host 显式调用（一段 using + 一行调用）；第三方事件用 Host 侧 `builder.Declare(typeof(T), ...)`，不要求修改第三方类型。
- 不寻找「某个约定生成类」后反射调用；编译期模块聚合不得代替显式调用成为隐式激活（模块聚合对生成产物不可见，属事实边界，见 §8）。

## 4. JSON 与死信身份

### 4.1 S1 编译实验（工具链顺序，已锁定）

实验位置：`/tmp/crest-126-s1-json-exp`（关键文件全文附录于实施计划文档）。结论（全部有可复现证据）：

| 检查 | 结果 | 含义 |
| --- | --- | --- |
| A1/A2：编译期文件（`<Compile Include>` 注入，模拟 BuildTask 产物）在 partial 上下文的另一部分之外注入 `[JsonSerializable]` roots | 通过：`GetTypeInfo(typeof(T)) as JsonTypeInfo<T>` 非空、round-trip 正确 | **roots 注入只允许走「CoreCompile 前的磁盘源文件」路径** |
| A3：未注册类型 | `GetTypeInfo` 返回 null（无反射 fallback） | fail-closed 可判定「codec 缺失」 |
| B：SG 输出 `[JsonSerializable]` | STJ 生成器**看不到**（编译期 CS0534：抽象成员未实现；手工补实现后无重复成员冲突） | 禁止「一个 Roslyn 生成器读取另一个生成器本轮输出」 |
| C：targets 导入设置的 MSBuild 属性 | 需配套 `<CompilerVisibleProperty>` 才对 SG 可见 | 工具链存在性检测用 property；**注意**：仓库现有 `build_property.CrestCreatesDynamicApiAotGeneration` 无 CompilerVisibleProperty 声明，实为死代码（记录，不在本 Issue 修复） |
| D：反射 JSON 关闭 | 环境序列化确定性抛异常 | 动态 JSON 无任何隐式可用性 |

**正式方式（锁定）**：扩展现有 `CrestCreates.JsonContracts.BuildTasks` 构建期工具链（即 A 路径的既有先例，`Mcp.Memory` 等 7 个项目在用）为可重放 LocalEvent 生成 roots；**不**新增平行 JSON 工具链。

### 4.2 codec 装配（三方解耦）

1. **BuildTask（事件所属项目，工具链导入时）**：语义扫描本项目源码中 `[LocalEvent(Replayable = true)]` 的事件类型 → 在既有生成文件中产出：
   - `[JsonSerializable(typeof(T))]` 附于 `CrestCreates.Generated.LocalEventJsonSerializerContext : JsonSerializerContext`（partial，官方 STJ 生成器在 CoreCompile 消费——A 路径）；
   - `CrestCreates.Generated.LocalEventJsonCodecContributions.Register()`：向静态 `LocalEventJsonCodecRegistry`（EventBus.Abstractions，键 = CLR Type）注册 `typeof(T) → () => LocalEventJsonSerializerContext.Default.GetTypeInfo(typeof(T))`。
2. **SG（任意程序集）**：只产出分发元数据；binding 的 `CodecFactory = static () => LocalEventJsonCodecRegistry.Resolve(typeof(T))`（**SG 从不引用上下文类名**，消除跨产物命名耦合）。
3. **生成扩展（事件所属程序集）** 在 `AddLocalEvents()` 中调用同程序集的 `LocalEventJsonCodecContributions.Register()`；重启放的事件**所属**项目必须导入工具链，**handler-only 程序集不需要**（跨程序集 codec 经静态 registry 解析）。
4. 冻结期：Replayable binding 的 `CodecFactory()` 必须非空且 `JsonTypeInfo.Type == EventType`，否则装配期拒绝（「replay 声明缺少 codec」）。
5. SG 诊断：本项目源码含 `[LocalEvent(Replayable = true)]` 且未检测到工具链属性 → **编译错误**，提示导入 `CrestCreates.JsonContracts.Build`。工具链 targets 增加 `CompilerVisibleProperty`。

### 4.3 编解码与拒绝规则

- 序列化/反序列化只用 `JsonTypeInfo` 参数重载（STJ fast path），禁止 Type overload 动态 JSON；成功发布与失败入队都不得临时反射生成 serializer。
- object、接口、抽象类型及自定义转换器只允许**已有、明确声明的封闭 JSON 契约**；`CapabilityEventEnvelope.Payload: object` 等不新建任何 fallback（保持 dispatch-only）。
- 未知 key、未知版本、身份冲突、JSON 损坏、反序列化 null、与注册类型不一致 → 重放失败（不标成功）。

### 4.4 死信记录字段（`DeadLetterMessage`，最小 additive 兼容）

- 新增**可空** `string? LocalReplayKey = null`（record 尾部可选参数，旧构造调用零改动）；EF `DeadLetterEntity` 增加同名可空列 + 映射。不改动既有列含义。
- 新记录：`LocalReplayKey = binding.LocalReplayKey`；`EventVersion` = descriptor 版本或 `[LocalEvent].Version`；`EventName` = descriptor 名（治理）或完整 metadata 类型名；`EventDescriptorId` = descriptor Id（治理时）；`PayloadTypeFullName` 保留为诊断/兼容字段，**不再作为可执行类型解析指令**。
- 重放查找顺序：① `LocalReplayKey + EventVersion` → ② `PayloadTypeFullName` 规范化（首个 `,` 前）+ `EventVersion` → ③ 静态 legacy alias 表。全 miss = 确定性拒绝并保留记录。
- 记录失败（含序列化失败、治理/本地版本冲突、store 故障）：**保留原 handler 异常为主错误**，附带可观察的记录失败报告，不以序列化/记录异常覆盖原故障。

## 5. 统一分发语义

### 5.1 分发表（唯一契约）

| 情况 | 契约 |
| --- | --- |
| 未声明事件 | 进入 handler/Channel 入队前抛 `UnknownLocalEventException`；不视为成功、无 fallback |
| 已声明且零 handler | 普通分发 no-op（保留 optional notification 场景）；不据此证明可靠交付 |
| 已声明有 handler | 按最终 DI 枚举顺序逐个 await；首个异常停止，后续不执行 |
| 必需 handler（生成清单）缺失 | 分发前确定性失败 `LocalEventRequiredHandlerMissingException`（`IsInstanceOfType` 判定）；不降格为零 handler |
| 多 handler | 泛型/非泛型/Channel/重放顺序一致：DI 枚举顺序（生成注册 = 稳定元数据顺序，手工绑定 = 显式注册顺序）；迁移记录旧/新顺序差异 |
| scoped 依赖 | 同次分发共用调用方 scope；Channel 每事件新 scope；重放每次 attempt 新 scope；异步释放 |
| 取消 | 入口预取消不调用 handler/不入队；执行时传原 CT，各 handler 前检查；不声称可强制终止不配合的 handler |

- 泛型与非泛型入口都按**事件实例精确运行时类型**查询同一注册项（泛型入口内部转非泛型路径）；不做运行时继承扫描；基类订阅需生成期显式闭合映射。
- 静态基类与非泛型精确类型的差异是**有意收口**：写迁移说明 + 判别测试。
- 同步 Publish：membership → `IEventValidator`（canonical 名称）→ dispatch；失败入 DLQ 后**原异常**继续向调用方传播。
- Channel Publish：membership 与验证先于 enqueue；入队成功即返回成功（容量/背压/单读者契约不变）。Consumer：每事件新 scope；失败记录 DLQ（共用 recorder）后**日志并继续**（不 rethrow）；停机取消时不记录。
- 首次发布中因取消（CT 触发）中断：不记录 DLQ（非业务失败），仅传播取消。

### 5.2 强类型 dispatcher（`DefaultLocalEventDispatcher` 重写）

```csharp
// ctor: (ILocalEventDispatchRegistry registry, IServiceProvider serviceProvider)
DispatchAsync(ILocalEvent e): 预取消检查 → registry.TryGet(e.GetType()) 否则 Unknown → binding.Invoker.InvokeAsync(sp, e, ct)
DispatchAsync<TEvent>: => DispatchAsync((ILocalEvent)e)
```

`LocalEventInvoker<TEvent>`（sealed，基类 `LocalEventInvoker`）：持 `(IServiceProvider, TEvent, CancellationToken) → Task` 闭合委托（= 框架静态核心 `LocalEventDispatch.InvokeHandlersAsync<TEvent>`）；非泛型路经 `IsInstanceOfType` 级别强转（`(TEvent)e`），零反射。预期清单校验与取消检查在核心内完成。

### 5.3 重放核心（`LocalDeadLetterReplayExecutor`）

`load → 预检（状态/预算/身份/codec/handler）→ MarkRetrying → 新 scope 解码及 dispatch → 成功/失败状态更新`。Manager（单条/批量入口）与 BackgroundService（周期/批次/停止）不得复制解码或状态迁移；重放直接进入同一 dispatcher，不经 Publish。

状态表：

| 情形 | 行为 |
| --- | --- |
| 仅 Pending 且预算未耗尽 | 允许普通重试；Retried/Archived/Retrying 拒绝（返回失败，状态不变） |
| 预算 | **用消息持久化的 MaxRetries**；全局 options 只作新消息默认值；MaxRetries < 1 或计数非法 → 确定性拒绝 |
| Pending 已耗尽预算 | 归 Archived，**不执行 handler** |
| 预检失败（未知 key/codec/缺 handler/非 replayable） | **发生在 MarkRetrying 前**：返回失败并保留消息（仍 Pending），绝不卡 Retrying |
| attempt 成功 | `MarkRetried` |
| handler 失败 | RetryCount + 1；未耗尽回 Pending，耗尽归 Archived；保持 MessageId，不新增同事件记录 |
| 确定性解码失败（坏 JSON/null/类型不符） | 计入 attempt 预算，归 Pending/Archived |
| 执行前取消 | 状态不变 |
| 执行中合作式取消 | 尽力恢复 Pending（独立、有界状态写入 token）；不计业务失败预算、不标成功；后再传播取消 |
| 状态写入失败 | 报告（保留原异常链），不得声称恢复成功 |
| 多 handler 重放 | 从头执行；前次已成功 handler 可能重复；无 per-handler checkpoint，不声称幂等/exactly-once |
| 零 handler 重放 | 失败（已有死信重放至少需要 1 个匹配 handler），不得标成功 |
| 记录时治理/本地版本不一致 | 确定性报错（作为记录失败报告，不覆盖原 handler 异常） |

不承诺：进程崩溃/store 故障后遗留 Retrying 的恢复；人工与后台并发重放的互斥（无原子 claim/CAS）。

### 5.4 InMemoryDeadLetterStore 修正

容量检查仅作用于**新键**：`_messages.Count >= MaxQueueSize && !ContainsKey(MessageId)` 才拒绝；同键状态更新在任何容量下生效。附回归：满队列下失败重试仍可更新状态。

## 6. 与已闭环主链的边界

- `HumanTaskCompletedOutboxHandler` required-consumer resolver / `RequiredConsumerIds` / Ack/Retry/DeadLetter 权威不变；optional lane 零 handler/失败/超时不替代 required-consumer、不阻塞 Ack（回归保留）。
- 不恢复已关闭的 runtime subscriber scanner、动态 Subscribe/Unsubscribe；`IEventBus` 空订阅兼容接口注明支持边界。
- 不给 LocalEvent 自动包 UoW；不改动 #137 的 BeginScope/StartAsync 或 CAP lease。
- 不从内存 DLQ native 证据推导 EF DLQ Provider/Mediator/RabbitMQ/整套 Runtime 的 NativeAOT 状态；`EfCoreDeadLetterStore` 的 NativeAOT 状态按独立证据声明（本 Issue 只做共享模型/映射兼容性验证）。
- 域事件（`IDomainEvent : ILocalEvent`）同样走唯一索引：真实消费方（测试/样例）迁移为声明 + 显式启用；未声明即 Unknown（不保留静默 no-op 双轨）。

## 7. 实施切片与退出条件

| 切片 | 工作 | 退出条件 |
| --- | --- | --- |
| S1 | 本 Spec + 实施计划落库；JSON 工具链编译实验（§4.1） | 关键决策落实、无双轨/fallback |
| S2 | 增量生成器、typed activation、静态 contributor、装配诊断；统一 generic/non-generic；迁移消费者 | 生成代码可编译，DI 生命周期与错误行为测试通过 |
| S3 | typed codec（工具链扩展）、同步/Channel 失败记录共用、Manager/后台共用重放核心、alias、状态与取消修复 | 正常→失败入队→人工/后台重放闭环，负例可区分旧缺陷 |
| S4 | 独立 native Host、脚本、架构守卫、回归、文档与 memory.md | 真正 publish/link/run 全场景断言通过，移除正式路径反射桥 |

每片提交说明列清：完成契约、变更范围、实际验证、剩余限制。不重开 GitHub CI；本地验证为门槛，脚本设计为日后可直接接回 CI。

## 8. 验收矩阵（可执行用例，不能只测生成源码字符串）

| 类别 | 必须断言 |
| --- | --- |
| 分发一致性 | generic/non-generic/基类变量/ILocalEvent 变量得到相同 handler 效果；无继承隐式扫描 |
| fan-out | 多 handler 顺序、首错停止、已成功 handler 重放可能重复的明确结果 |
| scope | handler 依赖在当前 scope、不同 Channel/retry attempt 使用新 scope、异步释放；并发 scope 不串实例 |
| 装配 | 缺 contributor、冲突 type/key/version、缺 binding/codec、跨项目 handler 与 event、生成关闭无 fallback |
| 未知/零 handler | 未声明失败；显式零 handler 普通发布 no-op；旧死信缺 handler 不标成功 |
| JSON | typed round-trip、损坏/null/未知版本/冲突身份、旧 AQN 静态 alias、无 alias 拒绝、动态 object 契约拒绝 |
| DLQ | 同步原异常传播 + 一条失败记录；Channel handler 失败记录且继续下一事件；DLQ 写失败可观察 |
| 重试 | 人工/后台同一核心；成功 Retried；失败预算与 Archived；预检失败不留 Retrying；不重入 Publish |
| 取消 | 入队前、handler 间、attempt 中取消；消息状态与取消异常一致；不夸大非合作式终止 |
| 可靠链回归 | HumanTask optional lane 零 handler/失败/超时不替代 required-consumer，也不阻塞 Ack |

Native fixture（S4）：`tests/Runtime/Eventing/CrestCreates.EventBus.Local.AotHost`（linux-x64 首组证据，`JsonSerializerIsReflectionEnabledByDefault=false`），经正式注册/Publish/Channel/Manager/HostedService 入口执行：

1. 泛型与非泛型成功分发，断言 payload 值、handler 次数、scoped 实例。
2. handler 首次失败 → 断言真实 DLQ payload/身份/状态；翻失败开关 → Manager.RetryAsync 解码执行 → 断言效果与 Retried。
3. 另一条消息经实际后台重试服务成功（可控间隔 + 有界等待观测状态，不直接调用核心）。
4. 未声明事件、未知 replay key/version、坏 JSON、缺 handler/codec 均按契约失败，无假成功/无 fallback。
5. Channel enqueue→consumer→失败入 DLQ→重放效果真实发生。
6. 原生产物退出码 0 + sentinel `CRESTCREATES_LOCALEVENT_TYPED_REPLAY_NATIVE_OK`；脚本检查超时/退出码/sentinel，保存 publish/run 日志、head、RID、SDK 与断言证据。

架构守卫（`tests/Boundary/`）：正式 LocalEvent 分发/codec/replay 路径禁止反射桥、动态 Type JSON overload、运行时扫描；校验各入口引用唯一索引/核心。不用 IL suppression 或关键词扫描代替行为测试。

## 9. 完成定义与交接

- [ ] 正式主链只剩一个分发索引、一个重放核心，无动态 fallback。
- [ ] 现有 EventBus/Channel/DLQ 测试迁移后通过；HumanTask optional lane 回归通过；依赖边界测试通过。
- [ ] 真正 NativeAOT publish/link/run 覆盖成功、失败入队、人工重放、后台重放与拒绝路径，证据绑定最终 head。
- [ ] EF DLQ 共享模型/映射兼容性有具体验证；其 NativeAOT 状态按独立证据声明。
- [ ] obsolete/替换文件归档 `99_RecycleBin`，文档给出退出路径，不保留「兼容成功兜底」。
- [ ] 更新 memory.md、#123 证据矩阵相关条目与本 Issue 验收记录；未覆盖 Provider 明写未验证。
- [ ] 提交 PR 供审查，不自动合并。

**范围外**：#127 RabbitMQ 反射桥、CAP、可靠 Outbox required-consumer、持久化 DLQ 并发抢占/租约、分布式 exactly-once、任意 object payload 序列化、DLQ 管理 UI、MediatorAdapter（保持独立适配器支持声明，不作主链 fallback）。
