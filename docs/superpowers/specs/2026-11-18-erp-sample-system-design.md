# ERP 示例系统顶层设计（ErpManagement）

- 日期：2026-11-18
- 状态：设计已获维护者三项决策确认，进入 P0 实施
- 位置：`samples/ErpManagement/`
- 关联：`docs/Feature/CapabilityMap.md` §5、`AGENTS.md`、`memory.md`

## 1. 背景与目标

现有 samples 全部是纯后端 Minimal API 宿主（无前端、无登录页、无菜单），且各自只覆盖单一业务切片。
维护者决定新建一个**企业级 ERP 示例系统**，双重目的：

1. **相对完整的业务示例**：不是几个菜单的玩具，而是覆盖基础资料、采购、库存、销售、生产工单、
   财务-lite、系统管理、经营看板的可运行系统，并预留 MES / CRM 接入接口。
2. **框架能力验证面**：每条业务链绑定一组框架正式主链能力；跑不通或被迫绕行的地方就是框架缺口，
   记录进 CapabilityMap / Issue，**不在 sample 里打业务补丁绕过框架**。

已确认决策：

| 决策点 | 结论 |
| --- | --- |
| 前端技术栈 | React + Ant Design Pro 组件体系（antd + ProComponents，Vite 构建） |
| 旧样例处置 | 新建 ERP；LibraryManagement 归档为**独立切片**（见 §10）；SaaSHelpdesk / Asset / Procurement / DescriptorControlPlane 保留（被主链测试与 AOT fixture 消费） |
| v1 业务范围 | 核心链完整版（约 30 实体），财务只做应收应付台账-lite |

## 2. 总体架构

```text
L4  管理后台 SPA（React + antd + ProComponents）
      登录(OpenIddict password flow) / 权限驱动动态菜单 / 业务 CRUD / 审批中心 / 看板
L3  集成预留层（MES / CRM）
      出站 canonical 事件（[CrestEvent] + RabbitMQ 可选编排）
      入站 API-Key adapter（复用 SaaSHelpdesk CustomerApiKey 范式）
      [CrestModule] 模块插槽（MES/CRM 作为可插拔模块，v1 只留契约与占位页）
L2  ERP 业务域
      MasterData / Procurement / Inventory / Sales / Production / Finance-lite / Dashboard
L1  平台层（全部复用框架正式主链，不复制逻辑）
      AddCrestWeb 聚合：Identity / Permission / Tenant / Setting / Feature / Audit /
      OpenIddict / OpenApi(Scalar) / HealthCheck / MultiTenancy / LocalEvent
      另接：Workflow + HumanTask + Outbox(Runtime.Delivery) + Quartz + EFCore UoW
L0  基础设施（compose 编排，docker / podman 双兼容）
      PostgreSQL 16 + erp-app（内嵌 SPA 静态文件）+ RabbitMQ（overlay 可选）
```

依赖方向遵守仓库边界：业务五层不反向引用 Platform 内部；运行时能力经既有装配入口消费；
租户身份一律 `TenantId`（`ICurrentTenant.Id` 权威），名称仅展示。

## 3. 项目结构

```text
samples/ErpManagement/
├── ErpManagement.Domain.Shared/        # 枚举、常量、共享 DTO 基元、本地化资源键
├── ErpManagement.Domain/               # 实体、仓储接口、领域服务、Setting/Feature/Permission 定义
├── ErpManagement.Application.Contracts/# DTO、AppService 接口、权限常量
├── ErpManagement.Application/          # AppService（[CrestService]）、validator、种子、事件 handler
├── ErpManagement.EntityFrameworkCore/  # ErpManagementDbContext、EF 配置、Migrations
├── ErpManagement.Web/                  # 宿主：AddCrestWeb + 生成投影 + wwwroot(SPA 产物)
├── erp-web/                            # React + TS + Vite + antd + ProComponents 源码
├── deploy/
│   ├── Dockerfile                      # 多阶段（含 host-build fallback 说明）
│   ├── docker-compose.yml              # postgres + erp-app
│   └── docker-compose.rabbitmq.yml     # overlay：RabbitMQ + ERP 事件出站
└── ErpManagement.slnx                  # 样例独立方案
```

- 五层 csproj 均导入 `CrestCreates.Modules.props`，模块用 `[CrestModule]` 声明链
  （Domain.Shared → Domain → Contracts → Application → EFCore → Web），`RegisterModules()` 源生成装配。
