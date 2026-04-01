using CrestCreates.Core;
using CrestCreates.Data;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Web.Module;

[Module(typeof(DataModule), Order = 100)]
public class WebModule : IModule
{
    public void OnPreInitialize()
    {
        throw new System.NotImplementedException();
    }

    public void OnInitialize()
    {
        throw new System.NotImplementedException();
    }

    public void OnPostInitialize()
    {
        throw new System.NotImplementedException();
    }

    public void OnConfigureServices(IServiceCollection services)
    {
        throw new System.NotImplementedException();
    }

    public void OnApplicationInitialization(IHost host)
    {
        throw new System.NotImplementedException();
    }
}