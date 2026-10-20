using CrestCreates.OpenApi;
using CrestCreates.Modularity;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Application.Services;
using CrestCreates.AuditLogging.Services;
using CrestCreates.Application.Identity;
using CrestCreates.Application.Permissions;
using CrestCreates.Application.Tenants;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.AuditLogging.Options;
using CrestCreates.AspNetCore;
using CrestCreates.Web.Legacy;
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
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CrestCreates.Application.Settings;
using CrestCreates.Application.Features;
using CrestCreates.Application.AuditLog;
using CrestCreates.Data.EFCore.Settings;
using CrestCreates.Data.EFCore.Features;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseCrestSerilog();
builder.Services.AddCrestLogging(builder.Configuration);
builder.Services.Configure<AuditLoggingOptions>(
    builder.Configuration.GetSection(AuditLoggingOptions.SectionName));
builder.Services.AddScoped<AuditLoggingMiddleware>();
builder.Services.AddScoped<AccountabilityHttpTerminalObserverMiddleware>();
builder.Services.AddScoped<AccountabilityHttpOperationScopeMiddleware>();
builder.Services.AddScoped<IAuditLogRedactor, AuditLogRedactor>();
builder.Services.AddScoped<IAuditLogWriter, AuditLogWriter>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddAuditLogging();
builder.Services.AddAccountability();

builder.Services.AddCrestOpenApi();
builder.Services.AddOpenIddictServer(options =>
{
    options.EnablePasswordFlow = true;
    options.EnableClientCredentialsFlow = true;
    options.EnableRefreshTokenFlow = true;
    options.AccessTokenLifetimeMinutes = 60;
    options.RefreshTokenLifetimeDays = 14;
});
builder.Services.AddOpenIddictAuthentication();
builder.Services.AddDataFilterServices();
builder.Services.AddCrestAuthorization();
builder.Services.AddCrestIdentityAuthentication(builder.Configuration);
builder.Services.AddIdentityManagement();
builder.Services.AddPermissionManagement();
builder.Services.AddSettingManagement();
builder.Services.AddSettingDefinitionProvider<CrestCreates.Domain.Settings.AuditLoggingSettingDefinitionProvider>();
builder.Services.AddSettingManagementInfrastructure();
builder.Services.AddSettingManagementEfCore();
builder.Services.AddFeatureManagement();
builder.Services.AddFeatureManagementEfCore();
builder.Services.AddCrestExceptionHandling();
builder.Services.AddTenantManagement();
builder.Services.AddTenantBootstrapper();
builder.Services.AddTenantManagementCore();

builder.Services.AddCrestLocalEventDispatch();
builder.Services.AddMultiTenancy(options =>
{
    options.ResolutionStrategy = TenantResolutionStrategy.Header;
});
builder.Services.AddTenantResolvers(TenantResolutionStrategy.Header);
builder.Services.AddRepositoryTenantProvider();
// #129 S4: explicit legacy activation with a per-service/action allowlist.
builder.Services.AddLegacyGeneratedApi(options =>
{
    options.AddApplicationServiceAssembly<BookAppService>();
    options.AddApplicationServiceAssembly<CategoryAppService>();
    options.AddApplicationServiceAssembly<LoanAppService>();
    options.AddApplicationServiceAssembly<MemberAppService>();

    options.Allow<IBookAppService>("GetByIsbn", "SearchBooks", "Create", "GetById", "GetList", "Update", "Delete");
    options.Allow<ICategoryAppService>("Create", "GetById", "GetAll", "GetList", "Query", "Update", "Delete", "Exists", "Count");
    options.Allow<ILoanAppService>("Search", "GetOverdueLoans", "ReturnBook", "ExtendLoan", "ProcessOverdueLoans", "Create", "GetById", "GetAll", "GetList", "Query", "Update", "Delete", "Exists", "Count");
    options.Allow<IMemberAppService>("GetByEmail", "GetActiveMembers", "PayBalance", "Create", "GetById", "GetAll", "GetList", "Query", "Update", "Delete", "Exists", "Count");
});

// Register all modules using the SourceGenerator-generated module discovery system
builder.Host.RegisterModules();

var app = builder.Build();

app.UseCrestRequestLogging();
app.UseAccountabilityHttpTerminalObserver();
app.UseExceptionHandling();
app.UseHttpsRedirection();
app.UseRouting();
app.UseMultiTenancy();
app.UseAuthentication();
app.UseAccountabilityHttpOperationScope();
app.UseTenantBoundary();
app.UseAuthorization();
app.MapCrestOpenIddictEndpoints();
app.MapLegacyGeneratedApi();
app.MapCrestOpenApi();

// Initialize all modules
await app.InitializeModulesAsync();

app.Run();

public partial class Program;
