namespace CrestCreates.MultiTenancy.Abstract
{
    public interface ICurrentTenant
    {
        ITenantInfo Tenant { get; }
        
        string Id { get; }
        
        IDisposable Change(string tenantId);
        
        void SetTenantId(string tenantId);
    }
}
