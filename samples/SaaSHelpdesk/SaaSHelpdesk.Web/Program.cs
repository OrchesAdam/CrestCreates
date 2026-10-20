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
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Application.Services;
using CrestCreates.Web.Legacy;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

builder.AddCrestWeb();

// #129 S4: the legacy AppService HTTP chain is an explicit opt-in with a per-service/action
// allowlist. Ticket is served exclusively by the formal ExplicitHost contribution.
builder.Services.AddLegacyGeneratedApi(api =>
{
    api.AddApplicationServiceAssembly<CustomerAppService>();
    api.AddApplicationServiceAssembly<CategoryAppService>();
    api.AddApplicationServiceAssembly<KnowledgeBaseAppService>();
    api.AddApplicationServiceAssembly<SLAPolicyAppService>();
    api.AddApplicationServiceAssembly<DashboardAppService>();
    api.AddApplicationServiceAssembly<AgentAppService>();
    api.AddApplicationServiceAssembly<CustomerPortalAppService>();

    api.Allow<IAgentAppService>("Create", "GetById", "Deactivate");
    api.Allow<ICategoryAppService>("Create", "GetById", "GetAll", "Update", "Delete", "GetTree", "GetRoots", "GetChildren", "Move", "Reorder");
    api.Allow<ICustomerAppService>("Create", "GetById", "GetAll", "Update", "Delete", "Activate", "Deactivate");
    api.Allow<ICustomerPortalAppService>("CreateTicket", "GetMyTickets", "GetTicket");
    api.Allow<IDashboardAppService>("GetSummary");
    api.Allow<IKnowledgeBaseAppService>("Create", "GetById", "GetAll", "Update", "Delete", "Publish", "Unpublish", "IncrementViewCount", "Search", "GetPopular");
    api.Allow<ISLAPolicyAppService>("Create", "GetById", "GetAll", "Update", "Delete");
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
app.MapLegacyGeneratedApi();

await app.InitializeCrestApplicationAsync();

app.Run();

public partial class Program;
