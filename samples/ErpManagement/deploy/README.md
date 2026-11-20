# ErpManagement 容器部署与浏览器验收

本示例以 Development/JIT 运行，基础栈必须同时包含 PostgreSQL、标准 RabbitMQ 和 ERP。
后端发布、应用镜像构建和宿主启动由总控执行；Playwright 仅连接已启动的 HTTPS 应用，不自行启动宿主。

## 构建和凭据

`build.sh` 在宿主使用 `npm ci` 构建前端，再发布后端并生成 localhost 开发证书，最后打包运行时镜像。
支持 `CONTAINER_RUNTIME=podman`（默认）或 `docker`；可用 `ERP_APP_IMAGE` 指定镜像标签。
也提供从仓库根上下文构建的 `Dockerfile.from-source`，依赖可访问的 node/SDK 镜像源。
运行时镜像安装 curl，用于 ERP readiness 探针。

从部署目录运行 Compose 前，在当前 Shell 导出以下变量。也可以将同一组变量写入 `deploy/.env`，每行使用 `名称='值'`，省略 `export`。

```bash
export ERP_POSTGRES_PASSWORD='<数据库密码>'
export ERP_RABBITMQ_PASSWORD='<broker 密码>'
export ERP_E2E_ADMIN_PASSWORD='<admin 密码>'
export ERP_E2E_PROCUREMENT_PASSWORD='<erp-approver 密码>'
export ERP_E2E_SALES_PASSWORD='<erp-sales-approver 密码>'
```

密码需符合宿主身份策略（至少 8 位、大小写字母和数字）。`.env` 已忽略，勿提交；缺少必需变量时
Compose 直接拒绝。Compose 自动读取 `.env` 用于配置插值，Playwright 进程只继承调用它的 Shell 环境。
如果使用本机 `.env`，从 `deploy/` 执行以下命令，再从同一 Shell 运行浏览器用例。

```bash
set -a
. ./.env
set +a
```

种子只在创建用户时写入密码，环境变量不会重置既有数据库用户密码。两个镜像构建上下文都会排除 `.env`、`.env.*`
等本机凭据文件。自定义凭据文件应放在构建上下文外。不要将带凭据的 `compose config` 输出或浏览器 token/登录记录提交进仓库。

RabbitMQ 配置直接对应框架 `RabbitMqOptions`：`RabbitMQ__HostName=rabbitmq`、`RabbitMQ__Port=5672`、
`RabbitMQ__UserName`、`RabbitMQ__Password`、`RabbitMQ__VirtualHost`。用户名和 vhost 默认 `erp`，
分别可由 `ERP_RABBITMQ_USER`、`ERP_RABBITMQ_VHOST` 覆盖。PostgreSQL 密码由 entrypoint 转为
Npgsql 引号转义后的连接串，支持分号和双引号。

OpenIddict 要求 HTTPS。示例使用生成的 localhost 开发证书，监听 8443；Playwright 允许该自签证书。
默认仅绑定宿主 `127.0.0.1:8443`，可通过 `ERP_HTTPS_PORT` 更改端口。

## 启动、就绪和持久化

从 `samples/ErpManagement/deploy/` 执行（应用镜像须已构建）：

```bash
podman-compose -p erp-acceptance up -d
# 或 docker compose -p erp-acceptance up -d --wait --wait-timeout 180
podman-compose -p erp-acceptance ps
curl -kfsS https://localhost:8443/health/ready
```

- PostgreSQL 探针用配置凭据执行 `SELECT 1`，验证数据库可用且密码有效。
- RabbitMQ 探针以 `rabbitmq` 用户检查 AMQP 5672 listener 与 vhost，避免启动期间诊断命令创建 root-owned cookie。
- ERP 依赖两个服务 `service_healthy` 后启动，readiness 必须 **2xx 且正文为 `Healthy`**。
  `/health/ready` 由宿主提供，应覆盖 PostgreSQL、审批 runtime 和 RabbitMQ 订阅 readiness。
  路由缺失时 SPA 回退的 HTTP 200/HTML 不能算就绪。
