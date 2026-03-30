# ORM 抽象层 - 接口索引

## 📚 文档导航

- [README.md](./README.md) - 项目概述和快速开始
- [ABSTRACTIONS_GUIDE.md](./ABSTRACTIONS_GUIDE.md) - 详细的接口使用指南
- [ARCHITECTURE.md](./ARCHITECTURE.md) - 架构设计文档
- 本文档 - 接口快速索引

## 🎯 核心接口速查

### 实体接口

| 接口 | 文件 | 用途 |
|------|------|------|
| `IEntity` | [IEntity.cs](./Abstractions/IEntity.cs) | 实体标记接口 |
| `IEntity<TKey>` | [IEntity.cs](./Abstractions/IEntity.cs) | 带主键的实体接口 |
| `IAuditedEntity` | [IEntity.cs](./Abstractions/IEntity.cs) | 审计实体接口 |
| `ISoftDelete` | [IEntity.cs](./Abstractions/IEntity.cs) | 软删除接口 |
| `IFullyAuditedEntity` | [IEntity.cs](./Abstractions/IEntity.cs) | 完整审计实体接口 |
| `IMultiTenant` | [IEntity.cs](./Abstractions/IEntity.cs) | 多租户接口 |

**快速示例：**
```csharp
public class Product : IEntity<Guid>, IAuditedEntity, IMultiTenant
{
    public Guid Id { get; set; }
    public DateTime CreationTime { get; set; }
    public Guid? TenantId { get; set; }
}
```

---

### 数据库上下文接口

| 接口 | 文件 | 用途 |
|------|------|------|
| `IDbContext` | [IDbContext.cs](./Abstractions/IDbContext.cs) | 数据库上下文抽象 |
| `IDbSet<TEntity>` | [IDbSet.cs](./Abstractions/IDbSet.cs) | 实体集抽象 |

**核心方法：**
```csharp
IDbSet<TEntity> Set<TEntity>()
IQueryableBuilder<TEntity> Queryable<TEntity>()
Task<int> SaveChangesAsync()
Task<IDbTransaction> BeginTransactionAsync()
```

**快速示例：**
```csharp
var productSet = _dbContext.Set<Product>();
await productSet.AddAsync(product);
await _dbContext.SaveChangesAsync();
```

---

### 仓储接口

| 接口 | 文件 | 用途 |
|------|------|------|
| `IRepository<TEntity>` | [IRepository.cs](./Abstractions/IRepository.cs) | 基础仓储接口 |
| `IRepository<TEntity, TKey>` | [IRepository.cs](./Abstractions/IRepository.cs) | 带主键的仓储接口 |
| `IReadOnlyRepository<TEntity>` | [IRepository.cs](./Abstractions/IRepository.cs) | 只读仓储接口 |
| `IReadOnlyRepository<TEntity, TKey>` | [IRepository.cs](./Abstractions/IRepository.cs) | 带主键的只读仓储接口 |

**主要方法分类：**

#### 查询操作
```csharp
Task<List<TEntity>> GetAllAsync()
Task<TEntity> GetByIdAsync(TKey id)
Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate)
Task<TEntity> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate)
Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate)
Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate = null)
```

#### 添加操作
```csharp
Task<TEntity> AddAsync(TEntity entity)
Task<int> AddRangeAsync(IEnumerable<TEntity> entities)
```

#### 更新操作
```csharp
Task<TEntity> UpdateAsync(TEntity entity)
Task<int> UpdateRangeAsync(IEnumerable<TEntity> entities)
```

#### 删除操作
```csharp
Task DeleteAsync(TEntity entity)
Task DeleteByIdAsync(TKey id)
Task<int> DeleteRangeAsync(IEnumerable<TEntity> entities)
Task<int> DeleteAsync(Expression<Func<TEntity, bool>> predicate)
```

#### 高级查询
```csharp
IQueryableBuilder<TEntity> AsQueryable()
```

**快速示例：**
```csharp
// 查询
var product = await _repository.GetByIdAsync(id);
var products = await _repository.FindAsync(p => p.Price > 100);

// 添加
var newProduct = await _repository.AddAsync(product);

// 更新
await _repository.UpdateAsync(product);

// 删除
await _repository.DeleteByIdAsync(id);
```

---

### 查询构建器接口

| 接口 | 文件 | 用途 |
|------|------|------|
| `IQueryableBuilder<TEntity>` | [IQueryableBuilder.cs](./Abstractions/IQueryableBuilder.cs) | 查询构建器接口 |
| `PagedResult<T>` | [IQueryableBuilder.cs](./Abstractions/IQueryableBuilder.cs) | 分页结果类 |

**方法分类：**

#### 过滤
```csharp
IQueryableBuilder<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
IQueryableBuilder<TEntity> WhereIf(bool condition, Expression<Func<TEntity, bool>> predicate)
```