- 加入 `CrestCreates.slnx`、`solutions/CrestCreates.All.slnx`、`solutions/CrestCreates.Platform.slnx`。
- CapabilityMap §5 `samples/**` 行登记 ErpManagement（samples 不要求 §4 准入记录，但集合变化需更新 §5）。
- 数据库：PostgreSQL（Npgsql），正式 EF Migrations（不用 EnsureCreated fallback）；
  运行时状态（Workflow/HumanTask/Outbox）接 `AddCrestCreatesPostgreSqlRuntimePersistence`（与业务同库不同 schema 前缀，按 Provider 既有约定）。

## 4. 业务域设计（约 30 实体）

### 4.1 MasterData 基础资料（9）

| 实体 | 要点 |
| --- | --- |
| Material | 编码唯一、类型（采购/自制/虚拟）、默认仓库、安全库存、采购提前期 |
| MaterialCategory | 树形分类 |
| UnitOfMeasure | 计量单位 + 换算率（相对基本单位） |
| Warehouse | 仓库、是否允许负库存（Setting 联动） |
| StorageLocation | 库位（属仓库） |
| Supplier | 供应商、付款条件、启用状态 |
| Customer | 客户、信用额度、启用状态（CRM 接入点） |
| Bom / BomLine | 物料 BOM 头/行，版本与生效状态 |

能力绑定：`[Entity]` + `[GenerateCrudService]` 正式 CRUD 生成链、`[GenerateObjectMapping]`、
权限定义 + AOP、 FluentValidation、软删除、审计。

### 4.2 Procurement 采购（6）

PurchaseRequisition / PurchaseRequisitionLine → PurchaseOrder / PurchaseOrderLine → GoodsReceipt / GoodsReceiptLine。

状态机：请购 `Draft → PendingApproval → Approved/Rejected → Converted`；
采购单 `Draft → Confirmed → PartiallyReceived → Received → Closed`；收货 `Draft → Posted`。

能力绑定：**Workflow + HumanTask 审批**（金额阈值来自 Setting，超阈值走人工审批，
阈值内自动批准）、**UoW**（单据+行同事务）、**Outbox**（收货过账 → 库存事件可靠投递）、
并发乐观锁（RowVersion/If-Match）。

### 4.3 Inventory 库存（6）

StockBalance（物料×仓库×库位现存）、StockTransaction（不可变流水）、
StockTransfer / StockTransferLine（调拨）、StockCount / StockCountLine（盘点盈亏）。

规则：一切库存变动只经由 StockTransaction 过账内核（共享执行核心），禁止 AppService 直接改余额；
不允许负库存时过账前校验；盘点盈亏生成调整流水。

能力绑定：**LocalEvent 强类型分发**（StockChanged 本地事件）、并发安全（余额行锁/版本）、
审计、多租户隔离验证。

### 4.4 Sales 销售（4）

SalesOrder / SalesOrderLine → SalesShipment / SalesShipmentLine。
状态机：订单 `Draft → PendingApproval → Approved → PartiallyShipped → Shipped → Closed`；
审批同采购走 Workflow/HumanTask；发货扣库存走库存过账内核；信用额度校验（Customer）。

能力绑定：**Feature 开关**（`Erp.Sales` 关闭时端点确定性拒绝）、Workflow、Outbox、UoW。

### 4.5 Production 生产（3）

WorkOrder（来源销售订单或手工）、WorkOrderMaterial（BOM 展开快照）、WorkOrderReport（报工，MES 回写点）。
状态机：`Planned → Released → InProgress → Completed → Closed`；
Release 时 BOM 展开 + 领料（库存过账）；报工 v1 手工录入，MES 经入站 adapter 回写。

能力绑定：**Quartz 定时作业**（逾期工单扫描）、canonical 事件 `WorkOrderReleased`（MES 出站）、
Feature `Erp.Production`。

### 4.6 Finance-lite 应收应付（2）

ArApLedger（方向应收/应付、来源单据、到期日、余额）、SettlementRecord（核销记录）。
收货过账 → 应付挂账；发货过账 → 应收挂账（LocalEvent handler）；核销减余额。

能力绑定：LocalEvent、UoW、Quartz（账龄快照作业）。不做总账/成本/结账（超出 v1）。

### 4.7 Dashboard 看板（无独立实体）

