# CrestCreates 项目 - GitHub Copilot 指导说明

## 📋 项目概述

CrestCreates 是一个基于 ASP.NET Core 和源代码生成器的模块化框架，专注于领域驱动设计 (DDD) 和基础设施即代码 (IaC) 能力。

### 🎯 核心价值
- **模块化架构**: 支持独立模块的开发和部署
- **ORM 抽象**: 统一的数据访问接口，支持多种 ORM 框架
- **多租户支持**: 完整的多租户解决方案
- **代码生成**: 自动化的代码生成工具
- **DDD 支持**: 完整的领域驱动设计实践

## 🏗️ 项目架构

### 解决方案结构
```
CrestCreates/
├── framework/                          # 框架核心代码
│   ├── src/                           # 源代码 (已展平化)
│   │   ├── CrestCreates.Application/                # 应用服务层
│   │   ├── CrestCreates.Application.Contracts/      # 应用合约层
│   │   ├── CrestCreates.Domain/                     # 领域层
│   │   ├── CrestCreates.Domain.Shared/              # 领域共享层
│   │   ├── CrestCreates.Infrastructure/             # 基础设施层
│   │   ├── CrestCreates.Web/                        # Web 表现层
│   │   ├── CrestCreates.DbContextProvider.Abstract/ # 数据库上下文抽象
│   │   ├── CrestCreates.MultiTenancy/               # 多租户核心
│   │   ├── CrestCreates.MultiTenancy.Abstract/      # 多租户抽象
│   │   ├── CrestCreates.OrmProviders.Abstract/      # ORM 抽象层
│   │   ├── CrestCreates.OrmProviders.EFCore/        # EF Core 实现
│   │   ├── CrestCreates.OrmProviders.FreeSqlProvider/ # FreeSql 实现
│   │   ├── CrestCreates.OrmProviders.SqlSugar/      # SqlSugar 实现
│   │   ├── CrestCreates.ModuleA/                     # 示例模块A
│   │   └── CrestCreates.ModuleB/                     # 示例模块B
│   ├── test/                          # 测试项目
│   └── tools/                         # 工具项目 (代码生成器)
├── docs/                              # 项目文档 (已重构)
└── .github/                          # GitHub 配置
```

### 📚 文档结构 (已整理)
```
docs/
├── INDEX.md                          # 📖 文档总索引
├── project-summary/                  # 📋 项目总结
│   ├── 工作单元完成总结.md
│   ├── 多租户完成总结.md
│   ├── ORM_ABSTRACTIONS_SUMMARY.md
│   └── DbContextProvider_Reorganization_Summary.md
├── analysis/                         # 🔍 分析报告
│   ├── 未完成功能分析.md
│   └── FreeSqlAuditInterceptor错误分析.md
├── components/                       # 🧩 组件文档 (31个文件)
├── tools/                           # 🛠️ 工具文档
└── testing/                         # 🧪 测试文档
```

## 🎯 核心组件详解

### 1. ORM 抽象层 (100% 完成) ✅

**位置**: `CrestCreates.OrmProviders.Abstract`
**完成度**: 100%

#### 核心接口
- `IEntity` / `IEntity<TKey>` - 实体接口
- `IAuditedEntity` / `ISoftDelete` / `IFullyAuditedEntity` - 审计和软删除
- `IMultiTenant` - 多租户支持
- `IDbContext` - 数据库上下文抽象
- `IDbSet<TEntity>` - 实体集操作
- `IRepository<TEntity>` / `IRepository<TEntity, TKey>` - 仓储模式
- `IQueryableBuilder<TEntity>` - 统一查询构建器
- `IDbTransaction` / `ITransactionManager` - 事务管理
- `IUnitOfWorkEnhanced` - 增强的工作单元

#### ORM 实现映射
| 抽象接口 | EF Core | FreeSql | SqlSugar |
|---------|---------|---------|----------|
| IDbContext | DbContext | IFreeSql | ISqlSugarClient |
| IDbSet\<T\> | DbSet\<T\> | ISelect\<T\> | ISugarQueryable\<T\> |
| IQueryableBuilder\<T\> | IQueryable\<T\> | ISelect\<T\> | ISugarQueryable\<T\> |

