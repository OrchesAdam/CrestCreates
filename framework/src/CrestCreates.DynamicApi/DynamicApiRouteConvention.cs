using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

public sealed class DynamicApiRouteConvention
{
    public string ResolveServiceRoute(Type serviceType, string serviceName, DynamicApiOptions options)
    {
        var attributeRoute = serviceType.GetCustomAttribute<DynamicApiRouteAttribute>()?.Template;
        if (!string.IsNullOrWhiteSpace(attributeRoute))
        {
            return attributeRoute!.Trim('/');
        }

        return $"{options.DefaultRoutePrefix.TrimEnd('/')}/{ToKebabCase(serviceName)}";
    }

    public string ResolveHttpMethod(MethodInfo methodInfo)
    {
        var methodName = TrimAsyncSuffix(methodInfo.Name);

        if (methodName is "Create" or "Add" or "Insert" || methodName.StartsWith("Create", StringComparison.Ordinal))
        {
            return HttpMethods.Post;
        }

        if (methodName is "Update" or "Put" || methodName.StartsWith("Update", StringComparison.Ordinal))
        {
            return HttpMethods.Put;
        }

        if (methodName is "Delete" or "Remove" || methodName.StartsWith("Delete", StringComparison.Ordinal))
        {
            return HttpMethods.Delete;
        }

        if (methodName.StartsWith("Process", StringComparison.Ordinal) || methodName.StartsWith("Return", StringComparison.Ordinal) || methodName.StartsWith("Extend", StringComparison.Ordinal))
        {
            return HttpMethods.Post;
        }

        if (methodName == "Query" || methodName == "Search")
        {
            return HttpMethods.Post;
        }

        return HttpMethods.Get;
    }

    public string ResolveActionRoute(MethodInfo methodInfo)
    {
        var methodName = TrimAsyncSuffix(methodInfo.Name);
        var parameters = methodInfo.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .ToArray();

        return methodName switch
        {
            "Create" => string.Empty,
            "GetById" => "{id}",
            "Get" when parameters.Length == 1 && IsScalar(parameters[0].ParameterType) => $"{{{parameters[0].Name}}}",
            "GetList" => string.Empty,
            "Update" => "{id}",
            "Delete" => "{id}",
            "GetAll" => "all",
            "Count" => "count",
            "Query" => "query",
            _ when methodName.StartsWith("GetBy", StringComparison.Ordinal) && parameters.Length == 1
                => $"by-{ToKebabCase(methodName["GetBy".Length..])}/{{{parameters[0].Name}}}",
            _ when methodName.StartsWith("Get", StringComparison.Ordinal) && parameters.Length == 0
                => ToKebabCase(methodName["Get".Length..]),
            _ when methodName.StartsWith("Get", StringComparison.Ordinal) && parameters.Length == 1 && IsScalar(parameters[0].ParameterType)
                => $"{ToKebabCase(methodName["Get".Length..])}/{{{parameters[0].Name}}}",
            _ when methodName.StartsWith("Exists", StringComparison.Ordinal) && parameters.Length == 1
                => $"{ToKebabCase(methodName)}/{{{parameters[0].Name}}}",
            _ => ToKebabCase(methodName)
        };
    }

    public IReadOnlyList<DynamicApiParameterDescriptor> ResolveParameters(MethodInfo methodInfo, string routePattern, string httpMethod)
    {
        var bodyAssigned = false;
        var routeTokens = routePattern.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment.StartsWith('{') && segment.EndsWith('}'))
            .Select(segment => segment[1..^1])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var parameters = new List<DynamicApiParameterDescriptor>();
        foreach (var parameter in methodInfo.GetParameters())
        {
            var source = ResolveSource(parameter, routeTokens, httpMethod, ref bodyAssigned);
            parameters.Add(new DynamicApiParameterDescriptor
            {
                Name = parameter.Name ?? "value",
                ParameterInfo = parameter,
                ParameterType = parameter.ParameterType,
                Source = source,
                IsOptional = parameter.IsOptional
            });
        }

        return parameters;
    }

    public DynamicApiPermissionMetadata ResolvePermission(string serviceName, MethodInfo methodInfo)
    {
        var methodName = TrimAsyncSuffix(methodInfo.Name);
        var permission = methodName switch
        {
            "Create" => $"{serviceName}.Create",
            "Update" => $"{serviceName}.Update",
            "Delete" => $"{serviceName}.Delete",
            "GetById" or "Get" => $"{serviceName}.Get",
            _ when methodName.StartsWith("GetBy", StringComparison.Ordinal) => $"{serviceName}.Get",
            _ => $"{serviceName}.Search"
        };

        return new DynamicApiPermissionMetadata
        {
            Permissions = new[] { permission },
            RequireAll = false
        };
    }

    public static DynamicApiReturnDescriptor ResolveReturnDescriptor(MethodInfo methodInfo)
    {
        var declaredType = methodInfo.ReturnType;
        if (declaredType == typeof(Task))
        {
            return new DynamicApiReturnDescriptor
            {
                DeclaredType = declaredType,
                PayloadType = null,
                IsVoid = true
            };
        }

        if (declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return new DynamicApiReturnDescriptor
            {
                DeclaredType = declaredType,
                PayloadType = declaredType.GetGenericArguments()[0],
                IsVoid = false
            };
        }

        return new DynamicApiReturnDescriptor
        {
            DeclaredType = declaredType,
            PayloadType = declaredType == typeof(void) ? null : declaredType,
            IsVoid = declaredType == typeof(void)
        };
    }

    private static DynamicApiParameterSource ResolveSource(
        ParameterInfo parameter,
        ISet<string> routeTokens,
        string httpMethod,
        ref bool bodyAssigned)
    {
        if (parameter.ParameterType == typeof(CancellationToken))
        {
            return DynamicApiParameterSource.CancellationToken;
        }

        if (routeTokens.Contains(parameter.Name ?? string.Empty))
        {
            return DynamicApiParameterSource.Route;
        }

        if (!bodyAssigned && (HttpMethods.IsPost(httpMethod) || HttpMethods.IsPut(httpMethod) || HttpMethods.IsPatch(httpMethod)))
        {
            if (!IsScalar(parameter.ParameterType))
            {
                bodyAssigned = true;
                return DynamicApiParameterSource.Body;
            }
        }

        return DynamicApiParameterSource.Query;
    }

    internal static bool IsScalar(Type type)
    {
        var targetType = Nullable.GetUnderlyingType(type) ?? type;
        return targetType.IsPrimitive
            || targetType.IsEnum
            || targetType == typeof(string)
            || targetType == typeof(Guid)
            || targetType == typeof(DateTime)
            || targetType == typeof(DateTimeOffset)
            || targetType == typeof(decimal)
            || targetType == typeof(TimeSpan);
    }

    internal static string TrimAsyncSuffix(string methodName)
    {
        return methodName.EndsWith("Async", StringComparison.Ordinal)
            ? methodName[..^"Async".Length]
            : methodName;
    }

    internal static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character))
            {
                if (index > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