聚合查询 AppService：库存总值/低库存清单、本月采购/销售金额、待审批数、逾期工单、应收应付账龄。
能力绑定：Setting（预警阈值）、权限、只读查询适配。

### 4.8 系统管理（全部复用平台，不新建实体）

用户/角色/权限授予（Identity/Permission）、租户（Tenant）、Setting、Feature、审计查询、
定时任务管理（Quartz RAMJobStore，JIT-only 边界如实声明）、健康检查、OpenAPI/Scalar。

## 5. 前端设计（erp-web）

- 技术：Vite + React 18 + TypeScript + antd 5 + @ant-design/pro-components（ProLayout/ProTable/ProForm）。
  不引入完整 UmiJS Pro 脚手架（过重）；用 ProComponents 自建布局、路由（react-router）、请求层。
- 认证：OpenIddict password flow（public client），`/connect/token` 获取 access/refresh token；
  axios 拦截器注入 Bearer、401 刷新/跳登录。同源部署，无 CORS。
- 菜单：**前端声明菜单树 + 权限键过滤**。登录后拉取当前用户已授权权限集合，按权限键裁剪菜单与路由。
  平台当前无"菜单管理/当前用户权限聚合"API —— 若既有 Permission 查询端点不足以支撑，
  记为框架缺口 G-01，P0 先用既有端点组合，不新造第二套权限真相。
- 页面清单（按分期）：
  - P0：登录、主布局、基础资料 7 页（物料/分类/单位/仓库+库位/供应商/客户/BOM）、
    系统管理（用户/角色/权限/租户/Setting/Feature/审计）。
  - P1：请购/采购单/收货、库存余额/流水/调拨/盘点、**审批中心**（HumanTask 待办/已办/批准/驳回）。
  - P2：销售订单/发货、生产工单、应收应付、Dashboard（图表）、定时任务页。
  - P3：集成状态页（MES/CRM 事件出站统计、API-Key 管理占位）。
- 构建产物输出到 `ErpManagement.Web/wwwroot`，宿主 SPA fallback（非 /api、/connect 路径回 index.html）。
  开发态 Vite dev server 代理到后端。

## 6. MES / CRM 集成预留（P3 落地，P0 留契约位）

- **出站事件**（canonical `[CrestEvent]`，JsonContracts 静态 codec，RabbitMQ overlay 启用时发布）：
  `WorkOrderReleasedEvent`、`WorkOrderCompletedEvent`、`StockChangedEvent`、
  `PurchaseOrderConfirmedEvent`、`SalesOrderShippedEvent`、`CustomerChangedEvent`。
- **入站 adapter**：`/api/integration/mes/*`（报工回写、工单状态同步）、`/api/integration/crm/*`
  （客户/联系人同步），API-Key scheme（每集成方独立 key，权限限定到集成权限组）。
- **模块插槽**：`ErpManagement.Integration.Mes` / `.Crm` 以 `[CrestModule]` 依赖 ErpManagement.Application，
  v1 不建项目，仅在 Domain.Shared 预留事件契约与集成权限常量；Feature `Erp.MesIntegration` / `Erp.CrmIntegration` 控制。
- 边界：v1 不承诺真实 MES/CRM 系统对接，只交付契约 + adapter + 模拟消费者验证。

## 7. 容器编排（docker / podman 双兼容）

- `deploy/Dockerfile` 多阶段：node:22 构建前端 → dotnet/sdk:10.0 publish 后端 → dotnet/aspnet:10.0 运行。
  **环境风险**：本机 Docker Hub 不可达（见 memory R3-22）。fallback：宿主机预构建
  （npm build + dotnet publish），Dockerfile 仅 COPY 产物 + 运行时基础镜像；
  基础镜像优先用本地已有 / mcr.microsoft.com。P0 实测后确定主路径。
- `deploy/docker-compose.yml`：`postgres:16-alpine`（本地已有镜像）+ `erp-app`。
  兼容性约束：不用 `depends_on.condition`（podman-compose 支持不稳），应用侧启动重试等 DB；
  healthcheck 用镜像内已有工具；不用 compose profiles，RabbitMQ 用独立 overlay 文件
  （`-f docker-compose.yml -f docker-compose.rabbitmq.yml`），两种运行时都支持。
- 数据卷：pgdata 命名卷；连接串经环境变量注入。
- 验收：`docker compose up` 与 `podman-compose up` 均能启动，浏览器登录并走完一条业务链；
  本机 docker 实为 podman 别名（6.1.3），另用真实 docker 语法路径（compose v2）验证等价性。

