# 技术栈

本文档介绍 CrestCreates 框架使用的技术栈。

## 基础框架

| 技术 | 版本 | 说明 |
|------|------|------|
| .NET | 10.0 | 开发框架 |
| C# | 12.0 | 编程语言 |

## ORM 提供程序

| 技术 | 版本 | 说明 |
|------|------|------|
| Entity Framework Core | 7.0.0 | 微软官方 ORM |
| FreeSql | 3.5.215 | 轻量级 ORM |
| SqlSugar | 5.1.4.104 | 高性能 ORM |

## 事件总线

| 技术 | 版本 | 说明 |
|------|------|------|
| MediatR | 11.1.0 | 本地事件总线 |
| RabbitMQ.Client | 6.5.0 | 分布式事件总线 |

## 缓存

| 技术 | 版本 | 说明 |
|------|------|------|
| Microsoft.Extensions.Caching.Memory | 7.0.0 | 内存缓存 |
| StackExchange.Redis | 2.7.10 | Redis 缓存 |

## 日志

| 技术 | 版本 | 说明 |
|------|------|------|
| Serilog | 3.1.1 | 结构化日志 |
| Serilog.AspNetCore | 7.0.0 | ASP.NET Core 集成 |
| Serilog.Sinks.Console | 5.0.0 | 控制台输出 |
| Serilog.Sinks.File | 5.0.0 | 文件输出 |

## 对象映射

| 技术 | 版本 | 说明 |
|------|------|------|
| AutoMapper | 12.0.1 | 对象映射 |

## 验证

| 技术 | 版本 | 说明 |
|------|------|------|
| FluentValidation | 11.8.0 | 验证库 |

## 测试

| 技术 | 版本 | 说明 |
|------|------|------|
| xUnit | 2.6.2 | 测试框架 |
| Moq | 4.20.69 | 模拟框架 |
| AutoFixture | 4.17.0 | 测试数据生成 |
| FluentAssertions | 6.12.0 | 断言库 |

## 代码生成

| 技术 | 版本 | 说明 |
|------|------|------|
| Microsoft.CodeAnalysis.CSharp | 4.8.0 | Roslyn 编译器 |

## 开发工具

| 工具 | 版本 | 说明 |
|------|------|------|
| Visual Studio | 2022 | IDE |
| JetBrains Rider | 2023.2 | IDE |
| VS Code | 最新版 | 编辑器 |
| PowerShell | 7.x | 脚本 |

## 技术选型理由

### .NET 10.0

- **性能**：.NET 10 提供了显著的性能提升
- **新特性**：支持 C# 12 的最新特性
- **长期支持**：提供长期支持版本

### Entity Framework Core

- **官方支持**：微软官方 ORM，文档完善
- **功能丰富**：支持 LINQ、迁移、变更追踪等
- **社区活跃**：社区活跃，生态丰富

### FreeSql

- **轻量级**：相比 EF Core 更轻量
- **性能**：在某些场景下性能更好
- **灵活性**：提供更灵活的配置选项

### SqlSugar

- **高性能**：在批量操作场景下性能优异
- **易用性**：API 设计简洁易用
- **功能丰富**：支持多种数据库特性

### MediatR

- **简洁**：API 设计简洁
- **灵活**：支持多种消息处理模式
- **集成**：与 ASP.NET Core 集成良好

### RabbitMQ

- **可靠性**：消息持久化、确认机制
- **灵活性**：支持多种消息模式
- **生态**：生态丰富，文档完善

### Redis

- **性能**：内存数据库，读写性能优异
- **功能丰富**：支持多种数据结构
- **持久化**：支持数据持久化

### Serilog

- **结构化**：支持结构化日志
- **可扩展**：丰富的 Sink 生态系统
- **性能**：高性能的日志记录

### AutoMapper

- **约定优于配置**：减少配置代码
- **性能**：编译时映射，性能优异
- **灵活性**：支持复杂映射场景

## 版本管理

### 版本策略

- **主版本**：重大变更，可能包含破坏性变更
- **次版本**：新功能，向后兼容
- **修订版本**：Bug 修复，向后兼容

### 包管理

使用 Central Package Management 统一管理包版本：

```xml
<!-- Directory.Packages.props -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="7.0.0" />
    <PackageVersion Include="FreeSql" Version="3.5.215" />
    <PackageVersion Include="SqlSugarCore" Version="5.1.4.104" />
    <!-- 其他包 -->
  </ItemGroup>
</Project>
```

## 升级策略

### 定期升级

- 每月检查依赖包更新
- 每季度评估重大版本升级
- 每年进行全面的技术栈评估

### 升级流程

1. **评估影响**：评估升级对项目的影响
2. **创建分支**：在独立分支进行升级
3. **运行测试**：确保所有测试通过
4. **性能测试**：进行性能回归测试
5. **文档更新**：更新相关文档
6. **逐步部署**：逐步部署到生产环境

## 相关文档

- [架构概览](00-overview.md) - 架构设计概览
- [安装指南](../00-getting-started/02-installation.md) - 安装和配置
