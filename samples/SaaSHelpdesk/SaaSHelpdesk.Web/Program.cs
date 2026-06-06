using CrestCreates.Web;
using CrestCreates.Modularity;
using CrestCreates.Security.Modules;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Features;
using CrestCreates.Application.Settings;
using SaaSHelpdesk.Application.Services;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

builder.AddCrestWeb(options =>
{
    options.UseGeneratedApi(api =>
    {
        api.AddApplicationServiceAssembly<TicketAppService>();
        api.AddApplicationServiceAssembly<CustomerAppService>();
        api.AddApplicationServiceAssembly<CategoryAppService>();
        api.AddApplicationServiceAssembly<KnowledgeBaseAppService>();
        api.AddApplicationServiceAssembly<SLAPolicyAppService>();
        api.AddApplicationServiceAssembly<DashboardAppService>();
        api.AddApplicationServiceAssembly<AgentAppService>();
        api.AddApplicationServiceAssembly<CustomerPortalAppService>();
        api.AddApplicationServiceAssembly<SettingAppService>();
        api.AddApplicationServiceAssembly<FeatureAppService>();
        api.AddApplicationServiceAssembly<AuditLogAppService>();
        api.AddApplicationServiceAssembly<AuditLogCleanupAppService>();
    });
});

builder.Services.AddAuthentication()
    .AddScheme<SaaSHelpdesk.Web.Auth.CustomerApiKeyOptions, SaaSHelpdesk.Web.Auth.CustomerApiKeyAuthenticationHandler>(
        "CustomerApiKey", options => { });

builder.Services.AddSettingDefinitionProvider<SaaSHelpdesk.Domain.Settings.HelpdeskSettingDefinitionProvider>();
builder.Services.AddValidatorsFromAssemblyContaining<SaaSHelpdesk.Application.Validators.CreateTicketDtoValidator>();
new SecurityModule(builder.Configuration).OnConfigureServices(builder.Services);

builder.Host.RegisterModules();

var app = builder.Build();

app.UseCrestWeb();
app.MapCrestWeb();

await app.InitializeCrestAsync();

app.Run();