## 8. 框架能力验证矩阵与已知缺口

| 能力 | ERP 消费点 | 已知边界/缺口 |
| --- | --- | --- |
| CRUD 生成链 + JSON 编译契约 | 全部基础资料/单据 | NativeAOT-verified 仅限既有 fixture 场景，ERP 本身 JIT 运行 |
| Identity/Permission/Role | 系统管理、菜单过滤 | G-01 菜单/当前用户权限聚合 API 待确认 |
| Tenant | 多租户演示（两个租户种子） | — |
| Setting/Feature | 审批阈值、预警阈值、域开关 | — |
| Workflow + HumanTask | 请购/销售审批 | activation 内存边界不变 |
| Outbox + LocalEvent | 收货/发货 → 库存/财务 | — |
| RabbitMQ canonical 事件 | MES/CRM 出站 | overlay 可选；JIT-only |
| Quartz | 预警/账龄/逾期扫描 | G-02 RAMJobStore 无持久化，重启丢任务（如实声明） |
| Audit | 全业务链 | retention 仅 PG 固定库 |
| FileManagement | 单据附件（P2 评估） | G-03 experimental：内存元数据，重启丢；不宣传为平台能力 |
| Notification | 审批待办提醒 | G-04 框架无通知模块——v1 用审批中心列表替代，缺口立项 |
| Excel 导入导出 | 基础资料批量导入 | G-05 框架无此能力——v1 不做或 sample 侧明示为样例私有 |
| DataDictionary | 枚举显示 | 仅 EnumDisplayAttribute，无独立模块 |
| OpenApi/Scalar | API 文档 | — |

缺口处理原则：G-xx 记录进 CapabilityMap/Issue 跟踪；sample 不造平台级补丁。

## 9. 分期与验收

| 期 | 内容 | 验收 |
| --- | --- | --- |
| P0 | 五层骨架 + AddCrestWeb + PG migrations + 种子；MasterData CRUD；前端登录/菜单/基础资料页；Dockerfile+compose | 容器启动，浏览器登录→物料 CRUD 全链；边界测试与 CapabilityMap 门禁通过 |
| P1 | 采购+库存+审批流（Workflow/HumanTask/Outbox/LocalEvent）；审批中心前端 | 请购→审批→采购→收货→库存流水端到端；负例（超阈值未批不可转单） |
| P2 | 销售+生产工单+财务-lite+看板+Quartz 作业+Feature/多租户验证 | 销售→发货→应收；工单 BOM 展开领料；看板数据正确；租户隔离断言 |
| P3 | MES/CRM 契约+adapter+模拟消费者；RabbitMQ overlay；缺口汇总 | 事件出站被模拟消费者收到；入站 API-Key 鉴权正负例；缺口清单落文档 |

每期：串行测试（一次一个套件）、容器内真实启动验证、证据记 memory.md；不新增 NativeAOT 支持声明。

## 10. LibraryManagement 归档（独立切片，P0 之后）

依赖事实（归档前必须迁移）：

- `tests/Framework/Web/CrestCreates.IntegrationTests` ProjectReference Library EFCore+Web（认证/租户/Setting/Feature 集成测试宿主）。
- `.github/workflows/aot-validation.yml:73` Library trimmed publish。
- `CrestCreates.slnx`、`solutions/CrestCreates.All.slnx`、`solutions/CrestCreates.Platform.slnx` 成员。

顺序：IntegrationTests 迁到 ERP 宿主 → CI publish 改指 ERP → 移 `samples/LibraryManagement` 至
`99_RecycleBin/`（人工确认删除）→ 更新 slnx / CapabilityMap / AGENTS.md 引用 → 边界守卫回归。

## 11. 支持声明边界

- ERP 示例是 JIT 运行的框架消费者；**不产生新的 NativeAOT/trim 支持声明**。
- Quartz/Kafka/RabbitMQ/FileManagement 等能力在 ERP 中的使用不超出 CapabilityMap 既有声明。
- 样例集合变化不改变框架支持声明（CapabilityMap §5 口径）。

## 12. P0 实施发现（框架缺口与摩擦，2026-11-18）