### 2. 多租户系统 (100% 完成) ✅

**位置**: `CrestCreates.MultiTenancy` / `CrestCreates.MultiTenancy.Abstract`
**完成度**: 100%

#### 核心功能
- **5种租户识别策略**: Header、Subdomain、QueryString、Cookie、Route
- **3种隔离策略**: Database (库级隔离)、Discriminator (行级隔离)、Schema (模式级隔离)
- **2种租户提供者**: InMemory、Configuration
- **安全防护**: 跨租户访问验证、自动数据过滤
- **中间件集成**: 自动租户解析和上下文管理

#### 使用示例
```csharp
// 快速配置
services.AddMultiTenancyWithInMemory(
    options => options.ResolutionStrategy = TenantResolutionStrategy.Header,
    provider => provider.AddTenant("tenant1", "Tenant 1", "ConnectionString1")
);

// 实体定义
public class Product : MultiTenantEntity<Guid>
{
    public string Name { get; set; }
}
```

### 3. 代码生成工具

**位置**: `tools/CrestCreates.CodeGenerator`

#### 生成器类型
- **EntityGenerator**: 实体代码生成 ✅
- **ServiceGenerator**: 服务代码生成 ✅
- **ModuleGenerator**: 模块代码生成 ✅
- **Authorization**: 授权代码生成 ✅

## 🛠️ 开发规范

### 1. 命名约定

#### 项目命名
- **领域层**: `CrestCreates.Domain`
- **应用层**: `CrestCreates.Application`
- **基础设施**: `CrestCreates.Infrastructure`
- **Web层**: `CrestCreates.Web`
- **抽象层**: `CrestCreates.*.Abstract`
- **模块**: `CrestCreates.Module{Name}`

#### 命名空间规范
```csharp
// ✅ 正确的命名空间
namespace CrestCreates.Infrastructure.Providers.EFCore
namespace CrestCreates.Application.Services
namespace CrestCreates.Domain.Entities

// ❌ 避免的命名空间 (与第三方库冲突)
namespace CrestCreates.Infrastructure.EntityFrameworkCore
namespace CrestCreates.Infrastructure.FreeSql
```

### 2. 接口设计原则

#### SOLID 原则应用
```csharp
// ✅ 单一职责 - 每个接口专注特定功能
public interface IRepository<TEntity> { }
public interface IQueryableBuilder<TEntity> { }
public interface IDbTransaction { }

// ✅ 开闭原则 - 对扩展开放，对修改关闭
public interface ITenantResolver 
{
    Task<string> ResolveAsync(HttpContext context);
}

// 添加新的解析器无需修改现有代码
public class JwtTenantResolver : ITenantResolver { }
```

#### 流式 API 设计
```csharp
// ✅ 链式调用支持
var result = await repository.AsQueryable()
    .Where(x => x.IsActive)
    .WhereIf(hasKeyword, x => x.Name.Contains(keyword))
    .OrderByDescending(x => x.CreationTime)
    .ToPagedResultAsync(pageIndex, pageSize);
```

### 3. 依赖注入配置

#### 扩展方法模式
```csharp
// ✅ 使用扩展方法进行服务注册
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMultiTenancy(
        this IServiceCollection services,
        Action<MultiTenancyOptions> configure = null)
    {
        // 配置逻辑
    }
}
```

### 4. 错误处理

#### 异常处理策略
```csharp
// ✅ 业务异常定义
public class CrossTenantAccessException : Exception
public class EntityNotFoundException : Exception
public class TransactionRollbackException : Exception

// ✅ 防御性编程
public async Task<TEntity> GetByIdAsync<TEntity>(object id)
{
    _ = id ?? throw new ArgumentNullException(nameof(id));
    
    var entity = await FindAsync<TEntity>(id);
    return entity ?? throw new EntityNotFoundException($"{typeof(TEntity).Name} with id {id} not found");
}
```

## 🧪 测试策略

