using CrestCreates.OpenApi;
using CrestCreates.Modularity;
using CrestCreates.AuditLogging.Services;
using CrestCreates.Application.Identity;
using CrestCreates.Application.Permissions;
using CrestCreates.Application.Tenants;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Options;
using CrestCreates.AspNetCore;
using CrestCreates.AspNetCore.Authentication.OpenIddict;
using CrestCreates.Authorization;
using CrestCreates.Infrastructure.Authorization;
using CrestCreates.Infrastructure.Settings;
using CrestCreates.Infrastructure.Permission;
using CrestCreates.Logging.Extensions;
using CrestCreates.MultiTenancy;
using CrestCreates.AspNetCore.Middlewares;
using CrestCreates.EventBus.Local;
using CrestCreates.Application.Settings;
using CrestCreates.Application.Features;
using CrestCreates.Application.AuditLog;
using CrestCreates.OrmProviders.EFCore.Settings;
using CrestCreates.OrmProviders.EFCore.Features;
using SaaSHelpdesk.Application.Services;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseCrestSerilog();
builder.Services.AddCrestLogging(builder.Configuration);
builder.Services.Configure<AuditLoggingOptions>(
    builder.Configuration.GetSection(AuditLoggingOptions.SectionName));
builder.Services.AddScoped<AuditLoggingMiddleware>();
builder.Services.AddScoped<IAuditLogRedactor, AuditLogRedactor>();
builder.Services.AddScoped<IAuditLogWriter, AuditLogWriter>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddAuditLogging();

builder.Services.AddCrestOpenApi(builder.Configuration.GetSection("CrestOpenApi"));
builder.Services.AddOpenIddictServer(options =>
{
    options.EnablePasswordFlow = true;
    options.EnableClientCredentialsFlow = true;
    options.EnableRefreshTokenFlow = true;
    options.AccessTokenLifetimeMinutes = 60;
    options.RefreshTokenLifetimeDays = 14;
});
builder.Services.AddOpenIddictAuthentication();
builder.Services.AddAuthentication()
    .AddScheme<SaaSHelpdesk.Web.Auth.CustomerApiKeyOptions, SaaSHelpdesk.Web.Auth.CustomerApiKeyAuthenticationHandler>(
        "CustomerApiKey", options => { });
builder.Services.AddDataFilterServices();
builder.Services.AddCrestAuthorization();
builder.Services.AddCrestIdentityAuthentication(builder.Configuration);
builder.Services.AddIdentityManagement();
builder.Services.AddPermissionManagement();
builder.Services.AddSettingManagement();
builder.Services.AddSettingDefinitionProvider<SaaSHelpdesk.Domain.Settings.HelpdeskSettingDefinitionProvider>();
builder.Services.AddSettingManagementInfrastructure();
builder.Services.AddSettingManagementEfCore();
builder.Services.AddFeatureManagement();
builder.Services.AddFeatureManagementEfCore();
builder.Services.AddCrestExceptionHandling();
builder.Services.AddHealthChecks();
builder.Services.AddValidatorsFromAssemblyContaining<SaaSHelpdesk.Application.Validators.CreateTicketDtoValidator>();
builder.Services.AddLocalization();
builder.Services.AddTenantManagement();
builder.Services.AddTenantBootstrapper();
builder.Services.AddTenantManagementCore();

builder.Services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, LocalEventBus>();
builder.Services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
// Quartz Scheduling auto-registered via CrestCreates.Scheduling.Quartz module
builder.Services.AddMultiTenancy(options =>
{
    options.ResolutionStrategy = TenantResolutionStrategy.Header;
});
builder.Services.AddTenantResolvers(TenantResolutionStrategy.Header);
builder.Services.AddRepositoryTenantProvider();
builder.Services.AddCrestAspNetCoreDynamicApi(options =>
{
    options.AddApplicationServiceAssembly<TicketAppService>();
    options.AddApplicationServiceAssembly<CustomerAppService>();
    options.AddApplicationServiceAssembly<CategoryAppService>();
    options.AddApplicationServiceAssembly<KnowledgeBaseAppService>();
    options.AddApplicationServiceAssembly<SLAPolicyAppService>();
    options.AddApplicationServiceAssembly<DashboardAppService>();
    options.AddApplicationServiceAssembly<AgentAppService>();
    options.AddApplicationServiceAssembly<CustomerPortalAppService>();
    options.AddApplicationServiceAssembly<SettingAppService>();
    options.AddApplicationServiceAssembly<FeatureAppService>();
    options.AddApplicationServiceAssembly<AuditLogAppService>();
    options.AddApplicationServiceAssembly<AuditLogCleanupAppService>();
});

// Register all modules using the SourceGenerator-generated module discovery system
builder.Host.RegisterModules();

var app = builder.Build();

app.UseCrestRequestLogging();
app.UseExceptionHandling();
app.UseAuditLogging();
app.UseHttpsRedirection();
app.UseMultiTenancy();
app.UseAuthentication();
app.UseTenantBoundary();
app.UseAuthorization();
app.MapCrestOpenIddictEndpoints();
app.MapHealthChecks("/health");
app.MapCrestAspNetCoreDynamicApi();
app.MapCrestOpenApi();

// Initialize all modules
app.InitializeModules();

// Print startup URLs
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Urls;
    Console.WriteLine();
    Console.WriteLine("┌──────────────────────────────────────────────────────┐");
    Console.WriteLine("│ SaaSHelpdesk application started                     │");
    Console.WriteLine("├──────────────────────────────────────────────────────┤");
    foreach (var address in addresses)
    {
        Console.WriteLine($"│   {address}                          │");
        Console.WriteLine($"│   OpenAPI:   {address}/openapi/v1.json");
        Console.WriteLine($"│   Scalar UI: {address}/scalar/v1");
    }
    Console.WriteLine("└──────────────────────────────────────────────────────┘");
    Console.WriteLine();
});

app.Run();

public partial class Program;
