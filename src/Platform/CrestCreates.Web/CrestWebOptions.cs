namespace CrestCreates.Web;

public sealed class CrestWebOptions
{
    public bool EnableOpenIddict { get; private set; } = true;

    public CrestWebOptions UseOpenIddict(bool enabled = true)
    {
        EnableOpenIddict = enabled;
        return this;
    }
}