using System;
using System.Threading.Tasks;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.OrmProviders.Abstract
{
    /// <summary>
    /// 工作单元工厂基类实现
    /// </summary>
    /// <remarks>
    /// 提供工作单元工厂的默认实现，通过服务提供者(DI容器)创建具体的工作单元实例
    /// </remarks>
    public class UnitOfWorkFactory : IUnitOfWorkFactory
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="serviceProvider">服务提供者(DI容器)</param>
        public UnitOfWorkFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// 根据指定的 ORM 提供者创建工作单元
        /// </summary>
        /// <param name="provider">ORM 提供者类型</param>
        /// <returns>工作单元实例</returns>
        /// <exception cref="NotSupportedException">不支持的 ORM 提供者类型</exception>
        public virtual IUnitOfWork Create(OrmProvider provider)
        {
            var typeName = provider switch
            {
                OrmProvider.EfCore => "CrestCreates.OrmProviders.EFCore.UnitOfWork.EfCoreUnitOfWork",
                OrmProvider.SqlSugar => "CrestCreates.OrmProviders.SqlSugar.UnitOfWork.SqlSugarUnitOfWork",
                OrmProvider.FreeSql => "CrestCreates.OrmProviders.FreeSqlProvider.UnitOfWork.FreeSqlUnitOfWork",
                _ => throw new NotSupportedException($"ORM provider '{provider}' is not supported.")
            };

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null && typeof(IUnitOfWork).IsAssignableFrom(type))
                {
                    var unitOfWork = _serviceProvider.GetService(type) as IUnitOfWork;
                    return unitOfWork
                        ?? throw new InvalidOperationException($"Unit of work type '{typeName}' is not registered.");
                }
            }

            throw new NotSupportedException(
                $"ORM provider '{provider}' is not supported or the unit of work type '{typeName}' was not found.");
        }
    }
}
