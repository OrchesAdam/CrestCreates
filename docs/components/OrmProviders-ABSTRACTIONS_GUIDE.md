# ORM 抽象层详细文档

## 核心接口详解

### 1. 实体接口 (IEntity)

定义了实体的基础契约，包括主键、审计、软删除等功能。

#### 接口层次结构

```
IEntity (标记接口)
├── IEntity<TKey> (带主键)
├── IAuditedEntity (审计实体)
├── ISoftDelete (软删除)
├── IFullyAuditedEntity (完整审计 = IAuditedEntity + ISoftDelete)
└── IMultiTenant (多租户)
```

#### 使用示例

```csharp
// 基础实体
public class Product : IEntity<Guid>, IAuditedEntity, IMultiTenant
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    
    // 审计字段
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
    
    // 多租户字段
    public Guid? TenantId { get; set; }
}

// 完整审计实体（包含软删除）
public class Order : IEntity<Guid>, IFullyAuditedEntity
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; }
    
    // 审计字段
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
    
    // 软删除字段
    public bool IsDeleted { get; set; }
    public DateTime? DeletionTime { get; set; }
    public Guid? DeleterId { get; set; }
}
```

### 2. 数据库上下文接口 (IDbContext)

提供对数据库上下文的统一抽象。

#### 核心方法

| 方法 | 说明 | EF Core 实现 | FreeSql 实现 | SqlSugar 实现 |
|------|------|-------------|-------------|--------------|
| `Set<TEntity>()` | 获取实体集 | `DbContext.Set<T>()` | `IFreeSql.Select<T>()` | `ISqlSugarClient.Queryable<T>()` |
| `Queryable<TEntity>()` | 获取查询构建器 | `DbSet<T>.AsQueryable()` | `ISelect<T>` | `ISugarQueryable<T>` |
| `SaveChangesAsync()` | 保存更改 | `DbContext.SaveChangesAsync()` | `UnitOfWork.CommitAsync()` | `ITenant.CommitAsync()` |
| `BeginTransactionAsync()` | 开始事务 | `Database.BeginTransactionAsync()` | `Transaction.Begin()` | `BeginTran()` |

#### 使用示例

```csharp
public class ProductService
{
    private readonly IDbContext _dbContext;

    public ProductService(IDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Product> CreateProductAsync(Product product)
    {
        var productSet = _dbContext.Set<Product>();
        await productSet.AddAsync(product);
        await _dbContext.SaveChangesAsync();
        return product;
    }

    public async Task<List<Product>> GetExpensiveProductsAsync()
    {
        return await _dbContext.Queryable<Product>()
            .Where(p => p.Price > 1000)
            .OrderByDescending(p => p.Price)
            .ToListAsync();
    }
}
```

### 3. 仓储接口 (IRepository)

提供完整的 CRUD 操作。

#### 接口层次结构

```
IRepository<TEntity>
├── 查询操作：GetAllAsync, FindAsync, FirstOrDefaultAsync, AnyAsync, CountAsync
├── 添加操作：AddAsync, AddRangeAsync
├── 更新操作：UpdateAsync, UpdateRangeAsync
├── 删除操作：DeleteAsync, DeleteRangeAsync
└── 高级查询：AsQueryable()

IRepository<TEntity, TKey> : IRepository<TEntity>
├── GetByIdAsync(TKey id)
├── GetByIdsAsync(IEnumerable<TKey> ids)
├── DeleteByIdAsync(TKey id)
└── DeleteByIdsAsync(IEnumerable<TKey> ids)

IReadOnlyRepository<TEntity>
└── 仅包含查询操作

IReadOnlyRepository<TEntity, TKey> : IReadOnlyRepository<TEntity>
└── GetByIdAsync(TKey id), GetByIdsAsync(IEnumerable<TKey> ids)
```

#### 使用示例

