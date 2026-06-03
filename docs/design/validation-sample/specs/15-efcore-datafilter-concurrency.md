# Spec: EF Core, DataFilter & Concurrency

## 1. EF Core DbContext

### DbContext 结构

```csharp
public class HelpdeskDbContext : CrestDbContext<HelpdeskDbContext>
{
    // === 业务实体 ===
    public DbSet<Ticket> Tickets { get; set; }
    public DbSet<TicketMessage> TicketMessages { get; set; }
    public DbSet<TicketAttachment> TicketAttachments { get; set; }
    public DbSet<TicketHistory> TicketHistories { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<KnowledgeBaseArticle> KnowledgeBaseArticles { get; set; }
    public DbSet<SLAPolicy> SLAPolicies { get; set; }
    public DbSet<WeeklyReport> WeeklyReports { get; set; }

    // === 框架实体 ===
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityUserRole> UserRoles { get; set; }
    public DbSet<PermissionGrant> PermissionGrants { get; set; }
    public DbSet<SettingValue> SettingValues { get; set; }
    public DbSet<FeatureValue> FeatureValues { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<IdentitySecurityLog> IdentitySecurityLogs { get; set; }
}
```

### DbContext 配置

```csharp
public class HelpdeskDbContext : CrestDbContext<HelpdeskDbContext>
{
    public HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // === 启用 ConcurrencyStamp (乐观锁) ===
        modelBuilder.ConfigureConcurrencyStamp();

        // === Ticket 配置 ===
        modelBuilder.Entity<Ticket>(b =>
        {
            b.HasIndex(t => new { t.TenantId, t.Status });
            b.HasIndex(t => new { t.TenantId, t.AssigneeId });
            b.HasIndex(t => new { t.TenantId, t.CustomerId });
            b.HasIndex(t => new { t.TenantId, t.IsOverdue });
            b.HasIndex(t => new { t.TenantId, t.DueBy });

            b.Property(t => t.Title).HasMaxLength(200).IsRequired();
            b.Property(t => t.Description).HasColumnType("text");  // 无长度限制
            b.Property(t => t.Status).HasConversion<string>();
            b.Property(t => t.Priority).HasConversion<string>();
            b.Property(t => t.Type).HasConversion<string>();

            b.HasOne(t => t.Customer).WithMany(c => c.Tickets)
                .HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.Restrict);

            b.HasOne(t => t.Category).WithMany()
                .HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);

            b.HasOne(t => t.SLAPolicy).WithMany()
                .HasForeignKey(t => t.SLAPolicyId).OnDelete(DeleteBehavior.SetNull);
        });

        // === Customer 配置 ===
        modelBuilder.Entity<Customer>(b =>
        {
            b.HasIndex(c => new { c.TenantId, c.Email }).IsUnique();
            b.HasIndex(c => new { c.TenantId, c.Name });

            b.Property(c => c.Name).HasMaxLength(100).IsRequired();
            b.Property(c => c.Email).HasMaxLength(256).IsRequired();
            b.Property(c => c.Phone).HasMaxLength(20);
            b.Property(c => c.Company).HasMaxLength(200);
        });

        // === Category 配置 ===
        modelBuilder.Entity<Category>(b =>
        {
            b.HasIndex(c => new { c.TenantId, c.Name });
            b.HasOne(c => c.Parent).WithMany(c => c.Children)
                .HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        // === KnowledgeBaseArticle 配置 ===
        modelBuilder.Entity<KnowledgeBaseArticle>(b =>
        {
            b.HasIndex(a => new { a.TenantId, a.IsPublished });
            b.HasIndex(a => new { a.TenantId, a.ViewCount });

            b.Property(a => a.Title).HasMaxLength(200).IsRequired();
            b.Property(a => a.Content).HasColumnType("text");
            b.Property(a => a.Tags).HasMaxLength(500);
        });

        // === SLAPolicy 配置 ===
        modelBuilder.Entity<SLAPolicy>(b =>
        {
            b.HasIndex(s => new { s.TenantId, s.Priority }).IsUnique();
        });

        // === TicketMessage 配置 ===
        modelBuilder.Entity<TicketMessage>(b =>
        {
            b.HasIndex(m => m.TicketId);
            b.Property(m => m.Content).HasColumnType("text").IsRequired();
        });

        // === TicketAttachment 配置 ===
        modelBuilder.Entity<TicketAttachment>(b =>
        {
            b.HasIndex(a => a.TicketId);
            b.Property(a => a.FileName).HasMaxLength(256).IsRequired();
        });

        // === TicketHistory 配置 ===
        modelBuilder.Entity<TicketHistory>(b =>
        {
            b.HasIndex(h => h.TicketId);
        });
    }
}
```