### 1. 测试项目结构
```
test/
├── CrestCreates.Application.Tests/     # 应用层测试
├── CrestCreates.Domain.Tests/          # 领域层测试
├── CrestCreates.IntegrationTests/      # 集成测试
└── CrestCreates.CodeGenerator.Tests/   # 代码生成器测试
```

### 2. 测试模式

#### 单元测试
```csharp
[Fact]
public async Task Repository_GetByIdAsync_ShouldReturnEntity()
{
    // Arrange
    var mockDbContext = new Mock<IDbContext>();
    var repository = new Repository<Product>(mockDbContext.Object);
    
    // Act
    var result = await repository.GetByIdAsync(Guid.NewGuid());
    
    // Assert
    Assert.NotNull(result);
}
```

#### 集成测试
```csharp
[Fact]
public async Task MultiTenancy_ShouldFilterDataByTenant()
{
    // Arrange
    var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Add("X-Tenant-Id", "tenant1");
    
    // Act
    var response = await client.GetAsync("/api/products");
    
    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

## 📊 项目状态

### 完成度统计

| 组件 | 完成度 | 状态 | 说明 |
|------|--------|------|------|
| **ORM 抽象层** | 100% | ✅ | 18个核心接口，支持3种ORM |
| **多租户系统** | 100% | ✅ | 5种识别策略，3种隔离策略 |
| **工作单元模式** | 100% | ✅ | 增强的UoW，事务管理 |
| **代码生成器** | 95% | ✅ | 4种生成器，文档完善 |
| **模块化架构** | 85% | 🟡 | 基础架构完成，模块示例 |
| **授权系统** | 70% | 🟡 | 基础组件完成 |
| **缓存系统** | 60% | 🟡 | 基础实现 |
| **事件总线** | 50% | 🟡 | 基础架构 |

### 重点关注领域

1. **ORM 抽象层使用**: 优先使用抽象接口而非直接使用 ORM
2. **多租户开发**: 所有新实体需继承 `MultiTenantEntity`
3. **事务管理**: 使用 `IUnitOfWorkEnhanced` 进行事务操作
4. **查询构建**: 使用 `IQueryableBuilder` 进行复杂查询
5. **代码生成**: 利用现有生成器减少重复代码

## 🚀 开发指南

### 1. 新组件开发流程

```csharp
// 1. 定义抽象接口 (在 Abstract 项目中)
public interface INewService
{
    Task<Result> ProcessAsync(Request request);
}

// 2. 创建实现类
public class NewService : INewService
{
    private readonly IRepository<Entity> _repository;
    private readonly IUnitOfWorkEnhanced _unitOfWork;
    
    public async Task<Result> ProcessAsync(Request request)
    {
        // 使用抽象接口进行操作
    }
}

// 3. 注册服务
services.AddScoped<INewService, NewService>();
```

### 2. 数据访问最佳实践

```csharp
// ✅ 推荐: 使用仓储模式
public class ProductService
{
    private readonly IRepository<Product> _productRepository;
    private readonly IUnitOfWorkEnhanced _unitOfWork;
    
    public async Task<PagedResult<ProductDto>> GetProductsAsync(ProductQuery query)
    {
        return await _productRepository.AsQueryable()
            .Where(p => p.IsActive)
            .WhereIf(!string.IsNullOrEmpty(query.Keyword), 
                     p => p.Name.Contains(query.Keyword))
            .OrderByDescending(p => p.CreationTime)
            .ToPagedResultAsync(query.PageIndex, query.PageSize);
    }
}

// ❌ 避免: 直接使用 DbContext
public class ProductService
{
    private readonly DbContext _dbContext; // 避免直接依赖
}
```

### 3. 多租户开发

```csharp
// ✅ 实体定义
public class Product : MultiTenantEntity<Guid>
{
    public string Name { get; set; }
    public decimal Price { get; set; }
}

// ✅ 服务中的租户感知操作
public class ProductService
{
    public async Task<List<Product>> GetMyProductsAsync()
    {
        // 无需手动过滤 TenantId，框架自动处理
        return await _repository.GetAllAsync();
    }
}
```

## 🔧 常见问题解决

### 1. ORM 切换

**问题**: 需要从 EF Core 切换到 FreeSql
**解决**: 更换 DI 容器中的注册即可

```csharp
// 从这个
services.AddDbContext<MyDbContext>();