#### 排序
```csharp
IQueryableBuilder<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector)
IQueryableBuilder<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
IQueryableBuilder<TEntity> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector)
IQueryableBuilder<TEntity> ThenByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
```

#### 分页
```csharp
IQueryableBuilder<TEntity> Skip(int count)
IQueryableBuilder<TEntity> Take(int count)
IQueryableBuilder<TEntity> Page(int pageIndex, int pageSize)
```

#### 关联
```csharp
IQueryableBuilder<TEntity> Include<TProperty>(Expression<Func<TEntity, TProperty>> navigationPropertyPath)
IQueryableBuilder<TEntity> Include(string navigationPropertyPath)
IQueryableBuilder<TEntity> ThenInclude<TPreviousProperty, TProperty>(...)
```

#### 投影
```csharp
IQueryableBuilder<TResult> Select<TResult>(Expression<Func<TEntity, TResult>> selector)
IQueryableBuilder<TEntity> Distinct()
```

#### 执行
```csharp
Task<List<TEntity>> ToListAsync()
Task<TEntity> FirstOrDefaultAsync()
Task<int> CountAsync()
Task<PagedResult<TEntity>> ToPagedResultAsync(int pageIndex, int pageSize)
```

#### 高级
```csharp
IQueryableBuilder<TEntity> AsNoTracking()
IQueryableBuilder<TEntity> IgnoreQueryFilters()
object GetNativeQuery()
```

**快速示例：**
```csharp
var pagedResult = await _repository.AsQueryable()
    .Where(p => p.IsActive)
    .WhereIf(!string.IsNullOrEmpty(keyword), p => p.Name.Contains(keyword))
    .OrderByDescending(p => p.CreationTime)
    .Include(p => p.Category)
    .ToPagedResultAsync(pageIndex, pageSize);
```

---

### 事务接口

| 接口 | 文件 | 用途 |
|------|------|------|
| `IDbTransaction` | [IDbTransaction.cs](./Abstractions/IDbTransaction.cs) | 数据库事务接口 |
| `ITransactionManager` | [IDbTransaction.cs](./Abstractions/IDbTransaction.cs) | 事务管理器接口 |
| `TransactionOptions` | [IDbTransaction.cs](./Abstractions/IDbTransaction.cs) | 事务选项类 |
| `TransactionPropagation` | [IDbTransaction.cs](./Abstractions/IDbTransaction.cs) | 事务传播行为枚举 |

**IDbTransaction 核心成员：**
```csharp
Guid TransactionId { get; }
IsolationLevel IsolationLevel { get; }
Task CommitAsync()
Task RollbackAsync()
bool IsCommitted { get; }
bool IsRolledBack { get; }
object GetNativeTransaction()
```

**ITransactionManager 核心方法：**
```csharp
Task<IDbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = ...)
IDbTransaction CurrentTransaction { get; }
bool HasActiveTransaction { get; }
Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, ...)
Task ExecuteInTransactionAsync(Func<Task> action, ...)
```

**快速示例：**
```csharp
// 方式 1: 手动管理
var transaction = await _dbContext.BeginTransactionAsync();
try
{
    // 执行操作
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}

// 方式 2: 使用事务管理器（推荐）
await _transactionManager.ExecuteInTransactionAsync(async () =>
{
    // 执行操作
});
```

---

### 工作单元接口

| 接口 | 文件 | 用途 |
|------|------|------|
| `IUnitOfWorkEnhanced` | [IUnitOfWorkEnhanced.cs](./Abstractions/IUnitOfWorkEnhanced.cs) | 增强的工作单元接口 |
| `UnitOfWorkOptions` | [IUnitOfWorkEnhanced.cs](./Abstractions/IUnitOfWorkEnhanced.cs) | 工作单元选项类 |
| `IUnitOfWorkManager` | [IUnitOfWorkManager.cs](./Abstractions/IUnitOfWorkManager.cs) | 工作单元管理器接口 |
| `IUnitOfWorkFactory` | [IUnitOfWorkFactory.cs](./Abstractions/IUnitOfWorkFactory.cs) | 工作单元工厂接口 |

**IUnitOfWorkEnhanced 核心成员：**

#### 数据库上下文
```csharp
IDbContext DbContext { get; }
OrmProvider Provider { get; }
```

#### 仓储访问
```csharp
IRepository<TEntity> GetRepository<TEntity>()
IRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
IReadOnlyRepository<TEntity> GetReadOnlyRepository<TEntity>()
```

#### 事务管理
```csharp
Task BeginTransactionAsync(IsolationLevel isolationLevel = ...)
IDbTransaction CurrentTransaction { get; }
bool HasActiveTransaction { get; }
Task CommitTransactionAsync()
Task RollbackTransactionAsync()
```

