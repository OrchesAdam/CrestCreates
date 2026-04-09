using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

public enum DynamicApiParameterSource
{
    Route = 1,
    Query = 2,
    Body = 3,
    CancellationToken = 4
}

public sealed class DynamicApiServiceDescriptor
{
    public string ServiceName { get; init; } = string.Empty;

    public string RoutePrefix { get; init; } = string.Empty;

    public Type ServiceType { get; init; } = null!;

    public Type ImplementationType { get; init; } = null!;

    public IReadOnlyList<DynamicApiActionDescriptor> Actions { get; init; } = Array.Empty<DynamicApiActionDescriptor>();
}

public sealed class DynamicApiActionDescriptor
{
    public string ActionName { get; init; } = string.Empty;

    public string DeclaringTypeName { get; init; } = string.Empty;

    public string OperationId { get; init; } = string.Empty;

    public string RelativeRoute { get; init; } = string.Empty;

    public string HttpMethod { get; init; } = HttpMethods.Get;

    public MethodInfo? ServiceMethod { get; init; }

    public MethodInfo? ImplementationMethod { get; init; }

    public DynamicApiReturnDescriptor ReturnDescriptor { get; init; } = null!;

    public IReadOnlyList<DynamicApiParameterDescriptor> Parameters { get; init; } = Array.Empty<DynamicApiParameterDescriptor>();

    public DynamicApiPermissionMetadata Permission { get; init; } = null!;

    public string FullRoute => string.IsNullOrWhiteSpace(RelativeRoute)
        ? RoutePrefix
        : $"{RoutePrefix}/{RelativeRoute}";

    public string RoutePrefix { get; init; } = string.Empty;
}

public sealed class DynamicApiParameterDescriptor
{
    public string Name { get; init; } = string.Empty;

    public ParameterInfo? ParameterInfo { get; init; }

    public Type ParameterType { get; init; } = null!;

    public DynamicApiParameterSource Source { get; init; }

    public bool IsOptional { get; init; }
}

public sealed class DynamicApiReturnDescriptor
{
    public Type DeclaredType { get; init; } = null!;

    public Type? PayloadType { get; init; }

    public bool IsVoid { get; init; }
}

public sealed class DynamicApiPermissionMetadata
{
    public string[] Permissions { get; init; } = Array.Empty<string>();

    public bool RequireAll { get; init; }
}

public sealed class DynamicApiRegistry
{
    public DynamicApiRegistry(IReadOnlyList<DynamicApiServiceDescriptor> services)
    {
        Services = services;
    }

    public IReadOnlyList<DynamicApiServiceDescriptor> Services { get; }
}

public class DynamicApiResponse
{
    public int Code { get; set; } = StatusCodes.Status200OK;

    public string Message { get; set; } = "操作成功";
}

public sealed class DynamicApiResponse<T> : DynamicApiResponse
{
    public T? Data { get; set; }
}