```csharp
// 基础 CRUD
public class ProductService
{
    private readonly IRepository<Product, Guid> _repository;

    public async Task<Product> GetProductAsync(Guid id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<List<Product>> SearchProductsAsync(string keyword)
    {
        return await _repository.FindAsync(p => p.Name.Contains(keyword));
    }

    public async Task<Product> CreateProductAsync(Product product)
    {
        return await _repository.AddAsync(product);
    }

    public async Task UpdateProductPriceAsync(Guid id, decimal newPrice)
    {
        var product = await _repository.GetByIdAsync(id);
        product.Price = newPrice;
        await _repository.UpdateAsync(product);
    }

    public async Task DeleteProductAsync(Guid id)
    {
        await _repository.DeleteByIdAsync(id);
    }

    // 批量操作
    public async Task<int> DeleteExpiredProductsAsync()
    {
        return await _repository.DeleteAsync(p => p.ExpiryDate < DateTime.Now);
    }

    public async Task<int> ImportProductsAsync(List<Product> products)
    {
        return await _repository.AddRangeAsync(products);
    }
}

// 只读仓储（用于查询服务）
public class ProductQueryService
{
    private readonly IReadOnlyRepository<Product, Guid> _repository;

    public ProductQueryService(IReadOnlyRepository<Product, Guid> repository)
    {
        _repository = repository;
    }

    public async Task<List<Product>> GetAllProductsAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<bool> ProductExistsAsync(string name)
    {
        return await _repository.AnyAsync(p => p.Name == name);
    }

    public async Task<int> GetTotalProductCountAsync()
    {
        return await _repository.CountAsync();
    }
}
```

### 4. 查询构建器接口 (IQueryableBuilder)

提供流式 API 进行复杂查询。

#### 功能分类

##### 过滤条件

```csharp
// 基础 Where
var products = await repository.AsQueryable()
    .Where(p => p.Price > 100)
    .Where(p => p.Stock > 0)
    .ToListAsync();

// 条件性 Where
var query = repository.AsQueryable()
    .WhereIf(!string.IsNullOrEmpty(keyword), p => p.Name.Contains(keyword))
    .WhereIf(minPrice.HasValue, p => p.Price >= minPrice.Value);
```

##### 排序

```csharp
// 单一排序
var products = await repository.AsQueryable()
    .OrderBy(p => p.Name)
    .ToListAsync();

// 多级排序
var products = await repository.AsQueryable()
    .OrderBy(p => p.CategoryId)
    .ThenByDescending(p => p.Price)
    .ThenBy(p => p.Name)
    .ToListAsync();
```

##### 分页

```csharp
// 方式 1：Skip + Take
var products = await repository.AsQueryable()
    .OrderBy(p => p.Name)
    .Skip(pageIndex * pageSize)
    .Take(pageSize)
    .ToListAsync();

// 方式 2：Page 方法
var products = await repository.AsQueryable()
    .OrderBy(p => p.Name)
    .Page(pageIndex, pageSize)
    .ToListAsync();

// 方式 3：获取分页结果（包含总数）
var pagedResult = await repository.AsQueryable()
    .OrderBy(p => p.Name)
    .ToPagedResultAsync(pageIndex, pageSize);

Console.WriteLine($"总记录数: {pagedResult.TotalCount}");
Console.WriteLine($"总页数: {pagedResult.TotalPages}");
Console.WriteLine($"当前页: {pagedResult.PageIndex + 1}");
foreach (var product in pagedResult.Items)
{
    Console.WriteLine(product.Name);
}
```

##### 关联查询

```csharp
// Include 导航属性
var products = await repository.AsQueryable()
    .Include(p => p.Category)
    .Include(p => p.Supplier)
    .ToListAsync();

// 多级 Include
var orders = await orderRepository.AsQueryable()
    .Include(o => o.Customer)
    .ThenInclude<Order, Address>(c => c.Address)
    .Include(o => o.Items)
    .ToListAsync();

// 字符串路径
var products = await repository.AsQueryable()
    .Include("Category")
    .Include("Category.Parent")
    .ToListAsync();
```

##### 投影

