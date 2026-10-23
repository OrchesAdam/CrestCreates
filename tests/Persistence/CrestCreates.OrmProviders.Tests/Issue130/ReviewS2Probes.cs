using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Data.EFCore.MultiTenancy;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.Data.FreeSql.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.Issue130;

[Collection("ModelFilterRegistry")]
public sealed class ReviewS2Probes
{
    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task Combined_filter_keeps_soft_deleted_rows_hidden_even_for_host(string? tenantId)
    {
        using var db = new Issue130SqliteFile();
        var options = new DbContextOptionsBuilder<ReviewCombinedContext>().UseSqlite(db.ConnectionString).EnableServiceProviderCaching(false).Options;
        await using var context = new ReviewCombinedContext(options, new Issue130MutableTenant(tenantId));
        await context.Database.EnsureCreatedAsync();
        context.Rows.AddRange(new ReviewCombinedEntity { Id = 1, TenantId = "tenant-a" },
            new ReviewCombinedEntity { Id = 2, TenantId = "tenant-a", IsDeleted = true });
        await context.SaveChangesAsync();
        Assert.Equal(2, await context.Rows.IgnoreQueryFilters().CountAsync());
        var rows = await context.Rows.ToListAsync();
        Assert.Single(rows);
        Assert.False(rows[0].IsDeleted);
    }

    [Fact]
    public async Task FreeSql_soft_delete_respects_mapped_soft_flag_column()
    {
        using var db = new Issue130SqliteFile();
        using var orm = new FreeSql.FreeSqlBuilder().UseConnectionString(FreeSql.DataType.Sqlite, db.ConnectionString).Build();
        orm.CodeFirst.SyncStructure<ReviewMappedSoft>();
        using var manager = new FreeSqlUnitOfWorkManager(orm);
        var repo = new Issue130FreeSqlRepository<ReviewMappedSoft>(manager, new Issue130CurrentUserProvider());
        await repo.InsertAsync(new ReviewMappedSoft { Id = 1 });
        Assert.Equal(1, db.Count("ReviewMappedSoft"));
        Assert.Equal(1, await repo.GetCountAsync());
        await repo.DeleteAsync(1);
        Assert.Equal(1, db.Count("ReviewMappedSoft", "\"deleted_flag\" = 1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlSugar_query_and_delete_respect_mapped_soft_flag_column(bool delete)
    {
        using var db = new Issue130SqliteFile();
        using var orm = new SqlSugar.SqlSugarClient(new SqlSugar.ConnectionConfig
        { DbType = SqlSugar.DbType.Sqlite, ConnectionString = db.ConnectionString, IsAutoCloseConnection = false });
        orm.CodeFirst.InitTables<ReviewMappedSoft>();
        var repo = new Issue130SqlSugarRepository<ReviewMappedSoft>(orm, new Issue130CurrentUserProvider());
        await repo.InsertAsync(new ReviewMappedSoft { Id = 1 });
        Assert.Equal(1, orm.Queryable<ReviewMappedSoft>().Count());
        if (delete)
        {
            await repo.DeleteAsync(1);
            Assert.Equal(1, db.Count("ReviewMappedSoft", "\"deleted_flag\" = 1"));
        }
        else
        {
            Assert.Equal(1, await repo.GetCountAsync());
        }
    }
}

public sealed class ReviewCombinedContext : DbContext
{
    private readonly Issue130MutableTenant _tenant;
    public ReviewCombinedContext(DbContextOptions<ReviewCombinedContext> options, Issue130MutableTenant? tenant = null) : base(options) => _tenant = tenant ?? new Issue130MutableTenant(null);
    public DbSet<ReviewCombinedEntity> Rows => Set<ReviewCombinedEntity>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReviewCombinedEntity>().HasKey(e => e.Id);
        SoftDeleteFilterConfiguration.ApplyAll(modelBuilder);
        TenantFilterConfiguration.ApplyAll(modelBuilder, _tenant);
    }
}

public sealed class ReviewCombinedEntity : IEntity<long>, ISoftDelete, CrestCreates.Data.EFCore.MultiTenancy.IMultiTenant
{
    public long Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletionTime { get; set; }
    public Guid? DeleterId { get; set; }
}

[FreeSql.DataAnnotations.Table(Name = "ReviewMappedSoft")]
[SqlSugar.SugarTable("ReviewMappedSoft")]
public sealed class ReviewMappedSoft : IEntity<long>, ISoftDelete
{
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = false)]
    [SqlSugar.SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public long Id { get; set; }
    [FreeSql.DataAnnotations.Column(Name = "deleted_flag")]
    [SqlSugar.SugarColumn(ColumnName = "deleted_flag")]
    public bool IsDeleted { get; set; }
    [SqlSugar.SugarColumn(IsNullable = true)]
    public DateTime? DeletionTime { get; set; }
    [SqlSugar.SugarColumn(IsNullable = true)]
    public Guid? DeleterId { get; set; }
}
