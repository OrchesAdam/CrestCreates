# #127 RabbitMQ 静态订阅与事件契约 — 设计规格（Spec）

- 契约来源：Issue #127 详细设计评论（2026-10-13，comment 6139956922）+ 总控 #121。
- 分支基线：`1cad6e35`（#126 已合并）+ 后续文档提交 `6dd608a3`；实施分支 `feat/issue-127-rabbitmq-static-bindings-20261013-145133`。
- 本 Spec 是 S1 交付物：锁定契约、迁移清单与实测结论；未完成实验一律显式标注，不用假设填“通过”。
- SDK/运行时锁定：RabbitMQ.Client 7.2.0（`Directory.Packages.props`）；broker 为 `rabbitmq:4.0-management-alpine`（`infra/docker-compose.eventbus.yml`）。

---

## 1. 现状事实（基线代码 + S1 实测）

### 1.1 代码事实（逐条已核实）

| # | 位置 | 事实 |
| --- | --- | --- |
| F1 | `src/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ/Consuming/RabbitMqConsumer.cs:57` | 订阅表经 `Type.GetType("...RabbitMqSubscriptionRegistry, CrestCreates.EventBus.RabbitMQ")` + `GetMethod/Invoke` 反射获取；生成器把注册表类写进**业务程序集**（`CrestCreates.EventBus.RabbitMQ.Generated` 命名空间但存在于 handler 所在程序集），框架程序集里永远找不到 → 任何真实 Host 的子集恒为空列表。 |
| F2 | 同上 `GetEventType` | AppDomain 全程序集扫描 + 短名匹配；同短名事件串事件风险；解析失败静默回退 `null`。 |
| F3 | `RabbitMqSubscriptionSourceGenerator` + `RabbitMqSubscriptionCodeWriter` | 属性按短名/`EndsWith` 匹配；`[RabbitMqSubscribe]` 第一个字符串参数被同时当作：绑定 routing key、信封校验名、**C# cast 类型**（`(EventType)evt!`）三者混用；无 method 签名校验；缺事件/Handler 校验；订阅字符串必须恰好是可编译类型名才能编译，语义完全未定义。 |
| F4 | `RabbitMqPublisher.cs` + `RabbitMqEventBus.cs` | publisher 以 CLR 短名（`typeof(TEvent).Name`）写信封 `EventType` 并作默认 routing key；event bus 又以 `EventNamingConvention.GetRoutingKey` = lower_underscore 覆盖 routing key；非泛型入口 `IDomainEvent` 经 `GetType()` 但同样短名。发布/订阅身份与路由三方不一致。 |
| F5 | `RabbitMqPublisher.PublishConfirmationTracker` | 自建 confirm 账本与 SDK 自带 tracking 并行；每次发布 attach/detach 事件处理器；错误路径 `channel.Dispose()` 不 `ReturnChannel` → semaphore 许可泄漏（`RabbitMqConnectionPool.cs:183-193`）。 |
| F6 | `RabbitMqConsumer.HandleMessageAsync` | 每订阅建独立 consumer（同队列多订阅 = competing consumer 被误当 fan-out）；Ack 异常落入业务失败 catch 后按“重试”再复制一份消息；重投丢 MessageId/CorrelationId/ContentType/Type，且经 consumer channel 发布（与消费共用 channel）；忽略 `RetryDelaySeconds`（立即重投）；`x-retry-count` 以字符串写入（接收端只认 `byte[]`）。 |
| F7 | 装配 | `AddRabbitMqEventBus<TContext>`/`AddRabbitMqEventBus(JsonSerializerContext)`/`RabbitMqEventBusModule` 三套注册路径，生命周期不一致（publisher Transient vs Singleton），且都把单个全局 `JsonSerializerContext` 注入 publisher/consumer —— 多程序集上下文互相覆盖；`IEventValidator` 无任何 DI 注册（ctor DI 缺口）；payload 序列化在无用户 context 时回退框架信封 context（AOT 下必然失败）。 |
| F8 | 事件内核接线 | `AddEventKernel` 无调用方；`EventRegistryBootstrapper` 未注册为任何 HostedService/IBootstrapTask；三个 `IRegistryValidator<GeneratedEventDescriptor>`（`DuplicateNameVersionValidator`/`EventVersionChainValidator`/`UniquePayloadTypeValidator`）无 DI 注册；`IEventDescriptorProvider` 无 DI 注册；仓库内 `[CrestEvent]` 声明数为 0。结论：**`[CrestEvent] → IEventDescriptorProvider → IEventRegistry` 生成接线在基线中不存在**，本任务 S2 补齐。 |
| F9 | 测试 | 集成测试（`RabbitMqPublishConsumeTests` 等）全部手工 `BasicConsume`，不经过框架 consumer；单元 `ConsumerTests` 直接构造 `RabbitMqConsumer` 断言旧 ctor。 |
| F10 | 文档 | `docs/superpowers/specs/2026-04-26-rabbitmq-event-bus-design.md` 的“AOT 兼容”“assembly-qualified wire type”“生成器已校验 JSON”“requeue:true 延迟重试”等描述与代码事实不符（S4 修正）。 |

### 1.2 S1 实测结论（2026-10-13，本机 podman broker）

**实验 A — RabbitMQ.Client 7.2.0 confirm 行为**（`/tmp/crest-127-s1/confirm-exp`，日志 `/tmp/crest-127-s1/confirm-exp-run.log`）：

