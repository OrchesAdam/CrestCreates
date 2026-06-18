using System.Threading.Channels;

namespace CrestCreates.EventBus.Abstractions;

public sealed class LocalEventBusOptions
{
    public int ChannelCapacity { get; set; } = 1024;

    public BoundedChannelFullMode ChannelFullMode { get; set; } = BoundedChannelFullMode.Wait;

    public bool SingleReader { get; set; } = true;

    public bool SingleWriter { get; set; } = false;
}
