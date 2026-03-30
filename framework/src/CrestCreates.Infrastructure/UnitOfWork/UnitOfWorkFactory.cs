using System;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.OrmProviders.EFCore.UnitOfWork;
using CrestCreates.OrmProviders.SqlSugar.UnitOfWork;
using CrestCreates.OrmProviders.FreeSqlProvider.UnitOfWork;

namespace CrestCreates.Infrastructure.UnitOfWork
{
    /// <summary>
    /// ORM 提供者类型
    /// </summary>
    public enum OrmProvider
    {
        EfCore,
        SqlSugar,
        FreeSql
    }

    /// <summary>
    /// 工作单元工厂接口
    /// </summary>
    public interface IUnitOfWorkFactory
    {
        /// <summary>
        /// 创建工作单元实例
        /// </summary>
        IUnitOfWork Create(OrmProvider provider);
    }

    /// <summary>
    /// 工作单元工厂实现
    /// </summary>
    public class UnitOfWorkFactory : IUnitOfWorkFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public UnitOfWorkFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// 根据指定的 ORM 提供者创建工作单元
        /// </summary>
        public IUnitOfWork Create(OrmProvider provider)
        {
            return provider switch
            {
                OrmProvider.EfCore => _serviceProvider.GetRequiredService<EfCoreUnitOfWork>(),
                OrmProvider.SqlSugar => _serviceProvider.GetRequiredService<SqlSugarUnitOfWork>(),
                OrmProvider.FreeSql => _serviceProvider.GetRequiredService<FreeSqlUnitOfWork>(),
                _ => throw new NotSupportedException($"ORM provider '{provider}' is not supported")
            };
        }
    }

    /// <summary>
    /// 工作单元管理器
    /// 提供当前工作单元的访问和管理
    /// </summary>
    public interface IUnitOfWorkManager
    {
        /// <summary>
        /// 当前工作单元
        /// </summary>
        IUnitOfWork Current { get; }

        /// <summary>
        /// 开始新的工作单元
        /// </summary>
        IUnitOfWork Begin(OrmProvider? provider = null);

        /// <summary>
        /// 使用工作单元执行操作
        /// </summary>
        TResult Execute<TResult>(Func<IUnitOfWork, TResult> action, OrmProvider? provider = null);

        /// <summary>
        /// 使用工作单元执行异步操作
        /// </summary>
        System.Threading.Tasks.Task<TResult> ExecuteAsync<TResult>(
            Func<IUnitOfWork, System.Threading.Tasks.Task<TResult>> action, 
            OrmProvider? provider = null);
    }

    /// <summary>
    /// 工作单元管理器实现
    /// </summary>
    public class UnitOfWorkManager : IUnitOfWorkManager
    {
        private readonly IUnitOfWorkFactory _factory;
        private readonly OrmProvider _defaultProvider;
        private IUnitOfWork _current;

        public UnitOfWorkManager(IUnitOfWorkFactory factory, OrmProvider defaultProvider = OrmProvider.EfCore)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _defaultProvider = defaultProvider;
        }

        /// <summary>
        /// 获取当前工作单元
        /// </summary>
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
        public async System.Threading.Tasks.Task<TResult> ExecuteAsync<TResult>(
            Func<IUnitOfWork, System.Threading.Tasks.Task<TResult>> action, 
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

    /// <summary>
    /// 工作单元扩展方法
    /// </summary>
    public static class UnitOfWorkExtensions
    {
        /// <summary>
        /// 注册工作单元服务
        /// </summary>
        public static IServiceCollection AddUnitOfWork(
            this IServiceCollection services, 
            OrmProvider defaultProvider = OrmProvider.EfCore)
        {
            services.AddScoped<IUnitOfWorkFactory, UnitOfWorkFactory>();
            services.AddScoped<IUnitOfWorkManager>(sp => 
                new UnitOfWorkManager(sp.GetRequiredService<IUnitOfWorkFactory>(), defaultProvider));

            return services;
        }
    }
}
