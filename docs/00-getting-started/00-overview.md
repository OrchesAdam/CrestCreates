# CrestCreates 项目概览

## 什么是 CrestCreates？

CrestCreates 是一个基于 .NET 10.0 的现代化应用框架，采用领域驱动设计（DDD）理念，提供了完整的基础设施和工具链，帮助开发者快速构建高质量的企业级应用。

## 核心特性

### 🏗️ 分层架构
- **领域层（Domain）**：核心业务逻辑，独立于外部依赖
- **应用层（Application）**：协调领域对象完成用例
- **基础设施层（Infrastructure）**：技术实现和外部集成
- **API 层（Web）**：处理 HTTP 请求和响应

### 📦 模块化设计
- 支持按需加载和隔离的功能模块
- 自动依赖管理和拓扑排序
- 清晰的模块生命周期管理

### 🗄️ 多 ORM 支持
- **EF Core**：Entity Framework Core 实现
- **FreeSql**：FreeSql ORM 实现
- **SqlSugar**：SqlSugar ORM 实现
- 统一的数据访问抽象层

### 📡 事件驱动
- **本地事件总线**：基于 MediatR 实现
- **分布式事件总线**：基于 RabbitMQ 实现
- **事件存储**：支持事件持久化和重放

### 🏢 多租户支持
- 多种租户解析方式（Header、Subdomain 等）
- 基于鉴别器的多租户数据隔离
- 灵活的连接字符串解析

### 🔐 RBAC 授权体系
- 细粒度的权限定义
- 基于角色的权限分配
- 声明式和命令式权限检查

### 🚀 代码生成器
- 实体生成器
- 服务生成器
- 模块生成器
- 控制器生成器

## 技术栈

| 类别 | 技术/框架 | 版本 |
|------|-----------|------|
| 基础框架 | .NET | 10.0 |
| ORM | Entity Framework Core | 7.0.0 |
| ORM | FreeSql | 3.5.215 |
| ORM | SqlSugar | 5.1.4.104 |
| 中介者模式 | MediatR | 11.1.0 |
| 缓存 | StackExchange.Redis | 2.7.10 |
| 日志 | Serilog | 3.1.1 |
| 消息队列 | RabbitMQ.Client | 6.5.0 |
| 测试 | xUnit | 2.6.2 |
| 测试 | Moq | 4.20.69 |
| 测试 | AutoFixture | 4.17.0 |

## 项目结构

```
CrestCreates/
├── framework/                    # 框架核心
│   ├── src/                      # 源代码
│   │   ├── CrestCreates.Domain/              # 领域层
│   │   ├── CrestCreates.Domain.Shared/       # 领域共享
│   │   ├── CrestCreates.Application/         # 应用层
│   │   ├── CrestCreates.Application.Contracts/ # 应用层接口
│   │   ├── CrestCreates.Infrastructure/      # 基础设施层
│   │   ├── CrestCreates.Web/                 # API 层
│   │   ├── CrestCreates.MultiTenancy/        # 多租户支持
│   │   ├── CrestCreates.OrmProviders.Abstract/ # ORM 抽象
│   │   ├── CrestCreates.OrmProviders.EFCore/ # EF Core 实现
│   │   ├── CrestCreates.OrmProviders.FreeSqlProvider/ # FreeSql 实现
│   │   └── CrestCreates.OrmProviders.SqlSugar/ # SqlSugar 实现
│   ├── test/                     # 测试项目
│   └── tools/                    # 工具
│       └── CrestCreates.CodeGenerator/       # 代码生成器
├── samples/                      # 示例项目
│   └── Ecommerce/                # 电商示例
├── docs/                         # 文档
└── README.md                     # 项目说明
```

## 适用场景

CrestCreates 适用于以下场景：

- **企业级应用**：需要复杂业务逻辑和高度可维护性的应用
- **微服务架构**：需要模块化和服务化的应用
- **多租户 SaaS**：需要支持多租户的应用
- **事件驱动应用**：需要事件驱动架构的应用
- **快速原型开发**：需要快速构建原型的场景

## 下一步

- [快速开始](01-quickstart.md) - 快速上手 CrestCreates
- [安装指南](02-installation.md) - 详细的安装和配置说明
- [架构概览](../01-architecture/00-overview.md) - 深入了解架构设计