#### 高级功能
```csharp
void EnableSoftDeleteFilter()
void DisableSoftDeleteFilter()
void EnableMultiTenancyFilter()
void DisableMultiTenancyFilter()
void SetTenantId(Guid? tenantId)
Guid? GetTenantId()
```

#### 继承自 IUnitOfWork
```csharp
Task<int> SaveChangesAsync()
void Dispose()
```

**快速示例：**
```csharp
await _unitOfWork.BeginTransactionAsync();
try
{
    var orderRepo = _unitOfWork.GetRepository<Order, Guid>();
    var productRepo = _unitOfWork.GetRepository<Product, Guid>();
    
    await orderRepo.AddAsync(order);
    await productRepo.UpdateAsync(product);
    
    await _unitOfWork.SaveChangesAsync();
    await _unitOfWork.CommitTransactionAsync();
}
catch
{
    await _unitOfWork.RollbackTransactionAsync();
    throw;
}
```

---

## 🔍 常见场景速查

### 场景 1: 简单 CRUD

```csharp
// 注入仓储
private readonly IRepository<Product, Guid> _repository;

// 查询
var product = await _repository.GetByIdAsync(id);
var all = await _repository.GetAllAsync();

// 添加
var newProduct = await _repository.AddAsync(product);

// 更新
await _repository.UpdateAsync(product);

// 删除
await _repository.DeleteByIdAsync(id);
```

### 场景 2: 复杂查询

```csharp
var result = await _repository.AsQueryable()
    .Where(p => p.IsActive)
    .WhereIf(hasKeyword, p => p.Name.Contains(keyword))
    .Include(p => p.Category)
    .OrderByDescending(p => p.CreationTime)
    .ToPagedResultAsync(pageIndex, pageSize);
```

### 场景 3: 事务操作

```csharp
await _transactionManager.ExecuteInTransactionAsync(async () =>
{
    await _orderRepository.AddAsync(order);
    await _productRepository.UpdateAsync(product);
});
```

### 场景 4: 工作单元

```csharp
await _unitOfWork.BeginTransactionAsync();
try
{
    var repo1 = _unitOfWork.GetRepository<Entity1, Guid>();
    var repo2 = _unitOfWork.GetRepository<Entity2, Guid>();
    
    // 多个操作...
    
    await _unitOfWork.SaveChangesAsync();
    await _unitOfWork.CommitTransactionAsync();
}
catch
{
    await _unitOfWork.RollbackTransactionAsync();
    throw;
}
```

### 场景 5: 多租户

```csharp
_unitOfWork.SetTenantId(tenantId);
_unitOfWork.EnableMultiTenancyFilter();

var products = await _repository.GetAllAsync(); // 自动过滤租户数据
```

### 场景 6: 软删除

```csharp
// 默认只查询未删除的数据
var active = await _repository.GetAllAsync();

// 查询所有数据（包括已删除）
_unitOfWork.DisableSoftDeleteFilter();
var all = await _repository.GetAllAsync();
```

---

## 📦 ORM 实现对照表

| 抽象接口 | EF Core | FreeSql | SqlSugar |
|---------|---------|---------|----------|
| `IDbContext` | `DbContext` | `IFreeSql` | `ISqlSugarClient` |
| `IDbSet<T>` | `DbSet<T>` | `ISelect<T>` | `ISugarQueryable<T>` |
| `IQueryableBuilder<T>` | `IQueryable<T>` | `ISelect<T>` | `ISugarQueryable<T>` |
| `IDbTransaction` | `IDbContextTransaction` | `DbTransaction` | 自定义包装 |
| `IRepository<T>` | 自定义实现 | `BaseRepository<T>` | 自定义实现 |

---

## 🎓 学习路径

1. **入门** - 阅读 [README.md](./README.md)
2. **理解架构** - 阅读 [ARCHITECTURE.md](./ARCHITECTURE.md)
3. **深入学习** - 阅读 [ABSTRACTIONS_GUIDE.md](./ABSTRACTIONS_GUIDE.md)
4. **实践** - 查看具体 ORM 实现示例
5. **进阶** - 自定义扩展和优化

---

## 📞 获取帮助

- 查看详细文档：[ABSTRACTIONS_GUIDE.md](./ABSTRACTIONS_GUIDE.md)
- 了解架构设计：[ARCHITECTURE.md](./ARCHITECTURE.md)
- 查看现有代码示例

---

## 🔄 版本历史

- **v1.0** - 初始版本，包含核心抽象接口
  - 实体接口
  - 数据库上下文接口
  - 仓储接口
  - 查询构建器接口
  - 事务接口
  - 工作单元接口

---

## 📝 接口设计原则

1. **统一性** - 所有 ORM 使用相同的接口
2. **简洁性** - API 设计简单易用
3. **扩展性** - 易于添加新功能
4. **性能** - 不牺牲 ORM 原生性能
5. **兼容性** - 支持多种 ORM 框架

---

*最后更新: 2025-11-01*
