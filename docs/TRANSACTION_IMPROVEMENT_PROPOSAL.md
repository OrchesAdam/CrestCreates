# EfCoreDataBaseTransaction 改进方案

## 📋 问题概述

当前 `EfCoreDataBaseTransaction` 实现存在以下问题：

1. ❌ **无法获取 ConnectionString**：`IDbContextTransaction` 不包含连接字符串信息
2. ✅ **IsolationLevel 可以正常获取**：通过 `_transaction.IsolationLevel` 正常工作
3. ⚠️ **命名不一致**：混用 `IDataBase*` 和 `IDb*` 前缀

## 🎯 改进方案

### 方案一：Transaction 持有 DbContext 引用 ⭐ 推荐

#### 设计思路

让 Transaction 持有对 DbContext 的引用，需要时可以访问 Context 的属性（如 ConnectionString）。

#### 优点
- ✅ Transaction 可以访问 Context 的所有公共属性
- ✅ 符合面向对象设计原则
- ✅ 更灵活，易于扩展
- ✅ 与 EF Core 原生设计保持一致

#### 缺点
- ⚠️ 需要小心处理循环引用和生命周期管理
- ⚠️ Transaction 依赖于 Context，耦合度稍高

#### 实现代码

```csharp
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using CrestCreates.DbContextProvider.Abstract;

namespace CrestCreates.OrmProviders.EFCore.DbContexts
{
    /// <summary>
    /// EF Core 数据库事务包装器
    /// </summary>
    public class EfCoreDataBaseTransaction : IDataBaseTransaction
    {
        private readonly IDbContextTransaction _transaction;
        private readonly DbContext _dbContext;
        private bool _isCommitted = false;
        private bool _isRolledBack = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="transaction">EF Core 原生事务</param>
        /// <param name="dbContext">关联的 DbContext</param>
        public EfCoreDataBaseTransaction(IDbContextTransaction transaction, DbContext dbContext)
        {
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            TransactionId = Guid.NewGuid();
        }

        /// <inheritdoc />
        public Guid TransactionId { get; }

        /// <inheritdoc />
        public IsolationLevel IsolationLevel => _transaction.IsolationLevel;

        /// <summary>
        /// 获取连接字符串
        /// </summary>
        public string ConnectionString => _dbContext.Database.GetConnectionString();

        /// <inheritdoc />
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_isCommitted || _isRolledBack)
            {
                throw new InvalidOperationException("Transaction has already been completed.");
            }

            await _transaction.CommitAsync(cancellationToken);
            _isCommitted = true;
        }

        /// <inheritdoc />
        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_isCommitted || _isRolledBack)
            {
                throw new InvalidOperationException("Transaction has already been completed.");
            }

            await _transaction.RollbackAsync(cancellationToken);
            _isRolledBack = true;
        }

        /// <inheritdoc />
        public object GetNativeTransaction() => _transaction;

        /// <summary>
        /// 获取关联的 DbContext
        /// </summary>
        public DbContext GetDbContext() => _dbContext;

        /// <inheritdoc />
        public bool IsCommitted => _isCommitted;

        /// <inheritdoc />
        public bool IsRolledBack => _isRolledBack;

        /// <inheritdoc />
        public bool IsCompleted => _isCommitted || _isRolledBack;

        /// <inheritdoc />
        public void Dispose()
        {
            _transaction?.Dispose();
            // 注意：不要 Dispose DbContext，因为它的生命周期由外部管理
        }
    }
}
```

#### 对应的 DbContext 修改

```csharp
public async Task<IDataBaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
{
    var transaction = await Database.BeginTransactionAsync(cancellationToken);
    // 传入 this 引用，让 Transaction 可以访问 DbContext
    return new EfCoreDataBaseTransaction(transaction, this);
}

public IDataBaseTransaction CurrentTransaction => 
    Database.CurrentTransaction != null 
        ? new EfCoreDataBaseTransaction(Database.CurrentTransaction, this) 
        : null;
```