```csharp
// Select 投影
var productDtos = await repository.AsQueryable()
    .Select(p => new ProductDto
    {
        Id = p.Id,
        Name = p.Name,
        Price = p.Price,
        CategoryName = p.Category.Name
    })
    .ToListAsync();
```

##### 高级功能

```csharp
// 禁用跟踪（只读查询，性能更好）
var products = await repository.AsQueryable()
    .AsNoTracking()
    .ToListAsync();

// 忽略查询过滤器（包括软删除过滤器）
var allProducts = await repository.AsQueryable()
    .IgnoreQueryFilters()
    .ToListAsync();

// 去重
var categories = await productRepository.AsQueryable()
    .Select(p => p.CategoryId)
    .Distinct()
    .ToListAsync();
```

##### 执行查询

```csharp
// ToListAsync - 获取列表
var list = await query.ToListAsync();

// FirstAsync - 获取第一个（不存在则抛异常）
var first = await query.FirstAsync();

// FirstOrDefaultAsync - 获取第一个或默认值
var firstOrDefault = await query.FirstOrDefaultAsync();

// SingleAsync - 获取唯一一个（不存在或多个则抛异常）
var single = await query.Where(p => p.Id == id).SingleAsync();

// SingleOrDefaultAsync - 获取唯一一个或默认值
var singleOrDefault = await query.Where(p => p.Code == code).SingleOrDefaultAsync();

// AnyAsync - 检查是否存在
bool exists = await query.AnyAsync();
bool hasExpensive = await query.AnyAsync(p => p.Price > 1000);

// CountAsync - 获取数量
int count = await query.CountAsync();
long longCount = await query.LongCountAsync();

// ToPagedResultAsync - 分页查询
var pagedResult = await query.ToPagedResultAsync(0, 20);
```

#### 完整示例

```csharp
public class ProductSearchService
{
    private readonly IRepository<Product, Guid> _repository;

    public async Task<PagedResult<ProductDto>> SearchProductsAsync(
        string keyword,
        Guid? categoryId,
        decimal? minPrice,
        decimal? maxPrice,
        bool? inStock,
        string sortBy,
        bool descending,
        int pageIndex,
        int pageSize)
    {
        var query = _repository.AsQueryable()
            .WhereIf(!string.IsNullOrEmpty(keyword), p => p.Name.Contains(keyword) || p.Description.Contains(keyword))
            .WhereIf(categoryId.HasValue, p => p.CategoryId == categoryId.Value)
            .WhereIf(minPrice.HasValue, p => p.Price >= minPrice.Value)
            .WhereIf(maxPrice.HasValue, p => p.Price <= maxPrice.Value)
            .WhereIf(inStock.HasValue && inStock.Value, p => p.Stock > 0)
            .Include(p => p.Category);

        // 动态排序
        query = sortBy?.ToLower() switch
        {
            "name" => descending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
            "price" => descending ? query.OrderByDescending(p => p.Price) : query.OrderBy(p => p.Price),
            "date" => descending ? query.OrderByDescending(p => p.CreationTime) : query.OrderBy(p => p.CreationTime),
            _ => query.OrderBy(p => p.Name)
        };

        // 执行分页查询并投影
        var result = await query
            .Select(p => new ProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                Stock = p.Stock,
                CategoryName = p.Category.Name
            })
            .ToPagedResultAsync(pageIndex, pageSize);

        return result;
    }
}
```

### 5. 事务接口 (IDbTransaction, ITransactionManager)

提供统一的事务管理。

#### 基础事务使用

```csharp
public class OrderService
{
    private readonly IDbContext _dbContext;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<Product, Guid> _productRepository;

    public async Task CreateOrderAsync(Order order)
    {
        // 方式 1：手动管理事务
        IDbTransaction transaction = null;
        try
        {
            transaction = await _dbContext.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            
            // 创建订单
            await _orderRepository.AddAsync(order);
            
            // 更新库存
            foreach (var item in order.Items)
            {
                var product = await _productRepository.GetByIdAsync(item.ProductId);
                product.Stock -= item.Quantity;
                await _productRepository.UpdateAsync(product);
            }
            
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }
}
```

