using System;
using System.Threading.Tasks;
using CrestCreates.Data.FreeSql.UnitOfWork;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.Issue130;

public sealed class ReviewS2R2MappingProbes
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreeSql_mapped_soft_delete_must_quote_identifiers(bool reservedColumn)
    {
        if (reservedColumn) await FreeSqlDelete(new ReviewReservedSoft { Id = 1 });
        else await FreeSqlDelete(new ReviewMappedSoft { Id = 1 });
    }

    private static async Task FreeSqlDelete<TEntity>(TEntity entity) where TEntity : class, IEntity<long>, ISoftDelete, new()
    {
        using var database = new Issue130SqliteFile();
        using var orm = new FreeSql.FreeSqlBuilder().UseConnectionString(FreeSql.DataType.Sqlite, database.ConnectionString).Build();
        orm.CodeFirst.SyncStructure<TEntity>();
        using var manager = new FreeSqlUnitOfWorkManager(orm);
        var repo = new Issue130FreeSqlRepository<TEntity>(manager, new Issue130CurrentUserProvider());
        await repo.InsertAsync(entity);
        Assert.Equal(1, await repo.GetCountAsync());
        await repo.DeleteAsync(1);
        Assert.Equal(0, await repo.GetCountAsync());
        Assert.Equal(1, database.Count(typeof(TEntity) == typeof(ReviewReservedSoft) ? "ReviewReservedSoft" : "ReviewMappedSoft", typeof(TEntity) == typeof(ReviewReservedSoft) ? "\"order\" = 1" : "\"deleted_flag\" = 1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlSugar_mapped_soft_delete_must_quote_identifiers(bool reservedColumn)
    {
        if (reservedColumn) await SqlSugarDelete(new ReviewReservedSoft { Id = 1 });
        else await SqlSugarDelete(new ReviewMappedSoft { Id = 1 });
    }

    private static async Task SqlSugarDelete<TEntity>(TEntity entity) where TEntity : class, IEntity<long>, ISoftDelete, new()
    {
        using var database = new Issue130SqliteFile();
        using var orm = new SqlSugar.SqlSugarClient(new SqlSugar.ConnectionConfig { DbType = SqlSugar.DbType.Sqlite, ConnectionString = database.ConnectionString, IsAutoCloseConnection = false });
        orm.CodeFirst.InitTables<TEntity>();
        var repo = new Issue130SqlSugarRepository<TEntity>(orm, new Issue130CurrentUserProvider());
        await repo.InsertAsync(entity);
        Assert.Equal(1, await repo.GetCountAsync());
        await repo.DeleteAsync(1);
        Assert.Equal(0, await repo.GetCountAsync());
        Assert.Equal(1, database.Count(typeof(TEntity) == typeof(ReviewReservedSoft) ? "ReviewReservedSoft" : "ReviewMappedSoft", typeof(TEntity) == typeof(ReviewReservedSoft) ? "\"order\" = 1" : "\"deleted_flag\" = 1"));
    }
}

[FreeSql.DataAnnotations.Table(Name = "ReviewReservedSoft")]
[SqlSugar.SugarTable("ReviewReservedSoft")]
public sealed class ReviewReservedSoft : IEntity<long>, ISoftDelete
{
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = false)]
    [SqlSugar.SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public long Id { get; set; }
    [FreeSql.DataAnnotations.Column(Name = "order")]
    [SqlSugar.SugarColumn(ColumnName = "order")]
    public bool IsDeleted { get; set; }
    [SqlSugar.SugarColumn(IsNullable = true)]
    public DateTime? DeletionTime { get; set; }
    [SqlSugar.SugarColumn(IsNullable = true)]
    public Guid? DeleterId { get; set; }
}
