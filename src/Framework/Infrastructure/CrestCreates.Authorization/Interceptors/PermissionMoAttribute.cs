using System;
using System.Threading.Tasks;
using CrestCreates.Aop.Abstractions;
using CrestCreates.Aop.Abstractions.Options;
using CrestCreates.Authorization.Abstractions;
using Microsoft.Extensions.DependencyInjection;
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
        var permissionChecker = ResolveService<IPermissionChecker>(context);
        if (permissionChecker == null)
        {
            // An unauthorized call that looks successful is worse than a deterministic refusal.
            throw new InvalidOperationException(
                $"[{nameof(PermissionMoAttribute)}] woven member "
                + $"'{context.Target?.GetType().Name}.{context.Method?.Name}' requires "
                + $"{nameof(IPermissionChecker)} in the target's service provider; the permission gate "
                + "fails closed instead of skipping the check. Ensure the authorization kernel is "
                + $"registered and the target implements {nameof(IServiceProviderAccessor)} or the host "
                + "pins the Rougamo scope (UsePinnedScopeServiceProvider).");
        }

        var options = ResolveService<IOptions<PermissionOptions>>(context)?.Value;
        var permissionName = options?.GetPermissionName(_permissionKey) ?? _permissionKey;
        await permissionChecker.CheckAsync(permissionName);
    }

    /// <summary>
    /// The accessor contract is authoritative when the target opts in; the pinned Rougamo scope
    /// remains the fallback for framework-owned targets that do not expose their provider.
    /// </summary>
    private static T? ResolveService<T>(MethodContext context) where T : class
    {
        var accessorProvider = (context.Target as IServiceProviderAccessor)?.ServiceProvider;
        return accessorProvider is not null
            ? accessorProvider.GetService<T>()
            : context.GetService<T>();
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
