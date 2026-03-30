# CrestCreates 工作单元 (Unit of Work) 使用指南

## 📋 概述

CrestCreates 框架提供了完整的工作单元（UoW）实现，支持三种主流ORM：
- ✅ **Entity Framework Core** - 完整支持
- ✅ **SqlSugar** - 完整支持  
- ✅ **FreeSql** - 完整支持

## 🎯 核心功能

### 1. 事务管理
- 开始事务 `BeginTransactionAsync()`
- 提交事务 `CommitTransactionAsync()`
- 回滚事务 `RollbackTransactionAsync()`
- 保存变更 `SaveChangesAsync()`

### 2. 审计字段自动填充
自动为实现 `IAuditedEntity` 的实体填充：
- `CreationTime` - 创建时间
- `CreatorId` - 创建人ID
- `LastModificationTime` - 最后修改时间
- `LastModifierId` - 最后修改人ID

### 3. 软删除支持
自动处理实现 `ISoftDelete` 的实体：
- 将物理删除转换为软删除
- 自动填充 `DeletionTime` 和 `DeleterId`
- 查询自动过滤已删除数据

## 🚀 快速开始

### 步骤 1: 注册服务

在 `Startup.cs` 或 `Program.cs` 中注册工作单元：

```csharp
// 方式1: 使用 EF Core（默认）
services.AddUnitOfWork();

// 方式2: 指定默认 ORM
services.AddUnitOfWork(OrmProvider.SqlSugar);
services.AddUnitOfWork(OrmProvider.FreeSql);

// 注册具体的 UoW 实现
services.AddScoped<EfCoreUnitOfWork>();
services.AddScoped<SqlSugarUnitOfWork>();
services.AddScoped<FreeSqlUnitOfWork>();
```

### 步骤 2: 配置审计拦截器

#### EF Core 配置

```csharp
services.AddDbContext<YourDbContext>((sp, options) =>
{
    options.UseSqlServer(connectionString);
    
    // 添加审计拦截器
    var currentUserProvider = sp.GetService<ICurrentUserProvider>();
    options.AddInterceptors(new AuditInterceptor(currentUserProvider));
});

// 注册当前用户提供者
services.AddScoped<ICurrentUserProvider, YourCurrentUserProvider>();
```

#### SqlSugar 配置

```csharp
var sqlSugarClient = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = connectionString,
    DbType = DbType.SqlServer,
    IsAutoCloseConnection = true
});

// 配置审计拦截器
var currentUserProvider = serviceProvider.GetService<ICurrentUserProvider>();
sqlSugarClient.ConfigureAuditInterceptor(currentUserProvider);
sqlSugarClient.ConfigureSoftDeleteFilter();

services.AddSingleton<ISqlSugarClient>(sqlSugarClient);
```

#### FreeSql 配置

```csharp
var freeSql = new FreeSqlBuilder()
    .UseConnectionString(DataType.SqlServer, connectionString)
    .UseAutoSyncStructure(true)
    .Build();

// 配置审计拦截器
var currentUserProvider = serviceProvider.GetService<ICurrentUserProvider>();
freeSql.ConfigureAuditInterceptor(currentUserProvider);
freeSql.ConfigureSoftDelete();

services.AddSingleton<IFreeSql>(freeSql);
```

## 💡 使用示例

### 示例 1: 基本事务操作

```csharp
public class ProductService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<Product, Guid> _productRepository;

    public ProductService(IUnitOfWork unitOfWork, IRepository<Product, Guid> productRepository)
    {
        _unitOfWork = unitOfWork;
        _productRepository = productRepository;
    }

    public async Task<Product> CreateProductAsync(CreateProductDto dto)
    {
        await _unitOfWork.BeginTransactionAsync();
        
        try
        {
            var product = new Product(Guid.NewGuid(), dto.Name, dto.Description);
            await _productRepository.AddAsync(product);
            
            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();
            
            return product;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }
}
```

### 示例 2: 使用工作单元管理器

