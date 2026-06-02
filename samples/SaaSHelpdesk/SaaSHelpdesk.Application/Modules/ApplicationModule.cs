using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Application.Contracts.Modules;
using SaaSHelpdesk.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SaaSHelpdesk.Application.Modules;

[CrestModule(typeof(ApplicationContractsModule), Order = -100)]
public class ApplicationModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ITicketAppService, TicketAppService>();
        services.AddScoped<ICustomerAppService, CustomerAppService>();
        services.AddScoped<ICategoryAppService, CategoryAppService>();
        services.AddScoped<IKnowledgeBaseAppService, KnowledgeBaseAppService>();
        services.AddScoped<ISLAPolicyAppService, SLAPolicyAppService>();
        services.AddScoped<IDashboardAppService, DashboardAppService>();
        services.AddScoped<IAgentAppService, AgentAppService>();
        services.AddScoped<ICustomerPortalAppService, CustomerPortalAppService>();
    }
}
