# CrestCreates Agent Runtime 架构设计

## 1. 背景与目标

CrestCreates 的框架主链已经基本完成，下一阶段的目标不是再增加一个“AI 功能模块”，而是在现有平台能力之上，建立一套可进入真实业务系统的 Agent Runtime。

该 Runtime 的目标是让 LLM Agents 能在 CrestCreates 的 .NET 业务系统内以如下方式运行：

- 安全：能力边界明确，默认最小权限
- 可控：运行策略、模型、工具、预算、并发、租户隔离都可配置
- 可观测：对请求、推理、工具调用、状态迁移、失败原因有统一观测面
- 可审计：对谁触发、做了什么、调用了什么工具、读写了什么业务能力有完整审计
- 可平台化：不是 sample 级 AI 接入，而是可复用的框架能力
- 可适配：可以使用 C# Agent Framework 作为模型编排/Agent 能力适配层，但不让其成为 CrestCreates 的领域真相来源

同时必须遵守 CrestCreates 当前架构共识：

- 主链优先编译期生成，避免 runtime reflection
- 主链优先 AoT 友好
- 不引入第二套租户、认证、配置、审计真相来源
- 不维护“双轨 Agent Runtime”

---

## 2. 设计原则

### 2.1 唯一主链

Agent Runtime 必须有唯一正式主链：

1. Agent 定义通过编译期生成注册信息
2. Agent 执行通过统一 Runtime Pipeline 驱动
3. Tool 暴露通过强类型描述符注册
4. 对外访问通过 Generated Endpoint 或现有 Application Service 暴露

不接受以下长期并存方案：

- 运行时扫描 Agent / Tool 类型作为正式主路径
- 一套 HTTP Agent API，一套内部私有执行 API，行为不一致
- 一套 AI 配置走 Setting，一套临时配置走自定义表
- 一套普通业务审计，一套 Agent 独立审计模型

### 2.2 安全优先于“智能”

Agent Runtime 首先是业务系统运行时，不是聊天 Playground。

优先级顺序：

1. 身份与租户上下文正确
2. 权限、预算、工具边界正确
3. 执行状态、失败恢复、审计可追踪
4. 模型能力与复杂编排

### 2.3 强类型优先于字符串拼装

以下内容不应散落为自由字符串约定：

- Agent Id / Version / Capability
- Tool 名称、参数、返回值
- Memory 分类
- Policy 名称
- 审计事件类型
- 预算与限流规则

应统一抽象为 Definition、Descriptor、Policy、Contributor、Context。

### 2.4 与平台能力收口

Agent Runtime 不单独发明以下基础设施，必须复用现有主链：

- 多租户：`ICurrentTenant.Id`
- 认证授权：`CurrentUser`、claims、permission checker
- 配置管理：Setting Definition / Provider / Manager
- 审计：Audit Context / Middleware / AuditLogService
- 日志：Serilog 主链
- 健康检查：HealthCheck 模块
- Dynamic API：Generated Endpoint 主链

---

## 3. 非目标

本设计当前不追求：

- 通用 AutoGPT 式开放环境自治
- 直接支持任意第三方 Agent SDK 作为核心执行主链
- 将 C# Agent Framework 的类型、状态、权限模型直接泄漏为 CrestCreates Agent Runtime 的核心模型
- 复杂图数据库记忆系统先行
- 脱离业务权限模型的全局 AI 助手
- 多套 Prompt DSL 并存

第一阶段应先把企业业务系统真正需要的闭环做出来：受控执行、工具编排、租户隔离、审计留痕、运维可观测。

---

## 4. 总体架构

### 4.1 分层建议

建议新增如下模块簇：

- `CrestCreates.AgentRuntime.Abstractions`
- `CrestCreates.AgentRuntime.Domain.Shared`
- `CrestCreates.AgentRuntime.Domain`
- `CrestCreates.AgentRuntime.Application.Contracts`
- `CrestCreates.AgentRuntime.Application`
- `CrestCreates.AgentRuntime.Infrastructure`
- `CrestCreates.AgentRuntime.DynamicApi`
- `CrestCreates.AgentRuntime.CodeGenerator`
- `CrestCreates.AgentRuntime.TestBase`

