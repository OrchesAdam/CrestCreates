# CrestCreates 项目文档索引

本目录包含 CrestCreates 项目的所有技术文档，按分类组织。

## 📁 目录结构

### 📋 项目总结 (`project-summary/`)
- **工作单元完成总结.md** - 工作单元模式实现的完成情况总结
- **多租户完成总结.md** - 多租户架构实现的完成情况总结
- **ORM_ABSTRACTIONS_SUMMARY.md** - ORM 抽象层的总结文档
- **DbContextProvider_Reorganization_Summary.md** - DbContext 提供程序重组总结

### 🔍 分析文档 (`analysis/`)
- **未完成功能分析.md** - 项目中未完成功能的详细分析
- **FreeSqlAuditInterceptor错误分析.md** - FreeSql 审计拦截器错误分析

### 🧩 组件文档 (`components/`)
#### 领域层 (Domain)
- **Domain-Entities-README.md** - 领域实体说明
- **Domain-ValueObjects-README.md** - 值对象说明
- **ModuleA-Domain-README.md** - 模块A领域层说明
- **ModuleB-Domain-README.md** - 模块B领域层说明

#### 应用层 (Application)
- **Application-Services-README.md** - 应用服务说明
- **Application-Contracts-DTOs-README.md** - 应用合约和DTOs说明
- **ModuleA-Application-README.md** - 模块A应用层说明
- **ModuleB-Application-README.md** - 模块B应用层说明

#### 基础设施层 (Infrastructure)
- **Infrastructure-Authorization-README.md** - 授权组件说明
- **Infrastructure-Caching-README.md** - 缓存组件说明
- **Infrastructure-EventBus-README.md** - 事件总线说明
- **Infrastructure-UnitOfWork-README.md** - 工作单元模式说明
- **ModuleA-Infrastructure-README.md** - 模块A基础设施说明
- **ModuleB-Infrastructure-README.md** - 模块B基础设施说明

#### Web层
- **Web-Controllers-README.md** - Web 控制器说明
- **Web-Middlewares-README.md** - Web 中间件说明
- **ModuleA-Web-README.md** - 模块A Web层说明
- **ModuleB-Web-README.md** - 模块B Web层说明

#### ORM 提供程序
- **OrmProviders-Abstract-README.md** - ORM 抽象层说明
- **OrmProviders-ABSTRACTIONS_GUIDE.md** - ORM 抽象层使用指南
- **OrmProviders-ARCHITECTURE.md** - ORM 提供程序架构设计
- **OrmProviders-INTERFACE_INDEX.md** - ORM 接口索引
- **OrmProviders-FreeSqlProvider-README.md** - FreeSql 提供程序说明

#### 多租户
- **MultiTenancy-README.md** - 多租户实现说明
- **MultiTenancy-Abstract-README.md** - 多租户抽象层说明

#### 数据库上下文
- **DbContextProvider-Abstract-README.md** - 数据库上下文提供程序抽象层说明

### 🛠️ 工具文档 (`tools/`)
- **ServiceGenerator完成总结.md** - 服务生成器完成总结
- **ServiceGenerator-README.md** - 服务生成器使用说明
- **EntityGenerator完成总结.md** - 实体生成器完成总结
- **EntityGenerator-README.md** - 实体生成器使用说明
- **ModuleGenerator完成总结.md** - 模块生成器完成总结
- **Authorization-README.md** - 授权工具说明

### 🧪 测试文档 (`testing/`)
- **CodeGenerator-Tests-Services-README.md** - 代码生成器服务测试说明
- **CodeGenerator-Tests-Services-快速参考.md** - 代码生成器服务测试快速参考
- **CodeGenerator-Tests-Services-迁移总结.md** - 代码生成器服务测试迁移总结
- **CodeGenerator-Tests-Modules-README.md** - 代码生成器模块测试说明

### 🏗️ 架构文档 (根目录)
- **REFACTORING_ORM_PROVIDERS.md** - ORM 提供程序重构文档
- **TRANSACTION_IMPROVEMENT_PROPOSAL.md** - 事务改进提案

## 📖 如何使用

1. **新手入门**: 先阅读项目总结文档，了解整体架构和实现状态
2. **组件开发**: 查阅相应组件文档了解具体实现细节
3. **工具使用**: 参考工具文档了解代码生成器的使用方法
4. **问题排查**: 查看分析文档了解已知问题和解决方案

## 📝 文档更新

文档已从各个项目目录中整理到此统一位置，便于管理和查阅。如需更新文档，请直接修改此目录下的相应文件。