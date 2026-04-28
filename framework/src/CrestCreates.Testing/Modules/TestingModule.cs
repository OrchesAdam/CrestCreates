using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Testing.Modules;

[CrestModule]
public class TestingModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        // 注册测试相关服务
    }
}