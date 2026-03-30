# ORM 抽象层架构设计文档

## 设计原则

### 1. 依赖倒置原则 (DIP)

业务层依赖于抽象接口，而非具体的 ORM 实现：

```
┌─────────────────────┐
│   Business Layer    │
│  (Application/      │
│   Domain Services)  │
└──────────┬──────────┘
           │ depends on
           ▼
┌─────────────────────┐
│  Abstraction Layer  │
│  (Interfaces)       │
└──────────┬──────────┘
           │ implemented by
           ▼
┌─────────────────────┐
│ Implementation      │
│ (EF Core/FreeSql/   │
│  SqlSugar)          │
└─────────────────────┘
```

### 2. 单一职责原则 (SRP)

每个接口专注于特定的功能领域：

- `IEntity`: 定义实体契约
- `IDbContext`: 数据库上下文管理
- `IDbSet`: 实体集操作
- `IRepository`: CRUD 操作
- `IQueryableBuilder`: 查询构建
- `IDbTransaction`: 事务管理
- `IUnitOfWork`: 工作单元模式

### 3. 开闭原则 (OCP)

对扩展开放，对修改关闭：

```csharp
// 添加新的 ORM 实现，无需修改现有代码
public class MongoDbProvider : IDbContext
{
    // 实现接口方法
}
```

### 4. 接口隔离原则 (ISP)

提供细粒度的接口，客户端不依赖于它们不需要的接口：

```csharp
// 只读场景使用只读仓储
IReadOnlyRepository<Product> readOnlyRepo;

// 完整 CRUD 场景使用完整仓储
IRepository<Product, Guid> fullRepo;
```

### 5. 里氏替换原则 (LSP)

不同 ORM 实现可以相互替换：

```csharp
// 可以透明地切换实现
IDbContext context = useEfCore 
    ? new EfCoreDbContext(options) 
    : new FreeSqlDbContext(options);
```

## 架构层次

```
┌─────────────────────────────────────────────────────┐
│                  Presentation Layer                  │
│              (Web API / MVC / gRPC)                  │
└────────────────────┬────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────┐
│               Application Layer                      │
│         (Application Services / DTOs)                │
└────────────────────┬────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────┐
│                  Domain Layer                        │
│        (Domain Services / Entities / Events)         │
└────────────────────┬────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────┐
│            ORM Abstraction Layer                     │
│   ┌─────────────────────────────────────────┐       │
│   │          Core Interfaces                │       │
│   ├─────────────────────────────────────────┤       │
│   │ IEntity, IRepository, IDbContext        │       │
│   │ IQueryableBuilder, IDbTransaction       │       │
│   │ IUnitOfWork, ITransactionManager        │       │
│   └─────────────────────────────────────────┘       │
└────────────────────┬────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────┐
│           ORM Implementation Layer                   │
│   ┌──────────┐  ┌──────────┐  ┌──────────┐         │
│   │ EF Core  │  │ FreeSql  │  │ SqlSugar │         │
│   │ Provider │  │ Provider │  │ Provider │         │
│   └──────────┘  └──────────┘  └──────────┘         │
└────────────────────┬────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────┐
│                  Database Layer                      │
│      (SQL Server / MySQL / PostgreSQL / etc.)        │
└─────────────────────────────────────────────────────┘
```

## 核心组件关系图

```
┌──────────────────────────────────────────────────────┐
│                  IUnitOfWorkEnhanced                 │
│  ┌────────────────────────────────────────────────┐ │
│  │ • DbContext: IDbContext                        │ │
│  │ • Provider: OrmProvider                        │ │
│  │ • GetRepository<T>()                           │ │
│  │ • CurrentTransaction: IDbTransaction           │ │
│  │ • EnableSoftDeleteFilter()                     │ │
│  │ • SetTenantId(Guid?)                           │ │
│  └────────────────────────────────────────────────┘ │
└────────────┬─────────────────────────────────────────┘
             │
             │ contains
             ▼
┌──────────────────────────────────────────────────────┐
│                    IDbContext                        │
│  ┌────────────────────────────────────────────────┐ │
│  │ • Set<TEntity>(): IDbSet<TEntity>              │ │
│  │ • Queryable<TEntity>(): IQueryableBuilder<T>   │ │
│  │ • SaveChangesAsync()                           │ │
│  │ • BeginTransactionAsync(): IDbTransaction      │ │
│  │ • GetNativeContext()                           │ │
│  └────────────────────────────────────────────────┘ │
└─────────┬────────────────────┬───────────────────────┘
          │                    │
          │ creates            │ creates
          ▼                    ▼
┌──────────────────┐   ┌───────────────────────────────┐
│   IDbSet<T>      │   │  IQueryableBuilder<T>         │
│ ┌──────────────┐ │   │ ┌───────────────────────────┐ │
│ │• AddAsync()  │ │   │ │• Where()                  │ │
│ │• Update()    │ │   │ │• OrderBy()                │ │
│ │• Remove()    │ │   │ │• Skip() / Take()          │ │
│ │• FindAsync() │ │   │ │• Include()                │ │
│ └──────────────┘ │   │ │• Select()                 │ │
└──────────────────┘   │ │• ToListAsync()            │ │
                       │ │• ToPagedResultAsync()     │ │
                       │ └───────────────────────────┘ │
                       └───────────────────────────────┘
```

