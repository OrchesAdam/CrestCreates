using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CrestCreates.IntegrationTests;

/// <summary>
/// 切片 3/4 验收（自包含 PostgreSQL + Testcontainers，CI 运行）：
/// 外层事务已开始时的嵌套隔离与提交后可见性、隔离级别实测接受。
/// 不依赖 legacy LibraryManagement fixture 的后启动 seeding（其 bootstrap 问题见 I-18）。
/// </summary>
public sealed class UnitOfWorkPostgresIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("crest_uow_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly string _schemaName = $"uow_{Guid.NewGuid():N}";
    private ServiceProvider _provider = null!;

    private string ConnectionString => $"{_postgres.GetConnectionString()};Search Path={_schemaName}";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{_schemaName}";""";
            await command.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<UowPgDbContext>(options => options.UseNpgsql(ConnectionString));
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp =>
            new EfCoreDbContextAdapter(sp.GetRequiredService<UowPgDbContext>()));
        services.AddScoped<IDataBaseContext>(sp => sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Requires_new_commits_independently_while_outer_transaction_is_active()
    {
        var innerRowId = Guid.NewGuid();
        var outerRowId = Guid.NewGuid();

        using (var scope = _provider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var preInjectedContext = scope.ServiceProvider.GetRequiredService<IEntityFrameworkCoreDbContext>();

            await preInjectedContext.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS uow_pg_probe (id uuid PRIMARY KEY, note text NOT NULL)");
            await preInjectedContext.ExecuteSqlRawAsync("DELETE FROM uow_pg_probe");

            // 外层事务先开始（PG 支持双连接并发事务；内层必须独立提交）。
            await using var outer = manager.BeginScope();
            await outer.StartAsync();
            await preInjectedContext.ExecuteSqlRawAsync(
                "INSERT INTO uow_pg_probe (id, note) VALUES ({0}, {1})",
                new object[] { outerRowId, "outer" });

            await using (var inner = manager.BeginScope(new UnitOfWorkOptions
            {
                Propagation = UnitOfWorkPropagation.RequiresNew
            }))
            {
                await inner.StartAsync();
                await preInjectedContext.ExecuteSqlRawAsync(
                    "INSERT INTO uow_pg_probe (id, note) VALUES ({0}, {1})",
                    new object[] { innerRowId, "inner" });
                await inner.CompleteAsync();
                inner.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
            }

            await outer.RollbackAsync();
        }

        using var verifyScope = _provider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<IEntityFrameworkCoreDbContext>();

        var innerVisible = await verifyContext.ExecuteSqlRawAsync(
            "UPDATE uow_pg_probe SET note = note WHERE id = {0}",
            new object[] { innerRowId });
        innerVisible.Should().Be(1, "the inner requiresNew commit must be durable after the outer rollback");

        var outerVisible = await verifyContext.ExecuteSqlRawAsync(
            "UPDATE uow_pg_probe SET note = note WHERE id = {0}",
            new object[] { outerRowId });
        outerVisible.Should().Be(0, "the outer rollback must not revoke the inner commit and must discard its own row");
    }

    [Theory]
    [InlineData(IsolationLevel.ReadCommitted)]
    [InlineData(IsolationLevel.RepeatableRead)]
    [InlineData(IsolationLevel.Serializable)]
    public async Task Declared_isolation_levels_are_accepted_on_postgres(IsolationLevel isolationLevel)
    {
        using var scope = _provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        await using var unitOfWorkScope = manager.BeginScope(new UnitOfWorkOptions
        {
            IsolationLevel = isolationLevel
        });

        await unitOfWorkScope.StartAsync();

        // 读取实际事务级别断言（不只判断 Complete 成功）。
        var dbContext = (DbContext)scope.ServiceProvider
            .GetRequiredService<IEntityFrameworkCoreDbContext>().GetNativeContext();
        dbContext.Database.CurrentTransaction.Should().NotBeNull();
        dbContext.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel.Should().Be(
            isolationLevel,
            "the declared isolation level must reach the actual database transaction");

        await unitOfWorkScope.CompleteAsync();
        unitOfWorkScope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
    }

    public sealed class UowPgDbContext : DbContext
    {
        public UowPgDbContext(DbContextOptions<UowPgDbContext> options)
            : base(options)
        {
        }
    }

    private sealed class NoOpDomainEventPublisher : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
            => Task.CompletedTask;
    }
}
