using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Domain.MultiTenancy
{
    public interface ITenantInfo
    {
        string Id { get; }
        string Name { get; }
        string ConnectionString { get; }
    }
    
    public class TenantInfo : ITenantInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ConnectionString { get; set; }
        
        public TenantInfo(string id, string name, string connectionString)
        {
            Id = id;
            Name = name;
            ConnectionString = connectionString;
        }
    }

    public interface ITenantProvider
    {
        Task<ITenantInfo> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default);
    }
}
