using System;
using System.Threading.Tasks;
using FreeSql;
using CrestCreatesDomainUnitOfWork = CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.OrmProviders.FreeSqlProvider.UnitOfWork
{
    /// <summary>
    /// FreeSql 工作单元实现
    /// 提供事务管理和变更追踪功能
    /// </summary>
    public class FreeSqlUnitOfWork : CrestCreatesDomainUnitOfWork.IUnitOfWork
    {
        private readonly IFreeSql _freeSql;
        private FreeSql.IUnitOfWork? _unitOfWork;
        private bool _disposed;

        public FreeSqlUnitOfWork(IFreeSql freeSql)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
        }

        /// <summary>
        /// 开始事务
        /// </summary>
        public Task BeginTransactionAsync()
        {
            if (_unitOfWork != null)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            // FreeSql 的 UnitOfWork 会自动开启事务
            _unitOfWork = _freeSql.CreateUnitOfWork();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 提交事务
        /// </summary>
        public async Task CommitTransactionAsync()
        {
            if (_unitOfWork == null)
            {
                throw new InvalidOperationException("No transaction has been started");
            }

            try
            {
                await Task.Run(() => _unitOfWork.Commit());
                DisposeUnitOfWork();
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
        public Task RollbackTransactionAsync()
        {
            if (_unitOfWork == null)
            {
                return Task.CompletedTask;
            }

            try
            {
                _unitOfWork.Rollback();
            }
            finally
            {
                DisposeUnitOfWork();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 保存变更
        /// </summary>
        /// <returns>影响的行数</returns>
        public Task<int> SaveChangesAsync()
        {
            // FreeSql 使用 Commit 来保存变更
            // 这里返回0表示成功（实际影响行数在Commit时已处理）
            return Task.FromResult(0);
        }

        private void DisposeUnitOfWork()
        {
            if (_unitOfWork != null)
            {
                _unitOfWork.Dispose();
                _unitOfWork = null;
            }
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
                if (_unitOfWork != null)
                {
                    try
                    {
                        _unitOfWork.Rollback();
                    }
                    catch
                    {
                        // 忽略回滚异常
                    }
                    finally
                    {
                        DisposeUnitOfWork();
                    }
                }
            }

            _disposed = true;
        }

        ~FreeSqlUnitOfWork()
        {
            Dispose(false);
        }
    }
}
