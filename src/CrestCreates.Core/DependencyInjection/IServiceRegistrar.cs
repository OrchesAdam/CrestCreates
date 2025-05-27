using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DependencyInjection
{
    /// <summary>
    /// 服务注册器接口
    /// </summary>
    public interface IServiceRegistrar
    {
        /// <summary>
        /// 注册服务到容器
        /// </summary>
        void RegisterServices(IServiceCollection services);
    }
}

