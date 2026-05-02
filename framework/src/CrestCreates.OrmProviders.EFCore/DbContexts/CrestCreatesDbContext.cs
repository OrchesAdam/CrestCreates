using System.Collections.Generic;
using System.Text.Json;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Settings;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.EFCore.Extensions;
using CrestCreates.OrmProviders.EFCore.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.DbContexts
{
    public class CrestCreatesDbContext : DbContext, IEntityFrameworkCoreDbContext, ITenantAwareDbContext
    {
        private readonly ICurrentTenant? _currentTenant;

        public CrestCreatesDbContext(DbContextOptions<CrestCreatesDbContext> options)
            : this(options, null)
        {
        }

        public CrestCreatesDbContext(
            DbContextOptions<CrestCreatesDbContext> options,
            ICurrentTenant? currentTenant)
            : base(options)
        {
            _currentTenant = currentTenant;
        }

        // DbSet properties for your entities
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
        public DbSet<FeatureValue> FeatureValues { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<TenantInitializationRecord> TenantInitializationRecords { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Product entity
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
                entity.Property(e => e.Parameters).HasMaxLength(-1); // MAX
                entity.Property(e => e.ReturnValue).HasMaxLength(-1); // MAX
                entity.Property(e => e.ExceptionMessage).HasMaxLength(4096);
                entity.Property(e => e.ExceptionStackTrace).HasMaxLength(-1); // MAX
                entity.Property(e => e.Status).IsRequired();
                entity.Property(e => e.CreationTime).IsRequired();
                entity.Property(e => e.ExtraProperties)
                    .HasConversion(
                        value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                        value => string.IsNullOrWhiteSpace(value)
                            ? new Dictionary<string, object>()
                            : JsonSerializer.Deserialize<Dictionary<string, object>>(value, (JsonSerializerOptions?)null)
                                ?? new Dictionary<string, object>());

                entity.HasIndex(e => e.TenantId);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.CreationTime);
                entity.HasIndex(e => e.TraceId);
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

                entity.HasIndex(e => new { e.TenantId, e.AttemptNo });
            });

            modelBuilder.ConfigureConcurrencyStamp();

            if (_currentTenant != null && TenantFilterRegistryStore.HasRegistrations)
            {
                modelBuilder.ConfigureTenantDiscriminator(_currentTenant);
            }
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

        public string? CurrentTenantId => _currentTenant?.Id;

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
