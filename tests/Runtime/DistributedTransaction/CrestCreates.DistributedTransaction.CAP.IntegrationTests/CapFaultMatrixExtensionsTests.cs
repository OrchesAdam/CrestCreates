using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.UnitOfWork;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using CrestCreates.Domain.UnitOfWork;
using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.DistributedTransaction.CAP.IntegrationTests;

/// <summary>
/// #125 故障矩阵补齐：Unknown 提交故障、确认提交后清理失败、放弃、窗口外状态恢复、拒绝矩阵全量、
/// 最低配置首次投递、broker 离线恢复。真实 PG + RabbitMQ（扫描等待 lookback=30s + interval=2s）。
/// </summary>
public class CapFaultMatrixExtensionsTests : IClassFixture<CapIntegrationFixture>
{
    private static readonly TimeSpan ScanWait = TimeSpan.FromSeconds(150);

    private readonly CapIntegrationFixture _fixture;

    public CapFaultMatrixExtensionsTests(CapIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Flush_stage_failure_after_publish_records_unknown_without_rows()
    {
        // 停库使 flush 先于 commit 失败（本用例覆盖的是 flush+终结阶段失败；
        // 真正进入提交派发的 Unknown 证据见 CapRound2FixTests.Commit_dispatch_failure_* 与
        // Confirmation_lost_after_commit_recovers_by_database_fact）。
        var topic = NewTopic("flush-stage-unknown");
        var probeId = Guid.NewGuid();

        using var scope = _fixture.Host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();
        var db = scope.ServiceProvider.GetRequiredService<CapIntegrationDbContext>();

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();
        db.Probes.Add(new CapProbeEntity(probeId, "unknown"));
        await publisher.PublishAsync(topic, new { Value = 1 });

        // 提交派发期故障：在 Complete 前停库 → 事务无法确认提交。
        await _fixture.PauseDatabaseAsync();
        Exception? observed = null;
        try
        {
            await unitOfWork.CompleteAsync();
        }
        catch (Exception exception)
        {
            observed = exception;
        }
        finally
        {
            await _fixture.ResumeDatabaseAsync();
        }

        Assert.NotNull(observed);
        Assert.Equal(UnitOfWorkTransactionOutcome.Unknown, unitOfWork.TransactionOutcome);
        Assert.Equal(UnitOfWorkState.Failed, unitOfWork.State);

        // 行存在 ⇔ 事务已提交：事务未提交 → 无行、无投递（不宣称任何结果）。
        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(topic));
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(topic));
    }

    [Fact]
    public async Task Confirmed_commit_with_cleanup_failure_still_delivers()
    {
        var topic = NewTopic("cleanup-failure");

        using var scope = _fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CapIntegrationDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();

        // 手动内核以 DI 形状构造（挂到 scoped 链节点）：DI 读方（publisher/borrower）可以看到本内核的帧。
        var controllable = new ControllableEfUnitOfWork(db) { FailCleanupAfterCommit = true };
        var manager = new UnitOfWorkManager(
            new SingleFactory(controllable),
            scope.ServiceProvider.GetRequiredService<UnitOfWorkProviderBindingRegistry>(),
            scopeFactory: scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            explicitDefault: OrmProvider.EfCore,
            chainNode: scope.ServiceProvider.GetRequiredService<UnitOfWorkChainNode>());

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();
        await publisher.PublishAsync(topic, new { Value = 1 });

        // 提交已确认，句柄清理失败：Committed 固定、State=Failed（#137 语义），消息行已随事务提交。
        await Assert.ThrowsAsync<UnitOfWorkCommitCleanupFailureException>(() => unitOfWork.CompleteAsync());
        Assert.Equal(UnitOfWorkTransactionOutcome.Committed, unitOfWork.TransactionOutcome);
        Assert.Equal(UnitOfWorkState.Failed, unitOfWork.State);

        Assert.True(await _fixture.Store.WaitForCountAsync(topic, 1, TimeSpan.FromSeconds(20)));
        Assert.True(await _fixture.Store.WaitForStatusAsync(topic, "Succeeded", ScanWait),
            "a confirmed commit must still be delivered by scan recovery");
    }

    [Fact]
    public async Task Abandoned_window_rolls_back_and_never_dispatches()
    {
        var topic = NewTopic("abandon");
        var probeId = Guid.NewGuid();

        using (var scope = _fixture.Host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();
            var db = scope.ServiceProvider.GetRequiredService<CapIntegrationDbContext>();

            await using (var unitOfWork = manager.BeginScope())
            {
                await unitOfWork.StartAsync();
                db.Probes.Add(new CapProbeEntity(probeId, "abandoned"));
                await publisher.PublishAsync(topic, new { Value = 1 });
                // 未完成退出（放弃）：内核终结事务并丢弃未 flush 写入。
            }
        }

        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(topic));
        using (var verify = _fixture.Host.Services.CreateScope())
        {
            var verifyDb = verify.ServiceProvider.GetRequiredService<CapIntegrationDbContext>();
            Assert.Null(await verifyDb.Probes.FindAsync(probeId));
        }

        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(topic));
    }

    [Fact]
    public async Task Publisher_context_restores_after_window_and_plain_publish_works()
    {
        var managedTopic = NewTopic("restore-managed");
        var plainTopic = NewTopic("restore-plain");

        using var scope = _fixture.Host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();
        var capPublisher = scope.ServiceProvider.GetRequiredService<ICapPublisher>();

        await using (var unitOfWork = manager.BeginScope())
        {
            await unitOfWork.StartAsync();
            await publisher.PublishAsync(managedTopic, new { Value = 1 });
            await unitOfWork.CompleteAsync();
        }

        // 窗口退出后同一个 publisher 上下文已恢复（无残留事务绑定）。
        Assert.Null(capPublisher.Transaction);

        await publisher.PublishAsync(plainTopic, new { Value = 2 });
        Assert.True(await _fixture.Store.WaitForStatusAsync(managedTopic, "Succeeded", ScanWait));
        Assert.True(await _fixture.Store.WaitForStatusAsync(plainTopic, "Succeeded", TimeSpan.FromSeconds(30)),
            "the same publisher must fall back to plain CAP publish after the window exits");
    }

    [Fact]
    public async Task Refusal_matrix_full_negative_without_any_row()
    {
        var notStarted = NewTopic("refuse-not-started");
        var nonTransactional = NewTopic("refuse-non-transactional");
        var completed = NewTopic("refuse-completed");

        using var scope = _fixture.Host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();

        await using (var unitOfWork = manager.BeginScope()) // 未 StartAsync
        {
            var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
                () => publisher.PublishAsync(notStarted, new { Value = 1 }));
            Assert.Contains("NotStarted", exception.Message);
        }

        await using (var unitOfWork = manager.BeginScope(new UnitOfWorkOptions { IsTransactional = false }))
        {
            await unitOfWork.StartAsync();
            var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
                () => publisher.PublishAsync(nonTransactional, new { Value = 1 }));
            Assert.Contains("NonTransactional", exception.Message);
        }

        await using (var unitOfWork = manager.BeginScope())
        {
            await unitOfWork.StartAsync();
            await unitOfWork.CompleteAsync();
            var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
                () => publisher.PublishAsync(completed, new { Value = 1 }));
            Assert.Contains("Terminal", exception.Message);
        }

        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(notStarted));
        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(nonTransactional));
        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(completed));
    }

    [Fact]
    public async Task Minimum_allowed_retry_config_still_delivers_first_message()
    {
        var topic = NewTopic("minimum-config");
        var host = await _fixture.CreateHostAsync(
            _fixture.PostgresConnectionString,
            _fixture.RabbitMqConnectionString,
            failedRetryCount: 1);
        try
        {
            using var scope = host.Services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();
            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();
            await publisher.PublishAsync(topic, new { Value = 1 });
            await unitOfWork.CompleteAsync();

            Assert.True(await _fixture.Store.WaitForStatusAsync(topic, "Succeeded", ScanWait),
                "FailedRetryCount=1 (minimum allowed) must still deliver the first transactional message");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task Broker_offline_after_commit_recovers_after_restart()
    {
        var topic = NewTopic("broker-offline");

        using var scope = _fixture.Host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();
        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();
        await publisher.PublishAsync(topic, new { Value = 1 });
        await unitOfWork.CompleteAsync();

        Assert.True(await _fixture.Store.WaitForCountAsync(topic, 1, TimeSpan.FromSeconds(20)));

        await _fixture.PauseBrokerAsync();
        try
        {
            // 扫描尝试投递失败 → 状态进入 Failed（重试计数递增，未耗尽）。
            var failed = await WaitForStatusOrRetriesAsync(topic, TimeSpan.FromSeconds(90));
            Assert.True(failed, "with the broker down, scan delivery attempts must fail visibly");
        }
        finally
        {
            await _fixture.ResumeBrokerAsync();
        }

        Assert.True(await _fixture.Store.WaitForStatusAsync(topic, "Succeeded", ScanWait),
            "after the broker restarts, the retry budget must deliver the committed message");
    }

    private async Task<bool> WaitForStatusOrRetriesAsync(string topic, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = await _fixture.Store.GetStatusAsync(topic);
            if (status == "Failed")
            {
                return true;
            }

            await Task.Delay(1000);
        }

        return false;
    }

    private static string NewTopic(string scenario) => $"cap.it.{scenario}.{Guid.NewGuid():N}";

    private sealed class SingleFactory : IUnitOfWorkFactory
    {
        private readonly IUnitOfWork _unitOfWork;

        public SingleFactory(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        public IUnitOfWork Create(OrmProvider provider) => _unitOfWork;
    }
}
