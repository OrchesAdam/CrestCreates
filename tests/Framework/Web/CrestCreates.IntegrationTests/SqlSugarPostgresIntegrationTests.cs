using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Data.SqlSugar.Repositories;
using CrestCreates.DataFilter.Entities;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.MultiTenancy.Abstract;
using Moq;
using Npgsql;
using SqlSugar;
using Testcontainers.PostgreSql;
using Xunit;

namespace CrestCreates.IntegrationTests;

public sealed class SqlSugarPostgresIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("crest_sqlsugar_test").WithUsername("test").WithPassword("test").Build();
    private readonly string _schema = $"itest_{Guid.NewGuid():N}";
    private readonly List<Statement> _statements = new();
    private SqlSugarClient _client = null!;
    private static readonly DateTime DeletedAt = new(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA \"{_schema}\"";
        await command.ExecuteNonQueryAsync();
        _client = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.PostgreSQL,
            ConnectionString = $"{_postgres.GetConnectionString()};Search Path={_schema}",
            IsAutoCloseConnection = true
        });
        _client.CodeFirst.InitTables<SoftRow>();
        _client.CodeFirst.InitTables<StampedRow>();
        // Copy values now: SDK parameter objects may be reused after execution.
        _client.Aop.OnLogExecuting = (sql, parameters) => _statements.Add(new Statement(sql,
            parameters.ToDictionary(p => p.ParameterName.TrimStart('@', ':'), p => p.Value)));
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        await _postgres.DisposeAsync();
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("A", 0)]
    [InlineData("B", 0)]
    [InlineData(null, 1)]
    [InlineData("A", 1)]
    [InlineData("B", 1)]
    [InlineData(null, 2)]
    [InlineData("A", 2)]
    [InlineData("B", 2)]
    [InlineData(null, 3)]
    [InlineData("A", 3)]
    [InlineData("B", 3)]
    public async Task Soft_delete_overloads_bind_boolean_and_preserve_tenant_and_live_conditions(string? tenant, int operation)
    {
        var rows = new[] { Row(1, null), Row(2, "A"), Row(3, "B"), Row(4, tenant, true) };
        await _client.Insertable(rows).ExecuteCommandAsync();
        var repository = CreateRepository<SoftRow>(tenant);
        var target = rows.Single(row => row.Id <= 3 && row.TenantId == tenant);
        _statements.Clear();

        switch (operation)
        {
            case 0: await repository.DeleteAsync(target); break;
            case 1: await repository.DeleteAsync(target.Id); break;
            case 2: await repository.DeleteRangeAsync(rows); break;
            default: await repository.DeleteRangeAsync(row => row.Id > 0); break;
        }

        AssertUpdate(tenant, stamped: false);
        var persisted = await _client.Queryable<SoftRow>().ClearFilter().OrderBy(row => row.Id).ToListAsync();
        Assert.Equal(4, persisted.Count);
        foreach (var row in persisted)
        {
            Assert.Equal(row.Id == target.Id || row.Id == 4, row.IsDeleted);
            if (row.Id == 4) Assert.Equal(DeletedAt, row.DeletionTime);
        }

        // Even a caller-supplied live entity must not rewrite a persisted tombstone.
        _statements.Clear();
        if (operation == 0) await repository.DeleteAsync(Row(4, tenant));
        else if (operation == 1) await repository.DeleteAsync(4);
        else if (operation == 2) await repository.DeleteRangeAsync(new[] { Row(4, tenant) });
        else await repository.DeleteRangeAsync(row => row.Id == 4);
        var tombstone = await _client.Queryable<SoftRow>().ClearFilter().Where(row => row.Id == 4).SingleAsync();
        Assert.Equal(DeletedAt, tombstone.DeletionTime);

        if (operation < 2)
        {
            var foreign = rows.First(row => row.Id <= 3 && row.TenantId != tenant);
            _statements.Clear();
            await Assert.ThrowsAsync<InvalidOperationException>(() => operation == 0
                ? repository.DeleteAsync(foreign) : repository.DeleteAsync(foreign.Id));
            Assert.DoesNotContain(_statements, statement => IsUpdate(statement.Sql));
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("A", false)]
    [InlineData("B", false)]
    [InlineData(null, true)]
    [InlineData("A", true)]
    [InlineData("B", true)]
    public async Task Stamped_delete_checks_old_stamp_and_advances_only_live_owned_row(string? tenant, bool byId)
    {
        var rows = new[] { Stamped(1, null), Stamped(2, "A"), Stamped(3, "B"), Stamped(4, tenant, true) };
        await _client.Insertable(rows).ExecuteCommandAsync();
        var repository = CreateRepository<StampedRow>(tenant);
        var target = rows.Single(row => row.Id <= 3 && row.TenantId == tenant);
        _statements.Clear();
        await Assert.ThrowsAsync<CrestConcurrencyException>(() => Delete("stale"));
        AssertUpdate(tenant, stamped: true);
        Assert.False((await Read(target.Id)).IsDeleted);

        _statements.Clear();
        await Delete("old-stamp");
        AssertUpdate(tenant, stamped: true);
        var deleted = await Read(target.Id);
        Assert.True(deleted.IsDeleted);
        Assert.NotEqual("old-stamp", deleted.ConcurrencyStamp);
        await Assert.ThrowsAsync<CrestConcurrencyException>(() => Delete("old-stamp"));
        Assert.Equal(deleted.ConcurrencyStamp, (await Read(target.Id)).ConcurrencyStamp);
        await Assert.ThrowsAsync<CrestConcurrencyException>(() => byId
            ? repository.DeleteAsync(4, "old-stamp")
            : repository.DeleteAsync(Stamped(4, tenant)));
        Assert.Equal(DeletedAt, (await Read(4)).DeletionTime);
        Assert.Equal("old-stamp", (await Read(4)).ConcurrencyStamp);
        foreach (var foreign in rows.Where(row => row.Id <= 3 && row.TenantId != tenant))
        {
            _statements.Clear();
            await Assert.ThrowsAsync<InvalidOperationException>(() => byId
                ? repository.DeleteAsync(foreign.Id, "old-stamp") : repository.DeleteAsync(foreign));
            Assert.DoesNotContain(_statements, statement => IsUpdate(statement.Sql));
            Assert.False((await Read(foreign.Id)).IsDeleted);
            Assert.Equal("old-stamp", (await Read(foreign.Id)).ConcurrencyStamp);
        }
        await Assert.ThrowsAsync<NotSupportedException>(() => repository.DeleteRangeAsync(rows));
        await Assert.ThrowsAsync<NotSupportedException>(() => repository.DeleteRangeAsync(row => row.Id > 0));

        Task Delete(string stamp) => byId ? repository.DeleteAsync(target.Id, stamp)
            : repository.DeleteAsync(new StampedRow { Id = target.Id, TenantId = tenant, ConcurrencyStamp = stamp });
        Task<StampedRow> Read(int id) => _client.Queryable<StampedRow>().ClearFilter().Where(row => row.Id == id).SingleAsync();
    }

    private Repository<TEntity> CreateRepository<TEntity>(string? tenant) where TEntity : class, IEntity<int>, new()
    {
        var current = new Mock<ICurrentTenant>();
        current.SetupGet(value => value.Id).Returns(tenant);
        return new Repository<TEntity>(_client, current.Object);
    }

    private void AssertUpdate(string? tenant, bool stamped)
    {
        var update = Assert.Single(_statements.Where(statement => IsUpdate(statement.Sql)));
        var whereIndex = update.Sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        Assert.True(whereIndex >= 0, update.Sql);
        var where = update.Sql[whereIndex..];
        Assert.Contains("IsDeleted", where, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@liveRow", where, StringComparison.OrdinalIgnoreCase);
        Assert.False(Assert.IsType<bool>(update.Parameters["liveRow"]));
        Assert.Contains("TenantId", where, StringComparison.OrdinalIgnoreCase);
        if (tenant is null) Assert.Contains("IS NULL", where, StringComparison.OrdinalIgnoreCase);
        else
        {
            Assert.Equal(tenant, update.Parameters["tenantId"]);
            Assert.DoesNotContain("IS NULL", where, StringComparison.OrdinalIgnoreCase);
        }
        if (stamped)
        {
            Assert.Contains("ConcurrencyStamp", where, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("@entityStamp", where, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(update.Parameters["entityStamp"], new object[] { "old-stamp", "stale" });
        }
    }

    private static bool IsUpdate(string sql) => sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase);
    private static SoftRow Row(int id, string? tenant, bool deleted = false) => new()
    {
        Id = id, TenantId = tenant, IsDeleted = deleted, DeletionTime = deleted ? DeletedAt : null
    };
    private static StampedRow Stamped(int id, string? tenant, bool deleted = false) => new()
    {
        Id = id, TenantId = tenant, IsDeleted = deleted, DeletionTime = deleted ? DeletedAt : null,
        ConcurrencyStamp = "old-stamp"
    };
    private sealed record Statement(string Sql, Dictionary<string, object> Parameters);
    private sealed class Repository<TEntity>(ISqlSugarClient client, ICurrentTenant tenant)
        : SqlSugarRepository<TEntity, int>(client, currentTenant: tenant)
        where TEntity : class, IEntity<int>, new()
    {
        public override System.Linq.IQueryable<TEntity> GetQueryableUnfiltered()
            => throw new NotSupportedException();
    }

    [SugarTable("sqlsugar_soft_row")]
    public class SoftRow : IEntity<int>, IMultiTenant, ISoftDelete
    {
        [SugarColumn(IsPrimaryKey = true)]
        public int Id { get; set; }
        [SugarColumn(IsNullable = true)]
        public string? TenantId { get; set; }
        public bool IsDeleted { get; set; }
        [SugarColumn(IsNullable = true)]
        public DateTime? DeletionTime { get; set; }
        [SugarColumn(IsNullable = true)]
        public Guid? DeleterId { get; set; }
    }

    [SugarTable("sqlsugar_stamped_row")]
    public sealed class StampedRow : SoftRow, IHasConcurrencyStamp
    {
        public string ConcurrencyStamp { get; set; } = "old-stamp";
    }
}
