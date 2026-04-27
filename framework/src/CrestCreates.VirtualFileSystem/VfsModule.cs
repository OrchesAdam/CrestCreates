using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.VirtualFileSystem.Services;

namespace CrestCreates.VirtualFileSystem;

public class VfsModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IVirtualFileSystem, Services.VirtualFileSystem>();
        services.AddSingleton<VfsModuleDiscovery>();
    }
}