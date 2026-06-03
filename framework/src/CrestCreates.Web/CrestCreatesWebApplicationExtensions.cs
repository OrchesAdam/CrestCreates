using System;
using CrestCreates.Aop.Extensions;
using CrestCreates.Application.AuditLog;
using CrestCreates.Application.Identity;
using CrestCreates.Application.Permissions;
using CrestCreates.Application.Settings;
using CrestCreates.Application.Tenants;
using CrestCreates.AspNetCore;
using CrestCreates.AspNetCore.Authentication.OpenIddict;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Controllers;
using CrestCreates.AspNetCore.Middlewares;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Options;
using CrestCreates.AuditLogging.Services;
using CrestCreates.Authorization;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.EventBus.Local;
using CrestCreates.Infrastructure.Authorization;
using CrestCreates.Infrastructure.Localization;
using CrestCreates.Infrastructure.Permission;
using CrestCreates.Infrastructure.Settings;
using CrestCreates.Infrastructure.UnitOfWork;
using CrestCreates.Logging.Extensions;
using CrestCreates.Modularity;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.MultiTenancy.Providers;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.EFCore.Configuration;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Repositories;
using CrestCreates.OrmProviders.EFCore.Settings;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;

namespace CrestCreates.Web;

public static class CrestCreatesWebApplicationExtensions
{
    public static WebApplicationBuilder AddCrestWeb(this WebApplicationBuilder builder)
    {
        builder.Host.UseCrestSerilog();
        builder.Host.UsePinnedScopeServiceProvider();

        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddCrestLogging(configuration);
        services.Configure<AuditLoggingOptions>(configuration.GetSection(AuditLoggingOptions.SectionName));
        services.AddScoped<AuditLoggingMiddleware>();
        services.AddScoped<IAuditLogRedactor, AuditLogRedactor>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddAuditLogging();

        services.AddControllers()
            .ConfigureApplicationPartManager(partManager =>
            {
                partManager.ApplicationParts.Clear();
                partManager.ApplicationParts.Add(new AssemblyPart(typeof(OpenIddictController).Assembly));
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
                options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve;
            });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "CrestCreates API", Version = "v1" });
        });

        services.AddOpenIddictServer(options =>
        {
            options.EnablePasswordFlow = true;
            options.EnableClientCredentialsFlow = true;
            options.EnableRefreshTokenFlow = true;
            options.AccessTokenLifetimeMinutes = 60;
            options.RefreshTokenLifetimeDays = 14;
        });
        services.AddOpenIddictAuthentication();

        services.AddSingleton<IEfCoreDbContextOptionsContributor>(_ =>
            new DelegateEfCoreDbContextOptionsContributor((serviceProvider, options) =>
            {
                var currentTenant = serviceProvider.GetService<ICurrentTenant>();
                var connectionString = currentTenant?.Tenant?.ConnectionString
                                       ?? configuration.GetConnectionString("Default");
                options.UseSqlServer(connectionString);
            }));

        services.AddCrestCreatesEfCoreDbContext();
        services.AddScoped(typeof(IRepository<,>), typeof(DomainRepositoryAdapter<,>));
        services.AddScoped(typeof(ICrestRepositoryBase<,>), typeof(EfCoreRepository<,>));

        services.AddUnitOfWork(OrmProvider.EfCore);
        services.AddScoped(sp => new CrestCreates.OrmProviders.EFCore.UnitOfWork.EfCoreUnitOfWork(
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

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(CrestCreatesWebApplicationExtensions).Assembly);
        });

        services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, LocalEventBus>();
        services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, DomainEventPublisher>();

        services.AddMultiTenancy(options =>
        {
            options.ResolutionStrategy = TenantResolutionStrategy.Header;
        });
        services.AddTenantResolvers(TenantResolutionStrategy.Header);
        services.AddRepositoryTenantProvider();

        services.AddScoped<ILocalizationProvider, JsonResourceLocalizationProvider>(_ =>
            new JsonResourceLocalizationProvider("Localization/Resources"));

        services.AddCrestAspNetCoreDynamicApi();
        services.AddCrestExceptionHandling();

        LogRegisteredModules();
        return builder;
    }

    public static WebApplication UseCrestWeb(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "CrestCreates API v1"));
        }

        app.UseCrestRequestLogging();
        app.UseExceptionHandling();
        app.UseAuditLogging();
        app.UseRouting();
        app.UseMultiTenancy();
        app.UseAuthentication();
        app.UseTenantBoundary();
        app.UseAuthorization();

        return app;
    }

    public static WebApplication MapCrestWeb(this WebApplication app)
    {
        // Keep MVC routing limited to the OpenIddict controller assembly only.
        app.MapCrestAspNetCoreDynamicApi();
        app.MapControllers();
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
