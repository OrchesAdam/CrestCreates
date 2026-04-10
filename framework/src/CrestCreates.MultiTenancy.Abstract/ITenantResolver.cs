using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.MultiTenancy.Abstract
{
    public interface ITenantResolver
    {
        Task<TenantResolutionResult> ResolveAsync(HttpContext httpContext);
    }
}
