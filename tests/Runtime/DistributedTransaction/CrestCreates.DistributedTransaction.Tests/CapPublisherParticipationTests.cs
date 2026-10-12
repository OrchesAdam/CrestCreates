using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Implementations;
using DotNetCore.CAP;
using Xunit;

namespace CrestCreates.DistributedTransaction.Tests;

/// <summary>
/// #125 S3（单元层）：发布器参与判定矩阵与零副作用拒绝、瞬态绑定序列。
/// 集成层（真实 DB/broker）见 CAP.IntegrationTests。
/// </summary>
public class CapPublisherParticipationTests
{
    [Fact]
    public async Task Not_started_window_refuses_with_zero_side_effects()
    {
        var publisher = new FakeCapPublisher();
        var provider = new FakeManagedProvider();
        var manager = NewManager(out _);
        await using var scope = manager.BeginScope(); // 未 StartAsync

        var subject = new CapDistributedEventPublisher(publisher, chainNode: null, provider);
        var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
            () => subject.PublishAsync("topic.a", new { Value = 1 }));

        Assert.Equal("ManagedWindowRefused", exception.Code);
        Assert.Contains("NotStarted", exception.Message);
        Assert.Equal(0, publisher.PublishCalls);
        Assert.Equal(0, provider.BindCalls);
        Assert.Equal(0, provider.EnsureCalls);
    }

    [Fact]
    public async Task Non_transactional_window_refuses_with_zero_side_effects()
    {
        var publisher = new FakeCapPublisher();
        var provider = new FakeManagedProvider();
        var manager = NewManager(out _);
        await using var scope = manager.BeginScope(new UnitOfWorkOptions { IsTransactional = false });
        await scope.StartAsync();

        var subject = new CapDistributedEventPublisher(publisher, chainNode: null, provider);
        var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
            () => subject.PublishAsync("topic.a", new { Value = 1 }));

        Assert.Contains("NonTransactional", exception.Message);
        Assert.Equal(0, publisher.PublishCalls);
        Assert.Equal(0, provider.BindCalls);
    }

    [Fact]
    public async Task Completed_window_refuses_new_participation()
    {
        var publisher = new FakeCapPublisher();
        var provider = new FakeManagedProvider();
        var manager = NewManager(out _);
        await using var scope = manager.BeginScope();
        await scope.StartAsync();
        await scope.CompleteAsync();

        var subject = new CapDistributedEventPublisher(publisher, chainNode: null, provider);
        var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
            () => subject.PublishAsync("topic.a", new { Value = 1 }));

        Assert.Contains("Terminal", exception.Message);
        Assert.Equal(0, publisher.PublishCalls);
    }

    [Fact]
    public async Task No_visible_window_publishes_plainly()
    {
        var publisher = new FakeCapPublisher();
        var subject = new CapDistributedEventPublisher(publisher);

        await subject.PublishAsync("topic.plain", new { Value = 1 });
        await subject.PublishDelayAsync(TimeSpan.FromMinutes(10), "topic.delayed", new { Value = 2 });

        Assert.Equal(2, publisher.TotalCalls);
        Assert.Equal("PublishAsync", publisher.Calls[0]);
        Assert.Equal("PublishDelayAsync", publisher.Calls[1]);
    }

    [Fact]
    public async Task No_visible_window_with_adapter_probes_unmanaged_native_transaction()
    {
        var publisher = new FakeCapPublisher();
        var provider = new FakeManagedProvider();
        var subject = new CapDistributedEventPublisher(publisher, chainNode: null, provider);

        await subject.PublishAsync("topic.plain", new { Value = 1 });

        Assert.Equal(1, provider.EnsureCalls);
        Assert.Equal(1, publisher.PublishCalls);
        Assert.Equal(0, provider.BindCalls);
    }

    [Fact]
    public async Task Granted_without_provider_fails_deterministically()
    {
        var publisher = new FakeCapPublisher();
        var manager = NewManager(out _);
        await using var scope = manager.BeginScope();
        await scope.StartAsync();

        var subject = new CapDistributedEventPublisher(publisher);
        var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
            () => subject.PublishAsync("topic.a", new { Value = 1 }));

        Assert.Equal("BorrowerUnavailable", exception.Code);
        Assert.Equal(0, publisher.PublishCalls);
    }

    [Fact]
    public async Task Granted_publish_binds_transaction_then_disposes_binding()
    {
        var provider = new FakeManagedProvider();
        var publisher = new FakeCapPublisher(provider.Sequence);
        var manager = NewManager(out _);
        await using var scope = manager.BeginScope();
        await scope.StartAsync();

        var subject = new CapDistributedEventPublisher(publisher, chainNode: null, provider);
        await subject.PublishAsync("topic.managed", new { Value = 1 });

        Assert.Equal(1, provider.BindCalls);
        Assert.Equal(1, provider.DisposedBindings);
        Assert.Equal(1, publisher.PublishCalls);
        Assert.Equal(new[] { "bind", "publish", "binding-disposed" }, provider.Sequence);
    }

    [Fact]
    public async Task Granted_delayed_publish_is_rejected_before_any_action()
    {
        var publisher = new FakeCapPublisher();
        var provider = new FakeManagedProvider();
        var manager = NewManager(out _);
        await using var scope = manager.BeginScope();
        await scope.StartAsync();

        var subject = new CapDistributedEventPublisher(publisher, chainNode: null, provider);
        var exception = await Assert.ThrowsAsync<CapPublishRefusalException>(
            () => subject.PublishDelayAsync(TimeSpan.FromMinutes(10), "topic.delayed", new { Value = 1 }));

        Assert.Equal("DelayedPublishInManagedWindow", exception.Code);
        Assert.Equal(0, publisher.TotalCalls);
        Assert.Equal(0, provider.BindCalls);
    }

    private static UnitOfWorkManager NewManager(out FakeUnitOfWork unitOfWork)
    {
        unitOfWork = new FakeUnitOfWork();
        return new UnitOfWorkManager(new SingleFactory(unitOfWork));
    }

    // ── 基架 ──

    private sealed class SingleFactory : IUnitOfWorkFactory
    {
        private readonly IUnitOfWork _unitOfWork;

        public SingleFactory(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        public IUnitOfWork Create(OrmProvider provider) => _unitOfWork;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork, IUnitOfWorkManagedResource, IUnitOfWorkCommittedNotifier
    {
        public object? Handle { get; private set; }

        public string? ManagedResourceKind => "FakeContext";

        public string? ManagedResourceConnectionString => null;

        public object? ManagedResourceContext { get; } = new object();

        public object? ManagedTransactionHandle => Handle;

        public Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default)
        {
            Handle = new object();
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task PublishCommittedNotificationsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FakeManagedProvider : ICapManagedTransactionProvider
    {
        public int EnsureCalls { get; private set; }

        public int BindCalls { get; private set; }

        public int DisposedBindings { get; private set; }

        public List<string> Sequence { get; } = new();

        public void EnsureNoUnmanagedNativeTransaction() => EnsureCalls++;

        public ICapManagedTransactionBinding BindManagedTransaction(ICapPublisher publisher)
        {
            BindCalls++;
            Sequence.Add("bind");
            return new FakeBinding(this);
        }

        private sealed class FakeBinding : ICapManagedTransactionBinding
        {
            private readonly FakeManagedProvider _owner;

            public FakeBinding(FakeManagedProvider owner) => _owner = owner;

            public void Dispose()
            {
                _owner.DisposedBindings++;
                _owner.Sequence.Add("binding-disposed");
            }
        }
    }

    private sealed class FakeCapPublisher : ICapPublisher
    {
        private readonly List<string>? _sequence;

        public FakeCapPublisher(List<string>? sequence = null) => _sequence = sequence;

        public List<string> Calls { get; } = new();

        public int PublishCalls { get; private set; }

        public int TotalCalls => Calls.Count;

        public IServiceProvider ServiceProvider => throw new NotSupportedException();

        public ICapTransaction? Transaction { get; set; }

        public Task PublishAsync<T>(string name, T? value, IDictionary<string, string?> headers, CancellationToken cancellationToken = default)
        {
            Calls.Add("PublishAsync");
            PublishCalls++;
            _sequence?.Add("publish");
            return Task.CompletedTask;
        }

        public Task PublishAsync<T>(string name, T? value, string? callbackName = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("PublishAsync");
            PublishCalls++;
            _sequence?.Add("publish");
            return Task.CompletedTask;
        }

        public Task PublishDelayAsync<T>(TimeSpan delayTime, string name, T? value, IDictionary<string, string?> headers, CancellationToken cancellationToken = default)
        {
            Calls.Add("PublishDelayAsync");
            return Task.CompletedTask;
        }

        public Task PublishDelayAsync<T>(TimeSpan delayTime, string name, T? value, string? callbackName = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("PublishDelayAsync");
            return Task.CompletedTask;
        }

        public void Publish<T>(string name, T? value, string? callbackName = null) => Calls.Add("Publish");

        public void Publish<T>(string name, T? value, IDictionary<string, string?> headers) => Calls.Add("Publish");

        public void PublishDelay<T>(TimeSpan delayTime, string name, T? value, IDictionary<string, string?> headers) => Calls.Add("PublishDelay");

        public void PublishDelay<T>(TimeSpan delayTime, string name, T? value, string? callbackName = null) => Calls.Add("PublishDelay");
    }
}
