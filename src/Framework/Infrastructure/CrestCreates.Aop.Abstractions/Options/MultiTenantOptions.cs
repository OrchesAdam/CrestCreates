namespace CrestCreates.Aop.Abstractions.Options;

public class MultiTenantOptions
{
    public bool EnableTenantFilter { get; set; } = true;
    public bool Required { get; set; } = true;
    public string? DefaultTenantId { get; set; }
}
