using System;
using System.Threading.Tasks;
using CrestCreates.Aop.Abstractions;
using CrestCreates.Aop.Abstractions.Options;
using CrestCreates.Authorization.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rougamo;
using Rougamo.Context;

namespace CrestCreates.Authorization.Interceptors;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class PermissionMoAttribute : AsyncMoAttribute
{
    private readonly string _permissionKey;
    public int Order => InterceptorOrders.Permission;

    public PermissionMoAttribute(string permissionKey)
    {
        _permissionKey = permissionKey ?? throw new ArgumentNullException(nameof(permissionKey));
    }

    public override async ValueTask OnEntryAsync(MethodContext context)
    {
        try
        {
            var options = context.GetService<IOptions<PermissionOptions>>()?.Value;
            var permissionName = options?.GetPermissionName(_permissionKey) ?? _permissionKey;
            
            var permissionChecker = context.GetService<IPermissionChecker>();
            if (permissionChecker == null)
            {
                var logger = context.GetService<ILogger<PermissionMoAttribute>>();
                logger?.LogWarning("IPermissionChecker 未注册，跳过权限检查");
                return;
            }

            await permissionChecker.CheckAsync(permissionName);
        }
        catch (Exception exception)
        {
            var logger = context.GetService<ILogger<PermissionMoAttribute>>();
            logger?.LogError(exception, "权限检查失败");
        }
    }

    public override ValueTask OnSuccessAsync(MethodContext context)
    {
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnExceptionAsync(MethodContext context)
    {
        return ValueTask.CompletedTask;
    }
}
