# 仓储（Repositories）

本文档介绍 CrestCreates 框架中的仓储概念和实现。

## 概述

仓储是领域驱动设计（DDD）中的模式，用于封装数据访问逻辑，提供领域对象的持久化。仓储作为领域层和基础设施层之间的抽象，使领域层不依赖于具体的数据访问技术。

## 核心概念

### 什么是仓储？

仓储是领域对象的集合，提供对领域对象的增删改查操作。仓储将数据访问逻辑从领域层分离出来，使领域层专注于业务逻辑。

### 仓储的特点

1. **抽象数据访问**：隐藏数据访问细节
2. **领域对象集合**：像操作内存集合一样操作数据库
3. **可测试性**：便于单元测试，可以使用内存仓储替代真实仓储
4. **关注点分离**：将数据访问逻辑与业务逻辑分离

## 仓储接口

### IRepository<TEntity, TId>

`IRepository<TEntity, TId>` 是仓储的基础接口，定义了基本的 CRUD 操作。

```csharp
public interface IRepository<TEntity, TId> where TEntity : class, IEntity<TId>
{
    /// <summary>
    /// 获取 IQueryable
    /// </summary>
    IQueryable<TEntity> GetQueryable();
    
    /// <summary>
    /// 根据 ID 获取实体
    /// </summary>
    Task<TEntity> GetAsync(TId id);
    
    /// <summary>
    /// 根据 ID 查找实体（可能返回 null）
    /// </summary>
    Task<TEntity> FindAsync(TId id);
    
    /// <summary>
    /// 插入实体
    /// </summary>
    Task<TEntity> InsertAsync(TEntity entity);
    
    /// <summary>
    /// 更新实体
    /// </summary>
    Task<TEntity> UpdateAsync(TEntity entity);
    
    /// <summary>
    /// 删除实体
    /// </summary>
    Task DeleteAsync(TId id);
    Task DeleteAsync(TEntity entity);
    
    /// <summary>
    /// 获取所有实体
    /// </summary>
    Task<List<TEntity>> GetListAsync();
    
    /// <summary>
    /// 根据条件获取实体列表
    /// </summary>
    Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate);
    
    /// <summary>
    /// 根据条件获取单个实体
    /// </summary>
    Task<TEntity> SingleAsync(Expression<Func<TEntity, bool>> predicate);
    
    /// <summary>
    /// 根据条件获取第一个实体（可能返回 null）
    /// </summary>
    Task<TEntity> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate);
    
    /// <summary>
    /// 根据条件统计数量
    /// </summary>
    Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate = null);
    
    /// <summary>
    /// 根据条件判断是否存在
    /// </summary>
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate);
}
```

## 创建仓储

### 1. 定义仓储接口

在领域层定义仓储接口：

```csharp
public interface IProductRepository : IRepository<Product, Guid>
{
    /// <summary>
    /// 根据名称查找产品
    /// </summary>
    Task<Product> FindByNameAsync(string name);
    
    /// <summary>
    /// 获取价格范围内的产品
    /// </summary>
    Task<List<Product>> GetProductsByPriceRangeAsync(decimal minPrice, decimal maxPrice);
    
    /// <summary>
    /// 获取热销产品
    /// </summary>
    Task<List<Product>> GetBestSellingProductsAsync(int count);
    
    /// <summary>
    /// 更新产品库存
    /// </summary>
    Task UpdateStockAsync(Guid productId, int quantity);
}
```

### 2. 实现仓储

在基础设施层实现仓储接口：

#### EF Core 实现

```csharp
public class ProductRepository : EfCoreRepository<Product, Guid>, IProductRepository
{
    public ProductRepository(IUnitOfWork unitOfWork) : base(unitOfWork)
    {
    }
    
    public async Task<Product> FindByNameAsync(string name)
    {
        return await DbSet
            .FirstOrDefaultAsync(p => p.Name == name);
    }
    
    public async Task<List<Product>> GetProductsByPriceRangeAsync(decimal minPrice, decimal maxPrice)
    {
        return await DbSet
            .Where(p => p.Price >= minPrice && p.Price <= maxPrice)
            .OrderBy(p => p.Price)
            .ToListAsync();
    }
    
    public async Task<List<Product>> GetBestSellingProductsAsync(int count)
    {
        return await DbSet
            .OrderByDescending(p => p.SalesCount)
            .Take(count)
            .ToListAsync();
    }
    
    public async Task UpdateStockAsync(Guid productId, int quantity)
    {
        await DbSet
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.StockQuantity, quantity));
    }
}
```

