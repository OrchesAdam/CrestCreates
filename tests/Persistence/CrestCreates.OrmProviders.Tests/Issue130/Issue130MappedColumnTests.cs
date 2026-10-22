using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Data.FreeSql.UnitOfWork;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.Issue130;

/// <summary>
/// R1-2 regression: physical column names are resolved through FreeSql/SqlSugar mappings, never
/// assumed to equal the CLR property names. Every delete overload and query path is exercised with
/// a renamed soft flag, primary key and concurrency stamp column.
/// </summary>
public sealed class Issue130MappedColumnTests : IDisposable
{
    private const string MappedStamped = "S1_MappedSoftStamped";
    private const string MappedOnly = "S1_MappedSoftOnly";

    private readonly Issue130SqliteFile _database = new();
    private readonly Issue130CurrentUserProvider _userProvider = new();
    private readonly Issue130MutableTenant _tenant = new("tenant-a");

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task D01_FreeSql_mapped_columns_cover_reads_and_single_deletes()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130MappedSoftStamped>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider);
        await repository.InsertAsync(new Issue130MappedSoftStamped { Id = 1, Name = "entity" });
        await repository.InsertAsync(new Issue130MappedSoftStamped { Id = 2, Name = "id" });
        await repository.InsertAsync(new Issue130MappedSoftStamped { Id = 3, Name = "stamp" });

        Assert.Equal(3, await repository.GetCountAsync());
        Assert.Equal(3, (await repository.GetListAsync()).Count);
        Assert.NotNull(await repository.GetAsync(1));
        Assert.True(await repository.ExistsAsync(1));
        Assert.True(await repository.AnyAsync());

        await repository.DeleteAsync((await repository.GetAsync(1))!);
        await repository.DeleteAsync(2);
        var stamp = (await repository.GetAsync(3))!.ConcurrencyStamp;
        await repository.DeleteAsync(3, stamp);

        Assert.Equal(3, _database.Count(MappedStamped));
        Assert.Equal(3, _database.Count(MappedStamped, "\"deleted_flag\" = 1"));
        Assert.Equal(1, _database.Count(MappedStamped, $"\"row_id\" = 3 AND \"stamp_col\" <> '{stamp}'"));
        Assert.Equal(3, _database.Count(MappedStamped, "\"deleted_by\" IS NOT NULL"));
    }

    [Fact]
    public async Task D01_SqlSugar_mapped_columns_cover_reads_and_single_deletes()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130MappedSoftStamped>(client, _userProvider);
        await repository.InsertAsync(new Issue130MappedSoftStamped { Id = 1, Name = "entity" });
        await repository.InsertAsync(new Issue130MappedSoftStamped { Id = 2, Name = "id" });
        await repository.InsertAsync(new Issue130MappedSoftStamped { Id = 3, Name = "stamp" });

        Assert.Equal(3, await repository.GetCountAsync());
        Assert.Equal(3, (await repository.GetListAsync()).Count);
        Assert.NotNull(await repository.GetAsync(1));
        Assert.True(await repository.ExistsAsync(1));
        Assert.True(await repository.AnyAsync());

        await repository.DeleteAsync((await repository.GetAsync(1))!);
        await repository.DeleteAsync(2);
        var stamp = (await repository.GetAsync(3))!.ConcurrencyStamp;
        await repository.DeleteAsync(3, stamp);

        Assert.Equal(3, _database.Count(MappedStamped));
        Assert.Equal(3, _database.Count(MappedStamped, "\"deleted_flag\" = 1"));
        Assert.Equal(3, _database.Count(MappedStamped, "\"deleted_by\" IS NOT NULL"));
    }

    [Fact]
    public async Task A02_FreeSql_mapped_stamp_update_uses_mapped_columns()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130MappedSoftStamped>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider);
        await repository.InsertAsync(new Issue130MappedSoftStamped { Id = 1, Name = "original" });
        var loaded = (await repository.GetAsync(1))!;
        loaded.Name = "updated";

        await repository.UpdateAsync(loaded);

        Assert.Equal(1, _database.Count(MappedStamped, "\"row_id\" = 1 AND Name = 'updated'"));
        loaded.ConcurrencyStamp = "stale";
        loaded.Name = "stale-update";
        await Assert.ThrowsAsync<CrestCreates.Domain.Exceptions.CrestConcurrencyException>(
            () => repository.UpdateAsync(loaded));
        Assert.Equal(1, _database.Count(MappedStamped, "\"row_id\" = 1 AND Name = 'updated'"));
    }

    [Fact]
    public async Task A02_SqlSugar_mapped_stamp_update_uses_mapped_columns()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130MappedSoftStamped>(client, _userProvider);
        await repository.InsertAsync(new Issue130MappedSoftStamped { Id = 1, Name = "original" });
        var loaded = (await repository.GetAsync(1))!;
        loaded.Name = "updated";

        await repository.UpdateAsync(loaded);

        Assert.Equal(1, _database.Count(MappedStamped, "\"row_id\" = 1 AND Name = 'updated'"));
        loaded.ConcurrencyStamp = "stale";
        loaded.Name = "stale-update";
        await Assert.ThrowsAsync<CrestCreates.Domain.Exceptions.CrestConcurrencyException>(
            () => repository.UpdateAsync(loaded));
        Assert.Equal(1, _database.Count(MappedStamped, "\"row_id\" = 1 AND Name = 'updated'"));
    }

    [Fact]
    public async Task D01_FreeSql_mapped_columns_cover_batch_deletes()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130MappedSoftOnly>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider);
        await repository.InsertAsync(new Issue130MappedSoftOnly { Id = 1, Name = "a" });
        await repository.InsertAsync(new Issue130MappedSoftOnly { Id = 2, Name = "b" });
        await repository.InsertAsync(new Issue130MappedSoftOnly { Id = 3, Name = "c" });
        await repository.InsertAsync(new Issue130MappedSoftOnly { Id = 4, Name = "d" });

        await repository.DeleteRangeAsync(new[] { (await repository.GetAsync(1))!, (await repository.GetAsync(2))! });
        await repository.DeleteRangeAsync(entity => entity.Id > 2);

        Assert.Equal(4, _database.Count(MappedOnly));
        Assert.Equal(4, _database.Count(MappedOnly, "\"deleted_flag\" = 1"));
    }

    [Fact]
    public async Task D01_SqlSugar_mapped_columns_cover_batch_deletes()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130MappedSoftOnly>(client, _userProvider);
        await repository.InsertAsync(new Issue130MappedSoftOnly { Id = 1, Name = "a" });
        await repository.InsertAsync(new Issue130MappedSoftOnly { Id = 2, Name = "b" });
        await repository.InsertAsync(new Issue130MappedSoftOnly { Id = 3, Name = "c" });
        await repository.InsertAsync(new Issue130MappedSoftOnly { Id = 4, Name = "d" });

        await repository.DeleteRangeAsync(new[] { (await repository.GetAsync(1))!, (await repository.GetAsync(2))! });
        await repository.DeleteRangeAsync(entity => entity.Id > 2);

        Assert.Equal(4, _database.Count(MappedOnly));
        Assert.Equal(4, _database.Count(MappedOnly, "\"deleted_flag\" = 1"));
    }
}
