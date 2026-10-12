using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace CrestCreates.DistributedTransaction.CAP.IntegrationTests;

/// <summary>
/// #125 S3 故障矩阵（真实 PG + RabbitMQ）：投递以 CAP 存储扫描恢复完成（名义扫描等待 ~30-40s，
/// lookback=30s + interval=2s）；「投递成功」以 cap."published" 行 StatusName=Succeeded（broker ack）为证。
/// </summary>
public class CapAtomicPublishIntegrationTests : IClassFixture<CapIntegrationFixture>
{
    private static readonly TimeSpan ScanWait = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan ImmediateWait = TimeSpan.FromSeconds(30);

    private readonly CapIntegrationFixture _fixture;

    public CapAtomicPublishIntegrationTests(CapIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Managed_commit_writes_probe_and_cap_rows_then_scan_delivers()
    {
        var topic = NewTopic("commit");
        var probeId = Guid.NewGuid();

        using var scope = _fixture.Host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();
        var db = scope.ServiceProvider.GetRequiredService<CapIntegrationDbContext>();

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();

        db.Probes.Add(new CapProbeEntity(probeId, "committed"));
        await publisher.PublishAsync(topic, new { Value = 42 });

        // 提交前：消息行不可见（同一事务内写入），broker 无投递。
        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(topic));

        await unitOfWork.CompleteAsync();

        // 提交后：消息行与业务行同时可见。
        Assert.True(await _fixture.Store.WaitForCountAsync(topic, 1, TimeSpan.FromSeconds(20)), "committed row must be visible");
        using (var verify = _fixture.Host.Services.CreateScope())
        {
            var verifyDb = verify.ServiceProvider.GetRequiredService<CapIntegrationDbContext>();
            Assert.NotNull(await verifyDb.Probes.FindAsync(probeId));
        }

        // 扫描恢复投递：Succeeded 证明 broker ack（名义扫描等待，不按精确区间断言）。
        Assert.True(await _fixture.Store.WaitForStatusAsync(topic, "Succeeded", ScanWait), "scan recovery must deliver the committed message");
    }

    [Fact]
    public async Task Rollback_leaves_no_rows_and_no_dispatch()
    {
        var topic = NewTopic("rollback");
        var probeId = Guid.NewGuid();

        using var scope = _fixture.Host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();
        var db = scope.ServiceProvider.GetRequiredService<CapIntegrationDbContext>();

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();

        db.Probes.Add(new CapProbeEntity(probeId, "rolled-back"));
        await publisher.PublishAsync(topic, new { Value = 1 });
        await unitOfWork.RollbackAsync();

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
    public async Task Consecutive_publishes_in_one_window_are_independent_rows()
    {
        var first = NewTopic("consecutive-a");
        var second = NewTopic("consecutive-b");

        using var scope = _fixture.Host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();
        await publisher.PublishAsync(first, new { Value = 1 });
        await publisher.PublishAsync(second, new { Value = 2 });
        await unitOfWork.CompleteAsync();

        Assert.True(await _fixture.Store.WaitForCountAsync(first, 1, TimeSpan.FromSeconds(20)));
        Assert.True(await _fixture.Store.WaitForCountAsync(second, 1, TimeSpan.FromSeconds(20)));
        Assert.True(await _fixture.Store.WaitForStatusAsync(first, "Succeeded", ScanWait));
        Assert.True(await _fixture.Store.WaitForStatusAsync(second, "Succeeded", ScanWait));
    }

    [Fact]
    public async Task RequiresNew_inner_and_outer_windows_publish_independently()
    {
        var innerTopic = NewTopic("requires-new-inner");
        var outerTopic = NewTopic("requires-new-outer");

        using var scope = _fixture.Host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();

        await using (var outer = manager.BeginScope())
        {
            await outer.StartAsync();

            await using (var inner = manager.BeginScope(new UnitOfWorkOptions { Propagation = UnitOfWorkPropagation.RequiresNew }))
            {
                await inner.StartAsync();
                await publisher.PublishAsync(innerTopic, new { Window = "inner" });
                await inner.CompleteAsync();
            }

            await publisher.PublishAsync(outerTopic, new { Window = "outer" });
            await outer.CompleteAsync();
        }

        Assert.True(await _fixture.Store.WaitForCountAsync(innerTopic, 1, TimeSpan.FromSeconds(20)));
        Assert.True(await _fixture.Store.WaitForCountAsync(outerTopic, 1, TimeSpan.FromSeconds(20)));
        Assert.True(await _fixture.Store.WaitForStatusAsync(innerTopic, "Succeeded", ScanWait));
        Assert.True(await _fixture.Store.WaitForStatusAsync(outerTopic, "Succeeded", ScanWait));
    }

    [Fact]
    public async Task Plain_publish_outside_window_dispatches_immediately()
    {
        var topic = NewTopic("plain");

        using var scope = _fixture.Host.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();

        await publisher.PublishAsync(topic, new { Value = 1 });

        Assert.True(await _fixture.Store.WaitForStatusAsync(topic, "Succeeded", ImmediateWait), "plain CAP publish must dispatch without the managed scan wait");
    }

    [Fact]
    public async Task Delayed_publish_in_window_is_rejected_and_outside_is_allowed()
    {
        var inWindowTopic = NewTopic("delay-in-window");
        var outsideTopic = NewTopic("delay-outside");

        using var scope = _fixture.Host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();

        await using (var unitOfWork = manager.BeginScope())
        {
            await unitOfWork.StartAsync();
            var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
                () => publisher.PublishDelayAsync(TimeSpan.FromMinutes(10), inWindowTopic, new { Value = 1 }));
            Assert.Equal("DelayedPublishInManagedWindow", exception.Code);
            await unitOfWork.RollbackAsync();
        }

        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(inWindowTopic));

