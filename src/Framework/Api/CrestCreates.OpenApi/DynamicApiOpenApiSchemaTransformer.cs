using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CrestCreates.OpenApi;

public sealed class DynamicApiOpenApiSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        if (type == typeof(DynamicApiResponse) ||
            (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DynamicApiResponse<>)))
        {
            schema.Description = type == typeof(DynamicApiResponse)
                ? "Standard response envelope (no data payload)"
                : $"Standard response envelope wrapping {type.GetGenericArguments()[0].Name}";
        }

        return Task.CompletedTask;
    }
}