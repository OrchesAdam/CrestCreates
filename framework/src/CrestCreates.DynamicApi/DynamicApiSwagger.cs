using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CrestCreates.DynamicApi;

public sealed class DynamicApiSwaggerDocumentFilter : IDocumentFilter
{
    private readonly DynamicApiRegistry _registry;

    public DynamicApiSwaggerDocumentFilter(DynamicApiRegistry registry)
    {
        _registry = registry;
    }

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        foreach (var service in _registry.Services)
        {
            foreach (var action in service.Actions)
            {
                var path = "/" + action.FullRoute.Trim('/');
                OpenApiPathItem pathItem;
                if (!swaggerDoc.Paths.TryGetValue(path, out var existingPathItem) || existingPathItem is not OpenApiPathItem existingOpenApiPathItem)
                {
                    pathItem = new OpenApiPathItem();
                    swaggerDoc.Paths[path] = pathItem;
                }
                else
                {
                    pathItem = existingOpenApiPathItem;
                }

                pathItem.Operations ??= new Dictionary<HttpMethod, OpenApiOperation>();
                pathItem.Operations[ToHttpMethod(action.HttpMethod)] = CreateOperation(action, swaggerDoc, context);
            }
        }
    }

    [SuppressMessage("AOT", "IL3050:Calling members annotated with \'RequiresDynamicCodeAttribute\' may break functionality when AOT compiling.")]
    private static OpenApiOperation CreateOperation(
        DynamicApiActionDescriptor action,
        OpenApiDocument swaggerDoc,
        DocumentFilterContext context)
    {
        var serviceTagName = string.IsNullOrWhiteSpace(action.DeclaringTypeName)
            ? "DynamicApi"
            : action.DeclaringTypeName;
        swaggerDoc.Tags ??= new HashSet<OpenApiTag>();
        if (!swaggerDoc.Tags.Any(tag => string.Equals(tag.Name, serviceTagName, StringComparison.Ordinal)))
        {
            swaggerDoc.Tags.Add(new OpenApiTag { Name = serviceTagName });
        }

        var operation = new OpenApiOperation
        {
            OperationId = string.IsNullOrWhiteSpace(action.OperationId)
                ? $"{serviceTagName}_{action.ActionName}"
                : action.OperationId,
            Summary = action.ActionName,
            Parameters = new List<IOpenApiParameter>(),
            Tags = new HashSet<OpenApiTagReference>
            {
                new(serviceTagName, swaggerDoc, string.Empty)
            },
            Responses = new OpenApiResponses(),
            Extensions = new Dictionary<string, IOpenApiExtension>()
        };

        foreach (var parameter in action.Parameters.Where(parameter => parameter.Source is DynamicApiParameterSource.Route or DynamicApiParameterSource.Query))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = parameter.Name,
                In = parameter.Source == DynamicApiParameterSource.Route ? ParameterLocation.Path : ParameterLocation.Query,
                Required = parameter.Source == DynamicApiParameterSource.Route || !parameter.IsOptional,
                Schema = context.SchemaGenerator.GenerateSchema(parameter.ParameterType, context.SchemaRepository)
            });
        }

        var bodyParameter = action.Parameters.FirstOrDefault(parameter => parameter.Source == DynamicApiParameterSource.Body);
        if (bodyParameter is not null)
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = !bodyParameter.IsOptional,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = context.SchemaGenerator.GenerateSchema(bodyParameter.ParameterType, context.SchemaRepository)
                    }
                }
            };
        }

        var responseType = action.ReturnDescriptor.IsVoid
            ? typeof(DynamicApiResponse)
            : typeof(DynamicApiResponse<>).MakeGenericType(action.ReturnDescriptor.PayloadType!);

        operation.Responses["200"] = new OpenApiResponse
        {
            Description = "Success",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = context.SchemaGenerator.GenerateSchema(responseType, context.SchemaRepository)
                    }
            }
        };

        operation.Responses["401"] = new OpenApiResponse { Description = "Unauthorized" };
        operation.Responses["403"] = new OpenApiResponse { Description = "Forbidden" };
        operation.Responses["404"] = new OpenApiResponse { Description = "Not Found" };

        var permissions = action.Permission?.Permissions ?? Array.Empty<string>();
        operation.Extensions["x-permissions"] = new JsonNodeExtension(
            new JsonArray(permissions
                .Select(permission => (JsonNode?)JsonValue.Create(permission))
                .ToArray()));

        return operation;
    }

    private static HttpMethod ToHttpMethod(string httpMethod)
    {
        return httpMethod.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            _ => HttpMethod.Post
        };
    }
}
