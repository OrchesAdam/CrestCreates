using System;
using CrestCreates.Data.Context;
using CrestCreates.Data.Repository;
using CrestCreates.Data.UnitOfWork;

namespace CrestCreates.Data.Adapters
{
    /// <summary>
    /// ORM适配器基础抽象类
    /// </summary>
    public abstract class OrmAdapterBase : IOrmAdapter, IDisposable
    {
        private bool _disposed;

        public abstract string Name { get; }
        public abstract OrmType OrmType { get; }

        public abstract IDbContext CreateDbContext(string connectionString, DatabaseOptions? options = null);
        public abstract IRepository<TEntity> CreateRepository<TEntity>(IDbContext dbContext) where TEntity : class;
        public abstract IUnitOfWork CreateUnitOfWork(IDbContext dbContext);
        public abstract bool SupportsDatabase(DatabaseType databaseType);

        public virtual void Initialize(DatabaseOptions options)
        {
            // 基础初始化逻辑，子类可以重写
            ValidateOptions(options);
        }

        /// <summary>
        /// 验证配置选项
        /// </summary>
        /// <param name="options">配置选项</param>
        protected virtual void ValidateOptions(DatabaseOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(options));

            if (!SupportsDatabase(options.DatabaseType))
                throw new NotSupportedException($"Database type '{options.DatabaseType}' is not supported by '{Name}' adapter.");
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                    OnDisposing();
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// 释放资源时调用，子类可重写
        /// </summary>
        protected virtual void OnDisposing()
        {
            // 子类实现具体的资源释放逻辑
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
