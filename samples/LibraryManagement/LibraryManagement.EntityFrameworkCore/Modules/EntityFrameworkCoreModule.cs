using System.Collections.Generic;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.DataSeed;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Data.EFCore.UnitOfWork;
using CrestCreates.Data.EFCore.Settings;
using LibraryManagement.Application.Modules;
using LibraryManagement.Domain.Repositories;
using LibraryManagement.EntityFrameworkCore.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryManagement.EntityFrameworkCore.Modules;

[CrestModule(typeof(ApplicationModule), Order = -50)]
public class EntityFrameworkCoreModule : ModuleBase
{

    public override void OnConfigureServices(IServiceCollection services)
    {
        // Register DbContext
        services.AddDbContext<LibraryDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var currentTenant = serviceProvider.GetService<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>();
            var connectionString = currentTenant?.Tenant?.ConnectionString
                                   ?? configuration.GetConnectionString("Default");
            options.UseNpgsql(connectionString);
        });

        services.AddUnitOfWork(OrmProvider.EfCore);
        services.AddScoped(sp => new EfCoreUnitOfWork(
            sp.GetRequiredService<IDataBaseContext>(),
            sp.GetRequiredService<CrestCreates.Domain.DomainEvents.IDomainEventPublisher>()));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<LibraryDbContext>());
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
            new EfCoreDbContextAdapter(sp.GetRequiredService<LibraryDbContext>()));
        services.AddScoped<IDataBaseContext>(sp =>
            sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        services.AddScoped(typeof(global::CrestCreates.Domain.Repositories.IRepository<,>), typeof(DomainRepositoryAdapter<,>));
        services.AddScoped(typeof(ICrestRepositoryBase<,>), typeof(EfCoreRepository<,>));

        // Register repositories
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<IPermissionGrantRepository, PermissionGrantRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IIdentitySecurityLogRepository, IdentitySecurityLogRepository>();
        services.AddSettingManagementEfCore();

        // Register host migration and seeding runner
        services.AddSingleton<IEnumerable<Type>>(_ => new List<Type>
        {
            typeof(LibraryDbContext),
        });
        services.AddSingleton<HostMigrationAndSeedRunner>();

        // Register host identity data seeder
        services.AddScoped<IDataSeeder, HostIdentityDataSeeder>();
    }
}
