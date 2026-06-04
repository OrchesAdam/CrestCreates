using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Application.Tenants;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using CrestCreates.OpenApi;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.MultiTenancy;
using CrestCreates.Data.EFCore.PostgreSQL.Configuration;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Data.EFCore.Settings;
using CrestCreates.Data.EFCore.UnitOfWork;
using CrestCreates.Data.EFCore.ValueConverters;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaaSHelpdesk.Application.Modules;
using SaaSHelpdesk.Domain.Repositories;
using SaaSHelpdesk.EntityFrameworkCore.Repositories;

namespace SaaSHelpdesk.EntityFrameworkCore.Modules;

[CrestModule(typeof(ApplicationModule), Order = -50)]
public class EntityFrameworkCoreModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        // Register DbContext
        services.AddDbContext<HelpdeskDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var currentTenant = serviceProvider.GetService<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>();
            var connectionString = currentTenant?.Tenant?.ConnectionString
                                   ?? configuration.GetConnectionString("Default");
            options.UseNpgsql(connectionString);
        });

        // PostgreSQL-specific framework services (OpenIddictDbContext, IEfCoreDbContextOptionsContributor, ITenantDatabaseInitializer)
        services.AddCrestCreatesEfCorePostgreSql();

        services.AddUnitOfWork(OrmProvider.EfCore);
        services.AddScoped(sp => new EfCoreUnitOfWork(
            sp.GetRequiredService<IDataBaseContext>(),
            sp.GetRequiredService<CrestCreates.Domain.DomainEvents.IDomainEventPublisher>()));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<HelpdeskDbContext>());
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
            new EfCoreDbContextAdapter(sp.GetRequiredService<HelpdeskDbContext>()));
        services.AddScoped<IDataBaseContext>(sp =>
            sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        services.AddScoped(typeof(CrestCreates.Domain.Repositories.IRepository<,>), typeof(DomainRepositoryAdapter<,>));
        services.AddScoped(typeof(ICrestRepositoryBase<,>), typeof(EfCoreRepository<,>));

        // Framework repositories
        services.AddScoped<IPermissionGrantRepository, PermissionGrantRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IIdentitySecurityLogRepository, IdentitySecurityLogRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();

        // Tenant infrastructure — override framework defaults for Npgsql (PostgreSQL)
        services.RemoveAll<Func<string, DbContext>>();
        services.AddSingleton<Func<string, DbContext>>(connectionString =>
        {
            var options = new DbContextOptionsBuilder<HelpdeskDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            return new HelpdeskDbContext(options);
        });
        services.Replace(ServiceDescriptor.Scoped<ITenantMigrationRunner, EfCoreTenantMigrationRunner>());
        services.Replace(ServiceDescriptor.Scoped<ITenantInitializationStore, EfCoreTenantInitializationStore>());

        // MediatR for domain events
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(EntityFrameworkCoreModule).Assembly));

        // Source-generated JSON metadata for OpenAPI schema generation (AoT/trimming compatibility)
        services.AddSingleton<IOpenApiJsonTypeInfoContributor, JsonTypeInfoContributor<DictionaryJsonContext>>();

        services.AddScoped<ITicketRepository, TicketRepository>();

        services.AddSettingManagementEfCore();
    }
}