| 用例 | 结果 |
| --- | --- |
| tracking ON + 正常发布 | `await BasicPublishAsync` 返回未同步完成（`completedBeforeAwait=False`），完成于 broker ack 之后 → **await 即参与确认等待**。 |
| tracking ON + mandatory 不可路由 | await 抛 `RabbitMQ.Client.Exceptions.PublishReturnException`（`312 NO_ROUTE`），channel 保持 open，`BasicReturnAsync` 事件同时触发。 |
| return 之后复用 channel | 再次发布成功。 |
| tracking OFF + mandatory 不可路由 | await 不抛（返回不可见），仅异步事件可观察。 |

结论：主链必须保持 `publisherConfirmationsEnabled + publisherConfirmionTrackingEnabled` 双开（`RabbitMqConnectionPool.CreateChannelAsync` 现状即如此），**移除自建 confirm 账本**（F5）；unroutable 经 await 异常路径确定性暴露，nack/timeout/断连分别映射独立诊断（timeout 由框架侧组合 CT + 配置时限实现）。

**实验 B — 最小 NativeAOT + 真实 broker**（`/tmp/crest-127-s1/aot-exp`，日志 `aot-exp-publish.log` / `aot-exp-run.log`）：

- `dotnet publish -c Release -r linux-x64 --self-contained -p:PublishAot=true` 干净重建：原生链接成功（“Generating native code”），**0 条 IL2026/IL3050**，产物 7.5MB。
- 原生产物 `JsonSerializerIsReflectionEnabledByDefault=false` 下连接真实 broker：声明 direct exchange/queue/binding → tracking 通道发布（await confirm）→ `AsyncEventingBasicConsumer` 消费 → STJ Source-Gen typed 反序列化（decimal + `List<string>`）→ Ack → sentinel 输出、exit 0：`CRESTCREATES_S1_AOT_BROKER_EXPERIMENT_OK`。
- 结论：锁定组合（7.2.0 + net10.0 + STJ source-gen）在 linux-x64 原生下无阻塞；S4 正式 fixture 沿用该形态但必须走正式主链（`AddRabbitMqEventBus` + 生成订阅 + 正式 publisher/consumer）。

**实验 C — descriptor/provider bootstrap 接线**：逐文件核实 F8。器路径只有两条且都未接线：DI 版（`EventRegistryBootstrapper(IEnumerable<IEventDescriptorProvider>)`）与静态版（`MetadataBootstrapper.BuildAll` → `DescriptorProviderRegistry.GetProviders<T>()`，由 SchemaCapability 生成器的 `[ModuleInitializer]` 写入进程全局 bag）。本任务锁定 **DI 版** 为主链（显式 contributor/扩展，无 ModuleInitializer、无进程全局扫描）；静态版不扩展、不新增使用。

**实验 D — 跨程序集 JSON 生成顺序**：#126 已验证的结论直接沿用——roots 只走「BuildTasks 于 CoreCompile 前写磁盘源文件 → 官方 STJ 生成器消费」路径；Roslyn SG 不读另一个 SG 输出。跨程序集维度：codec 只由**事件所属程序集**生成（其项目引用即可编译）；handler 程序集只引用事件 Type（普通 ProjectReference），不需要也不得重复生成 codec；Host 级装配顺序靠「所有显式扩展先注册、冻结在后解析」保证与调用顺序无关。

---

## 2. 唯一事件权威与 wire identity

### 2.1 权威分层（锁定）

```
GeneratedEventDescriptor / IEventRegistry（CrestCreates.Event.Abstractions + CrestCreates.Event）
    = 事件语义唯一权威：Id / Name / Version / PayloadType / Scope / State
RabbitMqBindingRegistry（CrestCreates.EventBus.RabbitMQ，Host 级冻结）
    = 传输绑定与执行索引：发布索引 / wire 索引 / 队列消费计划 / alias 表
```

- 传输绑定引用 descriptor 的 `Id / Name / Version / PayloadType`，冻结时核对**精确类型、版本、Active 状态、Integration scope**；不得用 `GetByName()`（自动取最新 Active 版本）替代收到的指定版本。
- 新主链禁止 CLR 短名、程序集限定名、`LocalReplayKey` 作为 wire identity。允许 `typeof` / `GetType()` 做**静态索引查找**；禁止据字符串动态发现类型（`Type.GetType` 字符串解析、程序集扫描、`GetExportedTypes`）。
- 默认 Id 复用 `GeneratedEventDescriptor.GenerateId(name)`（SHA256(Name) 前 12 位 hex，`evt_` 前缀）；显式 `Id`/`Version` 语义不经 RabbitMQ 重算。一个具体 Type 的默认发布身份必须唯一；多版本/多名称歧义 → 冻结期确定性失败，要求显式静态选择或不同具体 payload Type；禁止依赖注册先后。

### 2.2 声明与生成（锁定）

事件定义程序集：

```csharp
[CrestEvent(Name = "orders.order-created", Version = 1, Scope = EventScope.Integration)]
public sealed class OrderCreatedEvent { public string OrderId { get; init; } = ""; ... }
```

- 生成期产出两类静态物（同一程序集、同一 `AddEventDescriptors` 扩展）：
  1. `GeneratedEventDescriptorProvider : IEventDescriptorProvider`（`PayloadType = typeof(OrderCreatedEvent)`，Id 缺省按 `GenerateId(Name)`，`State = Active`，`ChangeKind` 取现有默认）；
  2. JSON codec 贡献（JsonContracts 工具链生成，见 §4）。
