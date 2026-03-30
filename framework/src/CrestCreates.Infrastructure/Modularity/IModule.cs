using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Infrastructure.Modularity
{
    /// <summary>
    /// 模块生命周期接口
    /// 定义模块在应用程序启动过程中的各个生命周期钩子
    /// </summary>
    public interface IModule
    {
        /// <summary>
        /// 预初始化阶段
        /// 在模块初始化之前调用，可用于执行准备工作
        /// 按照模块依赖顺序执行
        /// </summary>
        void OnPreInitialize();

        /// <summary>
        /// 初始化阶段
        /// 模块的主要初始化逻辑
        /// 按照模块依赖顺序执行
        /// </summary>
        void OnInitialize();

        /// <summary>
        /// 后初始化阶段
        /// 在模块初始化之后调用，可用于执行收尾工作
        /// 按照模块依赖顺序执行
        /// </summary>
        void OnPostInitialize();

        /// <summary>
        /// 配置服务
        /// 用于向 DI 容器注册服务
        /// 按照模块依赖顺序执行
        /// </summary>
        /// <param name="services">服务集合</param>
        void OnConfigureServices(IServiceCollection services);

        /// <summary>
        /// 应用程序初始化
        /// 在应用程序启动时调用，可用于执行应用级的初始化逻辑
        /// 按照模块依赖顺序执行
        /// </summary>
        /// <param name="host">应用程序主机</param>
        void OnApplicationInitialization(IHost host);
    }

    
}
