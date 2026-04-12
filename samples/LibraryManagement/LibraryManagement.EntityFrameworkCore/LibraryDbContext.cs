using LibraryManagement.Domain.Entities;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.Permission;
using Microsoft.EntityFrameworkCore;
using CrestCreates.Domain.Shared.Permissions;
using System.Text.Json;
using CrestCreates.Domain.Settings;

namespace LibraryManagement.EntityFrameworkCore;

public class LibraryDbContext : DbContext
{
    public LibraryDbContext(DbContextOptions<LibraryDbContext> options) : base(options)
    {
    }

    public DbSet<Book> Books { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Member> Members { get; set; } = null!;
    public DbSet<Loan> Loans { get; set; } = null!;
    public DbSet<Permission> Permissions { get; set; } = null!;
    public DbSet<PermissionGrant> PermissionGrants { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<UserRole> UserRoles { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<IdentitySecurityLog> IdentitySecurityLogs { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<Tenant> Tenants { get; set; } = null!;
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; } = null!;
    public DbSet<SettingValue> SettingValues { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Book Configuration
        modelBuilder.Entity<Book>(entity =>
        {
            entity.ToTable("Books");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Author).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ISBN).HasMaxLength(13).IsRequired();
            entity.HasIndex(e => e.ISBN).IsUnique();
            entity.Property(e => e.Publisher).HasMaxLength(100);
            entity.Property(e => e.Location).HasMaxLength(50);
            entity.HasOne(e => e.Category)
                .WithMany(c => c.Books)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Category Configuration
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(50).IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasOne(e => e.Parent)
                .WithMany(c => c.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Member Configuration
        modelBuilder.Entity<Member>(entity =>
        {
            entity.ToTable("Members");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Phone).HasMaxLength(20);
        });

        // Loan Configuration
        modelBuilder.Entity<Loan>(entity =>
        {
            entity.ToTable("Loans");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Book)
                .WithMany()
                .HasForeignKey(e => e.BookId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Member)
                .WithMany(m => m.Loans)
                .HasForeignKey(e => e.MemberId)
                .OnDelete(DeleteBehavior.Restrict);
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
}