P0 已落地：五层骨架 + AddCrestWeb 装配 + 正式 EF Migrations + 种子；MasterData 9 实体 CRUD
生成链；React + AntD Pro 前端（登录/权限菜单/基础资料页/BOM）；容器编排资产。浏览器实测
登录→菜单→物料列表→新建（含审计时间）→BOM 状态/行 全链通过；API 侧 create/update/delete
与乐观并发正负例通过。实施中发现以下框架缺口/摩擦（候选 Issue，不在 sample 内打补丁绕过）：

- **G-06 `[Entity]` DTO 生成与手写同名 DTO 冲突**：`[Entity]` 会为实体在 Domain 程序集生成
  `BomDto`/`CreateBomDto`/`UpdateBomDto` 等；若在 Contracts 另手写同名 DTO，触发 CS0436 类型
  冲突，并使投影预编译的验证器符号绑定分裂到两个程序集，报 CCHTTP008（找不到闭合
  `IValidator<T>`）。正确做法是用 partial 扩展生成 DTO（`CategoryDto.partial` 先例）。
  缺口：生成器未对跨程序集同名 DTO 给出诊断指引。
- **G-07 响应信封与枚举序列化约定需前端显式适配**：正式投影成功响应包 `{data,code,message}`
  信封，失败包 `{code,message,details,traceId,statusCode}`；枚举经 source-generated JSON
  序列化为**数字**（非字符串）。前端必须 unwrap `.data` 并按数字映射枚举。缺口：缺少面向
  消费者的统一响应/枚举契约文档或前端 SDK。
- **G-08 OpenIddict token 端点强制 HTTPS 且无开发降级开关**：`/connect/token` 在 HTTP 下报
  ID2083，框架 `AddOpenIddictServer` 未暴露 `DisableTransportSecurityRequirement`。容器部署
  必须自行终止 TLS（本示例用 localhost 开发证书）。缺口：样例/容器场景缺少官方 TLS 引导。
- **G-09 平台管理 HTTP 面是框架级已知缺口（#129 §14.6 推迟），非样例漏装配**：经隔离宿主实证，
  `/api/user`、`/api/role`、`/api/permission-grant`、`/api/setting`、`/api/feature`、`/api/tenant`、
  `/api/audit-log` 在正式主链中**根本未被映射**。平台 AppService 仅有 `[CrestService]`（DI），
  无 `[CapabilityCompatibilityProjection]` 锚点 → 不生成端点；`MapCrestWeb` 只映射
  OpenIddict(`/connect/*`)+HealthChecks+OpenApi。唯一有平台投影的是 AuditRetention
  （`Platform.Web` 手写锚点 + `AddCrestAuditRetentionHttp`）。Library 打这些平台端点的集成测试
  本就被 CI 排除（`ci.yml:140-141`）且为既有红（`memory.md` 全量集成 19 过/30 败，30 败即平台 404）。
  无参 vs 命名 `AddGeneratedAppServiceHttp` 解析到同一 per-assembly 扩展，改调用形式无效。
  **结论**：ERP 系统管理页（用户/角色/权限/租户/Setting/Feature/审计）当前无可用平台 HTTP 端点，
  保持占位；补齐须按 AuditRetention 模板在合适层（如 Platform.Web）为各平台 AppService 手写投影
  锚点 + JsonContracts 闭合 + 宿主激活，属**独立框架设计任务**（禁止给 DDD Application 加 ASP.NET
  引用/扫描引用程序集/生成所有 public 方法）。当前用户信息可用 `/connect/userinfo`；
  `GetUserEffectivePermissionsAsync` 存在但无投影 URL。本缺口由 ERP 示例发现并立项，不在 sample 内补平台锚点。
- **G-10 种子数据绕过审计赋值**：`IDataSeeder` 直写 DbContext 不经 AppService 审计管线，
  `CreationTime` 落 `DateTime.MinValue`（前端显示 1901）。已在 ERP seeder 内显式补审计时间；
  缺口：框架可考虑为 seeder 提供审计赋值助手。

## 13. P0 交付与验证证据（2026-11-18）

**已交付**：

- 后端五层 `samples/ErpManagement/`（Domain.Shared/Domain/Application.Contracts/Application/
  EntityFrameworkCore/Web），`AddCrestWeb` 聚合 + Feature/Setting 定义 + 正式 EF Migrations
  （`InitialCreate`）+ 身份/业务种子；加入 `CrestCreates.slnx`、`solutions/All`、`solutions/Platform`
  与独立 `ErpManagement.slnx`；CapabilityMap §5 已登记。
