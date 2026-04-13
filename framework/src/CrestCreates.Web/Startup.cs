using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using MediatR;
using CrestCreates.Application.Identity;
using CrestCreates.Application.Permissions;
using CrestCreates.Application.Tenants;
using CrestCreates.Authorization;
using CrestCreates.AspNetCore.Authentication.OpenIddict;
using CrestCreates.Infrastructure.Authorization;
using CrestCreates.Infrastructure.Localization;
using CrestCreates.Infrastructure.Permission;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Logging.Extensions;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Local;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Infrastructure.UnitOfWork;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Repositories;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.AspNetCore.Middlewares;
using CrestCreates.Domain.Shared;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Modularity;
using CrestCreates.Aop.Extensions;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Options;
using CrestCreates.AuditLogging.Services;
using CrestCreates.MultiTenancy.Providers;
using CrestCreates.Application.Settings;
using CrestCreates.Infrastructure.Settings;
using CrestCreates.OrmProviders.EFCore.Settings;

namespace CrestCreates.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCrestLogging(Configuration);
            services.Configure<AuditLoggingOptions>(Configuration.GetSection(AuditLoggingOptions.SectionName));
            services.AddScoped<AuditLoggingMiddleware>();
            services.AddScoped<IAuditLogService, AuditLogService>();

            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = null;
                });

            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "CrestCreates API", Version = "v1" });
                c.CustomSchemaIds(CrestCreates.DynamicApi.DynamicApiSwaggerSchemaIdHelper.GetSchemaId);
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

            services.AddDbContext<CrestCreatesDbContext>((serviceProvider, options) =>
            {
                var currentTenant = serviceProvider.GetService<ICurrentTenant>();
                var connectionString = currentTenant?.Tenant?.ConnectionString
                                       ?? Configuration.GetConnectionString("Default");
                options.UseSqlServer(connectionString);
            });

            services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
                new EfCoreDbContextAdapter(sp.GetRequiredService<CrestCreatesDbContext>()));
            services.AddScoped<IDataBaseContext>(sp =>
                sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
            services.AddScoped(typeof(IRepository<,>), typeof(DomainRepositoryAdapter<,>));
            services.AddScoped(typeof(ICrestRepositoryBase<,>), typeof(EfCoreRepository<,>));

            services.AddUnitOfWork(OrmProvider.EfCore);
            services.AddScoped(sp => new CrestCreates.OrmProviders.EFCore.UnitOfWork.EfCoreUnitOfWork(
                sp.GetRequiredService<IDataBaseContext>(),
                sp.GetRequiredService<IDomainEventPublisher>()));
            services.AddDataFilterServices();
            services.AddCrestAuthorization();
            services.AddCrestIdentityAuthentication(Configuration);
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
                configuration.RegisterServicesFromAssembly(typeof(Startup).Assembly);
            });

            services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, CrestCreates.EventBus.Local.LocalEventBus>();
            services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, CrestCreates.EventBus.Local.DomainEventPublisher>();

            services.AddMultiTenancy(options =>
            {
                options.ResolutionStrategy = TenantResolutionStrategy.Header;
            });
            services.AddTenantResolvers(TenantResolutionStrategy.Header);
            services.AddRepositoryTenantProvider();

            services.AddScoped<ILocalizationProvider, JsonResourceLocalizationProvider>(sp =>
                new JsonResourceLocalizationProvider("Localization/Resources"));


            Console.WriteLine("=== Module Auto Registration Demo ===");
            Console.WriteLine("Modules discovered and registered:");
            foreach (var moduleName in ModuleAutoInitializer.RegisteredModules)
            {
                Console.WriteLine($"  - {moduleName}");
            }
            Console.WriteLine();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "CrestCreates API v1"));
            }

            app.UseCrestRequestLogging();
            app.UseExceptionHandling();
            app.UseAuditLogging();

            app.UseRouting();

            app.UseMultiTenancy();
            app.UseAuthentication();
            app.UseTenantBoundary();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
