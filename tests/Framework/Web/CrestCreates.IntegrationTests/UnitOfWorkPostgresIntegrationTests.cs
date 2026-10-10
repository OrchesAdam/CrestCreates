using System;
using System.Data;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.IntegrationTests;

/// <summary>
/// 切片 3 验收（PostgreSQL / Testcontainers，CI 运行）：外层事务已开始时的嵌套隔离与提交后可见性、
/// 隔离级别矩阵（ReadCommitted/RepeatableRead/Serializable 实测接受）。
/// 本地无 Docker 时由 CI 套件执行。
/// </summary>
public class UnitOfWorkPostgresIntegrationTests : IClassFixture<LibraryManagementWebApplicationFactory>
{
    private readonly LibraryManagementWebApplicationFactory _factory;

    public UnitOfWorkPostgresIntegrationTests(LibraryManagementWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Requires_new_commits_independently_while_outer_transaction_is_active()
    {
        await _factory.EnsureSeedCompleteAsync();

        var innerRowId = Guid.NewGuid();
        var outerRowId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
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

        using var verifyScope = _factory.Services.CreateScope();
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
        await _factory.EnsureSeedCompleteAsync();

        using var scope = _factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        await using var unitOfWorkScope = manager.BeginScope(new UnitOfWorkOptions
        {
            IsolationLevel = isolationLevel
        });

        await unitOfWorkScope.StartAsync();
        await unitOfWorkScope.CompleteAsync();
        unitOfWorkScope.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
    }
}
