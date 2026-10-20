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
builder.Services.AddScoped<
    CrestCreates.Validation.Modules.IValidationService,
    CrestCreates.Validation.Modules.ValidationService>();
builder.Services.AddValidatorsFromAssemblyContaining<SaaSHelpdesk.Application.Validators.CreateTicketDtoValidator>();
new SecurityModule(builder.Configuration).OnConfigureServices(builder.Services);

builder.Host.RegisterModules();

var app = builder.Build();

app.UseCrestWeb();

// #129 S3: the activated contribution contexts are host-authoritative for their projection roots.
// Put them before host infrastructure resolvers (for example the OpenAPI schema context) so the
// map-time JSON verification sees the owning source-generated context, never a reflection-backed
// or metadata-modified resolver.
var generatedProjectionContexts = app.Services
    .GetServices<CapabilityEndpointContribution>()
    .Select(contribution => contribution.JsonContext)
    .Where(context => context is not null)
    .ToArray();
if (generatedProjectionContexts.Length > 0)
{
    var httpJsonOptions = app.Services
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
        .Value.SerializerOptions;
    var resolverChain = httpJsonOptions.TypeInfoResolverChain;
    foreach (var context in generatedProjectionContexts.Reverse())
    {
        resolverChain.Remove(context!);
        resolverChain.Insert(0, context!);
    }
}

app.MapCrestCapabilityEndpoints();
app.MapCrestWeb();

await app.InitializeCrestApplicationAsync();

app.Run();

public partial class Program;
