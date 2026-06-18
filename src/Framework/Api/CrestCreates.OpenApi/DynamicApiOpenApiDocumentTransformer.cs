using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CrestCreates.OpenApi;

public sealed class DynamicApiOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    private readonly CrestOpenApiOptions _options;

    public DynamicApiOpenApiDocumentTransformer(CrestOpenApiOptions options)
    {
        _options = options;
    }

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info.Title = _options.DocumentTitle;
        document.Info.Version = _options.DocumentVersion;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        if (_options.EnableAuthentication)
        {
            document.Components.SecuritySchemes["BearerAuth"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "JWT Authorization header using the Bearer scheme"
            };
        }

        if (_options.EnableTenantHeader)
        {
            document.Components.SecuritySchemes["TenantHeader"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Tenant-Id",
                Description = "Tenant identifier for multi-tenant requests"
            };
        }

        return Task.CompletedTask;
    }
}