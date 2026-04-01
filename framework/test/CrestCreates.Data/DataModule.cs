using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Core;

namespace CrestCreates.Data;

[Module(typeof(CoreModule), Order = -100)]
public class DataModule : IModule
{
    private readonly ILogger<DataModule>? _logger;

    public DataModule()
    {
    }

    public DataModule(ILogger<DataModule> logger)
    {
        _logger = logger;
    }

    public string Name => "DataModule";
    public string? Description => "数据模块 - 跨项目演示";
    public string? Version => "1.0.0";

    public void OnPreInitialize()
    {
        _logger?.LogInformation("[DataModule] OnPreInitialize");
    }

    public void OnInitialize()
    {
        _logger?.LogInformation("[DataModule] OnInitialize");
    }

    public void OnPostInitialize()
    {
        _logger?.LogInformation("[DataModule] OnPostInitialize");
    }

    public void OnConfigureServices(IServiceCollection services)
    {
        _logger?.LogInformation("[DataModule] OnConfigureServices");
    }

    public void OnApplicationInitialization(IHost host)
    {
        _logger?.LogInformation("[DataModule] OnApplicationInitialization");
    }
}