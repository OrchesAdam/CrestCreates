# CrestCreates 缓存功能增强设计计划

## 📋 项目现状分析

### 当前缓存实现

项目已具备基础的缓存基础设施：

| 组件 | 状态 | 说明 |
|------|------|------|
| `ICache` 接口 | ✅ 已实现 | 基础缓存操作接口 |
| `MemoryCache` | ✅ 已实现 | 基于内存的缓存实现 |
| `RedisCache` | ✅ 已实现 | 基于 Redis 的分布式缓存 |
| `CacheOptions` | ✅ 已实现 | 基础配置选项 |
| `CacheKeyGenerator` | ✅ 已实现 | 缓存键生成工具 |
| `CachingExtensions` | ✅ 已实现 | 服务注册扩展 |

### 现有架构特点

1. **分层架构**：遵循 DDD 分层，缓存位于 Infrastructure 层
2. **多 ORM 支持**：EF Core、FreeSql、SqlSugar
3. **模块化设计**：支持按需加载功能模块
4. **多租户支持**：需要缓存与租户隔离

---

## 🎯 缓存功能增强目标

### 核心目标

1. **简化使用**：通过 AOP/拦截器自动处理缓存
2. **缓存一致性**：数据变更时自动失效缓存
3. **多级缓存**：L1(内存) + L2(Redis) 混合策略
4. **监控统计**：缓存命中率、性能指标
5. **分布式支持**：更好的集群环境支持

---

## 📐 详细设计方案

### 第一阶段：缓存装饰器与自动缓存 (高优先级)

#### 1.1 缓存特性属性

```csharp
// 自动缓存方法返回值
[Cacheable("product", Expiration = 300)]
public async Task<ProductDto> GetProductAsync(int id)

// 缓存清除
[CacheEvict("product", Key = "#id")]
public async Task UpdateProductAsync(int id, ProductDto dto)

// 批量清除
[CacheEvict("product", AllEntries = true)]
public async Task ClearProductCacheAsync()
```

**实现文件**：
- `CacheableAttribute.cs` - 缓存方法返回值
- `CacheEvictAttribute.cs` - 清除缓存
- `CachePutAttribute.cs` - 更新缓存

#### 1.2 缓存拦截器

```csharp
public class CachingInterceptor : IAsyncInterceptor
{
    // 拦截带缓存特性的方法
    // 处理缓存键解析（支持 SpEL 表达式）
    // 执行缓存逻辑
}
```

**实现文件**：
- `CachingInterceptor.cs` - 缓存拦截器核心
- `CacheKeyExpressionParser.cs` - 缓存键表达式解析器

#### 1.3 应用层缓存服务

```csharp
public interface ICacheService
{
    Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, CacheOptions options);
    Task RemoveByPatternAsync(string pattern);
    Task<T> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, CacheOptions options);
}
```

**实现文件**：
- `ICacheService.cs` / `CacheService.cs` - 应用层缓存服务

---

### 第二阶段：多级缓存策略 (高优先级)

#### 2.1 多级缓存实现

```
请求流程：
  1. 先查 L1 (MemoryCache)
  2. 未命中查 L2 (Redis)
  3. 都未命中执行原方法
  4. 回填 L1 和 L2
```

**实现文件**：
- `MultiLevelCache.cs` - 多级缓存实现
- `CacheLevel.cs` - 缓存级别枚举 (L1, L2, L1L2)

#### 2.2 缓存同步机制

```csharp
// Redis 发布订阅实现 L1 缓存同步
public class CacheSynchronizer
{
    // 监听 Redis 频道，同步清除其他实例的 L1 缓存
}
```

**实现文件**：
- `CacheSynchronizer.cs` - 缓存同步器

---

### 第三阶段：仓储层缓存集成 (中优先级)

#### 3.1 缓存仓储装饰器

```csharp
public class CachedRepository<TEntity, TId> : IRepository<TEntity, TId>
    where TEntity : class, IEntity<TId>
{
    private readonly IRepository<TEntity, TId> _inner;
    private readonly ICache _cache;
    
    // 自动缓存 GetById 结果
    // 数据变更时清除相关缓存
}
```

**实现文件**：
- `CachedRepository.cs` - 缓存仓储装饰器
- `RepositoryCacheExtensions.cs` - 仓储缓存扩展