---

### 方案二：在构造时传入必要信息

#### 设计思路

在创建 Transaction 时，将必要的信息（如 ConnectionString、IsolationLevel）作为参数传入。

#### 优点
- ✅ Transaction 完全独立，不依赖 Context
- ✅ 降低耦合度
- ✅ 更容易测试

#### 缺点
- ❌ 如果需要更多信息，需要修改构造函数
- ❌ 不够灵活，扩展性差
- ❌ 需要在创建时复制数据

#### 实现代码

```csharp
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using CrestCreates.DbContextProvider.Abstract;

namespace CrestCreates.OrmProviders.EFCore.DbContexts
{
    /// <summary>
    /// EF Core 数据库事务包装器
    /// </summary>
    public class EfCoreDataBaseTransaction : IDataBaseTransaction
    {
        private readonly IDbContextTransaction _transaction;
        private readonly string _connectionString;
        private bool _isCommitted = false;
        private bool _isRolledBack = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="transaction">EF Core 原生事务</param>
        /// <param name="connectionString">连接字符串</param>
        public EfCoreDataBaseTransaction(
            IDbContextTransaction transaction, 
            string connectionString)
        {
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            TransactionId = Guid.NewGuid();
        }

        public Guid TransactionId { get; }

        public IsolationLevel IsolationLevel => _transaction.IsolationLevel;

        public string ConnectionString => _connectionString;

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _transaction.CommitAsync(cancellationToken);
            _isCommitted = true;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            await _transaction.RollbackAsync(cancellationToken);
            _isRolledBack = true;
        }

        public object GetNativeTransaction() => _transaction;

        public bool IsCommitted => _isCommitted;

        public bool IsRolledBack => _isRolledBack;

        public bool IsCompleted => _isCommitted || _isRolledBack;

        public void Dispose()
        {
            _transaction?.Dispose();
        }
    }
}
```

#### 对应的 DbContext 修改

```csharp
public async Task<IDataBaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
{
    var transaction = await Database.BeginTransactionAsync(cancellationToken);
    var connectionString = Database.GetConnectionString();
    return new EfCoreDataBaseTransaction(transaction, connectionString);
}

public IDataBaseTransaction CurrentTransaction 
{
    get
    {
        if (Database.CurrentTransaction == null)
            return null;
            
        var connectionString = Database.GetConnectionString();
        return new EfCoreDataBaseTransaction(Database.CurrentTransaction, connectionString);
    }
}
```

---

### 方案三：扩展 IDataBaseTransaction 接口（不推荐）

#### 设计思路

为 `IDataBaseTransaction` 添加 `ConnectionString` 属性，但让它成为可选的。

#### 优点
- ✅ 接口更完整

#### 缺点
- ❌ 违反了单一职责原则
- ❌ Transaction 不应该关心连接配置
- ❌ 会影响所有 ORM 实现

#### 不推荐的原因

ConnectionString 是 **Context 级别**的配置，不应该在 **Transaction 级别**暴露。这会导致：
- 职责混乱
- 不同 ORM 的行为不一致
- 增加不必要的复杂度

---

## 📊 方案对比

| 特性 | 方案一：持有 Context 引用 | 方案二：传入必要信息 | 方案三：扩展接口 |
|-----|------------------------|------------------|---------------|
| **灵活性** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐ |
| **解耦性** | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐ |
| **扩展性** | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ |
| **易用性** | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ |
| **符合 OOP 原则** | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ |
| **推荐度** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐ |

## 🎯 推荐方案

### ⭐ 方案一：Transaction 持有 DbContext 引用

**理由**：
1. **最灵活**：Transaction 可以访问 Context 的任何公共属性
2. **易于扩展**：未来如果需要其他信息，不需要修改构造函数
3. **符合 EF Core 设计**：EF Core 的 Transaction 本身就与 DbContext 关联
4. **生命周期清晰**：DbContext 管理 Transaction 的生命周期

