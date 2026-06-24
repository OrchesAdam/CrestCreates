namespace CrestCreates.EventBus;

public interface ITenantEventContextAccessor
{
    TenantEventContext? TenantContext { get; }
    void SetTenantContext(string? tenantId, string? tenantName = null, bool isSuperAdminContext = false);
    void ClearTenantContext();
}
