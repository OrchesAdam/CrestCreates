using CrestCreates.Domain.Permission;
using CrestCreates.Domain.AuditLog;
using CrestCreates.OrmProviders.EFCore.Extensions;
using Microsoft.EntityFrameworkCore;
using SaaSHelpdesk.Domain.Entities;
using System.Text.Json;

namespace SaaSHelpdesk.EntityFrameworkCore;

public class HelpdeskDbContext : DbContext
{
    public HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options) : base(options) { }

    // Business entity DbSets
    public DbSet<Ticket> Tickets { get; set; } = null!;
    public DbSet<TicketMessage> TicketMessages { get; set; } = null!;
    public DbSet<TicketAttachment> TicketAttachments { get; set; } = null!;
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<KnowledgeBaseArticle> KnowledgeBaseArticles { get; set; } = null!;
    public DbSet<SLAPolicy> SLAPolicies { get; set; } = null!;
    public DbSet<TicketHistory> TicketHistories { get; set; } = null!;

    // Framework entity DbSets
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
    public DbSet<CrestCreates.Domain.Settings.SettingValue> SettingValues { get; set; } = null!;
    public DbSet<CrestCreates.Domain.Features.FeatureValue> FeatureValues { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Business entity configurations
        modelBuilder.Entity<Ticket>(b =>
        {
            b.ToTable("Tickets");
            b.HasKey(t => t.Id);
            b.Property(t => t.Title).HasMaxLength(200).IsRequired();
            b.Property(t => t.Description).HasColumnType("text").IsRequired();
            b.Property(t => t.Status).HasConversion<string>().HasMaxLength(50);
            b.Property(t => t.Priority).HasConversion<string>().HasMaxLength(50);
            b.Property(t => t.Type).HasConversion<string>().HasMaxLength(50);
            b.Property(t => t.CustomerId).IsRequired();
            b.HasOne(t => t.Customer).WithMany(c => c.Tickets).HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TicketMessage>(b =>
        {
            b.ToTable("TicketMessages");
            b.HasKey(m => m.Id);
            b.Property(m => m.Content).HasColumnType("text").IsRequired();
            b.Property(m => m.SenderType).HasConversion<string>().HasMaxLength(50);
            b.HasOne(m => m.Ticket).WithMany(t => t.Messages).HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TicketAttachment>(b =>
        {
            b.ToTable("TicketAttachments");
            b.HasKey(a => a.Id);
            b.Property(a => a.FileName).HasMaxLength(255).IsRequired();
            b.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
            b.Property(a => a.FileHash).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Customer>(b =>
        {
            b.ToTable("Customers");
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(100).IsRequired();
            b.Property(c => c.Email).HasMaxLength(256).IsRequired();
            b.Property(c => c.Phone).HasMaxLength(20);
            b.Property(c => c.Company).HasMaxLength(200);
            b.HasIndex(c => new { c.TenantId, c.Email }).IsUnique();
        });

        modelBuilder.Entity<Category>(b =>
        {
            b.ToTable("Categories");
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(50).IsRequired();
            b.HasOne(c => c.Parent).WithMany(c => c.Children).HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(c => !c.IsDeleted);
        });

        modelBuilder.Entity<KnowledgeBaseArticle>(b =>
        {
            b.ToTable("KnowledgeBaseArticles");
            b.HasKey(a => a.Id);
            b.Property(a => a.Title).HasMaxLength(200).IsRequired();
            b.Property(a => a.Content).HasColumnType("text").IsRequired();
            b.Property(a => a.Tags).HasMaxLength(500);
        });

        modelBuilder.Entity<SLAPolicy>(b =>
        {
            b.ToTable("SLAPolicies");
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<TicketHistory>(b =>
        {
            b.ToTable("TicketHistories");
            b.HasKey(h => h.Id);
            b.Property(h => h.ChangeType).HasConversion<string>().HasMaxLength(50);
            b.HasOne(h => h.Ticket).WithMany(t => t.History).HasForeignKey(h => h.TicketId).OnDelete(DeleteBehavior.Cascade);
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
        });

        // Configure concurrency stamp
        modelBuilder.ConfigureConcurrencyStamp();
    }
}
