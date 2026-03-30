using System;
using System.Data;
using FreeSql;

namespace CrestCreates.OrmProviders.FreeSqlProvider.UnitOfWork
{
    /// <summary>
    /// FreeSql 工作单元管理器
    /// 基于官方 UnitOfWorkManager 的封装，支持事务传播和多仓储事务管理
    /// </summary>
    /// <remarks>
    /// 官方文档: https://freesql.net/guide/unitofwork-manager.html
    /// </remarks>
    public class FreeSqlUnitOfWorkManager : IDisposable
    {
        private readonly UnitOfWorkManager _unitOfWorkManager;
        private bool _disposed;

        /// <summary>
        /// 获取当前的 IFreeSql 实例
        /// </summary>
        /// <remarks>
        /// 此实例会自动跟随 UowManager 切换事务
        /// </remarks>
        public IFreeSql Orm => _unitOfWorkManager.Orm;

        /// <summary>
        /// 获取当前的工作单元
        /// </summary>
        public IUnitOfWork Current => _unitOfWorkManager.Current;

        public FreeSqlUnitOfWorkManager(IFreeSql freeSql)
        {
            _unitOfWorkManager = new UnitOfWorkManager(freeSql);
        }

        /// <summary>
        /// 将仓储绑定到当前工作单元管理器
        /// </summary>
        /// <param name="repository">仓储实例</param>
        public void Binding(FreeSql.IBaseRepository repository)
        {
            _unitOfWorkManager.Binding(repository);
        }

        /// <summary>
        /// 开始一个新的工作单元
        /// </summary>
        /// <param name="propagation">事务传播方式</param>
        /// <param name="isolationLevel">事务隔离级别</param>
        /// <returns>工作单元实例</returns>
        public IUnitOfWork Begin(Propagation propagation = Propagation.Required, IsolationLevel? isolationLevel = null)
        {
            return _unitOfWorkManager.Begin(propagation, isolationLevel);
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
                _unitOfWorkManager?.Dispose();
            }

            _disposed = true;
        }

        ~FreeSqlUnitOfWorkManager()
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// 事务传播方式枚举
    /// </summary>
    /// <remarks>
    /// 支持 6 种传播方式，与 Spring Framework 的事务传播机制一致
    /// </remarks>
    public enum TransactionPropagation
    {
        /// <summary>
        /// 如果当前没有事务，就新建一个事务；如果已存在一个事务，加入到这个事务中（默认）
        /// </summary>
        Required = 0,

        /// <summary>
        /// 支持当前事务，如果没有当前事务，就以非事务方法执行
        /// </summary>
        Supports = 1,

        /// <summary>
        /// 使用当前事务，如果没有当前事务，就抛出异常
        /// </summary>
        Mandatory = 2,

        /// <summary>
        /// 以非事务方式执行操作，如果当前存在事务，就把当前事务挂起
        /// </summary>
        NotSupported = 3,

        /// <summary>
        /// 以非事务方式执行操作，如果当前事务存在则抛出异常
        /// </summary>
        Never = 4,

        /// <summary>
        /// 以嵌套事务方式执行
        /// </summary>
        Nested = 5
    }
}
