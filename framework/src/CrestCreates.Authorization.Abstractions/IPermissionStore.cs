using System.Security.Claims;
using System.Threading.Tasks;

namespace CrestCreates.Authorization.Abstractions;

public interface IPermissionStore
{
    Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName);
}
