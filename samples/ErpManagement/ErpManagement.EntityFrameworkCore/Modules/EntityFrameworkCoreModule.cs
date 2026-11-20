using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.MultiTenancy;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using CrestCreates.OpenApi;
using CrestCreates.Data.Abstractions;
using CrestCreates.AspNetCore.Authentication.OpenIddict;
using CrestCreates.Data.EFCore;
using CrestCreates.Data.EFCore.DataSeed;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.MultiTenancy;
using CrestCreates.Data.EFCore.PostgreSql.Configuration;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Data.EFCore.Settings;
using CrestCreates.Data.EFCore.UnitOfWork;
using CrestCreates.Data.EFCore.ValueConverters;
using ErpManagement.Application.Modules;
using ErpManagement.Domain.Repositories;
using ErpManagement.EntityFrameworkCore.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ErpManagement.EntityFrameworkCore.Modules;

[CrestModule(typeof(ApplicationModule), Order = -50)]
public class EntityFrameworkCoreModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<ErpManagementDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var currentTenant = serviceProvider.GetService<ICurrentTenant>();
            var connectionString = currentTenant?.Tenant?.ConnectionString
                                   ?? configuration.GetConnectionString("Default");
            options.UseNpgsql(connectionString).AddInterceptors(new StockBalanceConflictInterceptor());
        });

        services.AddCrestCreatesEfCorePostgreSql();

        services.AddUnitOfWork(OrmProvider.EfCore);
        services.AddScoped<ErpManagement.Application.Events.IErpOutboxAppender, ErpOutboxAppender>();
        services.AddScoped<ErpManagement.Application.Events.IErpGoodsReceiptDelivery, ErpDeliveryStatusReader>();
        services.AddEfCoreUnitOfWork();
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<ErpManagementDbContext>());
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
            new EfCoreDbContextAdapter(sp.GetRequiredService<ErpManagementDbContext>()));
        services.AddScoped<IDataBaseContext>(sp =>
            sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        services.AddScoped(typeof(CrestCreates.Domain.Repositories.IRepository<,>), typeof(DomainRepositoryAdapter<,>));
        services.AddScoped(typeof(ICrestRepositoryBase<,>), typeof(EfCoreRepository<,>));

        services.AddScoped<IPermissionGrantRepository, PermissionGrantRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IIdentitySecurityLogRepository, IdentitySecurityLogRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();

        services.RemoveAll<Func<string, DbContext>>();
        services.AddSingleton<Func<string, DbContext>>(connectionString =>
        {
            var options = new DbContextOptionsBuilder<ErpManagementDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            return new ErpManagementDbContext(options);
        });
        services.TryAddScoped<ITenantSchemaMigrator, EfCoreTenantSchemaMigrator>();
        services.AddScoped<ITenantInitializationStore, EfCoreTenantInitializationStore>();

        services.AddSingleton<IOpenApiJsonTypeInfoContributor, JsonTypeInfoContributor<DictionaryJsonContext>>();

        services.AddScoped<IBomRepository, BomRepository>();

        // Procurement + inventory header repositories (Include(Lines)). Registered as their own
        // concrete type and contract; the generic ICrestRepositoryBase<T> binding above still
        // resolves the base for leaf entities (StockBalance/StockTransaction/Inbox).
        services.AddScoped<PurchaseRequisitionRepository>();
        services.AddScoped<ErpManagement.Application.Contracts.Interfaces.IPurchaseRequisitionRepository>(sp => sp.GetRequiredService<PurchaseRequisitionRepository>());
        services.AddScoped<PurchaseOrderRepository>();
        services.AddScoped<ErpManagement.Application.Contracts.Interfaces.IPurchaseOrderRepository>(sp => sp.GetRequiredService<PurchaseOrderRepository>());
        services.AddScoped<GoodsReceiptRepository>();
        services.AddScoped<ErpManagement.Application.Contracts.Interfaces.IGoodsReceiptRepository>(sp => sp.GetRequiredService<GoodsReceiptRepository>());
        services.AddScoped<StockTransferRepository>();
        services.AddScoped<ErpManagement.Application.Contracts.Interfaces.IStockTransferRepository>(sp => sp.GetRequiredService<StockTransferRepository>());
        services.AddScoped<StockCountRepository>();
        services.AddScoped<ErpManagement.Application.Contracts.Interfaces.IStockCountRepository>(sp => sp.GetRequiredService<StockCountRepository>());
        services.AddScoped<ErpManagement.Domain.Inventory.StockPostingManager>();

        services.AddScoped<ErpManagement.Application.Contracts.Interfaces.ISalesOrderRepository, SalesOrderRepository>();

        services.AddScoped<ErpManagement.Application.Contracts.Interfaces.ISalesShipmentRepository, SalesShipmentRepository>();

        services.AddScoped<ErpManagement.Application.Contracts.Interfaces.IWorkOrderRepository, WorkOrderRepository>();

        services.AddScoped<ErpManagement.Application.Contracts.Interfaces.IArApLedgerRepository, ArApLedgerRepository>();

        services.AddSettingManagementEfCore();

        services.AddSingleton<IEnumerable<Type>>(_ => new List<Type>
        {
            typeof(ErpManagementDbContext),
            typeof(OpenIddictDbContext)
        });
        services.AddSingleton<HostMigrationAndSeedRunner>();

        // HostIdentityDataSeeder is registered by AddCrestWeb and runs first;
        // the ERP demo seeder depends on the host tenant it creates.
        services.AddScoped<IDataSeeder, ErpDataSeeder>();
    }
}