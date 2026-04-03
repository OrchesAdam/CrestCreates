using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Application.Contracts.Modules;
using LibraryManagement.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LibraryManagement.Application.Modules;

[Module(typeof(ApplicationContractsModule), Order = -100)]
public class ApplicationModule : IModule
{
    public void OnPreInitialize()
    {
        // 应用层预初始化逻辑
    }

    public void OnInitialize()
    {
        // 应用层初始化逻辑
    }

    public void OnPostInitialize()
    {
        // 应用层后初始化逻辑
    }

    public void OnConfigureServices(IServiceCollection services)
    {
        // 注册应用服务
        services.AddScoped<IBookAppService, BookAppService>();
        services.AddScoped<ICategoryAppService, CategoryAppService>();
        services.AddScoped<IMemberAppService, MemberAppService>();
        services.AddScoped<ILoanAppService, LoanAppService>();

        // 注册 AutoMapper
        services.AddAutoMapper(typeof(ApplicationModule).Assembly);
    }

    public void OnApplicationInitialization(IHost host)
    {
        // 应用初始化逻辑
    }
}
