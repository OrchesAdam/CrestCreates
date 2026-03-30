using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;

namespace CrestCreates.OrmProviders.FreeSqlProvider.Attributes
{
    /// <summary>
    /// 事务特性（基于 Rougamo AOP）
    /// </summary>
    /// <remarks>
    /// 使用方法：
    /// 1. 安装 NuGet 包：Rougamo.Fody
    /// 2. 在方法上添加 [Transactional] 特性
    /// 3. 在中间件中调用 TransactionalAttribute.SetServiceProvider(context.RequestServices)
    /// 
    /// 示例:
    /// <code>
    /// [Transactional(Propagation.Required)]
    /// public async Task CreateOrder()
    /// {
    ///     await _orderRepository.InsertAsync(order);
    ///     await _orderItemRepository.InsertAsync(orderItem);
    /// }
    /// </code>
    /// 
    /// 官方文档: https://freesql.net/guide/unitofwork-manager.html
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class TransactionalAttribute : Attribute
    {
        /// <summary>
        /// 事务传播方式
        /// </summary>
        public Propagation Propagation { get; set; } = Propagation.Required;

        /// <summary>
        /// 事务隔离级别
        /// </summary>
        public IsolationLevel IsolationLevel
        {
            get => _isolationLevel ?? System.Data.IsolationLevel.ReadCommitted;
            set => _isolationLevel = value;
        }
        private IsolationLevel? _isolationLevel;

        /// <summary>
        /// 服务提供者（通过中间件设置）
        /// </summary>
        private static AsyncLocal<IServiceProvider> _serviceProvider = new AsyncLocal<IServiceProvider>();

        /// <summary>
        /// 设置服务提供者（在中间件中调用）
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        public static void SetServiceProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider.Value = serviceProvider;
        }

        /// <summary>
        /// 获取当前服务提供者
        /// </summary>
        protected static IServiceProvider ServiceProvider => _serviceProvider.Value;
    }

    #region Rougamo 实现版本（需要安装 Rougamo.Fody）

    /*
    /// <summary>
    /// 事务特性（Rougamo AOP 实现）
    /// </summary>
    /// <remarks>
    /// 使用前需要：
    /// 1. dotnet add package Rougamo.Fody
    /// 2. 在 Startup.cs 的 Configure 方法中添加：
    ///    app.Use(async (context, next) => {
    ///        TransactionalAttribute.SetServiceProvider(context.RequestServices);
    ///        await next();
    ///    });
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method)]
    public class TransactionalAttribute : Rougamo.MoAttribute
    {
        public Propagation Propagation { get; set; } = Propagation.Required;
        public IsolationLevel IsolationLevel { get => m_IsolationLevel.Value; set => m_IsolationLevel = value; }
        IsolationLevel? m_IsolationLevel;

        static AsyncLocal<IServiceProvider> m_ServiceProvider = new AsyncLocal<IServiceProvider>();
        public static void SetServiceProvider(IServiceProvider serviceProvider) => m_ServiceProvider.Value = serviceProvider;

        IUnitOfWork _uow;

        public override void OnEntry(Rougamo.Context.MethodContext context)
        {
            var uowManager = m_ServiceProvider.Value.GetService<UnitOfWork.FreeSqlUnitOfWorkManager>();
            _uow = uowManager.Begin(this.Propagation, this.m_IsolationLevel);
        }

        public override void OnExit(Rougamo.Context.MethodContext context)
        {
            if (typeof(Task).IsAssignableFrom(context.RealReturnType) && context.ReturnValue != null && context.ReturnValue is Task)
                ((Task)context.ReturnValue).ContinueWith(t => _OnExit(context));
            else 
                _OnExit(context);
        }

        void _OnExit(Rougamo.Context.MethodContext context)
        {
            try
            {
                if (context.Exception == null) 
                    _uow.Commit();
                else 
                    _uow.Rollback();
            }
            finally
            {
                _uow.Dispose();
            }
        }
    }
    */

    #endregion

    #region 手动事务辅助类（不使用 AOP 时的替代方案）

    /// <summary>
    /// 手动事务辅助类
    /// </summary>
    /// <remarks>
    /// 如果不想使用 AOP，可以使用此辅助类手动管理事务：
    /// <code>
    /// await TransactionHelper.ExecuteAsync(uowManager, async () =>
    /// {
    ///     await _orderRepository.InsertAsync(order);
    ///     await _orderItemRepository.InsertAsync(orderItem);
    /// });
    /// </code>
    /// </remarks>
    public static class TransactionHelper
    {
        /// <summary>
        /// 在事务中执行操作
        /// </summary>
        public static async Task ExecuteAsync(
            UnitOfWork.FreeSqlUnitOfWorkManager uowManager,
            Func<Task> action,
            Propagation propagation = Propagation.Required,
            IsolationLevel? isolationLevel = null)
        {
            using (var uow = uowManager.Begin(propagation, isolationLevel))
            {
                try
                {
                    await action();
                    uow.Commit();
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// 在事务中执行操作并返回结果
        /// </summary>
        public static async Task<TResult> ExecuteAsync<TResult>(
            UnitOfWork.FreeSqlUnitOfWorkManager uowManager,
            Func<Task<TResult>> action,
            Propagation propagation = Propagation.Required,
            IsolationLevel? isolationLevel = null)
        {
            using (var uow = uowManager.Begin(propagation, isolationLevel))
            {
                try
                {
                    var result = await action();
                    uow.Commit();
                    return result;
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// 同步版本：在事务中执行操作
        /// </summary>
        public static void Execute(
            UnitOfWork.FreeSqlUnitOfWorkManager uowManager,
            Action action,
            Propagation propagation = Propagation.Required,
            IsolationLevel? isolationLevel = null)
        {
            using (var uow = uowManager.Begin(propagation, isolationLevel))
            {
                try
                {
                    action();
                    uow.Commit();
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// 同步版本：在事务中执行操作并返回结果
        /// </summary>
        public static TResult Execute<TResult>(
            UnitOfWork.FreeSqlUnitOfWorkManager uowManager,
            Func<TResult> action,
            Propagation propagation = Propagation.Required,
            IsolationLevel? isolationLevel = null)
        {
            using (var uow = uowManager.Begin(propagation, isolationLevel))
            {
                try
                {
                    var result = action();
                    uow.Commit();
                    return result;
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }
    }

    #endregion
}
