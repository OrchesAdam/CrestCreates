using System;

namespace CrestCreates.Web;

public sealed class CrestWebOptions
{
    public CrestGeneratedApiWebOptions GeneratedApi { get; } = new();

    public bool EnableOpenIddict { get; private set; } = true;

    public CrestWebOptions UseGeneratedApi(Action<CrestGeneratedApiWebOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(GeneratedApi);
        return this;
    }

    public CrestWebOptions UseOpenIddict(bool enabled = true)
    {
        EnableOpenIddict = enabled;
        return this;
    }
}