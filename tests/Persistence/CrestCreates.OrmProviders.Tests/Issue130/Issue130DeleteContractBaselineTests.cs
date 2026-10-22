using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.OrmProviders.Tests.Issue130;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.Issue130;

/// <summary>
/// #130 S1 delete-contract baselines (D01-D08). Red cases are intentional: they execute the real
/// repository entry point, read the row through an independent SQLite connection, and fail on the
/// contract that S2 must implement. Positive controls sit next to each red batch.
/// </summary>
public sealed class Issue130DeleteContractBaselineTests : IDisposable
{
    private readonly Issue130SqliteFile _database = new();
    private readonly Issue130CurrentUser _user = new();
    private readonly Issue130CurrentUserProvider _userProvider = new();
    private readonly Issue130MutableTenant _tenant = new("tenant-a");

    public void Dispose() => _database.Dispose();

    private void InsertStamped(long id, string name, bool isDeleted = false)
    {
        _database.Execute($"""
            INSERT INTO "{Issue130Tables.SoftStamped}"
                ("Id","Name","CreationTime","CreatorId","LastModificationTime","LastModifierId",
                 "ConcurrencyStamp","IsDeleted","DeletionTime","DeleterId")
            VALUES ({id}, {Issue130SqliteFile.Quote(name)}, '2026-01-01 00:00:00', NULL, NULL, NULL,
                    'stamp-{id}', {(isDeleted ? 1 : 0)},
                    {(isDeleted ? "'2026-01-02 00:00:00'" : "NULL")}, {(isDeleted ? "'11111111-1111-1111-1111-111111111111'" : "NULL")});
            """);
    }

    // ---------------------------------------------------------------- EF Core

    [Fact]
    public async Task D01_EfCore_tracked_delete_overloads_preserve_rows_and_write_delete_audit()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = Issue130EfBed.Repository<Issue130SoftStamped>(context);

        await repository.InsertAsync(new Issue130SoftStamped(1, "by-entity"));
        await repository.InsertAsync(new Issue130SoftStamped(2, "by-id"));
        await repository.InsertAsync(new Issue130SoftStamped(3, "range"));
        await repository.InsertAsync(new Issue130SoftStamped(4, "predicate"));

        await repository.DeleteAsync(await repository.GetAsync(1));
        await repository.DeleteAsync(2);
        await repository.DeleteRangeAsync(new[] { await repository.GetAsync(3) });
        await repository.DeleteRangeAsync(e => e.Id == 4);

