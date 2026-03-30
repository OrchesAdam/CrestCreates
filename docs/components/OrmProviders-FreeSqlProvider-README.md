# FreeSql UnitOfWork 完整指南

## 📖 概述

基于 FreeSql 官方推荐的 `UnitOfWorkManager` 实现，支持：
- ✅ 事务传播机制（6 种传播方式）
- ✅ 多仓储共享事务
- ✅ AOP 特性 `[Transactional]` 支持
- ✅ 异步/同步事务管理
- ✅ 多库场景支持（FreeSql.Cloud）

**官方文档**: https://freesql.net/guide/unitofwork-manager.html

---

## 🚀 快速开始

### 第一步：依赖注入配置

在 `Startup.cs` 中注册服务：

```csharp
using CrestCreates.Infrastructure.Providers.FreeSql.Extensions;
using FreeSql;

public void ConfigureServices(IServiceCollection services)
{
    // 方式 1: 使用构建器
    services.AddFreeSqlWithUow(fsql =>
    {
        new FreeSqlBuilder()
            .UseConnectionString(DataType.SqlServer, Configuration.GetConnectionString("Default"))
            .UseAutoSyncStructure(true) // 自动同步表结构
            .Build();
    }, typeof(Startup).Assembly); // 传入仓储所在程序集

    // 方式 2: 使用已有实例
    var freeSql = new FreeSqlBuilder()
        .UseConnectionString(DataType.SqlServer, connStr)
        .Build();
    services.AddFreeSqlWithUow(freeSql, typeof(Startup).Assembly);
}
```

### 第二步：中间件配置（可选，使用 AOP 时需要）

```csharp
using CrestCreates.Infrastructure.Providers.FreeSql.Attributes;

public void Configure(IApplicationBuilder app)
{
    // 设置 ServiceProvider 供 TransactionalAttribute 使用
    app.Use(async (context, next) =>
    {
        TransactionalAttribute.SetServiceProvider(context.RequestServices);
        await next();
    });

    // 其他中间件...
}
```

---

## 📝 使用方式

### 方式 1: 仓储中自动事务（推荐）

#### 1.1 定义仓储接口

```csharp
public interface IOrderRepository : IRepository<Order, Guid>
{
    Task<List<Order>> GetOrdersByUserId(Guid userId);
}
```

#### 1.2 实现仓储（继承 FreeSqlRepositoryBase）

```csharp
using CrestCreates.Infrastructure.Providers.FreeSql.Repositories;
using CrestCreates.Infrastructure.Providers.FreeSql.UnitOfWork;

public class OrderRepository : FreeSqlRepositoryBase<Order, Guid>, IOrderRepository
{
    public OrderRepository(FreeSqlUnitOfWorkManager uowManager) : base(uowManager)
    {
        // uowManager.Binding(this) 在基类中已自动调用
    }

    public async Task<List<Order>> GetOrdersByUserId(Guid userId)
    {
        return await Select
            .Where(o => o.UserId == userId)
            .ToListAsync();
    }
}
```

#### 1.3 在 Service 中使用（手动事务）

```csharp
using CrestCreates.Infrastructure.Providers.FreeSql.UnitOfWork;
using CrestCreates.Infrastructure.Providers.FreeSql.Attributes;

public class OrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderItemRepository _orderItemRepository;
    private readonly FreeSqlUnitOfWorkManager _uowManager;

    public OrderService(
        IOrderRepository orderRepository,
        IOrderItemRepository orderItemRepository,
        FreeSqlUnitOfWorkManager uowManager)
    {
        _orderRepository = orderRepository;
        _orderItemRepository = orderItemRepository;
        _uowManager = uowManager;
    }

    // 方式 1: 使用 TransactionHelper
    public async Task CreateOrder(Order order, List<OrderItem> items)
    {
        await TransactionHelper.ExecuteAsync(_uowManager, async () =>
        {
            // 所有操作在同一个事务中
            await _orderRepository.AddAsync(order);
            
            foreach (var item in items)
            {
                item.OrderId = order.Id;
                await _orderItemRepository.AddAsync(item);
            }
        });
    }

    // 方式 2: 手动管理 UnitOfWork
    public async Task UpdateOrder(Order order)
    {
        using (var uow = _uowManager.Begin(Propagation.Required))
        {
            try
            {
                await _orderRepository.UpdateAsync(order);
                uow.Commit();
            }
            catch
            {
                uow.Rollback();
                throw;
            }
        }
    }
}
```

### 方式 2: 使用 [Transactional] AOP 特性（需要 Rougamo.Fody）

#### 2.1 安装 NuGet 包

```bash
dotnet add package Rougamo.Fody
```

#### 2.2 使用 [Transactional] 特性