// 切换到这个
services.AddFreeSql<MyDbContext>();
// 业务代码无需修改
```

### 2. 跨租户访问异常

**问题**: `CrossTenantAccessException`
**解决**: 检查实体的 `TenantId` 设置

```csharp
// ❌ 可能导致跨租户访问
var entity = new Product { TenantId = "wrong-tenant" };

// ✅ 正确做法 - 让框架自动设置
var entity = new Product(); // TenantId 自动填充
```

### 3. 查询性能优化

**问题**: 查询性能慢
**解决**: 使用正确的查询模式

```csharp
// ✅ 无跟踪查询
var result = await repository.AsQueryable()
    .AsNoTracking()
    .Where(predicate)
    .ToListAsync();

// ✅ 投影查询
var result = await repository.AsQueryable()
    .Select(x => new ProductDto { Name = x.Name })
    .ToListAsync();
```

## 📖 参考文档

### 必读文档
1. **[项目文档索引](../docs/INDEX.md)** - 完整的文档导航
2. **[ORM 抽象层总结](../docs/project-summary/ORM_ABSTRACTIONS_SUMMARY.md)** - ORM 使用指南
3. **[多租户完成总结](../docs/project-summary/多租户完成总结.md)** - 多租户使用指南
4. **[架构设计文档](../docs/REFACTORING_ORM_PROVIDERS.md)** - 架构设计原理

### 组件文档
- **ORM 组件**: `docs/components/OrmProviders-*.md`
- **多租户组件**: `docs/components/MultiTenancy-*.md`
- **基础设施**: `docs/components/Infrastructure-*.md`
- **模块示例**: `docs/components/Module*-*.md`

### 工具文档
- **代码生成器**: `docs/tools/*Generator*.md`
- **测试文档**: `docs/testing/*.md`

## 🎨 代码风格

### C# 编码规范
```csharp
// ✅ 推荐的代码风格
public class ProductService : IProductService
{
    private readonly IRepository<Product> _repository;
    private readonly ILogger<ProductService> _logger;
    
    public ProductService(
        IRepository<Product> repository,
        ILogger<ProductService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async Task<Result<ProductDto>> GetProductAsync(Guid id)
    {
        try
        {
            _logger.LogDebug("Getting product with ID: {ProductId}", id);
            
            var product = await _repository.GetByIdAsync(id);
            if (product == null)
            {
                return Result<ProductDto>.NotFound($"Product {id} not found");
            }
            
            return Result<ProductDto>.Success(MapToDto(product));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product {ProductId}", id);
            return Result<ProductDto>.Error("Failed to get product");
        }
    }
}
```

### 异步编程
```csharp
// ✅ 正确的异步模式
public async Task<List<Product>> GetActiveProductsAsync()
{
    return await _repository.AsQueryable()
        .Where(p => p.IsActive)
        .ToListAsync();
}

// ❌ 避免的模式
public List<Product> GetActiveProducts()
{
    return _repository.AsQueryable()
        .Where(p => p.IsActive)
        .ToListAsync()
        .Result; // 避免 .Result
}
```

---

## 📝 重要提醒

### 开发时优先考虑
1. **使用抽象接口**: 永远优先使用 `I*` 接口而不是具体实现
2. **多租户意识**: 所有实体操作都要考虑多租户影响
3. **事务边界**: 明确事务边界，正确使用 `IUnitOfWorkEnhanced`
4. **性能考虑**: 大量数据操作时使用 `AsNoTracking()` 和投影
5. **文档更新**: 新功能开发时同步更新相关文档

### 代码审查要点
1. **接口使用**: 检查是否使用了抽象接口
2. **租户安全**: 验证多租户数据隔离
3. **异常处理**: 确保有适当的异常处理
4. **性能优化**: 查询是否使用了正确的优化手段
5. **测试覆盖**: 关键逻辑是否有对应测试

---

*本指导文档基于项目当前状态 (2025年11月12日) 生成，会根据项目进展持续更新。*
*如有疑问，请参考 `docs/` 目录下的详细文档或联系项目维护者。*