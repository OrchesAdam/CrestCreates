using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Domain.Shared;
using CrestCreates.Domain.Shared.Enums;

namespace CrestCreates.Infrastructure.UnitOfWork
{
    /// <summary>
    /// ORM 配置选项
    /// </summary>
    public class OrmOptions
    {
        /// <summary>
        /// 配置节名称
        /// </summary>
        public const string SectionName = "Orm";

        /// <summary>
        /// 默认 ORM 提供者
        /// </summary>
        public OrmProvider DefaultProvider { get; set; } = OrmProvider.EfCore;

        /// <summary>
        /// 连接字符串
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// 验证配置
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrEmpty(ConnectionString))
            {
                throw new InvalidOperationException("ConnectionString is required");
            }
        }
    }

    /// <summary>
    /// 工作单元创建委托
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <returns>工作单元实例</returns>
    public delegate IUnitOfWork UnitOfWorkFactoryDelegate(IServiceProvider serviceProvider);

    /// <summary>
    /// 工作单元工厂接口
    /// </summary>
    public interface IUnitOfWorkFactory
    {
        /// <summary>
        /// 创建工作单元实例
        /// </summary>
        IUnitOfWork Create(OrmProvider provider);
        
        /// <summary>
        /// 注册工作单元工厂
        /// </summary>
        /// <param name="provider">ORM 提供者</param>
        /// <param name="factory">工作单元创建委托</param>
        void RegisterUnitOfWorkFactory(OrmProvider provider, UnitOfWorkFactoryDelegate factory);
    }

    /// <summary>
    /// 工作单元工厂实现
    /// </summary>
    public class UnitOfWorkFactory : IUnitOfWorkFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<OrmProvider, UnitOfWorkFactoryDelegate> _factories;
        private readonly OrmOptions _options;

        public UnitOfWorkFactory(IServiceProvider serviceProvider, Microsoft.Extensions.Options.IOptions<OrmOptions>? options = null)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _factories = new Dictionary<OrmProvider, UnitOfWorkFactoryDelegate>();
            _options = options?.Value ?? new OrmOptions();
            
            // 注册默认的工作单元工厂
            RegisterDefaultFactories();
        }

        /// <summary>
        /// 注册默认的工作单元工厂
        /// </summary>
        private void RegisterDefaultFactories()
        {
            // 尝试注册 EfCore 工作单元工厂
            try
            {
                RegisterUnitOfWorkFactory(OrmProvider.EfCore, sp =>
                {
                    var type = Type.GetType("CrestCreates.OrmProviders.EFCore.UnitOfWork.EfCoreUnitOfWork");
                    return type != null ? (IUnitOfWork)sp.GetRequiredService(type) : throw new NotSupportedException("EfCore unit of work type not found");
                });
            }
            catch { /* 忽略注册失败，可能是因为未引用 EfCore 提供者 */ }

            // 尝试注册 SqlSugar 工作单元工厂
            try
            {
                RegisterUnitOfWorkFactory(OrmProvider.SqlSugar, sp =>
                {
                    var type = Type.GetType("CrestCreates.OrmProviders.SqlSugar.UnitOfWork.SqlSugarUnitOfWork");
                    return type != null ? (IUnitOfWork)sp.GetRequiredService(type) : throw new NotSupportedException("SqlSugar unit of work type not found");
                });
            }
            catch { /* 忽略注册失败，可能是因为未引用 SqlSugar 提供者 */ }

            // 尝试注册 FreeSql 工作单元工厂
            try
            {
                RegisterUnitOfWorkFactory(OrmProvider.FreeSql, sp =>
                {
                    var type = Type.GetType("CrestCreates.OrmProviders.FreeSqlProvider.UnitOfWork.FreeSqlUnitOfWork");
                    return type != null ? (IUnitOfWork)sp.GetRequiredService(type) : throw new NotSupportedException("FreeSql unit of work type not found");
                });
            }
            catch { /* 忽略注册失败，可能是因为未引用 FreeSql 提供者 */ }
        }

        /// <summary>
        /// 注册工作单元工厂
        /// </summary>
        /// <param name="provider">ORM 提供者</param>
        /// <param name="factory">工作单元创建委托</param>
        public void RegisterUnitOfWorkFactory(OrmProvider provider, UnitOfWorkFactoryDelegate factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            
            _factories[provider] = factory;
        }

        /// <summary>
        /// 根据指定的 ORM 提供者创建工作单元
        /// </summary>
        public IUnitOfWork Create(OrmProvider provider)
        {
            if (_factories.TryGetValue(provider, out var factory))
            {
                var unitOfWork = factory(_serviceProvider);
                if (unitOfWork != null)
                {
                    return unitOfWork;
                }
            }

            // 回退到反射方式（保持向后兼容）
            return CreateUsingReflection(provider);
        }

        /// <summary>
        /// 使用反射创建工作单元（回退机制）
        /// </summary>
        private IUnitOfWork CreateUsingReflection(OrmProvider provider)
        {
            string typeName = provider switch
            {
                OrmProvider.EfCore => "CrestCreates.OrmProviders.EFCore.UnitOfWork.EfCoreUnitOfWork",
                OrmProvider.SqlSugar => "CrestCreates.OrmProviders.SqlSugar.UnitOfWork.SqlSugarUnitOfWork",
                OrmProvider.FreeSql => "CrestCreates.OrmProviders.FreeSqlProvider.UnitOfWork.FreeSqlUnitOfWork",
                _ => throw new NotSupportedException($"ORM provider '{provider}' is not supported")
            };

            // 尝试获取工作单元实例
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null && typeof(IUnitOfWork).IsAssignableFrom(type))
                {
                    return (IUnitOfWork)_serviceProvider.GetRequiredService(type);
                }
            }

            throw new NotSupportedException($"ORM provider '{provider}' is not supported or the unit of work type not found");
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
        private IUnitOfWork? _current;

        public UnitOfWorkManager(IUnitOfWorkFactory factory, OrmProvider defaultProvider = OrmProvider.EfCore)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _defaultProvider = defaultProvider;
            _current = null;
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
        /// 配置 ORM 选项
        /// </summary>
        public static IServiceCollection ConfigureOrm(
            this IServiceCollection services,
            Action<OrmOptions> configureOptions)
        {
            services.Configure(configureOptions);
            return services;
        }

        /// <summary>
        /// 从配置中加载 ORM 选项
        /// </summary>
        public static IServiceCollection ConfigureOrm(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<OrmOptions>(configuration.GetSection(OrmOptions.SectionName));
            return services;
        }

        /// <summary>
        /// 注册工作单元服务
        /// </summary>
        public static IServiceCollection AddUnitOfWork(
            this IServiceCollection services, 
            OrmProvider? defaultProvider = null)
        {
            services.AddScoped<IUnitOfWorkFactory, UnitOfWorkFactory>();
            services.AddScoped<IUnitOfWorkManager>(sp => 
            {
                var factory = sp.GetRequiredService<IUnitOfWorkFactory>();
                var options = sp.GetService<Microsoft.Extensions.Options.IOptions<OrmOptions>>();
                var provider = defaultProvider ?? options?.Value?.DefaultProvider ?? OrmProvider.EfCore;
                return new UnitOfWorkManager(factory, provider);
            });

            return services;
        }
    }
}
