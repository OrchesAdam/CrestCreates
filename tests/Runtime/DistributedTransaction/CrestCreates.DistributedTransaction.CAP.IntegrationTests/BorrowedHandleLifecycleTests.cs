using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DistributedTransaction.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.DistributedTransaction.CAP.IntegrationTests;

/// <summary>
/// R2-CAP-03 残项：经**正式绑定发布路径**统计借用句柄的生命周期调用次数
/// （替换 IRelationalTransactionFactory 装饰受管事务：Commit=1、Rollback=0、Dispose=1——
/// 证明 CAP 适配未额外驱动生命周期）。计数按事务实例每操作恰一次（同步/异步路径合并计一次）。
/// </summary>
public class BorrowedHandleLifecycleTests : IClassFixture<CapIntegrationFixture>
{
    private readonly CapIntegrationFixture _fixture;

    public BorrowedHandleLifecycleTests(CapIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Borrowed_handle_lifecycle_counts_are_commit1_rollback0_dispose1()
    {
        var topic = $"cap.it.lifecycle.{Guid.NewGuid():N}";
        var counters = new TransactionLifecycleCounters();
        CountingRelationalTransaction.Counters = counters;

        var host = await _fixture.CreateHostAsync(
            _fixture.PostgresConnectionString,
            _fixture.RabbitMqConnectionString,
            mutateServices: services => services.AddDbContext<CapIntegrationDbContext>((_, options) =>
                options.ReplaceService<IRelationalTransactionFactory, CountingRelationalTransactionFactory>()));
        try
        {
            // 后台补偿服务在宿主启动时立即执行首个周期（其 UoW 亦经受装饰的工厂提交）：
            // 等待其完成并重置计数，只统计本用例受管窗口的事务生命周期。
            await Task.Delay(TimeSpan.FromSeconds(2));
            counters.Reset();

            using var scope = host.Services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var publisher = scope.ServiceProvider.GetRequiredService<IDistributedEventPublisher>();

            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();
            await publisher.PublishAsync(topic, new { Value = 1 });
            await unitOfWork.CompleteAsync();

            Assert.Equal(1, counters.Committed);
            Assert.Equal(0, counters.RolledBack);
            Assert.Equal(1, counters.Disposed);
            Assert.True(await _fixture.Store.WaitForStatusAsync(topic, "Succeeded", TimeSpan.FromSeconds(150)));
        }
        finally
        {
            CountingRelationalTransaction.Counters = null;
            await host.StopAsync();
            host.Dispose();
        }
    }

    public sealed class TransactionLifecycleCounters
    {
        public int Committed { get; set; }

        public int RolledBack { get; set; }

        public int Disposed { get; set; }

        public void Reset()
        {
            Committed = 0;
            RolledBack = 0;
            Disposed = 0;
        }
    }

    private sealed class CountingRelationalTransactionFactory : IRelationalTransactionFactory
    {
        private readonly ISqlGenerationHelper _sqlGenerationHelper;

        public CountingRelationalTransactionFactory(ISqlGenerationHelper sqlGenerationHelper)
        {
            _sqlGenerationHelper = sqlGenerationHelper;
        }

        public RelationalTransaction Create(
            IRelationalConnection connection,
            DbTransaction transaction,
            Guid transactionId,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
            bool transactionOwned)
            => new CountingRelationalTransaction(
                connection, transaction, transactionId, logger, transactionOwned, _sqlGenerationHelper);

    }

    private sealed class CountingRelationalTransaction : RelationalTransaction
    {
        private bool _commitCounted;
        private bool _rollbackCounted;
        private bool _disposeCounted;

        public CountingRelationalTransaction(
            IRelationalConnection connection,
            DbTransaction transaction,
            Guid transactionId,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
            bool transactionOwned,
            ISqlGenerationHelper sqlGenerationHelper)
            : base(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
        {
        }

        public static TransactionLifecycleCounters? Counters { get; set; }

        public override void Commit()
        {
            CountCommitOnce();
            base.Commit();
        }

        public override async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CountCommitOnce();
            await base.CommitAsync(cancellationToken);
        }

        public override void Rollback()
        {
            CountRollbackOnce();
            base.Rollback();
        }

        public override async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            CountRollbackOnce();
            await base.RollbackAsync(cancellationToken);
        }

        public override void Dispose()
        {
            CountDisposeOnce();
            base.Dispose();
        }

        public override ValueTask DisposeAsync()
        {
            CountDisposeOnce();
            return base.DisposeAsync();
        }

        private void CountCommitOnce()
        {
            if (!_commitCounted && Counters is not null)
            {
                _commitCounted = true;
                Counters.Committed++;
            }
        }

        private void CountRollbackOnce()
        {
            if (!_rollbackCounted && Counters is not null)
            {
                _rollbackCounted = true;
                Counters.RolledBack++;
            }
        }

        private void CountDisposeOnce()
        {
            if (!_disposeCounted && Counters is not null)
            {
                _disposeCounted = true;
                Counters.Disposed++;
            }
        }
    }
}