#### 使用事务管理器

```csharp
public class OrderService
{
    private readonly ITransactionManager _transactionManager;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<Product, Guid> _productRepository;

    public async Task CreateOrderAsync(Order order)
    {
        // 方式 2：使用事务管理器（推荐）
        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            await _orderRepository.AddAsync(order);
            
            foreach (var item in order.Items)
            {
                var product = await _productRepository.GetByIdAsync(item.ProductId);
                product.Stock -= item.Quantity;
                await _productRepository.UpdateAsync(product);
            }
        }, IsolationLevel.ReadCommitted);
    }

    public async Task<OrderSummary> GetOrderSummaryAsync(Guid orderId)
    {
        // 带返回值的事务
        return await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            // ... 其他操作
            return new OrderSummary { /* ... */ };
        });
    }
}
```

#### 嵌套事务

```csharp
public class ComplexService
{
    private readonly ITransactionManager _transactionManager;

    public async Task ComplexOperationAsync()
    {
        // 外层事务
        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            await DoSomethingAsync();
            
            // 内层事务（根据实现可能是嵌套事务或保存点）
            await _transactionManager.ExecuteInTransactionAsync(async () =>
            {
                await DoAnotherThingAsync();
            }, IsolationLevel.Serializable);
            
            await DoFinalThingAsync();
        }, IsolationLevel.ReadCommitted);
    }
}
```

### 6. 增强的工作单元接口 (IUnitOfWorkEnhanced)

提供完整的工作单元模式实现。

#### 基础使用

```csharp
public class OrderService
{
    private readonly IUnitOfWorkEnhanced _unitOfWork;

    public async Task CreateOrderAsync(Order order)
    {
        // 开始工作单元
        await _unitOfWork.BeginTransactionAsync();

        try
        {
            // 获取仓储
            var orderRepo = _unitOfWork.GetRepository<Order, Guid>();
            var productRepo = _unitOfWork.GetRepository<Product, Guid>();
            
            // 执行操作
            await orderRepo.AddAsync(order);
            
            foreach (var item in order.Items)
            {
                var product = await productRepo.GetByIdAsync(item.ProductId);
                product.Stock -= item.Quantity;
                await productRepo.UpdateAsync(product);
            }
            
            // 提交
            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }
}
```

#### 多租户支持

```csharp
public class TenantService
{
    private readonly IUnitOfWorkEnhanced _unitOfWork;

    public async Task<List<Product>> GetTenantProductsAsync(Guid tenantId)
    {
        // 设置租户上下文
        _unitOfWork.SetTenantId(tenantId);
        _unitOfWork.EnableMultiTenancyFilter();
        
        var repository = _unitOfWork.GetRepository<Product, Guid>();
        
        // 自动过滤当前租户的数据
        return await repository.GetAllAsync();
    }

    public async Task<List<Product>> GetAllProductsIncludingOtherTenantsAsync()
    {
        // 禁用多租户过滤器，查看所有数据
        _unitOfWork.DisableMultiTenancyFilter();
        
        var repository = _unitOfWork.GetRepository<Product, Guid>();
        return await repository.GetAllAsync();
    }
}
```

#### 软删除支持

```csharp
public class ProductService
{
    private readonly IUnitOfWorkEnhanced _unitOfWork;

    public async Task<List<Product>> GetActiveProductsAsync()
    {
        // 启用软删除过滤器（默认启用）
        _unitOfWork.EnableSoftDeleteFilter();
        
        var repository = _unitOfWork.GetRepository<Product, Guid>();
        
        // 只返回未删除的产品
        return await repository.GetAllAsync();
    }

    public async Task<List<Product>> GetAllProductsIncludingDeletedAsync()
    {
        // 禁用软删除过滤器
        _unitOfWork.DisableSoftDeleteFilter();
        
        var repository = _unitOfWork.GetRepository<Product, Guid>();
        
        // 返回所有产品（包括已删除的）
        return await repository.GetAllAsync();
    }

    public async Task SoftDeleteProductAsync(Guid id)
    {
        var repository = _unitOfWork.GetRepository<Product, Guid>();
        var product = await repository.GetByIdAsync(id);
        
        if (product is ISoftDelete softDelete)
        {
            softDelete.IsDeleted = true;
            softDelete.DeletionTime = DateTime.UtcNow;
            // softDelete.DeleterId = _currentUser.Id;
            
            await repository.UpdateAsync(product);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
```

