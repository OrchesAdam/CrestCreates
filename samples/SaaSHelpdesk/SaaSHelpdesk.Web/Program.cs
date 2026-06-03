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
using OpenIddict.Server.AspNetCore;
using CrestCreates.Authorization;
using CrestCreates.Infrastructure.Authorization;
using CrestCreates.Infrastructure.Settings;
using CrestCreates.Infrastructure.Permission;
using CrestCreates.Logging.Extensions;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.AspNetCore.Middlewares;
using CrestCreates.EventBus.Local;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

// Add services to the container
builder.Services.AddControllers()
    .AddApplicationPart(typeof(CrestCreates.AspNetCore.Authentication.OpenIddict.Controllers.OpenIddictController).Assembly)
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.CustomSchemaIds(CrestCreates.DynamicApi.DynamicApiSwaggerSchemaIdHelper.GetSchemaId);
});
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

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCrestRequestLogging();
app.UseExceptionHandling();
app.UseAuditLogging();
app.UseHttpsRedirection();
app.UseMultiTenancy();
app.UseAuthentication();
app.UseTenantBoundary();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapCrestAspNetCoreDynamicApi();

// Initialize all modules
app.InitializeModules();

app.Run();

public partial class Program;