职责划分：

- `Abstractions`
  - Agent 执行接口
  - Tool 描述接口
  - Model Provider 抽象
  - Memory 抽象
  - Policy 抽象
- `Domain.Shared`
  - 常量、枚举、共享 DTO、错误码
- `Domain`
  - Agent Definition、Execution、Session、Tool Grant、Policy、Memory Entry 等核心模型
  - Setting Definition Provider
  - 领域规则与状态机
- `Application.Contracts`
  - 对外应用服务契约
  - Agent 执行请求/响应 DTO
  - 管理端 DTO
- `Application`
  - Agent 调度编排
  - 授权、预算、审计、观测接入
  - Tool 调用编排
- `Infrastructure`
  - 模型适配器
  - 向量/缓存/消息队列/持久化实现
  - OpenTelemetry/日志桥接
- `DynamicApi`
  - Agent Runtime 对外 Generated API 接入
- `CodeGenerator`
  - Agent / Tool compile-time registry 生成

### 4.2 Runtime 主链

正式执行主链如下：

1. 用户或系统任务发起 Agent Request
2. 进入 `IAgentExecutionAppService`
3. 构建 `AgentExecutionContext`
4. 解析当前 `TenantId`、`UserId`、`CorrelationId`
5. 从 Generated Registry 解析 Agent Definition
6. 加载 Agent Policy / Tool Grant / Runtime Settings
7. 创建 `AgentExecution`
8. 进入统一 `AgentRuntimePipeline`
9. Pipeline 内部驱动模型调用、工具调用、状态持久化、审计、日志、指标
10. 输出 `AgentExecutionResult`
11. 将执行轨迹与产物归档到审计与运行记录

这里的关键点是：Agent Runtime 自己不是一坨自由调用代码，而是一个和 HTTP 请求类似的统一运行管线。

### 4.3 C# Agent Framework 的定位

C# Agent Framework 应作为 `Infrastructure` 层的适配对象，而不是 Agent Runtime 的核心领域模型。

建议边界：

- CrestCreates 定义 `AgentDefinition`、`ToolDefinition`、`AgentExecution`、权限、审计、租户、Setting
- C# Agent Framework 负责模型交互、部分 Agent 编排能力、消息格式适配、tool-call 协议适配
- Runtime Pipeline 在调用 C# Agent Framework 前完成租户、权限、预算、模型 profile、prompt guard 检查
- Runtime Pipeline 在 C# Agent Framework 返回 tool-call 后再次做 Tool Authorization，不信任模型输出作为授权依据
- C# Agent Framework 的会话/线程/消息 Id 可以作为外部关联字段保存，但不能替代 `AgentExecution.Id`、`TenantId`、`AgentSession.Id`

这样可以复用 C# Agent Framework 的生态能力，同时保留 CrestCreates 对业务上下文、安全边界、审计模型的主导权。

---

## 5. 核心领域模型

### 5.1 AgentDefinition

表示一个可运行 Agent 的静态定义，不是数据库拼装对象。

建议字段：

- `AgentName`
- `DisplayName`
- `Version`
- `DefinitionHash`
- `Description`
- `ExecutionMode`：Sync / Async / LongRunning
- `AllowedTools`
- `DefaultModelProfile`
- `MemoryPolicy`
- `ApprovalPolicy`
- `BudgetPolicy`
- `PermissionRequirements`
- `Tags`

来源应优先是编译期生成注册，而不是运行时全局扫描。

### 5.2 AgentExecution

表示一次真实运行实例。

建议字段：

- `Id`
- `TenantId`
- `AgentName`
- `AgentVersion`
- `AgentDefinitionHash`
- `TriggerSource`
- `Status`
- `StartedAt`
- `CompletedAt`
- `InitiatorUserId`
- `ConversationId`
- `InputSnapshot`
- `OutputSnapshot`
- `SettingsSnapshotHash`
- `ToolGrantSnapshotHash`
- `FailureCode`
- `FailureMessage`
- `TokenUsage`
- `CostUsage`