        await publisher.PublishDelayAsync(TimeSpan.FromMinutes(10), outsideTopic, new { Value = 2 });
        Assert.True(await _fixture.Store.WaitForCountAsync(outsideTopic, 1, TimeSpan.FromSeconds(20)));
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.NotEqual("Succeeded", await _fixture.Store.GetStatusAsync(outsideTopic));
    }

    [Fact]
    public async Task Unmanaged_native_transaction_is_refused_without_writing()
    {
        var topic = NewTopic("unmanaged-native");

        using var scope = _fixture.Host.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();
        var db = scope.ServiceProvider.GetRequiredService<CapIntegrationDbContext>();

        await using var native = await db.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
            () => publisher.PublishAsync(topic, new { Value = 1 }));

        Assert.Equal("UnmanagedNativeTransaction", exception.Code);
        await native.RollbackAsync();
        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(topic));
    }

    [Fact]
    public async Task Storage_identity_mismatch_is_refused_without_writing()
    {
        var topic = NewTopic("identity-mismatch");

        await using var otherPostgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("cap_uow_other")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await otherPostgres.StartAsync();

        // UoW 指向另一个数据库，CAP 存储仍指向主库：发布必须在写入前确定性拒绝。
        var host = await _fixture.CreateHostAsync(
            otherPostgres.GetConnectionString(),
            _fixture.RabbitMqConnectionString,
            ensureUowSchema: true,
            capStorageConnectionString: _fixture.PostgresConnectionString);
        try
        {
            using var scope = host.Services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();

            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();

            var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
                () => publisher.PublishAsync(topic, new { Value = 1 }));

            Assert.Equal("StorageIdentityMismatch", exception.Code);
            await unitOfWork.RollbackAsync();

            Assert.Equal(0, await _fixture.Store.CountPublishedAsync(topic));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task Host_restart_recovers_committed_scheduled_rows()
    {
        var topic = NewTopic("restart-recovery");
        var isolated = await _fixture.CreateIsolatedDatabaseAsync($"cap_it_restart_{Guid.NewGuid():N}");
        var isolatedStore = new CapStoreInspector(isolated);

        var firstHost = await _fixture.CreateHostAsync(isolated, _fixture.RabbitMqConnectionString);
        using (var scope = firstHost.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();
            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();
            await publisher.PublishAsync(topic, new { Value = 1 });
            await unitOfWork.CompleteAsync();
        }

        Assert.True(await isolatedStore.WaitForCountAsync(topic, 1, TimeSpan.FromSeconds(20)));
        await firstHost.StopAsync();
        firstHost.Dispose();

        // 重启后的宿主通过扫描恢复投递已提交的 Scheduled 行。
        var secondHost = await _fixture.CreateHostAsync(isolated, _fixture.RabbitMqConnectionString);
        try
        {
            Assert.True(await isolatedStore.WaitForStatusAsync(topic, "Succeeded", ScanWait), "a restarted host must recover and deliver committed rows");
        }
        finally
        {
            await secondHost.StopAsync();
            secondHost.Dispose();
        }
    }

    private static string NewTopic(string scenario) => $"cap.it.{scenario}.{Guid.NewGuid():N}";
}
