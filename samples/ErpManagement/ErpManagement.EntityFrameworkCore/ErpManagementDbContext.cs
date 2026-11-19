using System.Diagnostics.CodeAnalysis;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Settings;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.ValueConverters;
using ErpManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.EntityFrameworkCore;

[UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "EF Core DbContext is not fully trim-compatible")]
public class ErpManagementDbContext : DbContext
{
    public ErpManagementDbContext(DbContextOptions<ErpManagementDbContext> options) : base(options) { }

    // MasterData
    public DbSet<Material> Materials { get; set; } = null!;
    public DbSet<MaterialCategory> MaterialCategories { get; set; } = null!;
    public DbSet<UnitOfMeasure> UnitsOfMeasure { get; set; } = null!;
    public DbSet<Warehouse> Warehouses { get; set; } = null!;
    public DbSet<StorageLocation> StorageLocations { get; set; } = null!;
    public DbSet<Supplier> Suppliers { get; set; } = null!;
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Bom> Boms { get; set; } = null!;
    public DbSet<BomLine> BomLines { get; set; } = null!;

    // Procurement
    public DbSet<PurchaseRequisition> PurchaseRequisitions { get; set; } = null!;
    public DbSet<PurchaseRequisitionLine> PurchaseRequisitionLines { get; set; } = null!;
    public DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;
    public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; } = null!;
    public DbSet<GoodsReceipt> GoodsReceipts { get; set; } = null!;
    public DbSet<GoodsReceiptLine> GoodsReceiptLines { get; set; } = null!;

    // Inventory
    public DbSet<StockBalance> StockBalances { get; set; } = null!;
    public DbSet<StockTransaction> StockTransactions { get; set; } = null!;
    public DbSet<StockTransfer> StockTransfers { get; set; } = null!;
    public DbSet<StockTransferLine> StockTransferLines { get; set; } = null!;
    public DbSet<StockCount> StockCounts { get; set; } = null!;
    public DbSet<StockCountLine> StockCountLines { get; set; } = null!;
    public DbSet<IntegrationEventInbox> IntegrationEventInboxes { get; set; } = null!;

    public DbSet<SalesOrder> SalesOrders { get; set; } = null!;
    public DbSet<SalesOrderLine> SalesOrderLines { get; set; } = null!;
    public DbSet<SalesShipment> SalesShipments { get; set; } = null!;
    public DbSet<SalesShipmentLine> SalesShipmentLines { get; set; } = null!;
    public DbSet<WorkOrder> WorkOrders { get; set; } = null!;
    public DbSet<WorkOrderMaterial> WorkOrderMaterials { get; set; } = null!;
    public DbSet<WorkOrderReport> WorkOrderReports { get; set; } = null!;
    public DbSet<ArApLedger> ArApLedgers { get; set; } = null!;
    public DbSet<SettlementRecord> SettlementRecords { get; set; } = null!;

    // Framework
    public DbSet<PermissionGrant> PermissionGrants { get; set; } = null!;
    public DbSet<Permission> Permissions { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<UserRole> UserRoles { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<IdentitySecurityLog> IdentitySecurityLogs { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<Tenant> Tenants { get; set; } = null!;
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; } = null!;
    public DbSet<SettingValue> SettingValues { get; set; } = null!;
    public DbSet<FeatureValue> FeatureValues { get; set; } = null!;
    public DbSet<TenantInitializationRecord> TenantInitializationRecords { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Material>(b =>
        {
            b.ToTable("Materials");
            b.HasKey(e => e.Id);
            b.Property(e => e.Code).HasMaxLength(64).IsRequired();
            b.Property(e => e.Name).HasMaxLength(200).IsRequired();
            b.Property(e => e.Specification).HasMaxLength(200);
            b.Property(e => e.MaterialType).HasConversion<string>().HasMaxLength(32);
            b.Property(e => e.SafetyStock).HasPrecision(18, 4);
            b.Property(e => e.Notes).HasMaxLength(1000);
            b.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        });

        modelBuilder.Entity<MaterialCategory>(b =>
        {
            b.ToTable("MaterialCategories");
            b.HasKey(e => e.Id);
            b.Property(e => e.Code).HasMaxLength(64).IsRequired();
            b.Property(e => e.Name).HasMaxLength(100).IsRequired();
            b.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
            b.HasOne<MaterialCategory>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UnitOfMeasure>(b =>
        {
            b.ToTable("UnitsOfMeasure");
            b.HasKey(e => e.Id);
            b.Property(e => e.Code).HasMaxLength(32).IsRequired();
            b.Property(e => e.Name).HasMaxLength(100).IsRequired();
            b.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        });

        modelBuilder.Entity<Warehouse>(b =>
        {
            b.ToTable("Warehouses");
            b.HasKey(e => e.Id);
            b.Property(e => e.Code).HasMaxLength(64).IsRequired();
            b.Property(e => e.Name).HasMaxLength(100).IsRequired();
            b.Property(e => e.Address).HasMaxLength(300);
            b.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        });

        modelBuilder.Entity<StorageLocation>(b =>
        {
            b.ToTable("StorageLocations");
            b.HasKey(e => e.Id);
            b.Property(e => e.Code).HasMaxLength(64).IsRequired();
            b.Property(e => e.Name).HasMaxLength(100).IsRequired();
            b.HasIndex(e => new { e.TenantId, e.WarehouseId, e.Code }).IsUnique();
            b.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Supplier>(b =>
        {
            b.ToTable("Suppliers");
            b.HasKey(e => e.Id);
            b.Property(e => e.Code).HasMaxLength(64).IsRequired();
            b.Property(e => e.Name).HasMaxLength(200).IsRequired();
            b.Property(e => e.ContactName).HasMaxLength(100);
            b.Property(e => e.Phone).HasMaxLength(32);
            b.Property(e => e.Email).HasMaxLength(256);
            b.Property(e => e.Address).HasMaxLength(300);
            b.Property(e => e.Notes).HasMaxLength(1000);
            b.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        });

        modelBuilder.Entity<Customer>(b =>
        {
            b.ToTable("Customers");
            b.HasKey(e => e.Id);
            b.Property(e => e.Code).HasMaxLength(64).IsRequired();
            b.Property(e => e.Name).HasMaxLength(200).IsRequired();
            b.Property(e => e.ContactName).HasMaxLength(100);
            b.Property(e => e.Phone).HasMaxLength(32);
            b.Property(e => e.Email).HasMaxLength(256);
            b.Property(e => e.Address).HasMaxLength(300);
            b.Property(e => e.CreditLimit).HasPrecision(18, 2);
            b.Property(e => e.Notes).HasMaxLength(1000);
            b.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        });

        modelBuilder.Entity<Bom>(b =>
        {
            b.ToTable("Boms");
            b.HasKey(e => e.Id);
            b.Property(e => e.Version).HasMaxLength(32).IsRequired();
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(e => e.Notes).HasMaxLength(1000);
            b.HasIndex(e => new { e.TenantId, e.MaterialId, e.Version }).IsUnique();
            b.HasOne<Material>().WithMany().HasForeignKey(e => e.MaterialId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.BomId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BomLine>(b =>
        {
            b.ToTable("BomLines");
            b.HasKey(e => e.Id);
            b.Property(e => e.Quantity).HasPrecision(18, 4);
            b.HasOne<Material>().WithMany().HasForeignKey(e => e.ComponentMaterialId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("Permissions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.DisplayName).HasMaxLength(256);
            entity.Property(e => e.GroupName).HasMaxLength(128);
            entity.Property(e => e.IsEnabled).IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<PermissionGrant>(entity =>
        {
            entity.ToTable("PermissionGrants");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.PermissionName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.ProviderType).HasConversion<int>().IsRequired();
            entity.Property(e => e.ProviderKey).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Scope).HasConversion<int>().IsRequired();
            entity.Property(e => e.TenantId).HasMaxLength(64);
            entity.HasIndex(e => new { e.PermissionName, e.ProviderType, e.ProviderKey, e.Scope, e.TenantId }).IsUnique();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UserName).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.PasswordHash).HasMaxLength(512);
            entity.Property(e => e.Phone).HasMaxLength(32);
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.IsSuperAdmin).IsRequired();
            entity.Property(e => e.AccessFailedCount).IsRequired();
            entity.Property(e => e.LockoutEnabled).IsRequired();
            entity.Property(e => e.CreationTime).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.UserName }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(64);
            entity.Property(e => e.DisplayName).HasMaxLength(128);
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.DataScope).HasConversion<int>().IsRequired();
            entity.Property(e => e.CreationTime).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRoles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.RoleId).IsRequired();
            entity.Property(e => e.TenantId).HasMaxLength(64);
            entity.HasIndex(e => new { e.UserId, e.RoleId, e.TenantId }).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Token).IsRequired().HasMaxLength(256);
            entity.Property(e => e.TenantId).HasMaxLength(64);
            entity.Property(e => e.CreationTime).IsRequired();
            entity.Property(e => e.ExpirationTime).IsRequired();
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.RevokedTime, e.ExpirationTime });
        });

        modelBuilder.Entity<IdentitySecurityLog>(entity =>
        {
            entity.ToTable("IdentitySecurityLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UserName).HasMaxLength(64);
            entity.Property(e => e.TenantId).HasMaxLength(64);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Detail).HasMaxLength(1024);
            entity.Property(e => e.ClientIpAddress).HasMaxLength(64);
            entity.Property(e => e.CreationTime).IsRequired();
            entity.HasIndex(e => new { e.UserId, e.CreationTime });
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Duration).IsRequired();
            entity.Property(e => e.ExecutionTime).IsRequired();
            entity.Property(e => e.TraceId).HasMaxLength(128);
            entity.Property(e => e.UserId).HasMaxLength(64);
            entity.Property(e => e.UserName).HasMaxLength(128);
            entity.Property(e => e.TenantId).HasMaxLength(64);
            entity.Property(e => e.ClientIpAddress).HasMaxLength(64);
            entity.Property(e => e.HttpMethod).HasMaxLength(16);
            entity.Property(e => e.Url).HasMaxLength(2048);
            entity.Property(e => e.ServiceName).HasMaxLength(256);
            entity.Property(e => e.MethodName).HasMaxLength(256);
            entity.Property(e => e.Parameters).HasMaxLength(-1);
            entity.Property(e => e.ReturnValue).HasMaxLength(-1);
            entity.Property(e => e.ExceptionMessage).HasMaxLength(4096);
            entity.Property(e => e.ExceptionStackTrace).HasMaxLength(-1);
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.CreationTime).IsRequired();
            entity.Property(e => e.ExtraProperties).HasConversion<DictionaryToJsonValueConverter>();
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreationTime);
            entity.HasIndex(e => e.TraceId);
        });

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenants");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(64);
            entity.Property(e => e.NormalizedName).IsRequired().HasMaxLength(64);
            entity.Property(e => e.DisplayName).HasMaxLength(128);
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.CreationTime).IsRequired();
            entity.HasIndex(e => e.NormalizedName).IsUnique();
            entity.HasMany(e => e.ConnectionStrings)
                .WithOne()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantConnectionString>(entity =>
        {
            entity.ToTable("TenantConnectionStrings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Value).IsRequired().HasMaxLength(2048);
            entity.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<SettingValue>(entity =>
        {
            entity.ToTable("SettingValues");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Value).HasMaxLength(4000);
            entity.Property(e => e.ProviderType).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Scope).HasConversion<int>().IsRequired();
            entity.Property(e => e.ProviderKey).IsRequired().HasMaxLength(128);
            entity.Property(e => e.TenantId).HasMaxLength(64);
            entity.Property(e => e.IsEncrypted).IsRequired();
            entity.Property(e => e.CreationTime).IsRequired();
            entity.Property(e => e.LastModificationTime);
            entity.HasIndex(e => new { e.Name, e.Scope, e.ProviderKey, e.TenantId }).IsUnique();
            entity.HasIndex(e => new { e.Name, e.Scope, e.ProviderKey })
                .IsUnique()
                .HasFilter("\"TenantId\" IS NULL");
        });

        modelBuilder.Entity<FeatureValue>(entity =>
        {
            entity.ToTable("FeatureValues");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Value).HasMaxLength(4000);
            entity.Property(e => e.Scope).HasConversion<int>().IsRequired();
            entity.Property(e => e.ProviderKey).IsRequired().HasMaxLength(128);
            entity.Property(e => e.TenantId).HasMaxLength(64);
            entity.Property(e => e.CreationTime).IsRequired();
            entity.Property(e => e.LastModificationTime);
            entity.HasIndex(e => new { e.Name, e.Scope, e.ProviderKey, e.TenantId }).IsUnique();
            entity.HasIndex(e => new { e.Name, e.Scope, e.ProviderKey })
                .IsUnique()
                .HasFilter("\"TenantId\" IS NULL");
            entity.HasIndex(e => new { e.Name, e.Scope, e.TenantId })
                .IsUnique()
                .HasFilter("\"TenantId\" IS NOT NULL");
        });

        modelBuilder.Entity<TenantInitializationRecord>(entity =>
        {
            entity.ToTable("TenantInitializationRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.AttemptNo).IsRequired();
            entity.Property(e => e.Status).HasConversion<int>().IsRequired();
            entity.Property(e => e.CurrentStep).HasMaxLength(128);
            entity.Property(e => e.StepResultsJson).IsRequired();
            entity.Property(e => e.Error).HasMaxLength(2048);
            entity.Property(e => e.StartedAt).IsRequired();
            entity.Property(e => e.CorrelationId).IsRequired().HasMaxLength(128);
            entity.HasIndex(e => new { e.TenantId, e.AttemptNo }).IsUnique();
        });

        modelBuilder.Entity<PurchaseRequisition>(b =>
        {
            b.ToTable("PurchaseRequisitions");
            b.HasKey(e => e.Id);
            b.Property(e => e.Number).HasMaxLength(64).IsRequired();
            b.Property(e => e.RequesterId).HasMaxLength(128).IsRequired();
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(e => e.TotalAmount).HasPrecision(18, 2);
            b.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
            b.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.RequisitionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PurchaseRequisitionLine>(b =>
        {
            b.ToTable("PurchaseRequisitionLines");
            b.HasKey(e => e.Id);
            b.Property(e => e.Quantity).HasPrecision(18, 4);
            b.Property(e => e.UnitPrice).HasPrecision(18, 2);
            b.Property(e => e.Amount).HasPrecision(18, 2);
            b.HasOne<Material>().WithMany().HasForeignKey(e => e.MaterialId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseOrder>(b =>
        {
            b.ToTable("PurchaseOrders");
            b.HasKey(e => e.Id);
            b.Property(e => e.Number).HasMaxLength(64).IsRequired();
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(e => e.TotalAmount).HasPrecision(18, 2);
            b.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
            b.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Supplier>().WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseOrderLine>(b =>
        {
            b.ToTable("PurchaseOrderLines");
            b.HasKey(e => e.Id);
            b.Property(e => e.Quantity).HasPrecision(18, 4);
            b.Property(e => e.ReceivedQuantity).HasPrecision(18, 4);
            b.Property(e => e.UnitPrice).HasPrecision(18, 2);
            b.Property(e => e.Amount).HasPrecision(18, 2);
            b.HasOne<Material>().WithMany().HasForeignKey(e => e.MaterialId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<GoodsReceipt>(b =>
        {
            b.ToTable("GoodsReceipts");
            b.HasKey(e => e.Id);
            b.Property(e => e.Number).HasMaxLength(64).IsRequired();
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            b.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
            b.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.ReceiptId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(e => e.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<GoodsReceiptLine>(b =>
        {
            b.ToTable("GoodsReceiptLines");
            b.HasKey(e => e.Id);
            b.Property(e => e.Quantity).HasPrecision(18, 4);
            b.Property(e => e.UnitPrice).HasPrecision(18, 2);
            b.HasOne<Material>().WithMany().HasForeignKey(e => e.MaterialId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<StockBalance>(b =>
        {
            b.ToTable("StockBalances");
            b.HasKey(e => e.Id);
            b.Property(e => e.Quantity).HasPrecision(18, 4);
            b.HasIndex(e => new { e.TenantId, e.MaterialId, e.WarehouseId, e.StorageLocationId })
                .HasDatabaseName(Repositories.StockBalanceConflictInterceptor.NaturalKeyIndexName).IsUnique();
        });
        modelBuilder.Entity<StockTransaction>(b =>
        {
            b.ToTable("StockTransactions");
            b.HasKey(e => e.Id);
            b.Property(e => e.SignedQuantity).HasPrecision(18, 4);
            b.Property(e => e.BalanceAfter).HasPrecision(18, 4);
            b.Property(e => e.MovementType).HasConversion<string>().HasMaxLength(32);
            b.Property(e => e.SourceDocType).HasMaxLength(32).IsRequired();
            b.HasIndex(e => new { e.TenantId, e.MaterialId, e.WarehouseId });
        });
        modelBuilder.Entity<StockTransfer>(b =>
        {
            b.ToTable("StockTransfers");
            b.HasKey(e => e.Id);
            b.Property(e => e.Number).HasMaxLength(64).IsRequired();
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            b.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
            b.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.TransferId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<StockTransferLine>(b =>
        {
            b.ToTable("StockTransferLines");
            b.HasKey(e => e.Id);
            b.Property(e => e.Quantity).HasPrecision(18, 4);
        });
        modelBuilder.Entity<StockCount>(b =>
        {
            b.ToTable("StockCounts");
            b.HasKey(e => e.Id);
            b.Property(e => e.Number).HasMaxLength(64).IsRequired();
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            b.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
            b.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.CountId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<StockCountLine>(b =>
        {
            b.ToTable("StockCountLines");
            b.HasKey(e => e.Id);
            b.Property(e => e.BookQuantity).HasPrecision(18, 4);
            b.Property(e => e.CountedQuantity).HasPrecision(18, 4);
        });
        modelBuilder.Entity<IntegrationEventInbox>(b =>
        {
            b.ToTable("IntegrationEventInboxes");
            b.HasKey(e => e.Id);
            b.Property(e => e.ConsumerId).HasMaxLength(128).IsRequired();
            b.Property(e => e.MessageId).HasMaxLength(256).IsRequired();
            b.Property(e => e.ContractId).HasMaxLength(128).IsRequired();
            b.Property(e => e.PayloadHash).HasMaxLength(64).IsRequired();
            b.HasIndex(e => new { e.ConsumerId, e.MessageId }).IsUnique();
        });

        modelBuilder.Entity<SalesOrder>(b =>
        {
            b.ToTable("SalesOrders");
            b.HasKey(e => e.Id);
            b.Property(e => e.Number).HasMaxLength(64).IsRequired();
            b.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
            b.Property(e => e.TotalAmount).HasPrecision(18, 2);
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            b.HasMany(e => e.Lines).WithOne().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.Property(e => e.RequesterId).HasMaxLength(128);
            b.Property(e => e.ApproverId).HasMaxLength(128);
            b.Property(e => e.ApprovalComment).HasMaxLength(1000);
        });

        modelBuilder.Entity<SalesOrderLine>(b =>
        {
            b.ToTable("SalesOrderLines");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Quantity).HasPrecision(18, 4);
            b.Property(e => e.ShippedQuantity).HasPrecision(18, 4);
            b.Property(e => e.UnitPrice).HasPrecision(18, 2);
            b.Property(e => e.Amount).HasPrecision(18, 2);
            b.HasOne<Material>().WithMany().HasForeignKey(e => e.MaterialId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StorageLocation>().WithMany().HasForeignKey(e => e.StorageLocationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesShipment>(b =>
        {
            b.ToTable("SalesShipments");
            b.HasKey(e => e.Id);
            b.Property(e => e.Number).HasMaxLength(64).IsRequired();
            b.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            b.HasMany(e => e.Lines).WithOne().HasForeignKey(e => e.ShipmentId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<SalesOrder>().WithMany().HasForeignKey(e => e.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesShipmentLine>(b =>
        {
            b.ToTable("SalesShipmentLines");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Quantity).HasPrecision(18, 4);
            b.Property(e => e.UnitPrice).HasPrecision(18, 2);
            b.HasOne<SalesOrderLine>().WithMany().HasForeignKey(e => e.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Material>().WithMany().HasForeignKey(e => e.MaterialId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StorageLocation>().WithMany().HasForeignKey(e => e.StorageLocationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkOrder>(b =>
        {
            b.ToTable("WorkOrders");
            b.HasKey(e => e.Id);
            b.Property(e => e.Number).HasMaxLength(64).IsRequired();
            b.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
            b.Property(e => e.Quantity).HasPrecision(18, 4);
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            b.HasMany(e => e.Materials).WithOne().HasForeignKey(e => e.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(e => e.Reports).WithOne().HasForeignKey(e => e.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Material>().WithMany().HasForeignKey(e => e.MaterialId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SalesOrder>().WithMany().HasForeignKey(e => e.SourceSalesOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkOrderMaterial>(b =>
        {
            b.ToTable("WorkOrderMaterials");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.RequiredQuantity).HasPrecision(18, 4);
            b.Property(e => e.IssuedQuantity).HasPrecision(18, 4);
            b.HasOne<Material>().WithMany().HasForeignKey(e => e.MaterialId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkOrderReport>(b =>
        {
            b.ToTable("WorkOrderReports");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.ReportedQuantity).HasPrecision(18, 4);
            b.Property(e => e.ReportSource).HasMaxLength(64).IsRequired();
            b.Property<string>("_operationResult").HasColumnName("OperationResult");
            b.HasIndex(e => new { e.TenantId, e.WorkOrderId, e.OperationId }).IsUnique();
        });

        modelBuilder.Entity<ArApLedger>(b =>
        {
            b.ToTable("ArApLedgers");
            b.HasKey(e => e.Id);
            b.Property(e => e.Amount).HasPrecision(18, 2);
            b.Property(e => e.SettledAmount).HasPrecision(18, 2);
            b.Property(e => e.Direction).HasConversion<string>().HasMaxLength(32);
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            b.HasMany(e => e.Settlements).WithOne().HasForeignKey(e => e.LedgerId).OnDelete(DeleteBehavior.Cascade);
            b.Property(e => e.SourceDocType).HasMaxLength(32).IsRequired();
            b.HasIndex(e => new { e.TenantId, e.SourceDocType, e.SourceDocId, e.Direction }).IsUnique();
        });

        modelBuilder.Entity<SettlementRecord>(b =>
        {
            b.ToTable("SettlementRecords");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Amount).HasPrecision(18, 2);
            b.Property<string>("_operationResult").HasColumnName("OperationResult");
            b.HasIndex(e => new { e.TenantId, e.LedgerId, e.OperationId }).IsUnique();
        });

        modelBuilder.ConfigureConcurrencyStamp();
    }
}