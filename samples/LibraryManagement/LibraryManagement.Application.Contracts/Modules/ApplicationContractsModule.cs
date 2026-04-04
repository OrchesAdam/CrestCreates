using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using LibraryManagement.Domain.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LibraryManagement.Application.Contracts.Modules;

[CrestModule(typeof(DomainModule), Order = -150)]
public class ApplicationContractsModule : IModule
{
    public void OnPreInitialize()
    {
        // 应用契约层预初始化逻辑
    }

    public void OnInitialize()
    {
        // 应用契约层初始化逻辑
    }

    public void OnPostInitialize()
    {
        // 应用契约层后初始化逻辑
    }

    public void OnConfigureServices(IServiceCollection services)
    {
        // 应用契约层通常只包含 DTO 和接口定义
        // 不需要注册具体服务实现
    }

    public void OnApplicationInitialization(IHost host)
    {
        // 应用初始化逻辑
    }
}
