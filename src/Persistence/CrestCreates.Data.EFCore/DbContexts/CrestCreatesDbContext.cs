using System.Collections.Generic;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Settings;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.MultiTenancy;
using CrestCreates.Data.EFCore.ValueConverters;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.DbContexts
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
                entity.HasIndex(e => new { e.Name, e.Scope, e.ProviderKey })
                    .IsUnique()
                    .HasFilter("\"TenantId\" IS NULL");
                entity.HasIndex(e => new { e.Name, e.Scope, e.TenantId })
                    .IsUnique()
                    .HasFilter("\"TenantId\" IS NOT NULL");
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
                    .HasConversion<DictionaryToJsonValueConverter>();

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

                entity.HasIndex(e => new { e.TenantId, e.AttemptNo }).IsUnique();
            });

            modelBuilder.ConfigureConcurrencyStamp();

            if (_currentTenant != null && TenantFilterRegistryStore.HasRegistrations)
            {
                modelBuilder.ConfigureTenantDiscriminator(_currentTenant);
            }
        }

        // IEntityFrameworkCoreDbContext implementation
        // 数据访问成员经 <see cref="EffectiveDataContext"/> 路由：requiresNew 隔离期间
        // 平台推入的内层上下文优先，默认装配下的预注入仓储/上下文跟随当前 UoW；
        // 非隔离期间行为与之前完全一致（返回本实例）。
        public OrmProvider Provider => OrmProvider.EfCore;

        public IDataBaseSet<TEntity> Set<TEntity>() where TEntity : class
        {
            var effective = EffectiveDataContext;
            return new EfCoreDataBaseSet<TEntity>(
                ReferenceEquals(effective, this) ? base.Set<TEntity>() : effective.Set<TEntity>());
        }

        public new Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var effective = EffectiveDataContext;
            return ReferenceEquals(effective, this)
                ? base.SaveChangesAsync(cancellationToken)
                : effective.SaveChangesAsync(cancellationToken);
        }

        public async Task<IDataBaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            var effective = EffectiveDataContext;
            var transaction = await effective.Database.BeginTransactionAsync(cancellationToken);
            return new EfCoreDataBaseTransaction(transaction, effective);
        }

        public IDataBaseTransaction? CurrentTransaction =>
            EffectiveDataContext.Database.CurrentTransaction != null
                ? new EfCoreDataBaseTransaction(
                    EffectiveDataContext.Database.CurrentTransaction!, EffectiveDataContext)
                : null;

        public string? ConnectionString => EffectiveDataContext.Database.GetConnectionString();

        public object GetNativeContext() => EffectiveDataContext;

        public string? CurrentTenantId => _currentTenant?.Id;

        public IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class
        {
            var effective = EffectiveDataContext;
            return new EfCoreQueryableBuilder<TEntity>(
                ReferenceEquals(effective, this) ? base.Set<TEntity>() : effective.Set<TEntity>());
        }

        public Task<int> ExecuteSqlRawAsync(string sql, IEnumerable<object>? parameters = null, CancellationToken cancellationToken = default)
        {
            return EffectiveDataContext.Database.ExecuteSqlRawAsync(
                sql, parameters ?? new object[0], cancellationToken);
        }

        public new void Dispose()
        {
            base.Dispose();
        }

        /// <summary>
        /// requiresNew 隔离期间返回环境推入的内层上下文；否则返回本实例。
        /// </summary>
        private DbContext EffectiveDataContext => EfCoreAmbientContext.Resolve(this, this);
    }
}
