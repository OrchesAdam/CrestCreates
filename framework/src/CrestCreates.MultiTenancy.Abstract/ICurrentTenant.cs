using System;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract
{
    public interface ICurrentTenant
    {
        ITenantInfo Tenant { get; }

        string Id { get; }

        Task<IDisposable> ChangeAsync(string tenantId);

        void SetTenantId(string tenantId);
    }
}