- PostgreSQL、RabbitMQ 分别使用项目作用域命名卷 `erp-pgdata`、`erp-rabbitmqdata`；broker 固定 hostname，
  保持 Mnesia 节点名跨容器重建稳定。`down` 后保留卷，勿对验收数据执行 `down -v`。
- broker 与数据库不默认暴露宿主端口。需要 broker 本机管理时叠加 `docker-compose.rabbitmq.yml`；
  其 AMQP/管理端口仅绑定 loopback。

验收后执行 `podman-compose -p erp-acceptance restart`，等待全部 healthy，再用同一账号查看刚才创建的
单据和台账，验证旧卷重启保留数据。改密码不会修改 RabbitMQ 既有卷内账号，请保留原凭据。

已有演示库按用户范围使用独立 Compose 项目、空命名卷和空闲端口初始化新版演示。保留旧卷及对应凭据。
如果旧库有未投递的 Outbox（包括待投递、投递中或尚未处置的死信），禁止原地升级。先在旧版核对并完成旧消息投递，或保留旧栈处理这些消息，新版仍使用独立空卷。新版的重投入口仅处理当前收货 Outbox 记录。

使用另一个项目名即可在独立空卷复跑：

```bash
ERP_HTTPS_PORT=8445 podman-compose -p erp-acceptance-fresh up -d
# 等待该栈全部 healthy，然后连接新端口再执行同一套浏览器用例
ERP_E2E_BASE_URL=https://localhost:8445 npm --prefix ../erp-web run test:e2e
```

两组项目卷名独立，分别验证迁移、身份种子、审批 runtime 和业务空库初始化。
完整流程结束且同一栈已重启后，用 `npm --prefix ../erp-web run test:e2e:restart` 验证重启后的登录、
库存余额、来源单据及各自的结算明细。沿用该栈的 `ERP_E2E_BASE_URL` 和种子密码。完整流程用例要求空库，重启用例读取已生成的业务数据。
`docker compose` 和 `podman-compose` 命令可替换；本机 docker 命令如果代理 podman-compose，不能据此声明
已测试独立的 Docker Engine。

### broker 中断续投

在独立空栈上先启动全部服务，再仅停止该栈的 `rabbitmq`。从 `deploy/` 对应的 Shell 运行以下命令，示例空栈使用 8445。

```bash
ERP_E2E_BASE_URL=https://localhost:8445 ERP_E2E_BROKER_RECOVERY=true npm --prefix ../erp-web run test:e2e
```

该命令只跑完整流程。用例先确认 `/health/ready` 返回 503 且正文为 `Unhealthy`，再验证收货、生产、发货与结算仍能提交。
随后启动该栈的 `rabbitmq`，等待就绪恢复，查询 `crest_erp_runtime.runtime_outbox_messages` 与 `IntegrationEventInboxes`，
核对同一业务消息由原 Outbox 记录续投且只产生一条 Inbox 回执。核对库存流水和结算明细计数保持一致，再重启同一栈，
待就绪后单独运行 `test:e2e:restart`。故障注入仅用于独立验收栈。

业务数据和 Outbox/runtime 必须位于同一 PostgreSQL 数据库，runtime 使用独立 schema（默认 `crest_erp_runtime`）。
`ConnectionStrings__Default` 与可选的 `Erp__Runtime__ConnectionString` 必须使用相同的单一主机、端口和数据库名。
租户也须沿用这个固定数据库，Outbox 追加才能复用业务事务。

