# Issue #123 原生执行门禁与能力证据矩阵

日期：2026-10-10
实施基线：master `85bdbb017a479503d250b6edd174a05daf620a7b`（PR #134 merge，重新核对远端后记录）
关联：[Issue #123](https://github.com/OrchesAdam/CrestCreates/issues/123)、[Issue #121](https://github.com/OrchesAdam/CrestCreates/issues/121) 编排、
[#122 校准记录](2026-10-10-framework-architecture-review-follow-up.md)
本地验证环境：linux-x64、.NET SDK 10.0.112；CI 运行记录以 PR 最终 head 的 `ci.yml` / `full-validation.yml` run 与上传的
`aot-native-evidence-<sha>` artifact 为准。

## 结论摘要

1. **HTTP 原生请求门禁已建立并通过。** 执行链为
   `Release publish(CrestCreatesPublishMode=aot) → native link → 启动发布目录中的可执行文件（loopback、随机隔离端口）→ readiness → HTTP 断言 → 进程树回收 → 证据落盘`。
   `ci.yml` 与 `full-validation.yml` 的有效 CI 均调用，任一步失败都会使 CI 失败。
2. **基线缺口被本门禁真实暴露并修复（非人为红灯）。** 基线（85bdbb01）上原生进程可启动、负例返回 400/404，
   但所有成功请求返回 500：`System.NotSupportedException: JsonTypeInfo metadata for type
   'CrestCreates.DynamicApi.DynamicApiResponse`1[System.Object]' was not provided by TypeInfoResolver
   of type '[ApplicationApiJsonContext]'`。修复方式：fixture 按其自身 JsonSerializerContext **声明 envelope 根类型**
   （与 Procurement/Asset golden host 的既有模式一致），未关闭 reflection-disabled JSON 约束、未回退 JIT、未使用静默 fallback。
   修复后 POST/GET 均返回 200 且 payload 与请求唯一值一致。
3. **MCP / Agent.Tools 既有 publish-and-run 测试已接入有效 CI。** 此前 active CI 只 publish 主 fixture、从不调用这两个测试
   （`aot-validation.yml` 为 `on: []` 的禁用文件）。接入时未重造等价 runner；两个测试的 sentinel 对应真实断言（见下）。
4. **门禁自身的失败传播已验证**：进程提前退出、readiness 超时、HTTP 内容断言失败、缺失可执行文件 → 均明确失败；
   所有路径回收本次进程树（仅对本次启动的进程句柄发信号，不按名 kill）。
5. **覆盖 RID 与既有声明一致：仅 linux-x64。** 其他 RID 未验证/待确认，未通过 skip 伪装成支持。

## 一、HTTP native 请求门禁（本项新建）

### 1.1 入口与文件

| 组成 | 位置 |
| --- | --- |
| 被测 fixture | `tests/Framework/Api/CrestCreates.CapabilityEndpoint.AotFixture/`（gen 生成端点 + 兼容投影） |
| 门禁测试 | `tests/Framework/Api/CrestCreates.CapabilityEndpoint.AotFixture.Tests/CapabilityEndpointNativeHttpGateTests.cs` |
| runner 契约测试 | `tests/Framework/Api/CrestCreates.CapabilityEndpoint.AotFixture.Tests/NativeGateRunnerContractTests.cs` |
| 进程/证据支持 | `.../NativeServerProcess.cs`、`.../NativeAotGateSupport.cs` |
| JIT 集成测试（保留） | `.../AotFixtureTests.cs`（WebApplicationFactory，非 native run） |
| CI 调用 | `ci.yml`「Test — CapabilityEndpoint native HTTP gate」；`full-validation.yml` 同 |

### 1.2 受控要求与实现

- publish 显式 `-c Release -r linux-x64 --self-contained true -p:CrestCreatesPublishMode=aot --disable-build-servers -o <每次唯一 GUID 目录>`；
  只执行本次发布目录中的原生可执行文件，不使用旧产物、不执行 `dotnet xxx.dll`。
- 服务仅监听 `127.0.0.1`；端口由 OS 临时端口预留后交给子进程，绑定竞争有界重试（≤3 次，仅针对
  “address already in use”），否则显式失败。
- readiness 双重确认：子进程 stdout 出现 `Now listening on: <本次 baseUrl>`（证明端口归属本次子进程）+
  只读端点 GET 返回 200；等待期间检测进程提前退出；readiness 30s、单请求 10s、publish 20 分钟、执行超时均有界。
- 进程回收：成功、断言失败、启动失败、超时、取消路径统一在 finally 中 `Kill(entireProcessTree: true)` 并
  `WaitForExitAsync`，只操作本次启动的进程句柄。
- 证据（成功与失败均写）：`tests/artifacts/aot-native-evidence/*.json`，含 SHA、RID、publish 命令/exit code/日志、
  进程 PID/退出码、stdout/stderr、请求/响应 transcript（case、method、path、request body、status、response body、耗时）、result/failure。
  CI 以 `if: always()` 上传 `aot-native-evidence-<sha>`；无 token/连接串等敏感内容。

### 1.3 HTTP 用例矩阵（全部在原生进程中执行）

| 场景 | 请求 | 必须观察的结果（已断言） |
| --- | --- | --- |
| 合法 POST | `POST /api/greeting/process-greeting`，`{"Name":"gate-<guid>"}` | 200；解析 envelope `code=200`，`data.message == "Hello, gate-<guid>!"`（binding + 业务执行 + 序列化 + 本次唯一值往返） |
| GET 列表 | `GET /api/greeting/list-greetings` | 200；`code=200`；`data` 为数组且长度为 0（envelope 结构与空列表语义） |
| malformed JSON | POST，body `{ this is not valid json` | 400 且空 body（现有 body reader contract），不执行业务 |
| 不兼容字段类型 | POST，body `{"Name":123}` | 400，不执行业务 |
| 未映射路由 | `GET /api/greeting/no-such-endpoint` | 404，无 fallback/成功包装吞掉 |

响应 envelope 实测为 `{"data":...,"code":200,"message":"操作成功"}`；未映射路由由 ASP.NET 路由给出 404。

### 1.4 门禁失败传播证据（“门禁本身会失败”）

| 故障实验 | 方式 | 结果 |
| --- | --- | --- |
| 进程提前退出 | `/bin/bash -c "exit 7"` + readiness 等待 | `NativeFixtureStartupException` 包含 exit code 7；进程已回收（契约测试） |
| readiness 超时 | `/bin/bash -c "sleep 30"` + 1.5s deadline | `TimeoutException` 含 deadline；进程已回收（契约测试） |
| HTTP 内容断言失败 | 复用同一原生产物，对唯一值故意断言错误值（负例门禁测试） | `XunitException` 传播、证据 `result=failed`、`expectationMode=deliberately-wrong`、进程已回收 |
| 缺失可执行文件 | 指向不存在的路径 | 启动前即 `FileNotFoundException`（契约测试） |
| 旧产物误用 | 每次发布到唯一 GUID 目录并只执行该目录内文件 | 结构上不可能执行旧产物 |

linux-x64 上 CI 必跑（`Category=NativeAotGate` 过滤）；非 linux-x64 环境 skip 并给出明确原因，与支持声明一致。

## 二、能力/场景证据矩阵

> 术语：`NativeAOT-verified` = 真实 `PublishAot` fixture 完成 native link 且执行原生产物并断言业务结果；
> `publish/link` = 仅证明可发布可链接；`JIT-only` / `未验证` 按实标注。SHA 为运行期值：本地为本次分支 head，
> CI 为上表中的 run head；证据位置指 CI artifact `aot-native-evidence-<sha>` 或测试 stdout。

### 2.1 第一方 Runtime

| 能力/场景 | 正式执行入口 | fixture/test | Provider/SDK | RID | publish/link/run/request | 业务断言/sentinel | 覆盖限制 | 结论 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| HTTP 兼容投影端点（Generated AppService） | `ci.yml` native gate 步骤 | CapabilityEndpoint AotFixture + `CapabilityEndpointNativeHttpGateTests` | Kestrel + STJ SourceGen（无 ORM） | linux-x64 | publish/link/**run/request** | 5 用例矩阵（见 §1.3）；证据 `capability-endpoint-native-http.json` | fixture 主动移除 validators/bootstrap contributors：不证明认证、权限、租户、bootstrap 全链；仅 loopback | **NativeAOT-verified**（该 fixture 范围） |
| MCP 工具调用（typed input/output + handler 执行） | `ci.yml`「MCP native pipeline gate」 | `Mcp.AotFixture.Tests`（本轮接入 CI） | Mcp ToolProjection + Capability runtime | linux-x64 | publish/link/**run** | `MCP_NATIVEAOT_PIPELINE_OK`；`StructuredContent.value=trimmed` 且 handler 收到 `LastInput.Value=trimmed` | fixture 直接调用 `IMcpToolInvoker`：不证明 MCP 宿主/传输层；单次调用 | **NativeAOT-verified** |
| Agent.Tools governed 调用与 replay | `ci.yml`「Agent.Tools native pipeline gate」 | `Agent.Tools.AotFixture.Tests`（本轮接入 CI） | Agent.Tools + Capability + 内存治理组件 | linux-x64 | publish/link/**run** | `AGENT_TOOL_NATIVEAOT_PIPELINE_OK`；catalog 恰 1 工具、两次 invoke 后 `CallCount==1`（replay 不重复执行）、typed 输出 `native-aot` | 内存审批/预算/审计组件；不证明数据库终态 | **NativeAOT-verified** |
| Agent Memory 工具链（pack/expand/compress/curate/promote） | `run-json-contract-aot-gates.sh`（CI 调用） | `Agent.Memory.Tools.AotFixture.Tests` | `AddAgentMemoryRuntime` 默认 **InMemory** stores + InMemory audit sink | linux-x64 | publish/link/**run** | 5 sentinel：`agent_memory_build_pack/expand_source/curation_replay/accountability: OK`、`AGENT_MEMORY_TOOL_NATIVEAOT_PIPELINE_OK`；expand 断言 sanitize 不含 `"adjacent"`；promote 幂等 replay | 内存 store；不证明 PostgreSQL provider 本体 | **NativeAOT-verified**（内存 store 路径） |
| MCP Memory 工具（ctx/memory recall/expand） | 同上脚本 | `Mcp.Memory.AotFixture.Tests` | 4 个 InMemory store 显式注册 | linux-x64 | publish/link/**run** | 6 sentinel：`ctx_recall/ctx_expand/memory_recall/memory_source_expand/memory_accountability: OK`、`MCP_MEMORY_NATIVEAOT_PIPELINE_OK`；JSON `GetTypeInfo` 非空 | 内存 store；不证明 PostgreSQL provider 本体 | **NativeAOT-verified**（内存 store 路径） |
| Control Plane JSON 契约 | `ci.yml`「ControlPlane JsonContracts AotFixture」 | `ControlPlane.JsonContracts.AotFixture.Tests` | 纯 STJ SourceGen | linux-x64 | publish/link/**run** | 14 sentinel 含 `ReflectionFallback_IsDisabled:PASS`、各 review/hash/envelope round-trip、`CONTROL_PLANE_JSON_CONTRACT_NATIVEAOT_OK`；publish 不含 JsonContracts.BuildTasks/Tool | **与 PostgreSQL provider 互相不背书**；不含 HTTP/工作流执行 | **NativeAOT-verified**（JSON 契约面） |
| Runtime 持久化 / Workflow / HumanTask / Outbox（PostgreSQL 直连 Npgsql） | `ci.yml`「Persistence / PostgreSQL direct Npgsql provider」 | `Runtime.Persistence.PostgreSql.AotFixture.Tests`（AotHost，30 条断言） | Npgsql + Testcontainers PostgreSQL 16（或 `CREST_RUNTIME_PG_CONNECTION`） | linux-x64 | publish/link/**run**（需 DB） | `PHASE9B_*`、`PHASE9C_*`、`CRESTCREATES_RUNTIME_OUTBOX_OK`、`CRESTCREATES_HUMANTASK_RELIABLE_DELIVERY_OK`、`CRESTCREATES_OUTBOX_TERMINAL_ALREADY_APPLIED/STALE_FENCE_OK`、`CRESTCREATES_WORKFLOW_CONDITION_AOT_OK`、`CRESTCREATES_DURABLE_CONTROL_PLANE_WORKFLOW_CAPABILITY/HUMAN_TASK/SUBWORKFLOW_OK`、`CRESTCREATES_DURABLE_AGENT_MEMORY_OK`、`CRESTCREATES_AGENTTOOL_PREDISPATCH_CW04/05/07/08/09_OK` 等 | 依赖 Docker/DB；崩溃窗口为 kill 子进程模拟而非真实崩溃；无 HTTP；`CRESTCREATES_WORKFLOW_ACCOUNTABILITY_ACCEPTED_BEFORE_RESTART_OK` 打印但未断言 | **NativeAOT-verified**（所列场景；边界见覆盖限制） |
| Agent Tool pre-dispatch 崩溃恢复（CW04/05/07/08/09） | 同上（`AotHost <conn> <schema> predispatch-scenario`） | 同上 | Npgsql | linux-x64 | publish/link/run（需 DB） | 重派生子进程打印 `PREDISPATCH_CWxx_<attemptId>`，父进程回收后 reconciliation 断言 `Abandoned`/`Released`、二次 reconcile `AlreadyReleased` | kill-based 模拟 | **NativeAOT-verified** |
| Control Plane reference data / versioned org cache | 同上（`AotScenarioVariant` 逐场景 + C12 ledger） | 同上 | Npgsql | linux-x64 | publish/link/run（需 DB） | `CRESTCREATES_DURABLE_REFERENCE_ORGANIZATION_OK`、`CRESTCREATES_DURABLE_REFERENCE_DATA_PERMISSION_OK`、`CRESTCREATES_DURABLE_CONTROL_PLANE_REFERENCE_DATA_OK`、`CRESTCREATES_VERSIONED_ORGANIZATION_CACHE_OK` | 仅记录 `CaseId.C12 / RequiredRunner.Aot` 执行向量 | **NativeAOT-verified** |

### 2.2 Golden App 组合执行（含 HTTP 自请求）

| 能力/场景 | 正式执行入口 | fixture/test | Provider/SDK | RID | publish/link/run/request | 业务断言/sentinel | 覆盖限制 | 结论 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Procurement 端到端（HTTP + MCP + human task + agent 去重 + accountability 链） | `ci.yml`「Procurement Approval Golden Sample」 | `ProcurementApproval/scripts/run-nativeaot-golden-scenario.sh` + `Procurement.AotFixture.Tests` | **InMemory** runtime persistence | linux-x64 | publish/link/run/**request（宿主内 HttpClient 自请求）** | `CRESTCREATES_PROCUREMENT_HTTP_OK`、`CRESTCREATES_PROCUREMENT_SAMPLE_OK`、`CRESTCREATES_ACCOUNTABILITY_OK`；审批状态迁移、agent replay `store.Count==before+1`、审计链 `http.request→capability.execute→method.invoke→workflow.started→workflow.suspended` 因果字段 | 内存 provider；不证明 PostgreSQL 持久化 | **NativeAOT-verified** |
| Asset 端到端 + candidate v2（多 store、重启恢复、SQL 直查） | `ci.yml`「Asset Management Golden Sample」 | `AssetManagement/scripts/run-nativeaot-golden-scenario.sh` + `Asset.AotFixture.Tests` | PostgreSQL runtime（`ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING` 或 Testcontainers）+ SQLite 应用库 | linux-x64 | publish/link/run/**request** | `CRESTCREATES_ASSET_MANAGEMENT_GOLDEN_OK`、`CRESTCREATES_ASSET_MANAGEMENT_CANDIDATE_V2_GOLDEN_OK`；v2 4 案例（approve/reject 组合）、`Skipped` 终态、`SELECT COUNT(*) ... MaintenanceRecords` 直查、负例 profile 校验 | 需要 DB/Docker | **NativeAOT-verified** |

### 2.3 Core 与组合边界的口径

- Core 的“已验证”只能指向**具体被执行行为**：上述 native 运行实际执行了 capability dispatcher、兼容投影
  binding/result、accountability 记录链、模块内生成注册、STJ SourceGen 序列化路径等；不能由 ProjectReference
  或“引用了 Core”推导整个 Core verified。
- Workflow/HumanTask/Outbox 的覆盖分别对应 §2.1 PG AotHost 行中的条件分支、suspension、reliable delivery、
  outbox 终态与 restart 语义；未列场景（如真实进程崩溃、跨集群投递）未验证。
- Memory / ControlPlane 的 JSON 契约验证与 PostgreSQL provider 本体互不背书；Golden App 的
  approved inventory → fresh-host loading 组合证据保留在 Asset fixture 内（本轮未删改）。

## 三、本轮明确未验证 / 不得外推

- 非 linux-x64 RID（CI 仅覆盖 linux-x64；`aot-validation.yml` 的跨平台矩阵继续禁用，未批量启用）。
- HTTP fixture 的认证/权限/租户/bootstrap 全链（fixture 移除相关 contributors）；HTTP 仅 loopback。
- MCP 宿主/传输层实现（fixture 直达 `IMcpToolInvoker`）。
- PostgreSQL provider 之外的一切 ORM/驱动（EF Core/FreeSql/SqlSugar 及其 AOT 边界留给 #130）。
- UnitOfWork 工厂/Provider 装配（#124 轨道；本轮未触碰）。
- 三个主 fixture 的发布日志中存在**依赖项目**既有 IL2026/IL3050 警告（`CrestCreates.Application.Contracts`
  QueryExecutor、`CrestCreates.Aop` 配置 provider）；HTTP 门禁将警告断言限定在 fixture 自身编译单元，
  依赖警告与本次 HTTP 执行缺口无关，保留原样并记录，不作为本门禁的阻塞信号。

## 四、变更与 CI 接入清单

| 类别 | 变更 |
| --- | --- |
| fixture 修复 | `ApplicationApiJsonContext` 增加 `[JsonSerializable(typeof(DynamicApiResponse<object>))]`、`[JsonSerializable(typeof(List<GreetingResponse>))]`（envelope 根类型声明，golden host 同款模式） |
| 新增门禁 | `CapabilityEndpointNativeHttpGateTests`、`NativeGateRunnerContractTests`、`NativeServerProcess`、`NativeAotGateSupport`（`Category=NativeAotGate` trait） |
| MCP/Agent.Tools | 既有测试接入有效 CI；增加证据落盘与 12 分钟有界 publish 超时；断言未放宽 |
| `ci.yml` | 删除 3 个 publish-only 步骤（避免同 gate 重复 publish，由门禁测试自发布）；新增 3 个 native gate 步骤 + 证据 reset/上传（`if: always()`）；JIT 步骤改用 `Category!=NativeAotGate`；job `timeout-minutes: 120` |
| `full-validation.yml` | `aot-fixture-publish` → `native-aot-gates`：同三门禁 + 证据上传 + 有界超时；主 job 同步过滤 |
| 未改动 | `aot-validation.yml` 保持禁用；无历史脚本/fixture 被替换，无需归档 |

## 五、验证记录（本地，2026-10-10）

| 命令 | 结果 |
| --- | --- |
| `dotnet test .../CapabilityEndpoint.AotFixture.Tests --filter "FullyQualifiedName~CapabilityEndpointNativeHttpGateTests"` | 2/2 通过（含发布+原生运行，≈1m31s） |
| 同上 `--filter "FullyQualifiedName~NativeGateRunnerContractTests"` | 3/3 通过（1s） |
| 同上 `--filter "Category!=NativeAotGate"` | 3/3 JIT 集成测试通过（268ms） |
| `dotnet test tests/Integrations/CrestCreates.Mcp.AotFixture.Tests` | 1/1 通过（28s，热缓存） |
| `dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.AotFixture.Tests` | 1/1 通过（31s，热缓存） |
| `dotnet test tests/Framework/Api/CrestCreates.DynamicApi.Tests` | 73/73 通过 |
| `dotnet test tests/Framework/Api/CrestCreates.CompatibilityProjection.E2E.Tests` | 9/9 通过 |
| `dotnet build CrestCreates.slnx` | 0 错误 |
| 基线缺口复现（修复前） | 500 NotSupportedException（`DynamicApiResponse` object envelope），见 §结论摘要 2 |

CI 有效性以 PR 最终 head 的 `ci.yml`/`full-validation.yml` 运行为准；关闭要求中的“有效 CI 的 native 门禁通过”
以该 run 为最终证据。
