namespace CrestCreates.Accountability.Recording;

public sealed class AccountabilityOptions
{
    public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public bool RequireAtLeastOneSink { get; set; }
}
