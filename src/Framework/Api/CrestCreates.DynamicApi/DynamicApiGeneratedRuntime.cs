using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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

    public static async Task ExecuteAsync(HttpContext context, bool requiresTransaction, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        var unitOfWorkManager = context.RequestServices.GetService<IUnitOfWorkManager>();
        if (unitOfWorkManager is null)
        {
            await action();
            return;
        }

        using var scope = unitOfWorkManager.BeginScope(isTransactional: requiresTransaction);

        try
        {
            if (scope.IsOwner && scope.IsTransactional)
            {
                await scope.UnitOfWork.BeginTransactionAsync();
            }

            await action();

            if (scope.IsOwner && scope.IsTransactional)
            {
                await scope.UnitOfWork.CommitTransactionAsync();
            }
            else if (scope.IsOwner)
            {
                await scope.UnitOfWork.SaveChangesAsync();
            }
        }
        catch
        {
            if (scope.IsOwner)
            {
                await scope.UnitOfWork.RollbackTransactionAsync();
            }

            throw;
        }
    }

    public static async Task<T?> ExecuteAsync<T>(HttpContext context, bool requiresTransaction, Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        var unitOfWorkManager = context.RequestServices.GetService<IUnitOfWorkManager>();
        if (unitOfWorkManager is null)
        {
            return await action();
        }

        using var scope = unitOfWorkManager.BeginScope(isTransactional: requiresTransaction);

        try
        {
            if (scope.IsOwner && scope.IsTransactional)
            {
                await scope.UnitOfWork.BeginTransactionAsync();
            }

            var result = await action();

            if (scope.IsOwner && scope.IsTransactional)
            {
                await scope.UnitOfWork.CommitTransactionAsync();
            }
            else if (scope.IsOwner)
            {
                await scope.UnitOfWork.SaveChangesAsync();
            }

            return result;
        }
        catch
        {
            if (scope.IsOwner)
            {
                await scope.UnitOfWork.RollbackTransactionAsync();
            }

            throw;
        }
    }
}