- MasterData 9 实体（Material/MaterialCategory/UnitOfMeasure/Warehouse/StorageLocation/
  Supplier/Customer/Bom/BomLine）正式 ExplicitHost 投影 CRUD；BOM 头行一体 + 启用/停用。
- 前端 `erp-web`（Vite + React18 + TS + antd5 + ProComponents）：OpenIddict 密码流登录、
  权限驱动菜单（超管 claim 兜底）、基础资料 7 页 + BOM 行编辑、看板占位；产物入 Web/wwwroot，
  SPA fallback。
- 容器编排 `deploy/`：Dockerfile（宿主预构建 + mcr/aspnet:10.0 运行时 + dev 证书 TLS）、
  docker-compose.yml（postgres + erp-app）、docker-compose.rabbitmq.yml（P3 预留）、
  entrypoint.sh（TCP 等 DB）、build.sh、README；可选 Dockerfile.from-source（代理多阶段，未验证）。

**验证证据**（JIT，linux-x64，本地 podman 6.1.3）：

- 后端构建 0 错误；API 侧 material create/update/delete 通过，乐观并发负例（旧 stamp 删除报
  `Crest.Concurrency.Conflict`）与新 stamp 删除→404 通过。
- 浏览器实测（vite dev 5173 与宿主静态 5001 两路径）：登录→权限菜单→物料列表→新建（审计时间
  正确）→BOM 状态/行 全链通过。
- 容器实测：`podman-compose up -d` 起 `deploy_db_1`(healthy)+`deploy_erp-app_1`；应用 TCP 等 DB→
  迁移→种子→绑定 https://+:8443；`https://localhost:8443` 静态 200、`/connect/token` 200、
  `/api/material/all` 5 条且创建时间正确；浏览器登录→物料表渲染通过。
- compose 文件经 `podman-compose config` 校验；标准 v2 语法，docker compose 兼容由构造保证
  （本环境 docker 为 podman 包装器，无独立 docker  daemon 可测）。

**环境约束记录**：Docker Hub 直连不可达，经系统代理 127.0.0.1:7897 可达（node:22-alpine 已拉取）；
mcr.microsoft.com 直连可达（aspnet:10.0/sdk:10.0）。本机 podman-compose 的 pipx venv 解释器损坏
（python3.14→3.15 升级所致，既有环境问题），验证用 `PYTHONPATH=<venv site-packages> python3 -m
podman_compose` 模块方式运行，未改动用户环境。

**P0 限界**：系统管理页（用户/角色/权限/租户/Setting/Feature/审计/定时任务）因 G-09 平台 HTTP 面
缺口保持占位；采购/库存/销售/生产/财务/审批中心为 P1/P2 占位。不产生新的 NativeAOT/trim 声明。

## 14. 缺口补齐状态（2026-11-18，后续批次）

维护者令"先补齐框架缺口 + 完整测试，再推进 P1"，并拍板 **干净 REST** 与 **字符串枚举**。状态：

- **G-09 已补齐**：平台管理 HTTP 面经正式 ExplicitHost 投影发布（8 接口锚点 + AuditLog self-contract，
  复数 prefix，写用正确动词）；权限 wire 常量 + Tenant/AuditLog 新权限定义；Role/SettingDefinition/Tenant/AuditLog
  服务层 admission；`AddCrestWeb` 收编 Feature；userinfo 短名 claim；修复 TenantAppService 生成器同名接口陷阱。
  宿主 opt-in `AddCrestPlatformManagementHttp()`（程序集聚合、幂等）。契约文档见
  `docs/Feature/platform-management-http-contract.md`；后续 ERP 系统管理页（P1）即可对接。
- **G-07 已补齐**（破坏性）：HTTP/CRUD 生成的 JSON 上下文 `UseStringEnumConverter=true`。
- **G-08 已补齐**：OpenIddict `EnableDevelopmentHttpTransport` 开发态 HTTP 传输开关。
- **G-10 已补齐**：公共 `AuditPropertySetter` + 拦截器/seeder 复用。
- **G-06 已补齐**：`CCCG005` 跨程序集同名 DTO 碰撞诊断（跳过 + 告警）。
- **证据**：路由基线 + 宿主激活 4/4、Application.Tests 271/271、JsonContracts 162/162、生产构建 exit 0、
  ERP 宿主实测平台端点 200/匿名 401/features host-only 403。全量 IntegrationTests 因 rootless podman 下
  Testcontainers 挂起未本地跑通（CI docker 环境运行），详见 memory.md 2026-11-18 条目。
