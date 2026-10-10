using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;

namespace CrestCreates.Data.Abstractions.Modules
{
    /// <summary>
    /// ORM 模块基类，继承自 ModuleBase 以融入模块生命周期
    /// </summary>
    public abstract class OrmModuleBase : ModuleBase
    {
        /// <summary>
        /// 注册 ORM 相关服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <remarks>
        /// 实现方在同一入口内注册具体工作单元与 Provider 绑定
        /// （<see cref="UnitOfWorkServiceCollectionExtensions.AddUnitOfWorkProvider"/>）。
        /// </remarks>
        public abstract void RegisterOrmServices(IServiceCollection services);

        /// <inheritdoc />
        public override void OnConfigureServices(IServiceCollection services)
        {
            // 注册工作单元基础装配（工厂 + 管理器）
            services.AddUnitOfWork();

            // 注册具体 ORM 服务（含 Provider 绑定）
            RegisterOrmServices(services);
        }
    }
}
