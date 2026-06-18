using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using Microsoft.Extensions.Options;

namespace CrestCreates.Application.Tenants;

public class TenantDeletionGuard : ITenantDeletionGuard
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly TenantDeletionOptions _options;

    public TenantDeletionGuard(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IOptions<TenantDeletionOptions> options)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _options = options.Value;
    }

    public async Task<TenantDeletionGuardResult> CanDeleteAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        if (_options.RequireEmptyUsersBeforeDelete)
        {
            var users = await _userRepository.GetListByTenantIdAsync(tenant.Id.ToString(), cancellationToken);
            if (users.Count > 0)
            {
                return TenantDeletionGuardResult.Failure(
                    $"租户下仍有 {users.Count} 个用户",
                    users.ConvertAll(u => u.UserName).ToArray());
            }
        }

        if (_options.RequireEmptyRolesBeforeDelete)
        {
            var roles = await _roleRepository.GetListByTenantIdAsync(tenant.Id.ToString(), cancellationToken);
            if (roles.Count > 0)
            {
                return TenantDeletionGuardResult.Failure(
                    $"租户下仍有 {roles.Count} 个角色",
                    null,
                    roles.ConvertAll(r => r.Name).ToArray());
            }
        }

        return TenantDeletionGuardResult.Success();
    }
}
