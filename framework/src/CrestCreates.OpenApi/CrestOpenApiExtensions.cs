using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;

namespace CrestCreates.OpenApi;

public static class CrestOpenApiExtensions
{
    public static IServiceCollection AddCrestOpenApi(
        this IServiceCollection services,
        Action<CrestOpenApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new CrestOpenApiOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        services.AddOpenApi(options.DocumentVersion, openApiOptions =>
        {
            openApiOptions.AddDocumentTransformer<DynamicApiOpenApiDocumentTransformer>();
            openApiOptions.AddOperationTransformer<DynamicApiOpenApiOperationTransformer>();
            openApiOptions.AddSchemaTransformer<DynamicApiOpenApiSchemaTransformer>();
        });

        return services;
    }

    public static IServiceCollection AddCrestOpenApi(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<CrestOpenApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new CrestOpenApiOptions();
        configuration.Bind(options);
        configure?.Invoke(options);

        services.AddSingleton(options);

        services.AddOpenApi(options.DocumentVersion, openApiOptions =>
        {
            openApiOptions.AddDocumentTransformer<DynamicApiOpenApiDocumentTransformer>();
            openApiOptions.AddOperationTransformer<DynamicApiOpenApiOperationTransformer>();
            openApiOptions.AddSchemaTransformer<DynamicApiOpenApiSchemaTransformer>();
        });

        return services;
    }

    public static IEndpointRouteBuilder MapCrestOpenApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<CrestOpenApiOptions>();

        if (options.EnableOpenApiDocument)
        {
            endpoints.MapOpenApi();
        }

        if (options.EnableUi)
        {
            endpoints.MapScalarApiReference(scalarOptions =>
            {
                scalarOptions.WithTitle(options.DocumentTitle);

                if (options.EnableAuthentication)
                {
                    scalarOptions.Authentication ??= new ScalarAuthenticationOptions();
                    scalarOptions.Authentication.PreferredSecuritySchemes = ["BearerAuth"];

                    // Pre-fill Bearer token from configuration (Development only)
                    if (!string.IsNullOrWhiteSpace(options.DefaultBearerToken))
                    {
                        scalarOptions.Authentication.SecuritySchemes ??= new Dictionary<string, ScalarSecurityScheme>();
                        scalarOptions.Authentication.SecuritySchemes["BearerAuth"] = new ScalarHttpSecurityScheme
                        {
                            Token = options.DefaultBearerToken
                        };
                    }
                }

                // Pre-fill X-Tenant-Id from configuration (Development only)
                if (options.EnableTenantHeader && !string.IsNullOrWhiteSpace(options.DefaultTenantId))
                {
                    scalarOptions.Authentication ??= new ScalarAuthenticationOptions();
                    scalarOptions.Authentication.SecuritySchemes ??= new Dictionary<string, ScalarSecurityScheme>();
                    scalarOptions.Authentication.SecuritySchemes["TenantHeader"] = new ScalarApiKeySecurityScheme
                    {
                        Value = options.DefaultTenantId
                    };
                }
            });
        }

        return endpoints;
    }
}
