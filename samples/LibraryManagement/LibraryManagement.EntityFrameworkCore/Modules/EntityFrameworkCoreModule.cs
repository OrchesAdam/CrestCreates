using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using LibraryManagement.Application.Modules;
using LibraryManagement.Domain.Repositories;
using LibraryManagement.EntityFrameworkCore.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LibraryManagement.EntityFrameworkCore.Modules;

[Module(typeof(ApplicationModule), Order = -50)]
public class EntityFrameworkCoreModule : IModule
{
    public void OnPreInitialize()
    {
        // 基础设施层预初始化逻辑
    }

    public void OnInitialize()
    {
        // 基础设施层初始化逻辑
    }

    public void OnPostInitialize()
    {
        // 基础设施层后初始化逻辑
    }

    public void OnConfigureServices(IServiceCollection services)
    {
        // 注册 DbContext
        services.AddDbContext<LibraryDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString("Default");
            options.UseSqlServer(connectionString);
        });

        // 注册仓储
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<ILoanRepository, LoanRepository>();
    }

    public void OnApplicationInitialization(IHost host)
    {
        // 应用初始化逻辑
    }
}
