using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local.Channel;

public class ChannelLocalEventQueue
{
    private readonly Channel<ILocalEvent> _channel;

    public ChannelLocalEventQueue(LocalEventBusOptions options)
    {
        var channelOptions = new BoundedChannelOptions(options.ChannelCapacity)
        {
            FullMode = options.ChannelFullMode,
            SingleReader = options.SingleReader,
            SingleWriter = options.SingleWriter,
            AllowSynchronousContinuations = true
        };

        _channel = global::System.Threading.Channels.Channel.CreateBounded<ILocalEvent>(channelOptions);
    }

    public ValueTask EnqueueAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(@event, cancellationToken);
    }

    public async IAsyncEnumerable<ILocalEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var @event))
            {
                yield return @event;
            }
        }
    }
}
