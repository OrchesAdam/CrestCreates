using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Implementations;
using CrestCreates.DistributedTransaction.CAP.Options;
using CrestCreates.DistributedTransaction.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.DistributedTransaction.Tests;

/// <summary>
/// #125 S2（单元层）：补偿注册/执行/重试真实性（registry 消费真实生成产物；真实 EF 仓储链见 PG 集成用例）。
/// </summary>
public class CompensationChainTests
{
    [Fact]
    public async Task Registration_writes_pending_for_known_executor()
    {
        var harness = new Harness();
        var transactionId = Guid.NewGuid();

        await harness.Compensator.RegisterCompensationAsync(transactionId, TestChargeCompensationExecutor.ExecutorName, new { OrderId = 7 });

        var row = Assert.Single(harness.Repository.Items);
        Assert.Equal(transactionId, row.TransactionId);
        Assert.Equal(TestChargeCompensationExecutor.ExecutorName, row.ParticipantName);
        Assert.Equal(CompensationStatus.Pending, row.Status);
        Assert.Equal(3, row.MaxRetries);
        Assert.Contains("7", row.CompensationData);
    }

    [Fact]
    public async Task Registration_rejects_missing_executor_without_writing()
    {
        var harness = new Harness();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Compensator.RegisterCompensationAsync(Guid.NewGuid(), "not-registered", null));

        Assert.Contains("not-registered", exception.Message);
        Assert.Empty(harness.Repository.Items);
    }

    [Fact]
    public async Task Compensation_invokes_executor_and_completes()
    {
        var harness = new Harness();
        var transactionId = Guid.NewGuid();
        await harness.Compensator.RegisterCompensationAsync(transactionId, TestChargeCompensationExecutor.ExecutorName, new { OrderId = 11 });

        await harness.Compensator.CompensateAsync(transactionId);

        var executor = harness.Executor;
        var invocation = Assert.Single(executor.Invocations);
        Assert.Contains("11", invocation);
        var row = Assert.Single(harness.Repository.Items);
        Assert.Equal(CompensationStatus.Completed, row.Status);
        Assert.NotNull(row.ExecutedAt);
    }

    [Fact]
    public async Task Failure_is_recorded_as_failed_at_max_retries_not_completed()
    {
        var harness = new Harness(maxRetries: 1);
        harness.Executor.ThrowOnExecute = true;
        var transactionId = Guid.NewGuid();
        await harness.Compensator.RegisterCompensationAsync(transactionId, TestChargeCompensationExecutor.ExecutorName, null);

        await harness.Compensator.CompensateAsync(transactionId);

        var row = Assert.Single(harness.Repository.Items);
        Assert.Equal(CompensationStatus.Failed, row.Status);
        Assert.Equal(1, row.RetryCount);
        Assert.Contains("executor-failure", row.ErrorMessage);
    }

    [Fact]
    public async Task Failure_below_max_retries_is_retrying_and_retry_succeeds()
    {
        var harness = new Harness(maxRetries: 3);
        harness.Executor.ThrowOnExecute = true;
        var transactionId = Guid.NewGuid();
        await harness.Compensator.RegisterCompensationAsync(transactionId, TestChargeCompensationExecutor.ExecutorName, null);

        await harness.Compensator.CompensateAsync(transactionId);
        var row = Assert.Single(harness.Repository.Items);
        Assert.Equal(CompensationStatus.Retrying, row.Status);
        Assert.Equal(1, row.RetryCount);

        harness.Executor.ThrowOnExecute = false;
        await harness.Compensator.ProcessRetryingCompensationsAsync();

        Assert.Equal(CompensationStatus.Completed, row.Status);
        Assert.Equal(2, harness.Executor.Invocations.Count);
    }

    [Fact]
    public async Task Historic_record_with_removed_executor_fails_visibly()
    {
        var harness = new Harness();
        var stale = new TransactionCompensation
        {
            Id = Guid.NewGuid(),
            TransactionId = Guid.NewGuid(),
            ParticipantName = "removed-executor",
            Status = CompensationStatus.Pending,
            MaxRetries = 3,
            CreatedAt = DateTime.UtcNow
        };
        harness.Repository.Items.Add(stale);

        await harness.Compensator.CompensateAsync(stale.TransactionId);

        Assert.Equal(CompensationStatus.Failed, stale.Status);
        Assert.Contains("No executor found", stale.ErrorMessage);
    }

    [Fact]
    public async Task Persistent_logger_roundtrips_with_repository()
    {
        var repository = new TestCapRepository<TransactionLog, Guid>();
        var logger = new PersistentTransactionLogger(repository);
        var transactionId = Guid.NewGuid();

        await logger.LogTransactionAsync(transactionId, TransactionStatus.Started, "started");
        await logger.LogTransactionErrorAsync(transactionId, new InvalidOperationException("boom"));

        var row = Assert.Single(repository.Items);
        Assert.Equal(TransactionStatus.Started, row.Status);
        Assert.Contains("boom", row.ErrorDetails);
        Assert.Equal(TransactionStatus.Started, await logger.GetTransactionStatusAsync(transactionId));
    }

    private sealed class Harness
    {
        public Harness(int maxRetries = 3)
        {
            CapOptions = Microsoft.Extensions.Options.Options.Create(new DistributedTransactionCapOptions
            {
                CompensationMaxRetries = maxRetries,
                CompensationRetryIntervalSeconds = 0
            });

            var services = new ServiceCollection();
            services.AddCompensationExecutors(); // 真实生成产物
            Provider = services.BuildServiceProvider();
            Scope = Provider.CreateScope();
            Executor = Scope.ServiceProvider.GetRequiredService<TestChargeCompensationExecutor>();
            var registry = Scope.ServiceProvider.GetRequiredService<ICompensationExecutorRegistry>();
            Repository = new TestCapRepository<TransactionCompensation, Guid>();
            Compensator = new PersistentTransactionCompensator(Repository, registry, CapOptions);
        }

        public IOptions<DistributedTransactionCapOptions> CapOptions { get; }

        public ServiceProvider Provider { get; }

        public IServiceScope Scope { get; }

        public TestChargeCompensationExecutor Executor { get; }

        public TestCapRepository<TransactionCompensation, Guid> Repository { get; }

        public PersistentTransactionCompensator Compensator { get; }
    }
}
