using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Adapters;
using CrestCreates.Data.Context;
using CrestCreates.Data.UnitOfWork;

namespace CrestCreates.Data.Providers
{
    /// <summary>
    /// 数据库提供者接口
    /// </summary>
    public interface IDatabaseProvider : IDisposable
    {
        /// <summary>
        /// 数据库配置选项
        /// </summary>
        DatabaseOptions Options { get; }

        /// <summary>
        /// ORM适配器
        /// </summary>
        IOrmAdapter Adapter { get; }

        /// <summary>
        /// 创建数据库上下文
        /// </summary>
        /// <returns>数据库上下文</returns>
        IDbContext CreateDbContext();

        /// <summary>
        /// 创建工作单元
        /// </summary>
        /// <returns>工作单元</returns>
        IUnitOfWork CreateUnitOfWork();

        /// <summary>
        /// 初始化数据库
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>Task</returns>
        Task InitializeDatabaseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查数据库连接
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否连接成功</returns>
        Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 执行数据库迁移
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>Task</returns>
        Task MigrateDatabaseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查数据库是否存在
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否存在</returns>
        Task<bool> DatabaseExistsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 创建数据库
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>Task</returns>
        Task CreateDatabaseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除数据库
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>Task</returns>
        Task DeleteDatabaseAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 数据库提供者实现
    /// </summary>
    public class DatabaseProvider : IDatabaseProvider
    {
        private readonly IOrmAdapterFactory _adapterFactory;
        private IOrmAdapter? _adapter;
        private bool _disposed;

        public DatabaseOptions Options { get; }

        public IOrmAdapter Adapter
        {
            get
            {
                if (_adapter == null)
                {
                    _adapter = _adapterFactory.GetAdapterForDatabase(Options.DatabaseType);
                    _adapter.Initialize(Options);
                }
                return _adapter;
            }
        }

        public DatabaseProvider(DatabaseOptions options, IOrmAdapterFactory adapterFactory)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _adapterFactory = adapterFactory ?? throw new ArgumentNullException(nameof(adapterFactory));
        }

        public IDbContext CreateDbContext()
        {
            return Adapter.CreateDbContext(Options.ConnectionString, Options);
        }

        public IUnitOfWork CreateUnitOfWork()
        {
            var dbContext = CreateDbContext();
            return Adapter.CreateUnitOfWork(dbContext);
        }

        public async Task InitializeDatabaseAsync(CancellationToken cancellationToken = default)
        {
            // 检查数据库是否存在，不存在则创建
            if (!await DatabaseExistsAsync(cancellationToken))
            {
                await CreateDatabaseAsync(cancellationToken);
            }

            // 如果配置了自动迁移，执行迁移
            if (Options.AutoMigrate)
            {
                await MigrateDatabaseAsync(cancellationToken);
            }
        }

        public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var dbContext = CreateDbContext();
                return await dbContext.CanConnectAsync(cancellationToken);
            }
            catch
            {
                return false;
            }
        }

        public virtual Task MigrateDatabaseAsync(CancellationToken cancellationToken = default)
        {
            // 默认实现为空，由具体的ORM适配器实现
            return Task.CompletedTask;
        }

        public virtual async Task<bool> DatabaseExistsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await CanConnectAsync(cancellationToken);
            }
            catch
            {
                return false;
            }
        }

        public virtual Task CreateDatabaseAsync(CancellationToken cancellationToken = default)
        {
            // 默认实现为空，由具体的ORM适配器实现
            return Task.CompletedTask;
        }

        public virtual Task DeleteDatabaseAsync(CancellationToken cancellationToken = default)
        {
            // 默认实现为空，由具体的ORM适配器实现
            return Task.CompletedTask;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _adapter?.Dispose();
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