状态建议：

- `Pending`
- `Running`
- `WaitingForToolApproval`
- `WaitingForExternalEvent`
- `Completed`
- `Failed`
- `Cancelled`
- `TimedOut`

### 5.3 AgentExecutionStep

用于表达一次执行中的离散步骤，便于审计和观测。

建议类型：

- `PromptPrepared`
- `ModelInvoked`
- `ModelResponded`
- `ToolCallPlanned`
- `ToolCallAuthorized`
- `ToolCallRejected`
- `ToolCallStarted`
- `ToolCallCompleted`
- `MemoryLoaded`
- `MemoryWritten`
- `HumanApprovalRequested`
- `HumanApprovalResolved`
- `ExecutionCompleted`
- `ExecutionFailed`

### 5.4 ToolDefinition

Tool 是 Runtime 内可授权、可审计、可限流的最小执行单元。

Tool 不应直接等价于任意 C# 方法。建议通过强类型描述符定义：

- `ToolName`
- `DisplayName`
- `Description`
- `InputType`
- `OutputType`
- `PermissionRequirements`
- `Scope`：Tenant / System / User
- `RiskLevel`：Low / Medium / High / Critical
- `RequiresApproval`
- `Timeout`
- `IdempotencyMode`
- `TransactionMode`
- `CompensationMode`
- `AuditMode`

### 5.5 AgentSession

用于承载业务侧持续交互上下文，不等价于 HTTP Session。

建议字段：

- `Id`
- `TenantId`
- `AgentName`
- `SubjectType`
- `SubjectId`
- `SessionState`
- `SummarySnapshot`
- `LastExecutionId`
- `ExpiresAt`

### 5.6 AgentMemoryEntry

记忆必须分级，而不是做成统一向量仓。

建议先区分：

- `ExecutionMemory`：单次执行上下文
- `SessionMemory`：会话级摘要
- `DomainMemory`：业务对象相关记忆
- `KnowledgeMemory`：知识/文档检索结果

并显式记录：

- `TenantId`
- `VisibilityScope`
- `SensitivityLevel`
- `RetentionPolicy`
- `SourceType`

---

## 6. 执行架构

### 6.1 Pipeline 结构

建议 Runtime Pipeline 采用与 ASP.NET 中间件类似的责任链：

1. `TenantResolutionStep`
2. `PrincipalBindingStep`
3. `AgentDefinitionResolutionStep`
4. `RuntimeSettingsResolutionStep`
5. `PermissionValidationStep`
6. `BudgetValidationStep`
7. `ModelSelectionStep`
8. `MemoryLoadStep`
9. `PromptAssemblyStep`
10. `ModelInvocationStep`
11. `ToolPlanningStep`
12. `ToolAuthorizationStep`
13. `ToolInvocationStep`
14. `StatePersistenceStep`
15. `ApprovalCheckpointStep`
16. `AuditEmissionStep`
17. `MetricsEmissionStep`

每一步都必须：

- 接受统一 `AgentExecutionContext`
- 返回强类型结果
- 可记录 step-level telemetry
- 可中止 pipeline

### 6.2 Execution Context

`AgentExecutionContext` 建议统一承载：

- `TenantId`
- `CurrentUser`
- `CorrelationId`
- `AgentDefinition`
- `Execution`
- `SettingsSnapshot`
- `ToolCatalog`
- `BudgetState`
- `MemoryContext`
- `ApprovalContext`
- `RiskContext`
- `CancellationToken`

避免各层重新从 `IServiceProvider` 随机抓上下文。

### 6.3 同步与异步执行

建议执行模式分三类：

- 同步执行：用于短时问答、轻量编排
- 异步执行：用于需排队的业务任务
- 长时执行：用于需要外部事件或人工批准的流程

三者使用同一执行模型，不要拆成三套独立实现。

差异只体现在：

- 调度器
- 超时策略
- 状态持久化频率
- 对外回调方式

