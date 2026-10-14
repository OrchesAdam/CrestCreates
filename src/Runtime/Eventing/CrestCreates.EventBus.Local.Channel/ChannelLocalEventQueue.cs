using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local.Channel;

/// <summary>
/// Bounded channel of immutable work items. The payload travels together with the context snapshot
/// and the accepted identity captured at the publish boundary; the consumer never re-reads the
/// publishing ambient state.
/// </summary>
public class ChannelLocalEventQueue
{
    private readonly Channel<LocalEventWorkItem> _channel;

    public ChannelLocalEventQueue(LocalEventBusOptions options)
    {
        var channelOptions = new BoundedChannelOptions(options.ChannelCapacity)
        {
            FullMode = options.ChannelFullMode,
            SingleReader = options.SingleReader,
            SingleWriter = options.SingleWriter,
            AllowSynchronousContinuations = true
        };

        _channel = global::System.Threading.Channels.Channel.CreateBounded<LocalEventWorkItem>(channelOptions);
    }

    public ValueTask EnqueueAsync(LocalEventWorkItem workItem, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    public async IAsyncEnumerable<LocalEventWorkItem> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var workItem))
            {
                yield return workItem;
            }
        }
    }
}
