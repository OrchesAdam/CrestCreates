namespace CrestCreates.HumanTask;

/// <summary>Bounds compatibility work which is allowed to outlive a delivery attempt.</summary>
public sealed class HumanTaskDeliveryOptions
{
    public int MaximumDetachedOptionalExecutions { get; set; } = 32;

    internal void Validate()
    {
        if (MaximumDetachedOptionalExecutions is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(MaximumDetachedOptionalExecutions));
    }
}
