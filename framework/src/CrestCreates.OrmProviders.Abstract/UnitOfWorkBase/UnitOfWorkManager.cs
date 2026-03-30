using System;
using System.Threading.Tasks;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.OrmProviders.Abstract
{
    /// <summary>
    /// 工作单元管理器基类实现
    /// </summary>
    /// <remarks>
    /// 提供工作单元生命周期管理的默认实现
    /// 支持嵌套工作单元和自动事务管理
    /// </remarks>
    public class UnitOfWorkManager : IUnitOfWorkManager
    {
        private readonly IUnitOfWorkFactory _factory;
        private readonly OrmProvider _defaultProvider;
        private IUnitOfWork _current;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="factory">工作单元工厂</param>
        /// <param name="defaultProvider">默认 ORM 提供者</param>
        public UnitOfWorkManager(IUnitOfWorkFactory factory, OrmProvider defaultProvider = OrmProvider.EfCore)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _defaultProvider = defaultProvider;
        }

        /// <summary>
        /// 获取当前工作单元
        /// </summary>
        /// <exception cref="InvalidOperationException">当前没有活动的工作单元</exception>
        public IUnitOfWork Current
        {
            get
            {
                if (_current == null)
                {
                    throw new InvalidOperationException("No active unit of work. Call Begin() first.");
                }
                return _current;
            }
        }

        /// <summary>
        /// 开始新的工作单元
        /// </summary>
        /// <param name="provider">ORM 提供者，null 使用默认提供者</param>
        /// <returns>新的工作单元实例</returns>
        /// <exception cref="InvalidOperationException">已存在活动的工作单元</exception>
        public IUnitOfWork Begin(OrmProvider? provider = null)
        {
            if (_current != null)
            {
                throw new InvalidOperationException("A unit of work is already active.");
            }

            _current = _factory.Create(provider ?? _defaultProvider);
            return _current;
        }

        /// <summary>
        /// 同步执行操作
        /// </summary>
        /// <typeparam name="TResult">返回值类型</typeparam>
        /// <param name="action">要执行的操作</param>
        /// <param name="provider">ORM 提供者</param>
        /// <returns>操作结果</returns>
        public TResult Execute<TResult>(Func<IUnitOfWork, TResult> action, OrmProvider? provider = null)
        {
            using (var uow = _factory.Create(provider ?? _defaultProvider))
            {
                var previousUow = _current;
                _current = uow;

                try
                {
                    uow.BeginTransactionAsync().GetAwaiter().GetResult();
                    var result = action(uow);
                    uow.CommitTransactionAsync().GetAwaiter().GetResult();
                    return result;
                }
                catch
                {
                    uow.RollbackTransactionAsync().GetAwaiter().GetResult();
                    throw;
                }
                finally
                {
                    _current = previousUow;
                }
            }
        }

        /// <summary>
        /// 异步执行操作
        /// </summary>
        /// <typeparam name="TResult">返回值类型</typeparam>
        /// <param name="action">要执行的异步操作</param>
        /// <param name="provider">ORM 提供者</param>
        /// <returns>操作结果的异步任务</returns>
        public async Task<TResult> ExecuteAsync<TResult>(
            Func<IUnitOfWork, Task<TResult>> action,
            OrmProvider? provider = null)
        {
            using (var uow = _factory.Create(provider ?? _defaultProvider))
            {
                var previousUow = _current;
                _current = uow;

                try
                {
                    await uow.BeginTransactionAsync();
                    var result = await action(uow);
                    await uow.CommitTransactionAsync();
                    return result;
                }
                catch
                {
                    await uow.RollbackTransactionAsync();
                    throw;
                }
                finally
                {
                    _current = previousUow;
                }
            }
        }
    }
}
