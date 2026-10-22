using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Data.FreeSql.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.Issue130;

/// <summary>
/// S3 tenant matrix: per-operation tenant resolution, required/optional semantics, host behavior
/// and persisted-ownership checks. Each provider uses the real repository entry points and an
/// independent SQLite file; EF uses the generated filter registry.
/// </summary>
public sealed class Issue130TenantIsolationTests : IDisposable
{
    private readonly Issue130SqliteFile _database = new();
    private readonly Issue130CurrentUserProvider _userProvider = new();
    private readonly Issue130MutableTenant _tenant = new("tenant-a");

    public void Dispose() => _database.Dispose();

    private void SeedOptionalRows()
    {
        _database.Execute("""
            INSERT INTO "S1_OptionalTenant" ("Id","TenantId","Name") VALUES
                (1,'tenant-a','a'),
                (2,'tenant-b','b'),
                (3,NULL,'shared');
            """);
    }

    [Fact]
    public async Task T_FreeSql_optional_tenant_reads_shared_rows_and_switches_per_operation()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        SeedOptionalRows();
        var repository = new Issue130FreeSqlRepository<Issue130OptionalTenantEntity>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);

        var tenantA = await repository.GetListAsync();
        Assert.Equal(new long[] { 1, 3 }, tenantA.Select(row => row.Id).OrderBy(id => id));
        Assert.Equal(2, await repository.GetCountAsync());

        _tenant.Id = "tenant-b";
        var tenantB = await repository.GetListAsync();
        Assert.Equal(new long[] { 2, 3 }, tenantB.Select(row => row.Id).OrderBy(id => id));

        _tenant.Id = null;
        var host = await repository.GetListAsync();
        Assert.Single(host);
        Assert.Equal(3, host[0].Id);
    }

    [Fact]
    public async Task T_SqlSugar_optional_tenant_reads_shared_rows_and_switches_per_operation()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        SeedOptionalRows();
        var repository = new Issue130SqlSugarRepository<Issue130OptionalTenantEntity>(client, _userProvider, _tenant);

        var tenantA = await repository.GetListAsync();
        Assert.Equal(new long[] { 1, 3 }, tenantA.Select(row => row.Id).OrderBy(id => id));
        Assert.Equal(2, await repository.GetCountAsync());

        _tenant.Id = "tenant-b";
        var tenantB = await repository.GetListAsync();
        Assert.Equal(new long[] { 2, 3 }, tenantB.Select(row => row.Id).OrderBy(id => id));

        _tenant.Id = null;
        var host = await repository.GetListAsync();
        Assert.Single(host);
        Assert.Equal(3, host[0].Id);
    }

    [Fact]
    public async Task T_FreeSql_required_tenant_host_reads_fail_closed()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130TenantEntity>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, new Issue130MutableTenant(null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetListAsync());
    }

    [Fact]
    public async Task T_SqlSugar_required_tenant_host_reads_fail_closed()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130TenantEntity>(
            client, _userProvider, new Issue130MutableTenant(null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetListAsync());
    }

    [Fact]
    public async Task T_FreeSql_insert_fills_required_tenant_and_rejects_foreign_tenant()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130TenantEntity>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);

        await repository.InsertAsync(new Issue130TenantEntity(1, string.Empty, "filled"));
        Assert.Equal(1, _database.Count(Issue130Tables.Tenant, "\"TenantId\" = 'tenant-a'"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.InsertAsync(new Issue130TenantEntity(2, "tenant-b", "foreign")));
        Assert.Equal(0, _database.Count(Issue130Tables.Tenant, "\"Id\" = 2"));
    }

    [Fact]
    public async Task T_SqlSugar_insert_fills_required_tenant_and_rejects_foreign_tenant()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130TenantEntity>(client, _userProvider, _tenant);

        await repository.InsertAsync(new Issue130TenantEntity(1, string.Empty, "filled"));
        Assert.Equal(1, _database.Count(Issue130Tables.Tenant, "\"TenantId\" = 'tenant-a'"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.InsertAsync(new Issue130TenantEntity(2, "tenant-b", "foreign")));
        Assert.Equal(0, _database.Count(Issue130Tables.Tenant, "\"Id\" = 2"));
    }

    [Fact]
    public async Task T_FreeSql_range_delete_only_touches_the_current_tenant()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        SeedTenantOnlyRows();
        var repository = new Issue130FreeSqlRepository<Issue130TenantOnlyEntity>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);

        await repository.DeleteRangeAsync(entity => entity.Id > 0);

        Assert.Equal(0, _database.Count("S1_TenantOnly", "\"TenantId\" = 'tenant-a'"));
        Assert.Equal(1, _database.Count("S1_TenantOnly", "\"TenantId\" = 'tenant-b'"));
    }

    [Fact]
    public async Task T_SqlSugar_range_delete_only_touches_the_current_tenant()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        SeedTenantOnlyRows();
        var repository = new Issue130SqlSugarRepository<Issue130TenantOnlyEntity>(client, _userProvider, _tenant);

        await repository.DeleteRangeAsync(entity => entity.Id > 0);

        Assert.Equal(0, _database.Count("S1_TenantOnly", "\"TenantId\" = 'tenant-a'"));
        Assert.Equal(1, _database.Count("S1_TenantOnly", "\"TenantId\" = 'tenant-b'"));
    }

    [Fact]
    public async Task T_EfCore_generated_filters_enforce_required_and_optional_host_semantics()
    {
        var options = new DbContextOptionsBuilder<Issue130EfTenantDbContext>()
            .UseSqlite(_database.ConnectionString)
            .EnableServiceProviderCaching(false)
            .Options;
        using (var seed = new Issue130EfTenantDbContext(options, _tenant))
        {
            seed.Database.EnsureCreated();
        }

        InsertTenantRow(1, "tenant-a");
        InsertTenantRow(2, "tenant-b");
        SeedOptionalRows();

        using (var tenantA = new Issue130EfTenantDbContext(options, new Issue130MutableTenant("tenant-a")))
        {
            Assert.Single(tenantA.TenantEntities.ToList());
            Assert.Equal("tenant-a", tenantA.TenantEntities.Single().TenantId);
            Assert.Equal(new long[] { 1, 3 }, tenantA.OptionalTenantEntities.Select(row => row.Id).OrderBy(id => id));
        }

        using (var host = new Issue130EfTenantDbContext(options, new Issue130MutableTenant(null)))
        {
            Assert.Empty(host.TenantEntities.ToList());
            Assert.Single(host.OptionalTenantEntities.ToList());
            Assert.Null(host.OptionalTenantEntities.Single().TenantId);
        }
    }

    private void SeedTenantOnlyRows()
    {
        _database.Execute("""
            INSERT INTO "S1_TenantOnly" ("Id","TenantId","Name") VALUES
                (1,'tenant-a','a'),
                (2,'tenant-b','b');
            """);
    }

    private void InsertTenantRow(long id, string tenantId)
    {
        _database.Execute($"""
            INSERT INTO "{Issue130Tables.Tenant}"
                ("Id","TenantId","Name","CreationTime","CreatorId","LastModificationTime","LastModifierId","ConcurrencyStamp")
            VALUES ({id}, {Issue130SqliteFile.Quote(tenantId)}, 'row',
                    '2026-01-01 00:00:00', NULL, NULL, NULL, 'stamp-{id}');
            """);
    }
}
