# ErpManagement 容器部署与浏览器验收

本示例以 Development/JIT 运行，基础栈必须同时包含 PostgreSQL、标准 RabbitMQ 和 ERP。
后端发布、应用镜像构建和宿主启动由总控执行；Playwright 仅连接已启动的 HTTPS 应用，不自行启动宿主。

## 构建和凭据

`build.sh` 在宿主使用 `npm ci` 构建前端，再发布后端并生成 localhost 开发证书，最后打包运行时镜像。
支持 `CONTAINER_RUNTIME=podman`（默认）或 `docker`；可用 `ERP_APP_IMAGE` 指定镜像标签。
也提供从仓库根上下文构建的 `Dockerfile.from-source`，依赖可访问的 node/SDK 镜像源。
运行时镜像安装 curl，用于 ERP readiness 探针。

从部署目录运行 Compose 前，在当前 Shell 或部署目录本机 `.env` 中提供：

```bash
export ERP_POSTGRES_PASSWORD='<数据库密码>'
export ERP_RABBITMQ_PASSWORD='<broker 密码>'
export ERP_E2E_ADMIN_PASSWORD='<admin 密码>'
export ERP_E2E_PROCUREMENT_PASSWORD='<erp-approver 密码>'
export ERP_E2E_SALES_PASSWORD='<erp-sales-approver 密码>'
```

密码需符合宿主身份策略（至少 8 位、大小写字母和数字）。`.env` 已忽略，勿提交；缺少必需变量时
Compose 直接拒绝。种子只在创建用户时写入密码，环境变量不会重置既有数据库用户密码。
不要将带凭据的 `compose config` 输出或浏览器 token/登录网络记录提交进仓库。

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

独立新卷复跑使用另一个项目名，不清除旧卷：

```bash
ERP_HTTPS_PORT=8445 podman-compose -p erp-acceptance-fresh up -d
# 等待该栈全部 healthy，然后连接新端口再执行同一套浏览器用例
ERP_E2E_BASE_URL=https://localhost:8445 npm --prefix ../erp-web run test:e2e
```

两组项目卷名独立，分别验证迁移、身份种子、审批 runtime 和业务空库初始化。
完整流程结束后，可用 `npm --prefix ../erp-web run test:e2e -- e2e/restart.spec.ts` 验证旧卷上的登录、
库存余额与已结清台账。该用例不创建新业务单据；完整流程用例仍要求空库。
`docker compose` 和 `podman-compose` 命令可替换；本机 docker 命令如果代理 podman-compose，不能据此声明
已测试独立的 Docker Engine。

### broker 中断续投

在独立的空栈上先启动全部服务，再停止 RabbitMQ。设置 `ERP_E2E_BROKER_RECOVERY=true` 执行完整浏览器用例；
该模式会先确认 `/health/ready` 为 503/`Unhealthy`，再验证收货、生产、发货与结算仍能提交。
随后启动 RabbitMQ，等待就绪恢复，查询 `crest_erp_runtime.runtime_outbox_messages` 与
`IntegrationEventInboxes`：同一业务消息应由原 Outbox 记录续投并只产生一条 Inbox 回执。
检查库存流水和结算明细不因续投增加，再运行重启数据检查。不要对共享演示栈执行此故障注入。

## Playwright 验收

在前端目录安装锁定依赖与浏览器（不启动后端）：

```bash
npm ci
npx playwright install chromium --only-shell
npm run test:e2e:typecheck
ERP_E2E_BASE_URL=https://localhost:8443 npm run test:e2e
```

`ERP_E2E_*_PASSWORD` 与该部署的种子密码相同。默认单 worker、不自动重试，避免把一次失败后的
业务写入当成可安全重跑。用例要求新卷的种子主数据和零初始库存，重复完整业务链请换 Compose 项目和端口。
trace/video 默认关闭，以免把登录密码、token 写入网络跟踪；失败输出与报告目录不提交。

用例使用真实 Chromium、登录表单、三个独立会话（管理员/采购经理/销售经理）、真实授权 HTTP 主链：

1. 未登录访问受保护页面重定向；管理员创建 15500 元请购，采购经理审批，异步回写后转采购订单、确认和收货过账。
2. 在库存余额页检查入库，调拨和盘点过账，再检查目标库位余额。
3. 生产驱动组件，启用 BOM 展开电机/壳体领料，实际报工 2 件并查看用料、报工记录和成品库存。
4. 创建 60000 元销售订单，销售经理审批，选择订单行创建发货单、过账并检查已发货和剩余库存。
5. 检查库存流水数量及来源身份，应付 15500 元与应收 60000 元通过页面全额结清；验证两个审批角色看不到另一类待办。

测试只观察由页面交互发出的响应，用于状态/金额断言和取得当前收货表单要求的采购订单及行 ID；
不通过 API 预置业务数据，不注入 token，不模拟接口。当前收货页需要手填内部 ID，属于已知页面体验限制。
审批状态通过页面重新读取等待真实异步回写，不使用固定 sleep。

覆盖边界：未覆盖审批驳回、跨租户、所有系统管理页、MES/CRM 外部调用、broker 故障恢复与并发幂等；
单个驱动组件 BOM 不代表多层 BOM 自动展开。Playwright 通过不产生 NativeAOT/trim 支持声明。
完整三服务新卷、旧卷重启与浏览器结果应以总控实际执行证据记账，不能仅凭配置或用例存在宣称通过。
