using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Permission;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.Abstract.Abstractions;
using CrestCreates.Domain.Examples;
using CrestCreates.Domain.Settings;

namespace CrestCreates.OrmProviders.EFCore.DbContexts
{
    public class CrestCreatesDbContext : DbContext, IEntityFrameworkCoreDbContext
    {
        public CrestCreatesDbContext(DbContextOptions<CrestCreatesDbContext> options)
            : base(options)
        {
            
        }

        // DbSet properties for your entities
        public DbSet<Product> Products { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<PermissionGrant> PermissionGrants { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<IdentitySecurityLog> IdentitySecurityLogs { get; set; }
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }
        public DbSet<SettingValue> SettingValues { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Configure Product entity
            modelBuilder.Entity<Product>(entity =>
            {
                entity.ToTable("Products");
                
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(1000);
                
                // Map Money value object as owned entity
                entity.OwnsOne(e => e.Price, price =>
                {
                    price.Property(p => p.Amount).HasColumnType("decimal(18,2)");
                    price.Property(p => p.Currency).HasMaxLength(3);
                });
                
                // Map ProductType enum as int
                entity.Property(e => e.Type).HasConversion<int>();
                
                entity.Property(e => e.StockCount).IsRequired();
                
                // Audit fields
                entity.Property(e => e.CreationTime).IsRequired();
                entity.Property(e => e.CreatorId);
                entity.Property(e => e.LastModificationTime);
                entity.Property(e => e.LastModifierId);
                
                // Soft delete
                entity.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.DeletionTime);
                entity.Property(e => e.DeleterId);
                
                // Global filter for soft delete
                entity.HasQueryFilter(e => !e.IsDeleted);
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

                entity.HasIndex(e => new
                {
                    e.PermissionName,
                    e.ProviderType,
                    e.ProviderKey,
                    e.Scope,
                    e.TenantId
                }).IsUnique();
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
            });
        }

        // IEntityFrameworkCoreDbContext implementation
        public OrmProvider Provider => OrmProvider.EfCore;

        public IDataBaseSet<TEntity> Set<TEntity>() where TEntity : class
        {
            return new EfCoreDataBaseSet<TEntity>(base.Set<TEntity>());
        }

        public new Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return base.SaveChangesAsync(cancellationToken);
        }

        public async Task<IDataBaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            var transaction = await Database.BeginTransactionAsync(cancellationToken);
            // 传入 this 引用，让 Transaction 可以访问 DbContext 的属性
            return new EfCoreDataBaseTransaction(transaction, this);
        }

        public IDataBaseTransaction CurrentTransaction => 
            Database.CurrentTransaction != null 
                ? new EfCoreDataBaseTransaction(Database.CurrentTransaction, this) 
                : null;

        public string ConnectionString => Database.GetConnectionString();

        public object GetNativeContext() => this;

        public IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class
        {
            return new EfCoreQueryableBuilder<TEntity>(base.Set<TEntity>());
        }

        public Task<int> ExecuteSqlRawAsync(string sql, IEnumerable<object> parameters = null, CancellationToken cancellationToken = default)
        {
            return Database.ExecuteSqlRawAsync(sql, parameters ?? new object[0], cancellationToken);
        }

        public new void Dispose()
        {
            base.Dispose();
        }
    }
}
