using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Aop.Extensions;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Identity;
using CrestCreates.Application.Permissions;
using CrestCreates.Application.Settings;
using CrestCreates.Application.Tenants;
using CrestCreates.AspNetCore;
using CrestCreates.AspNetCore.Authentication.OpenIddict;
using CrestCreates.AspNetCore.Middlewares;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.AuditLogging.Options;
using CrestCreates.AuditLogging.Services;
using CrestCreates.Authorization;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.HealthCheck.AspNetCore;
using CrestCreates.Infrastructure.Authorization;
using CrestCreates.Infrastructure.Localization;
using CrestCreates.Infrastructure.Permission;
using CrestCreates.Infrastructure.Settings;
using CrestCreates.Infrastructure.UnitOfWork;
using CrestCreates.Logging.Extensions;
using CrestCreates.ModuleDiagnostics.Modules;
using CrestCreates.Modularity;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.MultiTenancy.Providers;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore;
using CrestCreates.Data.EFCore.Configuration;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Data.EFCore.Settings;
using CrestCreates.Data.EFCore.DataSeed;
using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CrestCreates.OpenApi;

namespace CrestCreates.Web;

public static class CrestCreatesWebApplicationExtensions
{
    public static WebApplicationBuilder AddCrestWeb(this WebApplicationBuilder builder)
    {
        return AddCrestWeb(builder, configure: null);
    }

    public static WebApplicationBuilder AddCrestWeb(
        this WebApplicationBuilder builder,
        Action<CrestWebOptions>? configure)
    {
        var options = new CrestWebOptions();
        configure?.Invoke(options);
        return AddCrestWeb(builder, options);
    }

    private static WebApplicationBuilder AddCrestWeb(
        WebApplicationBuilder builder,
        CrestWebOptions options)
    {
        builder.Host.UseCrestSerilog();
        builder.Host.UsePinnedScopeServiceProvider();

        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddSingleton(options);

        services.AddCrestLogging(configuration);
        services.Configure<AuditLoggingOptions>(configuration.GetSection(AuditLoggingOptions.SectionName));
        services.AddScoped<AuditLoggingMiddleware>();
        services.AddScoped<AccountabilityHttpTerminalObserverMiddleware>();
        services.AddScoped<AccountabilityHttpOperationScopeMiddleware>();
        services.AddScoped<IAuditLogRedactor, AuditLogRedactor>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddAuditLogging();
        services.AddAccountability();

        services.AddCrestOpenApi();
        services.AddModuleDiagnostics();

        if (options.EnableOpenIddict)
        {
            services.AddOpenIddictServer(oidcOptions =>
            {
                oidcOptions.EnablePasswordFlow = true;
                oidcOptions.EnableClientCredentialsFlow = true;
                oidcOptions.EnableRefreshTokenFlow = true;
                oidcOptions.AccessTokenLifetimeMinutes = 60;
                oidcOptions.RefreshTokenLifetimeDays = 14;
            });
            services.AddOpenIddictAuthentication();
        }

        services.AddSingleton<IEfCoreDbContextOptionsContributor>(_ =>
            new DelegateEfCoreDbContextOptionsContributor((serviceProvider, dbOptions) =>
            {
                var currentTenant = serviceProvider.GetService<ICurrentTenant>();
                var connectionString = currentTenant?.Tenant?.ConnectionString
                                       ?? configuration.GetConnectionString("Default");
                dbOptions.UseSqlServer(connectionString);
            }));

        services.AddCrestCreatesEfCoreDbContext();

        // Register DbContext types that need migration at startup.
        // OpenIddictDbContext is registered by the provider-specific extension
        // (e.g., AddCrestCreatesEfCorePostgreSql / AddCrestCreatesEfCoreSqlServer).
        // CrestCreatesDbContext is registered by AddCrestCreatesEfCoreDbContext above.
        services.AddSingleton<IEnumerable<Type>>(_ => new List<Type>
        {
            typeof(CrestCreatesDbContext),
            typeof(OpenIddictDbContext)
        });
        services.AddSingleton<HostMigrationAndSeedRunner>();

        // Register host identity data seeder
        services.AddScoped<IDataSeeder, HostIdentityDataSeeder>();
        services.AddScoped(typeof(CrestCreates.Domain.Repositories.IRepository<,>), typeof(DomainRepositoryAdapter<,>));
        services.AddScoped(typeof(ICrestRepositoryBase<,>), typeof(EfCoreRepository<,>));

        services.AddUnitOfWork(OrmProvider.EfCore);
        services.AddScoped(sp => new CrestCreates.Data.EFCore.UnitOfWork.EfCoreUnitOfWork(
            sp.GetRequiredService<IDataBaseContext>(),
            sp.GetRequiredService<IDomainEventPublisher>()));
        services.AddDataFilterServices();
        services.AddCrestAuthorization();
        services.AddCrestIdentityAuthentication(configuration);
        services.AddIdentityManagement();
        services.AddPermissionManagement();
        services.AddSettingManagement();
        services.AddSettingManagementInfrastructure();
        services.AddTenantManagement();
        services.AddTenantBootstrapper();
        services.AddTenantManagementCore();
        services.AddSettingManagementEfCore();
        services.AddScoped<IPermissionGrantRepository, PermissionGrantRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IIdentitySecurityLogRepository, IdentitySecurityLogRepository>();

        services.AddSingleton<LocalEventBusOptions>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, DefaultLocalEventBus>();
        services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, DomainEventPublisher>();

        services.AddMultiTenancy(mtOptions =>
        {
            mtOptions.ResolutionStrategy = TenantResolutionStrategy.Header;
        });
        services.AddTenantResolvers(TenantResolutionStrategy.Header);
        services.AddRepositoryTenantProvider();

        services.AddScoped<ILocalizationProvider, JsonResourceLocalizationProvider>(_ =>
            new JsonResourceLocalizationProvider("Localization/Resources"));

        services.AddCrestAspNetCoreDynamicApi(dynamicApi =>
        {
            foreach (var markerType in options.GeneratedApi.ServiceMarkerTypes)
            {
                dynamicApi.AddApplicationServiceAssembly(markerType.Assembly);
            }
        });
        services.AddCrestExceptionHandling();

        LogRegisteredModules();
        return builder;
    }

    public static WebApplication UseCrestWeb(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseCrestRequestLogging();
        app.UseAccountabilityHttpTerminalObserver();
        app.UseExceptionHandling();
        app.UseRouting();
        app.UseMultiTenancy();
        app.UseAuthentication();
        app.UseAccountabilityHttpOperationScope();
        app.UseTenantBoundary();
        app.UseAuthorization();

        return app;
    }

    public static WebApplication MapCrestWeb(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<CrestWebOptions>();
        if (options.EnableOpenIddict)
        {
            app.MapCrestOpenIddictEndpoints();
        }

        app.MapCrestHealthChecks();
        app.MapCrestAspNetCoreDynamicApi();
        app.MapCrestOpenApi();
        return app;
    }

    public static async Task<WebApplication> InitializeCrestAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        await app.InitializeModulesAsync();
        return app;
    }

    private static void LogRegisteredModules()
    {
        Console.WriteLine("=== Module Auto Registration Demo ===");
        Console.WriteLine("Modules discovered and registered:");
        foreach (var moduleName in ModuleAutoInitializer.RegisteredModules)
        {
            Console.WriteLine($"  - {moduleName}");
        }

        Console.WriteLine();
    }
}