Outbox 自动投递默认每条消息最多执行 8 次 handler，失败按 1 秒起步指数退避，上限 5 分钟，
单次 handler 超时 20 秒，租约 30 秒。该预算按尝试计数，包含 broker 中断时的失败，不能换算成保证成功的恢复时限。
预算耗尽后进入死信。broker 恢复后，拥有 `GoodsReceipt.Post` 的用户可对因尝试预算耗尽而进入死信的收货事件请求「重新投递」。
这会为原 Outbox 记录重置一次投递预算，沿用原消息标识。随后在同一收货单查看状态、失败码及消费回执。

### MES 报工调用升级

旧客户端调用 `POST /api/integration/mes/work-orders/{workOrderId}/report` 时，须在请求体中同时提供 `reportedQuantity`
和非空 UUID `operationId`。每笔新报工生成一个 ID，请求前持久保存该 ID 与原数量。超时或响应丢失时复用同一 ID 和数量；
同键同参回放结果，同键异参返回 409。已确认的下一笔报工使用新的 ID。

## Playwright 验收

在前端目录安装锁定依赖与浏览器（不启动后端）：

```bash
npm ci
npx playwright install chromium --only-shell
npm run test:e2e:typecheck
ERP_E2E_BASE_URL=https://localhost:8443 npm run test:e2e
# 同一栈实际重启后再运行
ERP_E2E_BASE_URL=https://localhost:8443 npm run test:e2e:restart
# 独立前端回归，使用部署的 SPA 静态文件和拦截的接口，可在任意演示库状态运行
ERP_E2E_BASE_URL=https://localhost:8443 npm run test:e2e:retry
```

`ERP_E2E_*_PASSWORD` 与该部署的种子密码相同。默认单 worker、不自动重试，避免把一次失败后的
业务写入当成可安全重跑。用例要求新卷的种子主数据和零初始库存，重复完整业务链请换 Compose 项目和端口。
trace/video 默认关闭，以免把登录密码、token 写入网络跟踪；失败输出与报告目录不提交。

完整流程使用真实 Chromium、登录表单和三个独立会话（管理员、采购经理、销售经理），走真实授权 HTTP 主链。

1. 未登录访问受保护页面重定向；管理员创建 15500 元请购，采购经理审批，异步回写后转采购订单、确认和收货过账。
2. 在库存余额页检查入库，调拨和盘点过账，再检查目标库位余额。
3. 生产驱动组件，启用 BOM 展开电机/壳体领料，实际报工 2 件并查看用料、报工记录和成品库存。
4. 创建 60000 元销售订单，销售经理审批，选择订单行创建发货单、过账并检查已发货和剩余库存。
5. 检查库存流水数量及来源身份，应付 15500 元与应收 60000 元通过页面全额结清；验证两个审批角色看不到另一类待办。

完整流程只观察由页面交互发出的响应，用于状态/金额断言和取得当前收货表单要求的采购订单及行 ID。
不通过 API 预置业务数据，不注入 token，不模拟接口。当前收货页需要手填内部 ID，属于已知页面体验限制。
审批状态通过页面重新读取等待真实异步回写，不使用固定 sleep。

`test:e2e:retry` 模拟明确拒绝与响应丢失，检查同页和原标签页刷新后复用操作 ID 和原金额，以及确认后同金额的新操作取得新 ID。
浏览器的待确认记录保存在浏览器 `localStorage` 中，并按服务端确认的用户 ID、租户 ID、单据操作端点分隔；
同一浏览器的标签页通过其中的有界租约避免同时重发同一笔未确认操作，租约不改变服务端幂等契约。
清理站点数据会丢失该记录。遇到未知结果应先从原标签页重试确认。

覆盖边界包括缺少审批驳回、跨租户、所有系统管理页、MES/CRM 外部调用与并发幂等用例。
broker 中断期间的业务写入由专用浏览器模式覆盖，恢复后的投递与去重按上面的数据库检查核对。
单个驱动组件 BOM 仅覆盖单层展开。Playwright 的证据限 JIT 部署。
完整三服务新卷、旧卷重启与浏览器结果应以总控实际执行证据记账，不能仅凭配置或用例存在宣称通过。
