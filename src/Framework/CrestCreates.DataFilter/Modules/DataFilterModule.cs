using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DataFilter.Modules;

[CrestModule]
public class DataFilterModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        // 注册数据过滤相关服务
    }
}