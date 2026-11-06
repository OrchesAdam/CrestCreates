using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Data.FreeSql.UnitOfWork;
using FreeSql;
using FreeSql.Aop;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.Issue130;

/// <summary>
/// R3-07: the final physical DELETE must carry the current tenant predicate, not only trust the
/// ownership pre-read. The pre-read and the DELETE are separated by a deterministic row move.
/// </summary>
public sealed class Issue130TenantPhysicalDeleteRaceTests : IDisposable
{
    private readonly Issue130SqliteFile _database = new();
    private readonly Issue130CurrentUserProvider _userProvider = new();
    private readonly Issue130MutableTenant _tenant = new("tenant-a");

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task FreeSql_entity_delete_keeps_a_row_that_moves_after_the_ownership_preflight()
    {
        var freeSql = CreateFreeSql();
        SeedTenantRow();
        AttachRowMoveAfterSelect(freeSql);
        var repository = new Issue130FreeSqlRepository<Issue130TenantOnlyEntity>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);

        await repository.DeleteAsync(new Issue130TenantOnlyEntity { Id = 1, TenantId = "tenant-a", Name = "row" });

        AssertRowStillBelongsToTenantB();
    }

    [Fact]
    public async Task FreeSql_id_delete_keeps_a_row_that_moves_after_the_ownership_preflight()
    {
        var freeSql = CreateFreeSql();
        SeedTenantRow();
        AttachRowMoveAfterSelect(freeSql);
        var repository = new Issue130FreeSqlRepository<Issue130TenantOnlyEntity>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);

        await repository.DeleteAsync(1L);

        AssertRowStillBelongsToTenantB();
    }

    [Fact]
    public async Task FreeSql_entity_delete_sql_carries_the_current_tenant_predicate()
    {
        var freeSql = CreateFreeSql();
        SeedTenantRow();
        var deleteSql = new List<string>();
        freeSql.Aop.CurdBefore += (_, e) =>
        {
            if (e.CurdType == CurdType.Delete)
            {
                deleteSql.Add(e.Sql);
            }
        };
        var repository = new Issue130FreeSqlRepository<Issue130TenantOnlyEntity>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);

        await repository.DeleteAsync(new Issue130TenantOnlyEntity { Id = 1, TenantId = "tenant-a", Name = "row" });

        Assert.Single(deleteSql);
        Assert.Contains("TenantId", deleteSql[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _database.Count("S1_TenantOnly", "\"Id\" = 1"));
    }

    private IFreeSql CreateFreeSql()
        => Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);

    private void SeedTenantRow()
    {
        _database.Execute(
            """
            INSERT INTO "S1_TenantOnly" ("Id","TenantId","Name") VALUES (1,'tenant-a','row');
            """);
    }

    private void AttachRowMoveAfterSelect(IFreeSql freeSql)
    {
        var moved = false;
        freeSql.Aop.CurdAfter += (_, e) =>
        {
            if (moved || e.CurdType != CurdType.Select || e.EntityType != typeof(Issue130TenantOnlyEntity))
            {
                return;
            }

            moved = true;
            _database.Execute("UPDATE \"S1_TenantOnly\" SET \"TenantId\" = 'tenant-b' WHERE \"Id\" = 1;");
        };
    }

    private void AssertRowStillBelongsToTenantB()
    {
        Assert.Equal(1, _database.Count("S1_TenantOnly", "\"Id\" = 1"));
        Assert.Equal(1, _database.Count("S1_TenantOnly", "\"Id\" = 1 AND \"TenantId\" = 'tenant-b'"));
    }
}
