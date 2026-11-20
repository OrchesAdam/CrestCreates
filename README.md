# CrestCreates

CrestCreates 是一个基于 .NET 10 的企业应用开发框架。它以 DDD 和模块化组织业务代码，用 Descriptor 描述业务能力、数据契约和流程，再用编译期生成的代码把这些声明接入 HTTP、MCP 和 Agent Tool。

业务模块可以从实体、DTO、应用服务和 Handler 开始。框架负责生成绑定与注册代码，宿主显式选择发布的入口，运行时通过强类型注册表和执行管线应用权限、租户上下文与审计。审批等长流程由 Workflow 和 HumanTask 管理。

## 快速开始

先安装 .NET SDK **10.0.114**，并确认当前终端选中了该版本。以下命令从仓库根目录执行。

```bash
dotnet --version
dotnet build solutions/CrestCreates.All.slnx
dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests --no-build
```

采购审批示例使用内存存储，可以直接运行。它会启动本地 HTTP 宿主，执行采购请求及人工审批场景，然后退出。

```bash
dotnet run --project samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host -- --golden-scenario
```

带数据库或消息中间件的集成测试使用预先启动的测试服务。Compose 命令、连接环境变量和专用故障注入实例见 [测试基础设施](infra/README.md)。

## 从声明到执行

| 层次 | 职责 |
| --- | --- |
| 业务模块 | 定义实体、DTO、应用服务、Handler 和 Descriptor。 |
| 编译期工具 | Source Generator 生成项目内的端点、类型绑定和服务注册。BuildTasks 聚合跨项目的模块与契约。 |
| 执行层 | 宿主通过 ExplicitHost 装配正式 HTTP 投影。Capability Dispatcher 和 Pipeline 执行业务能力，Workflow 与 HumanTask 管理流程和人工步骤。 |
| Agent 治理 | Authoring 生成候选草稿，确定性评审检查契约，Control Plane 管理证据和激活请求。Agent Tool 通过受治理的运行时入口执行。 |

平台模块提供租户、权限、Setting、Feature 和审计。ORM、持久化及消息中间件的支持范围按 Provider 和宿主组合记录。参见 [能力地图](docs/Feature/CapabilityMap.md) 和 [ORM 支持矩阵](docs/Feature/OrmProviderSupportMatrix.md)。

## 示例应用

| 示例 | 适合查看 |
| --- | --- |
| [ProcurementApproval](samples/ProcurementApproval/) | 一次完整的采购申请与审批流程，含生成的 HTTP 端点和 HumanTask。 |
| [SaaSHelpdesk](samples/SaaSHelpdesk/) | 多租户工单、SLA、身份权限与平台管理 HTTP。 |
| [AssetManagement](samples/AssetManagement/README.md) | 资产维护审批、PostgreSQL 运行时持久化、MCP 和 Agent Tool。 |
| [ErpManagement](samples/ErpManagement/deploy/README.md) | 采购、库存、生产和结算，以及 PostgreSQL Outbox 与 RabbitMQ 投递。 |

## 发布与验证

仓库默认使用 Trim 发布模式。NativeAOT 由 `-p:CrestCreatesPublishMode=aot` 显式启用。`NativeAOT-verified` 表示对应 fixture 已完成原生发布、链接，并执行了原生产物。结论限于记录的宿主、用例和 RID。参见 [原生执行证据矩阵](docs/review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md)。

以下命令在 Linux x64 上运行 Generated CRUD 的原生 HTTP 门禁，需要 .NET SDK 10.0.114 和原生编译工具链。

```bash
dotnet test tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture.Tests --filter "Category=NativeAotGate"
```

面向 `master` 的 PR 运行 [CI](https://github.com/OrchesAdam/CrestCreates/actions/workflows/ci.yml)。`master` 更新后运行 [Full Validation](https://github.com/OrchesAdam/CrestCreates/actions/workflows/full-validation.yml)。两条工作流均可手动触发，并包含原生发布、链接和执行门禁。

## 文档导航

- [元数据模型](docs/Feature/UnifiedMetadataModel/usage-guide.md)与 [Capability 暴露](docs/Feature/CapabilityExposure/usage-guide.md)
- [平台管理 HTTP 契约](docs/Feature/platform-management-http-contract.md)与 [Accountability](docs/Feature/Accountability/usage-guide.md)
- [MCP](docs/Feature/MCP/usage-guide.md)、[Agent Tools](docs/Feature/AgentTools/usage-guide.md)与 [Agent Control Plane](docs/Feature/AgentControlPlane/usage-guide.md)
- [工程约定](AGENTS.md)与 [平台状态记录](memory.md)

框架代码位于 `src/`，验收项目位于 `tests/`，示例位于 `samples/`。仓库根目录的 `CrestCreates.slnx` 和 `solutions/` 下的方案均可用于开发。

## 许可

MIT
