# CrestCreates 项目文档索引

欢迎使用 CrestCreates 框架文档！本文档提供 CrestCreates 框架的全面指南，帮助您快速上手和深入了解框架。

## 📚 文档结构

### 🚀 入门指南 ([00-getting-started/](00-getting-started/))

适合新用户的快速入门文档。

- [项目概览](00-getting-started/00-overview.md) - CrestCreates 框架介绍
- [快速开始](00-getting-started/01-quickstart.md) - 5 分钟快速上手
- [安装指南](00-getting-started/02-installation.md) - 详细的安装和配置

### 🏗️ 架构文档 ([01-architecture/](01-architecture/))

深入了解 CrestCreates 的架构设计。

- [架构概览](01-architecture/00-overview.md) - 整体架构设计
- [领域驱动设计](01-architecture/01-domain-driven-design.md) - DDD 设计原则
- [分层架构](01-architecture/02-layered-architecture.md) - 分层架构详解
- [模块化设计](01-architecture/03-modularity.md) - 模块化开发指南
- [技术栈](01-architecture/04-technology-stack.md) - 技术栈说明

### 💡 核心概念 ([02-core-concepts/](02-core-concepts/))

学习 CrestCreates 的核心概念和用法。

- [实体](02-core-concepts/00-entities.md) - 实体详解
- [值对象](02-core-concepts/01-value-objects.md) - 值对象详解
- [领域事件](02-core-concepts/02-domain-events.md) - 领域事件详解
- [仓储](02-core-concepts/03-repositories.md) - 仓储详解
- [工作单元](02-core-concepts/04-unit-of-work.md) - 工作单元详解
- [应用服务](02-core-concepts/05-application-services.md) - 应用服务详解
- [DTOs](02-core-concepts/06-dtos.md) - 数据传输对象详解

### 🛠️ 基础设施 ([03-infrastructure/](03-infrastructure/))

了解基础设施组件的使用。

- [基础设施概览](03-infrastructure/00-overview.md)
- [ORM 提供程序](03-infrastructure/01-orm-providers/)
  - [ORM 概览](03-infrastructure/01-orm-providers/00-overview.md)
  - [EF Core](03-infrastructure/01-orm-providers/01-efcore.md)
  - [FreeSql](03-infrastructure/01-orm-providers/02-freesql.md)
  - [SqlSugar](03-infrastructure/01-orm-providers/03-sqlsugar.md)
- [事件总线](03-infrastructure/02-event-bus.md)
- [缓存](03-infrastructure/03-caching.md)
- [日志](03-infrastructure/04-logging.md)
- [授权](03-infrastructure/05-authorization.md)
- [多租户](03-infrastructure/06-multi-tenancy.md)

### 📦 模块开发 ([04-modules/](04-modules/))

学习如何开发和使用模块。

- [模块开发概览](04-modules/00-overview.md)
- [创建模块](04-modules/01-creating-modules.md)
- [模块生命周期](04-modules/02-module-lifecycle.md)
- [依赖管理](04-modules/03-dependency-management.md)

### 🔧 工具使用 ([05-tools/](05-tools/))

了解代码生成器等工具的使用。

- [工具概览](05-tools/00-overview.md)
- [代码生成器](05-tools/01-code-generator.md)
- [实体生成器](05-tools/02-entity-generator.md)
- [服务生成器](05-tools/03-service-generator.md)
- [模块生成器](05-tools/04-module-generator.md)

### 🧪 测试指南 ([06-testing/](06-testing/))

学习如何测试 CrestCreates 应用。

- [测试概览](06-testing/00-overview.md)
- [单元测试](06-testing/01-unit-testing.md)
- [集成测试](06-testing/02-integration-testing.md)
- [测试最佳实践](06-testing/03-test-best-practices.md)

### 📖 API 参考 ([07-api-reference/](07-api-reference/))

API 文档和参考。

