using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Security.Abstractions;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Constants;
using ErpManagement.Domain.Shared.Enums;
using ErpManagement.Domain.Shared.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ErpManagement.EntityFrameworkCore;

public class ErpDataSeeder : IDataSeeder
{
    private readonly ErpManagementDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ErpDataSeeder> _logger;

    public ErpDataSeeder(
        ErpManagementDbContext dbContext,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        ILogger<ErpDataSeeder> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedMasterDataAsync(cancellationToken);
        await SeedProcurementIdentityAsync(cancellationToken);
        await SeedSalesIdentityAsync(cancellationToken);
    }

    private async Task SeedMasterDataAsync(CancellationToken cancellationToken)
    {
        if (await _dbContext.Materials.AnyAsync(cancellationToken))
            return;

        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(cancellationToken);
        var tenantId = tenant?.Id ?? Guid.Empty;

        var pcs = new UnitOfMeasure(Guid.NewGuid(), "PCS", "件", tenantId);
        var kg = new UnitOfMeasure(Guid.NewGuid(), "KG", "千克", tenantId);
        kg.SetPrecision(3);
        var meter = new UnitOfMeasure(Guid.NewGuid(), "M", "米", tenantId);
        _dbContext.UnitsOfMeasure.AddRange(pcs, kg, meter);

        var rawCategory = new MaterialCategory(Guid.NewGuid(), "RAW", "原材料", tenantId);
        var semiCategory = new MaterialCategory(Guid.NewGuid(), "SEMI", "半成品", tenantId);
        var finishedCategory = new MaterialCategory(Guid.NewGuid(), "FG", "成品", tenantId);
        _dbContext.MaterialCategories.AddRange(rawCategory, semiCategory, finishedCategory);

        var rawWarehouse = new Warehouse(Guid.NewGuid(), "WH-RAW", "原材料仓", tenantId);
        rawWarehouse.SetAddress("厂区 A 栋 1 层");
        var fgWarehouse = new Warehouse(Guid.NewGuid(), "WH-FG", "成品仓", tenantId);
        fgWarehouse.SetAddress("厂区 B 栋 1 层");
        _dbContext.Warehouses.AddRange(rawWarehouse, fgWarehouse);

        _dbContext.StorageLocations.AddRange(
            new StorageLocation(Guid.NewGuid(), rawWarehouse.Id, "A-01", "A 区 01 架", tenantId),
            new StorageLocation(Guid.NewGuid(), rawWarehouse.Id, "A-02", "A 区 02 架", tenantId),
            new StorageLocation(Guid.NewGuid(), fgWarehouse.Id, "B-01", "B 区 01 架", tenantId));

        var supplier1 = new Supplier(Guid.NewGuid(), "SUP-001", "华东金属材料有限公司", tenantId);
        supplier1.SetContactName("张经理");
        supplier1.SetPhone("021-55550001");
        supplier1.SetPaymentTermsDays(30);
        var supplier2 = new Supplier(Guid.NewGuid(), "SUP-002", "南方电子元件厂", tenantId);
        supplier2.SetContactName("李经理");
        supplier2.SetPhone("0755-55550002");
        supplier2.SetPaymentTermsDays(45);
        _dbContext.Suppliers.AddRange(supplier1, supplier2);

        var customer1 = new Customer(Guid.NewGuid(), "CUS-001", "北方机械制造有限公司", tenantId);
        customer1.SetContactName("王采购");
        customer1.SetCreditLimit(200000m);
        var customer2 = new Customer(Guid.NewGuid(), "CUS-002", "西部装备集团", tenantId);
        customer2.SetContactName("赵采购");
        customer2.SetCreditLimit(500000m);
        _dbContext.Customers.AddRange(customer1, customer2);

        var steel = new Material(Guid.NewGuid(), "MAT-STEEL-01", "45# 圆钢", pcs.Id, tenantId);
        steel.SetSpecification("Φ50mm");
        steel.SetMaterialType(MaterialType.Purchased);
        steel.SetCategory(rawCategory.Id);
        steel.SetDefaultWarehouse(rawWarehouse.Id);
        steel.SetSafetyStock(100m);
        steel.SetLeadTimeDays(7);

        var motor = new Material(Guid.NewGuid(), "MAT-MOTOR-01", "伺服电机 750W", pcs.Id, tenantId);
        motor.SetMaterialType(MaterialType.Purchased);
        motor.SetCategory(rawCategory.Id);
        motor.SetDefaultWarehouse(rawWarehouse.Id);
        motor.SetSafetyStock(20m);
        motor.SetLeadTimeDays(14);

        var housing = new Material(Guid.NewGuid(), "MAT-HOUSING-01", "铝合金壳体", pcs.Id, tenantId);
        housing.SetMaterialType(MaterialType.Purchased);
        housing.SetCategory(rawCategory.Id);
        housing.SetDefaultWarehouse(rawWarehouse.Id);
        housing.SetSafetyStock(50m);
        housing.SetLeadTimeDays(10);

        var subAssembly = new Material(Guid.NewGuid(), "SEMI-DRIVE-01", "驱动组件", pcs.Id, tenantId);
        subAssembly.SetMaterialType(MaterialType.Manufactured);
        subAssembly.SetCategory(semiCategory.Id);
        subAssembly.SetDefaultWarehouse(rawWarehouse.Id);
        subAssembly.SetSafetyStock(10m);

        var product = new Material(Guid.NewGuid(), "FG-PUMP-01", "智能增压泵 X1", pcs.Id, tenantId);
        product.SetMaterialType(MaterialType.Manufactured);
        product.SetCategory(finishedCategory.Id);
        product.SetDefaultWarehouse(fgWarehouse.Id);
        product.SetSafetyStock(30m);
        product.SetLeadTimeDays(5);

        _dbContext.Materials.AddRange(steel, motor, housing, subAssembly, product);

        var driveBom = new Bom(Guid.NewGuid(), subAssembly.Id, "V1.0", tenantId);
        driveBom.ReplaceLines(new[]
        {
            new BomLine(Guid.NewGuid(), driveBom.Id, motor.Id, 1m, 1, tenantId),
            new BomLine(Guid.NewGuid(), driveBom.Id, housing.Id, 1m, 2, tenantId)
        });
        driveBom.Activate();

        var productBom = new Bom(Guid.NewGuid(), product.Id, "V1.0", tenantId);
        productBom.ReplaceLines(new[]
        {
            new BomLine(Guid.NewGuid(), productBom.Id, subAssembly.Id, 1m, 1, tenantId),
            new BomLine(Guid.NewGuid(), productBom.Id, steel.Id, 2m, 2, tenantId)
        });
        productBom.Activate();

        _dbContext.Boms.AddRange(driveBom, productBom);

        // The seeder writes entities directly (bypassing the AppService audit pipeline),
        // so stamp creation audit here to avoid DateTime.MinValue surfacing as year 1901.
        var now = DateTime.UtcNow;
        foreach (var entry in _dbContext.ChangeTracker.Entries())
        {
            if (entry.Entity is CrestCreates.Domain.Shared.Entities.Auditing.IAuditedEntity audited)
                CrestCreates.Domain.Shared.Entities.Auditing.AuditPropertySetter.SetCreationAudit(audited, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded ERP demo master data for tenant {TenantId}", tenantId);
    }

    /// <summary>
    /// Idempotent identity seed for the P1 approval chain: procurement-manager role, a demo
    /// approver (never the requester) and the procurement/inventory/approval permission grants.
    /// Runs on every startup, independent of whether master data was already seeded.
    /// </summary>
    private async Task SeedProcurementIdentityAsync(CancellationToken cancellationToken)
    {
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(cancellationToken);
        if (tenant is null)
            return;

        var tenantId = tenant.Id.ToString();
        const string roleName = "procurement-manager";
        var userName = _configuration["SeedIdentity:ErpApproverUserName"] ?? "erp-approver";
        var email = _configuration["SeedIdentity:ErpApproverEmail"] ?? "erp-approver@localhost.local";
        var password = _configuration["SeedIdentity:ErpApproverPassword"] ?? "ErpApprover#2026";

        var role = await _dbContext.Roles
            .FirstOrDefaultAsync(r => r.Name == roleName && r.TenantId == tenantId, cancellationToken);
        if (role is null)
        {
            role = new Role(Guid.NewGuid(), roleName, tenantId)
            {
                DisplayName = "采购审批经理",
                IsActive = true,
                CreationTime = DateTime.UtcNow
            };
            _dbContext.Roles.Add(role);
            _logger.LogInformation("Created ERP role: {RoleName}", roleName);
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.UserName == userName && u.TenantId == tenantId, cancellationToken);
        if (user is null)
        {
            user = new User(Guid.NewGuid(), userName, email, tenantId)
            {
                PasswordHash = _passwordHasher.HashPassword(password),
                IsActive = true,
                LockoutEnabled = true,
                CreationTime = DateTime.UtcNow,
                LastPasswordChangeTime = DateTime.UtcNow
            };
            _dbContext.Users.Add(user);
            _logger.LogInformation("Created ERP approver user: {UserName}", userName);
        }

        var userRole = await _dbContext.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == user.Id && ur.RoleId == role.Id, cancellationToken);
        if (userRole is null)
        {
            _dbContext.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, role.Id, tenantId));
        }

