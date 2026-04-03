using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using LibraryManagement.EntityFrameworkCore;
using LibraryManagement.EntityFrameworkCore.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LibraryManagement.Web.Modules;

[Module(typeof(EntityFrameworkCoreModule), Order = 0)]
public class WebModule : IModule
{
    public void OnPreInitialize()
    {
        // Web 层预初始化逻辑
    }

    public void OnInitialize()
    {
        // Web 层初始化逻辑
    }

    public void OnPostInitialize()
    {
        // Web 层后初始化逻辑
    }

    public void OnConfigureServices(IServiceCollection services)
    {
        // Web 层服务注册
        // 控制器、Swagger 等在 Program.cs 中注册
    }

    public void OnApplicationInitialization(IHost host)
    {
        // 确保数据库已创建
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        dbContext.Database.EnsureCreated();
    }
}
