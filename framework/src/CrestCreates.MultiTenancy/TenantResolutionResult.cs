using CrestCreates.Domain.Permission;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.MultiTenancy;

public static class TenantResolutionResultExtensions
{
    public static TenantResolutionResult Success(this Tenant tenant, string resolvedBy)
    {
        return TenantResolutionResult.Success(
            tenant.Id.ToString(),
            tenant.Name,
            tenant.GetDefaultConnectionString(),
            resolvedBy);
    }
}
