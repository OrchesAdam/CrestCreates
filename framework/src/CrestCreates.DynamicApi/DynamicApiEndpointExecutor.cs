using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using CrestCreates.Aop.Interceptors;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.Validation.Modules;
using CrestCreates.Validation.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestCreates.DynamicApi;

[Obsolete("Runtime reflection execution is no longer the Dynamic API default path. Use compile-time generated endpoints instead.")]
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class DynamicApiEndpointExecutor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public DynamicApiEndpointExecutor(IServiceProvider serviceProvider, IOptions<JsonOptions> jsonOptions)
    {
        _serviceProvider = serviceProvider;
        _jsonSerializerOptions = new JsonSerializerOptions(jsonOptions.Value.SerializerOptions)
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<IResult> ExecuteAsync(HttpContext httpContext, DynamicApiServiceDescriptor serviceDescriptor, DynamicApiActionDescriptor actionDescriptor)
    {
        await EnsurePermissionAsync(httpContext, actionDescriptor);

        var service = _serviceProvider.GetRequiredService(serviceDescriptor.ServiceType);
        var arguments = await BindArgumentsAsync(httpContext, actionDescriptor);
        var value = await InvokeAsync(service, actionDescriptor, arguments);

        return WrapResult(actionDescriptor, value);
    }

    private async Task<object?> InvokeAsync(object service, DynamicApiActionDescriptor actionDescriptor, object?[] arguments)
    {
        var unitOfWorkAttribute = ResolveUnitOfWorkAttribute(actionDescriptor);
        if (unitOfWorkAttribute is null)
        {
            return await UnwrapAsync(InvokeServiceMethod(service, actionDescriptor, arguments));
        }

        var unitOfWorkManager = _serviceProvider.GetService<IUnitOfWorkManager>();
        if (unitOfWorkManager is null)
        {
            return await UnwrapAsync(actionDescriptor.ServiceMethod.Invoke(service, arguments));
        }

        using var scope = unitOfWorkManager.BeginScope(isTransactional: unitOfWorkAttribute.RequiresTransaction);

        try
        {
            if (scope.IsOwner && scope.IsTransactional)
            {
                await scope.UnitOfWork.BeginTransactionAsync();
            }

            var result = InvokeServiceMethod(service, actionDescriptor, arguments);
            var value = await UnwrapAsync(result);

            if (scope.IsOwner && scope.IsTransactional)
            {
                await scope.UnitOfWork.CommitTransactionAsync();
            }
            else if (scope.IsOwner)
            {
                await scope.UnitOfWork.SaveChangesAsync();
            }

            return value;
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

    private async Task EnsurePermissionAsync(HttpContext httpContext, DynamicApiActionDescriptor actionDescriptor)
    {
        var permissionChecker = _serviceProvider.GetService<IPermissionChecker>();
        if (permissionChecker is null || actionDescriptor.Permission.Permissions.Length == 0)
        {
            return;
        }

        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("当前请求未认证。");
        }

        var grantResult = await permissionChecker.IsGrantedAsync(httpContext.User, actionDescriptor.Permission.Permissions);
        if (grantResult.AllProhibited)
        {
            throw new CrestPermissionException(string.Join(",", actionDescriptor.Permission.Permissions));
        }
    }

    private async Task<object?[]> BindArgumentsAsync(HttpContext httpContext, DynamicApiActionDescriptor actionDescriptor)
    {
        object? body = null;
        var arguments = new object?[actionDescriptor.Parameters.Count];

        for (var index = 0; index < actionDescriptor.Parameters.Count; index++)
        {
            var parameter = actionDescriptor.Parameters[index];
            arguments[index] = parameter.Source switch
            {
                DynamicApiParameterSource.CancellationToken => httpContext.RequestAborted,
                DynamicApiParameterSource.Route => ConvertTo(parameter.ParameterType, httpContext.Request.RouteValues[parameter.Name]),
                DynamicApiParameterSource.Query => BindFromQuery(httpContext, parameter),
                DynamicApiParameterSource.Body => body ??= await BindFromBodyAsync(httpContext, parameter),
                _ => null
            };

            await ValidateAsync(arguments[index]);
        }

        return arguments;
    }

    private object? BindFromQuery(HttpContext httpContext, DynamicApiParameterDescriptor parameter)
    {
        if (DynamicApiRouteConvention.IsScalar(parameter.ParameterType))
        {
            return ConvertTo(parameter.ParameterType, httpContext.Request.Query[parameter.Name].ToString(), parameter.IsOptional);
        }

        var instance = Activator.CreateInstance(parameter.ParameterType);
        if (instance is null)
        {
            return null;
        }

        foreach (var property in parameter.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.CanWrite))
        {
            var rawValue = httpContext.Request.Query[property.Name].ToString();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            property.SetValue(instance, ConvertTo(property.PropertyType, rawValue, optional: true));
        }

        return instance;
    }

    private async Task<object?> BindFromBodyAsync(HttpContext httpContext, DynamicApiParameterDescriptor parameter)
    {
        if (httpContext.Request.ContentLength == 0)
        {
            return parameter.IsOptional ? null : Activator.CreateInstance(parameter.ParameterType);
        }

        httpContext.Request.EnableBuffering();
        if (httpContext.Request.Body.CanSeek)
        {
            httpContext.Request.Body.Seek(0, SeekOrigin.Begin);
        }

        var result = await JsonSerializer.DeserializeAsync(
            httpContext.Request.Body,
            parameter.ParameterType,
            _jsonSerializerOptions,
            httpContext.RequestAborted);

        if (httpContext.Request.Body.CanSeek)
        {
            httpContext.Request.Body.Seek(0, SeekOrigin.Begin);
        }

        return result;
    }

    private static UnitOfWorkMoAttribute? ResolveUnitOfWorkAttribute(DynamicApiActionDescriptor actionDescriptor)
    {
        return actionDescriptor.ImplementationMethod.GetCustomAttribute<UnitOfWorkMoAttribute>(inherit: true)
               ?? actionDescriptor.ServiceMethod.GetCustomAttribute<UnitOfWorkMoAttribute>(inherit: true)
               ?? actionDescriptor.ImplementationMethod.DeclaringType?.GetCustomAttribute<UnitOfWorkMoAttribute>(inherit: true)
               ?? actionDescriptor.ServiceMethod.DeclaringType?.GetCustomAttribute<UnitOfWorkMoAttribute>(inherit: true);
    }

    private static object? InvokeServiceMethod(object service, DynamicApiActionDescriptor actionDescriptor, object?[] arguments)
    {
        try
        {
            return actionDescriptor.ServiceMethod.Invoke(service, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            return null;
        }
    }

    private async Task ValidateAsync(object? instance)
    {
        if (instance is null || DynamicApiRouteConvention.IsScalar(instance.GetType()))
        {
            return;
        }

        var validationService = _serviceProvider.GetService<IValidationService>();
        if (validationService is null)
        {
            return;
        }

        var methodInfo = typeof(IValidationService)
            .GetMethod(nameof(IValidationService.ValidateAsync))!
            .MakeGenericMethod(instance.GetType());

        var validationTask = (Task<ValidationResult>)methodInfo.Invoke(validationService, new[] { instance })!;
        var result = await validationTask;
        if (!result.IsValid)
        {
            throw new ArgumentException(string.Join("; ", result.Errors));
        }
    }

    private static async Task<object?> UnwrapAsync(object? invocationResult)
    {
        if (invocationResult is null)
        {
            return null;
        }

        if (invocationResult is Task task)
        {
            await task;
            var taskType = task.GetType();
            if (!taskType.IsGenericType)
            {
                return null;
            }

            return taskType.GetProperty("Result")?.GetValue(task);
        }

        return invocationResult;
    }

    private static IResult WrapResult(DynamicApiActionDescriptor actionDescriptor, object? value)
    {
        if (!actionDescriptor.ReturnDescriptor.IsVoid && value is null && HttpMethods.IsGet(actionDescriptor.HttpMethod))
        {
            return Results.NotFound(new DynamicApiResponse
            {
                Code = StatusCodes.Status404NotFound,
                Message = "资源不存在"
            });
        }

        if (actionDescriptor.ReturnDescriptor.IsVoid)
        {
            return Results.Ok(new DynamicApiResponse());
        }

        var responseType = typeof(DynamicApiResponse<>).MakeGenericType(actionDescriptor.ReturnDescriptor.PayloadType!);
        var response = Activator.CreateInstance(responseType)!;
        responseType.GetProperty(nameof(DynamicApiResponse<object>.Code))!.SetValue(response, StatusCodes.Status200OK);
        responseType.GetProperty(nameof(DynamicApiResponse<object>.Message))!.SetValue(response, "操作成功");
        responseType.GetProperty(nameof(DynamicApiResponse<object>.Data))!.SetValue(response, value);

        return Results.Ok(response);
    }

    private static object? ConvertTo(Type targetType, object? rawValue, bool optional = false)
    {
        if (rawValue is null)
        {
            return optional ? GetDefault(targetType) : null;
        }

        var stringValue = rawValue.ToString();
        if (string.IsNullOrWhiteSpace(stringValue))
        {
            return optional ? GetDefault(targetType) : null;
        }

        var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (actualType == typeof(string))
        {
            return stringValue;
        }

        if (actualType == typeof(Guid))
        {
            return Guid.Parse(stringValue);
        }

        if (actualType.IsEnum)
        {
            return Enum.Parse(actualType, stringValue, ignoreCase: true);
        }

        var converter = TypeDescriptor.GetConverter(actualType);
        return converter.ConvertFromInvariantString(stringValue);
    }

    private static object? GetDefault(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