```csharp
public class OrderService
{
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IRepository<Order, Guid> _orderRepository;

    public OrderService(IUnitOfWorkManager uowManager, IRepository<Order, Guid> orderRepository)
    {
        _uowManager = uowManager;
        _orderRepository = orderRepository;
    }

    public async Task<Order> CreateOrderAsync(CreateOrderDto dto)
    {
        // 自动管理事务生命周期
        return await _uowManager.ExecuteAsync(async uow =>
        {
            var order = new Order(dto.CustomerId, dto.Items);
            await _orderRepository.AddAsync(order);
            await uow.SaveChangesAsync();
            return order;
        });
    }

    // 使用特定的 ORM
    public async Task<Order> CreateOrderWithSqlSugarAsync(CreateOrderDto dto)
    {
        return await _uowManager.ExecuteAsync(async uow =>
        {
            var order = new Order(dto.CustomerId, dto.Items);
            await _orderRepository.AddAsync(order);
            await uow.SaveChangesAsync();
            return order;
        }, OrmProvider.SqlSugar);
    }
}
```

### 示例 3: 审计实体自动填充

```csharp
[Entity(GenerateRepository = true, GenerateAuditing = true)]
public class Product : FullyAuditedAggregateRoot<Guid>
{
    public string Name { get; private set; }
    public decimal Price { get; private set; }
    
    public Product(Guid id, string name, decimal price)
    {
        Id = id;
        Name = name;
        Price = price;
        // CreationTime 和 CreatorId 会自动填充
    }
    
    public void UpdatePrice(decimal newPrice)
    {
        Price = newPrice;
        // LastModificationTime 和 LastModifierId 会自动填充
    }
}

// 使用示例
public async Task<Product> CreateProductAsync()
{
    var product = new Product(Guid.NewGuid(), "Test Product", 99.99m);
    await _productRepository.AddAsync(product);
    await _unitOfWork.SaveChangesAsync();
    
    // product.CreationTime 已自动设置为当前时间
    // product.CreatorId 已自动设置为当前用户ID
    
    return product;
}
```

### 示例 4: 软删除

```csharp
public async Task DeleteProductAsync(Guid productId)
{
    var product = await _productRepository.GetByIdAsync(productId);
    
    // 调用 DeleteAsync，实际会转换为软删除
    await _productRepository.DeleteAsync(product);
    await _unitOfWork.SaveChangesAsync();
    
    // product.IsDeleted = true
    // product.DeletionTime = 当前时间
    // product.DeleterId = 当前用户ID
}

// 查询时自动过滤软删除的数据
public async Task<List<Product>> GetActiveProductsAsync()
{
    // 只返回 IsDeleted = false 的数据
    return await _productRepository.GetAllAsync();
}
```

### 示例 5: 多 ORM 切换

```csharp
public class MultiOrmService
{
    private readonly IUnitOfWorkFactory _factory;

    public MultiOrmService(IUnitOfWorkFactory factory)
    {
        _factory = factory;
    }

    public async Task PerformOperationWithDifferentOrms()
    {
        // 使用 EF Core
        using (var efUow = _factory.Create(OrmProvider.EfCore))
        {
            await efUow.BeginTransactionAsync();
            // ... 执行操作
            await efUow.CommitTransactionAsync();
        }

        // 使用 SqlSugar
        using (var sqlSugarUow = _factory.Create(OrmProvider.SqlSugar))
        {
            await sqlSugarUow.BeginTransactionAsync();
            // ... 执行操作
            await sqlSugarUow.CommitTransactionAsync();
        }

        // 使用 FreeSql
        using (var freeSqlUow = _factory.Create(OrmProvider.FreeSql))
        {
            await freeSqlUow.BeginTransactionAsync();
            // ... 执行操作
            await freeSqlUow.CommitTransactionAsync();
        }
    }
}
```

## 🔧 自定义当前用户提供者

```csharp
public class HttpContextCurrentUserProvider : ICurrentUserProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? GetCurrentUserId()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User
            ?.FindFirst(ClaimTypes.NameIdentifier);
            
        if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
        {
            return userId;
        }

        return null;
    }
}

// 注册
services.AddHttpContextAccessor();
services.AddScoped<ICurrentUserProvider, HttpContextCurrentUserProvider>();
```

