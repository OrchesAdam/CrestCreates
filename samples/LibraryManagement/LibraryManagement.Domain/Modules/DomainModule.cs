using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LibraryManagement.Domain.Modules;

[CrestModule(Order = -200)]
public class DomainModule : IModule
{
    public void OnPreInitialize()
    {
        // 领域层预初始化逻辑
    }

    public void OnInitialize()
    {
        // 领域层初始化逻辑
    }

    public void OnPostInitialize()
    {
        // 领域层后初始化逻辑
    }

    public void OnConfigureServices(IServiceCollection services)
    {
        // 注册领域层服务
        // 领域层通常不包含需要注册到 DI 的服务
        // 实体和值对象由仓储层管理
    }

    public void OnApplicationInitialization(IHost host)
    {
        // 应用初始化逻辑
    }
}
