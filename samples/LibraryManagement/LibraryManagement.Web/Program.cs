using LibraryManagement.Web.Modules;
using LibraryManagement.Application.Services;
using CrestCreates.AuditLogging.Services;
using CrestCreates.Application.Identity;
using CrestCreates.Application.Permissions;
using CrestCreates.Application.Tenants;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Options;
using CrestCreates.AspNetCore;
using CrestCreates.AspNetCore.Authentication.JwtBearer;
using CrestCreates.Authorization;
using CrestCreates.Infrastructure.Authorization;
using CrestCreates.Infrastructure.Settings;
using CrestCreates.Infrastructure.Permission;
using CrestCreates.Logging.Extensions;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.AspNetCore.Middlewares;
using CrestCreates.EventBus.Local;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CrestCreates.Application.Settings;
using CrestCreates.OrmProviders.EFCore.Settings;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseCrestSerilog();
builder.Services.AddCrestLogging(builder.Configuration);
builder.Services.Configure<AuditLoggingOptions>(
    builder.Configuration.GetSection(AuditLoggingOptions.SectionName));
builder.Services.AddScoped<AuditLoggingMiddleware>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.CustomSchemaIds(CrestCreates.DynamicApi.DynamicApiSwaggerSchemaIdHelper.GetSchemaId);
});
builder.Services.AddJwtBearerAuthentication(builder.Configuration);
builder.Services.AddDataFilterServices();
builder.Services.AddCrestAuthorization();
builder.Services.AddCrestIdentityAuthentication(builder.Configuration);
builder.Services.AddIdentityManagement();
builder.Services.AddPermissionManagement();
builder.Services.AddSettingManagement();
builder.Services.AddSettingManagementInfrastructure();
builder.Services.AddSettingManagementEfCore();
builder.Services.AddTenantManagement();
builder.Services.AddTenantBootstrapper();
builder.Services.AddTenantManagementCore();
builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(BookAppService).Assembly);
    configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
});
builder.Services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, LocalEventBus>();
builder.Services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
builder.Services.AddMultiTenancy(options =>
{
    options.ResolutionStrategy = TenantResolutionStrategy.Header;
});
builder.Services.AddTenantResolvers(TenantResolutionStrategy.Header);
builder.Services.AddRepositoryTenantProvider();
builder.Services.AddCrestAspNetCoreDynamicApi(options =>
{
    options.AddApplicationServiceAssembly<BookAppService>();
    options.AddApplicationServiceAssembly<SettingAppService>();
});

// Register all modules using the module discovery system
LibraryManagement.Web.Modules.ModuleAutoInitializer.RegisterAllModules(builder.Services);

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
app.MapCrestAspNetCoreDynamicApi();

// Initialize all modules
LibraryManagement.Web.Modules.ModuleAutoInitializer.InitializeAllModules(app.Services);

app.Run();

public partial class Program;
