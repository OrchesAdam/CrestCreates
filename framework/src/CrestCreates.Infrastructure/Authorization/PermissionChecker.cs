using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CrestCreates.Infrastructure.Authorization
{
    public interface IPermissionChecker
    {
        Task<bool> IsGrantedAsync(string permissionName);
        Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName);
        Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames);
        Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal principal, string[] permissionNames);
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

    public class PermissionChecker : IPermissionChecker
    {
        private readonly IPermissionStore _permissionStore;
        private readonly ICurrentPrincipalAccessor _principalAccessor;

        public PermissionChecker(
            IPermissionStore permissionStore,
            ICurrentPrincipalAccessor principalAccessor)
        {
            _permissionStore = permissionStore;
            _principalAccessor = principalAccessor;
        }

        public Task<bool> IsGrantedAsync(string permissionName)
        {
            return IsGrantedAsync(_principalAccessor.Principal, permissionName);
        }

        public async Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName)
        {
            if (principal?.Identity == null || !principal.Identity.IsAuthenticated)
            {
                return false;
            }

            return await _permissionStore.IsGrantedAsync(principal, permissionName);
        }

        public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames)
        {
            return IsGrantedAsync(_principalAccessor.Principal, permissionNames);
        }

        public async Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal principal, string[] permissionNames)
        {
            var result = new Dictionary<string, bool>();

            if (principal?.Identity == null || !principal.Identity.IsAuthenticated)
            {
                foreach (var permissionName in permissionNames)
                {
                    result[permissionName] = false;
                }
                
                return new MultiplePermissionGrantResult(result);
            }

            foreach (var permissionName in permissionNames)
            {
                result[permissionName] = await _permissionStore.IsGrantedAsync(principal, permissionName);
            }

            return new MultiplePermissionGrantResult(result);
        }
    }

    public interface IPermissionStore
    {
        Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName);
    }

    public interface ICurrentPrincipalAccessor
    {
        ClaimsPrincipal Principal { get; }
        IDisposable Change(ClaimsPrincipal principal);
    }
}
