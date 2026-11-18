# ErpManagement 容器部署

企业级 ERP 示例系统的容器编排启动方式，兼容 `docker compose` 与 `podman-compose`。

## 环境约束与构建策略

目标环境中 **Docker Hub 不可达**（`docker.io` / `registry-1.docker.io` 拉取失败），但
**mcr.microsoft.com 可达**。因此：

- 前端（node）与后端 SDK 镜像无法从 Docker Hub 拉取，不在容器内构建。
- 采用**宿主预构建 + 运行时镜像打包**：`build.sh` 在宿主用 npm 构建前端、`dotnet publish`
  发布后端，再打包到官方 `mcr.microsoft.com/dotnet/aspnet:10.0` 运行时镜像。
- PostgreSQL / RabbitMQ 使用本地已有镜像（`postgres:16-alpine`、`rabbitmq:4.0-management-alpine`）。

这是设计规格 §7 预定的 fallback 路径，对样例而言是诚实且可复现的交付方式。

## TLS 说明

OpenIddict 的 `/connect/token` 端点强制 HTTPS（框架未暴露 HTTP 降级开关）。容器内应用以
`build.sh` 生成的 **localhost 开发证书**（`erp-dev.pfx`，密码 `erp-dev-cert`）终止 TLS，
监听 `https://+:8443`。这是样例专用开发证书，非生产密钥；浏览器访问 `https://localhost:8443`
需信任该 localhost 开发证书（`dotnet dev-certs https --trust`）。

## 步骤

```bash
# 1. 构建前端 + 后端 + 开发证书 + 应用镜像（默认 podman，可用 CONTAINER_RUNTIME=docker 切换）
./build.sh

# 2. 启动栈（二选一）
docker compose up -d
podman-compose up -d

# 3. 访问
#    https://localhost:8443   admin / ErpDemo#2026
```

可选 RabbitMQ（P3 MES/CRM 事件出站预留）：

```bash
docker compose -f docker-compose.yml -f docker-compose.rabbitmq.yml up -d
```

## 编排兼容性要点

- 就绪等待由应用 `entrypoint.sh` 的 TCP 探测完成，而非 `depends_on.condition`
  （podman-compose 对该字段支持不一致）。
- 数据持久化用命名卷 `erp-pgdata`。
- 应用连接串经环境变量 `ConnectionStrings__Default` 注入，指向 `db` 服务。
- 容器以 `ASPNETCORE_ENVIRONMENT=Development` 运行，启用开发公共密码客户端
  （`crestcreates-client`）与临时签名密钥——样例限定，生产需另行配置认证。

## 支持声明边界

本部署为 JIT 运行的框架消费示例，不产生新的 NativeAOT/trim 支持声明。RabbitMQ/Kafka/
Quartz/FileManagement 等能力的使用不超出 CapabilityMap 既有声明。