        foreach (var permissionName in ProcurementApprovalPermissions())
        {
            var exists = await _dbContext.PermissionGrants.AnyAsync(
                g => g.PermissionName == permissionName
                     && g.ProviderType == PermissionGrantProviderType.Role
                     && g.ProviderKey == roleName
                     && g.Scope == PermissionGrantScope.Tenant
                     && g.TenantId == tenantId,
                cancellationToken);
            if (!exists)
            {
                _dbContext.PermissionGrants.Add(new PermissionGrant(
                    Guid.NewGuid(), permissionName, PermissionGrantProviderType.Role, roleName,
                    PermissionGrantScope.Tenant, tenantId));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("ERP procurement identity seeding completed for tenant {TenantId}", tenantId);
    }

    private async Task SeedSalesIdentityAsync(CancellationToken cancellationToken)
    {
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(cancellationToken);
        if (tenant is null) return;

        var tenantId = tenant.Id.ToString();
        const string roleName = ErpContractIds.SalesManagerRole;
        var role = await _dbContext.Roles.FirstOrDefaultAsync(
            entry => entry.Name == roleName && entry.TenantId == tenantId, cancellationToken);
        if (role is null)
        {
            role = new Role(Guid.NewGuid(), roleName, tenantId)
            {
                DisplayName = "销售审批经理", IsActive = true, CreationTime = DateTime.UtcNow
            };
            _dbContext.Roles.Add(role);
        }

        var userName = _configuration["SeedIdentity:ErpSalesApproverUserName"] ?? "erp-sales-approver";
        var user = await _dbContext.Users.FirstOrDefaultAsync(
            entry => entry.UserName == userName && entry.TenantId == tenantId, cancellationToken);
        if (user is null && _configuration["SeedIdentity:ErpSalesApproverPassword"] is { Length: > 0 } password)
        {
            var now = DateTime.UtcNow;
            var email = _configuration["SeedIdentity:ErpSalesApproverEmail"] ?? "erp-sales-approver@localhost.local";
            user = new User(Guid.NewGuid(), userName, email, tenantId)
            {
                PasswordHash = _passwordHasher.HashPassword(password), IsActive = true,
                LockoutEnabled = true, CreationTime = now, LastPasswordChangeTime = now
            };
            _dbContext.Users.Add(user);
        }

        if (user is not null && !await _dbContext.UserRoles.AnyAsync(
                entry => entry.UserId == user.Id && entry.RoleId == role.Id, cancellationToken))
            _dbContext.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, role.Id, tenantId));

