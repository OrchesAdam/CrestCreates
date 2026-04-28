using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CrestCreates.DynamicApi;

public sealed class DynamicApiSwaggerGenOptionsSetup : IPostConfigureOptions<SwaggerGenOptions>
{
    public void PostConfigure(string? name, SwaggerGenOptions options)
    {
        options.CustomSchemaIds(DynamicApiSwaggerSchemaIdHelper.GetSchemaId);
        options.DocumentFilter<DynamicApiSwaggerDocumentFilter>();
    }
}