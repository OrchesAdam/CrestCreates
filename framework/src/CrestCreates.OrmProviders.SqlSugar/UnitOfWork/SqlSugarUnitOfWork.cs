using System;
using System.Threading.Tasks;
using SqlSugar;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.OrmProviders.SqlSugar.UnitOfWork
{
    /// <summary>
    /// SqlSugar 工作单元实现
    /// 提供事务管理和变更追踪功能
    /// </summary>
    public class SqlSugarUnitOfWork : IUnitOfWork
    {
        private readonly ISqlSugarClient _sqlSugarClient;
        private bool _isTransactionStarted;
        private bool _disposed;

        public SqlSugarUnitOfWork(ISqlSugarClient sqlSugarClient)
        {
            _sqlSugarClient = sqlSugarClient ?? throw new ArgumentNullException(nameof(sqlSugarClient));
        }

        /// <summary>
        /// 开始事务
        /// </summary>
        public async Task BeginTransactionAsync()
        {
            if (_isTransactionStarted)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            await Task.Run(() => _sqlSugarClient.Ado.BeginTran());
            _isTransactionStarted = true;
        }

        /// <summary>
        /// 提交事务
        /// </summary>
        public async Task CommitTransactionAsync()
        {
            if (!_isTransactionStarted)
            {
                throw new InvalidOperationException("No transaction has been started");
            }

            try
            {
                await Task.Run(() => _sqlSugarClient.Ado.CommitTran());
                _isTransactionStarted = false;
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
        }

        /// <summary>
        /// 回滚事务
        /// </summary>
        public async Task RollbackTransactionAsync()
        {
            if (!_isTransactionStarted)
            {
                return;
            }

            try
            {
                await Task.Run(() => _sqlSugarClient.Ado.RollbackTran());
            }
            finally
            {
                _isTransactionStarted = false;
            }
        }

        /// <summary>
        /// 保存变更（SqlSugar不需要显式调用SaveChanges）
        /// </summary>
        /// <returns>影响的行数（对于SqlSugar，返回0表示成功）</returns>
        public Task<int> SaveChangesAsync()
        {
            // SqlSugar 是立即执行模式，不需要显式保存
            // 但为了与接口兼容，返回成功状态
            return Task.FromResult(0);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // 如果事务还在进行中，自动回滚
                if (_isTransactionStarted)
                {
                    try
                    {
                        _sqlSugarClient.Ado.RollbackTran();
                    }
                    catch
                    {
                        // 忽略回滚异常
                    }
                    finally
                    {
                        _isTransactionStarted = false;
                    }
                }
            }

            _disposed = true;
        }

        ~SqlSugarUnitOfWork()
        {
            Dispose(false);
        }
    }
}