        foreach (var name in new[] { ErpPermissions.SalesApproval.Search, ErpPermissions.SalesApproval.Complete })
        {
            if (!await _dbContext.PermissionGrants.AnyAsync(grant => grant.PermissionName == name
                    && grant.ProviderType == PermissionGrantProviderType.Role && grant.ProviderKey == roleName
                    && grant.Scope == PermissionGrantScope.Tenant && grant.TenantId == tenantId, cancellationToken))
                _dbContext.PermissionGrants.Add(new PermissionGrant(Guid.NewGuid(), name,
                    PermissionGrantProviderType.Role, roleName, PermissionGrantScope.Tenant, tenantId));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<string> ProcurementApprovalPermissions()
    {
        yield return ErpPermissions.PurchaseRequisitions.Create;
        yield return ErpPermissions.PurchaseRequisitions.Get;
        yield return ErpPermissions.PurchaseRequisitions.Search;
        yield return ErpPermissions.PurchaseRequisitions.Update;
        yield return ErpPermissions.PurchaseRequisitions.Delete;
        yield return ErpPermissions.PurchaseRequisitions.Submit;
        yield return ErpPermissions.PurchaseRequisitions.Convert;
        yield return ErpPermissions.PurchaseOrders.Create;
        yield return ErpPermissions.PurchaseOrders.Get;
        yield return ErpPermissions.PurchaseOrders.Search;
        yield return ErpPermissions.PurchaseOrders.Update;
        yield return ErpPermissions.PurchaseOrders.Delete;
        yield return ErpPermissions.PurchaseOrders.Confirm;
        yield return ErpPermissions.PurchaseOrders.Close;
        yield return ErpPermissions.GoodsReceipts.Create;
        yield return ErpPermissions.GoodsReceipts.Get;
        yield return ErpPermissions.GoodsReceipts.Search;
        yield return ErpPermissions.GoodsReceipts.Update;
        yield return ErpPermissions.GoodsReceipts.Delete;
        yield return ErpPermissions.GoodsReceipts.Post;
        yield return ErpPermissions.StockBalances.Get;
        yield return ErpPermissions.StockBalances.Search;
        yield return ErpPermissions.StockTransactions.Get;
        yield return ErpPermissions.StockTransactions.Search;
        yield return ErpPermissions.StockTransfers.Create;
        yield return ErpPermissions.StockTransfers.Get;
        yield return ErpPermissions.StockTransfers.Search;
        yield return ErpPermissions.StockTransfers.Update;
        yield return ErpPermissions.StockTransfers.Delete;
        yield return ErpPermissions.StockTransfers.Post;
        yield return ErpPermissions.StockCounts.Create;
        yield return ErpPermissions.StockCounts.Get;
        yield return ErpPermissions.StockCounts.Search;
        yield return ErpPermissions.StockCounts.Update;
        yield return ErpPermissions.StockCounts.Delete;
        yield return ErpPermissions.StockCounts.Post;
        yield return ErpPermissions.HumanTasks.Get;
        yield return ErpPermissions.HumanTasks.Search;
        yield return ErpPermissions.HumanTasks.Complete;
    }
}