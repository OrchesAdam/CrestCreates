# CrestCreates ORM Providers 抽象层

## 📖 概述

本项目提供了一套统一的 ORM 抽象层，支持 **EF Core**、**FreeSql** 和 **SqlSugar** 三种主流 ORM 框架。通过这套抽象层，您可以：

- ✅ 在不同 ORM 之间无缝切换
- ✅ 使用统一的 API 进行数据访问
- ✅ 保持业务代码独立于具体 ORM 实现
- ✅ 支持工作单元模式和事务管理
- ✅ 支持多租户和软删除等高级功能

## 🎯 核心抽象接口

本项目提供了一套完整的 ORM 抽象接口，涵盖以下核心功能：

### 📦 接口分类

#### 1. 实体接口 (Entity Interfaces)
- `IEntity` - 实体标记接口
- `IEntity<TKey>` - 带主键的实体接口
- `IAuditedEntity` - 审计实体接口
- `ISoftDelete` - 软删除接口
- `IFullyAuditedEntity` - 完整审计实体接口
- `IMultiTenant` - 多租户接口

#### 2. 数据库上下文接口 (Database Context)
- `IDbContext` - 数据库上下文统一抽象
- `IDbSet<TEntity>` - 实体集操作接口

#### 3. 仓储接口 (Repository)
- `IRepository<TEntity>` - 基础仓储接口
- `IRepository<TEntity, TKey>` - 带主键的仓储接口
- `IReadOnlyRepository<TEntity>` - 只读仓储接口
- `IReadOnlyRepository<TEntity, TKey>` - 带主键的只读仓储接口

#### 4. 查询构建器接口 (Query Builder)
- `IQueryableBuilder<TEntity>` - 统一的查询构建器
- `PagedResult<T>` - 分页结果类

#### 5. 事务接口 (Transaction)
- `IDbTransaction` - 数据库事务接口
- `ITransactionManager` - 事务管理器接口
- `TransactionOptions` - 事务选项
- `TransactionPropagation` - 事务传播行为

#### 6. 工作单元接口 (Unit of Work)
- `IUnitOfWorkEnhanced` - 增强的工作单元接口
- `IUnitOfWorkManager` - 工作单元管理器接口
- `IUnitOfWorkFactory` - 工作单元工厂接口
- `UnitOfWorkOptions` - 工作单元选项

### 📚 文档导航

- **[接口索引 (INTERFACE_INDEX.md)](./INTERFACE_INDEX.md)** - 所有接口的快速索引和速查表
- **[详细指南 (ABSTRACTIONS_GUIDE.md)](./ABSTRACTIONS_GUIDE.md)** - 接口详细说明和使用示例
- **[架构设计 (ARCHITECTURE.md)](./ARCHITECTURE.md)** - 架构设计原则和实现策略

### 1. ORM 提供者枚举
```csharp
public enum OrmProvider
{
    EfCore,     // Entity Framework Core
    SqlSugar,   // SqlSugar ORM
    FreeSql     // FreeSql ORM
}
```

### 2. 工作单元工厂
```csharp
public interface IUnitOfWorkFactory
{
    IUnitOfWork Create(OrmProvider provider);
}
```

### 3. 工作单元管理器
```csharp
public interface IUnitOfWorkManager
{
    IUnitOfWork Current { get; }
    IUnitOfWork Begin(OrmProvider? provider = null);
    TResult Execute<TResult>(Func<IUnitOfWork, TResult> action, OrmProvider? provider = null);
    Task<TResult> ExecuteAsync<TResult>(Func<IUnitOfWork, Task<TResult>> action, OrmProvider? provider = null);
}
```

## 📁 项目结构

