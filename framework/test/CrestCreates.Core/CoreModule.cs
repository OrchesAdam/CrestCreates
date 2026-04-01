using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Core;

[Module(Order = -200)]
public class CoreModule : IModule
{
    private readonly ILogger<CoreModule>? _logger;

    public CoreModule()
    {
    }

    public CoreModule(ILogger<CoreModule> logger)
    {
        _logger = logger;
    }

    public string Name => "CoreModule";
    public string? Description => "核心模块 - 跨项目演示";
    public string? Version => "1.0.0";

    public void OnPreInitialize()
    {
        _logger?.LogInformation("[CoreModule] OnPreInitialize");
    }

    public void OnInitialize()
    {
        _logger?.LogInformation("[CoreModule] OnInitialize");
    }

    public void OnPostInitialize()
    {
        _logger?.LogInformation("[CoreModule] OnPostInitialize");
    }

    public void OnConfigureServices(IServiceCollection services)
    {
        _logger?.LogInformation("[CoreModule] OnConfigureServices");
    }

    public void OnApplicationInitialization(IHost host)
    {
        _logger?.LogInformation("[CoreModule] OnApplicationInitialization");
    }
}