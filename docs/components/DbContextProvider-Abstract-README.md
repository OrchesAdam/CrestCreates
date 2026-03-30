# CrestCreates.DbContextProvider.Abstract

## 📖 概述

本项目提供了数据库上下文的统一抽象接口，用于跨不同 ORM 框架（EF Core、FreeSql、SqlSugar）的数据库访问。

## 🎯 核心接口

### 1. IDbContext - 数据库上下文

统一的数据库上下文抽象，提供：
- 实体集访问
- 查询构建
- 事务管理
- 数据持久化

```csharp
public interface IDbContext : IDisposable
{
    IDbSet<TEntity> Set<TEntity>() where TEntity : class;
    IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    IDbTransaction CurrentTransaction { get; }
    string ConnectionString { get; }
    OrmProvider Provider { get; }
    object GetNativeContext();
}
```

### 2. IDbSet<TEntity> - 实体集

提供实体集合的基本操作：
- 添加、更新、删除
- 查找和附加
- 批量操作

```csharp
public interface IDbSet<TEntity> where TEntity : class
{
    Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    void Update(TEntity entity);
    void UpdateRange(IEnumerable<TEntity> entities);
    void Remove(TEntity entity);
    void RemoveRange(IEnumerable<TEntity> entities);
    Task<TEntity> FindAsync(params object[] keyValues);
    void Attach(TEntity entity);
    void AttachRange(IEnumerable<TEntity> entities);
}
```

### 3. IQueryableBuilder<TEntity> - 查询构建器

统一的查询构建接口，支持：
- 过滤条件（Where、WhereIf）
- 排序（OrderBy、OrderByDescending、ThenBy）
- 分页（Skip、Take、Page）
- 关联查询（Include、ThenInclude）
- 投影（Select）
- 去重（Distinct）

```csharp
public interface IQueryableBuilder<TEntity> where TEntity : class
{
    IQueryableBuilder<TEntity> Where(Expression<Func<TEntity, bool>> predicate);
    IQueryableBuilder<TEntity> WhereIf(bool condition, Expression<Func<TEntity, bool>> predicate);
    IQueryableBuilder<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector);
    IQueryableBuilder<TEntity> Skip(int count);
    IQueryableBuilder<TEntity> Take(int count);
    IQueryableBuilder<TEntity> Include<TProperty>(Expression<Func<TEntity, TProperty>> navigationPropertyPath);
    IQueryableBuilder<TResult> Select<TResult>(Expression<Func<TEntity, TResult>> selector) where TResult : class;
    Task<List<TEntity>> ToListAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<TEntity>> ToPagedResultAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    // ... 更多方法
}
```

### 4. IDbTransaction - 数据库事务

统一的事务管理接口：
- 事务提交和回滚
- 事务状态跟踪
- 隔离级别支持

```csharp
public interface IDbTransaction : IDisposable
{
    Guid TransactionId { get; }
    IsolationLevel IsolationLevel { get; }
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
    object GetNativeTransaction();
    bool IsCommitted { get; }
    bool IsRolledBack { get; }
    bool IsCompleted { get; }
}
```

### 5. ITransactionManager - 事务管理器

提供事务的创建和管理：

```csharp
public interface ITransactionManager
{
    Task<IDbTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
    IDbTransaction CurrentTransaction { get; }
    bool HasActiveTransaction { get; }
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<Task<TResult>> action,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
}
```

## 📦 ORM 提供者枚举

```csharp
public enum OrmProvider
{
    EfCore,     // Entity Framework Core
    SqlSugar,   // SqlSugar ORM
    FreeSql     // FreeSql ORM
}
```

## 🔗 依赖关系

- `CrestCreates.Domain.Shared` - 共享的领域模型

## 📝 使用说明

### 基础使用

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

    public async Task<List<Product>> SearchProductsAsync(string keyword)
    {
        return await _dbContext.Queryable<Product>()
            .Where(p => p.Name.Contains(keyword))
            .OrderByDescending(p => p.CreationTime)
            .ToListAsync();
    }
}
```

### 使用事务

```csharp
public class OrderService
{
    private readonly IDbContext _dbContext;

    public async Task CreateOrderAsync(Order order)
    {
        using var transaction = await _dbContext.BeginTransactionAsync();
        
        try
        {
            var orderSet = _dbContext.Set<Order>();
            var productSet = _dbContext.Set<Product>();
            
            await orderSet.AddAsync(order);
            
            // 更新库存
            foreach (var item in order.Items)
            {
                var product = await productSet.FindAsync(item.ProductId);
                product.Stock -= item.Quantity;
                productSet.Update(product);
            }
            
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
```

## 🚀 后续步骤

具体的 ORM 实现将在 `CrestCreates.DbContextProvider` 项目中提供：
- EF Core 实现
- FreeSql 实现
- SqlSugar 实现

## 📚 相关项目

- `CrestCreates.DbContextProvider` - DbContext 的具体实现
- `CrestCreates.OrmProviders.Abstract` - ORM 提供者的抽象层（仓储、工作单元等）
- `CrestCreates.Domain.Shared` - 共享的领域模型

---

*创建日期: 2025-11-01*