```
CrestCreates.OrmProviders.Abstract/
├── Abstractions/                     - 核心抽象接口
│   ├── IEntity.cs                   - 实体接口定义
│   ├── IDbContext.cs                - 数据库上下文接口
│   ├── IDbSet.cs                    - 实体集接口
│   ├── IRepository.cs               - 仓储接口
│   ├── IQueryableBuilder.cs         - 查询构建器接口
│   ├── IDbTransaction.cs            - 事务接口
│   ├── IUnitOfWorkEnhanced.cs       - 增强工作单元接口
│   ├── IUnitOfWorkFactory.cs        - 工作单元工厂接口
│   └── IUnitOfWorkManager.cs        - 工作单元管理器接口
├── Base/                            - 基础实现
│   ├── UnitOfWorkFactory.cs         - 工作单元工厂基类
│   └── UnitOfWorkManager.cs         - 工作单元管理器基类
├── Extensions/                      - 扩展方法
│   └── UnitOfWorkServiceCollectionExtensions.cs
├── OrmProvider.cs                   - ORM 提供者枚举
├── README.md                        - 项目概述
├── INTERFACE_INDEX.md               - 接口速查索引
├── ABSTRACTIONS_GUIDE.md            - 详细使用指南
└── ARCHITECTURE.md                  - 架构设计文档
```

## 🚀 快速开始

### 基础 CRUD 操作

```csharp
public class ProductService
{
    private readonly IRepository<Product, Guid> _repository;

    public ProductService(IRepository<Product, Guid> repository)
    {
        _repository = repository;
    }

    // 查询
    public async Task<Product> GetProductAsync(Guid id)
    {
        return await _repository.GetByIdAsync(id);
    }

    // 添加
    public async Task<Product> CreateProductAsync(Product product)
    {
        return await _repository.AddAsync(product);
    }

    // 更新
    public async Task UpdateProductAsync(Product product)
    {
        await _repository.UpdateAsync(product);
    }

    // 删除
    public async Task DeleteProductAsync(Guid id)
    {
        await _repository.DeleteByIdAsync(id);
    }
}
```

### 高级查询

```csharp
public class ProductQueryService
{
    private readonly IRepository<Product, Guid> _repository;

    public async Task<PagedResult<Product>> SearchProductsAsync(
        string keyword, 
        decimal? minPrice,
        int pageIndex, 
        int pageSize)
    {
        return await _repository.AsQueryable()
            .WhereIf(!string.IsNullOrEmpty(keyword), p => p.Name.Contains(keyword))
            .WhereIf(minPrice.HasValue, p => p.Price >= minPrice.Value)
            .OrderByDescending(p => p.CreationTime)
            .Include(p => p.Category)
            .ToPagedResultAsync(pageIndex, pageSize);
    }
}
```

### 使用工作单元和事务

```csharp
public class OrderService
{
    private readonly IUnitOfWorkEnhanced _unitOfWork;

    public async Task CreateOrderAsync(Order order)
    {
        await _unitOfWork.BeginTransactionAsync();
        
        try
        {
            var orderRepo = _unitOfWork.GetRepository<Order, Guid>();
            var productRepo = _unitOfWork.GetRepository<Product, Guid>();
            
            // 创建订单
            await orderRepo.AddAsync(order);
            
            // 更新库存
            foreach (var item in order.Items)
            {
                var product = await productRepo.GetByIdAsync(item.ProductId);
                product.Stock -= item.Quantity;
                await productRepo.UpdateAsync(product);
            }
            
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

### 多租户支持

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
}
```

## 📖 详细文档

想要深入了解？请查看以下文档：

- **[接口速查索引](./INTERFACE_INDEX.md)** - 快速查找所有接口和方法
- **[详细使用指南](./ABSTRACTIONS_GUIDE.md)** - 深入学习每个接口的用法和最佳实践
- **[架构设计文档](./ARCHITECTURE.md)** - 了解设计原则和实现策略

## 🔗 依赖关系

### 项目依赖
```
CrestCreates.OrmProviders.Abstract
    ↓ 依赖
CrestCreates.Domain
    ├── Entities
    ├── Repositories (接口)
    └── UnitOfWork (IUnitOfWork 接口)
```

### 被依赖
```
CrestCreates.OrmProviders.FreeSql
    ↓ 引用
CrestCreates.OrmProviders.Abstract

CrestCreates.OrmProviders.SqlSugar
    ↓ 引用
CrestCreates.OrmProviders.Abstract

CrestCreates.OrmProviders.EFCore
    ↓ 引用
CrestCreates.OrmProviders.Abstract
```