- Handler 程序集：`[RabbitMqSubscribe("orders.order-created", Version = ..., RoutingKey = ..., Exchange = ..., Queue = ..., PrefetchCount = ...)]` —— 第一个参数是对 canonical **Name** 的引用/约束；payload 类型来自方法参数（编译期闭合类型）。
- 订阅 `Version` 语义：缺省（0）时冻结期解析——存在**恰好一个** Active 且 PayloadType 与参数类型精确相等的 descriptor 时绑定该版本；多版本歧义 → 冻结失败，诊断要求显式指定版本。
- 第三方事件：Host 通过手工 `IEventDescriptorProvider`（既有契约）+ 显式 typed binding（§3.4）参加同一冻结校验；不作失败 fallback。属性生成与手工 provider 重复定义同一 identity（Name+Version 或 Id 冲突）必须报告（注册表 validator + 冻结双重防线）。
- `IEventValidator` 复用现有链路：`RegistryEventValidator` 只做名称/状态检查，**不得**被宣称为已执行 payload/version/scope 检查；装配缺失时给出可读诊断（注册期 TryAdd，禁止隐藏 ctor DI 缺口）。

### 2.3 Envelope（最小 additive）

保留 `Payload`、`Headers`、`Timestamp` 及现有 JSON 外形；新增两字段：

```json
{ "eventType": "orders.order-created", "eventId": "evt_3f9c2a41b7d0", "eventVersion": 1,
  "payload": "{...}", "headers": { }, "timestamp": "2026-10-13T06:47:14Z" }
```

- `EventType` 写 canonical **Name**；`EventId`/`EventVersion` 新消息必写。执行键 = `(EventId, EventVersion)`；Name 用于交叉校验和诊断。三者不一致 → 接收期确定性拒绝（零业务调用）。
- 未知版本**不自动降级**；`(EventId, EventVersion)` 未冻结 → 拒绝。
- 原始短名/旧格式消息只通过**显式、静态、无歧义的 legacy alias** 映射到同一 canonical binding；alias 不重定义事件；缺版本旧消息只能由 alias 声明的版本解释（alias 必填显式 version，不猜测）。
- 字段不完整（如 EventId 有而 EventVersion 无）且不匹配明确旧格式的消息走拒绝路径，不经兼容蒙混。
- 发布侧只写新格式；旧格式读取有 alias 清单和退出说明（§8 迁移表），不长期维护第二个反射消费者。真实旧 body/headers fixture 必须入测试（S3）。

### 2.4 路由与身份分离（锁定）

- 保留 durable direct exchange、队列、DLX/`.dlq`；**不**改成 topic exchange、不引入 wildcard 语义。
- 新绑定默认 `RoutingKey = canonical Name`；`Exchange` 默认取配置（`RabbitMqOptions.DefaultExchange`）；默认队列名保持既有规则 `crestcreates.{订阅名}`（订阅名 = `[RabbitMqSubscribe]` 第一参数）。
- 已部署 binding 的 Exchange/Queue/RoutingKey 通过显式静态配置保留（`RoutingKey` 属性参数 / builder 配置），不自动重命名、不双发、不自动绑定所有短名。
- 队列/交换机同名但参数不兼容（prefetch/durable/DLX 参数冲突）→ **启动失败**并给出迁移说明；不得把 broker precondition failure 当健康吞掉。
- 同短名事件可不同 canonical identity + 不同 routing key；若有意共享 routing key，只能在已声明队列内按**精确 envelope identity** 分派。

---

## 3. 强类型注册、冻结与启动顺序

### 3.1 契约（`CrestCreates.EventBus.RabbitMQ`，名称按仓库规范微调，职责锁定）

| 契约 | 职责 |
| --- | --- |
| `IRabbitMqRegistrationContributor` | 每程序集静态贡献：事件绑定（descriptor 引用 + 精确 payload Type + codec）、订阅绑定与来源清单（程序集简单名、Handler、method）。合成该 contributor 的生成类名不允许被 consumer 硬编码。 |
| `RabbitMqEventBinding` | canonical 引用（Id/Name/Version/PayloadType）+ 精确 payload Type + typed codec（冻结时快照为闭合 `JsonTypeInfo` / 编解码委托）+ RoutingKey + 来源描述。 |
| `RabbitMqSubscriptionBinding` | event 引用 + Handler Type/method 标识 + exchange/queue/routing/prefetch + **闭合 invoker**（`IServiceProvider → Task`，内部强类型解析 Handler 并直调方法）+ 来源描述。 |
| `IRabbitMqBindingRegistry` | Host 级冻结只读：发布索引（`Type → binding`）、wire 索引（`(EventId, EventVersion) → binding`）、队列消费计划（`(exchange, queue) → 有序 handler 序列`）、alias 表。冻结后不可变。 |
| generated registration extension | 每程序集一个显式扩展（事件侧 `AddEventDescriptors`；订阅侧 `AddRabbitMqSubscriptions`），注册 contributor / Handler DI / 所属项目 JSON contribution。 |

Registry 只持有静态元数据、codec、委托；不捕获 root `IServiceProvider`、业务 scope 或 Handler 实例。**codec 贡献为 per-Host 显式装配**：事件所属程序集的 `AddEventDescriptors()` 以 `EventJsonCodecRegistration`（PayloadType/CodecFactory/Origin）注册进宿主 DI，冻结时按 Host 聚合解析并快照进绑定（同一 Origin 重复激活幂等；同一 PayloadType 多 Origin 冲突为确定性装配失败）；不存在进程全局 codec 字典（R1-RMQ-05 已修正 Spec 原措辞）。

### 3.2 冻结流程与校验（全部先于 broker I/O）

```
Startup:
  1. 既有 EventRegistry bootstrap 完成（未完成 → 可读诊断）
  2. RabbitMQ 合并校验/冻结（本任务新增；失败 → 启动失败，含来源/事件键/队列/Handler 明细）
  3. 创建 broker 拓扑（declare exchange/queue/binding 幂等）
  4. 开始消费（BasicConsume）
```

