using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using CrestCreates.Domain.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CrestCreates.DistributedTransaction.CAP.IntegrationTests;

/// <summary>
/// 第二轮复审修复证据（R2-CAP-01/03）：
/// 最终存储覆盖（ConnectionString / DataSource）负例、提交派发阶段 Unknown（未提交与确认丢失两种事实）、
/// broker 提交期离线恢复、拒绝矩阵补全（ResourceMismatch / NoTransactionAssociation）。
/// </summary>
public class CapRound2FixTests : IClassFixture<CapIntegrationFixture>
{
    private static readonly TimeSpan ScanWait = TimeSpan.FromSeconds(150);

    private readonly CapIntegrationFixture _fixture;

    public CapRound2FixTests(CapIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Final_connectionstring_override_is_refused_without_writing()
    {
        await AssertFinalStorageOverrideRefusedAsync(
            storageOverride: (options, connectionString) => options.ConnectionString = connectionString);
    }

    [Fact]
    public async Task Final_datasource_override_is_refused_without_writing()
    {
        // R2-CAP-01：ConnectionString 仍为 A，DataSource 覆盖为 B——CAP 实际优先 DataSource，必须被捕获。
        await AssertFinalStorageOverrideRefusedAsync(
            storageOverride: (options, connectionString) => options.DataSource = NpgsqlDataSource.Create(connectionString));
    }

    private async Task AssertFinalStorageOverrideRefusedAsync(
        Action<DotNetCore.CAP.PostgreSqlOptions, string> storageOverride)
    {
        var topic = NewTopic("final-storage-override");
        var databaseB = await _fixture.CreateIsolatedDatabaseAsync($"cap_it_alt_{Guid.NewGuid():N}");
        var storeB = new CapStoreInspector(databaseB);

        var host = await _fixture.CreateHostAsync(
            _fixture.PostgresConnectionString,
            _fixture.RabbitMqConnectionString,
            capStorageConnectionString: databaseB,
            mutateServices: services => services.PostConfigure<DotNetCore.CAP.PostgreSqlOptions>(
                options => storageOverride(options, databaseB)));
        try
        {
            using var scope = host.Services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();
            var capPublisher = scope.ServiceProvider.GetRequiredService<DotNetCore.CAP.ICapPublisher>();

            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();

            var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
                () => publisher.PublishAsync(topic, new { Value = 1 }));
            Assert.Equal("StorageIdentityMismatch", exception.Code);

            await unitOfWork.RollbackAsync();

            // 写入前拒绝：A（借用库）与 B（最终目标库）均无新增消息行；publisher 上下文无残留。
            Assert.Equal(0, await _fixture.Store.CountPublishedAsync(topic));
            Assert.Equal(0, await storeB.CountPublishedAsync(topic));
            Assert.Null(capPublisher.Transaction);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task Commit_dispatch_failure_records_unknown_without_commit_fact()
    {
        var topic = NewTopic("dispatch-failure");

        using var scope = _fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CapIntegrationDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();

        var controllable = new ControllableEfUnitOfWork(db) { FailCommitDispatch = true };
        var manager = NewManualManager(scope.ServiceProvider, controllable);

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();
        await publisher.PublishAsync(topic, new { Value = 1 });

        // 提交派发阶段故障：提交调用已触达但未产生已提交事实。
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.CompleteAsync());

        Assert.Equal(1, controllable.CommitDispatchAttempts);
        Assert.Equal(UnitOfWorkTransactionOutcome.Unknown, unitOfWork.TransactionOutcome);
        Assert.Equal(UnitOfWorkState.Failed, unitOfWork.State);

        // 未提交 → 无行、无投递（Unknown 不被回滚尝试改写为其他结论）。
        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(topic));
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(topic));
    }

    [Fact]
    public async Task Confirmation_lost_after_commit_recovers_by_database_fact()
    {
        var topic = NewTopic("confirmation-lost");
        var probeId = Guid.NewGuid();

        using var scope = _fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CapIntegrationDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();

        var controllable = new ControllableEfUnitOfWork(db) { CommitThenFailConfirmation = true };
        var manager = NewManualManager(scope.ServiceProvider, controllable);

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();
        db.Probes.Add(new CapProbeEntity(probeId, "confirmation-lost"));
        await publisher.PublishAsync(topic, new { Value = 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.CompleteAsync());

        Assert.Equal(1, controllable.CommitDispatchAttempts);
        Assert.Equal(UnitOfWorkTransactionOutcome.Unknown, unitOfWork.TransactionOutcome);
        Assert.Equal(UnitOfWorkState.Failed, unitOfWork.State);

        // 数据库事实：消息行与业务行同时已提交 → 行存在 ⇔ 提交；扫描按事实恢复投递。
        Assert.True(await _fixture.Store.WaitForCountAsync(topic, 1, TimeSpan.FromSeconds(20)));
        using (var verify = _fixture.Host.Services.CreateScope())
        {
            var verifyDb = verify.ServiceProvider.GetRequiredService<CapIntegrationDbContext>();
            Assert.NotNull(await verifyDb.Probes.FindAsync(probeId));
        }

        Assert.True(await _fixture.Store.WaitForStatusAsync(topic, "Succeeded", ScanWait),
            "a committed-but-unconfirmed message must still be delivered by scan recovery");
    }

    [Fact]
    public async Task Broker_offline_during_commit_recovers_after_restart()
    {
        var topic = NewTopic("broker-offline-during-commit");

        await _fixture.PauseBrokerAsync();
        try
        {
            using var scope = _fixture.Host.Services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();
            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();
            await publisher.PublishAsync(topic, new { Value = 1 });
            await unitOfWork.CompleteAsync();

            // broker 离线期间的提交仍应落库（投递失败可见、重试计数递增、未耗尽）。
            Assert.True(await _fixture.Store.WaitForCountAsync(topic, 1, TimeSpan.FromSeconds(20)));
            var failedVisible = await WaitForFailedStatusAsync(topic, TimeSpan.FromSeconds(90));
            Assert.True(failedVisible, "with the broker offline, delivery attempts must fail visibly");
        }
        finally
        {
            await _fixture.ResumeBrokerAsync();
        }

        Assert.True(await _fixture.Store.WaitForStatusAsync(topic, "Succeeded", ScanWait),
            "after the broker restarts, the committed message must be delivered");
    }

    [Fact]
    public async Task Tenant_mismatch_refuses_participation_without_writing()
    {
        var topic = NewTopic("resource-mismatch");
        var tenant = _fixture.Host.Services.GetRequiredService<MutableCurrentTenant>();

        try
        {
            using var scope = _fixture.Host.Services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();

            tenant.Id = "tenant-a"; // 窗口开启时的租户快照
            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();

            tenant.Id = "tenant-b"; // 窗口内切换租户：参与判定必须拒绝

            var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
                () => publisher.PublishAsync(topic, new { Value = 1 }));
            Assert.Contains("ResourceMismatch", exception.Message);

            await unitOfWork.RollbackAsync();
            Assert.Equal(0, await _fixture.Store.CountPublishedAsync(topic));
        }
        finally
        {
            tenant.Id = null;
        }
    }

    [Fact]
    public async Task Owner_without_transaction_association_refuses_without_writing()
    {
        var topic = NewTopic("no-association");

        using var scope = _fixture.Host.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();
        var manager = NewManualManager(scope.ServiceProvider, new PlainFakeUnitOfWork());

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();

        var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
            () => publisher.PublishAsync(topic, new { Value = 1 }));
        Assert.Contains("NoTransactionAssociation", exception.Message);

        await unitOfWork.RollbackAsync();
        Assert.Equal(0, await _fixture.Store.CountPublishedAsync(topic));
    }

    private static UnitOfWorkManager NewManualManager(IServiceProvider scopedProvider, IUnitOfWork unitOfWork)
        => new(
            new SingleFactory(unitOfWork),
            scopedProvider.GetRequiredService<UnitOfWorkProviderBindingRegistry>(),
            scopedProvider.GetRequiredService<IServiceScopeFactory>(),
            explicitDefault: OrmProvider.EfCore,
            chainNode: scopedProvider.GetRequiredService<UnitOfWorkChainNode>());

    private async Task<bool> WaitForFailedStatusAsync(string topic, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await _fixture.Store.GetStatusAsync(topic) == "Failed")
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

    /// <summary>无受管资源曝光的伪工作单元（NoTransactionAssociation 拒绝用例）。</summary>
    private sealed class PlainFakeUnitOfWork : IUnitOfWork
    {
        public Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public void Dispose()
        {
        }
    }
}