## 接口继承层次

### 实体接口继承

```
                    IEntity
                       │
         ┌─────────────┼─────────────┬────────────┐
         │             │             │            │
   IEntity<TKey>  IAuditedEntity  ISoftDelete  IMultiTenant
         │                          │
         └────────┬─────────────────┘
                  │
          IFullyAuditedEntity
```

### 仓储接口继承

```
                IRepository<TEntity>
                       │
                       │ extends
                       ▼
           IRepository<TEntity, TKey>
           
                       
          IReadOnlyRepository<TEntity>
                       │
                       │ extends
                       ▼
       IReadOnlyRepository<TEntity, TKey>
```

### 工作单元接口继承

```
       Domain.UnitOfWork.IUnitOfWork
                  │
                  │ extends
                  ▼
          IUnitOfWorkEnhanced
```

## 数据流示例

### 查询流程

```
1. Application Service
   ↓ calls
2. IRepository<Product, Guid>.AsQueryable()
   ↓ returns
3. IQueryableBuilder<Product>
   ↓ builds query
4. IQueryableBuilder<Product>.Where(...).OrderBy(...).ToListAsync()
   ↓ executes
5. IDbContext.Queryable<Product>()
   ↓ delegates to
6. Native ORM (EF Core / FreeSql / SqlSugar)
   ↓ generates SQL
7. Database
   ↓ returns data
8. List<Product>
```

### 事务流程

```
1. Application Service
   ↓ begins transaction
2. IUnitOfWorkEnhanced.BeginTransactionAsync()
   ↓ delegates to
3. IDbContext.BeginTransactionAsync()
   ↓ creates
4. IDbTransaction
   ↓ wraps
5. Native Transaction (DbTransaction / IDbContextTransaction)
   ↓
6. [Execute operations...]
   ↓
7. IDbTransaction.CommitAsync() or RollbackAsync()
   ↓ commits/rollbacks
8. Database
```

### 保存流程

```
1. Application Service
   ↓ calls
2. IRepository<Product, Guid>.AddAsync(product)
   ↓ delegates to
3. IDbSet<Product>.AddAsync(product)
   ↓ marks as added
4. DbContext change tracker (or equivalent)
   ↓
5. IUnitOfWork.SaveChangesAsync()
   ↓ delegates to
6. IDbContext.SaveChangesAsync()
   ↓ generates SQL
7. Native ORM
   ↓ executes
8. Database
```

## ORM 实现策略

### EF Core 实现

```csharp
public class EfCoreDbContext : IDbContext
{
    private readonly DbContext _dbContext;
    
    public IDbSet<TEntity> Set<TEntity>() where TEntity : class
    {
        return new EfCoreDbSet<TEntity>(_dbContext.Set<TEntity>());
    }
    
    public IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class
    {
        return new EfCoreQueryableBuilder<TEntity>(_dbContext.Set<TEntity>());
    }
    
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SaveChangesAsync(cancellationToken);
    }
    
    public async Task<IDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        return new EfCoreTransaction(transaction);
    }
    
    public object GetNativeContext() => _dbContext;
}
```

### FreeSql 实现

