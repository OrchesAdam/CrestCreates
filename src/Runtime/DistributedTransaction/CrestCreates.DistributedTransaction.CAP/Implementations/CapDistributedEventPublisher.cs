using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using DotNetCore.CAP;

namespace CrestCreates.DistributedTransaction.CAP.Implementations;

/// <summary>
/// CAP 发布器（原子发布协议的 Runtime 侧）：
/// 1) 以内核唯一权威 <see cref="UnitOfWorkExecutionContext.ResolveParticipation"/> 做参与判定（三值）；
/// 2) <c>NoVisibleWindow</c> → 普通发布（CAP 非事务路径；与业务落库非原子，文档明示）；
/// 3) <c>Granted</c> → 经集成层 <see cref="ICapManagedTransactionProvider"/> 瞬态绑定受管事务，
///    消息行与业务数据在同一事务内写入；投递由 CAP 存储扫描恢复完成（名义扫描等待，非时限）；
///    窗口内延迟发布不支持（确定性拒绝）；
/// 4) <c>Refused</c> → 确定性拒绝（<see cref="CapPublishRefusalException"/>），先于任何写入、零副作用。
/// </summary>
public class CapDistributedEventPublisher : IDistributedEventPublisher
{
    private readonly ICapPublisher _capPublisher;
    private readonly UnitOfWorkChainNode? _chainNode;
    private readonly ICapManagedTransactionProvider? _managedTransactionProvider;
    private readonly TenantKeySnapshot _readerTenant;

    public CapDistributedEventPublisher(
        ICapPublisher capPublisher,
        UnitOfWorkChainNode? chainNode = null,
        ICapManagedTransactionProvider? managedTransactionProvider = null,
        ICurrentTenant? currentTenant = null)
    {
        _capPublisher = capPublisher ?? throw new ArgumentNullException(nameof(capPublisher));
        _chainNode = chainNode;
        _managedTransactionProvider = managedTransactionProvider;
        _readerTenant = currentTenant is null ? TenantKeySnapshot.NotParticipating : new TenantKeySnapshot(currentTenant.Id);
    }

    public Task PublishAsync<TMessage>(
        string topicName,
        TMessage message,
        IDictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        return PublishCoreAsync(topicName, message, headers, delay: null, cancellationToken);
    }

    public Task PublishDelayAsync<TMessage>(
        TimeSpan delay,
        string topicName,
        TMessage message,
        IDictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        if (delay <= TimeSpan.Zero)
        {
            throw new ArgumentException("Delay time span must be greater than 0", nameof(delay));
        }

        return PublishCoreAsync(topicName, message, headers, delay, cancellationToken);
    }

    private async Task PublishCoreAsync<TMessage>(
        string topicName,
        TMessage message,
        IDictionary<string, string?>? headers,
        TimeSpan? delay,
        CancellationToken cancellationToken)
    {
        var participation = UnitOfWorkExecutionContext.ResolveParticipation(_chainNode, _readerTenant);
        switch (participation.Decision)
        {
            case UnitOfWorkParticipationDecision.Refused:
                throw new CapPublishRefusalException(
                    "ManagedWindowRefused",
                    $"The managed unit of work forbids CAP participation ('{participation.RefusalReason}'); " +
                    "the message was not written. Start a new unit of work or publish outside the window.");

            case UnitOfWorkParticipationDecision.NoVisibleWindow:
                // 明确允许的「无窗口」路径：CAP 原生非事务发布（与业务落库非原子）。
                // 集成边界可用时先探测未受管原生事务（可检测时）。
                _managedTransactionProvider?.EnsureNoUnmanagedNativeTransaction();
                if (delay is { } plainDelay)
                {
                    await _capPublisher
                        .PublishDelayAsync(plainDelay, topicName, message, headers ?? new Dictionary<string, string?>(), cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _capPublisher
                        .PublishAsync(topicName, message, headers ?? new Dictionary<string, string?>(), cancellationToken)
                        .ConfigureAwait(false);
                }
                return;

            case UnitOfWorkParticipationDecision.Granted:
                if (_managedTransactionProvider is null)
                {
                    throw new CapPublishRefusalException(
                        "BorrowerUnavailable",
                        "A managed unit-of-work window grants participation, but no CAP transaction provider is " +
                        "registered. Reference the EF Core integration package and register it; the message was not written.");
                }

                if (delay is not null)
                {
                    // CAP 事务分支不支持延迟调度（扫描恢复只处理即时行），语义会静默降级——确定性拒绝。
                    throw new CapPublishRefusalException(
                        "DelayedPublishInManagedWindow",
                        "Delayed publish inside a managed unit of work is not supported: the transactional message " +
                        "would lose its delay semantics. Publish delayed messages outside the window; nothing was written.");
                }

                using (var binding = _managedTransactionProvider.BindManagedTransaction(_capPublisher))
                {
                    await _capPublisher
                        .PublishAsync(topicName, message, headers ?? new Dictionary<string, string?>(), cancellationToken)
                        .ConfigureAwait(false);
                }
                return;

            default:
                throw new InvalidOperationException($"Unhandled participation decision '{participation.Decision}'.");
        }
    }
}