        foreach (var id in new long[] { 1, 2, 3, 4 })
        {
            var row = _database.Read(Issue130Tables.SoftStamped, id);
            Assert.True(row.Exists, $"row {id} must be retained");
            Assert.Equal(1, row.Integer("IsDeleted"));
            Assert.NotNull(row.DateTimeValue("DeletionTime"));
            Assert.Equal(Guid.Parse(_user.Id), row.GuidValue("DeleterId"));
        }
    }

    [Fact]
    public async Task D02_EfCore_default_reads_hide_soft_deleted_rows()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        InsertStamped(1, "live");
        InsertStamped(2, "deleted", isDeleted: true);

        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = Issue130EfBed.Repository<Issue130SoftStamped>(context);

        var list = await repository.GetListAsync();
        var byId = await repository.GetAsync(2);
        var count = await repository.GetCountAsync();
        var any = await repository.AnyAsync();
        var exists = await repository.ExistsAsync(2);
        var page = await repository.GetPagedAsync(0, 10);

        Assert.Multiple(
            () => Assert.Single(list),
            () => Assert.Null(byId),
            () => Assert.Equal(1, count),
            () => Assert.True(any),
            () => Assert.False(exists),
            () => Assert.Single(page.Items));
    }

    [Fact]
    public async Task D03_EfCore_generic_repository_stamped_id_delete_must_preserve_the_row()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = Issue130EfBed.Repository<Issue130SoftStamped>(context);
        await repository.InsertAsync(new Issue130SoftStamped(1, "stamped"));
        var stamp = (await repository.GetAsync(1))!.ConcurrencyStamp;

        await repository.DeleteAsync(1, stamp);

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.True(row.Exists, "stamped id delete must not bypass soft delete with ExecuteDelete");
        Assert.Equal(1, row.Integer("IsDeleted"));
        Assert.NotEqual(stamp, row.Text("ConcurrencyStamp"));
        Assert.Equal(Guid.Parse(_user.Id), row.GuidValue("DeleterId"));
    }

    [Fact]
    public async Task D03_EfCore_platform_repository_base_stamped_id_delete_must_preserve_the_row()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        InsertStamped(1, "stamped-base");

        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = new EfCoreRepositoryBaseProbe(context);

        await repository.DeleteAsync(1, "stamp-1");

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.True(row.Exists, "the platform base repository stamped delete must not bypass soft delete");
        Assert.Equal(1, row.Integer("IsDeleted"));
    }

    [Fact]
    public async Task D04_EfCore_repeated_stamp_less_delete_must_not_refresh_delete_audit()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = Issue130EfBed.Repository<Issue130SoftOnly>(context);
        await repository.InsertAsync(new Issue130SoftOnly(1, "soft-only"));

        await repository.DeleteAsync(1);
        var firstDeleter = _database.Read(Issue130Tables.SoftOnly, 1).GuidValue("DeleterId");
        _user.Id = "33333333-3333-3333-3333-333333333333";
        await repository.DeleteAsync(1);

        var row = _database.Read(Issue130Tables.SoftOnly, 1);
        Assert.True(row.Exists);
        Assert.Equal(1, row.Integer("IsDeleted"));
        Assert.Equal(firstDeleter, row.GuidValue("DeleterId"));
    }

    [Fact]
    public async Task D04_FreeSql_repeated_stamp_less_delete_must_not_refresh_delete_audit()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130SoftOnly>(
            new CrestCreates.Data.FreeSql.UnitOfWork.FreeSqlUnitOfWorkManager(freeSql));
        await repository.InsertAsync(new Issue130SoftOnly(1, "soft-only"));

        await repository.DeleteAsync(1);
        var firstDeleter = _database.Read(Issue130Tables.SoftOnly, 1).GuidValue("DeleterId");
        _userProvider.UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        await repository.DeleteAsync(1);

        var row = _database.Read(Issue130Tables.SoftOnly, 1);
        Assert.True(row.Exists);
        Assert.Equal(1, row.Integer("IsDeleted"));
        Assert.Equal(firstDeleter, row.GuidValue("DeleterId"));
    }

    [Fact]
    public async Task D04_SqlSugar_repeated_stamp_less_delete_must_not_refresh_delete_audit()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130SoftOnly>(client);
        await repository.InsertAsync(new Issue130SoftOnly(1, "soft-only"));

        await repository.DeleteAsync(1);
        var firstDeleter = _database.Read(Issue130Tables.SoftOnly, 1).GuidValue("DeleterId");
        _userProvider.UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        await repository.DeleteAsync(1);

        var row = _database.Read(Issue130Tables.SoftOnly, 1);
        Assert.True(row.Exists);
        Assert.Equal(1, row.Integer("IsDeleted"));
        Assert.Equal(firstDeleter, row.GuidValue("DeleterId"));
    }

    [Fact]
    public async Task D05_EfCore_non_soft_delete_entity_stays_physical()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = Issue130EfBed.Repository<Issue130Plain>(context);
        await repository.InsertAsync(new Issue130Plain(1, "plain"));

        await repository.DeleteAsync(1);

        Assert.Equal(0, _database.Count(Issue130Tables.Plain));
    }

    [Fact]
    public async Task D06_EfCore_stale_stamp_delete_throws_and_leaves_the_row_untouched()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        InsertStamped(1, "stale");

        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = Issue130EfBed.Repository<Issue130SoftStamped>(context);

        await Assert.ThrowsAsync<CrestConcurrencyException>(() => repository.DeleteAsync(1, "stale-stamp"));

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.True(row.Exists);
        Assert.Equal(0, row.Integer("IsDeleted"));
        Assert.Null(row.DateTimeValue("DeletionTime"));
    }

    [Fact]
    public async Task D08_EfCore_batch_delete_of_stamped_entities_must_reject_before_any_write()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        InsertStamped(1, "a");
        InsertStamped(2, "b");

        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = Issue130EfBed.Repository<Issue130SoftStamped>(context);
        var entities = await repository.GetListAsync();

        var exception = await Record.ExceptionAsync(() => repository.DeleteRangeAsync(entities));

        Assert.IsType<NotSupportedException>(exception);
        Assert.Contains("stamp", exception!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _database.Count(Issue130Tables.SoftStamped, "\"IsDeleted\" = 1"));
    }

    [Fact]
    public async Task D08_EfCore_predicate_delete_of_stamped_entities_must_reject_before_any_write()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        InsertStamped(1, "a");
        InsertStamped(2, "b");

        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = Issue130EfBed.Repository<Issue130SoftStamped>(context);

        var exception = await Record.ExceptionAsync(() => repository.DeleteRangeAsync(e => e.Id > 0));

        Assert.IsType<NotSupportedException>(exception);
        Assert.Contains("stamp", exception!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _database.Count(Issue130Tables.SoftStamped, "\"IsDeleted\" = 1"));
    }

    // ---------------------------------------------------------------- FreeSql

    [Fact]
    public async Task D01_FreeSql_DeleteAsync_entity_must_preserve_the_row()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130SoftStamped>(new CrestCreates.Data.FreeSql.UnitOfWork.FreeSqlUnitOfWorkManager(freeSql));
        await repository.InsertAsync(new Issue130SoftStamped(1, "entity"));

        await repository.DeleteAsync(new Issue130SoftStamped(1, "entity") { ConcurrencyStamp = (await repository.GetAsync(1))!.ConcurrencyStamp });

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.True(row.Exists, "FreeSql DeleteAsync(entity) must issue an UPDATE, not DELETE");
        Assert.Equal(1, row.Integer("IsDeleted"));
        Assert.NotNull(row.DateTimeValue("DeletionTime"));
        Assert.Equal(_userProvider.UserId, row.GuidValue("DeleterId"));
    }

    [Fact]
    public async Task D01_FreeSql_DeleteAsync_id_must_preserve_the_row()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130SoftStamped>(new CrestCreates.Data.FreeSql.UnitOfWork.FreeSqlUnitOfWorkManager(freeSql));
        await repository.InsertAsync(new Issue130SoftStamped(1, "id"));

        await repository.DeleteAsync(1);

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.True(row.Exists, "FreeSql DeleteAsync(id) must issue an UPDATE, not DELETE");
        Assert.Equal(1, row.Integer("IsDeleted"));
        Assert.NotNull(row.DateTimeValue("DeletionTime"));
        Assert.Equal(_userProvider.UserId, row.GuidValue("DeleterId"));
    }

    [Fact]
    public async Task D01_FreeSql_DeleteRangeAsync_entities_must_preserve_rows()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130SoftStamped>(new CrestCreates.Data.FreeSql.UnitOfWork.FreeSqlUnitOfWorkManager(freeSql));
        await repository.InsertAsync(new Issue130SoftStamped(1, "a"));
        await repository.InsertAsync(new Issue130SoftStamped(2, "b"));

        await repository.DeleteRangeAsync(await repository.GetListAsync());

        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped));
        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped, "\"IsDeleted\" = 1"));
        foreach (var id in new long[] { 1, 2 })
        {
            var row = _database.Read(Issue130Tables.SoftStamped, id);
            Assert.NotNull(row.DateTimeValue("DeletionTime"));
            Assert.Equal(_userProvider.UserId, row.GuidValue("DeleterId"));
        }
    }

    [Fact]
    public async Task D01_FreeSql_DeleteRangeAsync_predicate_must_preserve_rows()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130SoftStamped>(new CrestCreates.Data.FreeSql.UnitOfWork.FreeSqlUnitOfWorkManager(freeSql));
        await repository.InsertAsync(new Issue130SoftStamped(1, "a"));
        await repository.InsertAsync(new Issue130SoftStamped(2, "b"));

        await repository.DeleteRangeAsync(e => e.Id > 0);

        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped));
        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped, "\"IsDeleted\" = 1"));
        foreach (var id in new long[] { 1, 2 })
        {
            var row = _database.Read(Issue130Tables.SoftStamped, id);
            Assert.NotNull(row.DateTimeValue("DeletionTime"));
            Assert.Equal(_userProvider.UserId, row.GuidValue("DeleterId"));
        }
    }

    [Fact]
    public async Task D02_FreeSql_production_audit_wiring_filters_deleted_rows_from_reads()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130SoftStamped>(new CrestCreates.Data.FreeSql.UnitOfWork.FreeSqlUnitOfWorkManager(freeSql));
        await repository.InsertAsync(new Issue130SoftStamped(1, "live"));
        await repository.InsertAsync(new Issue130SoftStamped(2, "hidden"));
        await repository.DeleteAsync(2);

        Assert.Single(await repository.GetListAsync());
        Assert.Null(await repository.GetAsync(2));
        Assert.Equal(1, await repository.GetCountAsync());
        Assert.False(await repository.ExistsAsync(2));
    }

    [Fact]
    public async Task D03_FreeSql_stamped_id_delete_must_preserve_the_row_and_advance_the_stamp()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130SoftStamped>(new CrestCreates.Data.FreeSql.UnitOfWork.FreeSqlUnitOfWorkManager(freeSql));
        await repository.InsertAsync(new Issue130SoftStamped(1, "stamped"));
        var stamp = (await repository.GetAsync(1))!.ConcurrencyStamp;

        await repository.DeleteAsync(1, stamp);

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.True(row.Exists);
        Assert.Equal(1, row.Integer("IsDeleted"));
        Assert.NotEqual(stamp, row.Text("ConcurrencyStamp"));
        Assert.NotNull(row.DateTimeValue("DeletionTime"));
        Assert.Equal(_userProvider.UserId, row.GuidValue("DeleterId"));
    }

    [Fact]
    public async Task D06_FreeSql_stale_stamp_delete_throws_and_leaves_the_row_untouched()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130SoftStamped>(new CrestCreates.Data.FreeSql.UnitOfWork.FreeSqlUnitOfWorkManager(freeSql));
        await repository.InsertAsync(new Issue130SoftStamped(1, "stale"));

        await Assert.ThrowsAsync<CrestConcurrencyException>(() => repository.DeleteAsync(1, "stale-stamp"));

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.True(row.Exists);
        Assert.Equal(0, row.Integer("IsDeleted"));
    }

    [Fact]
    public async Task D08_FreeSql_batch_delete_of_stamped_entities_must_reject_before_any_write()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130SoftStamped>(new CrestCreates.Data.FreeSql.UnitOfWork.FreeSqlUnitOfWorkManager(freeSql));
        await repository.InsertAsync(new Issue130SoftStamped(1, "a"));
        await repository.InsertAsync(new Issue130SoftStamped(2, "b"));
        var entities = await repository.GetListAsync();

        var exception = await Record.ExceptionAsync(() => repository.DeleteRangeAsync(entities));

        Assert.IsType<NotSupportedException>(exception);
        Assert.Contains("stamp", exception!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped, "\"IsDeleted\" = 0"));
    }

    [Fact]
    public async Task D05_FreeSql_non_soft_delete_entity_stays_physical()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130Plain>(new CrestCreates.Data.FreeSql.UnitOfWork.FreeSqlUnitOfWorkManager(freeSql));
        await repository.InsertAsync(new Issue130Plain(1, "plain"));

        await repository.DeleteAsync(1);

        Assert.Equal(0, _database.Count(Issue130Tables.Plain));
    }

    // ---------------------------------------------------------------- SqlSugar

    [Fact]
    public async Task D01_SqlSugar_DeleteAsync_entity_must_preserve_the_row()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130SoftStamped>(client);
        await repository.InsertAsync(new Issue130SoftStamped(1, "entity"));
        var loaded = await repository.GetAsync(1);

        await repository.DeleteAsync(loaded!);

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.True(row.Exists, "SqlSugar DeleteAsync(entity) must issue an UPDATE, not DELETE");
        Assert.Equal(1, row.Integer("IsDeleted"));
        Assert.NotNull(row.DateTimeValue("DeletionTime"));
        Assert.Equal(_userProvider.UserId, row.GuidValue("DeleterId"));
    }

    [Fact]
    public async Task D01_SqlSugar_DeleteAsync_id_must_preserve_the_row()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130SoftStamped>(client);
        await repository.InsertAsync(new Issue130SoftStamped(1, "id"));

        await repository.DeleteAsync(1);

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.True(row.Exists, "SqlSugar DeleteAsync(id) must issue an UPDATE, not DELETE");
        Assert.Equal(1, row.Integer("IsDeleted"));
        Assert.NotNull(row.DateTimeValue("DeletionTime"));
        Assert.Equal(_userProvider.UserId, row.GuidValue("DeleterId"));
    }

    [Fact]
    public async Task D01_SqlSugar_DeleteRangeAsync_entities_must_preserve_rows()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130SoftStamped>(client);
        await repository.InsertAsync(new Issue130SoftStamped(1, "a"));
        await repository.InsertAsync(new Issue130SoftStamped(2, "b"));

        await repository.DeleteRangeAsync(await repository.GetListAsync());

        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped));
        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped, "\"IsDeleted\" = 1"));
    }

    [Fact]
    public async Task D01_SqlSugar_DeleteRangeAsync_predicate_must_preserve_rows()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130SoftStamped>(client);
        await repository.InsertAsync(new Issue130SoftStamped(1, "a"));
        await repository.InsertAsync(new Issue130SoftStamped(2, "b"));

        await repository.DeleteRangeAsync(e => e.Id > 0);

        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped));
        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped, "\"IsDeleted\" = 1"));
        foreach (var id in new long[] { 1, 2 })
        {
            var row = _database.Read(Issue130Tables.SoftStamped, id);
            Assert.NotNull(row.DateTimeValue("DeletionTime"));
            Assert.Equal(_userProvider.UserId, row.GuidValue("DeleterId"));
        }
    }

    [Fact]
    public async Task D02_SqlSugar_filtering_must_be_applied_by_production_read_paths()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        InsertStamped(1, "live");
        InsertStamped(2, "deleted", isDeleted: true);
        var repository = new Issue130SqlSugarRepository<Issue130SoftStamped>(client);

        var list = await repository.GetListAsync();
        var byId = await repository.GetAsync(2);
        var count = await repository.GetCountAsync();
        var exists = await repository.ExistsAsync(2);

        Assert.Multiple(
            () => Assert.Single(list),
            () => Assert.Null(byId),
            () => Assert.Equal(1, count),
            () => Assert.False(exists));
    }

    [Fact]
    public async Task D03_SqlSugar_stamped_id_delete_must_preserve_the_row_and_advance_the_stamp()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130SoftStamped>(client);
        await repository.InsertAsync(new Issue130SoftStamped(1, "stamped"));
        var stamp = (await repository.GetAsync(1))!.ConcurrencyStamp;

        await repository.DeleteAsync(1, stamp);

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.True(row.Exists);
        Assert.Equal(1, row.Integer("IsDeleted"));
        Assert.NotEqual(stamp, row.Text("ConcurrencyStamp"));
        Assert.NotNull(row.DateTimeValue("DeletionTime"));
        Assert.Equal(_userProvider.UserId, row.GuidValue("DeleterId"));
    }

    [Fact]
    public async Task D06_SqlSugar_stale_stamp_delete_throws_and_leaves_the_row_untouched()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130SoftStamped>(client);
        await repository.InsertAsync(new Issue130SoftStamped(1, "stale"));

        await Assert.ThrowsAsync<CrestConcurrencyException>(() => repository.DeleteAsync(1, "stale-stamp"));

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.True(row.Exists);
        Assert.Equal(0, row.Integer("IsDeleted"));
    }

    [Fact]
    public async Task D08_SqlSugar_batch_delete_of_stamped_entities_must_reject_before_any_write()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130SoftStamped>(client);
        await repository.InsertAsync(new Issue130SoftStamped(1, "a"));
        await repository.InsertAsync(new Issue130SoftStamped(2, "b"));
        var entities = await repository.GetListAsync();

        var exception = await Record.ExceptionAsync(() => repository.DeleteRangeAsync(entities));

        Assert.IsType<NotSupportedException>(exception);
        Assert.Contains("stamp", exception!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped, "\"IsDeleted\" = 0"));
    }

    [Fact]
    public async Task D05_SqlSugar_non_soft_delete_entity_stays_physical()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130Plain>(client);
        await repository.InsertAsync(new Issue130Plain(1, "plain"));

        await repository.DeleteAsync(1);

        Assert.Equal(0, _database.Count(Issue130Tables.Plain));
    }
}