#### FreeSql 实现

```csharp
public class ProductRepository : FreeSqlRepositoryBase<Product, Guid>, IProductRepository
{
    public ProductRepository(IFreeSql freeSql) : base(freeSql)
    {
    }
    
    public async Task<Product> FindByNameAsync(string name)
    {
        return await FreeSql.Select<Product>()
            .Where(p => p.Name == name)
            .FirstAsync();
    }
    
    public async Task<List<Product>> GetProductsByPriceRangeAsync(decimal minPrice, decimal maxPrice)
    {
        return await FreeSql.Select<Product>()
            .Where(p => p.Price >= minPrice && p.Price <= maxPrice)
            .OrderBy(p => p.Price)
            .ToListAsync();
    }
    
    public async Task<List<Product>> GetBestSellingProductsAsync(int count)
    {
        return await FreeSql.Select<Product>()
            .OrderByDescending(p => p.SalesCount)
            .Take(count)
            .ToListAsync();
    }
    
    public async Task UpdateStockAsync(Guid productId, int quantity)
    {
        await FreeSql.Update<Product>()
            .Set(p => p.StockQuantity, quantity)
            .Where(p => p.Id == productId)
            .ExecuteAffrowsAsync();
    }
}
```

### 3. 注册仓储

在模块中注册仓储：

```csharp
public class ProductModule : ModuleBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        // 注册仓储
        services.AddScoped<IProductRepository, ProductRepository>();
    }
}
```

## 使用仓储

### 在应用服务中使用

```csharp
public class ProductService : ApplicationService, IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    
    public ProductService(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }
    
    public async Task<ProductDto> GetAsync(Guid id)
    {
        var product = await _productRepository.GetAsync(id);
        return ObjectMapper.Map<Product, ProductDto>(product);
    }
    
    public async Task<ProductDto> CreateAsync(CreateProductInput input)
    {
        var product = new Product(input.Name, input.Price, input.Description);
        await _productRepository.InsertAsync(product);
        await _unitOfWork.SaveChangesAsync();
        
        return ObjectMapper.Map<Product, ProductDto>(product);
    }
    
    public async Task<List<ProductDto>> GetProductsByPriceRangeAsync(
        decimal minPrice, 
        decimal maxPrice)
    {
        var products = await _productRepository
            .GetProductsByPriceRangeAsync(minPrice, maxPrice);
        return ObjectMapper.Map<List<Product>, List<ProductDto>>(products);
    }
}
```

### 复杂查询

```csharp
public async Task<PagedResult<ProductDto>> GetPagedProductsAsync(
    GetProductListInput input)
{
    var query = _productRepository.GetQueryable()
        .WhereIf(!string.IsNullOrEmpty(input.Name), 
            p => p.Name.Contains(input.Name))
        .WhereIf(input.MinPrice.HasValue, 
            p => p.Price >= input.MinPrice.Value)
        .WhereIf(input.MaxPrice.HasValue, 
            p => p.Price <= input.MaxPrice.Value);
    
    var totalCount = await query.CountAsync();
    
    var products = await query
        .OrderBy(input.Sorting ?? "Name")
        .PageBy(input)
        .ToListAsync();
    
    return new PagedResult<ProductDto>
    {
        TotalCount = totalCount,
        Items = ObjectMapper.Map<List<Product>, List<ProductDto>>(products)
    };
}
```

## 仓储基类

### EfCoreRepository

