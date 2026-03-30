# CrestCreates.MultiTenancy.Abstract

多租户抽象层,提供多租户系统的核心接口和基础类型。

## 核心接口

### ITenantInfo
租户信息接口,定义租户的基本属性:
- `Id`: 租户唯一标识
- `Name`: 租户名称
- `ConnectionString`: 租户专用数据库连接字符串

### ICurrentTenant
当前租户上下文接口,用于访问和切换当前租户:
- `Tenant`: 获取当前租户信息
- `Id`: 获取当前租户ID
- `Change(tenantId)`: 临时切换租户上下文

### ITenantProvider
租户提供者接口,用于获取租户信息:
- `GetTenantAsync(tenantId)`: 根据租户ID获取租户详细信息

### ITenantResolver
租户解析器接口,用于从 HTTP 请求中提取租户标识:
- `ResolveAsync(httpContext)`: 从 HTTP 上下文解析租户ID

## 默认实现

### TenantInfo
`ITenantInfo` 的默认实现类,提供基本的租户信息存储。

## 设计原则

1. **接口隔离**: 每个接口职责单一,便于扩展和测试
2. **依赖倒置**: 高层模块依赖抽象,不依赖具体实现
3. **开闭原则**: 对扩展开放,对修改关闭

## 使用场景

此抽象层被以下项目引用:
- `CrestCreates.MultiTenancy`: 多租户具体实现
- `CrestCreates.OrmProviders.*`: ORM 提供者的多租户支持
- `CrestCreates.Infrastructure`: 基础设施层的多租户集成
- `CrestCreates.Web`: Web 层的多租户中间件

## 依赖关系

```
CrestCreates.MultiTenancy.Abstract
├── Microsoft.AspNetCore.Http.Abstractions (ITenantResolver 需要)
└── (无其他依赖)
```

## 扩展点

实现自定义多租户功能时,只需实现相应接口:
- 实现 `ITenantResolver` 可自定义租户识别策略(域名、Header、URL等)
- 实现 `ITenantProvider` 可自定义租户存储(数据库、缓存、配置文件等)
- 实现 `ICurrentTenant` 可自定义租户上下文管理

## 版本历史

- v1.0.0: 初始版本,提供核心多租户抽象接口