```csharp
public class FreeSqlDbContext : IDbContext
{
    private readonly IFreeSql _freeSql;
    private readonly UnitOfWorkManager _uowManager;
    
    public IDbSet<TEntity> Set<TEntity>() where TEntity : class
    {
        return new FreeSqlDbSet<TEntity>(_freeSql);
    }
    
    public IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class
    {
        return new FreeSqlQueryableBuilder<TEntity>(_freeSql.Select<TEntity>());
    }
    
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _uowManager.Current.CommitAsync();
    }
    
    public async Task<IDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var uow = _uowManager.Begin();
        return new FreeSqlTransaction(uow);
    }
    
    public object GetNativeContext() => _freeSql;
}
```

### SqlSugar 实现

```csharp
public class SqlSugarDbContext : IDbContext
{
    private readonly ISqlSugarClient _sqlSugarClient;
    
    public IDbSet<TEntity> Set<TEntity>() where TEntity : class
    {
        return new SqlSugarDbSet<TEntity>(_sqlSugarClient);
    }
    
    public IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class
    {
        return new SqlSugarQueryableBuilder<TEntity>(_sqlSugarClient.Queryable<TEntity>());
    }
    
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // SqlSugar 不需要显式保存
        return 0;
    }
    
    public async Task<IDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _sqlSugarClient.BeginTran();
        return new SqlSugarTransaction(_sqlSugarClient);
    }
    
    public object GetNativeContext() => _sqlSugarClient;
}
```

## 依赖注入配置

### 通用配置

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrmProvider(
        this IServiceCollection services,
        OrmProvider provider,
        string connectionString)
    {
        switch (provider)
        {
            case OrmProvider.EfCore:
                return services.AddEfCore(connectionString);
            
            case OrmProvider.FreeSql:
                return services.AddFreeSql(connectionString);
            
            case OrmProvider.SqlSugar:
                return services.AddSqlSugar(connectionString);
            
            default:
                throw new NotSupportedException($"ORM provider {provider} is not supported.");
        }
    }
}
```

### EF Core 配置

```csharp
public static IServiceCollection AddEfCore(
    this IServiceCollection services,
    string connectionString)
{
    services.AddDbContext<CrestCreatesDbContext>(options =>
    {
        options.UseSqlServer(connectionString);
        options.EnableSensitiveDataLogging();
    });
    
    services.AddScoped<IDbContext>(sp => 
        new EfCoreDbContext(sp.GetRequiredService<CrestCreatesDbContext>()));
    
    services.AddScoped(typeof(IRepository<>), typeof(EfCoreRepository<>));
    services.AddScoped(typeof(IRepository<,>), typeof(EfCoreRepository<,>));
    
    services.AddScoped<IUnitOfWorkEnhanced, EfCoreUnitOfWork>();
    services.AddScoped<ITransactionManager, EfCoreTransactionManager>();
    
    return services;
}
```

### FreeSql 配置

```csharp
public static IServiceCollection AddFreeSql(
    this IServiceCollection services,
    string connectionString)
{
    var freeSql = new FreeSqlBuilder()
        .UseConnectionString(DataType.SqlServer, connectionString)
        .UseAutoSyncStructure(true)
        .Build();
    
    services.AddSingleton<IFreeSql>(freeSql);
    
    services.AddScoped<IDbContext>(sp => 
        new FreeSqlDbContext(sp.GetRequiredService<IFreeSql>()));
    
    services.AddScoped(typeof(IRepository<>), typeof(FreeSqlRepository<>));
    services.AddScoped(typeof(IRepository<,>), typeof(FreeSqlRepository<,>));
    
    services.AddScoped<IUnitOfWorkEnhanced, FreeSqlUnitOfWork>();
    services.AddScoped<ITransactionManager, FreeSqlTransactionManager>();
    
    return services;
}
```

### SqlSugar 配置

```csharp
public static IServiceCollection AddSqlSugar(
    this IServiceCollection services,
    string connectionString)
{
    services.AddScoped<ISqlSugarClient>(sp =>
    {
        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });
    });
    
    services.AddScoped<IDbContext>(sp => 
        new SqlSugarDbContext(sp.GetRequiredService<ISqlSugarClient>()));
    
    services.AddScoped(typeof(IRepository<>), typeof(SqlSugarRepository<>));
    services.AddScoped(typeof(IRepository<,>), typeof(SqlSugarRepository<,>));
    
    services.AddScoped<IUnitOfWorkEnhanced, SqlSugarUnitOfWork>();
    services.AddScoped<ITransactionManager, SqlSugarTransactionManager>();
    
    return services;
}
```

## 扩展点

### 1. 添加新的 ORM 实现

```csharp
// 1. 实现核心接口
public class NewOrmDbContext : IDbContext { /* ... */ }
public class NewOrmRepository<T> : IRepository<T> { /* ... */ }
public class NewOrmUnitOfWork : IUnitOfWorkEnhanced { /* ... */ }

