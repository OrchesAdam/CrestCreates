using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.DistributedTransaction.Abstractions;

/// <summary>
/// 分布式事件发布器，用于把业务事件写入可靠消息通道。
/// </summary>
public interface IDistributedEventPublisher
{
    Task PublishAsync<TMessage>(
        string topicName,
        TMessage message,
        IDictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default);

    Task PublishDelayAsync<TMessage>(
        TimeSpan delay,
        string topicName,
        TMessage message,
        IDictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default);
}
