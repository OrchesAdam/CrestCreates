using System;
using CrestCreates.Domain.UnitOfWork;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 工作单元工厂：按显式 Provider 绑定在调用方作用域内解析工作单元。
    /// </summary>
    /// <remarks>
    /// 绑定由各 Provider 包在装配期注册（<see cref="UnitOfWorkServiceCollectionExtensions.AddUnitOfWorkProvider"/>），
    /// 工厂只消费强类型委托索引：无 <c>Type.GetType</c>、无程序集扫描、无反射回退、无吞异常分支。
    /// 缺少绑定或依赖缺失给出确定性异常，不返回 null、不跳过、不自动切换默认 Provider。
    /// </remarks>
    public class UnitOfWorkFactory : IUnitOfWorkFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly UnitOfWorkProviderBindingRegistry _bindings;

        /// <summary>
        /// 构造函数（DI 主链）
        /// </summary>
        /// <param name="serviceProvider">当前作用域的服务提供者</param>
        /// <param name="bindings">装配完成的 Provider 绑定索引</param>
        public UnitOfWorkFactory(IServiceProvider serviceProvider, UnitOfWorkProviderBindingRegistry bindings)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        }

        /// <summary>
        /// 根据指定的 ORM 提供者创建工作单元
        /// </summary>
        /// <param name="provider">ORM 提供者类型</param>
        /// <returns>工作单元实例（从当前作用域解析）</returns>
        /// <exception cref="InvalidOperationException">该 Provider 未注册绑定或依赖无法解析时抛出</exception>
        public virtual IUnitOfWork Create(OrmProvider provider)
        {
            var binding = _bindings.GetRequired(provider);
            return binding.Factory(_serviceProvider)
                ?? throw new InvalidOperationException(
                    $"The unit-of-work binding for provider '{provider}' returned null. " +
                    "Bindings must resolve a unit of work or throw.");
        }
    }
}
