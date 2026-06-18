namespace CrestCreates.MultiTenancy;

public class TenantDeletionOptions
{
    public TenantDeletionStrategy Strategy { get; set; } = TenantDeletionStrategy.SoftDelete;
    public bool RequireEmptyUsersBeforeDelete { get; set; } = true;
    public bool RequireEmptyRolesBeforeDelete { get; set; } = true;
    public bool RequireNoActiveSubscriptions { get; set; } = true;
}

public enum TenantDeletionStrategy
{
    Forbidden,
    SoftDelete,
    Archive
}
