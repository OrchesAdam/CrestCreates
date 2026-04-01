# HealthCheck SourceGenerator 分析报告

## 一、问题分析

用户询问是否需要使用 SourceGenerator 来使 CrestCreates.HealthCheck 更容易扩展，特别是为了将来添加 CrestCreates.HealthCheck.Mvc。

## 二、SourceGenerator 适用场景

### 2.1 什么情况下 SourceGenerator 有价值？

| 场景 | SourceGenerator 价值 | 说明 |
|------|----------------------|------|
| 需要在编译时发现标记了特定特性的类 | **高** | 自动扫描并生成注册代码 |
| 需要为每个发现的内容生成大量样板代码 | **高** | 减少手动编写 |
| 运行时扫描会带来显著性能开销 | **高** | 将扫描工作转移到编译时 |
| 需要类型安全的动态代码生成 | **高** | 编译时即可发现错误 |

### 2.2 什么情况下 SourceGenerator 价值有限？

| 场景 | SourceGenerator 价值 | 说明 |
|------|----------------------|------|
| 功能已经由框架提供，无需额外代码生成 | **低** | ASP.NET Core 的 AddHealthChecks 已经足够 |
| 配置是动态的，需要运行时决定 | **低** | SourceGenerator 在编译时工作 |
| 简单的服务注册 | **低** | 手动注册更简单直观 |

## 三、HealthCheck 的扩展场景分析

### 3.1 可能的扩展方向

1. **CrestCreates.HealthCheck.Mvc**
   - 提供 MVC 特定的健康检查视图
   - 需要生成控制器和视图

2. **CrestCreates.HealthCheck.Database**
   - 提供数据库连接检查
   - 需要注册特定的健康检查实现

3. **CrestCreates.HealthCheck.Redis**
   - 提供 Redis 连接检查
   - 需要注册特定的健康检查实现

### 3.2 SourceGenerator 价值分析

| 扩展场景 | SourceGenerator 价值 | 理由 |
|----------|----------------------|------|
| 发现并注册所有健康检查实现 | **中** | 可以自动发现标记了 `[HealthCheck]` 的类 |
| 生成健康检查端点 | **中** | 可以根据配置生成不同的端点 |
| 生成健康检查汇总报告 | **低** | MVC 视图可以手动编写 |
| 注册服务到 DI 容器 | **高** | 可以自动注册所有健康检查服务 |

## 四、建议方案

### 方案 A：使用 SourceGenerator（推荐用于扩展性）

**实现方式**：
1. 创建 `[HealthCheck]` 特性，用于标记健康检查类
2. 创建 `HealthCheckSourceGenerator`，扫描所有标记了 `[HealthCheck]` 的类
3. 自动生成服务注册代码

**优点**：
- 自动化程度高
- 易于扩展新的健康检查实现
- 与框架其他模块保持一致

**缺点**：
- 增加复杂度
- 编译时间可能增加

### 方案 B：保持现状（简单直接）

**实现方式**：
- 使用 ASP.NET Core 内置的 `AddHealthChecks()` 方式
- 手动注册健康检查

**优点**：
- 简单直观
- 无额外复杂度
- 符合 ASP.NET Core 标准做法

**缺点**：
- 扩展需要手动配置
- 与框架其他模块风格不一致

### 方案 C：混合方案（平衡复杂度与实用性）

**实现方式**：
- 使用 SourceGenerator 扫描标记了 `[HealthCheck]` 的类
- 自动生成扩展方法 `AddHealthChecks<THealthCheck>()`
- 保持模块配置简单

**优点**：
- 适度自动化
- 易于扩展
- 复杂度适中

**缺点**：
- 需要维护 SourceGenerator

## 五、结论

### 推荐：方案 A（使用 SourceGenerator）

**理由**：
1. 与 EventBus、Scheduling、DynamicApi 的设计模式保持一致
2. 使 HealthCheck.Mvc 等扩展更容易实现
3. 提高自动化程度，减少手动配置
4. 便于将来添加更多的健康检查实现

### 是否需要立即实施？

可以考虑：
- **短期**：保持现状，因为 HealthCheck.AspNetCore 已经能正常工作
- **中期**：当需要添加 HealthCheck.Mvc 等扩展时，再添加 SourceGenerator
- **长期**：为了框架一致性，可以统一添加

## 六、SourceGenerator 设计草图

如果决定使用 SourceGenerator，可以这样设计：

```csharp
// 用户代码
[HealthCheck(Name = "Database", Tags = new[] { "db", "critical" })]
public class DatabaseHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        // 检查逻辑
    }
}

// 生成的代码
public static class HealthCheckServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseHealthCheck(
        this IServiceCollection services)
    {
        services.AddSingleton<DatabaseHealthCheck>();
        return services;
    }
}
```

这样用户只需要：
1. 添加 `[HealthCheck]` 特性
2. 调用生成的扩展方法注册

是否需要我制定详细的实施方案？