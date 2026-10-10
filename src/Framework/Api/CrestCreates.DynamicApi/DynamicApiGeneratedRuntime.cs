using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Data.Abstractions;
using CrestCreates.Validation.Modules;
using CrestCreates.Validation.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Legacy runtime helpers for AppService-oriented Dynamic API endpoints.
/// New Capability Endpoint projection uses its own endpoint JSON binding runtime.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
    Justification = "Legacy runtime (Tier 4) — not on the AOT-verified mainline. Use CapabilityEndpointBodyReader for AOT-safe binding.")]
[UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
    Justification = "Legacy runtime (Tier 4) — not on the AOT-verified mainline.")]
public static class DynamicApiGeneratedRuntime
{
    public static JsonSerializerOptions ResolveJsonSerializerOptions(IServiceProvider serviceProvider)
    {
        var jsonOptions = serviceProvider.GetService<IOptions<JsonOptions>>();
        return new JsonSerializerOptions(jsonOptions?.Value.SerializerOptions ?? new JsonSerializerOptions())
        {
            PropertyNameCaseInsensitive = true
        };
    }

    [Obsolete("Use CapabilityEndpointBodyReader with JsonTypeInfo<T> from CapabilityEndpointJsonTypeInfoResolver.")]
    public static Task<T?> ReadBodyAsync<T>(HttpContext context, bool optional)
        where T : new()
        => CompatibilityBodyReader.ReadBodyAsync<T>(context, optional);

    public static async Task EnsurePermissionAsync(
        HttpContext context,
        IPermissionChecker? permissionChecker,
        IReadOnlyCollection<string> permissions)
    {
        if (permissionChecker is null || permissions.Count == 0)
        {
            return;
        }

        if (context.User?.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("当前请求未认证。");
        }

        var grantResult = await permissionChecker.IsGrantedAsync(context.User, permissions.ToArray());
        if (grantResult.AllProhibited)
        {
            throw new CrestPermissionException(string.Join(",", permissions));
        }
    }

    public static async Task ValidateAsync<T>(IValidationService? validationService, T? instance)
    {
        if (validationService is null || instance is null || DynamicApiRouteConvention.IsScalar(typeof(T)))
        {
            return;
        }

        var result = await validationService.ValidateAsync(instance);
        if (!result.IsValid)
        {
            throw new ArgumentException(string.Join("; ", result.Errors));
        }
    }

    public static IResult WrapResult<T>(T? value)
        => CompatibilityHttpResultMapper.WrapResult(value);

    public static IResult WrapVoidResult()
        => CompatibilityHttpResultMapper.WrapVoidResult();

    public static IResult WrapGetResult<T>(T? value)
        => CompatibilityHttpResultMapper.WrapGetResult(value);

    /// <summary>
    /// 委托内核执行；生成端点把内核联动 token 传入业务方法的 CT 参数。
    /// </summary>
    public static async Task ExecuteAsync(
        HttpContext context,
        UnitOfWorkOptions options,
        Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(action);

        var unitOfWorkManager = context.RequestServices.GetService<IUnitOfWorkManager>();
        if (unitOfWorkManager is null)
        {
            await action(context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await unitOfWorkManager.ExecuteAsync(
            async kernelToken =>
            {
                await action(kernelToken).ConfigureAwait(false);
                return true;
            },
            options,
            context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// 委托内核执行；生成端点把内核联动 token 传入业务方法的 CT 参数。
    /// </summary>
    public static async Task<T?> ExecuteAsync<T>(
        HttpContext context,
        UnitOfWorkOptions options,
        Func<CancellationToken, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(action);

        var unitOfWorkManager = context.RequestServices.GetService<IUnitOfWorkManager>();
        if (unitOfWorkManager is null)
        {
            return await action(context.RequestAborted).ConfigureAwait(false);
        }

        return await unitOfWorkManager.ExecuteAsync(action, options, context.RequestAborted).ConfigureAwait(false);
    }
}
