namespace CrestCreates.Data.EFCore.MultiTenancy
{
    public interface ITenantConnectionStringResolver
    {
        string Resolve();
    }
}
