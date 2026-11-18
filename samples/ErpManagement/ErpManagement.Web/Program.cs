using CrestCreates.Scheduling.Quartz.Services;
using CrestCreates.Scheduling.Services;
using ErpManagement.Application.Jobs;
using CrestCreates.Web;
using CrestCreates.Web.PlatformManagement;
using ErpManagement.Web.Approval;
using ErpManagement.Web.Integration;
using CrestCreates.DynamicApi;
using CrestCreates.Capability;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Generated.HttpProjection;
using CrestCreates.Modularity;
using CrestCreates.Security.Modules;
using CrestCreates.Application.Features;
using CrestCreates.Application.Settings;
using CrestCreates.Data.EFCore.Features;
using ErpManagement.Application.Validators;
using ErpManagement.Domain.Features;
using ErpManagement.Domain.Settings;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

builder.AddCrestWeb();
builder.Services.AddErpIntegration();

builder.Services.AddErpApprovalRuntime(builder.Configuration);
builder.Services.AddQuartzScheduling();
builder.Services.AddScoped<LowStockScanJob>();

builder.Services.AddFeatureManagement();
builder.Services.AddFeatureManagementEfCore();
builder.Services.AddFeatureDefinitionProvider<ErpFeatureDefinitionProvider>();
builder.Services.AddSettingDefinitionProvider<ErpSettingDefinitionProvider>();

// Every ERP AppService is served by its formal ExplicitHost projection.
builder.Services.AddCapabilityRuntime();
builder.Services.AddSingleton<ISchemaValidator, SchemaValidator>();
builder.Services.AddCrestCapabilityEndpoints();
builder.Services.AddCrestPlatformManagementHttp();
GeneratedAppServiceHttpActivation_ErpManagement_002EApplication.AddGeneratedAppServiceHttp(builder.Services);

builder.Services.AddScoped<
    CrestCreates.Validation.Modules.IValidationService,
    CrestCreates.Validation.Modules.ValidationService>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateMaterialDtoValidator>();
new SecurityModule(builder.Configuration).OnConfigureServices(builder.Services);

builder.Host.RegisterModules();

var app = builder.Build();

app.UseCrestWeb();

// The activated contribution contexts are host-authoritative for their projection roots;
// put them before host infrastructure resolvers so map-time JSON verification sees the
// owning source-generated context.
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

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapCrestCapabilityEndpoints();
app.MapCrestWeb();
app.MapErpIntegration();

await app.InitializeCrestApplicationAsync();

try
{
    var scheduler = app.Services.GetRequiredService<ISchedulerService>();
    await scheduler.RegisterAsync<LowStockScanJob>(new JobMetadata
    {
        Name = "erp-low-stock-scan",
        Group = "erp",
        CronExpression = "0 0/15 * * * ?",
        Timeout = TimeSpan.FromMinutes(1)
    });
}
catch (Exception exception)
{
    app.Logger.LogWarning(exception, "ERP low-stock scan could not be scheduled.");
}

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;