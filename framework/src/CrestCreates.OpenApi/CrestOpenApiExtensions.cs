using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
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
                }
            });
        }

        return endpoints;
    }
}