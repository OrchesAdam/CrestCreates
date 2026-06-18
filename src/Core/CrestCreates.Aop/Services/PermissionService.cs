using System;
using System.Threading.Tasks;
using CrestCreates.Aop.Abstractions.Interfaces;
using CrestCreates.Aop.Abstractions.Options;
using CrestCreates.Authorization.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.Aop.Services;

public class PermissionService : IPermissionService
{
    private readonly IPermissionChecker _permissionChecker;
    private readonly PermissionOptions _options;
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(
        IPermissionChecker permissionChecker,
        IOptions<PermissionOptions> options,
        ILogger<PermissionService> logger)
    {
        _permissionChecker = permissionChecker;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> IsGrantedAsync(string permissionKey)
    {
        var permissionName = _options.GetPermissionName(permissionKey);
        try
        {
            await _permissionChecker.CheckAsync(permissionName);
            return true;
        }
        catch (CrestPermissionException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "权限检查失败: {PermissionKey}", permissionKey);
            return false;
        }
    }

    public async Task CheckAsync(string permissionKey)
    {
        var permissionName = _options.GetPermissionName(permissionKey);
        await _permissionChecker.CheckAsync(permissionName);
    }

    public async Task<IEnumerable<string>> GetGrantedPermissionsAsync()
    {
        return await Task.FromResult(Enumerable.Empty<string>());
    }
}
