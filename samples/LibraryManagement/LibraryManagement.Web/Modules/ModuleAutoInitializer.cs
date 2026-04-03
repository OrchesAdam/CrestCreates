using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CrestCreates.Modularity;
using LibraryManagement.Domain.Modules;
using LibraryManagement.Application.Contracts.Modules;
using LibraryManagement.Application.Modules;
using LibraryManagement.EntityFrameworkCore.Modules;

namespace LibraryManagement.Web.Modules;

public static class ModuleAutoInitializer
{
    public static void RegisterAllModules(IServiceCollection services)
    {
        var modules = new List<IModule>
        {
            new DomainModule(),
            new ApplicationContractsModule(),
            new ApplicationModule(),
            new EntityFrameworkCoreModule(),
            new WebModule()
        };

        foreach (var module in modules)
        {
            module.OnConfigureServices(services);
            module.OnPreInitialize();
            module.OnInitialize();
            module.OnPostInitialize();
        }
    }

    public static void InitializeAllModules(IServiceProvider serviceProvider)
    {
        var modules = new List<IModule>
        {
            new DomainModule(),
            new ApplicationContractsModule(),
            new ApplicationModule(),
            new EntityFrameworkCoreModule(),
            new WebModule()
        };

        using var scope = serviceProvider.CreateScope();
        var host = scope.ServiceProvider.GetRequiredService<IHost>();

        foreach (var module in modules)
        {
            module.OnApplicationInitialization(host);
        }
    }

    public static List<string> RegisteredModules => new List<string>
    {
        "DomainModule",
        "ApplicationContractsModule",
        "ApplicationModule",
        "EntityFrameworkCoreModule",
        "WebModule"
    };
}