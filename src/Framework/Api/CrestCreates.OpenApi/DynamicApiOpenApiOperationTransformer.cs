using System.Text.Json.Nodes;
using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CrestCreates.OpenApi;

public sealed class DynamicApiOpenApiOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var description = context.Description;

        var permissionMetadata = description.ActionDescriptor.EndpointMetadata
            .OfType<DynamicApiPermissionMetadata>()
            .FirstOrDefault();

        if (permissionMetadata is not null)
        {
            operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();

            var permissionsArray = new JsonArray(
                permissionMetadata.Permissions
                    .Select(p => (JsonNode?)JsonValue.Create(p))
                    .ToArray());

            operation.Extensions["x-permissions"] = new JsonNodeExtension(permissionsArray);

            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("BearerAuth")] = []
            });
        }

        AddDefaultResponses(operation);

        return Task.CompletedTask;
    }

    private static void AddDefaultResponses(OpenApiOperation operation)
    {
        operation.Responses ??= new OpenApiResponses();
        if (operation.Responses.ContainsKey("401")) return;

        operation.Responses["401"] = new OpenApiResponse { Description = "Unauthorized" };
        operation.Responses["403"] = new OpenApiResponse { Description = "Forbidden" };
    }
}