using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using LibraryManagement.EntityFrameworkCore;
using LibraryManagement.EntityFrameworkCore.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LibraryManagement.Web.Modules;

[CrestModule(typeof(EntityFrameworkCoreModule), Order = 0)]
public class WebModule : ModuleBase
{ 

    public override void OnApplicationInitialization(IHost host)
    {
        // 确保数据库已创建
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        dbContext.Database.EnsureCreated();
    }
}