// 2. 添加到 OrmProvider 枚举
public enum OrmProvider
{
    EfCore,
    SqlSugar,
    FreeSql,
    NewOrm  // 新增
}

// 3. 注册到 DI 容器
public static IServiceCollection AddNewOrm(
    this IServiceCollection services,
    string connectionString)
{
    // 注册实现
}
```

### 2. 添加自定义拦截器

```csharp
public interface IDbInterceptor
{
    Task OnBeforeSaveAsync(IDbContext context);
    Task OnAfterSaveAsync(IDbContext context, int affectedRows);
}

public class AuditInterceptor : IDbInterceptor
{
    public Task OnBeforeSaveAsync(IDbContext context)
    {
        // 自动设置审计字段
    }
}
```

### 3. 添加查询过滤器

```csharp
public interface IQueryFilter
{
    Expression<Func<TEntity, bool>> GetFilter<TEntity>() where TEntity : class;
}

public class SoftDeleteFilter : IQueryFilter
{
    public Expression<Func<TEntity, bool>> GetFilter<TEntity>() where TEntity : class
    {
        if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
        {
            return e => !((ISoftDelete)e).IsDeleted;
        }
        return null;
    }
}
```

## 性能考虑

### 1. 查询优化

- 使用 `AsNoTracking()` 进行只读查询
- 使用 `Include()` 避免 N+1 查询
- 使用投影 (`Select`) 减少数据传输
- 使用分页避免加载大数据集

### 2. 批量操作优化

- 使用 `AddRangeAsync` / `UpdateRangeAsync` / `DeleteRangeAsync`
- 考虑使用批量插入扩展（如 EF Core 的 `BulkInsert`）

### 3. 缓存策略

```csharp
public interface ICacheableRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
{
    Task<TEntity> GetByIdWithCacheAsync(TKey id);
}
```

### 4. 连接池管理

- 使用连接池减少连接开销
- 正确管理 DbContext 生命周期（Scoped）
- 避免长时间持有连接

## 测试策略

### 1. 单元测试

```csharp
public class ProductServiceTests
{
    private readonly Mock<IRepository<Product, Guid>> _mockRepository;
    private readonly ProductService _service;

    public ProductServiceTests()
    {
        _mockRepository = new Mock<IRepository<Product, Guid>>();
        _service = new ProductService(_mockRepository.Object);
    }

    [Fact]
    public async Task GetProductAsync_ReturnsProduct()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var expectedProduct = new Product { Id = productId, Name = "Test" };
        _mockRepository.Setup(r => r.GetByIdAsync(productId, default))
            .ReturnsAsync(expectedProduct);

        // Act
        var result = await _service.GetProductAsync(productId);

        // Assert
        Assert.Equal(expectedProduct, result);
    }
}
```

### 2. 集成测试

```csharp
public class ProductRepositoryIntegrationTests : IClassFixture<DatabaseFixture>
{
    private readonly IRepository<Product, Guid> _repository;

    public ProductRepositoryIntegrationTests(DatabaseFixture fixture)
    {
        _repository = fixture.GetRepository<Product, Guid>();
    }

    [Fact]
    public async Task AddAsync_ShouldAddProduct()
    {
        // Arrange
        var product = new Product { Name = "Test Product", Price = 100 };

        // Act
        var result = await _repository.AddAsync(product);

        // Assert
        Assert.NotEqual(Guid.Empty, result.Id);
        
        var retrieved = await _repository.GetByIdAsync(result.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Test Product", retrieved.Name);
    }
}
```

## 总结

这套 ORM 抽象层架构提供了：

1. **清晰的分层结构**：每层职责明确
2. **灵活的扩展性**：易于添加新的 ORM 实现
3. **强大的功能**：支持复杂查询、事务、多租户等
4. **良好的性能**：提供多种优化选项
5. **易于测试**：基于接口的设计便于单元测试

通过遵循 SOLID 原则和最佳实践，这套架构能够满足各种规模项目的需求。
