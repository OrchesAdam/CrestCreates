namespace CrestCreates.Aop.Abstractions.Options;

public class AopOptions
{
    public PermissionOptions Permission { get; set; } = new();
    public CacheOptions Cache { get; set; } = new();
    public UnitOfWorkOptions UnitOfWork { get; set; } = new();
    public AuditOptions Audit { get; set; } = new();
    public InterceptorOrderOptions InterceptorOrder { get; set; } = new();
    public MultiTenantOptions MultiTenant { get; set; } = new();
    public DataPermissionOptions DataPermission { get; set; } = new();
}