## 📦 NuGet 包依赖

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  <ProjectReference Include="..\..\CrestCreates.Domain\CrestCreates.Domain.csproj" />
</ItemGroup>
```

## 🎨 设计模式

### 1. 抽象工厂模式 (Abstract Factory)
- `IUnitOfWorkFactory` 定义工厂接口
- 各 ORM Provider 实现具体工厂

### 2. 策略模式 (Strategy)
- `OrmProvider` 枚举定义策略
- `IUnitOfWorkFactory.Create()` 根据策略创建实例

### 3. 工作单元模式 (Unit of Work)
- `IUnitOfWorkManager` 管理工作单元生命周期
- 自动事务管理

## 📊 类图

```
┌─────────────────────────┐
│   OrmProvider (enum)    │
│  - EfCore               │
│  - SqlSugar             │
│  - FreeSql              │
└─────────────────────────┘
           ↑
           │ 使用
           │
┌──────────────────────────────────┐
│    IUnitOfWorkFactory            │
│  + Create(provider): IUnitOfWork │
└──────────────────────────────────┘
           ↑
           │ 实现
           │
┌──────────────────────────────────┐
│    UnitOfWorkFactory (基类)      │
│  # _serviceProvider              │
│  + Create(provider): IUnitOfWork │
└──────────────────────────────────┘
           ↑
           │ 使用
           │
┌──────────────────────────────────┐
│    IUnitOfWorkManager            │
│  + Current: IUnitOfWork          │
│  + Begin(): IUnitOfWork          │
│  + Execute<T>()                  │
│  + ExecuteAsync<T>()             │
└──────────────────────────────────┘
           ↑
           │ 实现
           │
┌──────────────────────────────────┐
│    UnitOfWorkManager (基类)      │
│  - _factory                      │
│  - _defaultProvider              │
│  - _current                      │
└──────────────────────────────────┘
```

## ✅ 最佳实践

### 1. 使用依赖注入
```csharp
// ✅ 推荐
services.AddUnitOfWork(OrmProvider.FreeSql);

// ❌ 不推荐
var factory = new UnitOfWorkFactory(serviceProvider);
```

### 2. 使用 using 释放资源
```csharp
// ✅ 推荐
using (var uow = _uowManager.Begin())
{
    // 业务逻辑
}

// ❌ 不推荐
var uow = _uowManager.Begin();
// 忘记释放
```

### 3. 使用 ExecuteAsync 简化事务
```csharp
// ✅ 推荐
await _uowManager.ExecuteAsync(async uow =>
{
    // 自动事务管理
    return result;
});

// ❌ 不推荐：手动管理事务容易出错
using var uow = _uowManager.Begin();
await uow.BeginTransactionAsync();
try { ... } catch { ... }
```

## 🔧 扩展点

### 添加新的 ORM 提供者

1. **更新枚举**
```csharp
public enum OrmProvider
{
    EfCore,
    SqlSugar,
    FreeSql,
    Dapper  // 新增
}
```

2. **实现工作单元**
```csharp
public class DapperUnitOfWork : IUnitOfWork
{
    // 实现接口
}
```

3. **更新工厂**
```csharp
public override IUnitOfWork Create(OrmProvider provider)
{
    return provider switch
    {
        // ...
        OrmProvider.Dapper => _serviceProvider.GetRequiredService<DapperUnitOfWork>(),
        _ => throw new NotSupportedException()
    };
}
```

## 📚 参考资源

- [工作单元模式 (Martin Fowler)](https://martinfowler.com/eaaCatalog/unitOfWork.html)
- [抽象工厂模式](https://refactoring.guru/design-patterns/abstract-factory)
- [依赖注入最佳实践](https://docs.microsoft.com/aspnet/core/fundamentals/dependency-injection)

---

**创建日期**: 2025年11月1日  
**版本**: 1.0.0  
**状态**: ✅ 已完成