冻结最低校验集（诊断排序稳定，含来源程序集、事件键、队列、Handler/method）：

1. 缺 required contributor / Handler 注册（Host 显式清单 expected vs 实际装配比对，§3.3；required Handler 除元数据比对（经 `IServiceProviderIsService` 探测，不构造实例）外还须在 DI 中真实可解析；非 required Handler 由 delivery 期整批预解析兜底）；
2. 重复 identity 映射到不同 Type（同 Name+Version 双来源不同 PayloadType；跨程序集重复启用同 contributor 幂等，元数据不同的同 Id 贡献拒绝）；
3. 同一 Type 的发布身份歧义（默认 Id/Name 冲突未显式消解）；
4. codec 缺失 / `JsonTypeInfo` 与声明 Type 不符；
5. canonical 引用不符（订阅 Name 无 descriptor；descriptor.PayloadType ≠ handler 参数类型；Id/Name/Version 组合不存在）；
6. 版本/状态/scope 不合法（非 Active、非 Integration、版本歧义未显式选择）；
7. alias 冲突（alias 与 canonical Name 冲突、重复 alias、alias 目标缺失、alias 无显式版本）；
8. 重复 Handler binding（同程序集内同 method 重复声明；跨程序集相同 handler 类型重复贡献）；
9. 同队列拓扑/prefetch 参数冲突（显式参数与默认值不一致、prefetch 越界）；
10. 非法配置值（§8）。
- 以上全部失败即抛 `RabbitMqCompositionException`（逐条违规行），**任何**声明校验先于第一个 broker I/O；不得先建队列再报缺 codec。

### 3.3 装配模式与 required 清单（锁定）

- contributor 通过生成扩展**显式**加入 DI；consumer 不知道生成类名，不用 `Assembly.Load`、反射扫描、`ModuleInitializer`、“寻找所有已加载程序集”。
- 支持事件定义项目 / 多 Handler 项目 / Host 分开；生成扩展按程序集命名空间隔离（`CrestCreates.Generated.<AssemblySimpleName>`），Host 静态调用多个；调用顺序不影响最终结果；同一 contributor 重复启用幂等。
- **required 清单**：Host 通过 builder 显式声明期望（typed 引用）：`RequirePublishableEvent<TEvent>()`、`RequireSubscription<THandler>()`；冻结比较 expected ⊆ 实际，缺失时诊断指出缺失程序集/事件/Handler。不做“无清单、无扫描也能发现忘记启用的程序集”的暗示。
- 默认启用 consumer 而**零订阅 = 配置错误**（冻结失败并指向 `PublisherOnly`）；显式 `PublisherOnly()` 允许零订阅、不注册 consumer；其发布事件仍须完整 descriptor + codec。
- 生成器关闭（`CrestCreatesCodeGeneration=false`）无 fallback：有消费要求的 Host 经编译契约或启动校验失败。低层手工 typed binding（§3.4）只为静态装配/第三方事件服务，受同一冻结校验。

### 3.4 手工 typed binding（第三方/低级路径，受同一校验）

- `builder.AddPublishedEvent<TEvent>(JsonTypeInfo<TEvent> codec, string? routingKey = null)`：为手工 provider 声明的第三方事件补 codec / routing；descriptor 仍须存在于 EventRegistry。
- `builder.AddContributor(IRabbitMqRegistrationContributor)`：显式静态装配入口；不引入字符串发现或动态注册。

### 3.5 启动顺序实现（不依赖 HostedService 注册顺序）

- `AddRabbitMqEventBus` 注册顺序固定：事件内核（TryAdd）→ `RabbitMqCompositionHostedService`（组合服务，先注册）→ consumer（后注册）。
- `RabbitMqCompositionHostedService.StartAsync`：① 确保 EventRegistry Built（`RegistryBase.Build` 幂等，双入口不重复构建）；② 解析并冻结 `IRabbitMqBindingRegistry`（冻结工厂校验 State==Built，否则可读诊断）；③ 置 readiness gate；④ 失败时先 fault gate 再抛（宿主启动确定性失败）。
- consumer `ExecuteAsync` 第一步 `await readiness.WaitAsync(stoppingToken)`；即使被先启动也不产生任何 broker I/O。module 与 plain Host 两种入口均有测试。

---

## 4. 生成器与 JSON 工具链

### 4.1 CodeGenerator（`src/Tooling/CrestCreates.CodeGenerator/`）

**RabbitMqSubscriptionSourceGenerator（重写）**：

- 按 `CrestCreates.Domain.Shared.Attributes.RabbitMqSubscribeAttribute` **完整 symbol**（MetadataName 精确匹配）识别；不按短属性名/EndsWith。
- 语义模型解析：本轮锁定支持具体、可访问、**非泛型实例** Handler 的 `Task Xxx(TEvent, CancellationToken)`（任意方法名；返回类型必须恰为 `Task`；`ValueTask` 不支持，如后续加入必须有独立生成/运行测试）。非法返回值、参数数量/类型、`ref/out`、开放泛型、不可访问类型/成员、静态方法 → **编译错误**（不静默跳过）。
- payload 参数 = 编译期闭合 Type；invoker 直接 cast 到该 Type 调方法；订阅字符串绝不拼进 C# 类型位置（生成结果必须可编译，测试断言语义）。
- 生成 Handler **scoped** 注册（与显式用户注册兼容：TryAdd 语义）；按方法完整签名建立唯一 binding（重复声明 = 生成错误 CCCRMQ003）。
- 诊断：`CCCRMQ001` 非法签名；`CCCRMQ002` 类型/成员不可访问；`CCCRMQ003` 重复 binding；`CCCRMQ004` 开放泛型/静态；`CCCRMQ005` 属性参数非法（空名、negative prefetch、超出 ushort）。诊断 ID 先查重（对照 CCCG/CCLE/CJC 空间），新增测试验证 ID、位置与生成代码可编译。
- 事件定义程序集可只贡献可发布事件/codec，不要求本程序集有 consumer。

