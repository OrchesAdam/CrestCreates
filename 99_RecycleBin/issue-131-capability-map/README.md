# 归档说明：Issue #131 能力地图长尾裁定（capability-map）

归档日期：2026-10-23
实施基线：master `348c4d3c35b8682e6b47b6c2dd6058b046c1113a`
裁定来源：`docs/superpowers/specs/2026-10-23-issue-131-capability-map-tenant-id-guard-design.md` §3.1
能力地图条目：`docs/Feature/CapabilityMap.md` D-01 … D-06
盘点证据：`docs/superpowers/plans/2026-10-23-issue-131-issues-log.md` I-01/I-02（源码 SHA-256、直接消费者、方案成员逐项核对）

本目录保留原相对路径，未删除任何文件。构建产物（`bin/`、`obj/`）随目录一并归档，仅作历史留痕，不再参与任何编译或打包输入。

## 归档项目与替代方向

| 项目 | 归档时状态 | 真实需求由此承接 |
| --- | --- | --- |
| `src/Integrations/CrestCreates.Integration.ExternalApi` | 1 个 `AssemblyMarker.cs`，无引用消费者 | 既有 `HttpClient` / 具体集成实现；新的外部 API 适配须按 `src/Integrations` 边界立项并附准入记录 |
| `src/Integrations/CrestCreates.Integration.LegacyDatabase` | 1 个 `AssemblyMarker.cs`，无引用消费者 | `src/Persistence` Provider 边界；在具备证据前不得宣称仓库已有旧库集成 |
| `src/Runtime/Agent/CrestCreates.Agent.Runtime` | marker-only，注释宣称聚合 Memory+Authoring+ControlPlane，csproj 无聚合引用 | 执行面由 `CrestCreates.Capability` / `CrestCreates.Workflow` / `CrestCreates.Agent.Tools` 承载；治理面保持 Authoring → review → Control Plane |
| `src/Framework/Infrastructure/CrestCreates.Configuration` | `Set` 抛 `NotSupportedException`、`Subscribe` 只存回调、`NotifySubscribers` 空循环，无消费者 | 启动期配置：`IConfiguration`/Options；运行时可管理配置：正式 Setting Management |
| `src/Metadata/Draft/CrestCreates.Draft.Abstractions` + `src/Metadata/Draft/CrestCreates.Draft` | 实验业务草稿；Workflow 仅有 csproj 引用、无 C# 使用；`TenantIsolatedDraftStore.DeleteAsync` 透传、null context 放行 | 治理草稿主链：`CrestCreates.DescriptorDraft` + `CrestCreates.Agent.DraftContracts`；不做 payload 自动转换 |
| `tests/Metadata/Draft/CrestCreates.Draft.Tests` | 上述实验的专用测试 | 不迁往 `CrestCreates.DescriptorDraft.Tests`；防复活边界改由守卫承担 |
| `src/Runtime/Eventing/CrestCreates.EventBus.EventStore` | 内存 retry store + 反射 JSON；`EventRetryService` 用 `Guid.NewGuid()` 删原记录，无消费者 | 正式事件主链：LocalEvent typed dispatch / RabbitMQ 静态订阅 / Outbox |
| `src/Runtime/Eventing/CrestCreates.EventBus.Abstract/IEventStore.cs` | `IEventStore` 无实现、`IEventStoreSerializer`/`IEventRetryStore` 仅被上面归档项目实现 | 同目录其余在用契约保留在 active `CrestCreates.EventBus.Abstract`，未整包搬走 |

## 防复活边界

- `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/ArchivedProjectRegressionArchitectureTests.cs`：active 项目、方案成员与源码不得再出现上述项目、命名空间或模块注册；归档内容必须仍可在本目录定位。
- `ControlPlaneReferenceDataKernelArchitectureTests` 既有用例继续禁止 PostgreSQL Provider 注册旧 `IDraftStore`。
- `DependencyBoundaryTests` / `RuntimePersistenceArchitectureTests` 中以禁止片段形式引用 `CrestCreates.Agent.Runtime` 的断言保留为守卫，不构成消费者。

## 外部消费者披露

本轮以静态 XML 与源码盘点核对仓库内引用；无法证明外部 NuGet 消费者不存在。若发现真实消费者，须先补充具体证据并给出迁移或重新裁定，不得静默恢复兼容执行链。