### 6.4 调度器

建议引入 `IAgentExecutionScheduler`，但调度器不是第二运行时，只负责：

- 排队
- 并发控制
- 重试
- 恢复
- 超时取消

真正业务执行仍由统一 Pipeline 完成。

---

## 7. 安全模型

### 7.1 身份与租户

Agent Runtime 必须严格绑定现有身份主链：

- 用户触发执行时，沿用当前 `CurrentUser`
- 系统触发执行时，使用明确的 `SystemPrincipal`
- 租户上下文统一取 `ICurrentTenant.Id`

禁止：

- 用 `TenantName` 作为运行上下文主键
- Agent 内部自己拼装 claims
- Tool 调用绕过现有权限检查
- 后台恢复执行时丢失原始触发主体和租户上下文

### 7.2 权限模型

建议引入三层权限：

1. `Agent.Execute.{AgentName}`
2. `Agent.Manage.{AgentName}`
3. `Agent.Tool.{ToolName}`

并支持策略叠加：

- 用户是否可运行该 Agent
- 该 Agent 是否可调用该 Tool
- 当前租户是否启用该 Tool
- 当前执行场景是否需要人工批准

即便模型“决定”调用某工具，也必须再经过运行时授权，不允许 LLM 结果直接越权落地。

### 7.3 Tool 风险分级

建议按风险分层：

- `Low`：纯查询、无副作用
- `Medium`：受控读写、幂等操作
- `High`：写业务数据、调用外部系统
- `Critical`：资金、权限、删除、跨租户敏感操作

高风险 Tool 应支持：

- 强制人工批准
- 双人审批
- 明确审计标签
- 默认关闭

### 7.4 Prompt 与数据泄露防护

需要把 Prompt 安全当成平台能力，而不是业务自己处理。

建议内建：

- Prompt 模板参数白名单
- 敏感字段脱敏器
- Tool 输出 redactor
- Prompt 注入检测 hook
- 对外模型响应内容分类

这部分要与现有 Audit Redaction 能力对齐，避免日志里留原文敏感数据。

### 7.5 预算与限流

预算控制必须是正式链路，不是运营备注。

建议提供：

- 每次执行最大 token
- 每次执行最大 cost
- 每租户并发上限
- 每 Agent QPS / queue depth 限制
- 每 Tool 调用次数上限
- 失败熔断

### 7.6 威胁模型

Agent Runtime 的安全模型必须显式覆盖以下威胁，而不是只做普通接口鉴权：

- Prompt Injection：用户输入或检索内容诱导 Agent 泄露上下文、绕过规则或调用高风险 Tool
- Tool Output Injection：外部系统返回内容诱导下一轮模型调用执行未授权动作
- Confused Deputy：Agent 以高权限用户身份替低权限请求方完成越权操作
- Tenant Data Leak：Memory、检索结果、Tool 输出或日志跨租户泄露
- Approval Bypass：模型通过参数伪造、重复调用、异步恢复绕过人工审批
- Replay / Duplicate Execution：重试、消息重复投递导致业务写操作重复执行
- Secret Exfiltration：Prompt、日志、Tool payload、模型上下文泄露 API Key、Token、连接串或业务敏感字段

这些威胁应落到 Pipeline 中的 guard、redactor、approval、idempotency、audit 机制里，而不是只停留在文档约束。

### 7.7 运行时开关

必须提供运行时级 kill switch：

- 全局关闭 Agent Runtime
- 租户级关闭 Agent Runtime
- 单个 Agent 禁用
- 单个 Tool 禁用
- 单个模型供应商禁用
- 高风险 Tool 强制进入审批模式

这些开关必须走 Setting 主链，并在 Pipeline 早期生效。

---

## 8. 配置模型

### 8.1 统一走 Setting Management

Agent Runtime 的可变配置必须走 Setting 主链。

建议定义：