## 15. P1 交付状态（2026-11-18）

采购（请购/采购单/收货）+ 库存（余额/流水/调拨/盘点）+ 审批中心已实现并端到端验证（真实宿主 + 手动 PG）：
- 阈值驱动审批：请购超阈值→Workflow+HumanTask→审批中心待办→approve→决策消费者经 Outbox 幂等回写状态；
  阈值内自动批准。
- 收货过账经 `StockPostingManager` 内核入库存；`StockPostedEvent` 经 Outbox 可靠投递（status=Delivered + 业务
  IntegrationEventInboxes 落库）+ `StockChanged` LocalEvent。
- 迁移 `AddProcurementInventory`（8 表）；运行时 `crest_erp_runtime` 自动建表。
- 证据与已知边界见 memory.md 2026-11-18 P1 条目（含 IsInRole→Roles 修复、自定义 Outbox 契约需
  AddOutboxDeliveryHandler、审计拦截器未覆盖 kernel 插入、resend 400 待核）。

## 16. P2 交付状态（2026-11-18）

销售（订单/发货 + 阈值审批）、生产工单（BOM 展开领料 + 完工入库 + 报工）、财务-lite（应收/应付/结算）、
看板聚合、Quartz 低库存作业已实现并端到端验证（真实宿主 + 手动 PG + 容器）：
- 销售链：订单超阈值→Workflow+HumanTask(sales-manager)→决策消费者经 Outbox 回写 Approved→发货→库存出库 + 应收挂账。
- 生产链：工单下达→BOM 展开+组件领料→报工达量完工→成品入库；财务：应收结算；看板 summary；Quartz 调度器运行。
- 迁移 `AddSalesProductionFinance`（9 表）；前端销售/生产/财务/审批中心/看板页；容器冒烟 P2 端点 200。
- 证据与边界见 memory.md 2026-11-18 P2 条目（含 sales-approval 对 admin 403 的角色隔离说明、种子销售审批员配置条件）。

## 17. P3 状态与框架缺口汇总

- P3 入站使用 MES、CRM 专属 API Key，从配置解析有效租户 GUID。两端点均读取 `X-Integration-Api-Key`，租户由 Key 配置确定。示例密钥仅在 Development 环境接受。MES 报工记为 `MES`，CRM 按租户和客户编码创建或更新，更新时缺省的可选字段保留原值。`Erp.MesIntegration`、`Erp.CrmIntegration` 默认关闭。
- P3 出站通过 LocalEvent 和日志消费者演示工单下达、完工与客户变更。生产 RabbitMQ 绑定待接入，需 `[CrestEvent]`、JsonContracts codec、`AddRabbitMqEventBus` 及 `deploy/docker-compose.rabbitmq.yml` overlay。
- 验证使用 .NET SDK 10.0.114 构建（0 error）及独立 PostgreSQL 库。HTTP 实测缺失或错误 Key 返回 401、串用 Key 或 Feature 关闭返回 403、CRM 创建/更新返回 201/200，MES 两次报工达量完工且来源为 `MES`，三种出站日志均已观察到。
- **G-01** 菜单及当前用户权限缺少专用聚合 API。**G-02** Quartz 使用 RAMJobStore，任务重启后不保留。**G-03** FileManagement 仍为 experimental，内存元数据不持久。**G-04** 暂无 Notification 模块，以审批中心代替待办提醒。**G-05** 暂无 Excel 导入导出能力。
- **G-06** 同名 DTO 用 `CCCG005` 诊断并跳过冲突生成。**G-07** 正式投影保留响应信封，枚举按字符串序列化。**G-08** OpenIddict 保持 HTTPS 要求，开发态可显式启用 HTTP。**G-09** 平台管理 HTTP 面已通过 ExplicitHost 补齐。**G-10** 种子数据可复用 `AuditPropertySetter`。
- **P1/P2** 审批角色读取已改用 `ICurrentUser.Roles` 以兼容两种角色 claim。自定义 Outbox 契约需同时注册 `AddOutboxDeliveryHandler`。库存 kernel 写入仍未覆盖审计拦截器。RID publish 曾出现 OpenIddict 程序集不匹配，示例使用可移植 publish。已发送事件的 resend 返回 400，重发幂等语义待核。
