using System;
using System.Threading.Tasks;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.OrmProviders.Abstract
{
    /// <summary>
    /// 工作单元管理器接口
    /// </summary>
    /// <remarks>
    /// 提供工作单元的生命周期管理，包括创建、访问当前工作单元以及事务执行
    /// </remarks>
    public interface IUnitOfWorkManager
    {
        /// <summary>
        /// 获取当前活动的工作单元，如果当前调用链没有工作单元则返回 null
        /// </summary>
        IUnitOfWork? CurrentOrNull { get; }

        /// <summary>
        /// 获取当前活动的工作单元
        /// </summary>
        /// <exception cref="InvalidOperationException">当前没有活动的工作单元时抛出</exception>
        IUnitOfWork Current { get; }

        /// <summary>
        /// 开始工作单元作用域。默认会复用当前调用链中的工作单元，除非 requiresNew 为 true。
        /// </summary>
        IUnitOfWorkScope BeginScope(
            bool isTransactional = true,
            bool requiresNew = false,
            OrmProvider? provider = null);

        /// <summary>
        /// 开始新的工作单元
        /// </summary>
        /// <param name="provider">ORM 提供者，如果为 null 则使用默认提供者</param>
        /// <returns>新创建的工作单元实例</returns>
        /// <exception cref="InvalidOperationException">已存在活动的工作单元时抛出</exception>
        IUnitOfWork Begin(OrmProvider? provider = null);

        /// <summary>
        /// 使用工作单元执行同步操作
        /// </summary>
        /// <typeparam name="TResult">操作返回值类型</typeparam>
        /// <param name="action">要执行的操作</param>
        /// <param name="provider">ORM 提供者，如果为 null 则使用默认提供者</param>
        /// <returns>操作执行结果</returns>
        TResult Execute<TResult>(Func<IUnitOfWork, TResult> action, OrmProvider? provider = null);

        /// <summary>
        /// 使用工作单元执行异步操作
        /// </summary>
        /// <typeparam name="TResult">操作返回值类型</typeparam>
        /// <param name="action">要执行的异步操作</param>
        /// <param name="provider">ORM 提供者，如果为 null 则使用默认提供者</param>
        /// <returns>操作执行结果的异步任务</returns>
        Task<TResult> ExecuteAsync<TResult>(
            Func<IUnitOfWork, Task<TResult>> action,
            OrmProvider? provider = null);
    }

    public interface IUnitOfWorkScope : IDisposable
    {
        IUnitOfWork UnitOfWork { get; }

        bool IsOwner { get; }

        bool IsTransactional { get; }
    }
}