## ORM 实现映射表

### EF Core 实现

| 抽象接口 | EF Core 实现 | 说明 |
|---------|-------------|------|
| `IDbContext` | `DbContext` 包装器 | 包装 `Microsoft.EntityFrameworkCore.DbContext` |
| `IDbSet<T>` | `DbSet<T>` 包装器 | 包装 EF Core 的 `DbSet<T>` |
| `IQueryableBuilder<T>` | `IQueryable<T>` 包装器 | 基于 LINQ to Entities |
| `IDbTransaction` | `IDbContextTransaction` 包装器 | 包装 EF Core 事务 |
| `IRepository<T>` | `EfCoreRepository<T>` | 基于 `DbContext` 和 `DbSet<T>` |

### FreeSql 实现

| 抽象接口 | FreeSql 实现 | 说明 |
|---------|-------------|------|
| `IDbContext` | `IFreeSql` 包装器 | 包装 `FreeSql.IFreeSql` |
| `IDbSet<T>` | `ISelect<T>` 包装器 | 基于 FreeSql 的查询 API |
| `IQueryableBuilder<T>` | `ISelect<T>` 包装器 | FreeSql 的流式查询 API |
| `IDbTransaction` | `DbTransaction` 包装器 | FreeSql 的事务管理 |
| `IRepository<T>` | `BaseRepository<T>` 继承 | 继承 FreeSql 的 `BaseRepository` 并绑定 UnitOfWorkManager |

### SqlSugar 实现

| 抽象接口 | SqlSugar 实现 | 说明 |
|---------|-------------|------|
| `IDbContext` | `ISqlSugarClient` 包装器 | 包装 `SqlSugar.ISqlSugarClient` |
| `IDbSet<T>` | `ISugarQueryable<T>` 包装器 | 基于 SqlSugar 的查询 API |
| `IQueryableBuilder<T>` | `ISugarQueryable<T>` 包装器 | SqlSugar 的流式查询 API |
| `IDbTransaction` | 自定义事务包装器 | 包装 `BeginTran` / `CommitTran` / `RollbackTran` |
| `IRepository<T>` | `SqlSugarRepository<T>` | 基于 `ISqlSugarClient` 实现 |

## 性能优化建议

### 1. 使用 AsNoTracking

对于只读查询，使用 `AsNoTracking()` 可以显著提升性能：

```csharp
// 不跟踪实体状态，性能更好
var products = await repository.AsQueryable()
    .AsNoTracking()
    .Where(p => p.Price > 100)
    .ToListAsync();
```

### 2. 批量操作

使用批量操作方法代替循环单个操作：

```csharp
// ❌ 不推荐：循环单个插入
foreach (var product in products)
{
    await repository.AddAsync(product);
}

// ✅ 推荐：批量插入
await repository.AddRangeAsync(products);
```

### 3. 选择性加载

只加载需要的字段：

```csharp
// ❌ 不推荐：加载整个实体
var products = await repository.GetAllAsync();
var names = products.Select(p => p.Name).ToList();

// ✅ 推荐：只查询需要的字段
var names = await repository.AsQueryable()
    .Select(p => p.Name)
    .ToListAsync();
```

### 4. 分页查询

对于大数据集，始终使用分页：

```csharp
// ✅ 推荐：分页查询
var pagedResult = await repository.AsQueryable()
    .OrderBy(p => p.Id)
    .ToPagedResultAsync(pageIndex, pageSize);
```

### 5. 避免 N+1 查询

使用 `Include` 预加载关联数据：

