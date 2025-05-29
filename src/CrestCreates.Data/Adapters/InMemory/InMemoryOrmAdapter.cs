using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Context;
using CrestCreates.Data.Repository;
using CrestCreates.Data.UnitOfWork;

namespace CrestCreates.Data.Adapters.InMemory
{
    /// <summary>
    /// 内存ORM适配器（用于测试）
    /// </summary>
    public class InMemoryOrmAdapter : OrmAdapterBase
    {
        public override string Name => "InMemory";
        public override OrmType OrmType => OrmType.Custom;

        public override bool SupportsDatabase(DatabaseType databaseType)
        {
            return databaseType == DatabaseType.InMemory || databaseType == DatabaseType.SQLite;
        }

        public override IDbContext CreateDbContext(string connectionString, DatabaseOptions? options = null)
        {
            return new InMemoryDbContext();
        }

        public override IRepository<TEntity> CreateRepository<TEntity>(IDbContext dbContext)
        {
            return new InMemoryRepository<TEntity>((InMemoryDbContext)dbContext);
        }

        public override IUnitOfWork CreateUnitOfWork(IDbContext dbContext)
        {
            return new InMemoryUnitOfWork((InMemoryDbContext)dbContext);
        }
    }

    /// <summary>
    /// 内存数据库上下文
    /// </summary>
    public class InMemoryDbContext : IDbContext
    {
        private readonly ConcurrentDictionary<Type, object> _sets = new();
        private readonly List<object> _changeTracker = new();
        private InMemoryTransaction? _currentTransaction;
        private bool _disposed;

        public IDbSet<TEntity> Set<TEntity>() where TEntity : class
        {
            return (IDbSet<TEntity>)_sets.GetOrAdd(typeof(TEntity), _ => new InMemoryDbSet<TEntity>());
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // 内存实现中，变更会立即生效
            var changeCount = _changeTracker.Count;
            _changeTracker.Clear();
            return await Task.FromResult(changeCount);
        }

        public int SaveChanges()
        {
            var changeCount = _changeTracker.Count;
            _changeTracker.Clear();
            return changeCount;
        }

        public async Task<IDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            _currentTransaction = new InMemoryTransaction();
            return await Task.FromResult(_currentTransaction);
        }        public IDbTransaction? CurrentTransaction => _currentTransaction;

        public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
        {
            // 内存数据库总是可以连接
            return await Task.FromResult(true);
        }

        public async Task<T[]> ExecuteQueryAsync<T>(string sql, object[]? parameters = null, CancellationToken cancellationToken = default)
        {
            // 内存数据库不支持原始SQL查询，返回空数组
            return await Task.FromResult(Array.Empty<T>());
        }

        public async Task<int> ExecuteCommandAsync(string sql, object[]? parameters = null, CancellationToken cancellationToken = default)
        {
            // 内存数据库不支持原始SQL命令，返回0
            return await Task.FromResult(0);
        }

        public void Entry<TEntity>(TEntity entity) where TEntity : class
        {
            _changeTracker.Add(entity);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _currentTransaction?.Dispose();
                _sets.Clear();
                _changeTracker.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 内存数据库集合
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    public class InMemoryDbSet<TEntity> : IDbSet<TEntity> where TEntity : class
    {
        private readonly List<TEntity> _entities = new();
        private readonly object _lock = new();

        public void Add(TEntity entity)
        {
            lock (_lock)
            {
                _entities.Add(entity);
            }
        }

        public void AddRange(IEnumerable<TEntity> entities)
        {
            lock (_lock)
            {
                _entities.AddRange(entities);
            }
        }

        public void Update(TEntity entity)
        {
            // 在内存实现中，引用类型的更新会自动生效
        }

        public void UpdateRange(IEnumerable<TEntity> entities)
        {
            // 在内存实现中，引用类型的更新会自动生效
        }

        public void Remove(TEntity entity)
        {
            lock (_lock)
            {
                _entities.Remove(entity);
            }
        }

        public void RemoveRange(IEnumerable<TEntity> entities)
        {
            lock (_lock)
            {
                foreach (var entity in entities.ToList())
                {
                    _entities.Remove(entity);
                }
            }
        }

        public TEntity? Find(params object[] keyValues)
        {
            if (keyValues.Length == 0) return null;
            
            lock (_lock)
            {
                // 简单的主键查找实现
                return _entities.FirstOrDefault(e => GetEntityId(e)?.Equals(keyValues[0]) == true);
            }
        }

        public async Task<TEntity?> FindAsync(params object[] keyValues)
        {
            return await Task.FromResult(Find(keyValues));
        }

        public async Task<TEntity?> FindAsync(CancellationToken cancellationToken, params object[] keyValues)
        {
            return await Task.FromResult(Find(keyValues));
        }

        public IQueryable<TEntity> AsQueryable()
        {
            lock (_lock)
            {
                return _entities.AsQueryable();
            }
        }

        private object? GetEntityId(TEntity entity)
        {
            // 尝试获取实体的Id属性
            var idProperty = typeof(TEntity).GetProperty("Id") ?? typeof(TEntity).GetProperty("ID");
            return idProperty?.GetValue(entity);
        }
    }

    /// <summary>
    /// 内存仓储实现
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    public class InMemoryRepository<TEntity> : RepositoryBase<TEntity> where TEntity : class
    {
        private readonly InMemoryDbContext _inMemoryContext;

        public InMemoryRepository(InMemoryDbContext dbContext) : base(dbContext)
        {
            _inMemoryContext = dbContext;
        }        public override async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            var result = await base.AddAsync(entity, cancellationToken);
            _inMemoryContext.Entry(entity);
            return result;
        }
    }    /// <summary>
    /// 内存工作单元实现
    /// </summary>
    public class InMemoryUnitOfWork : UnitOfWorkBase
    {
        private readonly InMemoryDbContext _inMemoryContext;

        public InMemoryUnitOfWork(InMemoryDbContext dbContext) : base(dbContext)
        {
            _inMemoryContext = dbContext;
        }

        protected override IRepository<TEntity> CreateRepository<TEntity>() where TEntity : class
        {
            return new InMemoryRepository<TEntity>(_inMemoryContext);
        }
    }    /// <summary>
    /// 内存事务实现
    /// </summary>
    public class InMemoryTransaction : IDbTransaction
    {
        private bool _disposed;

        public Guid TransactionId { get; } = Guid.NewGuid();

        public void Commit()
        {
            // 内存实现中，提交是立即的
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
        }

        public void Rollback()
        {
            // 内存实现中，回滚操作可以是空的
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
