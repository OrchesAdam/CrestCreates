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
            // 此方法应该在派生类中重写，或通过配置注入具体实现
            throw new NotSupportedException(
                $"ORM provider '{provider}' is not supported. " +
                $"Please override this method or configure the factory properly.");
        }
    }
}
