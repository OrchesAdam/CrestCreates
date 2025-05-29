using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Context;
using CrestCreates.Data.Repository;

namespace CrestCreates.Data.UnitOfWork
{
    /// <summary>
    /// 基础工作单元实现
    /// </summary>
    public abstract class UnitOfWorkBase : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repositories = new();
        private bool _disposed;

        protected UnitOfWorkBase(IDbContext dbContext)
        {
            DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public IDbContext DbContext { get; }

        public virtual IRepository<TEntity> GetRepository<TEntity>() where TEntity : class
        {
            var entityType = typeof(TEntity);
            
            if (_repositories.TryGetValue(entityType, out var repository))
            {
                return (IRepository<TEntity>)repository;
            }

            var newRepository = CreateRepository<TEntity>();
            _repositories[entityType] = newRepository;
            return newRepository;
        }

        /// <summary>
        /// 创建仓储实例
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <returns>仓储实例</returns>
        protected abstract IRepository<TEntity> CreateRepository<TEntity>() where TEntity : class;

        public virtual async Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            return await DbContext.SaveChangesAsync(cancellationToken);
        }

        public virtual int Commit()
        {
            return DbContext.SaveChanges();
        }

        public virtual async Task<IDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return await DbContext.BeginTransactionAsync(cancellationToken);
        }

        public virtual IDbTransaction? CurrentTransaction => DbContext.CurrentTransaction;

        public virtual async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            var transaction = CurrentTransaction;
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }

        public virtual async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            var transaction = CurrentTransaction;
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
        }

        public virtual async Task<T[]> ExecuteQueryAsync<T>(string sql, object[]? parameters = null,
            CancellationToken cancellationToken = default)
        {
            return await DbContext.ExecuteQueryAsync<T>(sql, parameters, cancellationToken);
        }

        public virtual async Task<int> ExecuteCommandAsync(string sql, object[]? parameters = null,
            CancellationToken cancellationToken = default)
        {
            return await DbContext.ExecuteCommandAsync(sql, parameters, cancellationToken);
        }

        public virtual async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
        {
            return await DbContext.CanConnectAsync(cancellationToken);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    DbContext?.Dispose();
                    _repositories.Clear();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