#### 3.2 实体变更缓存失效

```csharp
// 监听领域事件自动清除缓存
public class EntityChangedCacheInvalidator : 
    INotificationHandler<EntityCreatedEvent>,
    INotificationHandler<EntityUpdatedEvent>,
    INotificationHandler<EntityDeletedEvent>
{
    // 根据实体类型和ID清除对应缓存
}
```

**实现文件**：
- `EntityChangedCacheInvalidator.cs` - 实体变更缓存失效处理器

---

### 第四阶段：多租户缓存支持 (中优先级)

#### 4.1 租户隔离缓存

```csharp
public class TenantCacheKeyGenerator : ICacheKeyGenerator
{
    private readonly ICurrentTenant _currentTenant;
    
    public string GenerateKey(string baseKey)
    {
        // 格式: crestcreates:{tenantId}:{baseKey}
        return $"{_options.KeyPrefix}{_currentTenant.Id}:{baseKey}";
    }
}
```

**实现文件**：
- `TenantCacheKeyGenerator.cs` - 多租户缓存键生成器
- `TenantAwareCache.cs` - 租户感知缓存包装器

---

### 第五阶段：缓存监控与统计 (中优先级)

#### 5.1 缓存指标收集

```csharp
public class CacheMetrics
{
    public long Hits { get; set; }
    public long Misses { get; set; }
    public long Evictions { get; set; }
    public double HitRate => (double)Hits / (Hits + Misses);
}
```

**实现文件**：
- `CacheMetrics.cs` - 缓存指标
- `ICacheMetricsCollector.cs` - 指标收集器接口
- `CacheMetricsCollector.cs` - 指标收集器实现

#### 5.2 健康检查

```csharp
public class CacheHealthCheck : IHealthCheck
{
    // 检查 Redis 连接状态
    // 检查缓存响应时间
}
```

**实现文件**：
- `CacheHealthCheck.cs` - 缓存健康检查

---

### 第六阶段：高级缓存功能 (低优先级)

#### 6.1 缓存预热

```csharp
public interface ICacheWarmer
{
    Task WarmUpAsync(CancellationToken cancellationToken);
}

// 实现示例：产品缓存预热器
public class ProductCacheWarmer : ICacheWarmer
{
    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        // 启动时预加载热点数据
    }
}
```

#### 6.2 防缓存击穿/穿透

```csharp
public class CacheBreakerProtection
{
    // 布隆过滤器防止缓存穿透
    // 互斥锁防止缓存击穿
    // 随机过期时间防止缓存雪崩
}
```

#### 6.3 响应缓存支持

```csharp
// 与 ASP.NET Core ResponseCache 集成
[ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "id" })]
public async Task<IActionResult> GetProduct(int id)
```

---

## 📁 文件结构规划

```
CrestCreates.Infrastructure/Caching/
├── Core/                               # 核心接口和基础实现
│   ├── ICache.cs                       # (已有) 缓存接口
│   ├── MemoryCache.cs                  # (已有) 内存缓存
│   ├── RedisCache.cs                   # (已有) Redis缓存
│   ├── CacheOptions.cs                 # (已有) 配置选项
│   └── CacheKeyGenerator.cs            # (已有) 键生成器
│
├── Attributes/                         # 缓存特性
│   ├── CacheableAttribute.cs           # 新增：启用缓存
│   ├── CacheEvictAttribute.cs          # 新增：清除缓存
│   └── CachePutAttribute.cs            # 新增：更新缓存
│
├── Interceptors/                       # 拦截器
│   ├── CachingInterceptor.cs           # 新增：缓存拦截器
│   └── CacheKeyExpressionParser.cs     # 新增：表达式解析
│
├── MultiLevel/                         # 多级缓存
│   ├── MultiLevelCache.cs              # 新增：多级缓存实现
│   ├── CacheLevel.cs                   # 新增：缓存级别
│   └── CacheSynchronizer.cs            # 新增：缓存同步
│
├── Repository/                         # 仓储集成
│   ├── CachedRepository.cs             # 新增：缓存装饰器
│   └── RepositoryCacheExtensions.cs    # 新增：扩展方法
│
├── MultiTenancy/                       # 多租户支持
│   ├── TenantCacheKeyGenerator.cs      # 新增：租户键生成
│   └── TenantAwareCache.cs             # 新增：租户感知缓存
│
├── Metrics/                            # 监控统计
│   ├── CacheMetrics.cs                 # 新增：缓存指标
│   ├── ICacheMetricsCollector.cs       # 新增：收集器接口
│   ├── CacheMetricsCollector.cs        # 新增：收集器实现
│   └── CacheHealthCheck.cs             # 新增：健康检查
│
├── Advanced/                           # 高级功能
│   ├── ICacheWarmer.cs                 # 新增：缓存预热接口
│   └── CacheBreakerProtection.cs       # 新增：防击穿保护
│
└── Extensions/
    └── CachingExtensions.cs            # (已有) 扩展方法

CrestCreates.Application/Caching/       # 应用层
├── ICacheService.cs                    # 新增：缓存服务接口
└── CacheService.cs                     # 新增：缓存服务实现
```