- [API 概览](07-api-reference/00-overview.md)

### 👨‍💻 开发指南 ([08-development/](08-development/))

开发相关的指南和规范。

- [开发概览](08-development/00-overview.md)
- [编码规范](08-development/01-coding-standards.md)
- [Git 工作流](08-development/02-git-workflow.md)
- [贡献指南](08-development/03-contribution-guide.md)

### 📊 分析文档 ([09-analysis/](09-analysis/))

项目分析和问题排查。

- [分析概览](09-analysis/00-overview.md)
- [未完成功能](09-analysis/01-incomplete-features.md)

### 📝 项目总结 ([10-project-summary/](10-project-summary/))

项目开发总结和经验分享。

- [总结概览](10-project-summary/00-overview.md)
- [工作单元总结](10-project-summary/01-unit-of-work.md)
- [多租户总结](10-project-summary/02-multi-tenancy.md)
- [ORM 抽象总结](10-project-summary/03-orm-abstractions.md)

## 🎯 快速导航

### 按角色导航

**我是新用户**
1. [项目概览](00-getting-started/00-overview.md)
2. [快速开始](00-getting-started/01-quickstart.md)
3. [安装指南](00-getting-started/02-installation.md)

**我是架构师**
1. [架构概览](01-architecture/00-overview.md)
2. [领域驱动设计](01-architecture/01-domain-driven-design.md)
3. [分层架构](01-architecture/02-layered-architecture.md)

**我是开发者**
1. [核心概念](02-core-concepts/00-entities.md)
2. [模块开发](04-modules/00-overview.md)
3. [工具使用](05-tools/00-overview.md)

**我是测试工程师**
1. [测试概览](06-testing/00-overview.md)
2. [单元测试](06-testing/01-unit-testing.md)
3. [测试最佳实践](06-testing/03-test-best-practices.md)

### 按主题导航

**数据访问**
- [实体](02-core-concepts/00-entities.md)
- [仓储](02-core-concepts/03-repositories.md)
- [工作单元](02-core-concepts/04-unit-of-work.md)
- [ORM 提供程序](03-infrastructure/01-orm-providers/)

**事件驱动**
- [领域事件](02-core-concepts/02-domain-events.md)
- [事件总线](03-infrastructure/02-event-bus.md)

**模块化开发**
- [模块化设计](01-architecture/03-modularity.md)
- [创建模块](04-modules/01-creating-modules.md)
- [模块生命周期](04-modules/02-module-lifecycle.md)

**企业级特性**
- [多租户](03-infrastructure/06-multi-tenancy.md)
- [授权](03-infrastructure/05-authorization.md)
- [缓存](03-infrastructure/03-caching.md)
- [日志](03-infrastructure/04-logging.md)

## 📋 文档规范

### 文档命名

- 使用小写字母，单词之间用连字符（-）分隔
- 使用两位数字前缀表示顺序（00, 01, 02...）
- 文件扩展名为 `.md`

### 文档模板

请参考 [文档编写指南](DOCUMENTATION_GUIDE.md) 了解文档编写规范和模板。

## 🔄 文档更新

本文档会随框架版本更新而更新。如需查看最新文档，请访问：

- GitHub: https://github.com/your-org/CrestCreates/tree/main/docs
- 在线文档: https://docs.crestcreates.com

## 🤝 贡献文档

我们欢迎社区贡献文档！请参阅 [贡献指南](08-development/03-contribution-guide.md) 了解如何贡献。

## 📞 获取帮助

如果您在使用文档时遇到问题，可以通过以下方式获取帮助：

- 查看 [FAQ](00-getting-started/00-overview.md#常见问题)
- 提交 [GitHub Issue](https://github.com/your-org/CrestCreates/issues)
- 加入 [Discord 社区](https://discord.gg/crestcreates)

---

**最后更新**: 2026-03-31  
**文档版本**: v1.0.0  
**框架版本**: v1.0.0