**注意事项**：
- ⚠️ 不要在 Transaction.Dispose() 中释放 DbContext
- ⚠️ 小心循环引用（实际上不会有问题，因为是单向引用）
- ⚠️ Transaction 的生命周期应该小于 DbContext

---

## 🔧 命名规范统一建议

### 当前问题
- `IDataBaseContext` vs `IDbContext`
- `IDataBaseTransaction` vs `IDbTransaction`
- `IDataBaseSet` vs `IDbSet`

### 建议统一为 `IDb*` 前缀

**理由**：
1. ✅ 更简洁
2. ✅ 与 EF Core 命名保持一致（`DbContext`, `DbSet`）
3. ✅ 行业标准惯例

### 重命名建议

```
IDataBaseContext     → IDbContext
IDataBaseTransaction → IDbTransaction  
IDataBaseSet         → IDbSet
```

---

## 📝 实施步骤

### 步骤 1：更新 Transaction 实现
```csharp
// 修改 EfCoreDataBaseTransaction 构造函数，添加 DbContext 参数
public EfCoreDataBaseTransaction(IDbContextTransaction transaction, DbContext dbContext)
```

### 步骤 2：更新 DbContext 实现
```csharp
// 在创建 Transaction 时传入 this
return new EfCoreDataBaseTransaction(transaction, this);
```

### 步骤 3：添加 ConnectionString 属性（可选）
```csharp
// 在 EfCoreDataBaseTransaction 中添加
public string ConnectionString => _dbContext.Database.GetConnectionString();
```

### 步骤 4：考虑是否需要扩展接口
```csharp
// 如果需要标准化，可以扩展 IDataBaseTransaction
public interface IDataBaseTransactionWithContext : IDataBaseTransaction
{
    string ConnectionString { get; }
    object GetDbContext();
}
```

---

## 🎨 完整示例代码

### 改进后的 EfCoreDataBaseTransaction.cs

```csharp
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using CrestCreates.DbContextProvider.Abstract;

namespace CrestCreates.OrmProviders.EFCore.DbContexts
{
    /// <summary>
    /// EF Core 数据库事务包装器
    /// 提供对 EF Core 事务的统一抽象访问
    /// </summary>
    public class EfCoreDataBaseTransaction : IDataBaseTransaction
    {
        private readonly IDbContextTransaction _transaction;
        private readonly DbContext _dbContext;
        private bool _isCommitted = false;
        private bool _isRolledBack = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="transaction">EF Core 原生事务对象</param>
        /// <param name="dbContext">关联的数据库上下文</param>
        /// <exception cref="ArgumentNullException">当参数为 null 时抛出</exception>
        public EfCoreDataBaseTransaction(IDbContextTransaction transaction, DbContext dbContext)
        {
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            TransactionId = Guid.NewGuid();
        }

        /// <inheritdoc />
        public Guid TransactionId { get; }

        /// <inheritdoc />
        public IsolationLevel IsolationLevel => _transaction.IsolationLevel;

        /// <summary>
        /// 获取数据库连接字符串
        /// </summary>
        /// <remarks>
        /// 从关联的 DbContext 获取连接字符串
        /// </remarks>
        public string ConnectionString => _dbContext.Database.GetConnectionString();

        /// <inheritdoc />
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_isCommitted || _isRolledBack)
            {
                throw new InvalidOperationException(
                    $"Transaction {TransactionId} has already been completed. " +
                    $"IsCommitted: {_isCommitted}, IsRolledBack: {_isRolledBack}");
            }

            try
            {
                await _transaction.CommitAsync(cancellationToken);
                _isCommitted = true;
            }
            catch (Exception)
            {
                // 如果提交失败，尝试回滚
                if (!_isRolledBack)
                {
                    try
                    {
                        await _transaction.RollbackAsync(cancellationToken);
                        _isRolledBack = true;
                    }
                    catch
                    {
                        // 忽略回滚异常，重新抛出原始异常
                    }
                }
                throw;
            }
        }

        /// <inheritdoc />
        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_isCommitted || _isRolledBack)
            {
                throw new InvalidOperationException(
                    $"Transaction {TransactionId} has already been completed. " +
                    $"IsCommitted: {_isCommitted}, IsRolledBack: {_isRolledBack}");
            }

            await _transaction.RollbackAsync(cancellationToken);
            _isRolledBack = true;
        }

        /// <inheritdoc />
        public object GetNativeTransaction() => _transaction;

        /// <summary>
        /// 获取关联的数据库上下文
        /// </summary>
        /// <returns>原生 DbContext 对象</returns>
        public DbContext GetDbContext() => _dbContext;

        /// <inheritdoc />
        public bool IsCommitted => _isCommitted;

        /// <inheritdoc />
        public bool IsRolledBack => _isRolledBack;

        /// <inheritdoc />
        public bool IsCompleted => _isCommitted || _isRolledBack;

        /// <inheritdoc />
        public void Dispose()
        {
            // 只释放事务，不释放 DbContext
            // DbContext 的生命周期由 DI 容器或调用者管理
            _transaction?.Dispose();
        }
    }
}
```

