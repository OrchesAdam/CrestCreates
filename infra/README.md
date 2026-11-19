# 长驻测试服务

集成测试不再每次运行启动 Testcontainers 容器（exec 等待探针在 rootless Podman 上不稳定且慢）。
所有需要真实外部服务的测试一律经环境变量连接本目录预置的长驻实例；环境变量缺失时测试确定性失败。

## 启动

```bash
podman-compose -f infra/docker-compose.test-services.yml up -d
# 或 docker compose -f infra/docker-compose.test-services.yml up -d
```

全部端口仅绑定宿主 `127.0.0.1`。数据卷持久化；测试隔离靠每用例独立 schema / 一次性数据库，
不需要每次重建卷。RabbitMQ 卷内账号以首次启动为准，改密码需换卷。

## 环境变量对照表

```bash
# 共享 PG16（crest-test-pg16，5432）：runtime 契约 / LocalEvent / 边界 / IntegrationTests / Helpdesk / GeneratedCrud
export CREST_RUNTIME_PG_CONNECTION='Host=127.0.0.1;Port=5432;Database=crest_test;Username=test;Password=test'
export CREST_LOCAL_EVENT_PG_CONNECTION="$CREST_RUNTIME_PG_CONNECTION"
export CREST_ERP_TEST_PG_CONNECTION="$CREST_RUNTIME_PG_CONNECTION"
export CREST_HELPDESK_TEST_PG_CONNECTION="$CREST_RUNTIME_PG_CONNECTION"
export CREST_GENERATED_CRUD_TEST_PG_CONNECTION="$CREST_RUNTIME_PG_CONNECTION"

# 共享 RabbitMQ（crest-test-rabbitmq，5672 / 管理 15672）；ERP 夹具经 rabbitmqctl 自建每夹具 vhost，故需容器名
export CREST_ERP_TEST_AMQP_CONNECTION='amqp://test:test@127.0.0.1:5672/'
export CREST_ERP_TEST_RABBIT_CONTAINER=crest-test-rabbitmq

# RabbitMQ.Tests.Integration 直连共享 broker（管理 API 建/删每用例 vhost）
export CREST_RABBITMQ_HOST=127.0.0.1
export CREST_RABBITMQ_PORT=5672
export CREST_RABBITMQ_MGMT_PORT=15672
export CREST_RABBITMQ_USER=test
export CREST_RABBITMQ_PASSWORD=test

# CAP 专属（故障注入会停启这两个容器，禁止指向共享实例）
export CREST_CAP_TEST_PG_CONNECTION='Host=127.0.0.1;Port=5433;Database=cap_test;Username=test;Password=test'
export CREST_CAP_TEST_AMQP_CONNECTION='amqp://test:test@127.0.0.1:5673/'
export CREST_CAP_TEST_PG_CONTAINER=crest-test-cap-pg
export CREST_CAP_TEST_RABBIT_CONTAINER=crest-test-cap-rabbitmq

# Health 专属（同上，停启专属）
export CREST_HEALTH_TEST_PG_CONNECTION='Host=127.0.0.1;Port=5434;Database=health_test;Username=test;Password=test'
export CREST_HEALTH_TEST_PG_CONTAINER=crest-test-health-pg
export CREST_HEALTH_TEST_REDIS_ENDPOINT=127.0.0.1:6379
export CREST_HEALTH_TEST_REDIS_CONTAINER=crest-test-health-redis

# Kafka / Mongo
export CREST_KAFKA_TEST_BOOTSTRAP=127.0.0.1:9092
export CREST_MONGO_TEST_CONNECTION=mongodb://127.0.0.1:27017
```

含分号的值在 shell 中必须加引号。`CREST_TEST_CONTAINER_CLI` 可覆盖故障注入使用的 CLI
（默认按 podman → docker 顺序探测）。

## 约束

- 套件串行运行；故障注入套件（CAP、HealthConnectivity）停启的是专属容器，运行期间不得有其他套件依赖它们。
- ERP 浏览器验收（Playwright + 交付 Compose 栈）仍按 `samples/ErpManagement/deploy/README.md` 独立执行，
  其 broker 故障注入在一次性交付栈上做，不复用本目录共享实例。
- `CREST_ERP_TEST_PG_CONNECTION` 指向的数据库名必须含 `test`（或显式设置
  `CREST_ERP_ALLOW_EXTERNAL_TEST_DATABASE=true`），防止误连生产库。