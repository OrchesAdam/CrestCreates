using CrestCreates.Web;
using CrestCreates.DynamicApi;
using CrestCreates.Capability;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Generated.HttpProjection;
using CrestCreates.Modularity;
using CrestCreates.Security.Modules;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Features;
using CrestCreates.Application.Identity;
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
        api.AddApplicationServiceAssembly<UserAppService>();
    });
});

builder.Services.AddCapabilityRuntime();
builder.Services.AddSingleton<ISchemaValidator, SchemaValidator>();
builder.Services.AddCrestCapabilityEndpoints();
builder.Services.AddGeneratedAppServiceHttp();

builder.Services.AddAuthentication()
    .AddScheme<SaaSHelpdesk.Web.Auth.CustomerApiKeyOptions, SaaSHelpdesk.Web.Auth.CustomerApiKeyAuthenticationHandler>(
        "CustomerApiKey", options => { });

builder.Services.AddSettingDefinitionProvider<SaaSHelpdesk.Domain.Settings.HelpdeskSettingDefinitionProvider>();
builder.Services.AddValidatorsFromAssemblyContaining<SaaSHelpdesk.Application.Validators.CreateTicketDtoValidator>();
new SecurityModule(builder.Configuration).OnConfigureServices(builder.Services);

builder.Host.RegisterModules();

var app = builder.Build();

app.UseCrestWeb();
app.MapCrestCapabilityEndpoints();
app.MapCrestWeb();

await app.InitializeCrestApplicationAsync();

app.Run();

public partial class Program;
