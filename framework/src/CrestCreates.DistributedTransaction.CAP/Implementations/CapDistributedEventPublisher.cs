using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using DotNetCore.CAP;

namespace CrestCreates.DistributedTransaction.CAP.Implementations;

public class CapDistributedEventPublisher : IDistributedEventPublisher
{
    private readonly ICapPublisher _capPublisher;

    public CapDistributedEventPublisher(ICapPublisher capPublisher)
    {
        _capPublisher = capPublisher ?? throw new ArgumentNullException(nameof(capPublisher));
    }

    public Task PublishAsync<TMessage>(
        string topicName,
        TMessage message,
        IDictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);

        return _capPublisher.PublishAsync(topicName, message, headers ?? new Dictionary<string, string?>(), cancellationToken);
    }

    public Task PublishDelayAsync<TMessage>(
        TimeSpan delay,
        string topicName,
        TMessage message,
        IDictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);

        return _capPublisher.PublishDelayAsync(delay, topicName, message, headers ?? new Dictionary<string, string?>(), cancellationToken);
    }
}
