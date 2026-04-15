using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.VirtualFileSystem;

public class VfsModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Services.IVirtualFileSystem, Services.VirtualFileSystem>();
    }

    public override void OnPostInitialize()
    {
        // Auto-discover and register module resources
    }
}