**EventDescriptorSourceGenerator（新增）**：

- 发现 `[CrestEvent]` 声明（完整 symbol：`CrestCreates.Event.Abstractions.CrestEventAttribute`），生成每程序集 `GeneratedEventDescriptorProvider`（`IEventDescriptorProvider`）+ `CrestCreates.Generated.<Asm>.EventRegistrations.AddEventDescriptors(services)`。
- 诊断：`CCCEV001` 声明 Integration 事件但缺少 JSON 工具链（对照 CCCLE004 模式）；`CCCEV002` 非法声明（open generic / abstract / 不可访问 / Name 空）；`CCCEV003` 本程序集内重复 canonical identity。
- 生成扩展同时：`TryAddEnumerable IEventDescriptorProvider`；当声明不足（如无工具链）则同时给可读编译诊断。
- 该生成器同时服务 LocalEvent 死亡的接线缺口：Host 显式调用事件定义程序集扩展即可让 `EventRegistry` 完整。

**既有 EventRegistry bootstrap 接线补齐（S2，最小修正）**：

- `AddEventKernel` 增补三个 `IRegistryValidator<GeneratedEventDescriptor>` 的 TryAddEnumerable 注册（对象在 `CrestCreates.Metadata.Registry`，属既有契约，无新验证语义）。
- `IEventValidator`：`AddRabbitMqEventBus` 内 TryAddSingleton → `RegistryEventValidator`（依赖 `IEventResolver`+`IEventMetadataProvider`；`IEventResolver` 由事件内核 TryAdd）；缺依赖时启动期可读诊断。

### 4.2 JSON（复用 #126 工具链，不复制）

- 复用 JsonContracts BuildTasks：CoreCompile 前 roots → 官方 STJ Source Generator；不跨生成器消费输出。
- `LocalEventJsonSurfaceDiscovery` 旁**新增** `IntegrationEventJsonSurfaceDiscovery`：发现 `[CrestEvent(Scope = EventScope.Integration)]` 的闭合 class（非 abstract/generic，类似 CJC015/016/017 校验复用 `LocalEventJsonClosureValidator` 的成员闭包逻辑），产出：
  - `[JsonSerializable]` roots（写入同一 `CrestCreates.JsonContracts.g.cs`，独立 context 或并入既有生成 context——由 S2 定，原则是不为同一 Type 生成两份 STJ 元数据）；
  - `CrestCreates.Generated.EventJsonCodecContributions.AddCodecs(services)`（transport-neutral，注册 `EventJsonCodecRegistration` 服务；per-Host 生效，同 Origin 重复激活幂等、多 Origin 同类目冲突报装配失败）。
- 不要求同时标 `[LocalEvent(Replayable = true)]`；两者独立发现、共用同一 closure 校验器。
- codec 由事件所属项目产生（其导入工具链为硬要求，`CCCEV001`）；Handler-only 项目引用其静态贡献（Host 显式调用事件所属程序集扩展触发 `AddCodecs()`/`AddEventDescriptors()`）。
- 每 binding 冻结时解析出**精确 `JsonTypeInfo<T>`** 并快照；不使用 DI 里“最后一个 JsonSerializerContext”覆盖语义；发布/消费/重试均不使用无 metadata 的 Type overload，不启用 `DefaultJsonTypeInfoResolver` / 反射 fallback。
- 保留 #126 全部闭包回归（构造器可恢复性、嵌套集合/字典、枚举、JsonIgnore 条件、封闭多态、discriminator 冲突、derived collection 实例化、public 无参构造要求）。object/interface/abstract 成员仅在有明确封闭可恢复契约（封闭多态声明）时允许；自定义 converter 必须静态可用且有 round-trip 证据。
- 有效性断言必须含**实际序列化 → 反序列化 → Handler 读取值**；不得用 `GetTypeInfo` 非空或生成文本断言替代。
- 旧 `AddRabbitMqEventBus<TContext>` / context overload：作为 typed codec 显式贡献适配器短期保留，转发唯一入口并逐 binding 校验；无法静态提供契约时明确报错并给出迁移（§8），不得把单个全局 context 当第二主链。

---

## 5. 队列与 Handler 执行语义

1. 每 Host **每物理队列一个消费计划**：`(exchange, queue)` → `prefetch` + 该队列声明的 wire identity 集合 + `身份 → 有序 handler 序列`。同队列多个 Handler **顺序执行，全部成功才 Ack**。禁止每 Handler 建同队列 consumer 的竞争消费冒充 fan-out。
2. Handler 顺序按稳定标识排序：`(来源程序集简单名, Handler 元数据全名, method 名)` 序数排序；不依赖程序集加载/DI 调用先后。一个失败即停止后续；整条 delivery 重试会重放此前已成功的 handler（至少一次语义），**不提供 per-handler checkpoint**。
3. 不同队列各自获得 broker 副本；多进程订阅同一队列仍是 competing consumers（文档 + 测试区分）。
4. 每次 delivery 新建 async DI scope；**先在该 scope 内解析本次所需 handler 的全部实例（整批预解析、不做业务调用），再按序执行**——任一缺失/构造失败时零业务效果，预检与执行使用同一批实例（invoker 接收已解析实例）。scope 随后 await 释放（`IAsyncDisposable` 支持）。重投/重试新 scope；registry 不持有 scoped 服务。
5. 回调内完成 body 拷贝/反序列化；共享 channel 的发布/结算操作有明确串行所有权（§7）。此约束来自 RabbitMQ .NET client API guide。
6. 未知 identity、错误版本/Name、本队列未声明事件、坏 JSON、null payload → Handler 前拒绝、零业务调用（结算见 §6 表）。消费不从 wire headers 擅自建立可信身份/租户上下文；需要时显式复用既有上下文机制。
7. 本链提供**至少一次处理可能性**；`RequiresIdempotency` 等声明不等于框架已执行去重——不凭该字段声称幂等保证；如装配要求必须执行去重而缺实现，拒绝该能力声明。