```csharp
public abstract class EfCoreRepository<TEntity, TId> : IRepository<TEntity, TId>
    where TEntity : class, IEntity<TId>
{
    protected readonly DbContext DbContext;
    protected readonly DbSet<TEntity> DbSet;
    
    protected EfCoreRepository(IUnitOfWork unitOfWork)
    {
        DbContext = unitOfWork.GetDbContext();
        DbSet = DbContext.Set<TEntity>();
    }
    
    public virtual IQueryable<TEntity> GetQueryable()
    {
        return DbSet.AsQueryable();
    }
    
    public virtual async Task<TEntity> GetAsync(TId id)
    {
        var entity = await FindAsync(id);
        if (entity == null)
            throw new EntityNotFoundException(typeof(TEntity), id);
        return entity;
    }
    
    public virtual async Task<TEntity> FindAsync(TId id)
    {
        return await DbSet.FindAsync(id);
    }
    
    public virtual async Task<TEntity> InsertAsync(TEntity entity)
    {
        await DbSet.AddAsync(entity);
        return entity;
    }
    
    public virtual Task<TEntity> UpdateAsync(TEntity entity)
    {
        DbContext.Entry(entity).State = EntityState.Modified;
        return Task.FromResult(entity);
    }
    
    public virtual async Task DeleteAsync(TId id)
    {
        var entity = await FindAsync(id);
        if (entity != null)
        {
            await DeleteAsync(entity);
        }
    }
    
    public virtual Task DeleteAsync(TEntity entity)
    {
        DbSet.Remove(entity);
        return Task.CompletedTask;
    }
    
    // ... 其他方法实现
}
```

## 最佳实践

### 1. 仓储接口定义在领域层

仓储接口应该定义在领域层，实现定义在基础设施层：

```csharp
// 领域层
public interface IProductRepository : IRepository<Product, Guid>
{
    Task<Product> FindByNameAsync(string name);
}

// 基础设施层
public class ProductRepository : EfCoreRepository<Product, Guid>, IProductRepository
{
    // 实现
}
```

### 2. 仓储方法命名规范

使用清晰的命名：

```csharp
// 好的实践
Task<Product> FindByNameAsync(string name);
Task<List<Product>> GetProductsByCategoryAsync(Guid categoryId);
Task<bool> ExistsByNameAsync(string name);

// 避免
Task<Product> GetProduct(string name);
Task<List<Product>> GetList(Guid categoryId);
```

### 3. 避免在仓储中编写业务逻辑

仓储只负责数据访问，不包含业务逻辑：

```csharp
// 好的实践
public async Task<Product> FindByNameAsync(string name)
{
    return await DbSet.FirstOrDefaultAsync(p => p.Name == name);
}

// 避免：包含业务逻辑
public async Task<Product> FindValidProductAsync(string name)
{
    var product = await DbSet.FirstOrDefaultAsync(p => p.Name == name);
    if (product != null && product.StockQuantity > 0 && product.IsActive)
    {
        return product;
    }
    return null;
}
```

### 4. 使用工作单元管理事务

不要在仓储中管理事务，使用工作单元：

```csharp
// 好的实践
public async Task<OrderDto> CreateOrderAsync(CreateOrderInput input)
{
    using var transaction = await _unitOfWork.BeginTransactionAsync();
    
    try
    {
        var order = new Order(input.CustomerId);
        await _orderRepository.InsertAsync(order);
        
        // 更新库存
        await _productRepository.UpdateStockAsync(productId, newStock);
        
        await _unitOfWork.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

### 5. 使用异步方法

所有仓储方法都应该是异步的：

```csharp
// 好的实践
Task<Product> GetAsync(Guid id);
Task<List<Product>> GetListAsync();

// 避免
Product Get(Guid id);
List<Product> GetList();
```

### 6. 返回领域对象

仓储应该返回领域对象，而不是 DTO：

```csharp
// 好的实践
Task<Product> GetAsync(Guid id);

// 避免
Task<ProductDto> GetAsync(Guid id);
```

## 相关文档

- [实体](00-entities.md) - 实体详解
- [工作单元](04-unit-of-work.md) - 工作单元详解
- [ORM 提供程序](../03-infrastructure/01-orm-providers/) - ORM 提供程序
- [领域驱动设计](../01-architecture/01-domain-driven-design.md) - DDD 设计原则