---

## 🔧 配置示例

### appsettings.json

```json
{
  "Caching": {
    "Provider": "multilevel",      // memory, redis, multilevel
    "DefaultExpiration": "00:30:00",
    "RedisConnectionString": "localhost:6379",
    "RedisDatabase": 0,
    "EnableKeyPrefix": true,
    "KeyPrefix": "crestcreates:",
    "MultiLevel": {
      "L1Expiration": "00:05:00",  // 内存缓存5分钟
      "L2Expiration": "00:30:00",  // Redis缓存30分钟
      "EnableL1Sync": true         // 启用L1同步
    },
    "Metrics": {
      "Enabled": true,
      "SampleRate": 1.0
    }
  }
}
```

### 服务注册

```csharp
services.AddCaching(options =>
{
    options.Provider = "multilevel";
    options.DefaultExpiration = TimeSpan.FromMinutes(30);
    options.RedisConnectionString = "localhost:6379";
})
.AddCachingInterceptors()           // 启用缓存拦截器
.AddRepositoryCaching()              // 启用仓储缓存
.AddCacheMetrics()                   // 启用缓存监控
.AddCacheHealthChecks();             // 启用健康检查
```

---

## 📊 实施优先级

| 阶段 | 功能 | 优先级 | 预估工作量 |
|------|------|--------|-----------|
| 1 | 缓存特性与拦截器 | 🔴 高 | 2-3 天 |
| 2 | 多级缓存策略 | 🔴 高 | 2-3 天 |
| 3 | 仓储层缓存集成 | 🟡 中 | 2 天 |
| 4 | 多租户缓存支持 | 🟡 中 | 1-2 天 |
| 5 | 缓存监控与统计 | 🟡 中 | 2 天 |
| 6 | 高级缓存功能 | 🟢 低 | 3-4 天 |

---

## ✅ 验收标准

1. **功能验收**
   - [ ] 通过特性标记实现自动缓存
   - [ ] 多级缓存正常工作，L1/L2 数据一致
   - [ ] 数据变更自动失效相关缓存
   - [ ] 多租户环境下缓存正确隔离

2. **性能验收**
   - [ ] 缓存命中时响应时间 < 5ms
   - [ ] 多级缓存比单级 Redis 快 30%+
   - [ ] 缓存同步延迟 < 100ms

3. **可靠性验收**
   - [ ] Redis 故障时自动降级到内存缓存
   - [ ] 缓存操作异常不影响业务流程
   - [ ] 健康检查能正确检测缓存状态

---

## 🚀 后续扩展建议

1. **分布式锁集成**：结合缓存实现分布式锁
2. **响应缓存**：与 ASP.NET Core 响应缓存集成
3. **缓存可视化**：提供缓存管理界面
4. **智能预热**：基于访问模式自动预热
5. **压缩支持**：大对象缓存自动压缩

---

## 📝 总结

本计划基于 CrestCreates 现有的缓存基础设施，通过分阶段实施，逐步增强缓存功能：

1. **第一阶段** 提供声明式缓存，大幅降低使用门槛
2. **第二阶段** 实现多级缓存，提升性能和可靠性
3. **第三阶段** 与仓储层集成，实现透明缓存
4. **第四阶段** 支持多租户，满足 SaaS 需求
5. **第五阶段** 提供监控能力，便于运维优化
6. **第六阶段** 高级功能，应对复杂场景

该设计遵循项目现有的架构风格，与 DDD、模块化、多租户等特性良好集成。
