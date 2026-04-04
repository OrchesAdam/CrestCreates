using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CrestCreates.Authorization.Abstractions;

public interface IPermissionChecker
{
    Task<bool> IsGrantedAsync(string permissionName);
    Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName);
    Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames);
    Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal principal, string[] permissionNames);
    Task CheckAsync(string permissionName);
}

public class MultiplePermissionGrantResult
{
    public Dictionary<string, bool> Result { get; }

    public MultiplePermissionGrantResult(Dictionary<string, bool> result)
    {
        Result = result ?? new Dictionary<string, bool>();
    }

    public bool AllGranted => Result.Values.All(v => v);
    public bool AllProhibited => Result.Values.All(v => !v);
}
