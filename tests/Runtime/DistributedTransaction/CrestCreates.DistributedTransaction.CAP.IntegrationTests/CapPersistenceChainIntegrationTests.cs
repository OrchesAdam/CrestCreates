using System;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.DistributedTransaction.CAP.IntegrationTests;

/// <summary>
/// #125 S2 真实 PG 持久化链（R1-CAP-01/02 修复证据）：
/// 后台补偿经唯一内核建立 UoW 边界后状态跨 scope 持久化；日志链首次写入/连续更新/提交后更新可用。
/// 专用宿主 CompensationRetryIntervalSeconds=1 驱动真实后台服务路径。
/// </summary>
public class CapPersistenceChainIntegrationTests : IClassFixture<CapIntegrationFixture>, IAsyncLifetime
{
    private static readonly TimeSpan PersistWait = TimeSpan.FromSeconds(60);

    private readonly CapIntegrationFixture _fixture;
    private IHost _host = null!;

    public CapPersistenceChainIntegrationTests(CapIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await _fixture.CreateHostAsync(
            _fixture.PostgresConnectionString,
            _fixture.RabbitMqConnectionString,
            compensationRetryIntervalSeconds: 1);
        PgTestCompensationExecutor.Fail = false;
    }

    public async Task DisposeAsync()
    {
        PgTestCompensationExecutor.Fail = false;
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Register_compensation_persists_across_scopes()
    {
        var transactionId = Guid.NewGuid();

        using (var scope = _host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var compensator = scope.ServiceProvider.GetRequiredService<ITransactionCompensator>();
            await manager.ExecuteAsync<object?>(async _ =>
            {
                await compensator.RegisterCompensationAsync(transactionId, PgTestCompensationExecutor.ExecutorName, new { OrderId = 1 });
                return null;
            });
        }

        using (var verify = _host.Services.CreateScope())
        {
            var repository = verify.ServiceProvider.GetRequiredService<IRepository<TransactionCompensation, Guid>>();
            var row = await repository.FirstOrDefaultAsync(x => x.TransactionId == transactionId);
            Assert.NotNull(row);
            Assert.Equal(CompensationStatus.Pending, row!.Status);
        }
    }

    [Fact]
    public async Task Manual_cycle_failure_persists_retrying_then_background_completes()
    {
        var transactionId = Guid.NewGuid();
        await RegisterAsync(transactionId);

        // 手动周期（与业务调用同路径）：执行失败 → Retrying 持久化（跨 scope 可见）。
        PgTestCompensationExecutor.Fail = true;
        using (var scope = _host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var compensator = scope.ServiceProvider.GetRequiredService<ITransactionCompensator>();
            await manager.ExecuteAsync<object?>(async _ =>
            {
                await compensator.CompensateAsync(transactionId);
                return null;
            });
        }

        var retrying = await WaitForRowAsync(transactionId, row => row.Status == CompensationStatus.Retrying, PersistWait);
        Assert.True(retrying, "a failed compensation must persist Retrying across scopes");
        Assert.Equal(1, (await GetRowAsync(transactionId))!.RetryCount);

        // 真实后台服务路径（scoped + UoW 边界）接手：成功后 Completed 持久化。
        PgTestCompensationExecutor.Fail = false;
        var completed = await WaitForRowAsync(transactionId, row => row.Status == CompensationStatus.Completed, PersistWait);
        Assert.True(completed, "the background cycle must persist the successful completion");
    }

    [Fact]
    public async Task Max_retries_failure_persists_failed_not_completed()
    {
        var transactionId = Guid.NewGuid();
        await SeedPendingRowAsync(transactionId, PgTestCompensationExecutor.ExecutorName, maxRetries: 1);

        PgTestCompensationExecutor.Fail = true;
        using (var scope = _host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var compensator = scope.ServiceProvider.GetRequiredService<ITransactionCompensator>();
            await manager.ExecuteAsync<object?>(async _ =>
            {
                await compensator.CompensateAsync(transactionId);
                return null;
            });
        }

        var failed = await WaitForRowAsync(transactionId, row => row.Status == CompensationStatus.Failed, PersistWait);
        Assert.True(failed, "exhausted retries must persist Failed");
        var row = (await GetRowAsync(transactionId))!;
        Assert.Contains("pg-executor-failure", row.ErrorMessage);
        Assert.True(row.RetryCount >= 1);
    }

    [Fact]
    public async Task Historic_row_with_removed_executor_persists_failed()
    {
        var transactionId = Guid.NewGuid();
        await SeedPendingRowAsync(transactionId, "removed-executor", maxRetries: 3);

        using (var scope = _host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var compensator = scope.ServiceProvider.GetRequiredService<ITransactionCompensator>();
            await manager.ExecuteAsync<object?>(async _ =>
            {
                await compensator.CompensateAsync(transactionId);
                return null;
            });
        }

        var failed = await WaitForRowAsync(transactionId, row => row.Status == CompensationStatus.Failed, PersistWait);
        Assert.True(failed);
        Assert.Contains("No executor found", (await GetRowAsync(transactionId))!.ErrorMessage);
    }

    [Fact]
    public async Task Logger_first_write_persists_and_business_uow_completes()
    {
        var transactionId = Guid.NewGuid();

        using (var scope = _host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var logger = scope.ServiceProvider.GetRequiredService<ITransactionLogger>();
            await manager.ExecuteAsync<object?>(async _ =>
            {
                await logger.LogTransactionAsync(transactionId, TransactionStatus.Started, "started");
                return null;
            });
        }

        var row = await GetLogAsync(transactionId);
        Assert.NotNull(row);
        Assert.Equal(TransactionStatus.Started, row!.Status);
    }

    [Fact]
    public async Task Logger_consecutive_updates_in_same_uow_share_identity()
    {
        var transactionId = Guid.NewGuid();
        await SeedLogAsync(transactionId);

        using (var scope = _host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var logger = scope.ServiceProvider.GetRequiredService<ITransactionLogger>();
            await manager.ExecuteAsync<object?>(async _ =>
            {
                await logger.LogTransactionAsync(transactionId, TransactionStatus.Started, "started");
                await logger.LogParticipantCountAsync(transactionId, 2);
                return null;
            });
        }

        var row = await GetLogAsync(transactionId);
        Assert.NotNull(row);
        Assert.Equal(TransactionStatus.Started, row!.Status);
        Assert.Equal(2, row.ParticipantCount);
    }

    [Fact]
    public async Task Logger_update_after_commit_in_new_uow_persists()
    {
        var transactionId = Guid.NewGuid();
        await SeedLogAsync(transactionId);

        using (var scope = _host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var logger = scope.ServiceProvider.GetRequiredService<ITransactionLogger>();
            await manager.ExecuteAsync<object?>(async _ =>
            {
                await logger.LogTransactionErrorAsync(transactionId, new InvalidOperationException("post-commit-boom"));
                return null;
            });
        }

        var row = await GetLogAsync(transactionId);
        Assert.NotNull(row);
        Assert.Contains("post-commit-boom", row!.ErrorDetails);
    }

    [Fact]
    public async Task Compensating_twice_in_same_uow_executes_once_and_stays_completed()
    {
        var transactionId = Guid.NewGuid();
        await RegisterAsync(transactionId);

        using (var scope = _host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var compensator = scope.ServiceProvider.GetRequiredService<ITransactionCompensator>();
            var executor = scope.ServiceProvider.GetRequiredService<PgTestCompensationExecutor>();

            await manager.ExecuteAsync<object?>(async _ =>
            {
                await compensator.CompensateAsync(transactionId);
                await compensator.CompensateAsync(transactionId); // 同一 UoW 第二次：不得重复执行
                return null;
            });

            Assert.Equal(1, executor.Invocations.Count);
        }

        var row = await GetRowAsync(transactionId);
        Assert.NotNull(row);
        Assert.Equal(CompensationStatus.Completed, row!.Status);

        using (var verify = _host.Services.CreateScope())
        {
            var repository = verify.ServiceProvider.GetRequiredService<IRepository<TransactionCompensation, Guid>>();
            var pending = await repository.FindAsync(x =>
                x.TransactionId == transactionId &&
                (x.Status == CompensationStatus.Pending || x.Status == CompensationStatus.Retrying));
            Assert.Empty(pending);
        }
    }

    [Fact]
    public async Task Deleted_pending_row_is_not_compensated_and_is_gone_after_commit()
    {
        var transactionId = Guid.NewGuid();
        await RegisterAsync(transactionId);

        using (var scope = _host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var compensator = scope.ServiceProvider.GetRequiredService<ITransactionCompensator>();
            var executor = scope.ServiceProvider.GetRequiredService<PgTestCompensationExecutor>();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<TransactionCompensation, Guid>>();

            await manager.ExecuteAsync<object?>(async _ =>
            {
                var row = await repository.FirstOrDefaultAsync(x => x.TransactionId == transactionId);
                Assert.NotNull(row);
                await repository.DeleteAsync(row!);

                // 待删除（同 UoW 未 flush）记录不得被重新查询带回执行。
                await compensator.CompensateAsync(transactionId);
                return null;
            });

            Assert.Empty(executor.Invocations);
        }

        // 提交后行确实不存在。
        Assert.Null(await GetRowAsync(transactionId));
    }

    [Fact]
    public async Task Count_view_matches_find_view_after_local_mutations()
    {
        var transactionId = Guid.NewGuid();
        await RegisterAsync(transactionId);

        using (var scope = _host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var compensator = scope.ServiceProvider.GetRequiredService<ITransactionCompensator>();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<TransactionCompensation, Guid>>();

            await manager.ExecuteAsync<object?>(async _ =>
            {
                // Pending → Completed（本地跟踪变更，未 flush）
                await compensator.CompensateAsync(transactionId);

                var completed = await repository.FindAsync(x =>
                    x.TransactionId == transactionId && x.Status == CompensationStatus.Completed);
                Assert.Single(completed);
                Assert.Equal(1, await repository.CountAsync(x =>
                    x.TransactionId == transactionId && x.Status == CompensationStatus.Completed));
                Assert.Empty(await repository.FindAsync(x =>
                    x.TransactionId == transactionId &&
                    (x.Status == CompensationStatus.Pending || x.Status == CompensationStatus.Retrying)));
                Assert.Equal(0, await repository.CountAsync(x =>
                    x.TransactionId == transactionId &&
                    (x.Status == CompensationStatus.Pending || x.Status == CompensationStatus.Retrying)));

                // 反向状态变更：本地改回 Retrying → Completed 视图归零、Retrying 视图为一。
                var row = await repository.FirstOrDefaultAsync(x => x.TransactionId == transactionId);
                Assert.NotNull(row);
                row!.Status = CompensationStatus.Retrying;

                Assert.Equal(0, await repository.CountAsync(x =>
                    x.TransactionId == transactionId && x.Status == CompensationStatus.Completed));
                Assert.Empty(await repository.FindAsync(x =>
                    x.TransactionId == transactionId && x.Status == CompensationStatus.Completed));
                Assert.Equal(1, await repository.CountAsync(x =>
                    x.TransactionId == transactionId && x.Status == CompensationStatus.Retrying));
                Assert.Single(await repository.FindAsync(x =>
                    x.TransactionId == transactionId && x.Status == CompensationStatus.Retrying));
                return null;
            });
        }
    }

    // ── 辅助 ──

    private async Task RegisterAsync(Guid transactionId)
    {
        using var scope = _host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var compensator = scope.ServiceProvider.GetRequiredService<ITransactionCompensator>();
        await manager.ExecuteAsync<object?>(async _ =>
        {
            await compensator.RegisterCompensationAsync(transactionId, PgTestCompensationExecutor.ExecutorName, null);
            return null;
        });
    }

    private async Task SeedPendingRowAsync(Guid transactionId, string participantName, int maxRetries)
    {
        using var scope = _host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TransactionCompensation, Guid>>();
        await manager.ExecuteAsync<object?>(async _ =>
        {
            await repository.AddAsync(new TransactionCompensation
            {
                Id = Guid.NewGuid(),
                TransactionId = transactionId,
                ParticipantName = participantName,
                Status = CompensationStatus.Pending,
                MaxRetries = maxRetries,
                CreatedAt = DateTime.UtcNow
            });
            return null;
        });
    }

    private async Task SeedLogAsync(Guid transactionId)
    {
        using var scope = _host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TransactionLog, Guid>>();
        await manager.ExecuteAsync<object?>(async _ =>
        {
            await repository.AddAsync(new TransactionLog
            {
                Id = transactionId,
                Status = TransactionStatus.Started,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return null;
        });
    }

    private async Task<TransactionCompensation?> GetRowAsync(Guid transactionId)
    {
        using var scope = _host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TransactionCompensation, Guid>>();
        return await repository.FirstOrDefaultAsync(x => x.TransactionId == transactionId);
    }

    private async Task<TransactionLog?> GetLogAsync(Guid transactionId)
    {
        using var scope = _host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TransactionLog, Guid>>();
        return await repository.GetByIdAsync(transactionId);
    }

    private async Task<bool> WaitForRowAsync(
        Guid transactionId,
        Func<TransactionCompensation, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await GetRowAsync(transactionId);
            if (row is not null && predicate(row))
            {
                return true;
            }

            await Task.Delay(500);
        }

        return false;
    }
}