## 2. Repository 实现

```csharp
public class TicketRepository :
    EfCoreRepository<HelpdeskDbContext, Ticket, Guid>,
    ITicketRepository
{
    public TicketRepository(IDbContextProvider<HelpdeskDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    public async Task<List<Ticket>> GetOverdueTicketsAsync(Guid tenantId)
    {
        var dbContext = await GetDbContextAsync();
        return await dbContext.Tickets
            .Where(t => t.Status != TicketStatus.Closed
                     && t.Status != TicketStatus.Resolved
                     && t.DueBy != null
                     && t.DueBy < DateTime.UtcNow
                     && !t.IsOverdue)
            .ToListAsync();
    }

    public async Task<List<Ticket>> GetResolvedBeforeAsync(Guid tenantId, DateTime cutoff)
    {
        var dbContext = await GetDbContextAsync();
        return await dbContext.Tickets
            .Where(t => t.Status == TicketStatus.Resolved
                     && t.ResolvedAt < cutoff)
            .ToListAsync();
    }
}
```

## 3. DataFilter

### 默认启用

```csharp
// 自动过滤 IsDeleted = false (SoftDelete)
// 自动过滤 TenantId = CurrentTenant.Id (MultiTenancy)
```

### 软删除配置

```csharp
modelBuilder.Entity<Category>(b =>
{
    b.HasQueryFilter(c => !c.IsDeleted);  // 自动过滤软删除
    // Tenant filter 由框架 DataFilter 自动添加
});

modelBuilder.Entity<KnowledgeBaseArticle>(b =>
{
    // 知识库文章不支持软删除（直接物理删除）
    // Tenant filter 由框架自动添加
});
```

### 临时禁用过滤器

```csharp
// 需要在所有租户范围内检查数据时（如 SLA Job）
using (_unitOfWork.DisableFilter(DataFilter.MultiTenancy))
{
    var allTenants = await _tenantRepository.GetListAsync();
}
```

## 4. Concurrency

```csharp
// 启用乐观锁（已通过 ConfigureConcurrencyStamp() 配置）
// 并发修改同一工单时抛出 CrestConcurrencyException

public async Task<TicketDto> UpdateAsync(Guid id, UpdateTicketDto input)
{
    var ticket = await _ticketRepo.GetAsync(id);
    ticket.UpdateTitle(input.Title);  // 内部会触发 ConcurrencyStamp 更新
    await _unitOfWork.SaveChangesAsync();
    // 如果中间被其他人修改，SaveChangesAsync 抛出 DbUpdateConcurrencyException
    return ticket.ToDto();
}
```

## 验证检查点

- [ ] `Ticket.CustomerId` 关联查询正确加载
- [ ] Soft Delete 策略：查询不返回 `IsDeleted = true` 的分类
- [ ] Tenant Filter：租户A的查询不返回租户B的数据
- [ ] `Unique Email Per Tenant`：同租户下创建相同邮箱客户失败
- [ ] ConcurrencyStamp：并发修改同一工单时第二个操作失败
- [ ] `DisableFilter` 在跨租户 Job 中正确工作
- [ ] 索引覆盖所有高频查询字段
