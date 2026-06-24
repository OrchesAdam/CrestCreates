namespace CrestCreates.EventBus;

public class TenantEventContext
{
    public string? TenantId { get; set; }
    public string? TenantName { get; set; }
    public bool IsSuperAdminContext { get; set; }
}