```csharp
using CrestCreates.Infrastructure.Providers.FreeSql.Attributes;
using FreeSql;

public class OrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderItemRepository _orderItemRepository;

    public OrderService(
        IOrderRepository orderRepository,
        IOrderItemRepository orderItemRepository)
    {
        _orderRepository = orderRepository;
        _orderItemRepository = orderItemRepository;
    }

    // ✨ 使用 [Transactional] 特性，自动管理事务
    [Transactional(Propagation.Required)]
    public async Task CreateOrder(Order order, List<OrderItem> items)
    {
        // 所有仓储操作自动在同一个事务中
        await _orderRepository.AddAsync(order);
        
        foreach (var item in items)
        {
            item.OrderId = order.Id;
            await _orderItemRepository.AddAsync(item);
        }
        // 方法正常返回自动提交，抛出异常自动回滚
    }

    // 设置隔离级别
    [Transactional(Propagation.Required, IsolationLevel = IsolationLevel.ReadCommitted)]
    public async Task ProcessPayment(Guid orderId, decimal amount)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        order.Status = OrderStatus.Paid;
        await _orderRepository.UpdateAsync(order);
    }
}
```

---

## 🎯 事务传播方式

FreeSql 支持 6 种事务传播方式（与 Spring Framework 一致）：

### 1. Required（默认）✅

如果当前没有事务，就新建一个事务；如果已存在事务，加入到这个事务中。

```csharp
[Transactional(Propagation.Required)] // 默认
public async Task Method1()
{
    // 开启新事务
    await _repository1.InsertAsync(entity1);
    
    await Method2(); // 加入当前事务
}

[Transactional(Propagation.Required)]
public async Task Method2()
{
    // 在 Method1 的事务中执行
    await _repository2.InsertAsync(entity2);
}
```

### 2. Supports

支持当前事务，如果没有当前事务，就以非事务方式执行。

```csharp
[Transactional(Propagation.Supports)]
public async Task ReadData()
{
    // 如果有事务则加入，否则非事务执行
    return await _repository.GetAllAsync();
}
```

### 3. Mandatory

使用当前事务，如果没有当前事务，就抛出异常。

```csharp
[Transactional(Propagation.Mandatory)]
public async Task MustInTransaction()
{
    // 必须在已有事务中调用，否则抛出异常
    await _repository.InsertAsync(entity);
}
```

### 4. NotSupported

以非事务方式执行操作，如果当前存在事务，就把当前事务挂起。

```csharp
[Transactional(Propagation.NotSupported)]
public async Task LogAudit()
{
    // 即使在事务中调用，也以非事务方式执行
    await _auditRepository.InsertAsync(audit);
}
```

### 5. Never

以非事务方式执行，如果当前事务存在则抛出异常。

```csharp
[Transactional(Propagation.Never)]
public async Task MustNotInTransaction()
{
    // 不能在事务中调用，否则抛出异常
    await _repository.QueryAsync();
}
```

### 6. Nested

以嵌套事务方式执行。

```csharp
[Transactional(Propagation.Nested)]
public async Task NestedOperation()
{
    // 创建嵌套事务（保存点）
    await _repository.InsertAsync(entity);
}
```

---

## 🔧 高级用法

### 1. 多个仓储共享事务

```csharp
[Transactional]
public async Task ComplexOperation()
{
    // 所有仓储自动共享同一个事务
    await _orderRepository.AddAsync(order);
    await _orderItemRepository.AddAsync(item);
    await _inventoryRepository.UpdateAsync(inventory);
    await _paymentRepository.AddAsync(payment);
    
    // 任何一个失败都会回滚所有操作
}
```

### 2. 嵌套事务

```csharp
[Transactional(Propagation.Required)]
public async Task OuterMethod()
{
    await _repository1.InsertAsync(entity1);
    
    // 调用嵌套事务方法
    await InnerMethod();
    
    await _repository2.InsertAsync(entity2);
}

[Transactional(Propagation.Nested)]
public async Task InnerMethod()
{
    // 创建嵌套事务
    await _repository3.InsertAsync(entity3);
}
```

### 3. 不同隔离级别

```csharp
// 读未提交
[Transactional(IsolationLevel = IsolationLevel.ReadUncommitted)]
public async Task DirtyRead() { }

// 读已提交
[Transactional(IsolationLevel = IsolationLevel.ReadCommitted)]
public async Task NonRepeatableRead() { }

// 可重复读
[Transactional(IsolationLevel = IsolationLevel.RepeatableRead)]
public async Task PhantomRead() { }

// 序列化
[Transactional(IsolationLevel = IsolationLevel.Serializable)]
public async Task FullIsolation() { }
```

### 4. 手动事务（不使用 AOP）

```csharp
public async Task ManualTransaction()
{
    using (var uow = _uowManager.Begin(Propagation.Required))
    {
        try
        {
            await _repository1.InsertAsync(entity1);
            await _repository2.InsertAsync(entity2);
            
            uow.Commit();
        }
        catch (Exception ex)
        {
            uow.Rollback();
            throw;
        }
    }
}
```

---

## 🌐 多库场景（FreeSql.Cloud）

### 定义数据库枚举

```csharp
public enum DbEnum
{
    MainDb,
    LogDb,
    AnalyticsDb
}
```

