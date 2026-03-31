# CrestCreates 项目文档编写指南

## 文档结构规范

### 目录结构

```
docs/
├── 00-getting-started/          # 入门指南
│   ├── 00-overview.md           # 项目概览
│   ├── 01-quickstart.md         # 快速开始
│   └── 02-installation.md       # 安装指南
├── 01-architecture/             # 架构文档
│   ├── 00-overview.md           # 架构概览
│   ├── 01-domain-driven-design.md # DDD设计
│   ├── 02-layered-architecture.md # 分层架构
│   ├── 03-modularity.md         # 模块化设计
│   └── 04-technology-stack.md   # 技术栈
├── 02-core-concepts/            # 核心概念
│   ├── 00-entities.md           # 实体
│   ├── 01-value-objects.md      # 值对象
│   ├── 02-domain-events.md      # 领域事件
│   ├── 03-repositories.md       # 仓储
│   ├── 04-unit-of-work.md       # 工作单元
│   ├── 05-application-services.md # 应用服务
│   └── 06-dtos.md               # 数据传输对象
├── 03-infrastructure/           # 基础设施
│   ├── 00-overview.md           # 基础设施概览
│   ├── 01-orm-providers/        # ORM提供程序
│   │   ├── 00-overview.md
│   │   ├── 01-efcore.md
│   │   ├── 02-freesql.md
│   │   └── 03-sqlsugar.md
│   ├── 02-event-bus.md          # 事件总线
│   ├── 03-caching.md            # 缓存
│   ├── 04-logging.md            # 日志
│   ├── 05-authorization.md      # 授权
│   └── 06-multi-tenancy.md      # 多租户
├── 04-modules/                  # 模块开发
│   ├── 00-overview.md           # 模块开发概览
│   ├── 01-creating-modules.md   # 创建模块
│   ├── 02-module-lifecycle.md   # 模块生命周期
│   └── 03-dependency-management.md # 依赖管理
├── 05-tools/                    # 工具使用
│   ├── 00-overview.md           # 工具概览
│   ├── 01-code-generator.md     # 代码生成器
│   ├── 02-entity-generator.md   # 实体生成器
│   ├── 03-service-generator.md  # 服务生成器
│   └── 04-module-generator.md   # 模块生成器
├── 06-testing/                  # 测试指南
│   ├── 00-overview.md           # 测试概览
│   ├── 01-unit-testing.md       # 单元测试
│   ├── 02-integration-testing.md # 集成测试
│   └── 03-test-best-practices.md # 测试最佳实践
├── 07-api-reference/            # API参考
│   └── 00-overview.md           # API概览
├── 08-development/              # 开发指南
│   ├── 00-overview.md           # 开发概览
│   ├── 01-coding-standards.md   # 编码规范
│   ├── 02-git-workflow.md       # Git工作流
│   └── 03-contribution-guide.md # 贡献指南
├── 09-analysis/                 # 分析文档
│   ├── 00-overview.md           # 分析概览
│   └── 01-incomplete-features.md # 未完成功能
├── 10-project-summary/          # 项目总结
│   ├── 00-overview.md           # 总结概览
│   ├── 01-unit-of-work.md       # 工作单元总结
│   ├── 02-multi-tenancy.md      # 多租户总结
│   └── 03-orm-abstractions.md   # ORM抽象总结
└── INDEX.md                     # 文档索引（主入口）
```

### 文档命名规范

1. **文件命名**：使用小写字母，单词之间用连字符（-）分隔
2. **编号规则**：使用两位数字前缀（00, 01, 02...）表示顺序
3. **文件扩展名**：统一使用 `.md`
4. **目录命名**：使用小写字母，单词之间用连字符（-）分隔

### 文档模板

#### 组件文档模板

```markdown
# [组件名称]

## 概述

简要描述组件的作用和定位。

## 核心概念

解释组件涉及的核心概念。

## 使用场景

描述组件适用的场景。

## 基本用法

### 1. 配置

说明如何配置组件。

### 2. 使用示例

提供代码示例。

```csharp
// 代码示例
```

## 高级用法

### 1. 高级特性

描述高级特性。

### 2. 最佳实践

提供最佳实践建议。

## API 参考

### 接口/类

#### [接口/类名]

| 成员 | 类型 | 描述 |
|------|------|------|
| [成员名] | [类型] | [描述] |

## 相关文档

- [相关文档1](链接)
- [相关文档2](链接)

## 常见问题

### Q: [问题1]

A: [答案1]
```

#### 架构文档模板

```markdown
# [架构主题]

## 设计目标

描述架构设计的目标。

## 架构概览

提供架构图和简要说明。

## 核心组件

### [组件1]

描述组件1的职责和实现。

### [组件2]

描述组件2的职责和实现。

## 工作流程

描述工作流程。

## 设计决策

### 决策1: [决策标题]

- **背景**: [背景信息]
- **决策**: [决策内容]
- **理由**: [决策理由]
- **后果**: [决策后果]

## 相关文档

- [相关文档1](链接)
- [相关文档2](链接)
```

#### 指南文档模板

```markdown
# [指南主题]

## 前置条件

列出前置条件。

## 步骤

### 步骤 1: [步骤标题]

描述步骤1的内容。

```bash
# 命令示例
```

### 步骤 2: [步骤标题]

描述步骤2的内容。

## 验证

说明如何验证结果。

## 故障排除

### 问题1: [问题描述]

**解决方案**: [解决方案]

## 下一步

- [下一步1](链接)
- [下一步2](链接)
```

## 文档编写规范

### 格式规范

1. **标题层级**：使用 # 表示一级标题，## 表示二级标题，以此类推
2. **代码块**：使用 ```csharp 表示 C# 代码，```bash 表示 Bash 命令
3. **表格**：使用 Markdown 表格格式
4. **列表**：使用 - 表示无序列表，1. 表示有序列表
5. **链接**：使用 [文本](路径) 格式，路径使用相对路径

### 内容规范

1. **准确性**：确保文档内容与代码实现一致
2. **完整性**：覆盖组件的主要功能和用法
3. **清晰性**：使用简洁明了的语言
4. **示例性**：提供实用的代码示例
5. **关联性**：建立文档之间的交叉引用

### 更新规范

1. **及时更新**：代码变更时同步更新文档
2. **版本标记**：重大变更时添加版本说明
3. **变更日志**：记录文档的变更历史

## 文档维护流程

1. **创建文档**：按照模板创建新文档
2. **审查文档**：确保文档符合规范
3. **更新索引**：在 INDEX.md 中添加新文档链接
4. **交叉引用**：在相关文档中添加交叉引用
5. **定期审查**：定期审查文档的准确性和完整性