- `AgentRuntime.Enabled`
- `AgentRuntime.DefaultProvider`
- `AgentRuntime.DefaultModel`
- `AgentRuntime.MaxTokensPerExecution`
- `AgentRuntime.MaxCostPerExecution`
- `AgentRuntime.MaxConcurrentExecutions`
- `AgentRuntime.ToolApprovalEnabled`
- `AgentRuntime.AuditCapturePrompt`
- `AgentRuntime.AuditCaptureToolPayload`
- `AgentRuntime.MemoryRetentionDays`
- `AgentRuntime.KillSwitch.Enabled`
- `AgentRuntime.Provider.{ProviderName}.Enabled`
- `AgentRuntime.Tool.{ToolName}.Enabled`

并支持作用域：

- Global
- Tenant
- User

### 8.2 Definition 与 Profile

静态定义与可变策略分离：

- `AgentDefinition`：代码定义，编译期注册
- `AgentProfile`：租户/环境策略覆盖

这样既能保持主链强类型，又允许业务侧做有限配置，不会把 Agent 定义整个下放成数据库拼装系统。

### 8.3 密钥与供应商配置

模型供应商 API Key、Endpoint、部署名等配置必须区分：

- 非敏感 profile 配置：可按 Setting 读取和审计
- 敏感密钥：必须走加密 Setting 或后续统一 Secret Provider

禁止把密钥写入 AgentDefinition、Prompt Template、Tool payload、审计原文或普通日志。

---

## 9. Tool 接入模型

### 9.1 Tool 不是反射调用器

不建议把任意 Application Service 方法都自动暴露成 Tool。

正式主链应是：

1. 开发者声明 Tool Definition
2. 编译期生成 Tool Registry
3. Runtime 按 Descriptor 定位执行器
4. 参数绑定、权限检查、审计、超时控制由统一运行时处理

### 9.2 Tool 类型建议

建议先支持四类 Tool：

- `ApplicationTool`
  - 调用本系统 Application Service
- `QueryTool`
  - 只读查询
- `IntegrationTool`
  - 调用外部 HTTP / MQ / SaaS
- `WorkflowTool`
  - 发起平台内部任务或审批流程

### 9.3 Tool Registry 生成

建议新增 Agent Runtime Source Generator：

- 收集 `[AgentTool]` 或等价定义
- 生成 `IAgentToolRegistryProvider`
- 生成参数绑定代码
- 生成元数据描述

避免：

- 启动时扫描程序集
- 运行时反射推导参数
- 在 AoT 场景下注定脆弱的动态绑定

### 9.4 Tool 事务、幂等与补偿

Tool 调用必须显式声明副作用语义：

- `ReadOnly`：只读，无业务写入
- `IdempotentWrite`：可通过业务幂等键安全重试
- `NonIdempotentWrite`：默认不自动重试，通常需要审批
- `ExternalSideEffect`：调用外部系统，必须记录外部关联 Id

对于写操作 Tool，应支持：

- `IdempotencyKey`
- Unit of Work 边界
- 超时后的最终状态确认
- 失败补偿策略
- 审计中的 before/after 摘要或业务引用

这部分不能交给 LLM 自行处理，必须是 Runtime 的执行约束。

---

## 10. 模型接入架构

### 10.1 Provider 抽象

模型接入应有统一抽象：

- `IModelProvider`
- `IChatCompletionInvoker`
- `IEmbeddingProvider`
- `IResponseStreamInvoker`

Runtime 依赖抽象，不直接依赖具体厂商 SDK 作为核心执行主链。

### 10.2 Model Profile

建议引入 `ModelProfile`，统一描述：

- Provider
- Model
- Temperature
- MaxTokens
- ToolCallMode
- ReasoningMode
- Timeout
- RetryPolicy

业务和 Agent 绑定 Profile，而不是到处散落模型参数。

### 10.3 供应商隔离

外部模型供应商实现放在 `Infrastructure`，例如：

- OpenAI
- Azure OpenAI
- 本地推理网关

这些都是实现，不定义 Agent Runtime 的核心抽象。

---

## 11. 观测与审计

### 11.1 统一审计模型

Agent Runtime 必须复用现有 Audit 主链，而不是另造“AI 日志表”作为主要依据。