### 注册多库

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddFreeSqlCloud<DbEnum>(cloud =>
    {
        cloud.Register(DbEnum.MainDb, () => 
            new FreeSqlBuilder()
                .UseConnectionString(DataType.SqlServer, mainConnStr)
                .Build());
                
        cloud.Register(DbEnum.LogDb, () => 
            new FreeSqlBuilder()
                .UseConnectionString(DataType.MySql, logConnStr)
                .Build());
                
        cloud.Register(DbEnum.AnalyticsDb, () => 
            new FreeSqlBuilder()
                .UseConnectionString(DataType.PostgreSQL, analyticsConnStr)
                .Build());
    });
}
```

### 使用多库事务

```csharp
// 跨多个数据库的事务
[Transactional(DbEnum.MainDb)]
[Transactional(DbEnum.LogDb)]
public async Task CrossDatabaseOperation()
{
    // 同时在两个数据库中开启事务
    await _mainDbRepository.InsertAsync(entity1);
    await _logDbRepository.InsertAsync(log);
}
```

---

## ⚙️ 配置选项

### 1. Startup.cs 完整配置

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // FreeSql 配置
        var freeSql = new FreeSqlBuilder()
            .UseConnectionString(DataType.SqlServer, Configuration.GetConnectionString("Default"))
            .UseAutoSyncStructure(true) // 开发环境自动同步表结构
            .UseNoneCommandParameter(true) // 不使用参数化查询
            .UseMonitorCommand(cmd => Console.WriteLine(cmd.CommandText)) // 监控 SQL
            .Build();

        // 注册 FreeSql + UowManager
        services.AddFreeSqlWithUow(freeSql, typeof(Startup).Assembly);

        // 手动注册自定义仓储
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderItemRepository, OrderItemRepository>();
    }

    public void Configure(IApplicationBuilder app)
    {
        // 设置 ServiceProvider（用于 AOP）
        app.Use(async (context, next) =>
        {
            TransactionalAttribute.SetServiceProvider(context.RequestServices);
            await next();
        });

        app.UseRouting();
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }
}
```

---

## 📊 最佳实践

### ✅ 推荐做法

1. **使用 UnitOfWorkManager 而不是直接创建 UnitOfWork**
   ```csharp
   // ✅ 推荐
   public OrderRepository(FreeSqlUnitOfWorkManager uowManager) : base(uowManager)
   {
   }
   
   // ❌ 不推荐
   public OrderRepository(IFreeSql freeSql)
   {
       var uow = freeSql.CreateUnitOfWork();
   }
   ```

2. **仓储继承 FreeSqlRepositoryBase**
   ```csharp
   // ✅ 推荐：自动绑定到 UowManager
   public class OrderRepository : FreeSqlRepositoryBase<Order, Guid>
   {
   }
   
   // ❌ 不推荐：手动管理
   public class OrderRepository
   {
       private readonly IFreeSql _freeSql;
   }
   ```

3. **优先使用 [Transactional] 特性**
   ```csharp
   // ✅ 推荐：简洁清晰
   [Transactional]
   public async Task CreateOrder() { }
   
   // ❌ 不推荐：手动管理容易出错
   public async Task CreateOrder()
   {
       using var uow = _uowManager.Begin();
       try { ... } catch { ... }
   }
   ```

4. **事务粒度要合理**
   ```csharp
   // ✅ 推荐：一个业务操作一个事务
   [Transactional]
   public async Task CreateOrder(Order order, List<OrderItem> items)
   {
       // 业务逻辑
   }
   
   // ❌ 不推荐：事务粒度太大
   [Transactional]
   public async Task ProcessDailyOrders(List<Order> orders)
   {
       // 处理成千上万个订单，事务太长
   }
   ```

### ❌ 避免做法

1. **不要在事务中执行长时间操作**
   ```csharp
   // ❌ 错误：事务中调用外部 API
   [Transactional]
   public async Task CreateOrder()
   {
       await _orderRepository.AddAsync(order);
       await _httpClient.PostAsync("https://api.example.com/notify"); // ❌
   }
   ```

2. **不要嵌套过深**
   ```csharp
   // ❌ 错误：事务嵌套层级过深
   [Transactional]
   public async Task Method1()
   {
       Method2();  // 嵌套层级 2
   }
   
   [Transactional]
   public async Task Method2()
   {
       Method3();  // 嵌套层级 3
   }
   ```

3. **不要混用多种事务管理方式**
   ```csharp
   // ❌ 错误：同时使用 [Transactional] 和手动管理
   [Transactional]
   public async Task CreateOrder()
   {
       using var uow = _uowManager.Begin(); // ❌ 不要这样做
   }
   ```

---

## 🔗 参考资源

- [FreeSql 官方文档 - UowManager](https://freesql.net/guide/unitofwork-manager.html)
- [FreeSql 官方文档 - Repository](https://freesql.net/guide/repository.html)
- [Rougamo AOP 框架](https://github.com/inversionhourglass/Rougamo)
- [Spring Framework 事务传播](https://docs.spring.io/spring-framework/docs/current/reference/html/data-access.html#tx-propagation)

---

**最后更新**: 2025年11月1日  
**FreeSql 版本**: 3.x+  
**文档版本**: 2.0.0
