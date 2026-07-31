# CrestCreates

CrestCreates 是一个面向企业应用和 AI Agent 协作场景的 .NET 10 元数据驱动框架。它以统一的 Descriptor 模型描述业务能力，并通过编译期代码生成和受治理的执行主链，将能力投射为 Dynamic API、Workflow、HumanTask、MCP Tool 和 Agent Tool。

框架关注的不只是如何暴露一个 Handler，而是如何让同一项业务能力拥有可校验、可发现、可授权、可审计和可追踪的契约。这样，传统应用入口与 AI Agent 入口可以共享相同的业务语义和执行边界。

CrestCreates 不是单纯的 CRUD、模块化或运行时反射框架。它优先把发现和绑定工作移到编译期，把运行时收敛为强类型 Registry、不可变快照以及统一的 Capability Dispatcher / Pipeline；具体集成能力仍按各自的 Runtime 和 Host 边界提供。

## 核心架构

```text
Schema / Capability / Event / Workflow / Form / HumanTask
                          ↓
     Metadata Registry / Validation / Topology / Snapshot
                          ↓
       Capability / Workflow / HumanTask Runtime
                          ↓
       Capability Dispatcher + governed Pipeline
                          ↓
 Dynamic API / MCP Tool / Agent Tool / Application Host
```

- **Metadata first / 统一 Descriptor 模型**：用统一的元数据契约描述结构、能力、流程、事件和人工交互之间的关系。
- **Compile-time generation first**：Source Generator 和 BuildTasks 生成端点、绑定、注册和模块聚合代码，减少运行时扫描与反射。
- **不可变运行时快照**：Registry 在构建和校验后发布只读快照，让执行主链消费稳定、明确的契约。
- **One mainline / 唯一执行主链**：Dynamic API、MCP、Agent Tool 和 Workflow 都通过 Dispatcher / Pipeline 进入 Capability，而不是直接调用 Handler。
- **Human + AI governed execution**：授权、租户边界、审计、幂等、预算和人工审批属于执行链约束，而不是各入口自行复制的逻辑。
- **Trim / NativeAOT 友好方向**：默认发布模式是 Trim；NativeAOT 必须按具体 Runtime 的 publish-and-run fixture 显式验证，不能据此推导全仓库兼容。

## 当前能力

- **Unified Metadata Runtime**：提供 Descriptor、关系分析、校验、快照、兼容性和稳定契约等元数据基础能力。
- **Capability Runtime**：通过统一 Dispatcher 和 Pipeline 执行带有 Schema、权限、事件和治理边界的业务能力。
- **Workflow and HumanTask Runtime**：支持由 Capability 驱动的流程状态迁移、人工任务实例和完成结果。
- **Dynamic API and Compile-time Generation**：从应用服务和能力声明生成 Minimal API、绑定和注册代码。
- **MCP Tool Projection**：将符合约束的 Capability 投射为带 Schema 和强类型输入输出绑定的 MCP Tool 契约。
- **Agent Tool Projection**：将 Capability 投射为受角色、选择策略、预算和治理审计约束的 Agent Tool。
- **Multi-tenancy and Authorization**：提供租户上下文、权限检查、数据边界和身份集成的框架能力。
- **Infrastructure Integrations**：包含持久化、事件、缓存、调度、日志、审计和外部系统集成的可替换适配层。

CrestCreates 仍处于积极开发阶段，具体能力边界和使用方式以 [`docs/Feature`](docs/Feature/) 中的文档为准。

## Quick Start

要求 .NET SDK `10.0.100`；仓库通过 `global.json` 允许滚动到兼容的最新 minor SDK。

```bash
dotnet restore CrestCreates.slnx
dotnet build CrestCreates.slnx --no-restore
dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj --no-restore --no-build
```

运行 LibraryManagement Web sample：

```bash
dotnet run --project samples/LibraryManagement/LibraryManagement.Web
```

该 sample 启动时会执行 PostgreSQL 数据库迁移和种子初始化；默认连接配置位于 `samples/LibraryManagement/LibraryManagement.Web/appsettings.json`，需要本机提供对应的 PostgreSQL 服务。

针对 MCP Runtime 的 NativeAOT publish-and-run fixture：

```bash
dotnet test tests/Integrations/CrestCreates.Mcp.AotFixture.Tests/CrestCreates.Mcp.AotFixture.Tests.csproj --no-restore --no-build
```

该测试会执行固定的 `linux-x64` NativeAOT 发布、链接和原生产物运行，只代表该 fixture 覆盖的 MCP 主链。

## Documentation

| Topic | Documentation |
|---|---|
| Unified Metadata Model | [Usage guide](docs/Feature/UnifiedMetadataModel/usage-guide.md) · [Architecture](docs/Feature/UnifiedMetadataModel/arch-design.md) |
| Capability Exposure | [Usage guide](docs/Feature/CapabilityExposure/usage-guide.md) · [Architecture](docs/Feature/CapabilityExposure/arch-design.md) |
| Accountability | [Usage guide](docs/Feature/Accountability/usage-guide.md) · [Architecture](docs/Feature/Accountability/arch-design.md) |
| Form | [Usage guide](docs/Feature/Form/usage-guide.md) |
| Organization | [Usage guide](docs/Feature/Organization/usage-guide.md) |
| MCP | [Usage guide](docs/Feature/MCP/usage-guide.md) |
| Agent Tools | [Usage guide](docs/Feature/AgentTools/usage-guide.md) |

## Repository Layout

```text
CrestCreates/
├── src/                  # Core、Framework、Metadata、Runtime、Persistence、Platform、Tooling、Integrations
├── tests/                # 按源码分层组织的测试和 NativeAOT fixtures
├── samples/              # LibraryManagement、SaaSHelpdesk 等示例
├── docs/Feature/         # 面向使用者和模块开发者的正式文档
├── solutions/            # 分层 solution 文件
├── CrestCreates.slnx     # 根目录全量 solution
├── Directory.Build.props # 全局 .NET 10 和构建配置
└── Directory.Build.Aot.props # Trim / NativeAOT 发布配置
```

## Project Status and License

CrestCreates 处于积极开发状态，主要架构仍在持续收口，公开 API 可能继续演进。请以正式 Feature 文档和实际源码为准，不要将当前仓库状态理解为所有场景都已生产就绪。

MIT License
