using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.Validation.Modules;
using CrestCreates.Validation.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestCreates.DynamicApi;

public interface IDynamicApiGeneratedProvider
{
    IReadOnlyCollection<System.Reflection.Assembly> ServiceAssemblies { get; }

    DynamicApiRegistry CreateRegistry(DynamicApiOptions options);

    void MapEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options);
}

public static class DynamicApiGeneratedRegistryStore
{
    private static readonly ConcurrentDictionary<string, IDynamicApiGeneratedProvider> Providers = new(StringComparer.Ordinal);

    public static void Register(IDynamicApiGeneratedProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Providers.TryAdd(provider.GetType().FullName ?? provider.GetType().Name, provider);
    }

    public static IReadOnlyCollection<IDynamicApiGeneratedProvider> GetProviders()
    {
        return Providers.Values.ToArray();
    }

    public static DynamicApiRegistry? BuildRegistry(DynamicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var registries = GetProviders()
            .Select(provider => provider.CreateRegistry(options))
            .Where(registry => registry.Services.Count > 0)
            .ToArray();

        if (registries.Length == 0)
        {
            return null;
        }

        return new DynamicApiRegistry(registries.SelectMany(registry => registry.Services).ToArray());
    }

    public static DynamicApiRegistry BuildRequiredRegistry(DynamicApiOptions options)
    {
        return BuildRegistry(options) ?? throw CreateMissingGeneratedProviderException(options);
    }

    public static bool MapGeneratedEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(options);

        var mapped = false;
        foreach (var provider in GetProviders())
        {
            var registry = provider.CreateRegistry(options);
            if (registry.Services.Count == 0)
            {
                continue;
            }

            provider.MapEndpoints(endpoints, options);
            mapped = true;
        }

        return mapped;
    }

    public static InvalidOperationException CreateMissingGeneratedProviderException(DynamicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var assemblies = options.ServiceAssemblies.Count == 0
            ? "未配置 ServiceAssemblies"
            : string.Join(", ", options.ServiceAssemblies.Select(assembly => assembly.GetName().Name));

        return new InvalidOperationException(
            $"Dynamic API 未找到编译期生成的 provider，当前主链只支持生成链。ServiceAssemblies: {assemblies}。如需临时诊断，可显式启用 {nameof(DynamicApiOptions.UseRuntimeReflectionFallback)}。");
    }
}

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

    public static async Task<T?> ReadBodyAsync<T>(HttpContext context, bool optional)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.ContentLength == 0)
        {
            return optional ? default : Activator.CreateInstance<T>();
        }

        context.Request.EnableBuffering();
        if (context.Request.Body.CanSeek)
        {
            context.Request.Body.Seek(0, SeekOrigin.Begin);
        }

        try
        {
            if (context.Request.Body.CanSeek && context.Request.Body.Length == 0)
            {
                return optional ? default : Activator.CreateInstance<T>();
            }

            var result = await context.Request.ReadFromJsonAsync<T>(ResolveJsonSerializerOptions(context.RequestServices), context.RequestAborted);
            if (result is not null)
            {
                return result;
            }

            return optional ? default : Activator.CreateInstance<T>();
        }
        catch (JsonException) when (optional)
        {
            return default;
        }
        finally
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Seek(0, SeekOrigin.Begin);
            }
        }
    }

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
    {
        return Results.Ok(new DynamicApiResponse<T?>
        {
            Code = StatusCodes.Status200OK,
            Message = "操作成功",
            Data = value
        });
    }

    public static IResult WrapVoidResult()
    {
        return Results.Ok(new DynamicApiResponse
        {
            Code = StatusCodes.Status200OK,
            Message = "操作成功"
        });
    }

    public static IResult WrapGetResult<T>(T? value)
    {
        if (value is null)
        {
            return Results.NotFound(new DynamicApiResponse
            {
                Code = StatusCodes.Status404NotFound,
                Message = "资源不存在"
            });
        }

        return WrapResult(value);
    }

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

    public static async Task<T?> ExecuteAsync<T>(HttpContext context, bool requiresTransaction, Func<Task<T?>> action)
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