```csharp
// ❌ 不推荐：N+1 查询
var orders = await orderRepository.GetAllAsync();
foreach (var order in orders)
{
    var customer = await customerRepository.GetByIdAsync(order.CustomerId);
    // ...
}

// ✅ 推荐：预加载
var orders = await orderRepository.AsQueryable()
    .Include(o => o.Customer)
    .ToListAsync();
```

## 最佳实践

### 1. 使用依赖注入

```csharp
// Startup.cs 或 Program.cs
services.AddScoped<IRepository<Product, Guid>, ProductRepository>();
services.AddScoped<IUnitOfWorkEnhanced, UnitOfWork>();
```

### 2. 仓储模式 vs 直接使用 DbContext

```csharp
// ✅ 推荐：使用仓储（更好的抽象）
public class ProductService
{
    private readonly IRepository<Product, Guid> _repository;
    
    public ProductService(IRepository<Product, Guid> repository)
    {
        _repository = repository;
    }
}

// ⚠️ 可选：直接使用 DbContext（更灵活但耦合度高）
public class ProductService
{
    private readonly IDbContext _dbContext;
    
    public ProductService(IDbContext dbContext)
    {
        _dbContext = dbContext;
    }
}
```

### 3. 工作单元模式

对于涉及多个实体的复杂操作，使用工作单元：

```csharp
public class OrderService
{
    private readonly IUnitOfWorkEnhanced _unitOfWork;

    public async Task ProcessOrderAsync(Order order)
    {
        await _unitOfWork.BeginTransactionAsync();
        
        try
        {
            var orderRepo = _unitOfWork.GetRepository<Order, Guid>();
            var productRepo = _unitOfWork.GetRepository<Product, Guid>();
            var inventoryRepo = _unitOfWork.GetRepository<Inventory, Guid>();
            
            // 多个仓储操作在同一个事务中
            await orderRepo.AddAsync(order);
            // ... 其他操作
            
            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }
}
```

### 4. 错误处理

```csharp
public class ProductService
{
    private readonly IRepository<Product, Guid> _repository;
    private readonly ILogger<ProductService> _logger;

    public async Task<Product> GetProductAsync(Guid id)
    {
        try
        {
            var product = await _repository.GetByIdAsync(id);
            if (product == null)
            {
                throw new EntityNotFoundException($"Product with ID {id} not found");
            }
            return product;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product {ProductId}", id);
            throw;
        }
    }
}
```

## 迁移指南

### 从 EF Core 迁移

```csharp
// 之前
public class ProductService
{
    private readonly DbContext _dbContext;
    
    public async Task<List<Product>> GetProductsAsync()
    {
        return await _dbContext.Set<Product>()
            .Where(p => p.IsActive)
            .ToListAsync();
    }
}

// 之后
public class ProductService
{
    private readonly IRepository<Product, Guid> _repository;
    
    public async Task<List<Product>> GetProductsAsync()
    {
        return await _repository.AsQueryable()
            .Where(p => p.IsActive)
            .ToListAsync();
    }
}
```

### 从 FreeSql 迁移

```csharp
// 之前
public class ProductService
{
    private readonly IBaseRepository<Product> _repository;
    
    public async Task<List<Product>> GetProductsAsync()
    {
        return await _repository.Select
            .Where(p => p.IsActive)
            .ToListAsync();
    }
}

// 之后
public class ProductService
{
    private readonly IRepository<Product, Guid> _repository;
    
    public async Task<List<Product>> GetProductsAsync()
    {
        return await _repository.AsQueryable()
            .Where(p => p.IsActive)
            .ToListAsync();
    }
}
```

## 总结

通过这套统一的抽象层，您可以：

1. **轻松切换 ORM**：只需更改配置，无需修改业务代码
2. **保持代码整洁**：统一的 API 使代码更易读、易维护
3. **提高可测试性**：基于接口编程，便于单元测试
4. **支持高级功能**：多租户、软删除、审计等开箱即用
5. **性能优化**：提供多种性能优化选项

开始使用这套抽象层，让您的数据访问代码更加优雅和强大！