### 改进后的 CrestCreatesDbContext.cs

```csharp
public async Task<IDataBaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
{
    var transaction = await Database.BeginTransactionAsync(cancellationToken);
    // 传入 this 引用，让 Transaction 可以访问 DbContext 的属性
    return new EfCoreDataBaseTransaction(transaction, this);
}

public IDataBaseTransaction CurrentTransaction 
{
    get
    {
        if (Database.CurrentTransaction == null)
            return null;
            
        // 返回包装后的事务对象
        return new EfCoreDataBaseTransaction(Database.CurrentTransaction, this);
    }
}
```

---

## 🚀 其他 ORM 实现参考

### FreeSql 实现

```csharp
public class FreeSqlDataBaseTransaction : IDataBaseTransaction
{
    private readonly DbTransaction _transaction;
    private readonly IFreeSql _freeSql;
    
    public FreeSqlDataBaseTransaction(DbTransaction transaction, IFreeSql freeSql)
    {
        _transaction = transaction;
        _freeSql = freeSql;
    }
    
    public string ConnectionString => _freeSql.Ado.ConnectionString;
    public IsolationLevel IsolationLevel => _transaction.IsolationLevel;
    
    // ... 其他实现
}
```

### SqlSugar 实现

```csharp
public class SqlSugarDataBaseTransaction : IDataBaseTransaction
{
    private readonly DbTransaction _transaction;
    private readonly ISqlSugarClient _sqlSugarClient;
    
    public SqlSugarDataBaseTransaction(DbTransaction transaction, ISqlSugarClient client)
    {
        _transaction = transaction;
        _sqlSugarClient = client;
    }
    
    public string ConnectionString => _sqlSugarClient.Ado.Connection.ConnectionString;
    public IsolationLevel IsolationLevel => _transaction.IsolationLevel;
    
    // ... 其他实现
}
```

---

## 📚 相关文档

- [EF Core Transaction Documentation](https://docs.microsoft.com/en-us/ef/core/saving/transactions)
- [DbContext Lifetime](https://docs.microsoft.com/en-us/ef/core/dbcontext-configuration/)
- [Transaction Management Best Practices](https://docs.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/implement-resilient-entity-framework-core-sql-connections)

---

## ✅ 总结

1. **推荐使用方案一**：让 Transaction 持有 DbContext 引用
2. **IsolationLevel 无需改进**：当前实现已经正确
3. **ConnectionString 通过 DbContext 获取**：符合架构设计原则
4. **统一命名规范**：建议使用 `IDb*` 前缀
5. **注意生命周期管理**：Transaction 不应该释放 DbContext

---

*文档版本: 1.0*  
*创建日期: 2025-11-01*