---

## 6. 结算状态机、重试与 DLQ

业务执行结果与 broker settlement **分开**。Publisher confirm 只说明 broker 受理发布；consumer Ack 也不等于业务库与 broker 原子提交。

| 情况 | 原 delivery 处理 | 业务重试 |
| --- | --- | --- |
| 解码/身份/版本/队列成员校验失败 | `Nack(requeue:false)` → 已配置 DLX 路径 | 否 |
| Handler 全部成功 | `Ack(multiple:false)` | 否 |
| Handler 失败且预算有余 | 可取消延迟后经**独立 publisher channel** 重投原队列；**确认成功后 Ack 原消息** | 是 |
| Handler 失败且预算耗尽 | `Nack(requeue:false)` → DLQ | 否 |
| 重投 unroutable/nack/确认超时/取消/断连 | 不 Ack 原消息、不伪造重投成功；consumer channel 健康时：有界退避（≥1s，避免忙循环）后 `Nack(requeue:true)` 受控恢复（不递增重试计数、不消耗业务预算）；channel 失效时交未确认 delivery 恢复语义 | 不消耗业务预算 |
| Handler 成功但 Ack 失败 | 记录“业务成功、结算未确认”，恢复连接/channel；可能重投 | 不进入业务失败 catch 再主动复制 |
| Host 停机取消 | 停止接收、协调在途；未完成不 Ack | 不消耗业务失败预算 |

细则（锁定）：

- `RetryCount = N` = 初次尝试外最多 N 次业务重投；0 = 失败即终止。计数为消息持久 headers 明确字段（保留 `x-retry-count` 键），复制时递增一次。
- `RetryDelaySeconds` 实际生效：可取消的进程内等待、原 delivery 保持未 Ack；名义等待，不是 durable scheduler、不承诺严格时限；关闭期取消不算业务失败。
- 解析既有 retry header 兼容明确列出的 AMQP 值形态（`byte[]` UTF-8 数字、`int`、`long`）；负数、溢出、乱码、非法类型 → 确定性隔离/拒绝（`Nack(false)` → DLQ，含诊断），**禁止**重置为 0 造成无界循环。
- 重投经独立 publisher channel（不与 consumer channel 并发发布），目标原 queue：保留既有“默认 exchange 指定队列名重投”语义（`exchange: ""`，routing key = 队列名），`mandatory: true`，等待 SDK confirm。
- 属性保留：body、`MessageId`、`CorrelationId`、`ContentType`、`Type`、必要持久属性与应用 headers；只修改框架拥有的 retry 字段。SDK 自带的 publish tracking header 不复制进新序列号。
- 正常发布与重投共享同一确认/属性复制核心；重投不得重生成 `MessageId`、不得丢 correlation/canonical identity。
- 确认成功与 Ack 之间、业务成功与 Ack 之间存在 crash 窗口 → 可能重复；断线/确认超时属结果不确定，不声称 definitely-not-sent。
- 终态处理后不再抛无处理的回调异常触发额外重试；保留原 Handler 异常，结算失败作为独立诊断。
- 保留现有 direct DLX + `{queue}.dlq` 拓扑；普通 DLX 转发不自动具备无损保证；quorum at-least-once dead-lettering 为另一配置能力，本任务不默认承诺。
- 验收含：有效事件耗尽预算进入 DLQ 后，经 broker 重新投回主队列并**经正式 consumer** 成功消费；fixture 显式重置业务重试预算、保留消息身份、确认重投后才 Ack DLQ 原消息；本轮不新增公开运维重放 API。

---

## 7. Publisher、channel 所有权与停机

- 泛型/非泛型发布共享同一 binding 查找与 typed 序列化；非泛型 `IDomainEvent` 入口按实际具体 Type（`GetType()`）查静态索引，不按合约擦除 payload。未声明、缺 codec、身份不符 → broker I/O 前失败。
- 锁定 RabbitMQ.Client 7.2.0；确认行为以实验 A 实测为准；**移除平行自制确认账本**，await `BasicPublishAsync` 即参与确认（tracking 双开）。
- confirm timeout：组合操作 CT 与配置时限（linked CTS）；区分调用方取消、不可路由（`PublishReturnException`）、broker nack（`PublishNackException`）、超时（`OperationCanceledException` 且非调用方取消）、连接中断；不统一转成虚假成功或确定未发送。
- publisher channel 以 lease 管理：获取失败/取消、Return、Discard/异常 Dispose 都**精确释放一次**许可；不得直接 Dispose 后漏释放 semaphore（F5 修复）。生命周期与 DI（Transient vs Singleton，F7）统一到唯一入口。
- consumer 使用专属长生命周期 channel，不占用 publisher 可借用池；文档明确 `MaxChannels` 只约束 publisher 池、专属消费 channel 独立计算。`MaxChannels=1` 时必须能完成“正式消费 → 失败重投”（不自锁）。
- 同一 publisher channel 同时只有一个发布所有者、需要复用时串行归还；共享消费 channel 的 Ack/Nack/Cancel 有明确串行策略（单一 settlement 门 + SDK `ConsumerDispatchConcurrency` 显式配置，不依赖默认值）。
- 连接恢复沿用 SDK recovery 主链（`AutomaticRecoveryEnabled`），不另建竞争恢复循环；**恢复窗口内连接所有权归 SDK**：pool 对非 open 的既有连接执行有界等待（20s，可取消）而不得 Dispose/替换（替换会连带作废承载 consumer 的专属 channel）；专属 consumer channel 终态丢失（channel 关闭且连接已恢复/连接长期不可恢复超过 30s 宽限）→ 抛错停止宿主，不保持虚假 ready。真实断开测试必须先确认目标连接在 management 中存在且确已关闭，再在恢复窗口内发布验证 consumer 存活（不得仅遍历一次 connections 就认定断连）。
- 停机顺序：停止 intake/`BasicCancel` → 有界等待在途回调 → 关闭专属消费 channel → 释放 connection/lease；未完成消息保持未确认；禁止先把活跃 consumer channel 放回发布池再等 BackgroundService 停止。

