using System.Threading.Tasks;

namespace CrestCreates.Aop.Abstractions.Interfaces;

public interface IPermissionService
{
    Task<bool> IsGrantedAsync(string permissionKey);
    Task CheckAsync(string permissionKey);
    Task<IEnumerable<string>> GetGrantedPermissionsAsync();
}
