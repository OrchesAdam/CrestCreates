using AutoMapper;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Application.Contracts.Modules;
using LibraryManagement.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LibraryManagement.Application.Modules;

[CrestModule(typeof(ApplicationContractsModule), Order = -100)]
public class ApplicationModule : ModuleBase
{

    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddAutoMapper(configuration =>
        {
            configuration.AddMaps(typeof(ApplicationModule).Assembly);
        });

        // 注册应用服务
        services.AddScoped<IBookAppService, BookAppService>();
        services.AddScoped<ICategoryAppService, CategoryAppService>();
        services.AddScoped<IMemberAppService, MemberAppService>();
        services.AddScoped<ILoanAppService, LoanAppService>();
    }

}