建议将以下事件纳入统一审计：

- Agent 执行开始/结束
- Prompt 摘要
- 模型调用元数据
- Tool 调用申请/批准/执行/失败
- Memory 读取/写入
- 人工审批动作

审计中保存的是可控快照，不是无边界原文。

### 11.2 结构化日志

日志字段建议统一：

- `TenantId`
- `UserId`
- `AgentName`
- `ExecutionId`
- `SessionId`
- `ToolName`
- `ModelProvider`
- `ModelName`
- `StepType`
- `LatencyMs`
- `TokenUsage`
- `CostUsage`
- `Outcome`

### 11.3 指标

建议至少暴露以下指标：

- 执行总数
- 成功率
- 失败率
- 平均时延
- Token 用量
- Cost 用量
- Tool 调用成功率
- 人工审批等待时间
- Queue Depth
- Tenant 级并发占用

### 11.4 Trace

建议每次执行形成统一 Trace：

- Root Span：Agent Execution
- Child Span：Prompt Assembly
- Child Span：Model Invocation
- Child Span：Tool Invocation
- Child Span：Memory Read/Write

这样可以把业务请求链和 Agent 执行链串起来。

---

## 12. 持久化设计

### 12.1 需要持久化的对象

建议第一阶段持久化：

- `AgentExecution`
- `AgentExecutionStep`
- `AgentSession`
- `ToolApprovalRequest`
- `AgentMemoryEntry`（先支持摘要和引用）

### 12.2 持久化原则

- 执行记录是审计与运维资产，不是临时缓存
- Prompt/Response 原文默认不全量持久化
- 敏感内容按 Setting + Policy 决定采样和脱敏
- Memory 优先存摘要、引用、标签，不先上重型知识库依赖

### 12.3 异步恢复与 Outbox

异步和长时执行必须有可恢复模型。

建议：

- AgentExecution 状态变更先持久化，再触发后台执行
- Tool 调用外部系统时记录 outbox/inbox 或等价可靠消息
- 后台恢复时重新绑定 `TenantId`、原始触发主体、AgentDefinitionHash、SettingsSnapshotHash
- 重试必须经过预算、幂等、审批状态检查
- 等待人工审批或外部事件时，执行状态必须可查询、可取消、可超时

不要把长时执行建立在内存任务或不可恢复的进程内状态上。

---

## 13. 对外 API 设计

### 13.1 正式入口

对外主链建议以 Application Service + Generated Dynamic API 暴露：

- `POST /api/agent-runtime/agents/{agentName}/execute`
- `POST /api/agent-runtime/agents/{agentName}/schedule`
- `GET /api/agent-runtime/executions/{executionId}`
- `POST /api/agent-runtime/executions/{executionId}/cancel`
- `POST /api/agent-runtime/tool-approvals/{approvalId}/approve`
- `POST /api/agent-runtime/tool-approvals/{approvalId}/reject`

不要单独维护一套绕开应用层的 AI Controller 主链。

### 13.2 管理面 API

建议管理面只暴露平台需要的正式能力：

- 查询 Agent 定义
- 查询租户可用 Agent / Tool
- 查询执行记录
- 查询成本与用量
- 配置 Profile / Policy
- 处理审批

---

## 14. 编译期生成设计

这是 Agent Runtime 是否符合 CrestCreates 主链的关键。

### 14.1 生成目标

建议生成以下产物：

- Agent Definition Registry
- Tool Definition Registry
- Tool 参数绑定器
- Agent Dynamic API Endpoint 映射
- Agent 权限元数据

### 14.2 输入源

输入应来自明确声明的 Agent / Tool 类型与属性，而不是运行时扫描所有 Application Service。

### 14.3 生成收益

收益包括：

- 降低反射使用
- 明确主链注册面
- 提高 AoT 兼容性
- 提高启动性能
- 让测试与正式执行路径一致

### 14.4 Definition Versioning

Agent 和 Tool 的编译期定义必须可版本化。

建议：