---

## 8. 唯一装配入口与兼容迁移

`AddRabbitMqEventBus(options, builder?)` 收口：options、pool/lease、publisher、registry、验证与 consumer 注册；`RabbitMqEventBusModule` 与旧 overload 只转发同一入口（消除 F7 的三套生命周期/JSON 路径）。builder 配置累积（镜像 `LocalEventDispatchConfiguration` 模式，多次调用合并）。

配置校验（注册期即抛，禁止静默截断）：HostName/Port/VirtualHost 非空有效、`MaxChannels > 0`、`RetryCount >= 0`、`RetryDelaySeconds >= 0`、`PublisherConfirmTimeoutSeconds > 0`、prefetch ∈ [1, 65535]、exchange/queue/routing key 非法值、同一队列声明冲突。

迁移表（交付文档必须包含）：

| 旧行为/配置 | 新主链 | 迁移动作 |
| --- | --- | --- |
| short EventType / CLR 类型名字符串（信封 + attributes） | canonical envelope（Id/Name/Version）+ 静态 alias | 列出真实旧身份 → `AddLegacyWireAlias(alias, canonicalName, version)` |
| lower_underscore 或自定义 routing | 独立 `RoutingKey`（默认 canonical Name） | 显式保留原 binding（属性参数/builder），不自动改名 |
| 多个 context / 模块重复装配 | per-binding codec + 唯一入口 | 事件侧静态贡献 + 冻结校验；builder 累积 |
| 同队列多个订阅竞争 | 队列计划内顺序 fan-out | 明确行为变化；需要并发时拆不同队列 |
| 反射失败返回空订阅 | Required 清单 / PublisherOnly | 缺失即失败；零订阅显式声明 |
| runtime Subscribe/Unsubscribe | 仍为编译期订阅 | 不重新开放动态扫描 |
| requeue:true 即时重投（旧 Spec 描述） | 延迟重投 + 独立 publisher channel + confirm 后 Ack | 行为差异写入迁移文档 |
| 旧单 context `AddRabbitMqEventBus<TContext>` | typed codec 显式贡献适配器 | 短期过渡，标注退出条件（工具链就绪后删除） |

归档：旧反射桥（`RabbitMqConsumer` 反射段）、失效生成表（`RabbitMqSubscriptionRegistry` writer 旧形态）、过时测试（手工 consumer 集成测试、旧 ctor 单元测试）与旧 Spec 失真段 → `99_RecycleBin/issue-127-rabbitmq-static-bindings/`，保留退出说明，不直接删除。

---

## 9. 验证矩阵（必须证明正式链）

Testcontainers 或 podman socket（`DOCKER_HOST=unix:///run/user/1000/podman/podman.sock`），每套测试隔离 vhost/队列前缀；Host 使用正式 `AddRabbitMqEventBus` + 生成贡献 + 真实 publisher/consumer；至少两个程序集提供同短名不同 namespace 的事件/Handler。

| 层次 | 必须覆盖 |
| --- | --- |
| 生成器/编译 | 完整 Attribute symbol；合法闭合调用；非法签名/不可访问/开放泛型；publisher-only；跨程序集实际编译；生成器关闭 |
| 冻结 | 正反注册顺序；重复启用幂等；缺 required contributor/Handler/codec；冲突身份/alias/Type；队列参数冲突；失败先于 broker I/O |
| JSON | 真实 round-trip、嵌套数据值；多 context；接口入口具体类型；构造器/集合/多态边界；反射 JSON 关闭 |
| 正常消费 | 同短名不串；同队列多 Handler 各调用一次；不同队列各副本；async scope 与释放；明确 routing key |
| 拒绝 | 坏 envelope/JSON/null；未知 Id/版本；Id 与 Name 不符；当前队列未绑定；零业务调用，实际 DLQ 观察 |
| 重试/DLQ | RetryCount=0/1；次数与 delay；properties/MessageId 保留；失败后成功；耗尽进 DLQ；broker 重投再成功 |
| 结算故障 | unroutable/nack/超时/断连无提前 Ack；Ack 失败不走业务复制；重复窗口保持可解释 |
| 生命周期 | MaxChannels=1 重投无死锁；获取/取消/失败后许可可复用；停机在途；真实断连恢复消费，不以再次 GetChannel 代替 |
| 回归 | Event kernel、LocalEvent #126、CodeGenerator、JsonContracts、依赖边界；受影响的 CAP/RabbitMQ 既有场景 |