## 📊 架构图

```
┌─────────────────────────────────────────────────────┐
│                  Application Layer                   │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────┐ │
│  │   Service    │  │   Service    │  │  Service  │ │
│  └──────┬───────┘  └──────┬───────┘  └─────┬─────┘ │
└─────────┼──────────────────┼────────────────┼───────┘
          │                  │                │
          ▼                  ▼                ▼
┌─────────────────────────────────────────────────────┐
│              IUnitOfWorkManager / IUnitOfWork        │
└─────────────────────────────────────────────────────┘
          │                  │                │
    ┌─────┴─────┐      ┌─────┴─────┐    ┌───┴────┐
    ▼           ▼      ▼           ▼    ▼        ▼
┌────────┐ ┌─────────┐ ┌──────────┐ ┌─────────┐
│EfCore  │ │SqlSugar │ │ FreeSql  │ │Auditing │
│  UoW   │ │  UoW    │ │   UoW    │ │  Filter │
└────────┘ └─────────┘ └──────────┘ └─────────┘
```

## ⚙️ 高级配置

### 配置事务隔离级别（EF Core）

```csharp
public class CustomEfCoreUnitOfWork : EfCoreUnitOfWork
{
    public CustomEfCoreUnitOfWork(DbContext dbContext) : base(dbContext)
    {
    }

    public async Task BeginTransactionAsync(IsolationLevel isolationLevel)
    {
        if (_currentTransaction != null)
        {
            throw new InvalidOperationException("Transaction already in progress");
        }

        _currentTransaction = await _dbContext.Database
            .BeginTransactionAsync(isolationLevel);
    }
}
```

### 嵌套事务处理

```csharp
public async Task NestedTransactionExample()
{
    await _unitOfWork.BeginTransactionAsync();
    
    try
    {
        // 外层事务操作
        await _productRepository.AddAsync(product1);
        
        // 内层操作（使用同一个事务）
        await ProcessOrderAsync();
        
        await _unitOfWork.CommitTransactionAsync();
    }
    catch
    {
        await _unitOfWork.RollbackTransactionAsync();
        throw;
    }
}
```

## 🧪 测试

```csharp
public class ProductServiceTests
{
    [Fact]
    public async Task CreateProduct_ShouldFillAuditFields()
    {
        // Arrange
        var mockUserProvider = new Mock<ICurrentUserProvider>();
        mockUserProvider.Setup(x => x.GetCurrentUserId())
            .Returns(Guid.Parse("11111111-1111-1111-1111-111111111111"));
            
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase("TestDb")
            .AddInterceptors(new AuditInterceptor(mockUserProvider.Object))
            .Options;
            
        using var context = new TestDbContext(options);
        var uow = new EfCoreUnitOfWork(context);
        
        // Act
        var product = new Product(Guid.NewGuid(), "Test", 99.99m);
        context.Products.Add(product);
        await uow.SaveChangesAsync();
        
        // Assert
        Assert.NotEqual(default, product.CreationTime);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), product.CreatorId);
    }
}
```

## 📝 最佳实践

1. **始终使用 `using` 或 `try-finally` 确保资源释放**
```csharp
using (var uow = _factory.Create(OrmProvider.EfCore))
{
    // 操作
}
```

2. **使用 UnitOfWorkManager 简化事务管理**
```csharp
await _uowManager.ExecuteAsync(async uow => 
{
    // 自动处理事务
});
```

3. **实现自定义的 ICurrentUserProvider**
```csharp
// 不要使用默认实现，实现真实的用户获取逻辑
services.AddScoped<ICurrentUserProvider, HttpContextCurrentUserProvider>();
```

4. **为不同场景选择合适的 ORM**
- EF Core: 复杂查询、导航属性
- SqlSugar: 高性能、批量操作
- FreeSql: CodeFirst、跨数据库

## 🔗 相关链接

- [EF Core 文档](https://docs.microsoft.com/ef/core/)
- [SqlSugar 文档](https://www.donet5.com/Home/Doc)
- [FreeSql 文档](https://freesql.net/)

---

**完成日期**: 2025年10月30日  
**版本**: 1.0.0