- 每个 AgentDefinition 生成稳定 `DefinitionHash`
- 每次执行保存 `AgentVersion` 与 `AgentDefinitionHash`
- ToolDefinition 也保存 `DefinitionHash`
- 审计记录保存执行时的定义快照引用
- 不允许新版本定义覆盖历史执行的解释依据

这样可以保证半年后追溯一次 Agent 执行时，仍能知道当时的权限、工具、模型 profile、审批策略是什么。

---

## 15. 与现有 CrestCreates 能力的映射

### 15.1 Dynamic API

Agent Runtime 的外部 API 暴露走 Generated Endpoint 主链。

不设计独立 runtime scanner，也不为 Agent Runtime 引入反射式 endpoint executor。

### 15.2 Multi-Tenancy

所有运行记录、记忆、预算、审批、并发控制都以 `TenantId` 为分区键。

### 15.3 Authorization

Agent 与 Tool 的执行权限统一复用现有权限检查链路，不引入新的权限计算器真相来源。

### 15.4 Settings

模型、预算、审计采样、默认策略、租户开关全部走 Setting Definition。

### 15.5 Audit Logging

Agent 执行产生的动作与普通业务请求共用审计上下文和存储主链，必要时扩展事件类型，而不是旁路存储。

---

## 16. 参考实现切分

建议按以下顺序建设，而不是一次把“全智能平台”铺满。

### Phase 1: Core Runtime

- Agent Definition / Tool Definition
- Execution / Step / Session 核心模型
- Runtime Pipeline
- Setting Definitions
- 权限与租户绑定
- 审计与日志接入
- 基础执行 API

### Phase 2: Generated Mainline

- Agent / Tool Source Generator
- Generated Registry
- Generated Endpoint 映射
- AoT 验证

### Phase 3: Control Plane

- Tool 审批
- 预算治理
- 并发/排队/取消
- 成本与指标面板

### Phase 4: Advanced Runtime

- Session Memory
- Knowledge Retrieval
- External Event Resume
- Workflow-style Long Running Agent

---

## 17. 测试策略

测试必须表达正式主链。

### 17.1 优先级

优先补以下真实集成测试：

- Agent 执行是否正确继承 `TenantId`
- Agent 是否正确复用 `CurrentUser` 权限
- Tool 调用是否被权限/审批拦截
- Setting 作用域覆盖是否生效
- 审计链是否记录执行与工具调用
- Generated Registry 是否为正式执行入口
- Prompt/Tool 输出注入是否被 guard 或 approval 拦截
- 异步恢复是否保留 `TenantId`、主体、定义版本和预算状态
- 写操作 Tool 重试是否满足幂等要求
- Kill switch 是否在 Pipeline 早期终止执行

### 17.2 避免错误信号

不应大量维护以下测试作为主资产：

- runtime reflection tool scanner
- 任意方法自动暴露 tool 的 fallback 路径
- 绕开审计与权限检查的 mock-only happy path

---

## 18. 关键决策总结

1. Agent Runtime 是 CrestCreates 平台能力，不是示例级 AI 模块
2. 正式主链必须是 compile-time generated registry + unified runtime pipeline
3. Tool 必须是可授权、可审计、可限流的强类型执行单元
4. 所有运行上下文统一绑定 `TenantId`、`CurrentUser`、Audit Context
5. 所有可变策略统一走 Setting Management
6. 所有对外能力统一走 Application Service + Generated Dynamic API
7. 所有运行记录统一进入日志、指标、审计主链
8. C# Agent Framework 作为适配层接入，不替代 CrestCreates 的领域、安全、审计、租户主链
9. Agent / Tool 定义必须版本化，历史执行必须可追溯
10. 异步和长时执行必须可恢复，不能依赖进程内状态

---

## 19. 建议的后续文档

本设计文档之后，建议继续拆三份实现文档：

1. `agent-runtime-domain-model.md`
2. `agent-runtime-source-generator-design.md`
3. `agent-runtime-execution-pipeline.md`

这样可以把“是什么”“怎么生成”“怎么跑”拆开，避免首个实现阶段文档继续膨胀。