- 断言绑定 `MessageId`/`CorrelationId` 与 Handler 实际效果；有限超时观察最终状态，不得仅 sleep 后数日志。
- 确认原消息保留/重投以 broker 队列状态或可控断连实验为准，不能只断言 mock 调用顺序。
- broker 不可用必须使显式集成验证失败或明确报告“未执行”；不允许静默 Skip 后写通过。

---

## 10. NativeAOT 与支持声明

- S4 正式 fixture：Linux x64 原生 Host（`-p:CrestCreatesPublishMode=aot` + 真实 native link + 关闭反射 JSON），凭据走环境配置、日志不输出秘密；broker 由 harness 准备（显式 readiness、有限整体超时、失败日志、进程回收）。
- 原生进程调用正式 publisher + 正式 consumer；断言 typed payload 与 Handler 副作用；跨程序集、同短名、坏消息/未知 identity、重试/DLQ 路径均有断言；退出码与 sentinel 绑定这些结果。
- trim 与 native 分别留证（`-p:CrestCreatesPublishMode` 缺省 trim + 显式 aot）。
- 风险已前置：实验 B 证明锁定组合原生编译/运行无 blocker；若 fixture 阶段发现新的 native 阻塞，记录调用链/诊断/RID/复现命令/commit，**不得** suppress warning 或切 fallback 后标 `NativeAOT-verified`；该项保持未验证/受阻，按真实 JIT 或实际 trim publish/run 证据声明。
- 更新 #123 evidence matrix 与 `memory.md`；能力声明仅覆盖本 fixture 的 RabbitMQ 组合（linux-x64、单进程、direct exchange + DLX 拓扑），不从 CAP 集成测试或 #126 fixture 推导。
- 当前 GitHub workflows 手动禁用（2026-10-12 起）。提交可重复运行的本地门禁与未来 CI 接线说明，**不擅自恢复 CI**。

---

## 11. 实施切片

**S1 — 锁定契约和编译/SDK 实验**：本 Spec + 计划 + 迁移清单 + 实验 A/B 实测记录（已完成，见 §1.2）。 ✅

**S2 — 静态生成、codec 和唯一装配**：

- 事件侧：EventDescriptorSourceGenerator（`[CrestEvent]` → provider + `AddEventDescriptors`）；`IntegrationEventJsonSurfaceDiscovery` + per-Host codec contribution（`EventJsonCodecRegistration`）；CCCEV001–003。
- 订阅侧：`RabbitMqSubscriptionSourceGenerator` 重写 + 新属性参数（Version/RoutingKey）+ CCCRMQ001–005；contributor/binding/registry/freeze/required 诊断/唯一入口骨架；`RabbitMqCompositionHostedService` + readiness。
- `AddEventKernel` 接线补齐（validators、IEventResolver、IEventValidator TryAdd）。
- 负例（编译期/装配期）、多程序集、真实 JSON round-trip 通过；旧入口适配同一主链；consumer 反射 fallback 全部移除（本切片先保证发布链与冻结链可测）。

**S3 — 发布消费与结算闭环**：

- 具体 Type 发布（泛型/非泛型同索引）、envelope 新格式 + alias 接收解析、队列消费计划、async scope、校验后执行。
- 确认核心（SDK tracking、lease 精确释放）、retry/DLQ 状态机、属性保留、取消与 Ack 异常分离、`x-retry-count` 宽容解析。
- 专属 consumer channel、settlement 串行门、停机顺序、SDK recovery 恢复；
- 真实 broker 集成证据：正常/负例/重试/DLQ/结算边界/生命周期全矩阵（§9），手工 consumer 旧测试迁移或归档。

**S4 — 证据、迁移与收口**：

- 正式 native publish/link/run fixture（`tests/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ.AotHost` + `.Tests`，镜像 #126 形态 + harness 侧 broker 准备）；不支持项如实记录。
- #126/工具链/边界及受影响集成回归，本地可复现命令与结果记录到 issues log。
- 归档旧反射链及失真测试/旧 Spec 失真段；更新 `memory.md`、AGENTS.md 支持声明与 #123 matrix。
- PR 描述列出最终行为、迁移影响、证据与限制，关联 #127；等待审查，不自动合并。

建议交付路径：

- 本 Spec：`docs/superpowers/specs/2026-10-13-issue-127-rabbitmq-static-bindings-design.md`
- 实施计划：`docs/superpowers/plans/2026-10-13-issue-127-rabbitmq-static-bindings.md`
- 问题日志：`docs/superpowers/plans/2026-10-13-issue-127-issues-log.md`
- 代码：现有 RabbitMQ Runtime、CodeGenerator、JsonContracts 与对应镜像测试目录；native fixture 沿仓库既有命名约定。

---

## 12. 完成标准

- [ ] 正式 publisher/consumer 不再出现 `Type.GetType`、AppDomain/程序集扫描、`GetMethod/Invoke`、动态泛型调用或动态 JSON fallback。
- [ ] 同一份 canonical 事件定义驱动 wire 编解码与订阅；跨程序集、同短名与注册顺序均正确。
- [ ] 缺必需装配确定性失败；零订阅行为显式；主链没有模块/扩展、泛型/非泛型、首次/重试双轨。
- [ ] 真实 broker 已验证 Handler 效果、Ack/Nack、重试/DLQ、失败与停机边界；现有测试不再绕过 consumer 支撑结论。
- [ ] NativeAOT/trim/JIT 声明与实际证据逐项一致，未验证项保留明确限制。
- [ ] 迁移文档、归档、`memory.md` 与 #123 matrix 同步；不重新打开 #126/CAP/Outbox 已收口的职责边界。
